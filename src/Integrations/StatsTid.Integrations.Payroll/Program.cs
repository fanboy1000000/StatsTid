using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Resilience;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));

// ── Outbox: dual-binding per ADR-018 D3 + per-service publisher per D2/D6 ──
// Payroll owns period-calculation-* + payroll-export-* + retroactive correction
// streams per ADR-018 D6 stream-ownership table.
builder.Services.AddSingleton(new OutboxServiceContext("payroll"));
builder.Services.AddSingleton<PostgresEventStore>(sp => new PostgresEventStore(
    sp.GetRequiredService<DbConnectionFactory>(),
    sp.GetRequiredService<OutboxServiceContext>()));
builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddHostedService<OutboxPublisher>();

builder.Services.AddHttpClient();
// S29 TASK-2907: WageTypeMappingRepository is now an optional dependency of
// PayrollMappingService for the dated-read path (ADR-020 D1 / Implications §7).
// MapCalculationResultAsync stays current-row (no asOfDate); only the planner-driven
// per-segment export in PCS.MapSegmentToExportLinesAsync passes asOfDate = segmentStart.
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
builder.Services.AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>();
builder.Services.AddSingleton<PayrollMappingService>();
builder.Services.AddSingleton<PayrollExportService>();
// S90 / TASK-9004 (ADR-034): the corrections seam over payroll_export_records — reads
// current_effective_lines as the /recalculate diff baseline (B3) and rewrites it in the
// correction tx so a 2nd correction diffs against the 1st correction's result.
builder.Services.AddSingleton<PayrollExportRecordRepository>();
builder.Services.AddSingleton<PeriodCalculationService>();
builder.Services.AddSingleton<RetroactiveCorrectionService>();
// S45: cross-process audit projection for RetroactiveCorrectionRequested
builder.Services.AddSingleton<AuditProjectionRepository>();
builder.Services.AddSingleton<StatsTid.SharedKernel.Audit.IAuditProjectionMapper<StatsTid.SharedKernel.Events.RetroactiveCorrectionRequested>, StatsTid.Infrastructure.AuditMappers.RetroactiveCorrectionRequestedAuditMapper>();
// S90 (ADR-034 / TASK-9001): cross-process audit projection for PayrollExportGenerated
// — the per-(employee, year, month) payroll-export lock fact, emitted from this
// process (TASK-9002). Mirrors the RetroactiveCorrectionRequested registration above.
builder.Services.AddSingleton<StatsTid.SharedKernel.Audit.IAuditProjectionMapper<StatsTid.SharedKernel.Events.PayrollExportGenerated>, StatsTid.Infrastructure.AuditMappers.PayrollExportGeneratedAuditMapper>();
builder.Services.AddSingleton<ApprovalPeriodRepository>();
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
// EmptyRuleClassificationProvider (defined in PeriodCalculationService.cs) is retained
// for tests and for any environment where the Rule Engine is unreachable; it remains
// the documented fallback when this DI registration is absent.
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
builder.Services.AddSingleton<OrgScopeValidator>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payroll-integration" }));

