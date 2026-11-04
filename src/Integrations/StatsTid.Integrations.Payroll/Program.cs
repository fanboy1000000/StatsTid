using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Resilience;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.SharedKernel.Segmentation;

var builder = WebApplication.CreateBuilder(args);

// QUAL-008 render fold-in — render the ambient CorrelationId log scope in the default console sink.
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));

// ── Outbox: dual-binding per ADR-018 D3 + per-service publisher per D2/D6 ──
// Payroll owns period-calculation-* + payroll-export-* + retroactive correction
// streams per ADR-018 D6 stream-ownership table. Since S146 TASK-14604 (R-21) the
// manifest's streams travel through this outbox too: segment-manifest-{manifestId}
// and the legacy period-calc-{employeeId}-{periodStart:yyyy-MM-dd}, enqueued by
// PostgresSegmentManifestStore in one transaction with the segment_manifests row.
builder.Services.AddSingleton(new OutboxServiceContext("payroll"));
builder.Services.AddSingleton<PostgresEventStore>(sp => new PostgresEventStore(
    sp.GetRequiredService<DbConnectionFactory>(),
    sp.GetRequiredService<OutboxServiceContext>()));
builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddHostedService<OutboxPublisher>();

builder.Services.AddHttpClient();
// S29 TASK-2907: WageTypeMappingRepository is now an optional dependency of
// PayrollMappingService for the dated-read path (ADR-020 D1 / Implications §7): the planner-driven
// per-segment export in PCS.MapSegmentToExportLinesAsync passes asOfDate = segmentStart. (The
// per-line mapper that used the current-row lookup served only the raw export routes and was deleted
// with them in S147 TASK-14716.)
builder.Services.AddSingleton<WageTypeMappingRepository>();
// S69 TASK-6904 + S71 TASK-7105 (ADR-033 D4; SPRINT-71 R6/R7/R9): the settlement-export
// consumer + its data-access seam. SettlementExportEmitter is ONE BackgroundService with a
// type-discriminated poll over the canonical events table consuming VacationAutoPaidOut (§24,
// reversal-aware since S71), TerminationPayoutRequested (§26 line + the consumer-authoritative
// request OPEN→LINE_STAGED promotion) and SettlementReversed (compensating REVERSAL lines per
// staged bucket, atomic multi-bucket completion at the (source_event_id, bucket) composite inbox
// key). Durable, money-free lines + inbox checkpoints EXACTLY ONCE under the ADR-032 D4 employee
// advisory lock (the SAME lock the Backend close/reversal services + the reconcile endpoint take
// — mutual exclusion, R12). Naturally dormant pre-launch (the D13-gated close emits no events
// yet; §26/reversal events exist only via the HROrAbove operator endpoints). It depends on
// SettlementInboxLineRepository (the inbox/line/events-poll repository) + WageTypeMappingRepository
// (the snapshot-keyed dated lønart lookups, registered above) + a logger — all in this container.
// S71 adds NO new registration (the three consumers ride this one hosted service).
builder.Services.AddSingleton<SettlementInboxLineRepository>();
builder.Services.AddHostedService<SettlementExportEmitter>();
// S33 TASK-3301 (ADR-023): IEmploymentProfileResolver provides the dated lookup
// that PCS consumes per-segment for replay-deterministic employment-profile
// hydration (ADR-016 D5b consumption-time-lookup). Same dated-vs-live split
// pattern as WageTypeMappingRepository's ADR-018 D14 export-time effective-date
// lookup — adjacent registration mirrors the consumption shape.
// EmploymentProfileResolver ctor-requires UserAgreementCodeRepository for the dated
// agreement-code layer (ADR-023 D2) — registered in the Backend host but missing here
// until the S134 PayrollCalcAuditSmokeTests composed-stack probe 500'd on first
// resolver activation (run 32841900233).
builder.Services.AddSingleton<UserAgreementCodeRepository>();
builder.Services.AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>();
// S137 TASK-13702 (ADR-040 D5/D7): the employment-window read PCS consumes when it plans a
// calculation — BuildPlanWithHistoriesAsync asks "employed when?" via GetWindowsAsync and
// turns the answer into EmploymentStarted/EmploymentEnded boundaries + EMPLOYED/NOT_EMPLOYED
// segment typing. Read server-side inside THIS host (D7): the EmploymentProfile wire DTO never
// carries employment dates. Registered next to IEmploymentProfileResolver because the two are
// consulted in D10 order (employed? FIRST, then which profile?) at the same planning seam.
// S146 TASK-14605 (R-36): the Infrastructure in-transaction surface IEmploymentWindowResolverInTx is
// registered too, from the SAME singleton (the Backend host's pattern), so the two reads cannot disagree on
// semantics. Planning still reads outside any lock (self-managed); but this host now has writing transactions
// that need a window read inside them: the flex ledger's under-lock recheck re-derives a month's opening
// balance after the per-employee ledger lock, reading the windows over [the previous month's first day, this
// month's last day] on the caller's (conn, tx), so it sees what a concurrent write committed. The earlier
// "no write transaction for a window read to ride" rationale ended with that recheck.
builder.Services.AddSingleton<EmploymentWindowResolver>();
builder.Services.AddSingleton<IEmploymentWindowResolver>(sp => sp.GetRequiredService<EmploymentWindowResolver>());
builder.Services.AddSingleton<IEmploymentWindowResolverInTx>(sp => sp.GetRequiredService<EmploymentWindowResolver>());
// S137 TASK-13702 (ADR-040 D5): the employee_profiles history read (GetEffectiveFromDatesAsync)
// that feeds the now-ACTIVE EmployeeProfileChange boundary — a mid-period part-time-fraction
// or position change splits the calculation so each span resolves its own dated profile.
// Optional dependency of PeriodCalculationService (null in direct-construction fixtures →
// no profile-change boundaries, pre-S137 behavior).
builder.Services.AddSingleton<EmployeeProfileRepository>();
builder.Services.AddSingleton<PayrollMappingService>();
builder.Services.AddSingleton<PayrollExportService>();
// S90 / TASK-9004 (ADR-034): the corrections seam over payroll_export_records — reads
// current_effective_lines as the /recalculate diff baseline (B3) and rewrites it in the
// correction tx so a 2nd correction diffs against the 1st correction's result.
builder.Services.AddSingleton<PayrollExportRecordRepository>();
// S146 TASK-14605 (QUAL-188; R-14): the flex ledger's one read — the employee's export records with their
// balance columns — which PeriodCalculationService's opening derivation consumes (self-managed at admission,
// in-transaction for the under-lock recheck). Optional in PCS's constructor but never silent: without this
// registration every calculation of a month holding an EMPLOYED day throws, naming this line.
builder.Services.AddSingleton<IFlexLedgerReader, PostgresFlexLedgerReader>();
// S145 TASK-14508 (rulings R14, R25, R58): payroll reads the employee's recorded hours itself —
// the calculating routes no longer take them from the request. This registers IRecordedHoursReader
// (PeriodCalculationService's optional-but-never-silent hours source: without it every calculation
// throws, naming this line) and the two read-model repositories behind it. The registration lives in
// Services/ProjectionHoursReader.cs, the ONLY Integrations file allowed to name those repositories,
// so this file — which also hosts the endpoints — never does (R58; TASK-14514's hours-repository
// access guard keeps its allowlist to that one file).
builder.Services.AddProjectionHoursReader();
// S146 TASK-14604 (QUAL-205; rulings R-21, R-22): where a calculation's proof is written and read back.
// PostgresSegmentManifestStore writes the manifest event, its segment_manifests row and the legacy
// PeriodCalculationCompleted in ONE transaction through the outbox registered above (IOutboxEnqueue), and
// loads a manifest for a replay (projection first, then the event store, IEventStore read only).
// PeriodCalculationService takes it in place of the IEventStore + DbConnectionFactory pair it used only for
// the manifest.
builder.Services.AddSingleton<ISegmentManifestStore, PostgresSegmentManifestStore>();
builder.Services.AddSingleton<PeriodCalculationService>();
builder.Services.AddSingleton<RetroactiveCorrectionService>();
// S45: cross-process audit projection for RetroactiveCorrectionRequested
builder.Services.AddSingleton<AuditProjectionRepository>();
builder.Services.AddSingleton<StatsTid.SharedKernel.Audit.IAuditProjectionMapper<StatsTid.SharedKernel.Events.RetroactiveCorrectionRequested>, StatsTid.Infrastructure.AuditMappers.RetroactiveCorrectionRequestedAuditMapper>();
// S90 (ADR-034 / TASK-9001): cross-process audit projection for PayrollExportGenerated
// — the per-(employee, year, month) payroll-export lock fact, emitted from this
// process (TASK-9002). Mirrors the RetroactiveCorrectionRequested registration above.
builder.Services.AddSingleton<StatsTid.SharedKernel.Audit.IAuditProjectionMapper<StatsTid.SharedKernel.Events.PayrollExportGenerated>, StatsTid.Infrastructure.AuditMappers.PayrollExportGeneratedAuditMapper>();
// S134 TASK-13404 (QUAL-003): the audit_log writer. Before this, the Payroll host
// registered ONLY AuditProjectionRepository (the audit_projection cross-process table),
// so no request through this process ever produced an audit_log row and the ADR-016 D10
// segment_manifests⋈audit_log manifest linkage (persisted via audit_log.details) was
// never created. AuditLogRepository is the SAME singleton the Backend host registers
// (Backend Program.cs:100); it backs the AuditLoggingMiddleware wired into the pipeline
// below (the middleware method-injects this repository per request). Additive: no other
// registration or behaviour changes.
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<ApprovalPeriodRepository>();
// S145 TASK-14512 (rulings R5(b), R16, R21 as amended by R26): months in order. A month whose first
// Monday–Sunday week holds EMPLOYED days of the previous month is calculated only once that month is
// APPROVED; PeriodCalculationService asks through this payroll-local reader, whose production adapter
// wraps the ApprovalPeriodRepository registered above. Optional in PCS's constructor but never silent:
// without this registration every such calculation throws, naming it (the host 409 facts pin that,
// mutation MD-21).
builder.Services.AddSingleton<IPriorPeriodApprovalReader, ApprovalPeriodPriorPeriodReader>();
builder.Services.AddSingleton<LocalConfigurationRepository>();
// S21 TASK-2108 (ADR-017 D9c): hydrates BoundarySources.LocalProfileActivations on
// the legacy [Obsolete] CalculateAsync(profile, …) shim path. Optional dependency on
// PeriodCalculationService — when the repository is unregistered (test fixtures), the
// shim emits no profile-activation boundaries (D9c profile-less callers contract).
builder.Services.AddSingleton<LocalAgreementProfileRepository>();
builder.Services.AddSingleton<ConfigResolutionService>();
builder.Services.AddSingleton<IdempotencyGuard>();

