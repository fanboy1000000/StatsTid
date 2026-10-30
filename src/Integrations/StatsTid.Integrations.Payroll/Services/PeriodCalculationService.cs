using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;
using StatsTid.SharedKernel.Segmentation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// The "glue" service that connects Rule Engine output to Payroll Export.
///
/// <para>
/// Sprint 20 (ADR-016 D1, D8): the sole logic entry point is
/// <see cref="CalculateWithOutcomeAsync(PlannedCalculation, EmploymentProfile, string?, Guid?, bool, CancellationToken)"/>.
/// Every calculation routes through <see cref="PeriodPlanner"/> first — the back-compat
/// overload <c>CalculateAsync(profile, periodStart, periodEnd, …)</c> below is a thin shim that hydrates
/// <see cref="BoundarySources"/> from <see cref="OkVersionResolver"/> and immediately
/// re-enters the new entry point. There is no "skip planner" code path.
/// </para>
///
/// <para>
/// <strong>Payroll reads its own hours (S145 / TASK-14508, rulings R14 and R25).</strong> No entry
/// point takes time entries or absences any more. The sole logic entry point reads the employee's
/// CURRENT recorded rows itself, through <see cref="IRecordedHoursReader"/>, over
/// [the day before the opening week's Monday, period end], and keeps the rows exactly as read. A
/// calculation or replay on a service built without a reader throws before it reads anything.
/// </para>
///
/// <para>
/// <strong>Two evaluation lanes (S145 / TASK-14511, the QUAL-186 fix).</strong> Overtime /
/// merarbejde, the norm check and flex are evaluated ONCE per plan, week by week, by one
/// <c>POST /api/rules/evaluate-timeline</c> call over the plan's EMPLOYED days plus the EMPLOYED
/// context days of the month's opening week (a week is settled by the month holding its Sunday —
/// owner ruling Q3 = F1 — and a leaver's last week on their last employed day, R41). Supplement,
/// on-call and absence stay per segment. Before S145 the three were evaluated per segment, which
/// judged a whole month as if it were one week.
/// </para>
///
/// <para>
/// <strong>Admission, then evaluation (S145 / TASK-14512, ruling R42).</strong> A calculation runs in
/// two steps. <em>Admission</em> decides whether the month may be calculated today and gathers what it
/// reads: the planner (its split refusal is the first 422), the employee histories, the week check (an
/// agreement change inside a week the month settles — the second 422,
/// <see cref="PeriodNotCalculableException"/>), the months-in-order check (the previous month must be
/// APPROVED when the opening week holds its EMPLOYED days — a 409,
/// <see cref="PriorMonthNotApprovedException"/>, read through <see cref="IPriorPeriodApprovalReader"/>)
/// and the hours read. These admission refusals come in exactly that order (N1), and each one writes
/// nothing. (The export's own race recheck, TASK-14512 Do 7, is different: it refuses after the
/// calculation completed, so the manifest and the legacy calculation-completed event are kept, as with
/// the existing B2 recheck; no export record, export event, delivery envelope or export audit row is
/// written. Pinned by `PriorMonthExportRaceHostTests`.)
/// <em>Evaluation</em> runs the two lanes over a plan, its timeline pieces, the settlement cutoff, the
/// opening balance and the hours rows, then merges, emits the manifest and maps wage types. Forward
/// calculation and recalculation run both steps; replay runs evaluation only, because a replay proves
/// what WAS calculated and today's approval state cannot change that (see
/// <see cref="ReplayAsync(Guid, string?, Guid?, CancellationToken)"/>).
/// </para>
///
/// <para>
/// <strong>The dated wage-type key (S145 / TASK-14512, refinement A4 — QUAL-150).</strong> Each EMPLOYED
/// segment's <see cref="WtmNaturalKey"/> (OK version, agreement code, position) comes from the employee's
/// dated record at that segment's start, read once in planning and shared with the segment's evaluation,
/// so the rule engine and the export always see the same agreement and position. It never comes from
/// the request body.
/// </para>
///
/// <para>
/// Rule evaluation calls the Rule Engine over HTTP (PAT-005) and maps results to
/// wage types via <see cref="PayrollMappingService"/>. Per-segment payroll lines are kept
/// segment-stamped (each <see cref="PayrollExportLine.OkVersion"/> reflects the segment's
/// resolved version) and the per-segment rule outputs are merged via the strategy resolved
/// from <see cref="RuleClassification.MergeStrategy"/> for each rule (ADR-016 D3, applied
/// through <see cref="CalculationResultMerger.Apply"/> /
/// <see cref="ComplianceCheckResultMerger.Apply"/>). The final segment manifest is emitted
/// as a <see cref="SegmentManifestCreated"/> domain event (ADR-016 D10) — exactly once per
/// successful calculation, after segments are resolved, snapshots are gathered, and all
/// invariants pass.
/// </para>
///
/// <para>
/// <strong>D9 rule-side invariants (ADR-016)</strong> — every Plan / FromManifest call
/// receives the resolved <see cref="RuleClassification"/> set from
/// <see cref="IRuleClassificationProvider"/> so the planner can enforce the three rule-side
/// invariants (SnapshotContract completeness, non-null MergeStrategy, snapshot-key presence
/// per <c>NonDatedSourceFields</c>) uniformly across the shim path AND the replay paths.
/// </para>
///
/// <para>
/// <strong>Replay primitive (ADR-016 D10; S145 / TASK-14513, refinement A7)</strong>: a replay re-runs a
/// stored calculation on exactly the inputs it had and either reproduces it or refuses by name
/// (<see cref="ReplayRefusedException"/>). Every successful calculation freezes its inputs into
/// <see cref="PlanInputs"/> on the manifest, the event and <c>plan_inputs_jsonb</c>: what is DATA is stored
/// (the timeline pieces, the settlement cutoff, the opening flex balance, the hours rows' event ids, the
/// as-of outbox id and the rows' lossless digests), and what is CODE is fingerprinted (the Rule Engine's
/// config fingerprints and evaluation-model marker). The one replay entry point,
/// <see cref="ReplayAsync(Guid, string?, Guid?, CancellationToken)"/>, takes nothing but the manifest id
/// (plus the auth header and correlation id for the engine call): it loads the manifest (the
/// <c>segment_manifests</c> projection first, the event store as fallback), reconstructs the
/// <see cref="PlannedCalculation"/> through <see cref="PeriodPlanner.FromManifest"/>, re-reads the recorded
/// hours rows by event id, verifies them, evaluates from the frozen inputs only, and refuses when the
/// engine's marker or fingerprint no longer matches. The result carries the <em>same</em> manifest id; no
/// new manifest is minted.
/// </para>
///
/// <para>
/// <strong>Audit completeness contract (S146 / TASK-14604, QUAL-205; rulings R-21, R-22, R-27)</strong>: a
/// successful calculation's proof (its manifest event, its <c>segment_manifests</c> row and the legacy
/// <see cref="PeriodCalculationCompleted"/>) is written in ONE transaction through the ADR-018 outbox, by
/// <see cref="ISegmentManifestStore.PersistAsync"/>: all of it or none of it. The
/// <see cref="PeriodCalculationOutcome"/> returned by <see cref="CalculateWithOutcomeAsync"/> carries the
/// <see cref="AuditState"/> of that write (<see cref="AuditState.Complete"/> or
/// <see cref="AuditState.NotPersisted"/>). A calculation that is not provable is never used: both calculating
/// routes call <see cref="PeriodCalculationOutcome.ThrowIfNotProvable"/> before any payroll write and answer
/// 503 <c>payroll-manifest-not-persisted</c>. The calculate-and-export endpoint then calls
/// <see cref="StampAuditContext(HttpContext, PeriodCalculationOutcome)"/> to thread the manifest id into
/// <c>HttpContext.Items</c> for <see cref="AuditLoggingMiddleware"/>.
/// </para>
///
/// <para>
/// <strong>The flex ledger (S146 / TASK-14605, QUAL-188; rulings R-14, R-28, R-36, R-37)</strong>: no entry point
/// takes an opening flex balance any more. Admission derives it from the recorded ledger through the ONE function
/// <see cref="FlexLedgerOpening.Derive"/> (records through <see cref="IFlexLedgerReader"/>, employment windows over
/// <c>[the previous month's first day, this month's last day]</c>), after the existing checks and before any rule
/// call, for a month holding an EMPLOYED day; a refusal is <see cref="FlexLedgerRefusalException"/> (409). The
/// derived opening feeds the timeline call and <see cref="PlanInputs"/>; the result carries the opening, the
/// engine's clamped closing (<c>Flex.NewBalance</c>) and the provenance. The writing transactions re-run the same
/// function on in-transaction reads under the per-employee ledger lock
/// (<see cref="RecheckFlexOpeningUnderLockAsync"/>). Replay keeps the frozen opening and provenance.
/// </para>
/// </summary>
public sealed class PeriodCalculationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayrollMappingService _mappingService;
    // S146 TASK-14604 (QUAL-205; R-21, R-22): the ONE place a calculation's proof is written and read back. It
    // replaced the IEventStore + DbConnectionFactory pair, whose only uses were the manifest's direct append,
    // its projection insert and read, and the legacy event's direct append.
    private readonly ISegmentManifestStore _manifestStore;
    private readonly IRuleClassificationProvider _classificationProvider;
    // S21 TASK-2108 (ADR-017 D9c): optional repository for hydrating
    // BoundarySources.LocalProfileActivations on the back-compat shim path. Optional
    // because existing PCS unit tests construct PCS without DI; null-tolerant behavior
    // matches the IRuleClassificationProvider precedent (line 124). When null, the
    // shim emits no profile-activation boundaries — the same observable behavior as
    // pre-S21, so existing tests are unaffected.
    private readonly LocalAgreementProfileRepository? _localAgreementProfileRepo;
    // S33 TASK-3305 (ADR-023 D1): optional resolver for the dated employee_profiles
    // lookup at the segmentProfile construction site. Optional because existing PCS
    // unit-test fixtures construct the service directly (no DI); the legacy
    // copy-caller-profile path is preserved when this is null (refinement cycle 2
    // Codex W absorption — S29 _localAgreementProfileRepo precedent at line 86).
    private readonly IEmploymentProfileResolver? _profileResolver;
    // S137 TASK-13702 (ADR-040 D5/D7): optional resolver for the employee's employment
    // window(s). Hydrates BoundarySources.EmploymentStartedDates/EmploymentEndedDates and
    // types every planned segment EMPLOYED / NOT_EMPLOYED on the legacy shim path. Read
    // server-side inside this host (D7) — the EmploymentProfile wire DTO never carries the
    // dates. Optional per the _profileResolver precedent: when null (direct-construction
    // test fixtures) the planner receives null windows and the plan is byte-identical to
    // pre-S137 (every segment EMPLOYED, no employmentStatus key serialized).
    private readonly IEmploymentWindowResolver? _employmentWindowResolver;
    // S137 TASK-13702 (ADR-040 D5): optional repository for the employee_profiles
    // effective_from dates inside the period — activates the ADR-016 D5b-reserved
    // EmployeeProfileChange boundary (a mid-period part_time_fraction / position change
    // now splits the calculation so each span resolves its own dated profile). Same
    // null-tolerance as the two optional dependencies above.
    private readonly EmployeeProfileRepository? _employeeProfileRepo;
    // S144 TASK-14403 (QUAL-150 groundwork): optional repository for the user_agreement_codes
    // effective_from dates inside the period — feeds the AgreementCodeChange boundary, so a
    // mid-period agreement-code change splits (and, under the live rule set, REFUSES) the
    // calculation instead of paying the whole month under one agreement. Null-tolerant like
    // _employeeProfileRepo (ruled R2, S144); the host-level /recalculate pin
    // (PayrollHostRecalcBlockedTests) is what catches a dropped registration.
    private readonly UserAgreementCodeRepository? _userAgreementCodeRepo;
    // S145 TASK-14508 (rulings R14, R25): the ONE source of the hours a calculation consumes —
    // payroll reads the employee's recorded rows itself instead of taking them from its caller.
    // Optional in the constructor (fixtures that only PLAN — the raw routes' plan-only guard, the
    // reflection-driven planning tests — need no hours), but NEVER SILENT: every calculation and
    // replay entry point calls RequireHoursReader first, before any read, so a missing reader is a
    // named InvalidOperationException rather than a month paid as if nothing had been worked.
    private readonly IRecordedHoursReader? _hoursReader;
    // S145 TASK-14512 (rulings R5(b), R16, R21 as amended by R26): the months-in-order check's one read —
    // the approval status of the previous month. Optional in the constructor (planning-only instances and
    // fixtures for months that never reach a previous month need none), but NEVER SILENT: when a month's
    // opening week holds EMPLOYED days of the previous month and no reader is registered, the calculation
    // throws InvalidOperationException naming the registration (EnsurePriorMonthApprovedAsync). Replay
    // never consults it (R42).
    private readonly IPriorPeriodApprovalReader? _priorPeriodApprovalReader;
    // S146 TASK-14605 (QUAL-188; R-14, reviewer c3 W2): the flex ledger's one read — the employee's export records
    // with their balances. Optional in the constructor (plan-only instances, and calculations of a month with no
    // EMPLOYED day, need none), but NEVER SILENT: a calculation that reaches the opening derivation without it
    // throws InvalidOperationException naming the registration (RequireFlexLedgerReader). Defaulting to 0 would be
    // QUAL-188 again. Replay never consults it (R42): it keeps the frozen opening.
    private readonly IFlexLedgerReader? _flexLedgerReader;
    // S146 TASK-14605 (R-36): the in-transaction employment-window read the under-lock recheck uses over
    // [P.start, M.end]. Optional: without a self-managed window resolver the windows are null on BOTH reads
    // (implicit employment, R-37); with one, the recheck needs this surface — taken from here, else from the
    // self-managed resolver when it implements it too (the one concrete EmploymentWindowResolver implements both),
    // else the recheck throws naming the registration (never a silent windowless re-run).
    private readonly IEmploymentWindowResolverInTx? _employmentWindowResolverInTx;
    private readonly ILogger<PeriodCalculationService> _logger;
    private readonly string _ruleEngineUrl;

    /// <summary>
    /// Stream id pattern for the <see cref="SegmentManifestCreated"/> event. Keying by
    /// manifest id makes the event-replay fallback of the manifest load
    /// (<see cref="ISegmentManifestStore.TryLoadAsync"/>, called by
    /// <see cref="ReplayAsync(Guid, string?, Guid?, CancellationToken)"/>) a single
    /// <see cref="IEventStore.ReadStreamAsync"/> call rather than a full event-store scan. Since S146 /
    /// TASK-14604 the event is enqueued on this stream through the outbox, in the manifest's own transaction.
    /// </summary>
    internal static string ManifestStreamId(Guid manifestId) =>
        $"segment-manifest-{manifestId}";

    /// <summary>
    /// Stream id of the legacy <see cref="PeriodCalculationCompleted"/> event:
    /// <c>period-calc-{employeeId}-{periodStart:yyyy-MM-dd}</c>, unchanged since before S20. Since S146 /
    /// TASK-14604 it is enqueued through the outbox in the manifest's own transaction
    /// (<see cref="ISegmentManifestStore.PersistAsync"/>).
    /// </summary>
    internal static string PeriodCalculationCompletedStreamId(string employeeId, DateOnly periodStart) =>
        $"period-calc-{employeeId}-{periodStart:yyyy-MM-dd}";

    /// <summary>
    /// S145 / TASK-14513 (TASK-14511 Step 5a, Fable NOTE 2) — the evaluation-model marker frozen into
    /// <see cref="PlanInputs.EvaluationModel"/> for a plan that made NO rule-engine call: a plan with no
    /// EMPLOYED segment (every day of the period is outside the employment), which succeeds with no rule
    /// results and no export lines.
    ///
    /// <para>
    /// <b>Why a dedicated value (plain language).</b> The marker names the arithmetic that produced a result,
    /// and the Rule Engine returns it with each timeline call. A plan that makes no call has no engine answer
    /// to record, yet <see cref="PlanInputs.EvaluationModel"/> must hold something for the replay's
    /// <c>replay-evaluation-model-changed</c> check to compare. This value says exactly what happened:
    /// nothing was evaluated, so the (empty) result depends on no engine version at all. A replay of such a
    /// manifest again makes no call, records the same value, and matches. Recording the engine's CURRENT
    /// marker instead was rejected: payroll would have to hard-code a copy of the engine's constant (the
    /// marker lives in the Rule Engine, which payroll may not reference), and that copy would drift the day
    /// the engine moves to its next version, refusing replays of these manifests for nothing.
    /// </para>
    /// </summary>
    public const string NoEngineCallEvaluationModel = "no-engine-call";

    // S132 TASK-132-2b (QUAL-002): the single JsonSerializerOptions used to WRITE segments into
    // the segment_manifests projection (segments_jsonb) AND to READ them back (since S146 / TASK-14604 both
    // in PostgresSegmentManifestStore, which is why this field is internal rather than private; the test
    // suites still bind to it by reflection under its name, BindingFlags.NonPublic | Static).
    //
    // Why the JsonStringEnumConverter matters (auditability / correctness — ADR-016 D10, ADR-018):
    // the PlannedSegment.BoundaryCause enum previously serialized NUMERICALLY here (no converter),
    // e.g. "boundaryCause": 0. But the SegmentManifestCreated *event* is serialized by
    // EventSerializer, which DOES use JsonStringEnumConverter, so the immutable event — and any
    // projection row rebuilt from it (SegmentManifestProjectionRebuilder copies data->'segments'
    // verbatim) — encodes it as a STRING, e.g. "boundaryCause": "OkTransition". Two writers, two
    // encodings: a reader wired to one silently misread the other. Concretely, after a projection
    // rebuild (a first-class, always-supported operation) the manifest load's converter-less reader
    // (then PCS's LoadManifestAsync; since S146 PostgresSegmentManifestStore.TryLoadAsync) could not
    // parse the string form at all.
    //
    // The enforced contract this restores is DESERIALIZED EQUIVALENCE, not byte-identity: a
    // live-written and a rebuilt manifest deserialize to the SAME PlannedSegment set. Adding the
    // converter (no naming policy → PascalCase member names, matching EventSerializer) makes PCS's
    // writes STRING-encoded and enum-equivalent to the event/rebuild path, and allowIntegerValues:true
    // keeps the READER deliberately tolerant of BOTH encodings so a lingering LEGACY numeric row
    // (written by the old converter-less writer and not yet rebuilt) still round-trips — belt-and-
    // braces, no data loss.
    //
    // NOTE — a benign byte residual remains and is intentionally NOT closed here: PlannedSegment.Snapshot
    // is nullable and null is the common case. These options have no DefaultIgnoreCondition, so a
    // null snapshot serializes as "snapshot":null, whereas EventSerializer uses
    // JsonIgnoreCondition.WhenWritingNull and OMITS the key — and JSONB preserves explicit-null vs
    // absent-key. So a null-snapshot live row and its rebuilt twin are NOT byte-identical, but BOTH
    // deserialize back to Snapshot == null, so the deserialized-equivalence contract holds. Making
    // the two byte-identical would require adding WhenWritingNull HERE, but this is the SHARED
    // options object for the rule-engine HTTP payloads (EmploymentProfile / TimeEntry / …) — that
    // is a wire-format change out of QUAL-002's scope, deferred to its own task with its own tests.
    // No consumer depends on segments_jsonb byte-identity; the D10 manifest⋈audit join keys on
    // manifest_id.
    //
    // PARTIAL REVERSAL (S137 TASK-13702, 2026-09-02): the sentence above is now only half true.
    // The two-writer null-snapshot delta ("snapshot":null vs an absent key) REMAINS the deliberate
    // QUAL-146 residual — this object is also the rule-engine HTTP wire format and is NOT changed
    // here. But as of S137 the WINDOWLESS byte-shape of a segment IS pinned per writer by
    // SegmentSerializationParityTests: PlannedSegment.EmploymentStatus serializes as an ABSENT key
    // for EMPLOYED (WhenWritingDefault), so a windowless employee's segments_jsonb is byte-identical
    // to pre-S137 output under THESE options. Adding a DefaultIgnoreCondition here would now also
    // break that pin, not only the wire format.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) }
    };

    public PeriodCalculationService(
        IHttpClientFactory httpClientFactory,
        PayrollMappingService mappingService,
        ISegmentManifestStore manifestStore,
        IConfiguration configuration,
        ILogger<PeriodCalculationService> logger,
        IRuleClassificationProvider? classificationProvider = null,
        LocalAgreementProfileRepository? localAgreementProfileRepo = null,
        IEmploymentProfileResolver? profileResolver = null,
        IEmploymentWindowResolver? employmentWindowResolver = null,
        EmployeeProfileRepository? employeeProfileRepo = null,
        UserAgreementCodeRepository? userAgreementCodeRepo = null,
        IRecordedHoursReader? hoursReader = null,
        IPriorPeriodApprovalReader? priorPeriodApprovalReader = null,
        IFlexLedgerReader? flexLedgerReader = null,
        IEmploymentWindowResolverInTx? employmentWindowResolverInTx = null)
    {
        _httpClientFactory = httpClientFactory;
        _mappingService = mappingService;
        // S146 TASK-14604: not null-checked, like the mapping service above. A calculation on a service built
        // without a store fails its manifest write (logged at Error) and is refused as NotPersisted, never paid;
        // a replay on one fails loudly at the load.
        _manifestStore = manifestStore;
        _logger = logger;
        // The provider is the cross-domain seam to the Rule Engine's RuleRegistry. When
        // unwired (cross-domain dep — see file XML doc), we fall back to an empty provider
        // that logs a warning so the rule-side invariant pass is silenced rather than
        // exercised — the alternative (throwing) would break every existing endpoint.
        // TASK-2010 is expected to land the HTTP-backed provider; until then, callers who
        // construct PCS directly (tests) get the empty provider transparently.
        _classificationProvider = classificationProvider ?? EmptyRuleClassificationProvider.Instance;
        // S21 TASK-2108 (ADR-017 D9c): null-tolerant — when the repository is not
        // registered (legacy unit-test fixtures, environments without the S21 schema),
        // BuildPlanWithHistoriesAsync skips the activation lookup and passes an
        // empty LocalProfileActivations list. The 13 existing PCS unit tests that
        // construct PCS via `new(...)` continue to work without modification.
        _localAgreementProfileRepo = localAgreementProfileRepo;
        // S33 TASK-3305 (ADR-023 D1): null-tolerant — when the resolver is not
        // registered (legacy unit-test fixtures, environments without the S33
        // employee_profiles schema), the segmentProfile construction site below
        // falls back to copying the caller-supplied profile (pre-S33 behavior).
        // DI-registered code paths get fail-closed semantics per ADR-023 D3.
        _profileResolver = profileResolver;
        // S137 TASK-13702 (ADR-040 D5/D7): null-tolerant ONLY on the legacy fixture path —
        // when NO profile resolver is wired either, BuildPlanWithHistoriesAsync passes null
        // employment windows + null employment boundary dates to the planner, which is the
        // pre-S137 plan byte-for-byte.
        //
        // S137 Step-5a (Reviewer WARNING 1) — the two resolvers are COUPLED on the DI-wired
        // path. Plain-language: a null window resolver is not a harmless "feature off"; it
        // means the planner treats every employee as employed for the whole period, so a
        // leaver is paid for every day after their employment_end_date — a WRONG PAYROLL
        // VALUE, silently. If the Payroll host ever dropped the IEmploymentWindowResolver
        // registration, nothing would notice: the CI smoke probe's employee has NULL dates,
        // so its export would look identical. The profile resolver is the marker of the
        // DI-wired, fail-closed path (ADR-023 D3 — see its comment above); having it without
        // the window resolver is therefore always a wiring defect, never an intended mode,
        // and we refuse to construct so the host's FIRST calculation (and the smoke probe)
        // fails loudly instead of paying wrong. The EmployeeProfileRepository below stays
        // null-tolerant: its absence only loses EmployeeProfileChange boundaries (a coarser
        // plan, not an over-payment), so it is named in the message but not enforced.
        if (profileResolver is not null && employmentWindowResolver is null)
        {
            throw new InvalidOperationException(
                "PeriodCalculationService was constructed with an IEmploymentProfileResolver (the " +
                "DI-wired, fail-closed profile path) but WITHOUT an IEmploymentWindowResolver. On this " +
                "path a null window resolver is not a fallback — it plans every employee as EMPLOYED " +
                "for the whole period and re-pays leavers for days after their employment_end_date " +
                "(ADR-040 D5/D7). Fix the wiring: the Payroll host must register " +
                "EmploymentWindowResolver + IEmploymentWindowResolver (src/Integrations/" +
                "StatsTid.Integrations.Payroll/Program.cs — the AddSingleton<IEmploymentWindowResolver> " +
                "registration beside IEmploymentProfileResolver, L72-73 at the time of writing) and " +
                "EmployeeProfileRepository (L79 — optional: feeds the EmployeeProfileChange boundary). " +
                "Test fixtures that supply a profile resolver must supply a window resolver too.");
        }
        _employmentWindowResolver = employmentWindowResolver;
        // S137 TASK-13702 (ADR-040 D5): null-tolerant — when unregistered, no
        // EmployeeProfileChange boundaries are hydrated (pre-S137 behavior).
        _employeeProfileRepo = employeeProfileRepo;
        // S144 TASK-14403 (ruled R2): null-tolerant — when unregistered, no AgreementCodeChange
        // boundaries are hydrated (pre-S144 behavior). NOT coupled to the profile resolver with a
        // constructor throw like the window resolver above: that would need a new interface for a
        // repository the host already registers (Program.cs, for EmploymentProfileResolver). The
        // accepted cost — a dropped registration fails silently HERE — is covered at host level by
        // PayrollHostRecalcBlockedTests (a null repository → one segment → 200 instead of 422).
        _userAgreementCodeRepo = userAgreementCodeRepo;
        // S145 TASK-14508 (R25): NOT enforced here, deliberately — unlike the window resolver above,
        // whose absence is only ever a wiring defect, a PCS without a reader is a legitimate
        // plan-only instance (EnsurePeriodCalculableAsync, which the raw export routes call, never
        // reads hours). The never-silent check runs at call time instead (RequireHoursReader), on
        // every entry point that calculates or replays, before it reads anything.
        _hoursReader = hoursReader;
        // S145 TASK-14512 (R21/R26): NOT enforced here either, for the same reason — a plan-only instance,
        // and a calculation of a month whose opening week reaches no EMPLOYED day of the previous month
        // (June 2026 starts on a Monday; a starter hired on the 1st), legitimately need no reader. The
        // never-silent throw runs at call time, exactly when the answer is needed.
        _priorPeriodApprovalReader = priorPeriodApprovalReader;
        // S146 TASK-14605 (R-14): NOT enforced here either, for the same reason — a plan-only instance, and a
        // calculation of a month with no EMPLOYED day (no flex is settled), legitimately need no reader. The
        // never-silent throw runs at call time (RequireFlexLedgerReader), exactly when an opening is derived.
        _flexLedgerReader = flexLedgerReader;
        _employmentWindowResolverInTx = employmentWindowResolverInTx;
        _ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
    }

    /// <summary>
    /// S146 / TASK-14605 (QUAL-188; R-14 "optional but never silent") — the never-silent check for the flex ledger
    /// reader, called by the opening derivation (admission and the under-lock recheck) before it reads anything.
    ///
    /// <para>
    /// <b>Why it throws (plain language).</b> A month's opening flex balance comes only from the recorded ledger. A
    /// service built without a reader cannot know it; opening the month at 0 instead is exactly the defect S146
    /// removes (QUAL-188: a balance nobody recorded), so it refuses, by name.
    /// </para>
    /// </summary>
    private IFlexLedgerReader RequireFlexLedgerReader() =>
        _flexLedgerReader ?? throw new InvalidOperationException(
            "PeriodCalculationService was asked to derive the opening flex balance of a month holding an EMPLOYED " +
            "day, on a service constructed WITHOUT an IFlexLedgerReader. The opening comes only from the recorded " +
            "ledger (S146 R-14); opening the month at 0 instead would pay flex from a balance nobody recorded " +
            "(QUAL-188), so it is refused before anything is evaluated or written. Fix the wiring: the Payroll host " +
            "must register IFlexLedgerReader -> PostgresFlexLedgerReader in " +
            "src/Integrations/StatsTid.Integrations.Payroll/Program.cs (beside PayrollExportRecordRepository). Test " +
            "fixtures that calculate an employed month must pass an IFlexLedgerReader as the flexLedgerReader " +
            "constructor argument (a fake answering \"no ledger records\", declared with a comment, when the fixture " +
            "is not about the chain: the month then opens at LEDGER_START 0).");

    /// <summary>
    /// S145 / TASK-14508 (ruling R25) — the never-silent check for the hours source. Called FIRST by
    /// every public entry point that calculates or replays, before any read: before the rule
    /// classification fetch, the history and employment-window reads of planning, the manifest load
    /// and the hours read itself.
    ///
    /// <para>
    /// <b>Why it throws (plain language).</b> Since S145 payroll reads the employee's recorded hours
    /// itself; nothing else feeds a calculation. A service built without a reader would calculate a
    /// month on NO hours — a payslip as if nothing had been worked — so it refuses, by name, instead.
    /// </para>
    /// </summary>
    private IRecordedHoursReader RequireHoursReader(string entryPoint) =>
        _hoursReader ?? throw new InvalidOperationException(
            $"PeriodCalculationService.{entryPoint} was called on a service constructed WITHOUT an " +
            "IRecordedHoursReader. Payroll reads the employee's recorded hours itself (S145 R14/R25) — no " +
            "caller supplies them — so without a reader the calculation would run on no hours and pay the " +
            "month as if nothing had been worked. It is refused before anything is read. Fix the wiring: " +
            "the Payroll host must call builder.Services.AddProjectionHoursReader() in " +
            "src/Integrations/StatsTid.Integrations.Payroll/Program.cs (defined in Services/" +
            "ProjectionHoursReader.cs; it registers IRecordedHoursReader -> ProjectionHoursReader and the two " +
            "read-model repositories it reads). Test fixtures that calculate or replay must pass an " +
            "IRecordedHoursReader (an in-memory one is fine) as the hoursReader constructor argument.");

    // -------------------------------------------------------------------
    // Public outcome / audit-state types
    // -------------------------------------------------------------------

    /// <summary>
    /// Whether a single calculation run's proof (its manifest) was persisted. Since S146 / TASK-14604 (QUAL-205;
    /// rulings R-21, R-22) the manifest event, its <c>segment_manifests</c> projection row and the legacy
    /// <see cref="PeriodCalculationCompleted"/> are written in ONE transaction through the ADR-018 outbox
    /// (<see cref="ISegmentManifestStore.PersistAsync"/>), so there are three states, not the earlier five: a
    /// partial write (an event without its projection row, or the reverse) can no longer happen.
    /// <para>
    /// <b>What "provable" means after the commit (R-22).</b> The outbox rows and the projection row are durable
    /// together; the publisher then delivers each event to <c>events</c> at least once and in per-stream order
    /// (ADR-018 D4/D5), the same guarantee every other Payroll event has.
    /// </para>
    /// </summary>
    public enum AuditState
    {
        /// <summary>The manifest's one transaction committed: its outbox row, its projection row and the legacy
        /// event's outbox row are durable. The only state a calculation may be exported or corrected from. Default
        /// value.</summary>
        Complete,

        /// <summary>The commit of the manifest's one transaction was not confirmed (formerly <c>BothFailed</c>; R-22):
        /// it failed, or its acknowledgement was lost. Usually none of its three writes exists, but a lost
        /// acknowledgement can leave all three committed, so the state claims only that the proof cannot be relied on.
        /// The rule outputs are valid, but nothing could prove them later: both calculating routes refuse the
        /// calculation before any payroll write (503 <c>payroll-manifest-not-persisted</c>, through
        /// <see cref="PeriodCalculationOutcome.ThrowIfNotProvable"/>), which is safe either way: nothing is exported
        /// or corrected from it. The ManifestId carried alongside usually resolves in no store.</summary>
        NotPersisted,

        /// <summary>No manifest emission was attempted. Set on the refused calculation
        /// (any rule-engine call failed — typically Rule Engine outage, auth misconfiguration
        /// or a rejected input; S145 ruling R-6) and on replay (the original manifest already
        /// exists in the audit chain; replay re-evaluates rules but does NOT mint a new
        /// manifest event — ADR-016 D10 immutability). The ManifestId carried alongside
        /// this state references the in-memory plan; in the failure case it does not
        /// resolve in either store; in the replay case it resolves to the original
        /// forward-calc's persisted manifest. On a SUCCESSFUL forward calculation this state
        /// means no proof was written, so <see cref="PeriodCalculationOutcome.ThrowIfNotProvable"/>
        /// refuses it exactly as <see cref="NotPersisted"/>; replay never calls that predicate.</summary>
        NoManifest,
    }

    /// <summary>
    /// Wrapper surfacing the calculation result alongside its <see cref="AuditState"/>.
    /// Returned by <see cref="CalculateWithOutcomeAsync"/>; the legacy
    /// <see cref="CalculateAsync(PlannedCalculation, EmploymentProfile, string?, Guid?, CancellationToken)"/>
    /// returns just <see cref="PeriodCalculationResult"/> for backward compatibility but
    /// loses the <see cref="AuditState"/> signal.
    ///
    /// <para>
    /// <strong>Cross-domain dep (TASK-2010 / S20 wave 2)</strong>: prefer adding a nullable
    /// <c>AuditState</c> field to <see cref="PeriodCalculationResult"/> in SharedKernel,
    /// which would let us drop this wrapper entirely. Until that lands, callers needing the
    /// audit state must use this wrapper API.
    /// </para>
    ///
    /// <para>
    /// <strong>What the week-by-week evaluation ran on (S145 / TASK-14511, TASK-14513).</strong> The
    /// init-only members below record the per-plan lane's timeline call (the pieces sent, the settlement
    /// cutoff — R41 — and the engine's config fingerprints and evaluation-model marker) and, on a successful
    /// calculation, the complete <see cref="PlanInputs"/> frozen onto its manifest. They are not positional,
    /// so every existing construction and deconstruction of this record is unchanged.
    /// </para>
    /// </summary>
    public sealed record PeriodCalculationOutcome(
        PeriodCalculationResult Result,
        AuditState AuditState,
        Guid ManifestId)
    {
        /// <summary>
        /// The pieces the per-plan lane sent to <c>POST /api/rules/evaluate-timeline</c>: one per
        /// EMPLOYED segment of the plan, preceded by the pieces for the EMPLOYED context days of the
        /// month's opening week. Empty when the plan has no EMPLOYED segment (no call was made).
        /// </summary>
        public IReadOnlyList<FrozenPiece> TimelinePieces { get; init; } = Array.Empty<FrozenPiece>();

        /// <summary>
        /// The settlement cutoff sent with the timeline (ruling R41): the employment window's inclusive
        /// <c>End</c> when it is on or before the period end, otherwise <c>null</c>. Frozen into
        /// <see cref="PlanInputs.SettlementCutoff"/>, because it cannot be recovered from the plan.
        /// </summary>
        public DateOnly? SettlementCutoff { get; init; }

        /// <summary>
        /// The config fingerprints the Rule Engine returned for the timeline call. Empty when no call
        /// was made or the call failed.
        /// </summary>
        public IReadOnlyList<string> ConfigFingerprints { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The evaluation-model marker the Rule Engine returned for the timeline call (for example
        /// <c>weekly-timeline-v2</c>). <c>null</c> when no call was made or the call failed.
        /// </summary>
        public string? EvaluationModel { get; init; }

        /// <summary>
        /// S145 / TASK-14512 (Do 7) — the previous month's pay period that admission required to be
        /// APPROVED (the month's opening week holds EMPLOYED days of it), or <c>null</c> when the check did
        /// not apply (a month opening on a Monday, a starter with no earlier EMPLOYED day, and every
        /// replay). <c>/calculate-and-export</c> hands it to the export, which re-checks that approval
        /// under a row lock inside its own transaction, so a concurrent reopen of the previous month
        /// cannot slip between this check and the export's commit.
        /// </summary>
        public (DateOnly PeriodStart, DateOnly PeriodEnd)? PriorMonthApprovalRequired { get; init; }

        /// <summary>
        /// S145 / TASK-14513 (refinement A7) — the frozen inputs of this calculation: exactly what a forward
        /// calculation wrote onto its manifest, its <see cref="SegmentManifestCreated"/> event and
        /// <c>plan_inputs_jsonb</c> (the same record, whatever the audit state of those writes). <c>null</c>
        /// on a refused calculation (a rule-engine call failed, S145 ruling R-6), which freezes and writes
        /// nothing. A replay's outcome carries what the replay ran on, which is what it compares with the
        /// manifest's record.
        /// </summary>
        public PlanInputs? PlanInputs { get; init; }

        /// <summary>
        /// S146 / TASK-14605 (QUAL-188; R-14) — the opening admission derived from the recorded ledger
        /// (<see cref="FlexLedgerOpening.Derive"/>): the balance, its provenance (<c>LEDGER_START</c>, <c>CHAINED</c>,
        /// <c>REHIRE</c> or <c>UNTRACKED</c>) and the ledger start S the derivation saw. <c>null</c> when the month holds
        /// no EMPLOYED day (no flex is settled, so the function is not called and no record is written) and on every
        /// replay (R42). The writing transaction re-runs the derivation under the ledger lock and requires this same
        /// (opening, provenance) pair (<see cref="RecheckFlexOpeningUnderLockAsync"/>).
        /// </summary>
        public FlexOpening? FlexOpening { get; init; }

        /// <summary>
        /// S146 / TASK-14604 (QUAL-205; rulings R-22, R-27) — THE refusal predicate both calculating routes call
        /// before any payroll write: throws <see cref="ManifestNotPersistedException"/> when this outcome cannot be
        /// proven later, and returns otherwise.
        /// <list type="bullet">
        /// <item><see cref="AuditState.NotPersisted"/>: the commit of the manifest's one transaction was not confirmed;</item>
        /// <item><see cref="AuditState.NoManifest"/> with <c>Result.Success == true</c>: a successful forward
        /// calculation that wrote no manifest at all.</item>
        /// </list>
        /// A refused calculation (<c>Success == false</c>, <see cref="AuditState.NoManifest"/>) passes: it carries no
        /// figure, and the routes answer it with their own 422. <see cref="AuditState.Complete"/> passes.
        ///
        /// <para>
        /// <b>Who calls it.</b> <c>/api/payroll/calculate-and-export</c>, right after the calculation and before
        /// <see cref="StampAuditContext"/> and the export; <see cref="RetroactiveCorrectionService"/>, right after
        /// the recalculation and before its correction transaction opens. Replay NEVER calls it:
        /// <see cref="AuditState.NoManifest"/> with success is a replay's normal state (it mints no manifest,
        /// ADR-016 D10). One predicate, so the two routes cannot drift apart on what "provable" means.
        /// </para>
        /// </summary>
        /// <exception cref="ManifestNotPersistedException">The outcome is not provable; see above.</exception>
        public void ThrowIfNotProvable()
        {
            if (AuditState == AuditState.NotPersisted)
            {
                throw new ManifestNotPersistedException(
                    ManifestId,
                    $"The manifest {ManifestId} of this calculation was not persisted (audit state NotPersisted): the " +
                    "commit of its one transaction (manifest event, projection row, legacy event) was not confirmed, so " +
                    "the result is not provable and is refused before any payroll write.");
            }

            if (AuditState == AuditState.NoManifest && Result.Success)
            {
                throw new ManifestNotPersistedException(
                    ManifestId,
                    $"The calculation for manifest {ManifestId} succeeded without writing a manifest (audit state " +
                    "NoManifest on a successful result: a forward calculation run with emitAuditEvents:false), so the " +
                    "result is not provable and is refused before any payroll write.");
            }
        }
    }

    // -------------------------------------------------------------------
    // Sole logic entry point — D8 always-on planner
    // -------------------------------------------------------------------

    /// <summary>
    /// Sole logic entry point for segmented calculation (ADR-016 D1, D8). Per-segment rule
    /// evaluation, strategy-driven merge, manifest emission, payroll line mapping. The
    /// <paramref name="plan"/> must come from <see cref="PeriodPlanner.Plan"/> (the planner
    /// is the only constructor of <see cref="PlannedCalculation"/>; the type system enforces
    /// that callers cannot bypass).
    ///
    /// <para>
    /// Returns just <see cref="PeriodCalculationResult"/>; callers that need the
    /// <see cref="AuditState"/> signal should use
    /// <see cref="CalculateWithOutcomeAsync(PlannedCalculation, EmploymentProfile, string?, Guid?, bool, CancellationToken)"/>.
    /// </para>
    ///
    /// <para>
    /// S145 / TASK-14508 (R14, R25): takes no hours. The entry point it re-enters reads the
    /// employee's current recorded rows itself; a service built without an
    /// <see cref="IRecordedHoursReader"/> throws there before anything is read.
    /// </para>
    ///
    /// <para>
    /// S146 / TASK-14605 (R-14): takes no opening flex balance either; admission derives it from the ledger.
    /// </para>
    /// </summary>
    public async Task<PeriodCalculationResult> CalculateAsync(
        PlannedCalculation plan,
        EmploymentProfile profile,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var outcome = await CalculateWithOutcomeAsync(
            plan, profile,
            authorizationHeader, correlationId,
            ct: ct);
        return outcome.Result;
    }

    /// <summary>
    /// Sole logic entry point — same semantics as
    /// <see cref="CalculateAsync(PlannedCalculation, EmploymentProfile, string?, Guid?, CancellationToken)"/>
    /// but additionally returns an explicit <see cref="AuditState"/> describing the
    /// completeness of the manifest-persistence chain.
    ///
    /// <para>
    /// <strong>Admission, then evaluation (S145 / TASK-14512, ruling R42).</strong> A forward calculation
    /// of a caller-built plan. This entry point runs <em>admission</em> first: it reads the employee
    /// histories the plan does not carry (the employment window and the profile and agreement-code change
    /// dates), runs the week check (<see cref="PeriodNotCalculableException"/>) and the months-in-order check
    /// (<see cref="PriorMonthNotApprovedException"/>, never silent about a missing
    /// <see cref="IPriorPeriodApprovalReader"/>), and reads the hours. Then it runs <em>evaluation</em>.
    /// Replay does not come through here: it enters evaluation directly, so it is never refused for what is
    /// true today.
    /// </para>
    ///
    /// <para>
    /// <strong>The hours (S145 / TASK-14508, rulings R14 and R25).</strong> Admission reads the
    /// hours the calculation evaluates: the employee's CURRENT time-entry and absence rows dated in
    /// [the day before the opening week's Monday, <see cref="PlannedCalculation.PeriodEnd"/>], through
    /// <see cref="IRecordedHoursReader.ReadCurrentAsync"/>. The opening week is the Monday–Sunday week
    /// holding <see cref="PlannedCalculation.PeriodStart"/>; the extra day before its Monday catches a
    /// night shift filed on that day whose after-midnight hours land on the Monday (N5, the Compliance
    /// precedent). The rows are kept exactly as read (evaluation freezes their event ids and lossless
    /// digests into the manifest's <see cref="PlanInputs"/>, TASK-14513) and mapped
    /// once to the <see cref="TimeEntry"/> / <see cref="AbsenceEntry"/> inputs the lanes consume. A
    /// service built without a reader throws <see cref="InvalidOperationException"/> before any read.
    /// </para>
    ///
    /// <para>
    /// <strong>Two lanes (S145 / TASK-14511, the QUAL-186 fix).</strong> Overtime / merarbejde, the
    /// norm check and flex are evaluated ONCE for the whole plan, week by week, by one
    /// <c>POST /api/rules/evaluate-timeline</c> call (the per-plan lane). Its timeline covers every
    /// EMPLOYED day of the plan plus the EMPLOYED "context days" of the month's opening week (the
    /// previous month's days of a week this month settles, owner ruling Q3 = F1), and carries the
    /// settlement cutoff (R41). Supplement, on-call and absence stay per segment (the per-segment
    /// lane). The per-plan results are joined to the EMPLOYED segment's result list, so the merge and
    /// the wage-type mapping see exactly one result per rule per EMPLOYED segment, as before.
    /// </para>
    ///
    /// <para>
    /// <strong>The opening flex balance (S146 / TASK-14605, R-14).</strong> No caller hands it in any more:
    /// admission derives it from the recorded ledger (<see cref="FlexLedgerOpening.Derive"/>) and may refuse with
    /// <see cref="FlexLedgerRefusalException"/>; the outcome carries it as <see cref="PeriodCalculationOutcome.FlexOpening"/>.
    /// </para>
    /// </summary>
    public async Task<PeriodCalculationOutcome> CalculateWithOutcomeAsync(
        PlannedCalculation plan,
        EmploymentProfile profile,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        bool emitAuditEvents = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);

        // S145 TASK-14508 (R25) — never silent, and FIRST: before the history and hours reads and the
        // rule-classification fetch. (The planless shims and the replay overloads run the same check
        // before THEIR first read — planning or the manifest load.)
        var hoursReader = RequireHoursReader(nameof(CalculateWithOutcomeAsync));

        // The plan's own integrity, before anything is read: a programmer error, never a month to refuse.
        AssertPlanMatchesProfile(plan, profile);

        // ── Admission (R42). A caller-built plan arrives without the histories its planning read, so
        // admission reads them here. TASK-14512 hoisted this read out of evaluation (TASK-14511 Step 5a,
        // Fable NOTE 6): evaluation reads no history, so a replay entering evaluation does not reach it
        // (since TASK-14513 a replay's inputs are the frozen PlanInputs of its manifest). The planless shims
        // hand over the read that fed their planner instead, so no history is read twice in one calculation.
        var histories = await ReadPlanHistoriesAsync(plan.EmployeeId, plan.PeriodStart, plan.PeriodEnd, ct);
        var admitted = await AdmitAsync(plan, histories, hoursReader, ct);

        // ── Evaluation.
        return await EvaluateAdmittedAsync(
            admitted, profile, authorizationHeader, correlationId, emitAuditEvents, ct);
    }

    // -------------------------------------------------------------------
    // Admission — S145 TASK-14512 (rulings R42, N1)
    // -------------------------------------------------------------------

    /// <summary>
    /// What admission hands to evaluation: the plan; the histories its checks and the timeline pieces are
    /// built from; the hours rows exactly as read; the previous month's pay period admission required to
    /// be APPROVED (<c>null</c> when the check did not apply); and the opening flex balance derived from the
    /// ledger (S146 / TASK-14605; <c>null</c> when the month holds no EMPLOYED day).
    /// </summary>
    private sealed record AdmittedCalculation(
        PlannedCalculation Plan,
        PlanHistories Histories,
        RecordedHours Hours,
        (DateOnly PeriodStart, DateOnly PeriodEnd)? PriorMonthApproved,
        FlexOpening? FlexOpening);

    /// <summary>
    /// The admission step that follows planning, in exactly the refusal order N1 requires:
    /// <list type="number">
    /// <item>the <b>week check</b> (Q5 (c)): an agreement change inside a week the month settles, on a day
    /// other than its Monday → <see cref="PeriodNotCalculableException"/>, the second 422;</item>
    /// <item><b>months in order</b> (R5(b), R16, R21/R26): the previous month must be APPROVED when the
    /// opening week holds its EMPLOYED days → <see cref="PriorMonthNotApprovedException"/>, the 409 (or
    /// <see cref="InvalidOperationException"/> when no <see cref="IPriorPeriodApprovalReader"/> is
    /// registered);</item>
    /// <item>the <b>opening flex balance</b> (S146 / TASK-14605, R-14): derived from the recorded ledger by
    /// <see cref="FlexLedgerOpening.Derive"/> for a month holding an EMPLOYED day →
    /// <see cref="FlexLedgerRefusalException"/>, a 409 (<c>payroll-export-out-of-order</c> or
    /// <c>payroll-prior-month-not-recorded</c>), or <see cref="InvalidOperationException"/> when no
    /// <see cref="IFlexLedgerReader"/> is registered;</item>
    /// <item>the <b>hours read</b> (<see cref="IRecordedHoursReader.ReadCurrentAsync"/>).</item>
    /// </list>
    /// The planner's own refusal, the first 422, has already happened: it is raised while the plan is built,
    /// before this method runs. Nothing here writes anything, so every refusal leaves no trace: no manifest,
    /// no export record, no lock, no correction event.
    ///
    /// <para>
    /// <b>Why the 422s come before the 409 (plain language).</b> The 422s describe the month's own data,
    /// which approving another month cannot fix; the 409 describes a sequencing state the operator can fix.
    /// Reporting the month's own problem first saves the operator a detour.
    /// </para>
    /// </summary>
    private async Task<AdmittedCalculation> AdmitAsync(
        PlannedCalculation plan,
        PlanHistories histories,
        IRecordedHoursReader hoursReader,
        CancellationToken ct)
    {
        EnsureNoMidWeekAgreementChange(plan, histories);

        var priorMonth = await EnsurePriorMonthApprovedAsync(plan, histories, ct);

        // S146 TASK-14605 (QUAL-188; R-14, R-28) — the opening flex balance, after the existing checks and before
        // the hours read and any rule call. Nobody hands a calculation its opening any more: it is derived from the
        // recorded ledger by the one function, on self-managed reads (no lock is held while the engine is called);
        // the writing transaction re-runs it under the ledger lock. A refusal (409) writes nothing.
        var flexOpening = await DeriveFlexOpeningAtAdmissionAsync(plan, ct);

        // ---------------------------------------------------------------
        // S145 TASK-14508 (R14) — payroll reads its own hours. Nobody hands a calculation its
        // hours any more: it reads the employee's CURRENT recorded rows (one current row per Skema
        // cell plus every additive registration; a re-saved cell's earlier row is superseded and
        // not read) for [the day before the opening week's Monday, period end]. Reading the whole
        // opening week means the days of the month's first week that lie in the PREVIOUS month
        // ("context days") are present, so a missing one can no longer be mistaken for a day not
        // worked; the day before that Monday catches a night shift whose after-midnight hours land
        // on the Monday (N5 — the same widened read as ComplianceEndpoints, ADR-039 GAP-B).
        //
        // The rows are kept exactly as read: evaluation freezes their event ids, the highest outbox id and
        // their lossless digests into the manifest's PlanInputs (TASK-14513). Since TASK-14512 this is
        // admission's last step (R42). A replay never runs admission and never reads the current rows: it
        // re-reads the recorded rows by event id (ReadByEventIdsAsync).
        // ---------------------------------------------------------------
        var recordedHours = await hoursReader.ReadCurrentAsync(
            plan.EmployeeId, HoursReadStart(plan.PeriodStart), plan.PeriodEnd, ct);

        return new AdmittedCalculation(plan, histories, recordedHours, priorMonth, flexOpening);
    }

    /// <summary>
    /// Evaluation of an admitted calculation: derives the evaluation inputs from the histories admission
    /// read (<see cref="DeriveEvaluationInputsAsync"/>), runs <see cref="EvaluateAsync"/> on the opening
    /// admission derived, and records on the outcome the previous month admission required to be APPROVED,
    /// which <c>/calculate-and-export</c> hands to the export for its row-locked recheck (TASK-14512 Do 7),
    /// and the derived opening (S146 / TASK-14605), which both writing routes re-check under the ledger lock.
    /// </summary>
    private async Task<PeriodCalculationOutcome> EvaluateAdmittedAsync(
        AdmittedCalculation admitted,
        EmploymentProfile profile,
        string? authorizationHeader,
        Guid? correlationId,
        bool emitAuditEvents,
        CancellationToken ct)
    {
        var inputs = await DeriveEvaluationInputsAsync(admitted.Plan, admitted.Histories, profile, ct);

        // S146 TASK-14605: a month with no EMPLOYED day makes no timeline call, so its opening is never used; it
        // freezes 0 with no provenance, and its result carries no balance.
        var opening = admitted.FlexOpening;

        // S145 R-12: evaluation takes the employee id only. The caller's profile fed the derivation above
        // (the fallback where no dated record is resolved); the result reports what the pieces ran under.
        var outcome = await EvaluateAsync(
            admitted.Plan, inputs, admitted.Hours, profile.EmployeeId,
            opening?.Balance ?? 0m, opening?.Provenance,
            authorizationHeader, correlationId, emitAuditEvents, ct);

        return outcome with
        {
            PriorMonthApprovalRequired = admitted.PriorMonthApproved,
            FlexOpening = opening,
        };
    }

    // -------------------------------------------------------------------
    // The flex ledger — S146 TASK-14605 (QUAL-188; R-14, R-28, R-36, R-37)
    // -------------------------------------------------------------------

    /// <summary>
    /// The writing route a recheck runs for: it selects the <c>payroll-ledger-changed</c> sentence (R-14).
    /// </summary>
    public enum FlexLedgerRoute
    {
        /// <summary><c>/calculate-and-export</c>, through <see cref="PayrollExportService.ExportAsync"/>.</summary>
        Export,

        /// <summary><c>/recalculate</c>, through <see cref="RetroactiveCorrectionService"/>.</summary>
        Correction,
    }

    /// <summary>
    /// Admission's half of R-14 (C2 "PCS owns the derivation"): the opening of the plan's month M, on SELF-MANAGED
    /// reads — the employee's export records (<see cref="IFlexLedgerReader.ReadRecordsAsync(string, CancellationToken)"/>),
    /// read FIRST, then the employment windows over
    /// <see cref="FlexLedgerOpening.WindowsReadRange(DateOnly, DateOnly?)"/> with the ledger start S those records give
    /// (S147 R-13: <c>[min(S, P).start, M.end]</c>, or <c>[P.start, M.end]</c> without an S;
    /// <see cref="IEmploymentWindowResolver.GetWindowsAsync"/>; R-28: the derivation's own read, never the plan's
    /// <c>[periodStart, periodEnd]</c> histories, which can miss a spell that ended inside P). Returns <c>null</c>
    /// without reading anything when the plan holds no EMPLOYED segment (the function's precondition: no flex is
    /// settled and no record is written). Without a window resolver the windows are <c>null</c> and every day is
    /// EMPLOYED (R-37). <see cref="FlexLedgerOpening.Derive(DateOnly, IReadOnlyList{FlexLedgerRecord}, IReadOnlyList{EmploymentWindow})"/>
    /// walks the recorded chain from S to P before it derives M (S147 TASK-14700, R-2), so this is where both
    /// calculating routes, and every re-run or correction of a later month, refuse a month whose chain is stale. A
    /// refusal throws <see cref="FlexLedgerRefusalException"/> (<c>payroll-export-out-of-order</c>,
    /// <c>payroll-prior-month-not-recorded</c> or <c>payroll-prior-month-stale</c>) before any rule call; a missing
    /// reader throws <see cref="InvalidOperationException"/> (<see cref="RequireFlexLedgerReader"/>). M is the
    /// calendar month of the period start, the month the export record is keyed by.
    /// </summary>
    private async Task<FlexOpening?> DeriveFlexOpeningAtAdmissionAsync(PlannedCalculation plan, CancellationToken ct)
    {
        // The same predicate as the timeline call's (EvaluateAsync's employedIndices): a segment that is not
        // NOT_EMPLOYED is evaluated, so the month settles flex exactly when this holds.
        if (!plan.Segments.Any(s => s.EmploymentStatus != EmploymentWindowStatus.NOT_EMPLOYED))
            return null;

        var reader = RequireFlexLedgerReader();
        var month = FlexLedgerOpening.MonthStart(plan.PeriodStart);
        var records = await reader.ReadRecordsAsync(plan.EmployeeId, ct);

        IReadOnlyList<EmploymentWindow>? windows = null;
        if (_employmentWindowResolver is not null)
        {
            // S147 R-13: the range takes S from the records just read, so the walk can test every month from S to P.
            var (from, to) = FlexLedgerOpening.WindowsReadRange(month, FlexLedgerOpening.LedgerStartOf(records));
            windows = await _employmentWindowResolver.GetWindowsAsync(plan.EmployeeId, from, to, ct);
        }

        return FlexLedgerOpening.Derive(month, records, windows).OpeningOrThrow(plan.EmployeeId);
    }

    /// <summary>
    /// S146 / TASK-14605 (R-14 "the under-lock recheck"; R-36) — the writing transaction's half: the SAME function
    /// re-run inside the caller's <paramref name="conn"/> + <paramref name="tx"/>, which must already hold the
    /// employee's ledger lock (<see cref="PayrollExportRecordRepository.AcquireFlexLedgerLocksAsync"/>, the first
    /// statement of every writer). Records through the reader's in-transaction surface, read FIRST; windows through
    /// <see cref="IEmploymentWindowResolverInTx.GetWindowsAsync(NpgsqlConnection, NpgsqlTransaction, string, DateOnly, DateOnly, CancellationToken)"/>
    /// over the same helper as admission, <see cref="FlexLedgerOpening.WindowsReadRange(DateOnly, DateOnly?)"/> with the
    /// S of the in-transaction records (S147 R-13; null on both reads when no window resolver is wired, R-37).
    ///
    /// <para>
    /// The write proceeds only when the function returns no refusal and the same (opening, provenance) pair as
    /// <paramref name="used"/>, the pair the calculation ran on; otherwise this throws
    /// <see cref="FlexLedgerRefusalException"/> <c>payroll-ledger-changed</c> and the caller's transaction rolls back.
    /// The function walks the recorded chain from S to P here too (S147 R-2), so a correction of an earlier month that
    /// commits between admission and this recheck, making a month before M stale, is caught here as
    /// <c>payroll-ledger-changed</c> (the diagnostic names the inner <c>payroll-prior-month-stale</c>); the caller's
    /// retry then refuses at admission with <c>payroll-prior-month-stale</c>, naming the month to recalculate (R-8).
    /// A correction compares against ITS OWN calculation's pair, never M's recorded values: the point of a
    /// correction is that M's recorded opening may be stale. With provenance in the compare, two concurrent first
    /// exports of consecutive months that both computed <c>LEDGER_START 0</c> cannot both be written: the later
    /// month re-derives <c>CHAINED</c> here and refuses, and its retry chains.
    /// </para>
    /// </summary>
    public async Task RecheckFlexOpeningUnderLockAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        DateOnly month,
        FlexOpening used,
        FlexLedgerRoute route,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(used);

        var reader = RequireFlexLedgerReader();
        var monthStart = FlexLedgerOpening.MonthStart(month);
        var records = await reader.ReadRecordsAsync(conn, tx, employeeId, ct);

        IReadOnlyList<EmploymentWindow>? windows = null;
        if (_employmentWindowResolver is not null)
        {
            var inTx = _employmentWindowResolverInTx
                ?? _employmentWindowResolver as IEmploymentWindowResolverInTx
                ?? throw new InvalidOperationException(
                    "PeriodCalculationService was asked to re-check a month's opening flex balance under the ledger " +
                    "lock with an IEmploymentWindowResolver but WITHOUT an IEmploymentWindowResolverInTx. Admission read " +
                    "the employment windows, so the recheck must read them too, inside the writing transaction; re-running " +
                    "without them would treat every day as employed and could accept an opening admission refused, so it " +
                    "is refused instead. Fix the wiring: the Payroll host must register IEmploymentWindowResolverInTx from " +
                    "the EmploymentWindowResolver singleton in src/Integrations/StatsTid.Integrations.Payroll/Program.cs " +
                    "(beside IEmploymentWindowResolver), and pass it as the employmentWindowResolverInTx constructor " +
                    "argument (or use a window resolver that implements both surfaces).");
            // S147 R-13: the same helper as admission, with S from the in-transaction records read above.
            var (from, to) = FlexLedgerOpening.WindowsReadRange(monthStart, FlexLedgerOpening.LedgerStartOf(records));
            windows = await inTx.GetWindowsAsync(conn, tx, employeeId, from, to, ct);
        }

        var decision = FlexLedgerOpening.Derive(monthStart, records, windows);
        if (decision.Opening is { } rederived && rederived.SamePairAs(used))
            return;

        var sentence = route == FlexLedgerRoute.Correction
            ? FlexLedgerOpening.LedgerChangedCorrectionSentence
            : FlexLedgerOpening.LedgerChangedExportSentence;
        var now = decision.Opening is { } o
            ? $"{o.Provenance} {o.Balance.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"a refusal ({decision.Refusal!.Kind})";
        throw new FlexLedgerRefusalException(
            FlexLedgerRefusalException.LedgerChanged, monthStart, sentence,
            $"Employee '{employeeId}', {monthStart.ToString(FlexLedgerRefusalException.MonthFormat, System.Globalization.CultureInfo.InvariantCulture)}: " +
            $"the calculation ran on {used.Provenance} {used.Balance.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"but under the ledger lock the ledger now gives {now} ({route} route). Nothing was written.");
    }

    /// <summary>
    /// S146 / TASK-14605 (R-3, R-14, R-16) — what <c>/calculate-and-export</c> hands
    /// <see cref="PayrollExportService.ExportAsync"/> about the month's flex balance
    /// (<see cref="PayrollExportContext.FlexLedger"/>): the record's identity (the employee and the calendar month
    /// of the period start), the derived opening and provenance, the engine's clamped closing, and the under-lock
    /// recheck bound to this service and the derived pair. <c>null</c> when the month holds no EMPLOYED day (no flex
    /// is settled and no record is written, ADR-034 D3). Under <c>UNTRACKED</c> both balances are <c>null</c>.
    /// </summary>
    public FlexLedgerWrite? FlexLedgerWriteFor(PeriodCalculationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.FlexOpening is not { } opening)
            return null;

        var result = outcome.Result;

        // S146 R-47 — the writer's invariant: a ledger write is for one whole calendar month (the record's key; its closing
        // is the month's, which the next month opens from). /calculate-and-export refuses a partial period with 400
        // payroll-period-not-a-month before the calculation; reaching this with one is a programmer error.
        if (!FlexLedgerOpening.IsWholeCalendarMonth(result.PeriodStart, result.PeriodEnd))
        {
            throw new InvalidOperationException(
                $"PeriodCalculationService.FlexLedgerWriteFor: the period " +
                $"[{result.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.." +
                $"{result.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}] " +
                $"for employee '{result.EmployeeId}' is not one whole calendar month, so its closing is not the month's and " +
                "must not be recorded as it (R-47). Nothing was written.");
        }

        var employeeId = result.EmployeeId;
        var month = FlexLedgerOpening.MonthStart(result.PeriodStart);
        return new FlexLedgerWrite
        {
            EmployeeId = employeeId,
            Year = month.Year,
            Month = month.Month,
            Provenance = opening.Provenance,
            OpeningBalance = result.OpeningFlexBalance,
            ClosingBalance = result.ClosingFlexBalance,
            RecheckUnderLock = (conn, tx, ct) => RecheckFlexOpeningUnderLockAsync(
                conn, tx, employeeId, month, opening, FlexLedgerRoute.Export, ct),
        };
    }

    // -------------------------------------------------------------------
    // Evaluation — the two lanes over a plan, its pieces, a cutoff, an opening balance and hours rows
    // -------------------------------------------------------------------

    /// <summary>
    /// What evaluation runs on besides the plan, the opening balance and the hours rows (R42):
    /// <list type="bullet">
    /// <item><see cref="Pieces"/> — the timeline pieces: the EMPLOYED context days of the opening week,
    /// then one per EMPLOYED segment, each with one fraction, agreement and position;</item>
    /// <item><see cref="SegmentProfiles"/> — each plan segment's dated profile, in segment order
    /// (<c>null</c> for a NOT_EMPLOYED segment, which is skipped), which the per-segment lane and the
    /// wage-type mapping use;</item>
    /// <item><see cref="SettlementCutoff"/> — the leaver's settlement cutoff (R41), or <c>null</c>.</item>
    /// </list>
    /// A forward calculation derives them from the histories admission read
    /// (<see cref="DeriveEvaluationInputsAsync"/>); a replay builds them from its manifest's frozen
    /// <see cref="PlanInputs"/> alone (<see cref="FrozenEvaluationInputs"/>, TASK-14513).
    /// </summary>
    private sealed record EvaluationInputs(
        IReadOnlyList<FrozenPiece> Pieces,
        IReadOnlyList<EmploymentProfile?> SegmentProfiles,
        DateOnly? SettlementCutoff);

    /// <summary>
    /// Derives the <see cref="EvaluationInputs"/> from the employee histories: "employed?" before "what
    /// profile?" (ADR-040 D10), and every profile BEFORE any rule-engine call, so a missing employment
    /// record refuses the month (<c>employment_record_gap</c>) before anything is evaluated. The context
    /// days' pieces come first (they precede the period); then each EMPLOYED segment's dated profile —
    /// the one planning already resolved for the segment's wage-type key when there is one, so the engine
    /// and the export see the same record (A4) — which both lanes use.
    /// </summary>
    private async Task<EvaluationInputs> DeriveEvaluationInputsAsync(
        PlannedCalculation plan,
        PlanHistories histories,
        EmploymentProfile profile,
        CancellationToken ct)
    {
        var contextPieces = await BuildContextPiecesAsync(plan, histories, profile, ct);

        var pieces = new List<FrozenPiece>(contextPieces.Count + plan.Segments.Count);
        pieces.AddRange(contextPieces);
        var segmentProfiles = new EmploymentProfile?[plan.Segments.Count];

        for (int s = 0; s < plan.Segments.Count; s++)
        {
            var segment = plan.Segments[s];

            // ---------------------------------------------------------------
            // ADR-040 D5 / D10 (S137 TASK-13702) — the NOT_EMPLOYED skip point. It runs BEFORE
            // the profile resolution below: callers ask "employed?" before "what profile?" (D10).
            // A post-S136 employee's profile row starts at hire, so resolving a profile for a
            // pre-hire segment would hit the resolver's fail-closed path
            // (EmployeeProfileNotFoundException → a real 500).
            //
            // What a NOT_EMPLOYED segment does NOT do: no profile resolution, no rule evaluation
            // in either lane (no piece of the timeline covers its days, and it makes zero
            // per-segment calls), no export-line mapping. What it DOES do: contribute an EMPTY
            // rule-result list and an EMPTY export-line list at its own index — never a
            // synthesized row, which would perturb MergePerSegmentRuleResults (it seeds rule order
            // from the first non-empty segment). The segment itself stays in plan.Segments, so
            // BuildManifest still records it — non-employment is structurally explicit in the
            // audit-of-record (D5), never encoded as "norm 0".
            // ---------------------------------------------------------------
            if (segment.EmploymentStatus == EmploymentWindowStatus.NOT_EMPLOYED)
                continue;

            // Caller-supplied profile.OkVersion may be stale (pre-S20 callers commonly send
            // a single OK version even for straddling periods). The profile used for the segment
            // carries the date-resolved value (TryResolveDatedProfileAsync).
            var segmentOkVersion = OkVersionResolver.ResolveVersion(segment.StartDate);
            if (!string.Equals(profile.OkVersion, segmentOkVersion, StringComparison.Ordinal) && plan.Segments.Count == 1)
            {
                _logger.LogWarning(
                    "Caller-supplied profile.OkVersion '{Supplied}' differs from segment-resolved '{Resolved}' " +
                    "for employee {EmployeeId} segment [{SegStart}..{SegEnd}]. Using resolved value.",
                    profile.OkVersion, segmentOkVersion, profile.EmployeeId, segment.StartDate, segment.EndDate);
            }

            var segmentProfile = await SegmentProfileAsync(profile, segment.StartDate, plan, histories, ct);
            segmentProfiles[s] = segmentProfile;
            pieces.Add(new FrozenPiece(
                segment.StartDate, segment.EndDate,
                segmentProfile.PartTimeFraction, segmentProfile.AgreementCode, segmentProfile.Position));
        }

        return new EvaluationInputs(
            pieces, segmentProfiles, SettlementCutoffFrom(histories.EmploymentWindows, plan.PeriodEnd));
    }

    /// <summary>
    /// Evaluation itself (R42), shared by every forward calculation and by replay: the per-plan lane (one
    /// timeline call), the per-segment lane, the strategy-driven merge, the manifest emission and the
    /// wage-type mapping, over <paramref name="plan"/>, <paramref name="inputs"/> (pieces, segment profiles,
    /// cutoff), the opening balance and <paramref name="recordedHours"/>. It consults no approval state, no
    /// change-date history, no employment window and makes no hours read of its own: whatever it needs
    /// arrives as an argument.
    ///
    /// <para>
    /// <b>The frozen inputs (S145 / TASK-14513).</b> A successful evaluation records what it ran on as one
    /// <see cref="PlanInputs"/> (<see cref="FreezePlanInputs"/>): the pieces and the cutoff it was given, the
    /// opening balance, the hours rows' event ids, highest outbox id and lossless digests, and the engine's
    /// fingerprints and marker. A forward calculation writes it onto the manifest; every outcome carries it,
    /// which is what a replay compares with the manifest's record. A refused evaluation records nothing.
    /// </para>
    ///
    /// <para>
    /// <b>The agreement the result reports (S145 ruling R-12).</b> Evaluation takes the employee id, never a
    /// caller profile: the success result, the refused result (R-6) and the legacy
    /// <see cref="PeriodCalculationCompleted"/> event all report the agreement the period was EVALUATED under,
    /// the first EMPLOYED segment's piece (<see cref="FirstEmployedSegmentPiece"/>), or empty when no segment
    /// is EMPLOYED (nothing was evaluated under any agreement). One derivation serves a forward calculation and
    /// a replay alike, so a request body that disagrees with the employee's dated record can never relabel the
    /// month (the same principle as R-10 for the position).
    /// </para>
    ///
    /// <para>
    /// <b>The flex balances (S146 / TASK-14605, R-14).</b> <paramref name="openingFlexBalance"/> and
    /// <paramref name="openingFlexProvenance"/> are what admission derived (a forward calculation) or what the
    /// manifest froze (a replay). The opening goes to the timeline call and into <see cref="PlanInputs"/> with its
    /// provenance. The result reports the opening, the engine's clamped closing (<c>Flex.NewBalance</c>) and the
    /// provenance. Under <c>UNTRACKED</c> every <c>FLEX_PAYOUT</c> export line is dropped (a payout from a balance
    /// that is not tracked would be money from nowhere), the balance members are null and the provenance says
    /// <c>UNTRACKED</c>; a replay of such a manifest drops them the same way (R-33 item 2). A <c>null</c>
    /// provenance (no EMPLOYED day, or a manifest frozen before S146) reports no balance at all.
    /// </para>
    /// </summary>
    private async Task<PeriodCalculationOutcome> EvaluateAsync(
        PlannedCalculation plan,
        EvaluationInputs inputs,
        RecordedHours recordedHours,
        string employeeId,
        decimal openingFlexBalance,
        string? openingFlexProvenance,
        string? authorizationHeader,
        Guid? correlationId,
        bool emitAuditEvents,
        CancellationToken ct)
    {
        // Belt + braces at the emission boundary: the manifest is about to be persisted as the
        // audit-of-record, so a corrupted or mismatched plan must surface here (the forward entry points
        // checked it before admission too; replay arrives here directly).
        AssertPlanMatchesEmployee(plan, employeeId);

        // `recordedHours` is kept exactly as read; `entries` / `absences` are the one mapping of it that
        // BOTH lanes consume. Since TASK-14511 the per-plan lane sends the context days' entries with its
        // timeline (they count toward the opening week's overtime judgement); the per-segment lane's date
        // filter still keeps them out of the segment evaluation.
        var entries = ToTimeEntries(recordedHours.Entries);
        var absences = ToAbsenceEntries(recordedHours.Absences);

        // ADR-039 (S132 TASK-132-1b-1) — midnight-crossing normalization on the calculation
        // INPUT, BEFORE either lane filters by date. A shift crossing midnight (e.g.
        // 23:00→02:00) is filed as one row under its start-date; its post-midnight hours belong
        // (by wall clock + ADR-003) to the next calendar day, which at 2026-04-01 is a different
        // OK-version. Normalizing here splits the crossing entry into a day-D half and a day-D+1
        // half so the segment filter (:Date-based) routes each half to the correct OK-version
        // segment, and the per-day hours-summing checks count post-midnight hours on D+1 (D3).
        //
        // This is the evaluation every calculation shares, so applying it here covers BOTH the
        // forward calc AND the replay path (replay enters this method directly, with
        // emitAuditEvents:false) from the ONE shared implementation (D6). It is a pure derived
        // transform on the input only — the immutable event and every DISPLAY projection are
        // untouched (D1/D5a). The absences list is unaffected (absences are day-keyed, they cannot
        // cross midnight).
        var normalizedEntries = MidnightCrossingNormalizer.Normalize(entries);

        // ADR-039 D5b/D8 (GAP-B) — the month-end night shift. A shift filed on the period's last day
        // that runs past midnight yields a post-midnight half dated PeriodEnd + 1. That half belongs
        // to the NEXT month (ADR-039 D3: post-midnight hours are the next calendar day's), and the
        // next month reads it: its hours read starts at HoursReadStart(next.PeriodStart), the day
        // before its opening Monday, which is never later than this.PeriodEnd. So the half is
        // dropped HERE and counted THERE — no hours are lost and none are counted twice: the
        // per-segment date filter below drops it, and the per-plan lane sends only entries dated on
        // a day its timeline covers, which never runs past PeriodEnd (and the engine's weekly core
        // never evaluates a day after PeriodEnd either). The fail-closed guard that refused such a
        // month (AssertNoDroppedBoundaryCrossing, ADR-039 BLOCKER-3) was interim until payroll
        // widened its own read (QUAL-144; SPRINT-132); S145 TASK-14508 is that read, and the guard
        // was retired with it (adjudicator ruling, 2026-10-14). Pinned by
        // MonthEndCrossingTests.MonthEndNightShift_Wed30Sep2300To0200_SeptemberNotRefused_PostMidnightHoursReachOctober
        // (mutation MU-43: restore the throw).

        // Resolve the rule classification set once per call — used for the per-rule
        // MergeStrategy dispatch below.
        var ruleSet = _classificationProvider.GetClassifications();

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        // ---------------------------------------------------------------
        // 1. The EMPLOYED segments and their dated profiles come from the inputs: every profile was
        //    resolved before any rule-engine call (ADR-040 D10), so a missing employment record
        //    refused the month before anything was evaluated.
        // ---------------------------------------------------------------
        var pieces = inputs.Pieces;
        var segmentProfiles = inputs.SegmentProfiles;
        var settlementCutoff = inputs.SettlementCutoff;

        // S145 R-12 — the ONE derivation of the agreement this period is evaluated under: the first EMPLOYED
        // segment's piece, or empty for a plan with no EMPLOYED segment. The success result, the R-6 refused
        // result and the legacy completed event below all report it; never the request body's agreement.
        var evaluatedAgreementCode = FirstEmployedSegmentPiece(pieces, plan.PeriodStart)?.AgreementCode ?? string.Empty;

        var employedIndices = new List<int>(plan.Segments.Count);
        for (int s = 0; s < plan.Segments.Count; s++)
        {
            if (plan.Segments[s].EmploymentStatus != EmploymentWindowStatus.NOT_EMPLOYED)
                employedIndices.Add(s);
        }

        // ---------------------------------------------------------------
        // 2. The per-plan lane (S145 TASK-14511, QUAL-186). ONE timeline call evaluates overtime /
        //    merarbejde, the norm check and flex for the whole plan, week by week:
        //    - the pieces cover the EMPLOYED context days, then each EMPLOYED segment with its dated
        //      profile (a NOT_EMPLOYED day is covered by no piece, so it is not evaluated);
        //    - a week is settled by the month holding its Sunday and dated that Sunday (Q3 = F1), so
        //      a closing week that ends in the next month is paid THERE, not here; a leaver's last
        //      week is settled on their last employed day — the settlement cutoff (R41);
        //    - the opening flex balance goes in as derived (S146 TASK-14605: from the recorded ledger, never from
        //      a caller): flex is settled once for the month, so the per-segment flex carry is retired.
        //    No call is made for a plan with no EMPLOYED segment: there is nothing to settle (a week
        //    whose employed days all lie in the previous month was settled by that month).
        // ---------------------------------------------------------------

        // The rule-engine CALLS actually made — the one timeline call plus three per EMPLOYED segment —
        // and how many of them failed. ANY failed call refuses the calculation (S145 ruling R-6, below);
        // a plan that made no call is never refused. (S137 Codex-B2 hazards — a budget captured from a
        // skipped segment, or multiplied by segments that made no call — cannot recur: nothing is
        // multiplied.)
        int callsAttempted = 0;
        int callsFailed = 0;

        TimelineEvaluationResponse? timeline = null;
        if (employedIndices.Count > 0)
        {
            // With the timeline go the entries dated on a day it covers (context days included) and
            // the month's absences on a covered day; nothing outside the evaluated days is sent.
            var timelineEntries = normalizedEntries.Where(e => IsCovered(pieces, e.Date)).ToList();
            // Asymmetric on purpose: context-day ENTRIES count toward the opening week's overtime, but
            // context-day absences would be dead weight — the core reads absences only on in-period
            // covered days (flex), and overtime reads no absences at all.
            var timelineAbsences = absences
                .Where(a => a.Date >= plan.PeriodStart && IsCovered(pieces, a.Date))
                .ToList();

            callsAttempted++;
            timeline = await CallTimelineAsync(
                client, plan.EmployeeId, plan.PeriodStart, plan.PeriodEnd, settlementCutoff,
                pieces, timelineEntries, timelineAbsences, openingFlexBalance, ct);
            if (timeline is null)
                callsFailed++;
        }

        var perPlanRows = DistributePerPlanResults(timeline, plan, employedIndices);

        // ---------------------------------------------------------------
        // 3. The per-segment lane. Supplement, on-call and absence stay per segment; each segment
        //    carries its own OK version (resolved from segment.StartDate per ADR-003) and its own
        //    profile; entries/absences are filtered to the segment's date range before being shipped
        //    to the rule engine. The per-plan rows assigned to the segment join its result list.
        // ---------------------------------------------------------------
        var perSegmentRuleResults = new List<List<CalculationResult>>(plan.Segments.Count);
        var perSegmentExportLines = new List<List<PayrollExportLine>>(plan.Segments.Count);

        for (int s = 0; s < plan.Segments.Count; s++)
        {
            var segment = plan.Segments[s];
            if (segment.EmploymentStatus == EmploymentWindowStatus.NOT_EMPLOYED)
            {
                perSegmentRuleResults.Add(new List<CalculationResult>());
                perSegmentExportLines.Add(new List<PayrollExportLine>());
                continue;
            }

            var segmentProfile = segmentProfiles[s]
                ?? throw new InvalidOperationException(
                    $"PeriodCalculationService evaluation invariant violated: EMPLOYED segment {s} has no " +
                    $"dated profile in the evaluation inputs. ManifestId={plan.ManifestId}.");

            // This date filter is UNCHANGED and load-bearing. `normalizedEntries` / `absences` also
            // carry the context days before the period's first day (and a month-end crossing's
            // post-midnight half after its last); segments never reach outside the period, so those
            // rows stay out of the per-segment evaluation here (the per-plan lane consumes the
            // context rows).
            var segmentEntries = normalizedEntries.Where(e => e.Date >= segment.StartDate && e.Date <= segment.EndDate).ToList();
            var segmentAbsences = absences.Where(a => a.Date >= segment.StartDate && a.Date <= segment.EndDate).ToList();

            var lane = await EvaluateSegmentAsync(
                client, segmentProfile, segmentEntries, segmentAbsences,
                segment.StartDate, segment.EndDate, ct);
            callsAttempted += lane.Attempted;
            callsFailed += lane.Failures;

            var segmentRuleResults = AssembleSegmentResults(perPlanRows[s], lane);

            // Stamp every rule result on this segment with the manifest id so downstream
            // consumers (audit_log payload, SLS export, in-memory diff in
            // RetroactiveCorrectionService) can correlate every CalculationResult to the
            // manifest (PAT-006 amendment per ADR-016 D10).
            for (int i = 0; i < segmentRuleResults.Count; i++)
            {
                segmentRuleResults[i] = WithManifestId(segmentRuleResults[i], plan.ManifestId);
            }

            // Map this segment's line items to wage types using the segment's resolved OK version.
            // The full PlannedSegment is threaded through so MapSegmentToExportLinesAsync can read
            // the WtmNaturalKey replay-stable triple from segment.Snapshot.Values (ADR-020 D1 +
            // S29 TASK-2907; dated per segment since S145 TASK-14512, A4) and pass
            // asOfDate = segment.StartDate to the dated wage-type-mapping lookup (ADR-016 D10
            // replay determinism closed for WTM).
            var segmentExportLines = await MapSegmentToExportLinesAsync(
                segmentRuleResults, segmentProfile, segment, plan.ManifestId, ct);

            perSegmentRuleResults.Add(segmentRuleResults);
            perSegmentExportLines.Add(segmentExportLines);
        }

        // If ANY rule-engine call failed, the calculation is refused: a month exported without one of
        // its rules is wrong money labelled Success (S145 ruling R-6, adjudicator 2026-10-14). Since
        // TASK-14511 one failed timeline call would otherwise drop overtime, norm and flex together
        // while supplement, on-call and absence still exported. The refusal covers a Rule Engine
        // outage, an auth misconfiguration and a rejected input alike; the failed call itself was
        // already logged with its bounded diagnostic. Nothing is written: no manifest event, no
        // projection row, no export lines (the host answers 422 on !Success, so no export record).
        //
        // A plan with ZERO employed segments made no call, so it is never refused: it returns
        // Success == true with no rule results, no export lines, and a manifest whose segments are
        // all NOT_EMPLOYED. That is the RULED semantics — "nothing to calculate" is a successful,
        // auditable calculation (the manifest records WHY nothing was paid), not a failure.
        if (callsFailed > 0)
        {
            _logger.LogError(
                "{CallsFailed} of {CallsAttempted} rule-engine calls failed (the plan's timeline call and the " +
                "per-segment calls of {EmployedSegmentCount} employed segment(s), of {SegmentCount}) for employee " +
                "{EmployeeId} manifest {ManifestId} period {PeriodStart}-{PeriodEnd}. The calculation is REFUSED and " +
                "no payroll figure is produced for this period.",
                callsFailed, callsAttempted, employedIndices.Count, plan.Segments.Count, employeeId,
                plan.ManifestId, plan.PeriodStart, plan.PeriodEnd);

            var failResult = new PeriodCalculationResult
            {
                EmployeeId = employeeId,
                PeriodStart = plan.PeriodStart,
                PeriodEnd = plan.PeriodEnd,
                // S145 R-12: the agreement the period was evaluated under, as on the success result.
                AgreementCode = evaluatedAgreementCode,
                OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
                // No figure leaves a refused calculation: the rows of the calls that DID succeed are
                // not returned either (the host's 422 carries this result as its body). This is the
                // shape the all-calls-failed case always had — a failed call yields no row.
                RuleResults = [],
                ExportLines = [],
                Success = false,
                ErrorMessage = "Rule engine evaluation failed; no payroll figure is produced for this period"
            };
            // No manifest was emitted on this refusal (BuildManifest +
            // EmitManifestAsync run later). Return AuditState.NoManifest so callers do
            // not treat plan.ManifestId as a valid audit-query key — it does not exist
            // in segment_manifests or the event store. (Codex sprint-end review fix.)
            return new PeriodCalculationOutcome(failResult, AuditState.NoManifest, plan.ManifestId)
            {
                TimelinePieces = pieces,
                SettlementCutoff = settlementCutoff,
            };
        }

        // ---------------------------------------------------------------
        // 4. Strategy-driven merge of per-segment rule results (ADR-016 D3 / D7). Each rule
        //    is merged according to the strategy resolved at registration time; when the
        //    classification provider returns nothing for a rule (e.g. legacy / unwired
        //    callers), we default to Concatenate with a warning. Per-segment export lines
        //    are concatenated unconditionally — the per-line OK-version stamping carries
        //    the per-segment context, and the ExportLines list is intentionally a flat
        //    sequence at the period level. TASK-2010 / wave 2 retired the
        //    OkVersionBoundary.ResolveProfile collapse that previously lived inline in
        //    Program.cs; per-line OK-version stamping is now the canonical truth.
        //
        //    S145 Stage B (TASK-14522 contract 2; R-2, ruled R-75): a SinglePlanEvaluation rule (the three
        //    timeline rules) is merged from the ONE row the timeline call returned for it — all its lines,
        //    its period figures — not from the per-segment pieces it was cut into above. That cut served the
        //    wage-type mapping only (every line under the segment holding its date), and the export lines are
        //    already mapped, so the merge input changes the rule report and nothing that is exported. A reply
        //    that reached this point was checked to carry each of its three rule ids exactly once
        //    (TimelineReplyDefect), so the map cannot collide; no timeline call (no EMPLOYED segment) → null.
        // ---------------------------------------------------------------
        var allExportLines = perSegmentExportLines.SelectMany(s => s).ToList();
        var allRuleResults = MergePerSegmentRuleResults(
            perSegmentRuleResults, ruleSet, employeeId, plan.ManifestId,
            timelineRows: timeline?.Results.ToDictionary(r => r.RuleId, StringComparer.Ordinal));

        // S146 TASK-14605 (R-14 case 3b): a raw month before the ledger start runs UNTRACKED — its flex is not
        // tracked, so no payout may come from it: every FLEX_PAYOUT export line is dropped (the rule report keeps
        // what the engine computed). A replay of an UNTRACKED manifest drops them the same way (R-33 item 2).
        var untracked = string.Equals(openingFlexProvenance, FlexLedgerOpening.Untracked, StringComparison.Ordinal);
        if (untracked)
        {
            allExportLines.RemoveAll(l =>
                string.Equals(l.SourceTimeType, FlexLedgerOpening.FlexPayoutTimeType, StringComparison.Ordinal));
        }

        // S146 TASK-14605 (C1): the month's balances. The closing is the engine's clamped NewBalance (the balance
        // after any payout above the cap); a tracked provenance always comes with a timeline answer, because the
        // opening is derived only for a month that holds an EMPLOYED day and a failed call refused above.
        decimal? resultOpening = null;
        decimal? resultClosing = null;
        if (openingFlexProvenance is not null && !untracked)
        {
            resultOpening = openingFlexBalance;
            resultClosing = timeline?.Flex.NewBalance ?? throw new InvalidOperationException(
                $"PeriodCalculationService evaluation invariant violated: the opening flex balance of employee " +
                $"{employeeId} was derived ({openingFlexProvenance}) but the plan made no timeline call, so there is no " +
                $"closing balance to record. ManifestId={plan.ManifestId}.");
        }

        // ---------------------------------------------------------------
        // 5. Freeze what this evaluation ran on (S145 TASK-14513, refinement A7): the data as stored
        //    values, the code as the engine's fingerprints and marker. A replay re-runs exactly this.
        //    S146 TASK-14605 (R-33 item 2): with the opening's provenance, so a replay reproduces it.
        // ---------------------------------------------------------------
        var planInputs = FreezePlanInputs(inputs, recordedHours, openingFlexBalance, openingFlexProvenance, timeline);

        // ---------------------------------------------------------------
        // 6. Persist the proof (ADR-016 D10): the SegmentManifestCreated event, carrying the frozen
        //    PlanInputs, its segment_manifests projection row and the legacy PeriodCalculationCompleted,
        //    in ONE transaction through the ADR-018 outbox (S146 TASK-14604, QUAL-205; R-21, R-22). All
        //    three commit or none does: AuditState.Complete or AuditState.NotPersisted. A NotPersisted
        //    outcome still carries the (valid) result, and both calculating routes refuse it through
        //    PeriodCalculationOutcome.ThrowIfNotProvable before any payroll write.
        // ---------------------------------------------------------------
        AuditState auditState;
        if (emitAuditEvents)
        {
            var manifest = BuildManifest(plan, planInputs);

            // The legacy event, kept for backward compatibility with the pre-S20 audit chain: existing audit
            // queries against PeriodCalculationCompleted keep working. The manifest is the source of truth for
            // segmentation, the legacy event for high-level period-result counts. Since S146 it is written in
            // the manifest's own transaction, so it exists exactly when the manifest does (R-18).
            var legacyEvent = BuildLegacyPeriodCompleted(
                plan, employeeId, evaluatedAgreementCode, allRuleResults.Count, allExportLines.Count,
                allExportLines.Sum(l => l.Hours), correlationId);

            auditState = await EmitManifestAsync(plan, manifest, legacyEvent, correlationId, ct);
        }
        else
        {
            // Replay path (ADR-016 D10 immutability): the original manifest already exists
            // in the audit chain from the forward-calc that produced it; replay re-evaluates
            // rules but does NOT mint a new SegmentManifestCreated event or a new
            // PeriodCalculationCompleted event. Codex sprint-end review fix.
            auditState = AuditState.NoManifest;
        }

        _logger.LogInformation(
            "Period calculation complete for {EmployeeId} manifest {ManifestId}: " +
            "{SegmentCount} segment(s), {RuleCount} rule results, {LineCount} export lines, audit={AuditState}",
            employeeId, plan.ManifestId, plan.Segments.Count, allRuleResults.Count, allExportLines.Count, auditState);

        var okResult = new PeriodCalculationResult
        {
            EmployeeId = employeeId,
            PeriodStart = plan.PeriodStart,
            PeriodEnd = plan.PeriodEnd,
            // S145 R-12: the agreement the period was EVALUATED under (the first EMPLOYED segment's piece, or
            // empty when none is EMPLOYED), never the request body's. No rule and no export line reads it.
            AgreementCode = evaluatedAgreementCode,
            // The result-level OkVersion remains the period-start-resolved version for
            // legacy callers that read this field (RetroactiveCorrectionService, /export).
            // Per-line OK-version stamping on PayrollExportLine.OkVersion is the new
            // canonical truth; TASK-2010 will route the export boundary through that.
            OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
            RuleResults = allRuleResults,
            ExportLines = allExportLines,
            Success = true,
            // S146 TASK-14605 (C1): the opening used, the engine's clamped closing and where the opening came from.
            // Under UNTRACKED the balances are null and the provenance says so; with no EMPLOYED day all three are null.
            OpeningFlexBalance = resultOpening,
            ClosingFlexBalance = resultClosing,
            FlexProvenance = openingFlexProvenance,
        };

        return new PeriodCalculationOutcome(okResult, auditState, plan.ManifestId)
        {
            TimelinePieces = pieces,
            SettlementCutoff = settlementCutoff,
            ConfigFingerprints = timeline?.ConfigFingerprints ?? (IReadOnlyList<string>)Array.Empty<string>(),
            EvaluationModel = timeline?.EvaluationModel,
            PlanInputs = planInputs,
        };
    }

    /// <summary>
    /// S145 / TASK-14513 (refinement A7; rulings R14, R15, R41) — the <see cref="PlanInputs"/> of one
    /// successful evaluation, each member from what the evaluation actually consumed:
    /// <list type="bullet">
    /// <item><see cref="PlanInputs.Pieces"/> — the timeline pieces exactly as sent: the EMPLOYED context days of
    /// the opening week, then one per EMPLOYED segment, each with its fraction, agreement and position;</item>
    /// <item><see cref="PlanInputs.SettlementCutoff"/> — the cutoff as sent (R41: the window's inclusive
    /// <c>End</c> when it is on or before the period end, else <c>null</c>; derived once, by
    /// <see cref="SettlementCutoffFrom"/>, and not recoverable from the plan);</item>
    /// <item><see cref="PlanInputs.OpeningFlexBalance"/> — the opening balance the timeline call received (0 when
    /// the plan made no call), and <see cref="PlanInputs.OpeningFlexProvenance"/> — where it came from (S146 /
    /// TASK-14605; <c>null</c> when no opening was derived). The provenance is optional and outside
    /// <see cref="IsComplete"/> (R-33 item 2);</item>
    /// <item><see cref="PlanInputs.EntryEventIds"/> / <see cref="PlanInputs.AbsenceEventIds"/> — the event ids of
    /// every row the hours read returned (all of them, as read: the evaluation's own date filters are code and
    /// re-run identically at replay), sorted ordinally on their <c>D</c> string form;</item>
    /// <item><see cref="PlanInputs.AsOfOutboxId"/> — the highest <c>outbox_id</c> among those rows, or 0 when the
    /// read returned none (outbox ids start at 1, so 0 means "nothing consumed");</item>
    /// <item><see cref="PlanInputs.EntriesDigest"/> / <see cref="PlanInputs.AbsencesDigest"/> — the lossless
    /// digests of the rows as read, before normalization (R15, <see cref="HoursDigest"/>);</item>
    /// <item><see cref="PlanInputs.ConfigFingerprints"/> / <see cref="PlanInputs.EvaluationModel"/> — what the
    /// Rule Engine returned for the timeline call; for a plan that made no call (no EMPLOYED segment), no
    /// fingerprint and <see cref="NoEngineCallEvaluationModel"/>.</item>
    /// </list>
    /// Pure: it reads nothing. Only called on the success path, where a missing timeline answer can only mean
    /// that no call was made (a failed call refuses the calculation before this point, R-6).
    /// </summary>
    private static PlanInputs FreezePlanInputs(
        EvaluationInputs inputs,
        RecordedHours recordedHours,
        decimal openingFlexBalance,
        string? openingFlexProvenance,
        TimelineEvaluationResponse? timeline)
    {
        var maxEntryOutboxId = recordedHours.Entries.Count > 0 ? recordedHours.Entries.Max(r => r.OutboxId) : 0L;
        var maxAbsenceOutboxId = recordedHours.Absences.Count > 0 ? recordedHours.Absences.Max(r => r.OutboxId) : 0L;

        return new PlanInputs(
            Pieces: inputs.Pieces.ToArray(),
            SettlementCutoff: inputs.SettlementCutoff,
            OpeningFlexBalance: openingFlexBalance,
            EntryEventIds: SortedEventIds(recordedHours.Entries.Select(r => r.EventId)),
            AbsenceEventIds: SortedEventIds(recordedHours.Absences.Select(r => r.EventId)),
            AsOfOutboxId: Math.Max(maxEntryOutboxId, maxAbsenceOutboxId),
            EntriesDigest: HoursDigest.ComputeEntriesDigest(recordedHours.Entries),
            AbsencesDigest: HoursDigest.ComputeAbsencesDigest(recordedHours.Absences),
            ConfigFingerprints: timeline?.ConfigFingerprints.ToArray() ?? Array.Empty<string>(),
            EvaluationModel: timeline?.EvaluationModel ?? NoEngineCallEvaluationModel)
        {
            OpeningFlexProvenance = openingFlexProvenance,
        };
    }

    /// <summary>
    /// Event ids in the frozen order: ordinal over the <c>D</c> string form (lowercase, hyphenated) — the
    /// order the digest lines sort in. Never <c>Guid.CompareTo</c> or <c>OrderBy(g =&gt; g)</c>, which order
    /// Guids by their internal byte layout and disagree with the string order (TASK-14500 Step 5a finding).
    /// </summary>
    private static Guid[] SortedEventIds(IEnumerable<Guid> eventIds) =>
        eventIds
            .Distinct()
            .OrderBy(id => id.ToString("D", System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The plan's own integrity, checked before admission and again at the emission boundary: the
    /// geometric invariants (re-asserted — the planner already ran them, but an out-of-band corruption
    /// between planner and PCS, e.g. a test fixture or a future caller bug, must surface here rather than
    /// be persisted as the audit-of-record) and the employee pairing. An EmployeeId mismatch is a
    /// programmer error (the caller paired the wrong plan with the wrong profile); the planner cannot
    /// catch it, because <c>Plan()</c> takes the employee id as an input and the profile is supplied
    /// separately. Throwing keeps the manifest contract honest: one plan == one employee == one set of
    /// inputs. Both sides are strings per the ADR-016 D10 amendment of 2026-05-01 — no synthetic Guid
    /// round-trip.
    /// </summary>
    private static void AssertPlanMatchesProfile(PlannedCalculation plan, EmploymentProfile profile) =>
        AssertPlanMatchesEmployee(plan, profile.EmployeeId);

    /// <summary>
    /// The same check over an employee id: what evaluation receives since S145 ruling R-12 (a forward
    /// calculation passes its caller profile's id, a replay its manifest's own). The message is unchanged.
    /// </summary>
    private static void AssertPlanMatchesEmployee(PlannedCalculation plan, string employeeId)
    {
        AssertGeometricInvariants(plan);

        if (!string.Equals(plan.EmployeeId, employeeId, StringComparison.Ordinal))
        {
            throw new PlannerInvariantViolation(
                $"PeriodCalculationService.CalculateAsync invariant violated: PlannedCalculation.EmployeeId " +
                $"('{plan.EmployeeId}') does not match profile.EmployeeId ('{employeeId}'). " +
                $"ManifestId={plan.ManifestId}.");
        }
    }

    // -------------------------------------------------------------------
    // Back-compat shim — D8 always-on planner. It hydrates BoundarySources, runs the planner,
    // and re-enters the new entry point. No "skip planner" code path exists.
    //
    // NO PRODUCTION CALLER (corrected S146 TASK-14604): both former callers moved to the
    // outcome-returning sibling CalculateWithOutcomeAsync(profile, …) below — the
    // /calculate-and-export endpoint in S134 (QUAL-003) and RetroactiveCorrectionService in S145
    // (ruling R-11) — because only the outcome carries the manifest id and the audit state the
    // routes need. This overload is kept for its test callers only (for example the parity pins
    // that prove the sibling's result bit-identical to this one's), and returns that same body's
    // .Result.
    //
    // [Obsolete] is intentional: it marks the migration runway from caller-supplied
    // (profile, period, …) to caller-constructed PlannedCalculation. Full retirement of the
    // planless path was explicitly out-of-scope per S20 Step 0b W2.
    //
    // S145 TASK-14508 (R14, R25): the shim takes no hours. The never-silent hours-reader check
    // runs FIRST, before planning's reads (the rule-classification fetch, the employment-window
    // and history reads); the hours themselves are read by admission.
    //
    // S145 TASK-14512 (R42, N1): a forward calc, so both steps run — planning (the planner's 422),
    // then admission (the week check's 422, the prior-month 409, the hours read), then evaluation.
    // -------------------------------------------------------------------
    [Obsolete(
        "Use CalculateAsync(PlannedCalculation, …) or CalculateWithOutcomeAsync(PlannedCalculation, …). " +
        "Boundary sources are limited to OK-transitions, LocalProfileActivations (S21), employment-window, " +
        "employee-profile-change dates (S137) and agreement-code dates (S144) in this path; full segmentation requires explicit " +
        "PlannedCalculation construction. It has no production caller (the /calculate-and-export endpoint and " +
        "RetroactiveCorrectionService call the outcome-returning CalculateWithOutcomeAsync(profile, …)); it is kept " +
        "for test callers only. Full retirement is deferred per S20 Step 0b W2.",
        error: false)]
    public async Task<PeriodCalculationResult> CalculateAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // S145 TASK-14508 (R25): never silent, before any read.
        var hoursReader = RequireHoursReader(nameof(CalculateAsync));

        var outcome = await CalculatePlanlessAsync(
            profile, periodStart, periodEnd,
            authorizationHeader, correlationId, hoursReader, ct);
        return outcome.Result;
    }

    /// <summary>
    /// The ONE body of both planless shims (the legacy <c>CalculateAsync</c> and its outcome-returning
    /// sibling), which is what keeps their results bit-identical by construction (QUAL-003): plan through
    /// <see cref="BuildPlanWithHistoriesAsync"/> (the planner's split refusal is the first 422), admit
    /// the plan (<see cref="AdmitAsync"/>: the week check's 422, the prior-month 409, the hours read — in
    /// that order, N1), then evaluate it. A forward calc: <c>emitAuditEvents</c> is true. The histories
    /// planning read are the ones admission and evaluation use (S145 TASK-14511), so nothing is read twice;
    /// the dated profiles planning resolved for the segments' wage-type keys are the ones evaluation uses
    /// (TASK-14512, A4).
    /// </summary>
    private async Task<PeriodCalculationOutcome> CalculatePlanlessAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        string? authorizationHeader,
        Guid? correlationId,
        IRecordedHoursReader hoursReader,
        CancellationToken ct)
    {
        // Planning became async in S21 TASK-2108 (ADR-017 D9c) so it can hydrate
        // LocalProfileActivations from the local_agreement_profiles table. S145 TASK-14511:
        // the plan comes back WITH the histories its planning read.
        var (plan, histories) = await BuildPlanWithHistoriesAsync(profile, periodStart, periodEnd, ct);

        AssertPlanMatchesProfile(plan, profile);

        var admitted = await AdmitAsync(plan, histories, hoursReader, ct);

        return await EvaluateAdmittedAsync(
            admitted, profile, authorizationHeader, correlationId,
            emitAuditEvents: true, ct);
    }

    // -------------------------------------------------------------------
    // Back-compat shim (outcome-returning sibling) — S134 TASK-13404 (QUAL-003)
    // -------------------------------------------------------------------

    /// <summary>
    /// Planless back-compat shim that returns the full <see cref="PeriodCalculationOutcome"/>
    /// (result + <see cref="AuditState"/> + <see cref="PeriodCalculationOutcome.ManifestId"/>),
    /// the outcome-returning sibling of the legacy planless
    /// <see cref="CalculateAsync(EmploymentProfile, DateOnly, DateOnly, string?, Guid?, CancellationToken)"/>
    /// shim above.
    ///
    /// <para>
    /// <strong>Byte-identical export by construction (QUAL-003 payroll-boundary invariant).</strong>
    /// This method runs the SAME body (<see cref="CalculatePlanlessAsync"/>: the same planning through
    /// <see cref="BuildPlanWithHistoriesAsync"/>, the same admission and the same evaluation, which the
    /// plan-first
    /// <see cref="CalculateWithOutcomeAsync(PlannedCalculation, EmploymentProfile, string?, Guid?, bool, CancellationToken)"/>
    /// also runs) as the planless <c>CalculateAsync</c> — which is literally
    /// <c>CalculatePlanlessAsync(…).Result</c>. So <c>this(…).Result</c> is bit-identical
    /// to <c>CalculateAsync(profile, …)</c> for the same inputs; the ONLY difference across two
    /// separate invocations is the freshly-minted <see cref="PlannedCalculation.ManifestId"/>, which
    /// was already non-deterministic per call before this method existed. The added value is the
    /// exposed <see cref="PeriodCalculationOutcome.ManifestId"/>, which the
    /// <c>/api/payroll/calculate-and-export</c> endpoint threads into the audit chain via
    /// <see cref="StampAuditContext(HttpContext, PeriodCalculationOutcome)"/> so the ADR-016 D10
    /// <c>segment_manifests⋈audit_log</c> linkage is created — without perturbing the export at all.
    /// </para>
    ///
    /// <para>
    /// <c>emitAuditEvents</c> is left at its default (true): this is a FORWARD calc, so the
    /// SegmentManifestCreated event + projection row are persisted exactly as before — mirroring how
    /// the planless <c>CalculateAsync</c> re-enters the emitting path (only <c>ReplayAsync</c> passes
    /// <c>emitAuditEvents:false</c>).
    /// </para>
    ///
    /// <para>
    /// S145 / TASK-14508 (R14, R25): takes no hours, like its sibling. The never-silent
    /// hours-reader check runs first, before planning reads anything; the hours are read by
    /// admission.
    /// </para>
    ///
    /// <para>
    /// S145 / TASK-14512: the refusals come in the N1 order — the planner's
    /// <see cref="PlannerInvariantViolation"/> (split refusal), then <see cref="PeriodNotCalculableException"/>
    /// (week check), then <see cref="PriorMonthNotApprovedException"/> (months in order) — and the outcome
    /// carries <see cref="PeriodCalculationOutcome.PriorMonthApprovalRequired"/> for the export's row-locked
    /// recheck (Do 7).
    /// </para>
    /// </summary>
    [Obsolete(
        "Use CalculateWithOutcomeAsync(PlannedCalculation, …). Boundary sources are limited to " +
        "OK-transitions, LocalProfileActivations (S21), employment-window, employee-profile-change " +
        "dates (S137) and agreement-code dates (S144) in this path; full segmentation requires explicit PlannedCalculation construction. " +
        "The two surviving callers are the /calculate-and-export endpoint (S134 QUAL-003) and " +
        "RetroactiveCorrectionService (S145 R-11); full retirement is deferred per S20 Step 0b W2.",
        error: false)]
    public async Task<PeriodCalculationOutcome> CalculateWithOutcomeAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // S145 TASK-14508 (R25): never silent, before any read.
        var hoursReader = RequireHoursReader(nameof(CalculateWithOutcomeAsync));

        // Mirrors the planless CalculateAsync shim EXACTLY (the same CalculatePlanlessAsync body), then
        // returns the PeriodCalculationOutcome INSTEAD of its .Result — this shared body is what makes
        // the export output bit-identical. S146 TASK-14605 (R-14): no opening is passed; admission derives it.
        return await CalculatePlanlessAsync(
            profile, periodStart, periodEnd,
            authorizationHeader, correlationId, hoursReader, ct);
    }

    // -------------------------------------------------------------------
    // Calculability guard for the raw export routes — S144 TASK-14410 (QUAL-183), extended by
    // S145 TASK-14512 (Q5 (c)) and S145 Stage B / TASK-14522 (Q6 (a))
    // -------------------------------------------------------------------

    /// <summary>
    /// Plans <paramref name="periodStart"/>..<paramref name="periodEnd"/> for
    /// <paramref name="profile"/> exactly as the calculating endpoints do, runs the same week check,
    /// then the raw routes' own single-profile check, and discards the plan. Returns normally when the
    /// period can be exported through a raw route. Otherwise it throws, in this order (ruled R-74):
    /// <list type="number">
    /// <item>the planner's <see cref="PlannerInvariantViolation"/> when the period is not plannable (a split
    /// refusal under a rule set that still holds a Reject or AlignedWindow rule, or a geometric defect);</item>
    /// <item><see cref="PeriodNotCalculableException"/> with
    /// <see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/> when an agreement change falls inside
    /// a week the month settles, on a day other than its Monday (S145 / TASK-14512, Q5 (c));</item>
    /// <item><see cref="PeriodNotCalculableException"/> with
    /// <see cref="PeriodNotCalculableException.SplitMonthSingleProfileRoute"/> when the plan has two or more
    /// EMPLOYED segments (S145 Stage B / TASK-14522, Q6 (a), ruling R-76), carrying the EMPLOYED segment count
    /// and the plan's distinct interior boundary causes in date order.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Why it exists (plain language).</b> The raw <c>/api/payroll/export</c> and
    /// <c>/api/payroll/export-period</c> routes take lines the caller has ALREADY calculated and
    /// export them; they never plan. So a month the calculating endpoints refuse could still be exported
    /// through them with wrong wage-type lines. The owner ruled (S144, 2026-09-29) that those routes must
    /// refuse the same months, with the same 422 — which since S145 includes the week check's variant. They
    /// still bypass APPROVAL by design: the months-in-order check (the 409) is the calculating routes' alone,
    /// and nothing here reads an approval or an hour.
    /// </para>
    ///
    /// <para>
    /// <b>The single-profile check (Q6 (a)).</b> Since S145 Stage B the calculating routes pay a month with
    /// two or more EMPLOYED parts, mapping each line under the wage-type codes of the part its date falls in.
    /// These routes cannot: they map every line under the request's ONE profile, so the second part's lines
    /// would be exported under the first part's codes. They therefore refuse such a month by their own reason,
    /// separate from the week check's, because they answer a different question: the week check asks "can
    /// this week be calculated?", this one asks "can a single-profile route export a two-profile month?". A
    /// month with one EMPLOYED part (a hire or a leaver month: an employment edge truncates, it does not split)
    /// still exports. The check runs after the week check, so an interior agreement change on a Tuesday to
    /// Sunday — both a mid-week change and a split month — gets the week check's answer, exactly as on the
    /// calculating routes ("one predicate, three consumers"). The calculating routes never meet this check: it
    /// lives here, in the raw-route-only method.
    /// </para>
    ///
    /// <para>
    /// <b>Same builder and same week check, by design.</b> This goes through the SAME private
    /// <see cref="BuildPlanWithHistoriesAsync"/> as
    /// <see cref="CalculateWithOutcomeAsync(EmploymentProfile, DateOnly, DateOnly, string?, Guid?, CancellationToken)"/>
    /// — same boundary sources, same rule classifications (<see cref="EvaluatedRuleIds"/>), same
    /// <see cref="PeriodPlanner.Plan"/> — and the SAME <see cref="EnsureNoMidWeekAgreementChange"/> the
    /// calculating routes' admission runs. There is no second copy of the boundary or week logic to drift,
    /// and no caller-supplied "already checked" flag. It writes nothing: no manifest, no event, no row — the
    /// plan is built in memory and dropped. When the rule classification set cannot be obtained, it throws
    /// <see cref="RuleClassificationsUnavailableException"/> rather than planning blind (the routes map that
    /// to a 503; S144 TASK-14412).
    /// </para>
    /// </summary>
    public async Task EnsurePeriodCalculableAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var (plan, histories) = await BuildPlanWithHistoriesAsync(profile, periodStart, periodEnd, ct);
        EnsureNoMidWeekAgreementChange(plan, histories);

        // Q6 (a) (S145 Stage B, R-76): a single-profile route cannot export a month with two or more
        // EMPLOYED parts. The causes are the planner's own report of the plan's interior boundaries: each
        // segment carries the cause of one boundary (segment 0 that of boundary 0, PeriodPlanner.Plan), so
        // the distinct causes in segment order, first occurrence kept, are the distinct boundary causes in
        // date order — the list the planner's refusal reports. Causes and a count only, never a date
        // (ADR-040 D7).
        var employedSegmentCount = plan.Segments.Count(s => s.EmploymentStatus == EmploymentWindowStatus.EMPLOYED);
        if (employedSegmentCount < 2)
            return;

        var interiorBoundaryCauses = new List<BoundaryCause>(plan.Segments.Count);
        foreach (var segment in plan.Segments)
        {
            if (!interiorBoundaryCauses.Contains(segment.BoundaryCause))
                interiorBoundaryCauses.Add(segment.BoundaryCause);
        }

        throw new PeriodNotCalculableException(
            PeriodNotCalculableException.SplitMonthSingleProfileRoute,
            $"PeriodCalculationService raw-route check: employee '{plan.EmployeeId}' — the period " +
            $"[{plan.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.." +
            $"{plan.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}] has " +
            $"{employedSegmentCount} EMPLOYED segments (interior boundary causes: " +
            $"{string.Join(", ", interiorBoundaryCauses)}). A raw export route maps every line under the " +
            "request's single profile, so it cannot export a period with more than one EMPLOYED part (Q6 (a)); " +
            $"the calculating routes pay it part by part. ManifestId={plan.ManifestId}.",
            employedSegmentCount,
            interiorBoundaryCauses);
    }

    /// <summary>
    /// Build a <see cref="PlannedCalculation"/> for callers that haven't migrated to the
    /// PlannedCalculation-first signature yet, returning ALSO the employee histories the planning
    /// read (S145 / TASK-14511; see "The histories" below). Hydrates an OK-version boundary source from
    /// <see cref="OkVersionResolver"/> — S20's end-to-end boundary — and (S21 TASK-2108,
    /// ADR-017 D9c) local-agreement-profile activation boundaries from
    /// <see cref="LocalAgreementProfileRepository"/>, and (S137 TASK-13702, ADR-040 D5/D7)
    /// the employee's employment window(s) from <see cref="IEmploymentWindowResolver"/> plus
    /// <c>employee_profiles</c> effective-from dates from <see cref="EmployeeProfileRepository"/>,
    /// and (S144 TASK-14403) <c>user_agreement_codes</c> effective-from dates from
    /// <see cref="UserAgreementCodeRepository"/> (the <c>AgreementCodeChange</c> boundary).
    /// Agreement-config / position-override / EU WTD boundary hydration remain extension
    /// points that TASK-2009/TASK-2010 callers will populate when they construct plans directly.
    ///
    /// <para>
    /// <strong>Employment-window fenceposts (ADR-040 D1 — the recurring inclusive/exclusive
    /// hazard, stated once here):</strong> a boundary date is always the FIRST day of the NEW
    /// segment. A window's <c>Start</c> is itself the first employed day, so it is passed
    /// as-is (the pre-hire NOT_EMPLOYED prefix ends at <c>Start − 1</c>). A window's
    /// <c>End</c> is INCLUSIVE (the last day employed), so the boundary is <c>End + 1</c>
    /// — the first NOT-employed day. Passing <c>End</c> itself is the classic off-by-one
    /// that would cut the last paid day out of the employed segment. Raw dates are passed;
    /// <c>BoundaryDetector</c> filters to the period interior itself. The same windows are
    /// handed to <see cref="PeriodPlanner.Plan"/> to TYPE the resulting segments — the
    /// planner never derives boundaries from windows (it fail-safes a straddling segment to
    /// EMPLOYED), so boundary hydration and typing must come from the same read, as here.
    /// </para>
    ///
    /// <para>
    /// <strong>Null-resolver byte-identity:</strong> when neither S137 dependency is wired
    /// (direct-construction fixtures), the new <see cref="BoundarySources"/> fields and the
    /// planner's <c>employmentWindows</c> stay <c>null</c> — the planner treats null as
    /// "windowless", every segment types EMPLOYED, and the serialized plan is byte-identical
    /// to pre-S137 output (the EMPLOYED key is omitted by <c>WhenWritingDefault</c>).
    /// </para>
    ///
    /// <para>
    /// The resolved <see cref="RuleClassification"/> set comes from the same
    /// <see cref="IRuleClassificationProvider"/> used by the main entry point, so D9
    /// rule-side invariants run uniformly across the shim and PlannedCalculation-first
    /// paths (ADR-016 D9).
    /// </para>
    ///
    /// <para>
    /// <strong>Async (S21 TASK-2108)</strong>: planning became async to accommodate the
    /// DB lookup against <c>local_agreement_profiles</c>, and its call sites <c>await</c> it.
    /// Sync-over-async (<c>.GetAwaiter().GetResult()</c>) was rejected to avoid ASP.NET
    /// deadlock risk.
    /// </para>
    ///
    /// <para>
    /// <strong>D9c profile-less contract</strong>: callers that do not associate the
    /// employee with an organization (test fixtures, internal use, or the current
    /// post-S21 state where <see cref="EmploymentProfile"/> does not yet expose
    /// <c>OrgId</c>) MUST see an empty <c>LocalProfileActivations</c> list — i.e. no
    /// segment boundaries from this source. The check is gated on both repository
    /// presence AND a non-empty resolved org id; either being absent yields an empty
    /// activation list and a single-segment plan (modulo other boundary sources).
    /// Since S145 / TASK-14519 (R-20) the org id, agreement code and OK version are the
    /// employee's DATED record's, per (agreement, OK-version) interval of the period, never
    /// the request's (see <see cref="ReadLocalProfileActivationsAsync"/>); without a profile
    /// resolver the caller's profile is that record.
    /// </para>
    ///
    /// <para>
    /// <strong>The histories.</strong> The planless shims hand them to admission and evaluation, so the
    /// week check, the months-in-order check and the per-plan lane's timeline are built from the SAME read
    /// that fed the planner: the employment window that typed the segments also gives the context days and
    /// the settlement cutoff (R41), and the widened profile and agreement-code date reads (from the opening
    /// week's Monday) give the context pieces' edges and the week check's change dates. The planner itself
    /// ignores every date on or before the period start (<c>BoundaryDetector.IsInsidePeriod</c> keeps only
    /// <c>periodStart &lt; d &lt;= periodEnd</c>), so the widened reads change no plan. The raw routes'
    /// <see cref="EnsurePeriodCalculableAsync"/> uses them for the same week check.
    /// </para>
    ///
    /// <para>
    /// <strong>The dated wage-type key (S145 / TASK-14512, A4 — QUAL-150).</strong> Before the planner
    /// runs, the employee's dated record is resolved once at every date a segment can start on (the
    /// period start and every boundary-source date inside the period) that the employee is EMPLOYED on,
    /// and handed to the planner as data: its <c>WtmNaturalKey</c> hydrator returns, for each EMPLOYED
    /// segment, the key in force at that segment's start — the dated OK version, agreement code and
    /// position, never the request body's. The same resolutions are returned on
    /// <see cref="PlanHistories.SegmentStartProfiles"/> and are the segments' profiles in evaluation, so
    /// the rule engine and the export cannot disagree. A record gap is not refused here: it is recorded
    /// and refused by evaluation (<c>employment_record_gap</c>), after the planner and admission, exactly
    /// where it was refused before.
    /// </para>
    ///
    /// <para>
    /// <strong>The dated local-profile key (S145 / TASK-14519, R-20 — QUAL-203).</strong> The
    /// local-agreement-profile activations are asked of the same dated record, per (agreement,
    /// OK-version) interval of the period (<see cref="ReadLocalProfileActivationsAsync"/>), through the
    /// same per-date cache, so the request body's organisation, agreement code and OK version no longer
    /// reach this boundary source, and the key adds no resolver read.
    /// </para>
    ///
    /// <para>
    /// <strong>Its name is load-bearing.</strong> Two planning-shape suites,
    /// <c>EmploymentWindowHydrationTests</c> (Unit) and <c>ProfileBoundaryHydrationTests</c> (Regression),
    /// invoke this private method by name through reflection, so renaming it breaks them.
    /// </para>
    /// </summary>
    private async Task<(PlannedCalculation Plan, PlanHistories Histories)> BuildPlanWithHistoriesAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        // Extract OK transitions whose dates fall strictly inside (periodStart, periodEnd]
        // — the planner's BoundaryDetector expects "first day of the new segment" semantics.
        var versionPeriods = OkVersionResolver.ResolveVersionsForPeriod(periodStart, periodEnd);
        var okTransitions = new List<(DateOnly Date, string FromVersion, string ToVersion)>();
        for (int i = 1; i < versionPeriods.Count; i++)
        {
            okTransitions.Add((versionPeriods[i].Start, versionPeriods[i - 1].Version, versionPeriods[i].Version));
        }

        // The employee histories — ONE read each, shared with the calculation's per-plan lane
        // (S145 TASK-14511; see ReadPlanHistoriesAsync for the ranges and why the two date reads
        // start at the opening week's Monday). Read FIRST since S145 TASK-14519: the local-profile
        // activation key below cuts the period at the agreement-code dates this read returns.
        var histories = await ReadPlanHistoriesAsync(profile.EmployeeId, periodStart, periodEnd, ct);

        // Employment window(s) — ADR-040 D5/D7 (S137 TASK-13702). Read server-side inside
        // this host via IEmploymentWindowResolver (the DTO never carries the dates, D7). The
        // resolver's list is spells-proof: 0-or-1 entries today, a genuine list once re-hire
        // spells land — this loop is already list-shaped. An EMPTY list means "the window is
        // known and no employed day falls in the period" → the planner types every segment
        // NOT_EMPLOYED. null (resolver not wired) means "windowless" → every segment EMPLOYED.
        var employmentWindows = histories.EmploymentWindows;
        List<DateOnly>? employmentStartedDates = null;
        List<DateOnly>? employmentEndedDates = null;
        if (employmentWindows is not null)
        {
            employmentStartedDates = new List<DateOnly>(employmentWindows.Count);
            employmentEndedDates = new List<DateOnly>(employmentWindows.Count);
            foreach (var window in employmentWindows)
            {
                // Start IS the first employed day → boundary at Start (fencepost, see XML doc).
                if (window.Start is { } start)
                    employmentStartedDates.Add(start);

                // End is INCLUSIVE (last day employed) → boundary at End + 1, the first
                // NOT-employed day. The MaxValue guard only avoids an ArgumentOutOfRange on a
                // sentinel 9999-12-31 end date, which can never be interior to a period anyway.
                if (window.End is { } end && end < DateOnly.MaxValue)
                    employmentEndedDates.Add(end.AddDays(1));
            }
        }

        // employee_profiles effective_from dates — ADR-040 D5 activates the ADR-016 D5b-reserved
        // EmployeeProfileChange cause. Since S145 TASK-14511 the read starts at the opening week's
        // Monday (the per-plan lane needs the context days' changes); the planner still turns only
        // the dates STRICTLY inside (periodStart, periodEnd] into boundaries: a row taking effect ON
        // periodStart is not an interior boundary (the segment already starts there), and
        // BoundaryDetector.IsInsidePeriod drops it and every earlier date. No second filter here.
        var employeeProfileEffectiveDates = histories.EmployeeProfileEffectiveDates;

        // user_agreement_codes effective_from dates — S144 (QUAL-150 groundwork) feeds the
        // AgreementCodeChange cause. Widened exactly like the profile read above (S145 TASK-14511:
        // this one read feeds the planner, the timeline's context pieces, TASK-14512's week check and
        // A4), and planned exactly as before: only the dates inside (periodStart, periodEnd] become
        // boundaries. The read applies no effective_to filter and no exclusion (see its XML doc):
        // over-detecting only refuses a month, under-detecting pays it under the wrong agreement.
        var agreementCodeEffectiveDates = histories.AgreementCodeEffectiveDates;

        // S145 TASK-14519 — the employee's dated record, resolved at most ONCE per date for this plan:
        // the local-profile activation key below and the segment-start profiles further down read the
        // same date through this cache, so the key adds no resolver read (every date it resolves is an
        // EMPLOYED candidate segment start, which the segment-start resolution reads anyway).
        var datedResolutions = new Dictionary<DateOnly, DatedProfile>();

        // Local-agreement-profile activations (S21 TASK-2108, ADR-017 D9c) — asked of the employee's
        // DATED record since S145 TASK-14519 (adjudicator R-20, QUAL-203), per (agreement, OK-version)
        // interval, never of the request body: see ReadLocalProfileActivationsAsync for the cut, the
        // key and why a single lookup keyed on the period start would miss an interior change.
        var localProfileActivations = await ReadLocalProfileActivationsAsync(
            profile, periodStart, periodEnd, okTransitions, agreementCodeEffectiveDates,
            employmentWindows, employmentStartedDates, datedResolutions, ct);

        var sources = new BoundarySources(
            OkTransitions: okTransitions,
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>(),
            LocalProfileActivations: localProfileActivations,
            EmploymentStartedDates: employmentStartedDates,
            EmploymentEndedDates: employmentEndedDates,
            EmployeeProfileEffectiveDates: employeeProfileEffectiveDates,
            AgreementCodeEffectiveDates: agreementCodeEffectiveDates);

        // S145 TASK-14512 (A4, QUAL-150) — the employee's dated record at every date a segment can
        // start on, resolved ONCE, before planning, and handed to the planner as data. The candidates
        // are the period start plus every boundary-source date inside the period (a superset of the
        // planner's segment starts, by construction over the same sources), and only the EMPLOYED
        // ones: a NOT_EMPLOYED segment carries no key, and its days may have no employment record.
        // A record gap is recorded, not thrown: evaluation refuses it, after the planner and admission
        // (N1), at the same point as before.
        var segmentStartProfiles = await ResolveSegmentStartProfilesAsync(
            profile, CandidateSegmentStarts(sources, periodStart, periodEnd), employmentWindows, datedResolutions, ct);

        // ADR-020 D1 (S29 TASK-2907) — planner-enrollment seam for non-rule snapshot
        // contracts. Register the wage-type-mapping natural-key triple as a replay-stable
        // input keyed by "WtmNaturalKey"; the planner runs the hydrator once per EMPLOYED segment,
        // and MapSegmentToExportLinesAsync reads the typed record back to drive the dated
        // wage-type-mapping lookup (PayrollMappingService.GetMappingAsync(..., asOfDate: segmentStart)).
        //
        // S145 TASK-14512 (A4; amends ADR-020 D1 component 5 / D1.5): the hydrator is DATED. It
        // receives the segment's start and returns the key of the employee's dated record on that
        // day — OK version, agreement code AND position (R-10: a null dated position means "no
        // position", never the caller's) — so the request body's profile no longer reaches the key.
        // This closes QUAL-150's planner half: a split month's second segment now carries its own
        // key (the increment that makes such splits plannable "MUST land the per-segment key first").
        //
        // Position is normalized to "" when null per the canonical empty-string fallback
        // convention (PayrollMappingService.GetMappingAsync XML doc + S29 TASK-2904
        // GetByKeyAtAsync replication).
        var enrollment = new PlannerEnrollment();
        enrollment.RegisterSnapshotContract(
            "WtmNaturalKey",
            (callerProfile, segmentStart) => DatedWtmKey(callerProfile, segmentStart, segmentStartProfiles));

        // EmployeeId is now string end-to-end (ADR-016 D10 amendment 2026-05-01) — pass
        // profile.EmployeeId directly with no synthetic Guid derivation.
        //
        // S145 Stage B (TASK-14522, refinement B1): the planner is handed only the classifications of the
        // rules payroll evaluates (EvaluatedRuleIds — the list and its reason). The fetch throws
        // RuleClassificationsUnavailableException (the routes' 503) before anything is filtered, so an
        // unknown set is never mistaken for an empty one.
        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: periodStart,
            periodEnd: periodEnd,
            calculationKind: "forward-calc",
            ruleSet: _classificationProvider.GetClassifications().Where(c => EvaluatedRuleIds.Contains(c.RuleId)).ToList(),
            sources: sources,
            options: PlannerOptions.Default,
            enrollment: enrollment,
            profile: profile,
            // ADR-040 D5: the SAME windows that fed the boundary dates above type the
            // segments (null → windowless → all EMPLOYED, byte-identical to pre-S137).
            employmentWindows: employmentWindows);

        return (plan, histories with { SegmentStartProfiles = segmentStartProfiles });
    }

    /// <summary>
    /// S145 Stage B (TASK-14522 contract 1; refinement B1) — the rules payroll evaluates, by result id, and so
    /// the only classifications its planning is handed (<see cref="BuildPlanWithHistoriesAsync"/>): the per-plan
    /// lane's <c>NORM_CHECK_37H</c>, <c>OVERTIME_CALC</c> and <c>FLEX_BALANCE</c> (one timeline call for the whole
    /// plan) and the per-segment lane's <c>SUPPLEMENT_CALC</c>, <c>ON_CALL_DUTY</c> and the absence rule, whose
    /// result id is <c>ABSENCE_CALC</c> (its endpoint is called without a rule id; the id is the Rule Engine's
    /// <c>AbsenceRule.RuleId</c>, which this assembly does not reference).
    ///
    /// <para>
    /// <b>Why (plain language).</b> The planner refuses a month with two or more EMPLOYED parts when the rule
    /// set it is handed holds a rule that must never be cut in two (<c>Reject</c> or <c>AlignedWindow</c>).
    /// The Rule Engine's registry lists every rule it knows, including three that payroll never runs — the
    /// weekly norm check and the two rest-period checks — and those keep "never cut in two". Handed the whole
    /// list, the planner refused payroll's split months over rules payroll does not evaluate. Since S145 B1 the
    /// three rules payroll evaluates over the whole month are <c>TimelineAware</c>: evaluated once across every
    /// part, never split. So planning sees these six, none of which refuses, and a month with a mid-month
    /// change in hours, position or agreement is paid part by part, each part under its own wage-type codes.
    /// </para>
    ///
    /// <para>
    /// <b>Where it applies, and where not.</b> Only planning uses it, so the calculating routes and the raw
    /// routes' guard (<see cref="EnsurePeriodCalculableAsync"/>) plan with the same set. The merge and a
    /// replay's <see cref="PeriodPlanner.FromManifest"/> keep the provider's whole list: the merge sees only
    /// rules that produced results, and <c>FromManifest</c> re-asserts only the rule-side invariants (it never
    /// refuses a split). A rule set handed in by a caller keeps every rule of this list it names, so a fixture
    /// that classifies <c>OVERTIME_CALC</c> as <c>AlignedWindow</c> still makes the planner refuse.
    /// </para>
    ///
    /// <para>
    /// <b>Keep it in step with the lanes.</b> The six ids are the results the two lanes produce. A rule payroll
    /// starts to evaluate must be added here; otherwise planning cannot see how it may be split, and a rule that
    /// must not be split would reach the merge in pieces (where its merger fails the rule, never silently).
    /// One asymmetry to know: planning decides whether to gather snapshots from THIS set
    /// (<c>HasAnySnapshotContract</c>), while a replay's <c>FromManifest</c> re-checks snapshot contracts against
    /// the whole list. No rule declares a contract today, so nothing differs; a future contract on a rule payroll
    /// does not run could make replay refuse a manifest a forward run wrote (5a NOTE N-7 on TASK-14522).
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> EvaluatedRuleIds = new HashSet<string>(StringComparer.Ordinal)
    {
        NormRuleId,
        OvertimeRuleId,
        FlexRuleId,
        SupplementRuleId,
        OnCallRuleId,
        "ABSENCE_CALC",
    };

    /// <summary>
    /// S145 / TASK-14512 (A4) — every date a planned segment can start on: the period start plus every
    /// date of every boundary source that lies inside <c>(periodStart, periodEnd]</c> (the planner's
    /// <c>BoundaryDetector.IsInsidePeriod</c> window). The planner's segment starts are a subset by
    /// construction: <c>BoundaryDetector</c> derives its boundaries from exactly these sources. Listing
    /// every member of <see cref="BoundarySources"/>, including the ones this service leaves empty, keeps
    /// the superset true if one of them is ever hydrated.
    /// </summary>
    private static SortedSet<DateOnly> CandidateSegmentStarts(
        BoundarySources sources, DateOnly periodStart, DateOnly periodEnd)
    {
        var starts = new SortedSet<DateOnly> { periodStart };

        void AddInterior(DateOnly d)
        {
            if (d > periodStart && d <= periodEnd)
                starts.Add(d);
        }

        foreach (var t in sources.OkTransitions) AddInterior(t.Date);
        foreach (var p in sources.AgreementConfigPromotions) AddInterior(p.Date);
        foreach (var p in sources.PositionOverrideEffectiveDates) AddInterior(p.Date);
        foreach (var e in sources.EuWtdRulesetTransitions) AddInterior(e.Date);
        foreach (var a in sources.LocalProfileActivations ?? Array.Empty<(DateOnly, Guid)>()) AddInterior(a.Item1);
        foreach (var d in sources.EmploymentStartedDates ?? Array.Empty<DateOnly>()) AddInterior(d);
        foreach (var d in sources.EmploymentEndedDates ?? Array.Empty<DateOnly>()) AddInterior(d);
        foreach (var d in sources.EmployeeProfileEffectiveDates ?? Array.Empty<DateOnly>()) AddInterior(d);
        foreach (var d in sources.AgreementCodeEffectiveDates ?? Array.Empty<DateOnly>()) AddInterior(d);

        return starts;
    }

    /// <summary>
    /// S145 / TASK-14512 (A4) — the employee's dated record at each candidate segment start the
    /// employee is EMPLOYED on (<see cref="IsEmployedOn"/>: every day, without a window resolver).
    /// Returns <c>null</c> when no <see cref="IEmploymentProfileResolver"/> is wired (direct-construction
    /// fixtures), where the caller's profile is the record (<see cref="DatedWtmKey"/>). A record gap is
    /// kept as a gap (<see cref="DatedProfile.MissingRecord"/>), never thrown here. Since S145 / TASK-14519
    /// a date the local-profile activation key already resolved is taken from
    /// <paramref name="datedResolutions"/> instead of being read again; the result holds exactly the
    /// EMPLOYED candidate starts, as before.
    /// </summary>
    private async Task<IReadOnlyDictionary<DateOnly, DatedProfile>?> ResolveSegmentStartProfilesAsync(
        EmploymentProfile callerProfile,
        IEnumerable<DateOnly> candidateStarts,
        IReadOnlyList<EmploymentWindow>? employmentWindows,
        Dictionary<DateOnly, DatedProfile> datedResolutions,
        CancellationToken ct)
    {
        if (_profileResolver is null)
            return null;

        var resolved = new Dictionary<DateOnly, DatedProfile>();
        foreach (var start in candidateStarts)
        {
            if (!IsEmployedOn(employmentWindows, start))
                continue;
            resolved[start] = await DatedResolutionAsync(callerProfile, start, datedResolutions, ct);
        }

        return resolved;
    }

    /// <summary>
    /// S145 / TASK-14519 — <see cref="TryResolveDatedProfileAsync"/> through the plan's per-date cache, so
    /// planning reads the employee's dated record at most once per date however many planning steps ask.
    /// </summary>
    private async Task<DatedProfile> DatedResolutionAsync(
        EmploymentProfile callerProfile,
        DateOnly asOf,
        Dictionary<DateOnly, DatedProfile> datedResolutions,
        CancellationToken ct)
    {
        if (datedResolutions.TryGetValue(asOf, out var known))
            return known;

        var dated = await TryResolveDatedProfileAsync(callerProfile, asOf, ct);
        datedResolutions[asOf] = dated;
        return dated;
    }

    /// <summary>
    /// S145 / TASK-14519 — the key one local-agreement-profile activation lookup is asked under: the
    /// organisation, agreement code and OK version of the employee's dated record (adjudicator R-20).
    /// </summary>
    private readonly record struct LocalProfileActivationKey(string OrgId, string AgreementCode, string OkVersion);

    /// <summary>
    /// S145 / TASK-14519 (adjudicator R-20, QUAL-203) — the local-agreement-profile activations that split
    /// the period (ADR-017 D9c), asked of the employee's DATED record, never of the request body.
    ///
    /// <para>
    /// <b>Plain language.</b> When an organisation switches on a local agreement profile partway through a
    /// month, that date splits the month (and under the live rule set a split month is refused). A profile
    /// belongs to one (organisation, agreement, OK version), so the question "did a profile switch on?" is
    /// only meaningful for the agreement and OK version the employee actually had on each day. Before this
    /// task it was asked once, with the request body's organisation, agreement and OK version; every other
    /// planning input already came from the dated record (R-10, R-11, R-12, A4), so a stale body for a past
    /// month could invent a boundary (refusing the month for nothing) or miss one (calculating as one piece a
    /// month that should split), and the manifest's cause summary would record the wrong reason. No paid
    /// figure depends on it: the Rule Engine applies no local profile, so a profile is a boundary source only.
    /// A replay never comes here (it loads the frozen plan and never plans).
    /// </para>
    ///
    /// <para>
    /// <b>The intervals.</b> The period is cut at every interior agreement-code date (the
    /// <c>user_agreement_codes</c> effective-from dates inside <c>(periodStart, periodEnd]</c>) and every
    /// interior OK transition. Those are the only dates on which the record's agreement or OK version can
    /// change, so each interval has one key. Both lists are read before the planner runs and neither
    /// depends on activations, so there is no circularity. One lookup keyed on the period start alone would
    /// ask only the first interval's history and miss an activation after an interior change. An agreement
    /// change ON the period start is not interior: the record at the period start already carries the new
    /// code.
    /// </para>
    ///
    /// <para>
    /// <b>Each interval's key</b> (<see cref="IntervalActivationKeyAsync"/>): the dated record's organisation
    /// and agreement code, and the OK version of the day it is read on (<see cref="OkVersionResolver"/>, a
    /// pure function of the date, ADR-003). The organisation is the one the resolver puts on the record: today
    /// the employee's primary organisation, which is not yet dated (organisation history is a named
    /// follow-up), so it is the same for every interval. The record is read on the interval's first day, or,
    /// when the employee is not employed that day, on the first employment start inside the interval (a starter
    /// hired mid-month has no record before the hire). An interval with no employed day, with a record gap on
    /// that day, or whose record carries no organisation asks nothing: ADR-017 D9c's "no organisation, no
    /// profile boundaries", now with the record's organisation. (A gap there is refused by evaluation as
    /// <c>employment_record_gap</c>, because that day is an EMPLOYED segment start.) Without a profile
    /// resolver the caller's profile is the record, with the date's OK version, as for the wage-type key
    /// (<see cref="DatedWtmKey"/>).
    /// </para>
    ///
    /// <para>
    /// <b>One lookup per run of an unchanged key.</b> Consecutive intervals with the same key are asked as
    /// one run: (the run's first day, the run's last day]. A cut where nothing in the key changes (a
    /// re-dated agreement-code row with the same code) therefore asks exactly what one lookup over the whole
    /// run would. The lower bound is strict, as the repository's is at the period start: the profile active
    /// on a run's first day is that run's starting state, not a boundary. That first day is a boundary
    /// already (the period start, or the agreement change or OK transition that started the new key), so
    /// no split is lost, and the segment keeps the cause that actually changed the key. The runs are
    /// disjoint and ascending, so the union of their activations is in date order.
    /// </para>
    ///
    /// <para>
    /// Returns <c>null</c> when the repository is not wired or no interval has a key, exactly as the single
    /// lookup returned <c>null</c> without a repository or an organisation; otherwise the union (possibly
    /// empty).
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)>?> ReadLocalProfileActivationsAsync(
        EmploymentProfile callerProfile,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<(DateOnly Date, string FromVersion, string ToVersion)> okTransitions,
        IReadOnlyList<DateOnly>? agreementCodeEffectiveDates,
        IReadOnlyList<EmploymentWindow>? employmentWindows,
        IReadOnlyList<DateOnly>? employmentStartedDates,
        Dictionary<DateOnly, DatedProfile> datedResolutions,
        CancellationToken ct)
    {
        if (_localAgreementProfileRepo is null)
            return null;

        // The interior dates on which the record's agreement or OK version can change.
        var cuts = new SortedSet<DateOnly>();
        foreach (var transition in okTransitions)
        {
            if (transition.Date > periodStart && transition.Date <= periodEnd)
                cuts.Add(transition.Date);
        }
        foreach (var date in agreementCodeEffectiveDates ?? Array.Empty<DateOnly>())
        {
            if (date > periodStart && date <= periodEnd)
                cuts.Add(date);
        }

        var intervalStarts = new List<DateOnly>(cuts.Count + 1) { periodStart };
        intervalStarts.AddRange(cuts);

        // Each interval's key, merged into runs of an unchanged key. An interval with no key ends a run.
        var runs = new List<(LocalProfileActivationKey Key, DateOnly AfterExclusive, DateOnly ToInclusive)>();
        LocalProfileActivationKey? previousKey = null;
        for (var i = 0; i < intervalStarts.Count; i++)
        {
            var first = intervalStarts[i];
            var last = i + 1 < intervalStarts.Count ? intervalStarts[i + 1].AddDays(-1) : periodEnd;
            var key = await IntervalActivationKeyAsync(
                callerProfile, first, last, employmentWindows, employmentStartedDates, datedResolutions, ct);

            if (key is { } current)
            {
                if (previousKey == current)
                    runs[^1] = runs[^1] with { ToInclusive = last };
                else
                    runs.Add((current, first, last));
            }

            previousKey = key;
        }

        if (runs.Count == 0)
            return null;

        var activations = new List<(DateOnly EffectiveFrom, Guid ProfileId)>();
        foreach (var run in runs)
        {
            activations.AddRange(await _localAgreementProfileRepo.GetActivationsInPeriodAsync(
                run.Key.OrgId,
                run.Key.AgreementCode,
                run.Key.OkVersion,
                run.AfterExclusive,
                run.ToInclusive,
                ct));
        }

        // Already in date order (disjoint ascending runs, each read ordered by effective_from); the stable
        // sort states the contract rather than relying on it.
        return activations.OrderBy(a => a.EffectiveFrom).ToList();
    }

    /// <summary>
    /// S145 / TASK-14519 — the activation key of the interval <c>[first, last]</c> (see
    /// <see cref="ReadLocalProfileActivationsAsync"/>), or <c>null</c> when the interval asks nothing. The
    /// record is read on the interval's first day when the employee is EMPLOYED on it, otherwise on the first
    /// employment start inside the interval. Either day is an EMPLOYED candidate segment start
    /// (<see cref="CandidateSegmentStarts"/> holds the period start, every interior OK transition, agreement
    /// date and employment start), so the read is one the segment-start resolution makes anyway and the
    /// shared cache serves it once. Within an interval the key cannot change (its edges are the only dates the
    /// agreement or OK version changes on, and the organisation is not dated), so any employed day gives it.
    /// </summary>
    private async Task<LocalProfileActivationKey?> IntervalActivationKeyAsync(
        EmploymentProfile callerProfile,
        DateOnly first,
        DateOnly last,
        IReadOnlyList<EmploymentWindow>? employmentWindows,
        IReadOnlyList<DateOnly>? employmentStartedDates,
        Dictionary<DateOnly, DatedProfile> datedResolutions,
        CancellationToken ct)
    {
        DateOnly? keyDay = IsEmployedOn(employmentWindows, first) ? first : null;
        if (keyDay is null)
        {
            foreach (var start in employmentStartedDates ?? Array.Empty<DateOnly>())
            {
                if (start > first && start <= last
                    && (keyDay is null || start < keyDay.Value)
                    && IsEmployedOn(employmentWindows, start))
                {
                    keyDay = start;
                }
            }
        }

        if (keyDay is not { } day)
            return null; // no employed day in the interval: no employee agreement to ask under

        var dated = await DatedResolutionAsync(callerProfile, day, datedResolutions, ct);
        if (dated.Profile is not { } record)
            return null; // a record gap: evaluation refuses the month (employment_record_gap)

        if (string.IsNullOrEmpty(record.OrgId))
            return null; // ADR-017 D9c: no organisation, no profile boundaries

        return new LocalProfileActivationKey(
            OrgId: record.OrgId,
            AgreementCode: record.AgreementCode,
            OkVersion: OkVersionResolver.ResolveVersion(day));
    }

    /// <summary>
    /// S145 / TASK-14512 (A4) — the planner's <c>WtmNaturalKey</c> hydrator: the key in force at an
    /// EMPLOYED segment's start, from the employee's dated record (<paramref name="segmentStartProfiles"/>):
    /// its date-resolved OK version, agreement code and position (a null position normalized to "").
    /// <list type="bullet">
    /// <item>No profile resolver (<paramref name="segmentStartProfiles"/> is <c>null</c>; direct-construction
    /// fixtures): the caller's profile is the only record there is — the same profile the segment is
    /// evaluated with on that path — with the OK version of the segment's start (ADR-003).</item>
    /// <item>A record gap at the segment start: <c>null</c>. Evaluation refuses the month
    /// (<c>employment_record_gap</c>) when it reaches that segment's profile, before any rule call and long
    /// before the key would be read, so no line is ever mapped without a key.</item>
    /// <item>A segment start that was not resolved at all would mean the candidate dates missed a
    /// boundary — a defect in this service, never a month to pay: it throws.</item>
    /// </list>
    /// </summary>
    private static WtmNaturalKey? DatedWtmKey(
        EmploymentProfile callerProfile,
        DateOnly segmentStart,
        IReadOnlyDictionary<DateOnly, DatedProfile>? segmentStartProfiles)
    {
        if (segmentStartProfiles is null)
        {
            return new WtmNaturalKey(
                OkVersion: OkVersionResolver.ResolveVersion(segmentStart),
                AgreementCode: callerProfile.AgreementCode,
                Position: callerProfile.Position ?? "");
        }

        if (!segmentStartProfiles.TryGetValue(segmentStart, out var dated))
        {
            throw new InvalidOperationException(
                "PeriodCalculationService planning invariant violated: an EMPLOYED segment starts on a date " +
                "for which no dated employment record was resolved, so its wage-type key cannot be hydrated. " +
                "The candidate segment starts must cover every boundary source the planner uses " +
                $"(CandidateSegmentStarts). EmployeeId={callerProfile.EmployeeId}.");
        }

        return dated.Profile is { } record
            ? new WtmNaturalKey(
                OkVersion: record.OkVersion,
                AgreementCode: record.AgreementCode,
                Position: record.Position ?? "")
            : null;
    }

    // -------------------------------------------------------------------
    // Replay primitive (ADR-016 D10) — S145 TASK-14513: frozen inputs, verify and refuse
    // -------------------------------------------------------------------

    /// <summary>
    /// Replays a stored calculation by its manifest id alone: re-runs it on exactly the inputs it had and
    /// returns the result, or refuses BY NAME with <see cref="ReplayRefusedException"/> when it cannot do that
    /// faithfully (S145 / TASK-14513, refinement A7; rulings R14, R15, R41, R42).
    ///
    /// <para>
    /// <b>Plain language.</b> A payslip must be provable after the fact. A replay never asks today's world
    /// anything that could have changed since the month was calculated. It reads what the calculation recorded
    /// about its own inputs on the manifest (<see cref="PlanInputs"/>), re-reads the hours rows the calculation
    /// recorded (by their event ids, so a row the employee has since re-saved is still found), and re-runs the
    /// evaluation. Inputs that are data are checked against their record; inputs that are code (agreement
    /// configs, the arithmetic version) are checked through the fingerprint and marker the Rule Engine returns.
    /// Anything that no longer matches refuses the replay by name instead of producing a different figure.
    /// </para>
    ///
    /// <para>
    /// <b>The steps, in order</b> (a replay writes nothing, so no refusal leaves a trace):
    /// <list type="number">
    /// <item>the never-silent hours-reader check (R25), before any read;</item>
    /// <item>the manifest load (the projection first, the event store as fallback); an unknown id is an
    /// <see cref="InvalidOperationException"/>, as before;</item>
    /// <item>no (complete) frozen inputs on the manifest — every pre-S145 manifest, the ADR-016:242 exception —
    /// → <see cref="ReplayRefusedException.ManifestWithoutFrozenInputs"/>;</item>
    /// <item>the plan from the manifest (<see cref="PeriodPlanner.FromManifest"/>), and the evaluation inputs from
    /// the frozen pieces and cutoff alone (<see cref="FrozenEvaluationInputs"/>);</item>
    /// <item>the recorded hours rows, re-read by event id (<see cref="IRecordedHoursReader.ReadByEventIdsAsync"/>,
    /// current or superseded); a genuinely absent id → <see cref="ReplayRefusedException.EntriesMissing"/>;</item>
    /// <item>the digests recomputed over the rows as re-read →
    /// <see cref="ReplayRefusedException.EntriesDigestMismatch"/> /
    /// <see cref="ReplayRefusedException.AbsencesDigestMismatch"/>;</item>
    /// <item>evaluation (<see cref="EvaluateAsync"/>, no manifest minted), on the frozen opening balance;</item>
    /// <item>the engine's marker, then its fingerprints, compared with the record →
    /// <see cref="ReplayRefusedException.EvaluationModelChanged"/> /
    /// <see cref="ReplayRefusedException.ConfigChanged"/>.</item>
    /// </list>
    /// A failed rule-engine call returns the refused result (<c>Success == false</c>, no figure; S145 ruling
    /// R-6), exactly as a forward calculation does: an engine that cannot be reached proves nothing either way.
    /// </para>
    ///
    /// <para>
    /// <b>Evaluation only, never admission (R42).</b> A replay proves what WAS calculated. Whether the previous
    /// month is approved TODAY, today's employment window, today's agreement or profile history and today's
    /// hours rows cannot change whether that result is reproducible. So a replay never consults the
    /// <see cref="IPriorPeriodApprovalReader"/>, the agreement-code repository, the
    /// <see cref="IEmploymentWindowResolver"/>, the <see cref="IEmploymentProfileResolver"/> or
    /// <see cref="IRecordedHoursReader.ReadCurrentAsync"/>, and a replay on a service built without an approval
    /// reader succeeds. The planner's refusal, the week check and the months-in-order check are calculation
    /// rules.
    /// </para>
    ///
    /// <para>
    /// <b>The signature (R14; ADR-016:149).</b> The earlier overloads took the caller's profile and opening
    /// balance; this one takes neither, restoring the inputs-free signature ADR-016 D10 declares. It also
    /// replaces the inputs-free envelope overload, which evaluated nothing and returned an empty result.
    /// </para>
    /// </summary>
    /// <param name="manifestId">The manifest of the calculation to replay.</param>
    /// <param name="authorizationHeader">Forwarded to the Rule Engine calls.</param>
    /// <param name="correlationId">Forwarded to the Rule Engine calls.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The replayed result; every rule result and export line carries the original manifest id.</returns>
    /// <exception cref="ReplayRefusedException">
    /// The replay cannot reproduce the calculation faithfully; <see cref="ReplayRefusedException.Reason"/> names why.
    /// </exception>
    public async Task<PeriodCalculationResult> ReplayAsync(
        Guid manifestId,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        // S145 TASK-14508 (R25): never silent, before any read (the manifest load is a read).
        var hoursReader = RequireHoursReader(nameof(ReplayAsync));

        // S146 TASK-14604 (R-21 (c)): the load goes through the manifest store — the projection first, the event
        // store as fallback, the found-row rule (R-17) unchanged — so a fixture replays without a database.
        var manifest = await _manifestStore.TryLoadAsync(manifestId, ct)
            ?? throw new InvalidOperationException(
                $"Manifest {manifestId} not found in segment_manifests projection or event store. " +
                "Cannot replay.");

        // 1. The frozen inputs. A manifest without them is refused by name, never recomputed from live data.
        var frozen = manifest.PlanInputs is { } recorded && IsComplete(recorded)
            ? recorded
            : throw Refuse(
                ReplayRefusedException.ManifestWithoutFrozenInputs, manifestId, manifest.EmployeeId,
                manifest.PlanInputs is null
                    ? "the manifest carries no frozen inputs (a manifest written before S145, or a projection row whose plan_inputs_jsonb is NULL)"
                    : "the manifest's frozen inputs are incomplete (a member is null or empty)");

        // 2. The plan (today's rule set re-asserts the D9 invariants, as on a forward calculation) and the
        //    evaluation inputs from the frozen pieces and cutoff alone: no employment window, no profile or
        //    agreement history, no dated profile read (R41, R42).
        var plan = PeriodPlanner.FromManifest(manifest, _classificationProvider.GetClassifications());
        var inputs = FrozenEvaluationInputs(plan, frozen);

        // 3. The hours: exactly the recorded rows, re-read by event id (current OR superseded), then verified.
        var recordedHours = await hoursReader.ReadByEventIdsAsync(
            plan.EmployeeId, frozen.EntryEventIds, frozen.AbsenceEventIds, ct);
        EnsureRecordedRowsPresent(plan, frozen, recordedHours);
        EnsureHoursDigestsMatch(plan, frozen, recordedHours);

        // 4. Evaluation only (R42), on the frozen opening balance. emitAuditEvents:false — a replay re-evaluates
        //    but does NOT mint a new SegmentManifestCreated or PeriodCalculationCompleted event (ADR-016 D10
        //    immutability; Codex sprint-end review). The result's agreement comes from the frozen pieces by the
        //    same derivation a forward calculation uses (R-12), so a replay needs no caller profile (R14).
        //    S146 TASK-14605 (R-33 item 2): the frozen provenance goes with it, so the replay's result carries it
        //    and an UNTRACKED replay drops FLEX_PAYOUT as the forward run did. The ledger is never read (R42).
        var outcome = await EvaluateAsync(
            plan, inputs, recordedHours, plan.EmployeeId,
            frozen.OpeningFlexBalance, frozen.OpeningFlexProvenance, authorizationHeader, correlationId,
            emitAuditEvents: false, ct);

        // A failed engine call refused the result (R-6): there is no engine answer to compare.
        if (!outcome.Result.Success || outcome.PlanInputs is not { } replayed)
            return outcome.Result;

        // 5. The code: the engine's marker and fingerprints must still be the recorded ones.
        EnsureEvaluationModelUnchanged(plan, frozen, replayed);
        EnsureConfigUnchanged(plan, frozen, replayed);

        return outcome.Result;
    }

    /// <summary>
    /// True when every member of a loaded <see cref="PlanInputs"/> holds a usable value. This is the semantic
    /// half of the completeness check: a MISSING member already fails the decode (<c>[JsonRequired]</c>, S145
    /// ruling R-18: on the projection row it is refused by name by <see cref="PostgresSegmentManifestStore.TryLoadAsync"/>;
    /// on the event the stream read fails, see the same method), but a member written as an explicit
    /// JSON <c>null</c> list or an empty digest still decodes; it is refused by the same name
    /// (<see cref="ReplayRefusedException.ManifestWithoutFrozenInputs"/>) rather than crashing later. An empty
    /// list is complete (no rows read; no fingerprint for a plan that made no engine call).
    /// </summary>
    private static bool IsComplete(PlanInputs planInputs) =>
        planInputs.Pieces is not null
        && planInputs.EntryEventIds is not null
        && planInputs.AbsenceEventIds is not null
        && planInputs.ConfigFingerprints is not null
        && !string.IsNullOrEmpty(planInputs.EntriesDigest)
        && !string.IsNullOrEmpty(planInputs.AbsencesDigest)
        && !string.IsNullOrEmpty(planInputs.EvaluationModel);

    /// <summary>
    /// S145 / TASK-14513 — a replay's <see cref="EvaluationInputs"/>, from its manifest's frozen
    /// <see cref="PlanInputs"/> alone (refinement A7: both lanes build the profile from the piece covering the
    /// day):
    /// <list type="bullet">
    /// <item>the per-plan lane sends the frozen pieces exactly as recorded, context days included;</item>
    /// <item>each EMPLOYED segment's profile, which the per-segment lane sends and the wage-type mapping stamps
    /// on its lines, is built from the frozen piece covering the segment's start (<see cref="ProfileFromPiece"/>).
    /// The dated profile read a forward calculation makes at the segment start is not made;</item>
    /// <item>the settlement cutoff is the frozen one (R41), never re-derived from the employment window.</item>
    /// </list>
    /// A NOT_EMPLOYED segment gets no profile and is not evaluated, as in a forward calculation (ADR-040 D10). A
    /// frozen record with no piece covering an EMPLOYED segment's start cannot be replayed faithfully, so it is
    /// refused as a manifest without (complete) frozen inputs. A forward calculation always freezes one piece per
    /// EMPLOYED segment, starting on that segment's first day, so that is a damaged record, never a month.
    /// </summary>
    private EvaluationInputs FrozenEvaluationInputs(PlannedCalculation plan, PlanInputs frozen)
    {
        var segmentProfiles = new EmploymentProfile?[plan.Segments.Count];
        for (int s = 0; s < plan.Segments.Count; s++)
        {
            var segment = plan.Segments[s];
            if (segment.EmploymentStatus == EmploymentWindowStatus.NOT_EMPLOYED)
                continue;

            // The segment's index, never its date, goes into the refusal: an EMPLOYED segment's first day can
            // be a hire date (ADR-040 D7).
            var piece = PieceCovering(frozen.Pieces, segment.StartDate)
                ?? throw Refuse(
                    ReplayRefusedException.ManifestWithoutFrozenInputs, plan.ManifestId, plan.EmployeeId,
                    $"no frozen piece covers the first day of EMPLOYED segment {s}");
            segmentProfiles[s] = ProfileFromPiece(plan.EmployeeId, piece, segment.StartDate);
        }

        return new EvaluationInputs(frozen.Pieces, segmentProfiles, frozen.SettlementCutoff);
    }

    /// <summary>
    /// S145 / TASK-14513 — the employment profile a replay evaluates a segment with, built from the frozen piece
    /// covering it: the piece's part-time fraction, agreement code and position, and the OK version of
    /// <paramref name="asOf"/> (a pure function of the date, ADR-003). These are the profile fields the Rule
    /// Engine reads (agreement, OK version and position select the config; the absence rule scales by the
    /// fraction); the wage-type mapping reads only the employee id.
    ///
    /// <para>
    /// <b>What is not frozen.</b> The employment category and the organisation are read by no rule and by no
    /// export line, so a piece does not carry them (refinement A7 freezes the per-piece inputs rules read; a rule
    /// that starts reading another field adds it to the piece and bumps the evaluation-model marker). The replay
    /// profile leaves the category empty and the organisation unset rather than guessing values.
    /// </para>
    /// </summary>
    private static EmploymentProfile ProfileFromPiece(string employeeId, FrozenPiece piece, DateOnly asOf) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = piece.AgreementCode,
        OkVersion = OkVersionResolver.ResolveVersion(asOf),
        EmploymentCategory = string.Empty,
        IsPartTime = piece.PartTimeFraction < 1.0m,
        PartTimeFraction = piece.PartTimeFraction,
        Position = piece.Position,
        OrgId = null,
    };

    /// <summary>
    /// S145 / TASK-14513 — the frozen piece of the period's first EMPLOYED segment: the first piece starting on
    /// or after <paramref name="periodStart"/> (the opening week's context pieces all end before it), or
    /// <c>null</c> when the plan has no EMPLOYED segment. It is what the period actually ran under (its
    /// agreement, fraction and position), as opposed to what a caller sent. Two readers take their labels from
    /// it and from nowhere else: <see cref="EvaluateAsync"/> reports its agreement on every result and on the
    /// legacy completed event, forward and replay alike (ruling R-12), and
    /// <c>RetroactiveCorrectionService</c> records its agreement and position on the
    /// <c>RetroactiveCorrectionRequested</c> event (rulings R-10, R-11).
    /// </summary>
    internal static FrozenPiece? FirstEmployedSegmentPiece(IReadOnlyList<FrozenPiece> pieces, DateOnly periodStart)
    {
        foreach (var piece in pieces)
        {
            if (piece.From >= periodStart)
                return piece;
        }
        return null;
    }

    /// <summary>
    /// S145 / TASK-14513 (R14 as amended; A0 constraint 5) — every recorded event id must come back from the
    /// by-id read. That read returns current AND superseded rows, so a row the employee re-saved after the
    /// calculation is found, not missing; only an id with no row at all is refused, as
    /// <see cref="ReplayRefusedException.EntriesMissing"/>, for a time-entry id and an absence id alike (the
    /// refinement names one refusal for the hours rows). It runs before the digest check, so an absent row is
    /// named as absent, not as a digest mismatch.
    /// </summary>
    private void EnsureRecordedRowsPresent(PlannedCalculation plan, PlanInputs frozen, RecordedHours reread)
    {
        var entriesFound = reread.Entries.Select(r => r.EventId).ToHashSet();
        var absencesFound = reread.Absences.Select(r => r.EventId).ToHashSet();
        var missingEntries = frozen.EntryEventIds.Count(id => !entriesFound.Contains(id));
        var missingAbsences = frozen.AbsenceEventIds.Count(id => !absencesFound.Contains(id));
        if (missingEntries == 0 && missingAbsences == 0)
            return;

        throw Refuse(
            ReplayRefusedException.EntriesMissing, plan.ManifestId, plan.EmployeeId,
            $"{missingEntries} recorded time-entry row(s) and {missingAbsences} recorded absence row(s) are " +
            "absent from the hours read models");
    }

    /// <summary>
    /// S145 / TASK-14513 (R15) — the digests recomputed over the rows as re-read must equal the recorded ones. A
    /// row whose date, times, hours, activity or absence type changed after the calculation refuses the replay:
    /// <see cref="ReplayRefusedException.EntriesDigestMismatch"/>, then
    /// <see cref="ReplayRefusedException.AbsencesDigestMismatch"/>. Both comparisons live here.
    /// </summary>
    private void EnsureHoursDigestsMatch(PlannedCalculation plan, PlanInputs frozen, RecordedHours reread)
    {
        if (!string.Equals(HoursDigest.ComputeEntriesDigest(reread.Entries), frozen.EntriesDigest, StringComparison.Ordinal))
        {
            throw Refuse(
                ReplayRefusedException.EntriesDigestMismatch, plan.ManifestId, plan.EmployeeId,
                "the recorded time-entry rows no longer hash to the digest recorded with the calculation");
        }

        if (!string.Equals(HoursDigest.ComputeAbsencesDigest(reread.Absences), frozen.AbsencesDigest, StringComparison.Ordinal))
        {
            throw Refuse(
                ReplayRefusedException.AbsencesDigestMismatch, plan.ManifestId, plan.EmployeeId,
                "the recorded absence rows no longer hash to the digest recorded with the calculation");
        }
    }

    /// <summary>
    /// S145 / TASK-14513 (refinement A7) — the replay's evaluation-model marker (what the engine answers now, or
    /// <see cref="NoEngineCallEvaluationModel"/> again for a plan that makes no call) must equal the recorded one.
    /// An engine whose arithmetic changed is refused, never silently recomputed
    /// (<see cref="ReplayRefusedException.EvaluationModelChanged"/>). Checked before the fingerprints: a new
    /// arithmetic version may also change how configs are fingerprinted, and the marker is then the real cause.
    /// </summary>
    private void EnsureEvaluationModelUnchanged(PlannedCalculation plan, PlanInputs frozen, PlanInputs replayed)
    {
        if (string.Equals(replayed.EvaluationModel, frozen.EvaluationModel, StringComparison.Ordinal))
            return;

        throw Refuse(
            ReplayRefusedException.EvaluationModelChanged, plan.ManifestId, plan.EmployeeId,
            $"the calculation was evaluated by '{frozen.EvaluationModel}' and the Rule Engine now answers " +
            $"'{replayed.EvaluationModel}'");
    }

    /// <summary>
    /// S145 / TASK-14513 (refinement A7) — the config fingerprints the engine returns for the frozen pieces must
    /// equal the recorded ones, in order: agreement configs are code, so a config that changed since the
    /// calculation is refused by name (<see cref="ReplayRefusedException.ConfigChanged"/>) rather than paying a
    /// different figure.
    /// </summary>
    private void EnsureConfigUnchanged(PlannedCalculation plan, PlanInputs frozen, PlanInputs replayed)
    {
        if (replayed.ConfigFingerprints.SequenceEqual(frozen.ConfigFingerprints, StringComparer.Ordinal))
            return;

        throw Refuse(
            ReplayRefusedException.ConfigChanged, plan.ManifestId, plan.EmployeeId,
            "the agreement configs the Rule Engine resolves for the frozen pieces no longer match the recorded " +
            "fingerprints");
    }

    /// <summary>
    /// Logs and returns a replay refusal; the caller throws it. The log line and the message carry the reason,
    /// the manifest id, the employee id and a fixed description only: no hours and no dates (an EMPLOYED
    /// segment's first day can be a hire date, ADR-040 D7).
    /// </summary>
    private ReplayRefusedException Refuse(string reason, Guid manifestId, string employeeId, string detail) =>
        LogAndBuildReplayRefusal(_logger, reason, manifestId, employeeId, detail);

    /// <summary>
    /// The one shape of a replay refusal's log line and message, shared since S146 / TASK-14604 with
    /// <see cref="PostgresSegmentManifestStore"/>, whose manifest load refuses a found projection row with
    /// undecodable frozen inputs (R-17) exactly as this service did when the load lived here.
    /// </summary>
    internal static ReplayRefusedException LogAndBuildReplayRefusal(
        ILogger logger, string reason, Guid manifestId, string employeeId, string detail)
    {
        logger.LogWarning(
            "Replay of manifest {ManifestId} (employee {EmployeeId}) REFUSED: {Reason}: {Detail}. Nothing is " +
            "recomputed from live data and nothing is written.",
            manifestId, employeeId, reason, detail);

        return new ReplayRefusedException(reason, manifestId, $"replay of manifest {manifestId} refused: {detail}.");
    }

    // -------------------------------------------------------------------
    // Audit-context propagation helper (TASK-2008 WARNING fix #2)
    // -------------------------------------------------------------------

    /// <summary>
    /// Stamps the <see cref="PeriodCalculationOutcome.ManifestId"/> of a PERSISTED manifest onto
    /// <see cref="HttpContext.Items"/> so <see cref="AuditLoggingMiddleware"/> picks it up for the
    /// <c>audit_log.details</c> JSONB column (the ADR-016 D10 <c>segment_manifests⋈audit_log</c> linkage).
    /// Endpoints that compute a calculation outcome call this once, after
    /// <see cref="PeriodCalculationOutcome.ThrowIfNotProvable"/> passed, before returning the response.
    ///
    /// <para>
    /// <b>On success only (S146 / TASK-14604, QUAL-205).</b> The id is stamped only when the outcome's
    /// <see cref="AuditState"/> is <see cref="AuditState.Complete"/>, i.e. the manifest's one transaction
    /// committed and the id resolves in <c>segment_manifests</c>. Every other outcome is a no-op: a refused
    /// calculation (<see cref="AuditState.NoManifest"/>, S145 ruling R-6) carries its plan's id, which resolves in
    /// no store, and must not be written into the audit row as if it did; a not-provable outcome never reaches
    /// this call (the route refuses first). So is a <see cref="Guid.Empty"/> id. The degraded-audit-state key
    /// this method stamped before S146 is gone: nobody read it (the middleware reads only the manifest id), and a
    /// degraded state is now refused instead of reported.
    /// </para>
    ///
    /// <para>
    /// Re-stamping the same id overwrites the previous value, which is fine because there is at most one
    /// manifest per HTTP request. The key is <see cref="AuditLoggingMiddleware.ManifestIdItemKey"/>.
    /// </para>
    /// </summary>
    public static void StampAuditContext(HttpContext context, PeriodCalculationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.AuditState != AuditState.Complete || outcome.ManifestId == Guid.Empty)
            return; // no persisted manifest; nothing to surface to the audit chain

        context.Items[AuditLoggingMiddleware.ManifestIdItemKey] = outcome.ManifestId;
    }

    // -------------------------------------------------------------------
    // Per-rule MergeStrategy dispatch (TASK-2008 BLOCKER fix #3)
    // -------------------------------------------------------------------

    /// <summary>
    /// Merges per-segment rule results into a single flat list using each rule's resolved
    /// <see cref="MergeStrategy"/> from <paramref name="ruleSet"/>. Rules that don't appear
    /// in <paramref name="ruleSet"/> default to Concatenate with a warning (indicates a
    /// misconfigured registration or an unwired classification provider).
    ///
    /// <para>
    /// <b>A single-plan rule's merge input (S145 Stage B, TASK-14522 contract 2; R-2, ruled R-75).</b> For a
    /// rule whose resolved strategy is <see cref="MergeStrategyKind.SinglePlanEvaluation"/> (a
    /// <see cref="SplitBehavior.TimelineAware"/> rule) that has a row in <paramref name="timelineRows"/>, the
    /// merger is given that ONE row — the timeline call's answer for the whole plan, unsplit, stamped with the
    /// manifest id — not the per-segment rows <see cref="DistributePerPlanResults"/> cut it into for the
    /// wage-type mapping. The merger passes it through, period figures intact, so its rule "two results is a
    /// contract violation" holds literally, and there is still ONE mapping path (the cut stays). Every other
    /// strategy merges the per-segment rows exactly as before; the one-segment fast path is unchanged (there
    /// the segment's row already holds every line and the period figures). A single-plan rule with no
    /// timeline row (a caller's rule set marking a per-segment rule timeline-aware) merges its per-segment
    /// rows, and two of them fail that rule loudly (below), never silently.
    /// </para>
    /// </summary>
    /// <param name="timelineRows">The timeline call's rows by rule id, or <c>null</c> when no timeline call
    /// was made (a plan with no EMPLOYED segment).</param>
    private List<CalculationResult> MergePerSegmentRuleResults(
        IReadOnlyList<List<CalculationResult>> perSegmentResults,
        IReadOnlyList<RuleClassification> ruleSet,
        string employeeId,
        Guid manifestId,
        IReadOnlyDictionary<string, CalculationResult>? timelineRows = null)
    {
        if (perSegmentResults.Count == 0)
            return new List<CalculationResult>();

        // Single-segment fast path: no merge needed; per-rule strategies are still
        // semantically applied (RejectIfMultipleSegments passes through; Concatenate is
        // a no-op for a one-element list; Custom delegates also pass through). Skipping
        // the loop here saves allocation on the dominant case.
        if (perSegmentResults.Count == 1)
            return new List<CalculationResult>(perSegmentResults[0]);

        // Group by RuleId, preserving first-seen order so the merged list keeps the
        // original rule order from the first segment (NORM_CHECK, SUPPLEMENT, …).
        var ruleOrder = new List<string>();
        var bucket = new Dictionary<string, List<CalculationResult>>(StringComparer.Ordinal);
        foreach (var segmentResults in perSegmentResults)
        {
            foreach (var r in segmentResults)
            {
                if (!bucket.TryGetValue(r.RuleId, out var list))
                {
                    list = new List<CalculationResult>(perSegmentResults.Count);
                    bucket[r.RuleId] = list;
                    ruleOrder.Add(r.RuleId);
                }
                list.Add(r);
            }
        }

        var classificationByRuleId = new Dictionary<string, RuleClassification>(ruleSet.Count, StringComparer.Ordinal);
        foreach (var rc in ruleSet)
            classificationByRuleId[rc.RuleId] = rc;

        var merged = new List<CalculationResult>(ruleOrder.Count);
        foreach (var ruleId in ruleOrder)
        {
            var segments = bucket[ruleId];

            MergeStrategy strategy;
            if (classificationByRuleId.TryGetValue(ruleId, out var classification))
            {
                strategy = classification.MergeStrategy;
            }
            else
            {
                _logger.LogWarning(
                    "No RuleClassification registered for rule '{RuleId}' (manifest {ManifestId}, employee {EmployeeId}); " +
                    "defaulting per-segment merge to Concatenate. This indicates a misconfigured rule or an unwired " +
                    "IRuleClassificationProvider — the cross-domain dep on Rule Engine /api/rules/classifications " +
                    "must be resolved (TASK-2010).",
                    ruleId, manifestId, employeeId);
                strategy = MergeStrategy.Concatenate;
            }

            // S145 Stage B (TASK-14522 contract 2; R-75): a single-plan rule is merged from the timeline
            // call's own row (see the summary), never from the per-segment pieces of the mapping cut.
            IReadOnlyList<CalculationResult> mergeInput = segments;
            if (strategy.Kind == MergeStrategyKind.SinglePlanEvaluation
                && timelineRows is not null
                && timelineRows.TryGetValue(ruleId, out var timelineRow))
            {
                mergeInput = new[] { WithManifestId(timelineRow, manifestId) };
            }

            // Per-rule custom merge delegates: the planner's contract is that any rule with
            // SplitBehavior.Mergeable carries MergeStrategy.Custom (or UnionDedupe for
            // compliance). Multi-week / annual norm sums hours across segments. (Flex left
            // this path at S145 B1: it is TimelineAware → SinglePlanEvaluation.) The actual merge arithmetic for these rules is
            // out of S20 wave 1's scope (TASK-2009 wires per-rule delegates as part of
            // RetroactiveCorrectionService cleanup); for now we provide a defensive
            // single-segment-passthrough fallback that preserves the segment-0 row when
            // only one segment exists, and otherwise concatenates with a warning. The
            // CalculationResultMerger.Apply() Custom path throws when no delegate is
            // supplied — wrapping that in a fallback keeps single-segment plans working
            // without TASK-2009 landing first. Since S145 Stage B no rule payroll evaluates is
            // Custom in the live registry (flex is SinglePlanEvaluation; the multi-week and annual
            // norm ids are not evaluated by payroll), so a split month reaches this fallback only
            // under a caller's rule set that classifies one of payroll's rules Mergeable (a test
            // fixture).
            CalculationResult mergedResult;
            try
            {
                mergedResult = CalculationResultMerger.Apply(
                    mergeInput,
                    strategy,
                    customMerge: strategy.Kind == MergeStrategyKind.Custom
                        ? FallbackCustomMerge
                        : null);
            }
            catch (PlannerInvariantViolation ex)
            {
                // RejectIfMultipleSegments fired with > 1 segment, SinglePlanEvaluation was handed
                // > 1 row (a single-plan rule with no timeline row, S145 Stage B), or Custom without
                // delegate and our fallback failed: surface the failure as a single failed
                // CalculationResult rather than crashing the whole calculation. The error
                // message already contains the rule id and the contract breach.
                _logger.LogError(ex,
                    "MergeStrategy '{Strategy}' rejected per-segment results for rule '{RuleId}' " +
                    "(manifest {ManifestId}). Calculation continues; failure surfaces on the rule.",
                    strategy.Kind, ruleId, manifestId);
                mergedResult = new CalculationResult
                {
                    RuleId = ruleId,
                    EmployeeId = employeeId,
                    Success = false,
                    LineItems = new List<CalculationLineItem>(),
                    ErrorMessage = ex.Message,
                    ManifestId = manifestId,
                };
            }

            // Re-stamp ManifestId on the merged row — Concatenate and other mergers do not
            // necessarily preserve it (PAT-006 amendment per ADR-016 D10).
            merged.Add(WithManifestId(mergedResult, manifestId));
        }

        return merged;
    }

    /// <summary>
    /// Defensive fallback for the Custom merge path: single-segment plans pass through
    /// unchanged; multi-segment plans concatenate with a warning. Real per-rule custom
    /// delegates (NormCheckRule.MULTI_WEEK / ANNUAL summing; flex left this path at S145 B1) are
    /// wired in TASK-2009 alongside RetroactiveCorrectionService's segmentation migration;
    /// until then, this fallback keeps the dominant single-segment case working.
    /// </summary>
    private static CalculationResult FallbackCustomMerge(IReadOnlyList<CalculationResult> segments)
    {
        if (segments.Count == 1)
            return segments[0];

        // Concatenate is a sensible last-resort merge for line-item-style results; it loses
        // norm-period metadata (intentional — the merger sets those to null) but keeps
        // line items intact. Callers that require true custom semantics (e.g. flex carry)
        // must register a per-rule custom delegate via the IRuleClassificationProvider.
        return CalculationResultMerger.Apply(segments, MergeStrategy.Concatenate);
    }

    // -------------------------------------------------------------------
    // Manifest persistence
    // -------------------------------------------------------------------

    /// <summary>
    /// The manifest of a successful calculation: the plan's segmentation plus, since S145 / TASK-14513, the
    /// <see cref="PlanInputs"/> the evaluation froze (refinement A7), which the event and the projection row
    /// carry beside the segments.
    /// </summary>
    private static SegmentManifest BuildManifest(PlannedCalculation plan, PlanInputs planInputs)
    {
        // Dedupe the boundary-cause names while preserving first-seen order — the projection
        // column boundary_cause_summary is queried by GIN index; predictable order makes
        // human-readability of audit rows better and matches the order BoundaryDetector
        // produced.
        var boundaryCauseSummary = new List<string>(plan.Segments.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seg in plan.Segments)
        {
            var name = seg.BoundaryCause.ToString();
            if (seen.Add(name))
                boundaryCauseSummary.Add(name);
        }

        return new SegmentManifest(
            ManifestId: plan.ManifestId,
            PeriodStart: plan.PeriodStart,
            PeriodEnd: plan.PeriodEnd,
            EmployeeId: plan.EmployeeId,
            CalculationKind: plan.CalculationKind,
            BoundaryCauseSummary: boundaryCauseSummary,
            CreatedAt: DateTimeOffset.UtcNow,
            Segments: plan.Segments,
            PlanInputs: planInputs);
    }

    /// <summary>
    /// S146 / TASK-14604 (QUAL-205; rulings R-21, R-22) — persists a successful calculation's proof through
    /// <see cref="ISegmentManifestStore.PersistAsync"/>: the <see cref="SegmentManifestCreated"/> event, its
    /// <c>segment_manifests</c> projection row and <paramref name="legacyEvent"/>, in ONE transaction through the
    /// ADR-018 outbox. Returns <see cref="AuditState.Complete"/> when that transaction committed, and
    /// <see cref="AuditState.NotPersisted"/> when it did not (logged at Error with the manifest id, so operations
    /// can alert on it). There is no partial state any more: the event, the row and the legacy event commit
    /// together or not at all.
    ///
    /// <para>
    /// <b>Why it returns a state instead of throwing (plain language).</b> The rule outputs are valid either way;
    /// what is missing on <see cref="AuditState.NotPersisted"/> is the proof. The outcome carries both, and the
    /// calculating routes decide through ONE predicate, <see cref="PeriodCalculationOutcome.ThrowIfNotProvable"/>,
    /// which refuses before any payroll write. Cancellation propagates as cancellation.
    /// </para>
    /// </summary>
    private async Task<AuditState> EmitManifestAsync(
        PlannedCalculation plan,
        SegmentManifest manifest,
        PeriodCalculationCompleted legacyEvent,
        Guid? correlationId,
        CancellationToken ct)
    {
        // The SegmentManifestCreated domain event (audit-of-record per ADR-016 D10). Since S145 / TASK-14513
        // it carries the frozen PlanInputs: the event is their record of truth, because the projection is
        // rebuildable from events.
        var manifestEvent = new SegmentManifestCreated
        {
            ManifestId = manifest.ManifestId,
            EmployeeId = manifest.EmployeeId,
            PeriodStart = manifest.PeriodStart,
            PeriodEnd = manifest.PeriodEnd,
            CalculationKind = manifest.CalculationKind,
            BoundaryCauseSummary = manifest.BoundaryCauseSummary,
            CreatedAt = manifest.CreatedAt,
            Segments = manifest.Segments,
            PlanInputs = manifest.PlanInputs,
            CorrelationId = correlationId,
        };

        try
        {
            await _manifestStore.PersistAsync(manifestEvent, manifest, legacyEvent, ct);
            return AuditState.Complete;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "The manifest {ManifestId} of employee {EmployeeId} was NOT persisted: the commit of its one " +
                "transaction (the SegmentManifestCreated outbox row, the segment_manifests row and the " +
                "PeriodCalculationCompleted outbox row) was not confirmed. The calculation is not provable and the " +
                "calculating routes refuse it before any payroll write (503 payroll-manifest-not-persisted).",
                manifest.ManifestId, plan.EmployeeId);
            return AuditState.NotPersisted;
        }
    }

    /// <summary>
    /// The legacy <see cref="PeriodCalculationCompleted"/> event. <paramref name="evaluatedAgreementCode"/> is the
    /// agreement the period was evaluated under, the same single derivation the result reports (S145 R-12). Pure:
    /// since S146 / TASK-14604 it is persisted in the manifest's own transaction by <see cref="EmitManifestAsync"/>
    /// (stream <see cref="PeriodCalculationCompletedStreamId"/>), so it exists exactly when the manifest does.
    /// </summary>
    private static PeriodCalculationCompleted BuildLegacyPeriodCompleted(
        PlannedCalculation plan,
        string employeeId,
        string evaluatedAgreementCode,
        int ruleResultCount,
        int exportLineCount,
        decimal totalHours,
        Guid? correlationId) => new()
        {
            EmployeeId = employeeId,
            PeriodStart = plan.PeriodStart,
            PeriodEnd = plan.PeriodEnd,
            AgreementCode = evaluatedAgreementCode,
            OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
            RuleCount = ruleResultCount,
            ExportLineCount = exportLineCount,
            TotalHours = totalHours,
            CorrelationId = correlationId,
        };

    // -------------------------------------------------------------------
    // Recorded hours — S145 TASK-14508 (R14, R25)
    // -------------------------------------------------------------------

    /// <summary>
    /// The first day of a calculation's hours read: the day BEFORE the Monday of the
    /// Monday–Sunday week holding <paramref name="periodStart"/> (the opening week). The read runs
    /// from here to the period end, so every context day of the opening week is present, plus the
    /// day before it for a night shift whose after-midnight hours land on that Monday (N5 — the
    /// window mirrors ComplianceEndpoints' widened read). Example: October 2026 starts on a
    /// Thursday, its opening week starts Monday 28 September, so the read starts Sunday 27 September.
    /// </summary>
    private static DateOnly HoursReadStart(DateOnly periodStart) =>
        WeeklyContext.WeekStart(periodStart).AddDays(-1);

    /// <summary>
    /// Maps recorded time-entry rows to the <see cref="TimeEntry"/> inputs the lanes consume, one to
    /// one and in read order (outbox id). Pure: the result depends on the rows alone.
    /// <list type="bullet">
    /// <item><c>SourceStintId</c> = the row's immutable event id — the ADR-039 D4 continuity link,
    /// populated at the read boundary that builds the consumed list, exactly as ComplianceEndpoints
    /// does, so a midnight-crossing shift's two normalized halves keep one identity.</item>
    /// <item><c>RegisteredAt</c> = the registration's <c>occurred_at</c> (the TimeEndpoints mapping),
    /// not the type's wall-clock default, so the mapped input is reproducible from the row.</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<TimeEntry> ToTimeEntries(IReadOnlyList<TimeEntryProjectionRow> rows) =>
        rows.Select(r => new TimeEntry
        {
            EmployeeId = r.EmployeeId,
            Date = r.Date,
            Hours = r.Hours,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            TaskId = r.TaskId,
            ActivityType = r.ActivityType,
            AgreementCode = r.AgreementCode,
            OkVersion = r.OkVersion,
            RegisteredAt = r.OccurredAt,
            VoluntaryUnsocialHours = r.VoluntaryUnsocialHours,
            SourceStintId = r.EventId,
        }).ToList();

    /// <summary>
    /// Maps recorded absence rows to the <see cref="AbsenceEntry"/> inputs the lanes consume, one to
    /// one and in read order. The row's <c>feriedage</c> (the vacation-day revaluation, ADR-032) is
    /// not an input of any payroll rule and is not carried.
    /// </summary>
    private static IReadOnlyList<AbsenceEntry> ToAbsenceEntries(IReadOnlyList<AbsenceProjectionRow> rows) =>
        rows.Select(r => new AbsenceEntry
        {
            EmployeeId = r.EmployeeId,
            Date = r.Date,
            AbsenceType = r.AbsenceType,
            Hours = r.Hours,
            AgreementCode = r.AgreementCode,
            OkVersion = r.OkVersion,
        }).ToList();

    // -------------------------------------------------------------------
    // The per-plan lane — S145 TASK-14511 (QUAL-186)
    // -------------------------------------------------------------------

    // The rules each lane evaluates. The per-plan lane's three come back from one timeline call;
    // the per-segment lane makes the other calls (absence has its own endpoint and no rule id).
    private const string NormRuleId = "NORM_CHECK_37H";
    private const string OvertimeRuleId = "OVERTIME_CALC";
    private const string FlexRuleId = "FLEX_BALANCE";
    private const string SupplementRuleId = "SUPPLEMENT_CALC";
    private const string OnCallRuleId = "ON_CALL_DUTY";

    /// <summary>
    /// What a calculation reads about its employee besides the hours, ONCE: the employment window(s)
    /// (<c>null</c> = no window resolver wired, "windowless"; empty = no employed day in the period)
    /// and the <c>employee_profiles</c> / <c>user_agreement_codes</c> effective-from dates from the
    /// opening week's Monday to the period end (<c>null</c> = that repository is not wired). Planning
    /// feeds them to the planner; admission's checks and the per-plan lane's timeline use the same values.
    /// </summary>
    private sealed record PlanHistories(
        IReadOnlyList<EmploymentWindow>? EmploymentWindows,
        IReadOnlyList<DateOnly>? EmployeeProfileEffectiveDates,
        IReadOnlyList<DateOnly>? AgreementCodeEffectiveDates)
    {
        /// <summary>
        /// S145 / TASK-14512 (A4) — the employee's dated record at each EMPLOYED candidate segment start,
        /// as planning resolved it for the segments' wage-type keys; evaluation uses the same resolution as
        /// the segment's profile. <c>null</c> when it was not resolved (a caller-built plan, or no profile
        /// resolver wired): evaluation then resolves each segment's profile itself. (A replay has no histories
        /// at all: its segment profiles come from the frozen pieces, TASK-14513.)
        /// </summary>
        public IReadOnlyDictionary<DateOnly, DatedProfile>? SegmentStartProfiles { get; init; }
    }

    /// <summary>
    /// S145 / TASK-14512 — one dated resolution of the employee's employment record: the profile in force
    /// (date-resolved OK version applied), or a gap, naming the effective-dated table whose row is missing
    /// (<c>employee_profiles</c> or <c>user_agreement_codes</c>; never a date).
    /// </summary>
    private sealed record DatedProfile(EmploymentProfile? Profile, string? MissingRecord);

    /// <summary>
    /// Reads the <see cref="PlanHistories"/> for one calculation.
    /// <list type="bullet">
    /// <item><b>The employment window</b> — over exactly <c>[periodStart, periodEnd]</c>, server side
    /// (ADR-040 D7), as before S145. The resolver hands back each overlapping window unclipped, so the
    /// window covering the period's first day also says which context days of the opening week were
    /// employed; a window that ended BEFORE the period is (rightly) not returned — its last week was
    /// settled by its own month, on its own last day (R41).</item>
    /// <item><b>The profile and agreement-code change dates</b> — widened (S145 TASK-14511) to
    /// <c>[opening week's Monday, periodEnd]</c>: the per-plan lane needs the changes inside the
    /// context days, and TASK-14512's week check needs an agreement change on the 1st or in the
    /// context days. The planner turns only dates inside <c>(periodStart, periodEnd]</c> into
    /// boundaries (<c>BoundaryDetector.IsInsidePeriod</c>), so the extra dates change no plan and are
    /// not filtered twice.</item>
    /// </list>
    /// </summary>
    private async Task<PlanHistories> ReadPlanHistoriesAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        IReadOnlyList<EmploymentWindow>? windows = null;
        if (_employmentWindowResolver is not null)
        {
            windows = await _employmentWindowResolver.GetWindowsAsync(employeeId, periodStart, periodEnd, ct);
        }

        // The repositories' reads are "effective_from > afterExclusive AND <= toInclusive", so the day
        // before the opening Monday makes the read START at the Monday (inclusive).
        var afterExclusive = WeeklyContext.WeekStart(periodStart).AddDays(-1);

        IReadOnlyList<DateOnly>? profileDates = null;
        if (_employeeProfileRepo is not null)
        {
            profileDates = await _employeeProfileRepo.GetEffectiveFromDatesAsync(
                employeeId, afterExclusive: afterExclusive, toInclusive: periodEnd, ct);
        }

        IReadOnlyList<DateOnly>? agreementDates = null;
        if (_userAgreementCodeRepo is not null)
        {
            agreementDates = await _userAgreementCodeRepo.GetEffectiveFromDatesAsync(
                employeeId, afterExclusive: afterExclusive, toInclusive: periodEnd, ct);
        }

        return new PlanHistories(windows, profileDates, agreementDates);
    }

    /// <summary>
    /// The EMPLOYED <b>context days</b> of the month's opening Monday–Sunday week — the days before the
    /// period's first day that this month settles (owner ruling Q3 = F1) — as an inclusive range, or
    /// <c>null</c> when there are none. Shared by the per-plan lane's context pieces
    /// (<see cref="BuildContextPiecesAsync"/>) and the months-in-order check
    /// (<see cref="PriorMonthRequiringApproval"/>), so the check applies exactly when the timeline counts
    /// the previous month's days.
    /// <list type="bullet">
    /// <item>A month opening on a Monday has none.</item>
    /// <item>"Employed?" before "what profile?" (ADR-040 D10): when the period's first segment is not
    /// EMPLOYED, the context days belong to no employment this month continues — a starter hired inside
    /// the month has none.</item>
    /// <item>With employment windows, the days run from the later of the opening Monday and the start of
    /// the window covering the period's first day (a starter hired inside the opening week has the days
    /// from the hire on; one hired on the 1st has none). Without a window resolver (windowless), an
    /// employee employed on the 1st is employed on every context day.</item>
    /// </list>
    /// </summary>
    private static (DateOnly From, DateOnly To)? EmployedContextDays(
        PlannedCalculation plan, IReadOnlyList<EmploymentWindow>? windows)
    {
        var openingMonday = WeeklyContext.WeekStart(plan.PeriodStart);
        if (openingMonday == plan.PeriodStart)
            return null; // the month opens on a Monday: its opening week has no context days

        if (plan.Segments[0].EmploymentStatus != EmploymentWindowStatus.EMPLOYED)
            return null;

        var contextEnd = plan.PeriodStart.AddDays(-1);
        var contextStart = openingMonday;
        if (windows is not null)
        {
            EmploymentWindow? covering = null;
            foreach (var window in windows)
            {
                if (window.Overlaps(plan.PeriodStart, plan.PeriodStart))
                {
                    covering = window;
                    break;
                }
            }

            // ClipTo gives the employed days of [opening Monday, the day before the period], or null.
            if (covering?.ClipTo(openingMonday, contextEnd)?.Start is not { } employedFrom)
                return null;
            contextStart = employedFrom;
        }

        return (contextStart, contextEnd);
    }

    /// <summary>
    /// The timeline pieces for the <b>context days</b>: the days of the month's opening Monday–Sunday
    /// week that fall before the period (owner ruling Q3 = F1 — this month settles that week, so its
    /// earlier days count toward the week's overtime judgement; they never enter this month's norm
    /// total, <c>NORMAL_HOURS</c> lines or flex, which the engine's core enforces, R6).
    ///
    /// <para>
    /// <b>Which context days.</b> Only days in the SAME employment as the period's first day
    /// (<see cref="EmployedContextDays"/>): the first segment must be EMPLOYED, and the days run from
    /// the later of the opening Monday and the covering window's start. A starter hired inside the
    /// month has no context days (their first segment is NOT_EMPLOYED); one hired inside the opening
    /// week gets the days from the hire on. Without a window resolver (windowless) an employee employed
    /// on the 1st is employed on every context day. A month opening on a Monday has none.
    /// </para>
    ///
    /// <para>
    /// <b>Where a piece ends.</b> At every profile or agreement-code change inside the context days,
    /// so each piece carries one fraction, agreement and position; each piece's profile is resolved
    /// at its first day by the same dated resolution as a segment's (<see cref="ResolveProfileAtAsync"/>),
    /// fail-closed on a missing employment record. A change ON the period's first day needs no
    /// split here: the first segment starts there.
    /// </para>
    /// </summary>
    private async Task<List<FrozenPiece>> BuildContextPiecesAsync(
        PlannedCalculation plan, PlanHistories histories, EmploymentProfile callerProfile, CancellationToken ct)
    {
        var pieces = new List<FrozenPiece>();

        if (EmployedContextDays(plan, histories.EmploymentWindows) is not { } context)
            return pieces;
        var (contextStart, contextEnd) = context;

        // Piece edges: every change strictly after the first context day, up to the last; the
        // period's first day closes the last context piece.
        var edges = new SortedSet<DateOnly>();
        foreach (var d in histories.EmployeeProfileEffectiveDates ?? Array.Empty<DateOnly>())
        {
            if (d > contextStart && d <= contextEnd)
                edges.Add(d);
        }
        foreach (var d in histories.AgreementCodeEffectiveDates ?? Array.Empty<DateOnly>())
        {
            if (d > contextStart && d <= contextEnd)
                edges.Add(d);
        }
        edges.Add(plan.PeriodStart);

        var from = contextStart;
        foreach (var edge in edges)
        {
            var piecesProfile = await ResolveProfileAtAsync(callerProfile, from, plan, ct);
            pieces.Add(new FrozenPiece(
                from, edge.AddDays(-1),
                piecesProfile.PartTimeFraction, piecesProfile.AgreementCode, piecesProfile.Position));
            from = edge;
        }

        return pieces;
    }

    /// <summary>
    /// The employment profile in force on <paramref name="asOf"/> — for an EMPLOYED segment (asked at
    /// its start) and for a context piece (asked at its first day) — or the named
    /// <c>employment_record_gap</c> refusal. Moved out of the per-segment loop in S145 / TASK-14511 so
    /// both uses share one resolution and one fail-closed path; split in TASK-14512 into the resolution
    /// itself (<see cref="TryResolveDatedProfileAsync"/>, which planning also uses for the wage-type key)
    /// and the refusal (<see cref="RecordGap"/>). One change was made in TASK-14511 (S145 ruling R-10):
    /// the TASK-1802 caller-position fallback was retired. A dated <c>employee_profiles</c> row with no
    /// position means "no position" (the base agreement config) and is never filled in from the request's
    /// profile — otherwise a context day before a promotion would carry the new position into the
    /// timeline and the frozen record.
    /// </summary>
    private async Task<EmploymentProfile> ResolveProfileAtAsync(
        EmploymentProfile callerProfile, DateOnly asOf, PlannedCalculation plan, CancellationToken ct)
    {
        var dated = await TryResolveDatedProfileAsync(callerProfile, asOf, ct);
        return dated.Profile ?? throw RecordGap(dated.MissingRecord, callerProfile.EmployeeId, plan);
    }

    /// <summary>
    /// S145 / TASK-14512 (A4) — an EMPLOYED segment's profile: the dated record planning already resolved
    /// at the segment's start for its wage-type key (<see cref="PlanHistories.SegmentStartProfiles"/>), so
    /// the rule engine and the export see the same agreement and position; or, when planning resolved none
    /// (a caller-built plan, no profile resolver), a fresh <see cref="ResolveProfileAtAsync"/>. A replay never
    /// comes here (its profiles are built from the frozen pieces, <see cref="FrozenEvaluationInputs"/>).
    /// A gap planning recorded is refused here exactly as a fresh resolution's gap is.
    /// </summary>
    private async Task<EmploymentProfile> SegmentProfileAsync(
        EmploymentProfile callerProfile,
        DateOnly segmentStart,
        PlannedCalculation plan,
        PlanHistories histories,
        CancellationToken ct)
    {
        if (histories.SegmentStartProfiles is { } dated && dated.TryGetValue(segmentStart, out var resolution))
            return resolution.Profile ?? throw RecordGap(resolution.MissingRecord, callerProfile.EmployeeId, plan);

        return await ResolveProfileAtAsync(callerProfile, segmentStart, plan, ct);
    }

    /// <summary>
    /// The dated resolution itself: the employee's employment record in force on <paramref name="asOf"/>,
    /// with the date's OK version, or a gap naming the missing effective-dated table. It never throws for a
    /// gap and never logs: planning records gaps without refusing (the planner and admission come first,
    /// N1), and every refusal goes through <see cref="RecordGap"/>.
    /// </summary>
    private async Task<DatedProfile> TryResolveDatedProfileAsync(
        EmploymentProfile callerProfile, DateOnly asOf, CancellationToken ct)
    {
        // ADR-023 D1 cutover: resolve the profile from dated employee_profiles
        // (via EmploymentProfileResolver) instead of copying caller-supplied profile.
        // Legacy test-fixture path preserved when resolver is null (refinement
        // cycle 2 Codex W absorption — S29 _localAgreementProfileRepo precedent).
        //
        // ONE CONDITION, ONE NAME (S141 / TASK-14114; see RecordGap for why it is refused and how it is
        // logged). The resolver reports "no complete employment record on this date" two different ways:
        // it RETURNS NULL when no employee_profiles row covers the as-of date, and it THROWS
        // EmployeeProfileNotFoundException when a profile row covers it but no user_agreement_codes row
        // does (its documented data-integrity fail-loud). Both land on the one gap, named with the table
        // an HR administrator's repair actually touches. Neither name is date-shaped.
        //
        // ADR-040 D7 — THE CAUGHT EXCEPTION IS DISCARDED, NOT LOGGED AND NOT WRAPPED. The resolver's own
        // exception carries asOfDate in its Message, and for a starter's first EMPLOYED segment that date
        // IS the hire date. Passing it as ILogger's exception argument (or as an inner exception a handler
        // later renders) would write that employment date straight into the logs. RecordGap's log line
        // replaces the discarded exception as the diagnosis.
        EmploymentProfile resolved;
        if (_profileResolver is null)
        {
            resolved = callerProfile;
        }
        else
        {
            EmploymentProfile? resolvedProfile = null;
            // The initial value covers the NULL return (no employee_profiles row spans the as-of date);
            // the catch below narrows it to the agreement-code hole.
            var missingRecord = "employee_profiles";
            try
            {
                resolvedProfile = await _profileResolver.GetByEmployeeIdAtAsync(
                    callerProfile.EmployeeId, asOf, ct);
            }
            catch (EmployeeProfileNotFoundException)
            {
                missingRecord = "user_agreement_codes";
            }

            if (resolvedProfile is null)
                return new DatedProfile(Profile: null, MissingRecord: missingRecord);

            resolved = resolvedProfile;
        }

        // OkVersion server-resolution overlay (ADR-003: the OK version is a pure function of the
        // date). S137 (TASK-13703, QUAL-147) made the resolver return the DATE-resolved OK version
        // for the as-of date, so on the DI-wired path this overlay is a no-op — it is KEPT as
        // defense-in-depth for the nullable-resolver fixture path (the caller-supplied profile,
        // whose OkVersion may be stale for a straddling period) and as a guard should the resolver
        // ever regress to a live read.
        var okVersion = OkVersionResolver.ResolveVersion(asOf);
        if (!string.Equals(resolved.OkVersion, okVersion, StringComparison.Ordinal))
            resolved = resolved with { OkVersion = okVersion };

        // S145 R-10: no position fallback. A dated row's null Position means "no position" (the base
        // agreement config — ADR-040 D4) and is never substituted from the request's profile. The
        // resolver-free path above keeps the caller's WHOLE profile by construction, position included.
        return new DatedProfile(resolved, MissingRecord: null);
    }

    /// <summary>
    /// Logs and returns the <c>employment_record_gap</c> refusal for a calculation whose employment record
    /// does not cover a day it must evaluate. The caller throws it.
    /// </summary>
    private EmployeeProfileNotFoundException RecordGap(string? missingRecord, string employeeId, PlannedCalculation plan)
    {
        // ADR-040 D7 (S137 Step-7a Codex cycle-2 WARNING, absorbed): the resolver is asked at
        // the as-of day (the correct D10 as-of), but the fail-closed exception is anchored on
        // plan.PeriodStart — CALLER INPUT. For a starter's first EMPLOYED segment, the as-of IS the
        // hire date, and EmployeeProfileNotFoundException embeds its as-of date in the message,
        // which reaches exception logs and could reach a detailed 500 body — an employment date
        // must not leak through either. Diagnostics keep the employee id + the period.
        //
        // ── S141 / TASK-14114 — THE FAIL-CLOSED PATH IS NOW CAUGHT AND NAMED ────────────────
        // WHY THIS CHANGED. Before S141 this branch could only be reached by a seeding or
        // backfill defect, so an anonymous unhandled exception was an acceptable "should never
        // happen". S141 lets HR date an employment change into the FUTURE, which makes "no
        // employment record covers this date" a state the PRODUCT ITSELF can now produce — a
        // record ended in September whose replacement does not start until November leaves the
        // employee with no record describing them today. From then on every payroll calculation
        // touching that hole would surface as an unnamed crash, and whoever read the log could
        // not tell a broken server from an employee whose records have a gap. Failing closed
        // was always right; failing closed ANONYMOUSLY is what was wrong.
        //
        // ONE CONDITION, ONE NAME — `employment_record_gap`, the same name ComplianceEndpoints
        // uses for the same state (S141 / TASK-14105), so one grep finds every refusal caused
        // by a record gap regardless of which read hit it.
        //
        // STILL FAIL-CLOSED — DELIBERATELY UNCHANGED. No fallback profile is substituted, the
        // day is NOT skipped, and the calculation does not continue on a guess: a payroll
        // figure derived from a guessed agreement code or part-time fraction puts wrong money
        // on a real wage line, which is worse than producing no figure at all. The same
        // EmployeeProfileNotFoundException, anchored on the same caller-supplied
        // plan.PeriodStart, is still thrown and still reaches the caller. (S145 TASK-14511: a
        // context day of the opening week is evaluated too, so a gap there refuses the month
        // the same way — its hours would otherwise be judged under a guessed contract. S145
        // TASK-14512: a gap planning recorded at a segment start is refused here too, at the same
        // point in the calculation as before — after the planner and admission.)
        //
        // The diagnostics carry only the employee id, the replay-stable manifest id, and the CALLER'S
        // OWN period — nothing date-shaped that describes the employment itself.
        _logger.LogError(
            "employment_record_gap: no effective-dated employment record ({MissingRecord}) covers " +
            "this calculation segment for employee {EmployeeId} manifest {ManifestId} period " +
            "{PeriodStart}-{PeriodEnd}. The calculation is REFUSED and no payroll figure is " +
            "produced for this period — a figure derived from a guessed employment record would " +
            "be worse than none. This is a data gap, not a server fault: the employee should " +
            "appear on the HR follow-up 'cannot register' list (HRP-015), which names the missing " +
            "record and whether a scheduled one will close the gap, and an HR administrator " +
            "repairs it there.",
            missingRecord ?? "employee_profiles", employeeId, plan.ManifestId, plan.PeriodStart, plan.PeriodEnd);

        return new EmployeeProfileNotFoundException(employeeId, plan.PeriodStart);
    }

    /// <summary>
    /// The settlement cutoff (ruling R41): the <c>End</c> of the employment window — the inclusive last
    /// employed day — when it is on or before <paramref name="periodEnd"/>, otherwise <c>null</c>. It
    /// comes from the SAME window read that typed the segments. It is deliberately NOT "the last
    /// EMPLOYED day of the period", which is the period end for a stayer and for a leaver on the
    /// month's last day alike: only the window tells them apart, and F1 settles their closing week in
    /// different months. With several windows (re-hire spells; 0-or-1 today) the latest-ending one
    /// decides. No window information (windowless, or an empty list) gives <c>null</c>.
    /// </summary>
    private static DateOnly? SettlementCutoffFrom(IReadOnlyList<EmploymentWindow>? windows, DateOnly periodEnd)
    {
        if (windows is null || windows.Count == 0)
            return null;

        var latest = windows[0];
        foreach (var window in windows)
        {
            if ((window.End ?? DateOnly.MaxValue) > (latest.End ?? DateOnly.MaxValue))
                latest = window;
        }

        return latest.End is { } end && end <= periodEnd ? end : null;
    }

    // -------------------------------------------------------------------
    // The calculability checks — S145 TASK-14512 (Q5 (c); R5(b), R16, R21/R26, R43)
    // -------------------------------------------------------------------

    /// <summary>The one approval status that lets the next month be calculated (R5(b)).</summary>
    private const string ApprovedStatus = "APPROVED";

    /// <summary>
    /// True when <paramref name="day"/> is an EMPLOYED day of <paramref name="windows"/> — inside one of
    /// them, bounds inclusive (ADR-040 D1/D2). Without a window resolver (<c>null</c>) every day is EMPLOYED,
    /// exactly as the planner types every segment of a windowless plan.
    /// </summary>
    private static bool IsEmployedOn(IReadOnlyList<EmploymentWindow>? windows, DateOnly day)
    {
        if (windows is null)
            return true;

        foreach (var window in windows)
        {
            if (window.Overlaps(day, day))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The week check (Q5 (c)), run by admission after the planner and by the raw routes'
    /// <see cref="EnsurePeriodCalculableAsync"/>: throws <see cref="PeriodNotCalculableException"/>
    /// (<see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/>) when an agreement change falls on a
    /// day other than a Monday inside any Monday–Sunday week the month settles (context days included).
    ///
    /// <para>
    /// <b>The pieces.</b> The settled weeks are <see cref="WeeklyContext.SettledWeeks"/> of the period with
    /// the plan's settlement cutoff (R41), so a leaver's last week counts. The change dates are the widened
    /// agreement-code read (from the opening week's Monday to the period end, TASK-14511) — which is what
    /// lets the check see a change ON the 1st, or in the previous month's last days, that the planner cannot
    /// see (neither is inside the period).
    /// </para>
    ///
    /// <para>
    /// <b>A hire is not a change (R43).</b> Before asking the pure, date-only predicate
    /// (<see cref="WeeklyContext.MidWeekAgreementChange"/>), every date <c>d</c> whose previous day
    /// <c>d − 1</c> is not an EMPLOYED day of the employment windows is dropped: that row is the employee's
    /// first agreement row, dated at the hire, not a change between two agreements. Without a window
    /// resolver every day is EMPLOYED and nothing is dropped. (The repository read applies no exclusion of
    /// its own, and its gap-insert path writes a version-1 row at any caller date; the planner has always
    /// absorbed such a row as one EMPLOYED segment, but a date-only predicate would not.)
    /// </para>
    ///
    /// <para>
    /// Pure over the histories: it reads nothing and writes nothing. The exception's message names the
    /// employee, the period and the change date for diagnosis; the host's 422 is built without it.
    /// </para>
    /// </summary>
    private static void EnsureNoMidWeekAgreementChange(PlannedCalculation plan, PlanHistories histories)
    {
        if (histories.AgreementCodeEffectiveDates is not { Count: > 0 } dates)
            return;

        // R43: drop the hire-dated rows — a date whose previous day the employee was not employed on.
        var changes = new List<DateOnly>(dates.Count);
        foreach (var date in dates)
        {
            if (date > DateOnly.MinValue && IsEmployedOn(histories.EmploymentWindows, date.AddDays(-1)))
                changes.Add(date);
        }
        if (changes.Count == 0)
            return;

        var settledWeeks = WeeklyContext.SettledWeeks(
            plan.PeriodStart, plan.PeriodEnd, SettlementCutoffFrom(histories.EmploymentWindows, plan.PeriodEnd));
        if (!WeeklyContext.MidWeekAgreementChange(settledWeeks, changes))
            return;

        // For the diagnostic only: the first change the predicate refuses.
        var offending = changes
            .Where(d => d.DayOfWeek != DayOfWeek.Monday && settledWeeks.Any(w => d >= w.Start && d <= w.End))
            .Min();

        throw new PeriodNotCalculableException(
            PeriodNotCalculableException.AgreementChangeMidWeek,
            $"PeriodCalculationService week check: employee '{plan.EmployeeId}' has an agreement change effective " +
            $"{offending.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} ({offending.DayOfWeek}), " +
            "inside a Monday–Sunday week the period " +
            $"[{plan.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.." +
            $"{plan.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}] settles. A week " +
            "cannot be judged under two agreements (Q5 (c)), so the period cannot be calculated automatically. " +
            $"ManifestId={plan.ManifestId}.");
    }

    /// <summary>
    /// The previous month's pay period (M−1, the calendar month before the period's month) when the
    /// month's opening week holds EMPLOYED days of it — the context days the per-plan lane counts
    /// (<see cref="EmployedContextDays"/>) that lie before the period's calendar month — otherwise
    /// <c>null</c>. Hires on or after the 1st, first months, a month opening on a Monday, and any month
    /// whose employee was not EMPLOYED in M−1 have no such days and need no earlier approval.
    /// </summary>
    private static (DateOnly PeriodStart, DateOnly PeriodEnd)? PriorMonthRequiringApproval(
        PlannedCalculation plan, IReadOnlyList<EmploymentWindow>? windows)
    {
        if (EmployedContextDays(plan, windows) is not { } context)
            return null;

        // For a month-shaped period (it starts on the 1st) every context day is a day of M−1; the check
        // stays exact for a period that starts mid-month, whose context days may lie in its own month.
        var monthStart = new DateOnly(plan.PeriodStart.Year, plan.PeriodStart.Month, 1);
        if (context.From >= monthStart)
            return null;

        return (monthStart.AddMonths(-1), monthStart.AddDays(-1));
    }

    /// <summary>
    /// Months in order (R5(b), R16, R21 as amended by R26), run by admission after the week check and
    /// before the hours read and any rule call. When the opening week holds EMPLOYED days of the previous
    /// month (<see cref="PriorMonthRequiringApproval"/>), that month must be <c>APPROVED</c>:
    /// <c>EMPLOYEE_APPROVED</c> is not enough (the manager has not decided), and no row at all — the case
    /// of a month exported through the raw routes, which write no approval period (R16) — is not approved
    /// either. Otherwise it throws <see cref="PriorMonthNotApprovedException"/> carrying M−1's pay period.
    ///
    /// <para>
    /// <b>Optional but never silent (R21/R26).</b> When the check applies and no
    /// <see cref="IPriorPeriodApprovalReader"/> is registered, it throws
    /// <see cref="InvalidOperationException"/> naming the registration — skipping the check would let a
    /// month be paid on the previous month's unapproved hours. When the check does not apply, a missing
    /// reader is fine. A calculation rule only: replay never comes here (R42).
    /// </para>
    ///
    /// <para>
    /// Returns the period it required (for the export's row-locked recheck, Do 7), or <c>null</c> when the
    /// check did not apply.
    /// </para>
    /// </summary>
    private async Task<(DateOnly PeriodStart, DateOnly PeriodEnd)?> EnsurePriorMonthApprovedAsync(
        PlannedCalculation plan, PlanHistories histories, CancellationToken ct)
    {
        if (PriorMonthRequiringApproval(plan, histories.EmploymentWindows) is not { } priorMonth)
            return null;

        var reader = _priorPeriodApprovalReader ?? throw new InvalidOperationException(
            "PeriodCalculationService was asked to calculate a month whose opening week holds EMPLOYED days of the " +
            "previous month, on a service constructed WITHOUT an IPriorPeriodApprovalReader. Such a month may only " +
            "be calculated once the previous month is APPROVED (S145 R5(b), R21/R26), and that cannot be checked " +
            "without the reader; skipping the check would pay the month on the previous month's unapproved hours, " +
            "so it is refused instead. Fix the wiring: the Payroll host must register IPriorPeriodApprovalReader -> " +
            "ApprovalPeriodPriorPeriodReader in src/Integrations/StatsTid.Integrations.Payroll/Program.cs (beside " +
            "ApprovalPeriodRepository). Test fixtures that calculate such a month must pass an " +
            "IPriorPeriodApprovalReader as the priorPeriodApprovalReader constructor argument (a fake answering " +
            "APPROVED, declared with a comment, when the fixture is not about this check).");

        var status = await reader.GetApprovalStatusAsync(plan.EmployeeId, priorMonth.PeriodStart, priorMonth.PeriodEnd, ct);
        if (!string.Equals(status, ApprovedStatus, StringComparison.Ordinal))
        {
            throw new PriorMonthNotApprovedException(
                priorMonth.PeriodStart,
                priorMonth.PeriodEnd,
                $"PeriodCalculationService months-in-order check: employee '{plan.EmployeeId}' — the period " +
                $"[{plan.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.." +
                $"{plan.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}] opens " +
                "with EMPLOYED days of the previous month, whose approval status is " +
                $"'{status ?? "(no approval row)"}', not {ApprovedStatus}. ManifestId={plan.ManifestId}.");
        }

        return priorMonth;
    }

    /// <summary>True when one of the timeline's <paramref name="pieces"/> covers <paramref name="day"/>.</summary>
    private static bool IsCovered(IReadOnlyList<FrozenPiece> pieces, DateOnly day) =>
        PieceCovering(pieces, day) is not null;

    /// <summary>The first of <paramref name="pieces"/> covering <paramref name="day"/> (bounds inclusive), or <c>null</c>.</summary>
    private static FrozenPiece? PieceCovering(IReadOnlyList<FrozenPiece> pieces, DateOnly day)
    {
        foreach (var piece in pieces)
        {
            if (day >= piece.From && day <= piece.To)
                return piece;
        }
        return null;
    }

    /// <summary>
    /// The per-plan rows assigned to one EMPLOYED segment (<c>null</c> = that row is absent, because the
    /// timeline call failed).
    /// </summary>
    private sealed record PerPlanRows(CalculationResult? Norm, CalculationResult? Overtime, CalculationResult? Flex)
    {
        public static readonly PerPlanRows None = new(null, null, null);
    }

    /// <summary>
    /// Joins the timeline's three results to the plan's EMPLOYED segments, for the wage-type mapping.
    ///
    /// <para>
    /// <b>One EMPLOYED segment</b> (an unsplit month, a hire or a leaver month): the segment receives the
    /// three rows whole, so every line is mapped under that segment.
    /// </para>
    ///
    /// <para>
    /// <b>Several EMPLOYED segments</b> — since S145 Stage B a month the calculating routes pay part by part
    /// (payroll hands the planner only the rules it evaluates, none of which refuses the split): each
    /// EMPLOYED segment receives one row per rule holding the lines dated inside it, so every line is mapped
    /// under the segment whose OK version and wage-type key govern its date. The period-level figures (norm
    /// totals, <c>NormPeriodWeeks</c>) are carried on the LAST EMPLOYED segment's row only, so no merge can
    /// count them twice. These per-segment rows serve the mapping; a <c>SinglePlanEvaluation</c> rule's
    /// report row is merged from the timeline's own unsplit row instead (TASK-14522 contract 2), while a
    /// caller's rule set with another strategy for these rules merges the per-segment rows (a
    /// <c>RejectIfMultipleSegments</c> rule is still rejected by the merger).
    /// </para>
    ///
    /// <para>
    /// <b>A line dated in no EMPLOYED segment refuses (S145 Stage B, TASK-14522 contract 7, ruled R-56).</b>
    /// It throws <see cref="InvalidOperationException"/> naming the rule and the manifest, never the date:
    /// filing the line under a neighbouring segment would map it under the wrong wage-type code and report
    /// success. The engine dates lines only on covered days inside the period, and a user has one employment
    /// spell, so this is unreachable today; the throw turns a future breach of that contract into a 500 with
    /// nothing written (it runs before the per-segment lane, the merge and every emission), forward and on
    /// replay alike.
    /// </para>
    /// </summary>
    private static Dictionary<int, PerPlanRows> DistributePerPlanResults(
        TimelineEvaluationResponse? timeline, PlannedCalculation plan, IReadOnlyList<int> employedIndices)
    {
        var rows = new Dictionary<int, PerPlanRows>(employedIndices.Count);
        if (timeline is null || employedIndices.Count == 0)
        {
            foreach (var s in employedIndices)
                rows[s] = PerPlanRows.None;
            return rows;
        }

        var norm = SplitBySegment(RowOf(timeline, NormRuleId), plan, employedIndices);
        var overtime = SplitBySegment(RowOf(timeline, OvertimeRuleId), plan, employedIndices);
        var flex = SplitBySegment(RowOf(timeline, FlexRuleId), plan, employedIndices);

        foreach (var s in employedIndices)
            rows[s] = new PerPlanRows(norm[s], overtime[s], flex[s]);
        return rows;

        static CalculationResult RowOf(TimelineEvaluationResponse response, string ruleId) =>
            response.Results.Single(r => string.Equals(r.RuleId, ruleId, StringComparison.Ordinal));
    }

    private static Dictionary<int, CalculationResult> SplitBySegment(
        CalculationResult row, PlannedCalculation plan, IReadOnlyList<int> employedIndices)
    {
        var linesBySegment = new Dictionary<int, List<CalculationLineItem>>(employedIndices.Count);
        foreach (var s in employedIndices)
            linesBySegment[s] = new List<CalculationLineItem>();

        foreach (var line in row.LineItems)
            linesBySegment[SegmentIndexForLine(plan, employedIndices, row, line.Date)].Add(line);

        var last = employedIndices[^1];
        var split = new Dictionary<int, CalculationResult>(employedIndices.Count);
        foreach (var s in employedIndices)
            split[s] = CopyWithLines(row, linesBySegment[s], carryPeriodFigures: s == last);
        return split;
    }

    /// <summary>
    /// The EMPLOYED segment a per-plan line of <paramref name="row"/> belongs to: the one containing its
    /// date. <paramref name="employedIndices"/> is in segment (calendar) order.
    ///
    /// <para>
    /// <b>No EMPLOYED segment contains the date → refused (S145 Stage B, TASK-14522 contract 7, ruled
    /// R-56).</b> Before Stage B such a line went to the last EMPLOYED segment starting on or before its
    /// date (else the first): a wrong wage-type code reported as success once a month can hold two
    /// employment spells. It now throws <see cref="InvalidOperationException"/>, the evaluation-time
    /// invariant shape (not <see cref="PlannerInvariantViolation"/>, which would blur "planner" with
    /// "evaluation"; no route maps either, so it surfaces as a 500). The message names the rule and the
    /// manifest, never the date or the employee (a line's date can be an employment date, ADR-040 D7).
    /// </para>
    /// </summary>
    private static int SegmentIndexForLine(
        PlannedCalculation plan, IReadOnlyList<int> employedIndices, CalculationResult row, DateOnly date)
    {
        foreach (var s in employedIndices)
        {
            var segment = plan.Segments[s];
            if (date >= segment.StartDate && date <= segment.EndDate)
                return s;
        }

        throw new InvalidOperationException(
            $"PeriodCalculationService evaluation invariant violated: rule '{row.RuleId}' returned a line " +
            $"dated in no EMPLOYED segment of the plan. ManifestId={plan.ManifestId}.");
    }

    private static CalculationResult CopyWithLines(
        CalculationResult row, List<CalculationLineItem> lines, bool carryPeriodFigures) =>
        new()
        {
            RuleId = row.RuleId,
            EmployeeId = row.EmployeeId,
            Success = row.Success,
            LineItems = lines,
            ErrorMessage = row.ErrorMessage,
            NormPeriodWeeks = carryPeriodFigures ? row.NormPeriodWeeks : null,
            NormHoursTotal = carryPeriodFigures ? row.NormHoursTotal : null,
            ActualHoursTotal = carryPeriodFigures ? row.ActualHoursTotal : null,
            Deviation = carryPeriodFigures ? row.Deviation : null,
            NormFulfilled = carryPeriodFigures ? row.NormFulfilled : null,
            ManifestId = row.ManifestId,
        };

    /// <summary>
    /// One EMPLOYED segment's result list: the per-plan rows assigned to it and its own per-segment
    /// results, in the order the single per-segment evaluation produced before S145 — norm,
    /// supplement, overtime, on-call, absence, flex — so the export lines of a month keep their order.
    /// A row whose call failed is simply absent (never synthesized).
    /// </summary>
    private static List<CalculationResult> AssembleSegmentResults(PerPlanRows perPlan, SegmentLaneResults lane)
    {
        var results = new List<CalculationResult>(6);
        AddIfPresent(perPlan.Norm);
        AddIfPresent(lane.Supplement);
        AddIfPresent(perPlan.Overtime);
        AddIfPresent(lane.OnCall);
        AddIfPresent(lane.Absence);
        AddIfPresent(perPlan.Flex);
        return results;

        void AddIfPresent(CalculationResult? result)
        {
            if (result is not null)
                results.Add(result);
        }
    }

    // -------------------------------------------------------------------
    // Per-segment helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// What the per-segment lane got back for one EMPLOYED segment: the supplement, on-call and
    /// absence results (<c>null</c> where that call failed — a bounded diagnostic is logged and the
    /// calculation is refused, S145 ruling R-6), how many of its calls failed and how many it made.
    /// </summary>
    private sealed record SegmentLaneResults(
        CalculationResult? Supplement,
        CalculationResult? OnCall,
        CalculationResult? Absence,
        int Failures,
        int Attempted);

    /// <summary>
    /// The per-segment lane for one EMPLOYED segment: the rules that are judged entry by entry or
    /// day by day, and so stay per segment — supplement and on-call (<c>/api/rules/evaluate</c>) and
    /// absence (<c>/api/rules/evaluate-absence</c>), run in parallel. Since S145 / TASK-14511 the
    /// norm check, overtime / merarbejde and flex are NOT evaluated here: the per-plan lane evaluates
    /// them once for the whole plan, week by week, and there is no per-segment flex carry any more.
    /// </summary>
    private async Task<SegmentLaneResults> EvaluateSegmentAsync(
        HttpClient client,
        EmploymentProfile segmentProfile,
        IReadOnlyList<TimeEntry> segmentEntries,
        IReadOnlyList<AbsenceEntry> segmentAbsences,
        DateOnly segmentStart,
        DateOnly segmentEnd,
        CancellationToken ct)
    {
        var supplementTask = CallTimeRuleAsync(
            client, SupplementRuleId, segmentProfile, segmentEntries, segmentStart, segmentEnd, ct);
        var onCallTask = CallTimeRuleAsync(
            client, OnCallRuleId, segmentProfile, segmentEntries, segmentStart, segmentEnd, ct);
        var absenceTask = CallAbsenceRuleAsync(
            client, segmentProfile, segmentAbsences, segmentStart, segmentEnd, ct);

        await Task.WhenAll(supplementTask, onCallTask, absenceTask);

        var supplement = await supplementTask;
        var onCall = await onCallTask;
        var absence = await absenceTask;

        var failures = (supplement is null ? 1 : 0) + (onCall is null ? 1 : 0) + (absence is null ? 1 : 0);

        // Three calls: supplement + on-call + absence (counted, not a magic constant elsewhere).
        return new SegmentLaneResults(supplement, onCall, absence, failures, Attempted: 3);
    }

    private async Task<List<PayrollExportLine>> MapSegmentToExportLinesAsync(
        IReadOnlyList<CalculationResult> segmentRuleResults,
        EmploymentProfile segmentProfile,
        PlannedSegment plannedSegment,
        Guid manifestId,
        CancellationToken ct)
    {
        var lines = new List<PayrollExportLine>();

        // ADR-020 D1 / Implications §1 + S29 TASK-2907 (LOCKED — REFINEMENT-s29-phase-4d1.md
        // L122 + L275 + cycle 2 R2-B2): read the wage-type-mapping natural-key triple from
        // the replay-stable segment snapshot, NOT from segmentProfile. The hydrator
        // registered in planning (BuildPlanWithHistoriesAsync) lands a WtmNaturalKey record at
        // Snapshot.Values["WtmNaturalKey"] — since S145 TASK-14512 (A4) the key of the employee's
        // dated record at THIS segment's start. Reading from segmentProfile would revert
        // to the pre-S29 profile-drift risk (the replay of that era took a caller-supplied profile
        // that could differ from the forward-calc profile). Since S145 TASK-14513 a replay takes no
        // profile at all and builds segmentProfile from the frozen piece, but the key still comes from
        // the snapshot: the manifest records it per segment, and that record is what replay reproduces.
        //
        // Two paths reach this site:
        //   - Forward calc: the hydrator just ran in-process; the value is a typed
        //     WtmNaturalKey reference.
        //   - Replay: the value round-tripped through segments_jsonb (or the event). Snapshot.Values is
        //     IReadOnlyDictionary<string, object?>, so System.Text.Json materialises the
        //     entry as JsonElement on load. Deserialize using the same JsonOptions that
        //     was used to write it (line 974) so the triple is byte-stable.
        //
        // Snapshot-missing or unrecognised payload → throw. Pre-launch posture
        // (REFINEMENT-s29-phase-4d1.md Assumption #1) precludes legitimate manifest gaps;
        // replay against a pre-S29 manifest is out of scope per Phase 4d-1 deferred
        // carry-forward. Test-direct callers exercising this method must EITHER supply a
        // profile that the planner enrolls against OR not call MapSegmentToExportLinesAsync
        // directly (the planner's null-profile clause silently skips hydrator invocation
        // per ADR-020 D1.5).
        if (plannedSegment.Snapshot is null
            || !plannedSegment.Snapshot.Values.TryGetValue("WtmNaturalKey", out var wtmKeyObj)
            || wtmKeyObj is null)
        {
            throw new InvalidOperationException(
                "PCS.MapSegmentToExportLinesAsync requires a WtmNaturalKey entry in the segment " +
                $"snapshot (ADR-020 D1 / Implications §1). Segment range: [{plannedSegment.StartDate:yyyy-MM-dd}, " +
                $"{plannedSegment.EndDate:yyyy-MM-dd}]; ManifestId={manifestId}. This indicates either a " +
                "manifest persisted before S29 or a test-direct caller that bypassed " +
                "PCS.BuildPlanWithHistoriesAsync (where the WtmNaturalKey hydrator is registered).");
        }

        WtmNaturalKey wtmKey = wtmKeyObj switch
        {
            WtmNaturalKey direct => direct,
            JsonElement el => el.Deserialize<WtmNaturalKey>(JsonOptions)
                ?? throw new InvalidOperationException(
                    "WtmNaturalKey JsonElement in segment snapshot deserialised to null. " +
                    $"Segment range: [{plannedSegment.StartDate:yyyy-MM-dd}, {plannedSegment.EndDate:yyyy-MM-dd}]; " +
                    $"ManifestId={manifestId}; payload={el.GetRawText()}."),
            _ => throw new InvalidOperationException(
                $"WtmNaturalKey entry in segment snapshot is of unexpected type {wtmKeyObj.GetType().FullName}; " +
                $"expected WtmNaturalKey or JsonElement. Segment range: [{plannedSegment.StartDate:yyyy-MM-dd}, " +
                $"{plannedSegment.EndDate:yyyy-MM-dd}]; ManifestId={manifestId}."),
        };

        foreach (var result in segmentRuleResults)
        {
            if (!result.Success) continue;

            foreach (var lineItem in result.LineItems)
            {
                // ADR-020 D1 + ADR-016 D10 (S29 TASK-2907): dated wage-type-mapping lookup
                // pinned to segment.StartDate. The natural-key triple comes from the
                // replay-stable snapshot; asOfDate routes through
                // WageTypeMappingRepository.GetByKeyAtAsync which carries the
                // (position = @position OR position = '') fallback at the dated read
                // (TASK-2904 / Assumption #17 in REFINEMENT-s29-phase-4d1.md).
                var mapping = await _mappingService.GetMappingAsync(
                    lineItem.TimeType, wtmKey.OkVersion, wtmKey.AgreementCode,
                    wtmKey.Position, ct, asOfDate: plannedSegment.StartDate);

                if (mapping is null)
                {
                    _logger.LogWarning(
                        "No wage type mapping for {TimeType}/{OkVersion}/{Agreement} at {AsOfDate} — skipping line item from rule {RuleId}",
                        lineItem.TimeType, wtmKey.OkVersion, wtmKey.AgreementCode,
                        plannedSegment.StartDate, result.RuleId);
                    continue;
                }

                // Per-segment semantics: PeriodStart/End = segment range, OkVersion =
                // segment-resolved (NOT per-line). Contrast with PayrollMappingService.
                // MapCalculationResultAsync which sets Period* = lineItem.Date. Both go
                // through PayrollMappingService.BuildLine for the canonical line shape
                // (post-S20 cleanup).
                lines.Add(PayrollMappingService.BuildLine(
                    lineItem, result, segmentProfile, mapping,
                    periodStart: plannedSegment.StartDate,
                    periodEnd: plannedSegment.EndDate,
                    okVersion: segmentProfile.OkVersion,
                    manifestId: manifestId));
            }
        }

        return lines;
    }

    /// <summary>
    /// Returns a copy of <paramref name="result"/> with <see cref="CalculationResult.ManifestId"/>
    /// set to <paramref name="manifestId"/>. CalculationResult is a sealed class with init-only
    /// properties, so we rebuild via the public initialiser; cheap, allocates once per result.
    /// </summary>
    private static CalculationResult WithManifestId(CalculationResult result, Guid manifestId) =>
        new()
        {
            RuleId = result.RuleId,
            EmployeeId = result.EmployeeId,
            Success = result.Success,
            LineItems = result.LineItems,
            ErrorMessage = result.ErrorMessage,
            NormPeriodWeeks = result.NormPeriodWeeks,
            NormHoursTotal = result.NormHoursTotal,
            ActualHoursTotal = result.ActualHoursTotal,
            Deviation = result.Deviation,
            NormFulfilled = result.NormFulfilled,
            ManifestId = manifestId,
        };

    /// <summary>
    /// Re-asserts the geometric invariants from <see cref="PlannedCalculation"/>'s ctor at
    /// the manifest emission boundary. The planner-side ctor already throws on construction;
    /// this is belt-and-braces protection against an out-of-band corruption between planner
    /// invocation and PCS (test fixtures, future caller bugs) silently persisting a bad
    /// manifest into the audit projection.
    /// </summary>
    private static void AssertGeometricInvariants(PlannedCalculation plan)
    {
        if (plan.Segments.Count < 1)
            throw new PlannerInvariantViolation(
                $"PeriodCalculationService.CalculateAsync invariant violated: PlannedCalculation.Segments " +
                $"is empty. ManifestId={plan.ManifestId}.");

        for (int i = 0; i < plan.Segments.Count - 1; i++)
        {
            var current = plan.Segments[i];
            var next = plan.Segments[i + 1];
            if (next.StartDate != current.EndDate.AddDays(1))
                throw new PlannerInvariantViolation(
                    $"PeriodCalculationService.CalculateAsync invariant violated: PlannedCalculation.Segments " +
                    $"are not contiguous between index {i} and {i + 1}. " +
                    $"Segment[{i}].EndDate={current.EndDate}, Segment[{i + 1}].StartDate={next.StartDate}. " +
                    $"ManifestId={plan.ManifestId}.");
        }

        if (plan.Segments[0].StartDate != plan.PeriodStart)
            throw new PlannerInvariantViolation(
                $"PeriodCalculationService.CalculateAsync invariant violated: first segment StartDate " +
                $"({plan.Segments[0].StartDate}) does not equal PeriodStart ({plan.PeriodStart}). " +
                $"ManifestId={plan.ManifestId}.");

        if (plan.Segments[^1].EndDate != plan.PeriodEnd)
            throw new PlannerInvariantViolation(
                $"PeriodCalculationService.CalculateAsync invariant violated: last segment EndDate " +
                $"({plan.Segments[^1].EndDate}) does not equal PeriodEnd ({plan.PeriodEnd}). " +
                $"ManifestId={plan.ManifestId}.");
    }

    // ---------------------------------------------------------------
    // Private helpers — HTTP calls to Rule Engine (PAT-005)
    // ---------------------------------------------------------------

    private async Task<CalculationResult?> CallTimeRuleAsync(
        HttpClient client,
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                ruleId,
                profile,
                entries,
                periodStart,
                periodEnd
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                // SEC-039 / QUAL-061 (data-confidentiality hygiene): the rule-engine error
                // response body echoes the request payload — employee id + per-day hours /
                // rates / balances, i.e. confidential employment data. Logging the whole body
                // copied that data into the application log stream. Log a bounded, non-
                // confidential diagnostic instead: HTTP status + rule id + employee id (an
                // identifier, acceptable) — enough to triage the failure, never the payload.
                // Failure behaviour (return null) is unchanged.
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for {RuleId} (employee {EmployeeId})",
                    (int)response.StatusCode, ruleId, profile.EmployeeId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for {RuleId}", ruleId);
            return null;
        }
    }

    private async Task<CalculationResult?> CallAbsenceRuleAsync(
        HttpClient client,
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                profile,
                absences,
                periodStart,
                periodEnd
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate-absence", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                // SEC-039 / QUAL-061: do not log the response body — it echoes confidential
                // employment data (see CallTimeRuleAsync). Bounded diagnostic only.
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for absence evaluation (employee {EmployeeId})",
                    (int)response.StatusCode, profile.EmployeeId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for absence evaluation");
            return null;
        }
    }

    /// <summary>
    /// S145 / TASK-14511 — the per-plan lane's one rule-engine call:
    /// <c>POST /api/rules/evaluate-timeline</c>, which evaluates overtime / merarbejde, the norm check
    /// and flex for the whole plan, week by week. Returns the engine's response, or <c>null</c> when the
    /// call failed — the same failure contract as the per-segment calls: never silent (a bounded
    /// diagnostic is logged and the calculation is refused, S145 ruling R-6), and never a guess (no
    /// result is synthesized for the three rules).
    ///
    /// <para>
    /// <b>The endpoint's 400 has its own shape.</b> A rejected timeline answers 400 with an anonymous
    /// <c>{ success: false, error: "&lt;fixed sentence&gt;" }</c> body — NOT the
    /// <see cref="CalculationResult"/> / <c>ErrorMessage</c> shape the other rule endpoints use — so it
    /// is never deserialized as a result. The engine guarantees the sentence is fixed and value-free (no
    /// employee id, dates or hours: SEC-039), so it is logged, bounded in length, to name WHY the input
    /// was refused. Any other non-success status logs the status only: those bodies can echo the
    /// request (SEC-039 / QUAL-061), so they are never read.
    /// </para>
    ///
    /// <para>
    /// A 2xx answer is accepted only when it is complete AND successful: exactly the three rows
    /// <c>OVERTIME_CALC</c>, <c>NORM_CHECK_37H</c> and <c>FLEX_BALANCE</c>, each with
    /// <c>Success == true</c>; a flex block with <c>Success == true</c>; at least one config fingerprint;
    /// and a non-empty evaluation-model marker (both are frozen into <see cref="PlanInputs"/> and checked
    /// again by a replay, TASK-14513). Anything
    /// else is a failed call, not a partial result (<see cref="TimelineReplyDefect"/>).
    /// </para>
    /// </summary>
    private async Task<TimelineEvaluationResponse?> CallTimelineAsync(
        HttpClient client,
        string employeeId,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly? settlementCutoff,
        IReadOnlyList<FrozenPiece> pieces,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        decimal openingFlexBalance,
        CancellationToken ct)
    {
        try
        {
            // The body of EvaluateTimelineRequest (Rule Engine contract, TASK-14505). Each piece
            // serializes as { from, to, partTimeFraction, agreementCode, position }; the OK version is
            // not sent — the engine derives it from the dates and splits a piece at 1 April itself.
            // S146 TASK-14605: the engine's wire field keeps its name, previousFlexBalance; its value is the
            // opening derived from the ledger (or frozen on the manifest, for a replay).
            var payload = new
            {
                employeeId,
                periodStart,
                periodEnd,
                settlementCutoff,
                pieces,
                entries,
                absences,
                previousFlexBalance = openingFlexBalance,
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate-timeline", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    var reason = await ReadTimelineRejectionAsync(response, ct);
                    _logger.LogWarning(
                        "Rule engine rejected the timeline (400) for employee {EmployeeId}: {Reason}",
                        employeeId, reason);
                }
                else
                {
                    // SEC-039 / QUAL-061: do not log the response body — it can echo confidential
                    // employment data (see CallTimeRuleAsync). Bounded diagnostic only.
                    _logger.LogWarning(
                        "Rule engine returned {StatusCode} for the timeline evaluation (employee {EmployeeId})",
                        (int)response.StatusCode, employeeId);
                }
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var timeline = JsonSerializer.Deserialize<TimelineEvaluationResponse>(json, JsonOptions);
            if (TimelineReplyDefect(timeline) is { } defect)
            {
                // The defect is a fixed phrase naming rule ids only — never anything from the body. That holds
                // because TimelineReplyDefect checks the rule-id set before it names a failed row: the
                // ordering is load-bearing (SEC-039; S145 TASK-14511 Step 5a cycle 2, Fable NOTE 1).
                _logger.LogWarning(
                    "Rule engine answered the timeline evaluation for employee {EmployeeId} with an unusable " +
                    "response ({Defect}); the call is treated as failed",
                    employeeId, defect);
                return null;
            }

            return timeline;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for the timeline evaluation (employee {EmployeeId})", employeeId);
            return null;
        }
    }

    /// <summary>
    /// The fixed sentence of the timeline endpoint's 400 body (<c>{ success: false, error }</c>),
    /// bounded to 200 characters, or a placeholder when the body does not have that shape. Nothing
    /// else of the body is ever returned.
    /// </summary>
    private static async Task<string> ReadTimelineRejectionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int maxLength = 200;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                var sentence = error.GetString() ?? string.Empty;
                return sentence.Length <= maxLength ? sentence : sentence[..maxLength];
            }
        }
        catch (JsonException)
        {
            // Not the documented shape: fall through to the placeholder; the body is not logged.
        }

        return "(the response named no reason)";
    }

    /// <summary>
    /// Why a 2xx timeline answer cannot be used, or <c>null</c> when it can. Usable means complete AND
    /// successful — what the per-plan lane and the frozen inputs (TASK-14513) rely on:
    /// <list type="bullet">
    /// <item>exactly one row each for <c>OVERTIME_CALC</c>, <c>NORM_CHECK_37H</c> and
    /// <c>FLEX_BALANCE</c> (and nothing else), each with a line-item list and <c>Success == true</c>
    /// (a failed row is a failed evaluation, not a payroll figure — Codex WARNING 2, R-6);</item>
    /// <item>a flex block with <c>Success == true</c>;</item>
    /// <item>at least one config fingerprint, none blank (an answer without one could not be checked by a
    /// replay, TASK-14513);</item>
    /// <item>a non-empty evaluation-model marker.</item>
    /// </list>
    /// The returned phrase is fixed text naming rule ids only, so it is safe to log; nothing from the
    /// response body is ever echoed (SEC-039).
    /// </summary>
    private static string? TimelineReplyDefect(TimelineEvaluationResponse? timeline)
    {
        if (timeline?.Results is null || timeline.Results.Count != 3)
            return $"incomplete: it must carry exactly the {OvertimeRuleId}, {NormRuleId} and {FlexRuleId} rows";

        foreach (var row in timeline.Results)
        {
            if (row?.LineItems is null)
                return "incomplete: a result row without a line-item list";
        }

        foreach (var ruleId in new[] { OvertimeRuleId, NormRuleId, FlexRuleId })
        {
            if (timeline.Results.Count(r => string.Equals(r.RuleId, ruleId, StringComparison.Ordinal)) != 1)
                return $"incomplete: it must carry exactly the {OvertimeRuleId}, {NormRuleId} and {FlexRuleId} rows";
        }

        foreach (var row in timeline.Results)
        {
            if (!row.Success)
                return $"failed result row {row.RuleId}";
        }

        if (timeline.Flex is null)
            return "incomplete: no flex block";
        if (!timeline.Flex.Success)
            return "failed flex block";

        if (timeline.ConfigFingerprints is null
            || timeline.ConfigFingerprints.Count == 0
            || timeline.ConfigFingerprints.Any(string.IsNullOrWhiteSpace))
        {
            return "no config fingerprint";
        }

        if (string.IsNullOrWhiteSpace(timeline.EvaluationModel))
            return "no evaluation-model marker";

        return null;
    }
}