// Low-level export endpoint: intentionally NOT guarded by period approval.
// Used for admin retroactive corrections and internal service-to-service calls.
// The high-level /api/payroll/calculate-and-export endpoint enforces the approval guard.
app.MapPost("/api/payroll/export", async (
    PayrollExportRequest request,
    PayrollMappingService mapping,
    PayrollExportService export,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // TASK-2010 (S20): per-line OK-version stamping inside the mapper supersedes the
    // pre-S20 OkVersionBoundary.ResolveProfile collapse. The mapping service resolves
    // OK per-line from each CalculationLineItem.Date, so straddling exports stay correct
    // (ADR-016 D5). Manifest id flows through from the upstream CalculationResult; legacy
    // callers that don't thread a manifest send Guid.Empty (D10 amendment 2026-04-29).
    var lines = await mapping.MapCalculationResultAsync(
        request.CalculationResult,
        request.Profile,
        request.Profile.Position,
        request.CalculationResult.ManifestId,
        ct);

    if (lines.Count == 0)
        return Results.BadRequest(new { success = false, error = "No mappable line items" });

    // S90 / TASK-9002 (OQ-6): the raw /export gets the lock record + idempotency, but
    // passes a null PeriodId — it bypasses approval BY DESIGN (admin retroactive / internal
    // calls), so the B2 APPROVED FOR UPDATE re-check does not apply. source = EXPORT.
    var context = BuildExportContext(httpContext, request.Profile, periodId: null, source: "EXPORT");
    try
    {
        var result = await export.ExportAsync(lines, context, ct);
        return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
    }
    catch (PayrollExportConflictException ex)
    {
        return Results.Json(new { success = false, error = ex.Message, kind = "payroll-export-conflict" }, statusCode: 409);
    }
}).RequireAuthorization("GlobalAdminOnly");

app.MapPost("/api/payroll/export-period", async (
    PayrollPeriodExportRequest request,
    PayrollMappingService mapping,
    PayrollExportService export,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var allLines = new List<PayrollExportLine>();

    foreach (var calcResult in request.CalculationResults)
    {
        // Per-line OK + per-line manifest stamping inside the mapper (TASK-2010, ADR-016 D5/D10).
        var lines = await mapping.MapCalculationResultAsync(
            calcResult,
            request.Profile,
            request.Profile.Position,
            calcResult.ManifestId,
            ct);
        allLines.AddRange(lines);
    }

    if (allLines.Count == 0)
        return Results.BadRequest(new { success = false, error = "No mappable line items in period" });

    // S90 / TASK-9002 (OQ-6 + B4): the raw /export-period flattens multiple periods/months
    // into one call; ExportAsync groups by (employee, year, month) and writes ONE record per
    // tuple, all-or-nothing. Null PeriodId → skips the B2 APPROVED re-check (bypasses approval
    // by design). source = EXPORT_PERIOD.
    var context = BuildExportContext(httpContext, request.Profile, periodId: null, source: "EXPORT_PERIOD");
    try
    {
        var result = await export.ExportAsync(allLines, context, ct);
        return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
    }
    catch (PayrollExportConflictException ex)
    {
        return Results.Json(new { success = false, error = ex.Message, kind = "payroll-export-conflict" }, statusCode: 409);
    }
}).RequireAuthorization("GlobalAdminOnly");