// JWT minting service (TASK-2010): used by HttpRuleClassificationProvider to mint
// service-to-service tokens for the GET /api/rules/classifications fetch. JwtSettings
// is registered transitively by AddStatsTidJwtAuth below; the singleton constructor
// resolves it from DI at first request.
builder.Services.AddSingleton<JwtTokenService>();

// HTTP-backed rule classification provider (TASK-2010 / S20). Replaces the implicit
// EmptyRuleClassificationProvider fallback inside PeriodCalculationService — once this
// registration is in place, D9 rule-side invariants fire on every calculation against
// the live RuleRegistry contents fetched from /api/rules/classifications.
//
// EmptyRuleClassificationProvider (defined in PeriodCalculationService.cs) is retained for
// TESTS ONLY — the constructor fallback when no provider is registered. It is never a stand-in
// for an unreachable Rule Engine: since S144 (TASK-14412, ruling B1) this provider throws
// RuleClassificationsUnavailableException when it cannot fetch the set, and every route that
// plans answers that with a 503 (RulesUnavailable below) — never a silent empty set: payroll plans
// and merges by the strategies its rules declare, so an unknown set read as an empty one would plan
// and merge as though no rule declared anything. (Before S145 B2 the reason was that the planner
// would read it as "no rule forbids a split"; payroll now hands the planner only the rules it
// evaluates, none of which refuses one — R-91.)
builder.Services.AddHttpClient<IRuleClassificationProvider, HttpRuleClassificationProvider>(client =>
{
    var ruleEngineUrl = builder.Configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
    client.BaseAddress = new Uri(ruleEngineUrl);
});

// Resource-scope enforcement for /api/payroll/calculate-and-export (TASK-1902).
// LocalAdmin role alone is not sufficient — the caller's scope must also cover
// the target employee's org. Reuses the same OrgScopeValidator chain the Backend
// uses for its own scope enforcement.
builder.Services.AddSingleton<OrganizationRepository>();
builder.Services.AddSingleton<UserRepository>();
// S147 / TASK-14703 (BC-6, R-30): the validator now takes the server clock (its departed-self clause
// compares the target's last day of employment with the Copenhagen business day), so this host must
// register one or the validator cannot be built and the host does not start. TimeProvider.System is the
// production clock, as in the Backend host; tests override it with a fixed provider (PAT-008). The
// /recalculate departed-self guard reads the same registration. Every other service in this container
// whose constructor takes an OPTIONAL TimeProvider already fell back to TimeProvider.System, so they
// now receive the same clock from the container and behave identically.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OrgScopeValidator>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
// QUAL-009 / SEC-038 (S134 TASK-13403) — the denial audit-ROW writer, BEFORE UseAuthorization: an
// authorization denial short-circuits the pipeline, so only a before-authz middleware survives to
// record it. Payroll's admin/mutating endpoints (GlobalAdminOnly / LocalAdminOrAbove) are exactly the
// ADMIN-STRICT/MUTATING routes the owner ruling (OQ-1a) wants a row for. Denial-gated (no double-write
// with the allowed-request row that AuditLoggingMiddleware writes below) + best-effort. It
// method-injects AuditLogRepository (registered above by TASK-13404).
app.UseMiddleware<PolicyDenialAuditRowMiddleware>();
app.UseAuthorization();
// S134 TASK-13404 (QUAL-003): audit middleware, registered in the SAME pipeline position as
// the Backend host (Backend Program.cs:487 — after auth/authz so GetActorContext() is populated).
// It runs POST-_next in a swallowing try/catch (an audit-write failure never touches the export
// transaction or the response) and writes one audit_log row per non-/health request. The
// /calculate-and-export endpoint additionally stamps the calc's manifest id into HttpContext.Items
// (PeriodCalculationService.StampAuditContext) BEFORE this middleware persists, so that row's
// audit_log.details carries {"manifest_id":"<guid>"} — creating the ADR-016 D10 linkage.
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payroll-integration" }));