// =====================================================================================
// IRuleClassificationProvider — cross-domain seam to the Rule Engine's RuleRegistry
// =====================================================================================

/// <summary>
/// Provides the resolved <see cref="RuleClassification"/> set used by the segmentation
/// framework (ADR-016 D9) so <see cref="PeriodCalculationService"/> can pass a non-empty
/// rule set to <see cref="PeriodPlanner.Plan"/> and <see cref="PeriodPlanner.FromManifest"/>
/// — keeping the three rule-side invariants (SnapshotContract completeness, non-null
/// MergeStrategy, snapshot-key presence per <c>NonDatedSourceFields</c>) active across
/// every calculation code path.
///
/// <para>
/// <strong>Cross-domain wiring</strong>: the production implementation is
/// <see cref="HttpRuleClassificationProvider"/>, registered in
/// <c>StatsTid.Integrations.Payroll.Program.cs</c> against the Rule Engine's
/// <c>GET /api/rules/classifications</c> (TASK-2010, S20).
/// <see cref="EmptyRuleClassificationProvider"/> is the constructor fallback for TESTS only
/// (see its doc); since S144 (TASK-14412) an unreachable registry throws rather than
/// degrading to it.
/// </para>
///
/// <para>
/// Lives in the Payroll integration assembly (not SharedKernel) because:
/// <list type="bullet">
///   <item>SharedKernel.Segmentation already owns the data types
///     (<see cref="RuleClassification"/>); this is the consumer-side abstraction, only
///     needed by callers that resolve them at runtime — i.e. the Payroll integration.</item>
///   <item>Adding it to SharedKernel would force a SharedKernel change in this PR (out of
///     scope for TASK-2008's corrective re-dispatch).</item>
/// </list>
/// </para>
/// </summary>
public interface IRuleClassificationProvider
{
    /// <summary>
    /// Returns the resolved rule-classification set for the current process. Implementations
    /// may cache a SUCCESSFUL response across calls (the Rule Engine's registry is immutable per
    /// process startup) and never return null.
    ///
    /// <para>
    /// <strong>Unavailable is not empty</strong> (S144 TASK-14412, ruling B1). An implementation
    /// that resolves the set at runtime (e.g. <see cref="HttpRuleClassificationProvider"/>) THROWS
    /// <see cref="RuleClassificationsUnavailableException"/> when it cannot obtain it, and caches
    /// nothing so the next call retries. An empty list means a genuinely empty registry, never an
    /// unknown one: payroll plans and merges by the strategies its rules declare (their split behaviour
    /// for planning, their merge strategy for the per-rule merge), so an "empty because I could not find
    /// out" would have it plan and merge without them, as though no rule declared anything. The S20
    /// "empty on failure is degraded but correct" contract is retired. (Since S145 Stage B payroll hands
    /// the planner only the classifications of the rules it evaluates, none of which refuses a split,
    /// so the earlier reason — the planner refuses a split month only when it can see a Reject or
    /// AlignedWindow rule — no longer describes payroll; R-91.)
    /// </para>
    /// </summary>
    IReadOnlyList<RuleClassification> GetClassifications();
}

/// <summary>
/// Empty fallback used when no <see cref="IRuleClassificationProvider"/> is passed to the
/// <see cref="PeriodCalculationService"/> constructor — i.e. in TESTS. It models a genuinely
/// empty registry: no D9 invariants fire, no split refusal fires, and every per-rule
/// MergeStrategy lookup goes through the "default to Concatenate with warning" fallback.
///
/// <para>
/// <strong>Tests only; never a stand-in for an outage.</strong> Production always registers
/// <see cref="HttpRuleClassificationProvider"/> in <c>Program.cs</c>, which throws
/// <see cref="RuleClassificationsUnavailableException"/> (a 503 at the routes) when the Rule
/// Engine is unreachable — it never degrades to this empty set (S144 TASK-14412, ruling B1).
/// </para>
/// </summary>
public sealed class EmptyRuleClassificationProvider : IRuleClassificationProvider
{
    public static readonly EmptyRuleClassificationProvider Instance = new();

    public IReadOnlyList<RuleClassification> GetClassifications() =>
        Array.Empty<RuleClassification>();
}