app.MapPost("/api/payroll/calculate-and-export", async (
    CalculateAndExportRequest request,
    PeriodCalculationService calculator,
    PayrollExportService export,
    ApprovalPeriodRepository approvalRepo,
    OrgScopeValidator scopeValidator,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // Resource-scope guard (TASK-1902): the caller's scope must cover the target
    // employee's org. Pre-fix, LocalAdminOrAbove + the APPROVED-period match below
    // were treated as equivalent to per-org scoping, but the approval guard only
    // checks (employee_id, period) — a LocalAdmin from org A could trigger payroll
    // for any employee in org B whose period happens to be APPROVED. This check
    // closes that vector before the approval lookup or any downstream call.
    var actor = httpContext.GetActorContext();
    var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
        actor, request.Profile.EmployeeId, ct);
    if (!allowed)
    {
        return Results.Json(new
        {
            error = "Access denied",
            reason,
            employeeId = request.Profile.EmployeeId
        }, statusCode: 403);
    }

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

    var result = await calculator.CalculateAsync(
        request.Profile,
        request.Entries,
        request.Absences,
        request.PeriodStart,
        request.PeriodEnd,
        request.PreviousFlexBalance,
        authHeader,
        correlationId,
        ct);

    if (!result.Success)
        return Results.UnprocessableEntity(result);

    // If calculation produced export lines, send to payroll
    if (result.ExportLines.Count > 0)
    {
        // S90 / TASK-9002 (B2 + OQ-1): thread the approval period id so ExportAsync re-asserts
        // status='APPROVED' under FOR UPDATE before writing the lock record — closing the
        // export↔reopen race. The lock is set at COMMIT, independent of downstream delivery.
        var actorId = httpContext.User.FindFirst("sub")?.Value;
        var context = new PayrollExportContext
        {
            PeriodId = approval.PeriodId,
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

    return Results.Ok(result);
// LocalAdmin is acceptable here because the resource-scope guard above (TASK-1902)
// validates the caller's scope against the target employee's org. The previous
// comment relied on the approval guard for scoping, which Codex showed to be
// incorrect since approval matches only (employee, period). Raw /export and
// /export-period below bypass both guards and stay GlobalAdminOnly.
}).RequireAuthorization("LocalAdminOrAbove");

// Retroactive correction endpoint: intentionally NOT guarded by period approval.
// Corrections are initiated by admins for already-exported periods where rules or data changed.
// Idempotency guard prevents duplicate correction processing (TASK-1001).
app.MapPost("/api/payroll/recalculate", async (
    RecalculateRequest request,
    RetroactiveCorrectionService correctionService,
    IdempotencyGuard idempotencyGuard,
    DbConnectionFactory dbConnectionFactory,
    HttpContext httpContext,
    CancellationToken ct) =>
{
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
        result = await correctionService.RecalculateAsync(
            request.Profile,
            request.Entries,
            request.Absences,
            request.PeriodStart,
            request.PeriodEnd,
            request.PreviousFlexBalance,
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

    if (!result.Success)
        return Results.UnprocessableEntity(result);

    // Mark idempotency token as delivered in outbox
    try
    {
        await using var conn = dbConnectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(
            """
            INSERT INTO outbox_messages (destination, payload, status, delivered_at, idempotency_token)
            VALUES ('retroactive-correction', '{}', 'delivered', NOW(), @token)
            """, conn);
        cmd.Parameters.AddWithValue("token", idempotencyToken);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    catch (Exception)
    {
        // Non-fatal: idempotency mark failed but correction was processed.
        // Next call with same token will re-process (at-least-once is acceptable).
    }

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

// S90 / TASK-9002 — builds the PayrollExportContext for the raw /export + /export-period
// endpoints (the calculate-and-export path builds its own context inline because it already
// has the resolved actor + correlation id + the approval period). The raw endpoints pass a
// null PeriodId (they bypass approval by design). ResolvedTargetOrgId comes from the request's
// EmploymentProfile.OrgId (mirroring RetroactiveCorrectionService's audit-org resolution).
static PayrollExportContext BuildExportContext(
    HttpContext httpContext, EmploymentProfile profile, Guid? periodId, string source)
{
    var actorId = httpContext.User.FindFirst("sub")?.Value;
    var actorRole = httpContext.User.FindFirst("role")?.Value;
    Guid? correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var corrValues)
        && Guid.TryParse(corrValues.FirstOrDefault(), out var parsedCorr)
            ? parsedCorr
            : null;
    return new PayrollExportContext
    {
        PeriodId = periodId,
        Source = source,
        ActorId = actorId,
        ActorRole = actorRole,
        ResolvedTargetOrgId = profile.OrgId,
        CorrelationId = correlationId,
    };
}

public sealed class PayrollExportRequest
{
    public required CalculationResult CalculationResult { get; init; }
    public required EmploymentProfile Profile { get; init; }
}

public sealed class PayrollPeriodExportRequest
{
    public required List<CalculationResult> CalculationResults { get; init; }
    public required EmploymentProfile Profile { get; init; }
}

public sealed class CalculateAndExportRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required List<AbsenceEntry> Absences { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public decimal PreviousFlexBalance { get; init; }
}

public sealed class RecalculateRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required List<AbsenceEntry> Absences { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public decimal PreviousFlexBalance { get; init; }

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