// S147 TASK-14716 (SEC-054 step 2; owner OQ-3 = (a)): the raw POST /api/payroll/export and /api/payroll/export-period
// routes are retired, with what only they used (their plan-only guard, their export-context builder and their
// request types). They took pay lines the caller had written, with no approved month behind them and no second
// person, so they were a single-person path to payroll. Every export now goes through /calculate-and-export below:
// an APPROVED month, re-asserted under a row lock with its four-eyes checkpoint inside the export transaction
// (PayrollExportService.ExportAsync requires the period). A request to either retired path answers 404.

// S146 Step 7a (R-56): the log category of the flex-ledger refusal line (FlexLedgerRefused), taken by the two
// calculating handlers, /calculate-and-export and /recalculate.
const string FlexLedgerLogCategory = "StatsTid.Integrations.Payroll.FlexLedger";

app.MapPost("/api/payroll/calculate-and-export", async (
    CalculateAndExportRequest request,
    PeriodCalculationService calculator,
    PayrollExportService export,
    ApprovalPeriodRepository approvalRepo,
    OrgScopeValidator scopeValidator,
    // S147 / TASK-14713 (QUAL-230 T5b; R-15) — the employed-month bound for a switched-off subject:
    // the terminated-inclusive subject read and the window read.
    UserRepository userRepo,
    IEmploymentWindowResolver windowResolver,
    ILoggerFactory loggerFactory,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // S146 Step 7a (R-56): a flex-ledger refusal is logged where it is answered (FlexLedgerRefused).
    var flexLedgerLogger = loggerFactory.CreateLogger(FlexLedgerLogCategory);

    // Resource-scope guard (TASK-1902): the caller's scope must cover the target
    // employee's org. Pre-fix, LocalAdminOrAbove + the APPROVED-period match below
    // were treated as equivalent to per-org scoping, but the approval guard only
    // checks (employee_id, period) — a LocalAdmin from org A could trigger payroll
    // for any employee in org B whose period happens to be APPROVED. This check
    // closes that vector before the approval lookup or any downstream call.
    // S145 Step 7a W-1 (R-129): role-FLOORED at LocalAdmin, the route's policy floor
    // (SECURITY.md, "The mixed-role scope-floor invariant (S76 / B1)"): a scope only admits
    // the target if it also clears LocalAdmin, so a LocalHR scope over the target's org
    // no longer admits an actor who is LocalAdmin elsewhere.
    // S147 / TASK-14713 (QUAL-230 T5b; owner OQ-2) — the TERMINATED-INCLUSIVE validator, same
    // LocalAdmin floor (an R9c entry). From day 6 after a leaver's last working day the leaver job
    // switches them off; the active-only call then answered "Target employee not found" to every
    // caller, so the leaver's final month could not be paid. For an ACTIVE employee this call is
    // behaviour-identical under the floor. For a switched-off employee it admits only through a scope
    // that is itself LocalHR or above AND clears the LocalAdmin floor — so a LocalAdmin-or-above in
    // scope, never a leader scope. TASK-14703's departed-self clause runs inside it, as before.
    var actor = httpContext.GetActorContext();
    var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
        actor, request.Profile.EmployeeId, StatsTid.SharedKernel.Security.StatsTidRoles.LocalAdmin, ct);
    if (!allowed)
    {
        return Results.Json(new
        {
            error = "Access denied",
            reason,
            employeeId = request.Profile.EmployeeId
        }, statusCode: 403);
    }

    // S145 TASK-14508 (R14; placement ruled N1): hours in the body are refused, loudly. Payroll reads
    // the employee's recorded hours itself, so a non-empty Entries/Absences list is never used — and
    // silently ignoring it is exactly the dropped-hours defect class S145 removes. It sits AFTER the
    // org-scope 403 (an out-of-scope caller learns nothing about the body) and BEFORE the approval read
    // below, which is itself a database read: nothing further is read, calculated or written.
    if (RefuseBodyHours(request.Entries, request.Absences) is { } bodyHoursRefusal)
        return bodyHoursRefusal;

    // S146 TASK-14605 (QUAL-188; R-14, AC-C2): an opening flex balance in the body is refused, loudly, beside the
    // hours refusal and for the same reason — the calculation derives the opening from the recorded ledger, so a
    // caller-supplied one would be silently ignored. Same placement (ruling N1): after the org-scope 403, before
    // the approval read and every read the calculation makes; nothing is calculated, written or exported.
    if (RefuseBodyOpeningBalance(request.PreviousFlexBalance) is { } openingRefusal)
        return openingRefusal;

    // S146 R-47 (Codex 5a BLOCKER): the period must be one whole calendar month. Placed after the body refusals and
    // before the approval read, so nothing is read for a malformed period (a legacy WEEKLY APPROVED row answers 400).
    if (RefusePeriodNotAMonth(request.PeriodStart, request.PeriodEnd) is { } periodRefusal)
        return periodRefusal;

    // S147 / TASK-14713 (QUAL-230 T5b; R-15) — the employed-month bound, after the scope check. A
    // switched-off subject is paid only for a month that holds an employed day; any other month answers
    // ADR-040 D3's date-free 422. It sits after the three request-shape refusals above (which read
    // nothing, by their own ruling N1, and which guarantee a whole, well-ordered month for the window
    // read) and before the approval read. An actor the scope check refused got its 403 for every month.
    if (await RefuseMonthWithoutEmployedDayAsync(
            userRepo, windowResolver, loggerFactory, actor,
            request.Profile.EmployeeId, request.PeriodStart, request.PeriodEnd, ct) is { } employedMonthRefusal)
        return employedMonthRefusal;

    // Approval guard: period must be APPROVED before payroll export
    var approval = await approvalRepo.GetByEmployeeAndPeriodAsync(
        request.Profile.EmployeeId, request.PeriodStart, request.PeriodEnd, ct);
    if (approval is null || approval.Status != "APPROVED")
    {
        return Results.Json(new
        {
            error = "Period must be approved before payroll export",
            employeeId = request.Profile.EmployeeId,
            periodStart = request.PeriodStart,
            periodEnd = request.PeriodEnd,
            currentStatus = approval?.Status ?? "NOT_FOUND"
        }, statusCode: 403);
    }

    // Forward auth header and correlation ID for traceability
    var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
    Guid? correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var corrValues)
        && Guid.TryParse(corrValues.FirstOrDefault(), out var parsedCorr)
            ? parsedCorr
            : null;

    // S134 TASK-13404 (QUAL-003): call the planless CalculateWithOutcomeAsync overload — the
    // outcome-returning sibling of the legacy planless CalculateAsync shim. It builds the SAME
    // PlannedCalculation via the SAME BuildPlanWithHistoriesAsync and runs the SAME core, so
    // outcome.Result is bit-identical to what CalculateAsync(profile, …) returned here before:
    // the export payload / SLS lines are unchanged BY CONSTRUCTION. The outcome additionally
    // exposes the ManifestId and the AuditState, which the provability check below reads.
    // S145 TASK-14508 (R14): no hours are passed — the calculation reads the employee's recorded
    // rows itself (the body's lists were refused above when non-empty).
    PeriodCalculationService.PeriodCalculationOutcome outcome;
    try
    {
        // S146 TASK-14605 (R-14): no opening flex balance is passed — admission derives it from the ledger.
        outcome = await calculator.CalculateWithOutcomeAsync(
            request.Profile,
            request.PeriodStart,
            request.PeriodEnd,
            authHeader,
            correlationId,
            ct);

        // S146 TASK-14604 (QUAL-205; rulings R-22, R-27): no export without its proof. The calculation's
        // manifest (written in one transaction with the legacy summary event) must be stored before anything
        // is exported from the result. ThrowIfNotProvable is the ONE predicate both calculating routes call;
        // it throws ManifestNotPersistedException (→ 503 below) when the manifest was not persisted, or when a
        // successful result carries no manifest. It runs BEFORE StampAuditContext and the export, so a refusal
        // writes no export record, no PayrollExportGenerated, no ADR-026 audit row and no outbox message, and
        // stamps no manifest id. (The HTTP request log still records the failed request, by design.)
        outcome.ThrowIfNotProvable();
    }
    catch (PlannerInvariantViolation ex) when (PayrollPlanRefusalProblem.TryCreate(ex) is { } problem)
    {
        // S144 TASK-14403: the planner REFUSED the month (a whole-period rule would be evaluated in
        // 2+ segments). Since S145 B2 payroll's own rule set never refuses a split month here, so
        // this answers only a rule set that holds such a rule (a test fixture). The plan is built before anything
        // is calculated or written, so no manifest and no export record exist. 422 with the
        // redacted problem (counts + causes, never dates or the employee id); any OTHER planner
        // violation is a bug and falls through as a 500.
        return Results.Json(problem, statusCode: 422);
    }
    catch (PeriodNotCalculableException ex) when (PayrollPlanRefusalProblem.TryCreate(ex) is { } problem)
    {
        // S145 TASK-14512 (Q5 (c)): the week check REFUSED the month — an agreement change inside a
        // Monday–Sunday week the month settles (a change on a 1st that is not a Monday, or in the
        // previous month's last days). Raised by admission, after the planner and before the
        // prior-month check and any rule call (N1), so nothing was calculated or written. The second
        // 422 variant: reason "agreement-change-mid-week", no ruleId, no date, no employee id.
        return Results.Json(problem, statusCode: 422);
    }
    catch (PriorMonthNotApprovedException ex)
    {
        // S145 TASK-14512 (R5(b), R16): months in order — the month's first week holds EMPLOYED days
        // of the previous month, which is not APPROVED. Raised by admission after both 422 checks and
        // before any rule call, so nothing was calculated or written. 409 naming the previous month.
        return PriorMonthNotApproved(ex);
    }
    catch (FlexLedgerRefusalException ex)
    {
        // S146 TASK-14605 (QUAL-188; R-14): the month's opening flex balance cannot be derived from the recorded
        // ledger — an employed previous month with no ledger record (payroll-prior-month-not-recorded, naming it), or
        // (S147 TASK-14700) an earlier recorded month that is out of date (payroll-prior-month-stale, naming it). A
        // month before the ledger start is no longer refused (S147 TASK-14715): it is paid with flex not tracked. Raised
        // by admission after the prior-month check and before any rule call, so nothing was calculated or written.
        return FlexLedgerRefused(flexLedgerLogger, ex, "calculate-and-export admission");
    }
    catch (RuleClassificationsUnavailableException)
    {
        // S144 TASK-14412 (ruling B1): the rule set could not be fetched, so the month could not be
        // checked. The plan is built before anything is calculated or written → nothing exists to
        // undo. Fixed 503; the upstream detail stays in the provider's log.
        return RulesUnavailable();
    }
    catch (ManifestNotPersistedException)
    {
        // S146 TASK-14604 (QUAL-205): the calculation's manifest was not persisted (the commit of its one
        // transaction was not confirmed), so the result is not provable and is not exported. Nothing was
        // exported. Fixed 503; the cause is in the calculation service's Error log.
        return ManifestNotPersisted();
    }

    // Thread the manifest id into HttpContext.Items BEFORE returning, so the AuditLoggingMiddleware —
    // which reads Items AFTER the endpoint runs (post-_next) — writes it into audit_log.details,
    // creating the ADR-016 D10 segment_manifests⋈audit_log linkage. Reached only by a provable outcome
    // (the check above refused the rest). It stamps a PERSISTED manifest only (AuditState.Complete): a
    // calculation refused because a rule-engine call failed (S145 TASK-14511, adjudicator R-6) carries a
    // plan id that resolves in no store, so it is not stamped (S146 TASK-14604).
    PeriodCalculationService.StampAuditContext(httpContext, outcome);

    // Derive the SAME PeriodCalculationResult the endpoint returned before — the response body,
    // export lines, and every downstream branch are unchanged (outcome.Result IS that result).
    var result = outcome.Result;

    if (!result.Success)
        return Results.UnprocessableEntity(result);

    // S146 TASK-14605 (QUAL-188; R-3, R-14): the month's flex-ledger write — its record identity, the derived
    // opening and provenance, the engine's closing, and the under-lock recheck. Null exactly when the month holds
    // no EMPLOYED day: then no flex is settled, the calculation produced no line, and no record is written (ADR-034
    // D3). Otherwise the record is written even when there are no lines (an employed zero-line month: lines empty,
    // balances set, corrections-only from then on) — the export used to be skipped for an empty line list. The one
    // exception (S147 TASK-14715, R-17): a month before the ledger start with no lines and no record has its flex not
    // tracked, so it hands off nothing and the export writes no record (ADR-034 D3).
    var flexWrite = calculator.FlexLedgerWriteFor(outcome);
    if (flexWrite is null && result.ExportLines.Count > 0)
    {
        throw new InvalidOperationException(
            "calculate-and-export invariant violated: the calculation produced export lines for a month whose opening " +
            "flex balance was never derived (no EMPLOYED day). Nothing was exported.");
    }

    // Send to payroll whenever the month holds an EMPLOYED day (S146: zero lines included).
    if (flexWrite is not null)
    {
        // S90 / TASK-9002 (B2 + OQ-1): thread the approval period id so ExportAsync re-asserts
        // status='APPROVED' under FOR UPDATE before writing the lock record — closing the
        // export↔reopen race. The lock is set at COMMIT, independent of downstream delivery.
        //
        // S145 TASK-14512 Do 7: thread the PREVIOUS month the calculation's admission required to be
        // APPROVED as well. ExportAsync re-checks it under a row lock inside the same transaction, taking
        // that lock BEFORE this month's (the reopen's order: an earlier month before a later one), so a
        // reopen of the previous month and this export serialize: whichever commits first, the other sees
        // it — no export on a withdrawn approval, and no reopen that misses this export's record.
        var actorId = httpContext.User.FindFirst("sub")?.Value;
        var context = new PayrollExportContext
        {
            PeriodId = approval.PeriodId,
            PriorMonthToRecheck = outcome.PriorMonthApprovalRequired,
            FlexLedger = flexWrite,
            // S146 Step 7a (R-54): the calculation's manifest rides PayrollExportGenerated, so even a record with no
            // lines names it. A committed manifest: this point is past ThrowIfNotProvable and StampAuditContext.
            ManifestId = outcome.ManifestId,
            Source = "CALCULATE_AND_EXPORT",
            ActorId = actorId,
            ActorRole = actor.ActorRole,
            ResolvedTargetOrgId = request.Profile.OrgId,
            CorrelationId = correlationId,
        };
        ExportResult exportResult;
        try
        {
            exportResult = await export.ExportAsync(result.ExportLines, context, ct);
        }
        catch (FlexLedgerRefusalException ex)
        {
            // S146 TASK-14605 (R-14 "the under-lock recheck"): inside the export transaction, after the ledger lock,
            // the derivation re-ran on in-transaction reads and no longer gave the opening the calculation used
            // (another export or a correction recorded the previous month meanwhile). The transaction rolled back:
            // no export record, no PayrollExportGenerated, no audit row. The calculation's own manifest stays, as
            // with the other export-time rechecks. 409 payroll-ledger-changed: retry.
            return FlexLedgerRefused(flexLedgerLogger, ex, "calculate-and-export export");
        }
        catch (PriorMonthNotApprovedException ex)
        {
            // S145 TASK-14512 Do 7: the previous month was reopened after this calculation's admission
            // read it as APPROVED; the export's row-locked recheck saw the withdrawal and rolled back its
            // transaction: no export record (so no lock), no PayrollExportGenerated event, no audit row.
            // The calculation's own manifest, written when the calculation succeeded a moment earlier,
            // stays — the same as the B2 recheck of this month's approval below. The same 409 as
            // admission's.
            return PriorMonthNotApproved(ex);
        }
        catch (PayrollExportConflictException ex)
        {
            // Already exported for this month (idempotency: different content), or the period
            // was no longer APPROVED at export time (B2) → 409 corrections-only.
            return Results.Json(new
            {
                result.EmployeeId,
                result.PeriodStart,
                result.PeriodEnd,
                Success = false,
                error = ex.Message,
                kind = "payroll-export-conflict"
            }, statusCode: 409);
        }
        if (!exportResult.Success)
        {
            return Results.UnprocessableEntity(new
            {
                result.EmployeeId,
                result.PeriodStart,
                result.PeriodEnd,
                result.AgreementCode,
                result.OkVersion,
                result.RuleResults,
                result.ExportLines,
                Success = false,
                ErrorMessage = "Calculation succeeded but payroll export failed",
                ExportId = exportResult.ExportId
            });
        }
    }

    // S146 TASK-14605 (R-14 case 3b), and since S147 TASK-14715 (SEC-054 step 1; OQ-3 remedy (i)) case 3a: a month
    // before the ledger start ran UNTRACKED, whether it was already recorded (3b) or this is its first export (3a).
    // Its balances stay null and no FLEX_PAYOUT was paid; with no lines and no record nothing was written. The
    // response says so in plain words beside the result.
    if (outcome.FlexOpening is { IsUntracked: true } untrackedOpening)
        return OkWithFlexNote(httpContext, result, FlexLedgerOpening.UntrackedNote(untrackedOpening.LedgerStart));

    return Results.Ok(result);
// LocalAdmin is acceptable here because the resource-scope guard above (TASK-1902)
// validates the caller's scope against the target employee's org. The previous
// comment relied on the approval guard for scoping, which Codex showed to be
// incorrect since approval matches only (employee, period). (The raw export routes, which
// bypassed both guards under GlobalAdminOnly, were retired in S147 TASK-14716, SEC-054.)
}).RequireAuthorization("LocalAdminOrAbove");

// Retroactive correction endpoint: intentionally NOT guarded by period approval.
// Corrections are initiated by admins for already-exported periods where rules or data changed.
// Idempotency guard prevents duplicate correction processing (TASK-1001).
app.MapPost("/api/payroll/recalculate", async (
    RecalculateRequest request,
    RetroactiveCorrectionService correctionService,
    IdempotencyGuard idempotencyGuard,
    // S147 / TASK-14703 (F1): the departed-self guard's deny-only subject read and its clock.
    UserRepository userRepo,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // S146 Step 7a (R-56): a flex-ledger refusal is logged where it is answered (FlexLedgerRefused).
    var flexLedgerLogger = loggerFactory.CreateLogger(FlexLedgerLogCategory);

    // S145 TASK-14508 (R14): hours in the body are refused, loudly — payroll reads the employee's
    // recorded hours itself. This route has no org-scope check (GlobalAdminOnly) and no approval read,
    // so the refusal comes FIRST: before the idempotency lookup below (a database read) and before
    // anything is calculated or written — no baseline change, correction event, audit row or marker.
    if (RefuseBodyHours(request.Entries, request.Absences) is { } bodyHoursRefusal)
        return bodyHoursRefusal;

    // S146 TASK-14605 (QUAL-188; R-14, AC-C2): an opening flex balance in the body is refused, beside the hours
    // refusal and before any read (the idempotency lookup included): the recalculation derives the opening from
    // the recorded ledger. Nothing is calculated or written.
    if (RefuseBodyOpeningBalance(request.PreviousFlexBalance) is { } openingRefusal)
        return openingRefusal;

    // S146 R-47 (Codex 5a BLOCKER): the period must be one whole calendar month. Placed after the body refusals and
    // before the idempotency lookup (a database read), so nothing is read or written for a malformed period.
    if (RefusePeriodNotAMonth(request.PeriodStart, request.PeriodEnd) is { } periodRefusal)
        return periodRefusal;

    // S147 / TASK-14703 (QUAL-230 T5a; F1 — the Codex cycle-5 BLOCKER): a departed person cannot correct
    // their OWN pay. This route is GlobalAdminOnly with no org-scope check by design, so the validator's
    // departed-self clause (R-30) never runs here; the guard is inline. When the correction names the
    // actor (Ordinal; an id-less token never matches), the actor's record is read terminated-inclusive —
    // a DENY-ONLY read (inventoried on DepartedSubject): it can only refuse — and a departed self
    // (deactivated, or past their last day) answers 403 with the date-free reason. It sits after the three
    // body refusals and BEFORE the idempotency lookup below (P-4), so a refused call reads and writes
    // nothing else: no baseline, correction event, audit row or idempotency marker, and an already-delivered
    // token cannot answer "Correction already processed" first. A subject that cannot be read at all is not
    // refused here; the route's own outcome applies. "Today" is read once, only when the identity matches.
    var guardActor = httpContext.GetActorContext();
    if (DepartedSubject.IsSelf(guardActor.ActorId, request.Profile.EmployeeId))
    {
        var self = await userRepo.GetByIdIncludingTerminatedAsync(guardActor.ActorId!, ct);
        if (self is not null && DepartedSubject.IsDeparted(self, CopenhagenBusinessDate.Today(timeProvider)))
        {
            loggerFactory.CreateLogger("StatsTid.Integrations.Payroll.Recalculate").LogWarning(
                "Access denied ({Clause}): Actor={ActorId} Role={ActorRole} Route={Route} CorrelationId={CorrelationId}",
                DepartedSubject.ClauseName, guardActor.ActorId, guardActor.ActorRole,
                "POST /api/payroll/recalculate", guardActor.CorrelationId);
            return Results.Json(new { error = "Access denied", reason = DepartedSubject.DenyReason }, statusCode: 403);
        }
    }

    var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
    Guid? correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var corrValues)
        && Guid.TryParse(corrValues.FirstOrDefault(), out var parsedCorr)
            ? parsedCorr
            : null;

    // Resolve idempotency token: use provided or generate new
    var idempotencyToken = request.IdempotencyToken ?? Guid.NewGuid();

    // Check idempotency: if this token was already processed, return early
    if (await idempotencyGuard.HasBeenDeliveredAsync(idempotencyToken, ct))
    {
        return Results.Ok(new
        {
            success = true,
            message = "Correction already processed",
            idempotencyToken
        });
    }

    // Extract actor ID from claims
    var actorId = httpContext.User.FindFirst("sub")?.Value ?? "unknown";

    RetroactiveCorrectionResult result;
    try
    {
        // S90 / TASK-9004 (B3): the diff baseline is read from payroll_export_records
        // (current_effective_lines), NOT from the request body — corrections require a prior
        // export. A never-exported month throws PayrollExportNotFoundException → a clean 422 below.
        // S145 TASK-14508 (R14): no hours are passed — the recalculation reads the employee's current
        // recorded rows itself. S146 TASK-14605 (R-14): no opening flex balance either — it is derived.
        result = await correctionService.RecalculateAsync(
            request.Profile,
            request.PeriodStart,
            request.PeriodEnd,
            request.Reason,
            actorId,
            authHeader,
            correlationId,
            idempotencyToken,
            request.OkTransitionDate,
            request.PreviousOkVersion,
            ct);
    }
    catch (PayrollExportNotFoundException ex)
    {
        // A correction of a month never sent to payroll — there is nothing to correct; the operator
        // reopens/edits instead (corrections-only-post-lock). Discriminated so the FE can branch.
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            kind = "payroll-not-exported",
            employeeId = request.Profile.EmployeeId,
            periodStart = request.PeriodStart,
            periodEnd = request.PeriodEnd
        }, statusCode: 422);
    }
    catch (PlannerInvariantViolation ex) when (PayrollPlanRefusalProblem.TryCreate(ex) is { } problem)
    {
        // S144 TASK-14403: the planner REFUSED the month (a whole-period rule would be evaluated in
        // 2+ segments). Since S145 B2 payroll's own rule set never refuses a split month here, so
        // this answers only a rule set that holds such a rule (a test fixture). The
        // correction service plans BEFORE it opens its transaction, so nothing was written: the
        // export baseline, the correction event and the idempotency marker are all untouched. 422
        // with the redacted problem (counts + causes, never dates or the employee id); any OTHER
        // planner violation is a bug and falls through as a 500.
        return Results.Json(problem, statusCode: 422);
    }
    catch (PeriodNotCalculableException ex) when (PayrollPlanRefusalProblem.TryCreate(ex) is { } problem)
    {
        // S145 TASK-14512 (Q5 (c)): the week check REFUSED the month (an agreement change inside a week
        // it settles). The correction service calculates BEFORE it opens its transaction, so nothing was
        // written: baseline, correction event, audit row and idempotency marker are all untouched. The
        // second 422 variant (reason "agreement-change-mid-week").
        return Results.Json(problem, statusCode: 422);
    }
    catch (PriorMonthNotApprovedException ex)
    {
        // S145 TASK-14512 (R5(b), R16): months in order — the previous month is not APPROVED (for a
        // month exported through the raw routes it typically has no approval row at all). Raised before
        // the correction transaction opens, so nothing was written. 409 naming the previous month.
        return PriorMonthNotApproved(ex);
    }
    catch (FlexLedgerRefusalException ex)
    {
        // S146 TASK-14605 (QUAL-188; R-14): either admission could not derive the month's opening flex balance
        // (payroll-prior-month-not-recorded: the employed previous month has no ledger record — "export it first",
        // or "correct it first" for a raw one), raised before the correction transaction opens; or the under-lock
        // recheck inside the correction transaction found the ledger changed while the correction was computed
        // (payroll-ledger-changed), and the transaction rolled back. Either way nothing was written: no baseline
        // or balance change, no correction event, no audit row, no idempotency marker (a retry with the same token
        // is not short-circuited). 409 naming the month.
        return FlexLedgerRefused(flexLedgerLogger, ex, "recalculate");
    }
    catch (RuleClassificationsUnavailableException)
    {
        // S144 TASK-14412 (ruling B1): the rule set could not be fetched. The correction service
        // plans (inside CalculateAsync) BEFORE it opens its transaction, so the export baseline, the
        // correction event and the idempotency marker are all untouched. Fixed 503.
        return RulesUnavailable();
    }
    catch (ManifestNotPersistedException)
    {
        // S146 TASK-14604 (QUAL-205; R-22, R-27): the recalculation's manifest was not persisted, so the
        // correction would link a manifest that exists nowhere. RetroactiveCorrectionService calls
        // ThrowIfNotProvable BEFORE it opens its correction transaction, so nothing was written: no correction
        // event, no ADR-026 audit row, no baseline change and no idempotency marker (a retry with the same
        // token is not short-circuited). Fixed 503, the same body as /calculate-and-export's.
        return ManifestNotPersisted();
    }

    if (!result.Success)
        return Results.UnprocessableEntity(result);

    // QUAL-006 (S132): the idempotency token is now marked delivered ATOMICALLY inside
    // RetroactiveCorrectionService.RecalculateAsync's correction transaction (the same tx as the
    // correction event + audit row + baseline advance), so the duplicate-prevention marker can no
    // longer be lost independently of the correction it guards. Pre-S132 the marker was written HERE
    // in a separate post-commit tx and its failure was swallowed by an empty catch — silently
    // disarming the guard for that token and letting a same-token retry emit a phantom second
    // correction event + audit row. A marker-write failure now rolls the whole correction back and
    // surfaces as a 5xx (logged with the token/employee/period inside RecalculateAsync); the result
    // reaching this line is fully committed, marker included. The HasBeenDeliveredAsync pre-check
    // above remains the cheap fast-path dedupe that avoids re-running the calc for an already-marked
    // token; the in-tx marker + its UNIQUE constraint are the authoritative guard behind it.
    return Results.Ok(result);
}).RequireAuthorization("GlobalAdminOnly");

// Correction export endpoint: formats correction lines into SLS correction format.
// Pure formatting — no side effects, no DB writes. Used after retroactive recalculation
// to produce the delta export file for the payroll system.
app.MapPost("/api/payroll/export-corrections", (CorrectionExportRequest request) =>
{
    if (request.CorrectionLines.Count == 0)
        return Results.BadRequest(new { success = false, error = "No correction lines to export" });

    var exportId = request.ExportId ?? Guid.NewGuid().ToString();
    var timestamp = DateTime.UtcNow;
    var formatted = SlsExportFormatter.FormatCorrections(request.CorrectionLines, exportId, timestamp);

    return Results.Ok(new
    {
        success = true,
        exportId,
        timestamp,
        recordCount = request.CorrectionLines.Count,
        slsContent = formatted
    });
}).RequireAuthorization("GlobalAdminOnly");

app.Run();

// S144 TASK-14412 (Step 7a cycle 2, ruling B1) — the ONE response for "the Rule Engine's rule
// classification set could not be obtained, so the period could not be planned". 503 because the
// cause is a dependency outage and the caller should retry. The body is FIXED: no employee id, no
// dates, and never the upstream status or body (the provider logs those; the route never echoes
// them). Shared by /calculate-and-export and /recalculate (S147 TASK-14716 retired the raw routes' plan-only
// guard, its third user).
static IResult RulesUnavailable() =>
    Results.Json(new
    {
        success = false,
        error = "The rule set could not be obtained from the Rule Engine, so the period could not be checked. Nothing was calculated, written or exported. Retry when the Rule Engine is reachable.",
        kind = "payroll-rules-unavailable"
    }, statusCode: 503);

// S146 TASK-14604 (QUAL-205; rulings R-22, R-27) — the ONE response for "the calculation's manifest was not
// persisted, so its result is not provable and is not used". Both calculating routes answer it when
// PeriodCalculationOutcome.ThrowIfNotProvable throws, before any payroll write. 503 because the usual cause is
// the Payroll database being unavailable for the manifest's transaction, and the caller should retry. The body is
// FIXED (ManifestNotPersistedException.ErrorSentence): no employee id, no date, no manifest id, never the
// underlying database error (the calculation service logs that).
static IResult ManifestNotPersisted() =>
    Results.Json(new
    {
        success = false,
        error = ManifestNotPersistedException.ErrorSentence,
        kind = ManifestNotPersistedException.KindValue
    }, statusCode: 503);

// S145 TASK-14512 (rulings R5(b), R16, N2) — the ONE response for "the previous month must be approved
// first". 409 because the month itself is fine but its predecessor is in the wrong state, which the operator
// can fix (approve it) and retry. The body carries the PREVIOUS month's pay period — a pay period, never an
// employment date — plus the members every sibling body has (success, error, kind). The sentence is fixed:
// no employee id and no other date. Shared by /calculate-and-export (admission's check and the export's
// row-locked recheck, Do 7) and /recalculate.
static IResult PriorMonthNotApproved(PriorMonthNotApprovedException ex) =>
    Results.Json(new
    {
        success = false,
        error = PriorMonthNotApprovedException.ErrorSentence,
        kind = PriorMonthNotApprovedException.KindValue,
        periodStart = ex.PeriodStart,
        periodEnd = ex.PeriodEnd
    }, statusCode: 409);

// S145 TASK-14508 (R14) — the ONE refusal for "this request carries hours". Payroll reads the
// employee's recorded, current hours itself, so a caller-supplied hours list is no longer an input.
// An absent or EMPTY list is accepted (callers built for the old contract send empty lists); a
// NON-EMPTY list answers 400 rather than being silently ignored, because silently dropped hours is
// exactly the defect class S145 fixes — and unlike the quiet deprecation of the optional
// PreviousExportLines field (RecalculateRequest below), hours were never optional to the result.
// The body is FIXED: it echoes neither the refused entries' dates nor the employee id. Shared by
// /calculate-and-export and /recalculate; returns null when the request carries no hours.
static IResult? RefuseBodyHours(List<TimeEntry>? entries, List<AbsenceEntry>? absences) =>
    (entries is { Count: > 0 } || absences is { Count: > 0 })
        ? Results.Json(new
        {
            success = false,
            error = "Hours are not accepted in the request. Payroll reads the employee's recorded hours itself; send the request without entries and absences. Nothing was calculated, written or exported.",
            kind = "payroll-entries-not-accepted"
        }, statusCode: 400)
        : null;

// S146 TASK-14605 (QUAL-188; R-14, AC-C2) — the ONE refusal for "this request carries an opening flex balance".
// The calculating routes derive a month's opening from the recorded ledger (the previous month's recorded closing,
// or 0 at the ledger start or after a gap in employment); a caller-supplied opening is no longer an input. Any
// non-null value, 0 included, answers 400 rather than being silently ignored — silently ignoring it would hide a
// caller that still believes it controls the balance. An absent or null field is accepted. The body is FIXED: it
// echoes neither the refused value nor the employee id. Shared by /calculate-and-export and /recalculate; returns
// null when the request carries no opening.
static IResult? RefuseBodyOpeningBalance(decimal? previousFlexBalance) =>
    previousFlexBalance is not null
        ? Results.Json(new
        {
            success = false,
            error = "An opening flex balance is not accepted in the request. Payroll derives each month's opening flex balance from the recorded ledger (the previous month's recorded closing balance); send the request without previousFlexBalance. Nothing was calculated, written or exported.",
            kind = "payroll-opening-balance-not-accepted"
        }, statusCode: 400)
        : null;

// S146 TASK-14605 fix-up (ruling R-47; Codex 5a BLOCKER) — the ONE refusal for "this period is not one whole calendar
// month". Payroll records, locks and corrects a calendar month (ADR-034 D3/D5), and since S146 the month's export record
// carries the flex balance the month closed at, which the next month opens from (R-14). A partial period would store its
// own closing as the month's and the next month would chain from it; on the correction route it would also replace the
// month's diff baseline with a partial period's lines. Both calculating routes therefore accept only periodStart = the
// 1st and periodEnd = the last day of the same month. 400 like the two body refusals (the request's shape, not the
// month's data). The body is FIXED: no employee id, no dates.
static IResult? RefusePeriodNotAMonth(DateOnly periodStart, DateOnly periodEnd) =>
    FlexLedgerOpening.IsWholeCalendarMonth(periodStart, periodEnd)
        ? null
        : Results.Json(new
        {
            success = false,
            error = "The period must be one whole calendar month: periodStart the first day and periodEnd the last day of the same month. Payroll records, locks and corrects a calendar month, and the flex ledger chains from the month's closing balance, so a partial period cannot be calculated and exported or corrected. Nothing was calculated, written or exported.",
            kind = "payroll-period-not-a-month"
        }, statusCode: 400);

// S147 / TASK-14713 (QUAL-230 T5b; refinement R-15) — the employed-month bound on /calculate-and-export, the
// Payroll host's copy of the Backend's ApprovalEndpoints.EmployedMonthBoundAsync (this host cannot reference that
// assembly). WHY: from day 6 after a leaver's last working day the leaver job switches them off, and the scope check
// above now reaches a switched-off employee (an R9c widening, LocalAdmin-floored). The widening is bounded to months
// in which that person was actually employed. Returns null when the request may proceed: the subject is active (a
// leaver in the manager's 5-day grace included — unchanged), or the month holds an employed day (the window's end date
// inclusive, a NULL side unbounded — ADR-040 D1/D2). Otherwise ADR-040 D3's date-free answer: a 422 with the same key,
// kind and message as the Backend's, plus the success member every sibling body here carries — no date, no employee id,
// no window field (the binary-search oracle D3 closes). The server log names the clause and the requested month. A
// missing users row cannot reach here (the scope check answered "not found"); it is left to the flow, unchanged.
static async Task<IResult?> RefuseMonthWithoutEmployedDayAsync(
    UserRepository userRepo,
    IEmploymentWindowResolver windowResolver,
    ILoggerFactory loggerFactory,
    ActorContext actor,
    string employeeId,
    DateOnly periodStart,
    DateOnly periodEnd,
    CancellationToken ct)
{
    var subject = await userRepo.GetByIdIncludingTerminatedAsync(employeeId, ct);
    if (subject is null || subject.IsActive)
        return null;

    var windows = await windowResolver.GetWindowsAsync(employeeId, periodStart, periodEnd, ct);
    if (windows.Count > 0)
        return null;

    loggerFactory.CreateLogger("StatsTid.Integrations.Payroll.EmployedMonthBound").LogWarning(
        "Refused ({Clause}): actor {ActorId} asked for {PeriodStart:yyyy-MM} of deactivated employee {EmployeeId}, " +
        "which holds no employed day (POST /api/payroll/calculate-and-export)",
        "employed-month-bound", actor.ActorId, periodStart, employeeId);
    return Results.Json(new
    {
        success = false,
        error = "month_outside_employment_period",
        kind = "employment-window",
        message = "Måneden ligger uden for ansættelsesperioden.",
    }, statusCode: 422);
}

// S146 TASK-14605 (QUAL-188; R-14) — the ONE response for a flex-ledger refusal. 409 because the month itself is
// fine but the ledger is in a state the operator can fix (export or correct the named month first) or that a retry
// resolves (the ledger changed while this request was computed). The body carries the named month as yyyy-MM — a
// pay month, never an employment date — plus the members every sibling body has (success, error, kind). The
// sentence is fixed per kind: no employee id and no date. Shared by /calculate-and-export (admission and the
// export's under-lock recheck) and /recalculate (admission and the correction's under-lock recheck).
//
// S146 Step 7a (R-56): the refusal is logged first, as a Warning on the caller's FlexLedger-category logger, with
// the exception, its kind, the route that answered (calculate-and-export admission | calculate-and-export export |
// recalculate), the named month and the diagnostic message, which never reaches the response. A refusal is by
// design a non-event (R-14), so this is a diagnosability line, not an audit row. The 409 body is unchanged.
static IResult FlexLedgerRefused(ILogger logger, FlexLedgerRefusalException ex, string route)
{
    logger.LogWarning(ex, "Flex ledger refusal {Kind} on {Route}, month {Month}: {Diagnostic}",
        ex.Kind, route, ex.MonthText, ex.Message);
    return Results.Json(new
    {
        success = false,
        error = ex.ErrorSentence,
        kind = ex.Kind,
        month = ex.MonthText
    }, statusCode: 409);
}

// S146 TASK-14605 (R-14 case 3b; case 3a since S147 TASK-14715) — the success body of /calculate-and-export with
// the "flex not tracked" statement beside the result: the result exactly as Results.Ok(result) would serialize it (the host's own JSON options), plus
// one member, flexNote. Built from the serialized result rather than a hand-listed copy of its members, so a member
// added to PeriodCalculationResult later can never be left out of this body.
static IResult OkWithFlexNote(HttpContext httpContext, PeriodCalculationResult result, string flexNote)
{
    var jsonOptions = httpContext.RequestServices
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
        .Value.SerializerOptions;
    var body = System.Text.Json.JsonSerializer.SerializeToNode(result, jsonOptions)!.AsObject();
    body["flexNote"] = flexNote;
    return Results.Ok(body);
}

public sealed class CalculateAndExportRequest
{
    public required EmploymentProfile Profile { get; init; }

    /// <summary>
    /// NOT AN INPUT (S145 / TASK-14508, R14). Payroll reads the employee's recorded hours itself.
    /// Optional so that callers built for the old contract, which send an empty list, still bind; an
    /// absent or empty list is accepted, a NON-EMPTY list is refused with 400
    /// <c>payroll-entries-not-accepted</c> (never silently ignored).
    /// </summary>
    public List<TimeEntry>? Entries { get; init; }

    /// <summary>NOT AN INPUT (S145 / TASK-14508, R14) — as <see cref="Entries"/>.</summary>
    public List<AbsenceEntry>? Absences { get; init; }

    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// NOT AN INPUT (S146 / TASK-14605, R-14). The month's opening flex balance is derived from the recorded
    /// ledger. Nullable so that an absent or null field still binds; ANY value (0 included) is refused with 400
    /// <c>payroll-opening-balance-not-accepted</c> before anything is read, calculated or written.
    /// </summary>
    public decimal? PreviousFlexBalance { get; init; }
}

public sealed class RecalculateRequest
{
    public required EmploymentProfile Profile { get; init; }

    /// <summary>
    /// NOT AN INPUT (S145 / TASK-14508, R14). The recalculation reads the employee's current recorded
    /// hours itself. An absent or empty list is accepted; a NON-EMPTY list is refused with 400
    /// <c>payroll-entries-not-accepted</c> before anything is read or written.
    /// </summary>
    public List<TimeEntry>? Entries { get; init; }

    /// <summary>NOT AN INPUT (S145 / TASK-14508, R14) — as <see cref="Entries"/>.</summary>
    public List<AbsenceEntry>? Absences { get; init; }

    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// NOT AN INPUT (S146 / TASK-14605, R-14) — as <see cref="CalculateAndExportRequest.PreviousFlexBalance"/>: the
    /// recalculation derives the opening from the recorded ledger; any value is refused with 400
    /// <c>payroll-opening-balance-not-accepted</c> before anything is read or written.
    /// </summary>
    public decimal? PreviousFlexBalance { get; init; }

    /// <summary>
    /// DEPRECATED (S90 / TASK-9004 / B3). The correction diff baseline is now read from
    /// <c>payroll_export_records.current_effective_lines</c>, NOT from the request body — so a
    /// SECOND correction diffs against the FIRST correction's result (no double-count), and the
    /// manifest does not have to be re-supplied (and could not be kept correct caller-side anyway).
    /// Retained as an OPTIONAL field so pre-S90 callers that still send it deserialize without error;
    /// the value is IGNORED.
    /// </summary>
    public List<PayrollExportLine>? PreviousExportLines { get; init; }

    public required string Reason { get; init; }
    public Guid? IdempotencyToken { get; init; }

    /// <summary>
    /// When an OK version transition occurs mid-period, this is the date on which
    /// the new OK version takes effect. Entries before this date use PreviousOkVersion;
    /// entries on or after use Profile.OkVersion. Null means no version split.
    /// </summary>
    public DateOnly? OkTransitionDate { get; init; }

    /// <summary>
    /// The old OK version that applied before OkTransitionDate.
    /// Required when OkTransitionDate is set; ignored otherwise.
    /// </summary>
    public string? PreviousOkVersion { get; init; }
}

public sealed class CorrectionExportRequest
{
    public required List<CorrectionExportLine> CorrectionLines { get; init; }
    public string? ExportId { get; init; }
}

