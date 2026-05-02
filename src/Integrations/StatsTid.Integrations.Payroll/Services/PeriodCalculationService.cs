using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using System.Text.Json;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// The "glue" service that connects Rule Engine output to Payroll Export.
///
/// <para>
/// Sprint 20 (ADR-016 D1, D8): the sole logic entry point is
/// <see cref="CalculateAsync(PlannedCalculation, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, Guid?, CancellationToken)"/>.
/// Every calculation routes through <see cref="PeriodPlanner"/> first — the back-compat
/// overload <c>CalculateAsync(profile, entries, …)</c> below is a thin shim that hydrates
/// <see cref="BoundarySources"/> from <see cref="OkVersionResolver"/> and immediately
/// re-enters the new entry point. There is no "skip planner" code path.
/// </para>
///
/// <para>
/// Per-segment rule evaluation calls the Rule Engine over HTTP (PAT-005) and maps results to
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
/// <strong>Replay primitive (ADR-016 D10)</strong>:
/// <see cref="ReplayAsync(Guid, CancellationToken)"/> loads a persisted manifest from the
/// <c>segment_manifests</c> projection (with event-replay fallback over the event store),
/// reconstructs the <see cref="PlannedCalculation"/> via
/// <see cref="PeriodPlanner.FromManifest"/> using the same rule-set provider as a forward
/// calculation, and returns a <see cref="CalculationResult"/> envelope carrying the
/// <em>same</em> <see cref="CalculationResult.ManifestId"/>. The richer overload
/// <see cref="ReplayAsync(Guid, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, Guid?, CancellationToken)"/>
/// re-evaluates rules against the reconstructed plan when the caller supplies the original
/// inputs (TASK-2012 manifest-replay tests use this overload).
/// </para>
///
/// <para>
/// <strong>Audit completeness contract</strong>: a successful calculation always returns
/// a populated <see cref="PeriodCalculationResult"/>, but the audit chain may be partially
/// degraded when one of the two manifest-persistence steps (event-store append, projection
/// insert) fails. Callers that need to know whether the audit chain is complete should use
/// <see cref="CalculateWithOutcomeAsync"/> (or the Replay equivalent) which returns a
/// <see cref="PeriodCalculationOutcome"/> carrying an explicit <see cref="AuditState"/>.
/// Endpoints that surface manifest-id to clients should also call
/// <see cref="StampAuditContext(HttpContext, PeriodCalculationOutcome)"/> to thread the
/// manifest id (and the audit state when degraded) into <c>HttpContext.Items</c> for
/// <see cref="AuditLoggingMiddleware"/>.
/// </para>
/// </summary>
public sealed class PeriodCalculationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayrollMappingService _mappingService;
    private readonly IEventStore _eventStore;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IRuleClassificationProvider _classificationProvider;
    private readonly ILogger<PeriodCalculationService> _logger;
    private readonly string _ruleEngineUrl;

    /// <summary>
    /// Stream id pattern for the <see cref="SegmentManifestCreated"/> event. Keying by
    /// manifest id makes the event-replay fallback in <see cref="ReplayAsync(Guid, CancellationToken)"/>
    /// a single <see cref="IEventStore.ReadStreamAsync"/> call rather than a full event-store scan.
    /// </summary>
    internal static string ManifestStreamId(Guid manifestId) =>
        $"segment-manifest-{manifestId}";

    /// <summary>
    /// HttpContext.Items key carrying the <see cref="AuditState"/> alongside the manifest id
    /// when the persistence chain was partially degraded. Consumed by audit/log middleware
    /// or downstream handlers that want to surface degraded-audit warnings to clients.
    /// </summary>
    public const string AuditStateItemKey = "audit:audit_state";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PeriodCalculationService(
        IHttpClientFactory httpClientFactory,
        PayrollMappingService mappingService,
        IEventStore eventStore,
        DbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<PeriodCalculationService> logger,
        IRuleClassificationProvider? classificationProvider = null)
    {
        _httpClientFactory = httpClientFactory;
        _mappingService = mappingService;
        _eventStore = eventStore;
        _connectionFactory = connectionFactory;
        _logger = logger;
        // The provider is the cross-domain seam to the Rule Engine's RuleRegistry. When
        // unwired (cross-domain dep — see file XML doc), we fall back to an empty provider
        // that logs a warning so the rule-side invariant pass is silenced rather than
        // exercised — the alternative (throwing) would break every existing endpoint.
        // TASK-2010 is expected to land the HTTP-backed provider; until then, callers who
        // construct PCS directly (tests) get the empty provider transparently.
        _classificationProvider = classificationProvider ?? EmptyRuleClassificationProvider.Instance;
        _ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
    }

    // -------------------------------------------------------------------
    // Public outcome / audit-state types
    // -------------------------------------------------------------------

    /// <summary>
    /// Audit-chain completeness for a single calculation run. Values describe the outcome
    /// of the two-step manifest persistence (event-store append + projection insert).
    /// </summary>
    public enum AuditState
    {
        /// <summary>Both event-store append and projection insert succeeded. Default value.</summary>
        Complete,

        /// <summary>Event-store append succeeded, projection insert failed. Replay is fully
        /// supported; audit-query consumers should rebuild the projection (TASK-2011 ops
        /// script).</summary>
        EventOnly,

        /// <summary>Projection insert succeeded, event-store append failed. Audit-query is
        /// supported but the manifest is not replayable from events alone — the projection
        /// row is the only source of truth. Investigate the event-store failure.</summary>
        ProjectionOnly,

        /// <summary>Both writes failed. The calculation result is still returned to the
        /// caller (the rule outputs are valid), but the audit chain is empty for this
        /// manifest id; ManifestId on result rows points to a record that does not exist
        /// in either store. Operators must investigate; treating the response as
        /// audit-of-record is unsafe.</summary>
        BothFailed,

        /// <summary>No manifest emission was attempted. Set on the total-failure
        /// short-circuit (every rule across every segment failed — typically Rule Engine
        /// outage or auth misconfiguration) and on replay (the original manifest already
        /// exists in the audit chain; replay re-evaluates rules but does NOT mint a new
        /// manifest event — ADR-016 D10 immutability). The ManifestId carried alongside
        /// this state references the in-memory plan; in the failure case it does not
        /// resolve in either store; in the replay case it resolves to the original
        /// forward-calc's persisted manifest. Callers that surface ManifestId to clients
        /// must check this state explicitly before using it as an audit-query key.</summary>
        NoManifest,
    }

    /// <summary>
    /// Wrapper surfacing the calculation result alongside its <see cref="AuditState"/>.
    /// Returned by <see cref="CalculateWithOutcomeAsync"/>; the legacy
    /// <see cref="CalculateAsync(PlannedCalculation, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, Guid?, CancellationToken)"/>
    /// returns just <see cref="PeriodCalculationResult"/> for backward compatibility but
    /// loses the <see cref="AuditState"/> signal.
    ///
    /// <para>
    /// <strong>Cross-domain dep (TASK-2010 / S20 wave 2)</strong>: prefer adding a nullable
    /// <c>AuditState</c> field to <see cref="PeriodCalculationResult"/> in SharedKernel,
    /// which would let us drop this wrapper entirely. Until that lands, callers needing the
    /// audit state must use this wrapper API.
    /// </para>
    /// </summary>
    public sealed record PeriodCalculationOutcome(
        PeriodCalculationResult Result,
        AuditState AuditState,
        Guid ManifestId);

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
    /// <see cref="CalculateWithOutcomeAsync(PlannedCalculation, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, Guid?, CancellationToken)"/>.
    /// </para>
    /// </summary>
    public async Task<PeriodCalculationResult> CalculateAsync(
        PlannedCalculation plan,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        decimal previousFlexBalance,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var outcome = await CalculateWithOutcomeAsync(
            plan, profile, entries, absences,
            previousFlexBalance, authorizationHeader, correlationId,
            ct: ct);
        return outcome.Result;
    }

    /// <summary>
    /// Sole logic entry point — same semantics as
    /// <see cref="CalculateAsync(PlannedCalculation, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, Guid?, CancellationToken)"/>
    /// but additionally returns an explicit <see cref="AuditState"/> describing the
    /// completeness of the manifest-persistence chain.
    /// </summary>
    public async Task<PeriodCalculationOutcome> CalculateWithOutcomeAsync(
        PlannedCalculation plan,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        decimal previousFlexBalance,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        bool emitAuditEvents = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(absences);

        // -------------------------------------------------------------------
        // Belt + braces re-check of geometric invariants. The planner already ran them when
        // it constructed `plan`, but this is the emission boundary — if the manifest is
        // about to be persisted as the audit-of-record, an out-of-band corruption between
        // planner and PCS (e.g. test fixtures, future caller bugs) must surface here, not
        // silently get persisted into the projection.
        // -------------------------------------------------------------------
        AssertGeometricInvariants(plan);

        // EmployeeId mismatch is a programmer-error case (caller paired the wrong plan with
        // the wrong profile). The planner can't catch this — Plan() takes employeeId as an
        // input, profile is supplied separately to CalculateAsync. Throwing keeps the
        // manifest contract honest: one plan == one employee == one set of inputs. Both
        // sides are string per ADR-016 D10 amendment 2026-05-01 — no synthetic Guid round-trip.
        if (!string.Equals(plan.EmployeeId, profile.EmployeeId, StringComparison.Ordinal))
        {
            throw new PlannerInvariantViolation(
                $"PeriodCalculationService.CalculateAsync invariant violated: PlannedCalculation.EmployeeId " +
                $"('{plan.EmployeeId}') does not match profile.EmployeeId ('{profile.EmployeeId}'). " +
                $"ManifestId={plan.ManifestId}.");
        }

        // Resolve the rule classification set once per call. Used for per-rule MergeStrategy
        // dispatch below AND for the "rules attempted per segment" budget in the
        // total-failure short-circuit.
        var ruleSet = _classificationProvider.GetClassifications();

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        // ---------------------------------------------------------------
        // 1. Per-segment rule evaluation. Each segment carries its own OK version (resolved
        //    from segment.StartDate per ADR-003); entries/absences are filtered to the
        //    segment's date range before being shipped to the rule engine.
        //
        //    For the FlexBalanceRule (CrossPeriod, Mergeable, Custom merge), the previous
        //    segment's flex delta becomes the next segment's "previousBalance" — preserving
        //    the carry-forward semantics that RetroactiveCorrectionService used to encode
        //    explicitly in RecalculateWithVersionSplitAsync (ADR-013, S11), which was
        //    retired by TASK-2009 / wave 2.
        // ---------------------------------------------------------------
        var perSegmentRuleResults = new List<List<CalculationResult>>(plan.Segments.Count);
        var perSegmentExportLines = new List<List<PayrollExportLine>>(plan.Segments.Count);
        decimal flexBalanceCarry = previousFlexBalance;
        int totalFailures = 0;
        int rulesAttemptedPerSegment = 0; // computed from the first segment's evaluation

        for (int s = 0; s < plan.Segments.Count; s++)
        {
            var segment = plan.Segments[s];
            var segmentOkVersion = OkVersionResolver.ResolveVersion(segment.StartDate);

            // Caller-supplied profile.OkVersion may be stale (pre-S20 callers commonly send
            // a single OK version even for straddling periods). Build a per-segment effective
            // profile with the resolved value; preserve Position so position-specific
            // overrides (TASK-1802) keep applying within the segment.
            if (!string.Equals(profile.OkVersion, segmentOkVersion, StringComparison.Ordinal) && plan.Segments.Count == 1)
            {
                _logger.LogWarning(
                    "Caller-supplied profile.OkVersion '{Supplied}' differs from segment-resolved '{Resolved}' " +
                    "for employee {EmployeeId} segment [{SegStart}..{SegEnd}]. Using resolved value.",
                    profile.OkVersion, segmentOkVersion, profile.EmployeeId, segment.StartDate, segment.EndDate);
            }

            var segmentProfile = string.Equals(profile.OkVersion, segmentOkVersion, StringComparison.Ordinal)
                ? profile
                : new EmploymentProfile
                {
                    EmployeeId = profile.EmployeeId,
                    AgreementCode = profile.AgreementCode,
                    OkVersion = segmentOkVersion,
                    WeeklyNormHours = profile.WeeklyNormHours,
                    EmploymentCategory = profile.EmploymentCategory,
                    IsPartTime = profile.IsPartTime,
                    PartTimeFraction = profile.PartTimeFraction,
                    Position = profile.Position
                };

            var segmentEntries = entries.Where(e => e.Date >= segment.StartDate && e.Date <= segment.EndDate).ToList();
            var segmentAbsences = absences.Where(a => a.Date >= segment.StartDate && a.Date <= segment.EndDate).ToList();

            var (segmentRuleResults, segmentFailureCount, segmentAttempted) = await EvaluateSegmentAsync(
                client, segmentProfile, segmentEntries, segmentAbsences,
                segment.StartDate, segment.EndDate, flexBalanceCarry, ct);

            totalFailures += segmentFailureCount;
            if (s == 0)
                rulesAttemptedPerSegment = segmentAttempted;

            // Stamp every rule result on this segment with the manifest id so downstream
            // consumers (audit_log payload, SLS export, in-memory diff in
            // RetroactiveCorrectionService) can correlate every CalculationResult to the
            // manifest (PAT-006 amendment per ADR-016 D10).
            for (int i = 0; i < segmentRuleResults.Count; i++)
            {
                segmentRuleResults[i] = WithManifestId(segmentRuleResults[i], plan.ManifestId);
            }

            // Carry the flex delta from this segment forward (FlexBalanceRule is Span.CrossPeriod).
            var segmentFlexDelta = ExtractFlexDelta(segmentRuleResults);
            if (segmentFlexDelta.HasValue)
            {
                flexBalanceCarry += segmentFlexDelta.Value;
            }

            // Map this segment's line items to wage types using the segment's resolved OK version.
            var segmentExportLines = await MapSegmentToExportLinesAsync(
                segmentRuleResults, segmentProfile, segment.StartDate, segment.EndDate, plan.ManifestId, ct);

            perSegmentRuleResults.Add(segmentRuleResults);
            perSegmentExportLines.Add(segmentExportLines);
        }

        // If ALL rule evaluations across ALL segments failed, treat the whole calculation as
        // failed. The denominator is computed from the first segment's actual attempt count
        // (no magic constant) so adding/removing rule calls doesn't silently break the
        // short-circuit.
        var totalRules = rulesAttemptedPerSegment * plan.Segments.Count;
        if (totalRules > 0 && totalFailures >= totalRules)
        {
            _logger.LogError(
                "All {TotalRules} rule evaluations failed across {SegmentCount} segment(s) for employee " +
                "{EmployeeId} manifest {ManifestId} period {PeriodStart}-{PeriodEnd}",
                totalRules, plan.Segments.Count, profile.EmployeeId, plan.ManifestId, plan.PeriodStart, plan.PeriodEnd);

            var failResult = new PeriodCalculationResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = plan.PeriodStart,
                PeriodEnd = plan.PeriodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
                RuleResults = MergePerSegmentRuleResults(perSegmentRuleResults, ruleSet, profile.EmployeeId, plan.ManifestId),
                ExportLines = [],
                Success = false,
                ErrorMessage = "All rule evaluations failed"
            };
            // No manifest was emitted on this short-circuit (BuildManifest +
            // EmitManifestAsync run later). Return AuditState.NoManifest so callers do
            // not treat plan.ManifestId as a valid audit-query key — it does not exist
            // in segment_manifests or the event store. (Codex sprint-end review fix.)
            return new PeriodCalculationOutcome(failResult, AuditState.NoManifest, plan.ManifestId);
        }

        // ---------------------------------------------------------------
        // 2. Strategy-driven merge of per-segment rule results (ADR-016 D3 / D7). Each rule
        //    is merged according to the strategy resolved at registration time; when the
        //    classification provider returns nothing for a rule (e.g. legacy / unwired
        //    callers), we default to Concatenate with a warning. Per-segment export lines
        //    are concatenated unconditionally — the per-line OK-version stamping carries
        //    the per-segment context, and the ExportLines list is intentionally a flat
        //    sequence at the period level. TASK-2010 / wave 2 retired the
        //    OkVersionBoundary.ResolveProfile collapse that previously lived inline in
        //    Program.cs; per-line OK-version stamping is now the canonical truth.
        // ---------------------------------------------------------------
        var allExportLines = perSegmentExportLines.SelectMany(s => s).ToList();
        var allRuleResults = MergePerSegmentRuleResults(
            perSegmentRuleResults, ruleSet, profile.EmployeeId, plan.ManifestId);

        // ---------------------------------------------------------------
        // 3. Emit SegmentManifestCreated event (ADR-016 D10). Single audit-of-record per
        //    successful calculation. The two-step persistence (event-store append +
        //    projection insert) tracks an explicit AuditState so callers can react to
        //    partial failures rather than silently treating success-with-degraded-audit
        //    as success-with-complete-audit.
        // ---------------------------------------------------------------
        AuditState auditState;
        if (emitAuditEvents)
        {
            var manifest = BuildManifest(plan);
            auditState = await EmitManifestAsync(plan, manifest, correlationId, ct);

            // Legacy event for backward compatibility with the pre-S20 audit chain. Kept
            // alongside SegmentManifestCreated so existing audit queries against
            // PeriodCalculationCompleted continue to work; the new event is the source of
            // truth for segmentation, the legacy one for high-level period-result counts.
            await EmitLegacyPeriodCompletedAsync(plan, profile, allRuleResults.Count, allExportLines.Count, allExportLines.Sum(l => l.Hours), correlationId, ct);
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
            profile.EmployeeId, plan.ManifestId, plan.Segments.Count, allRuleResults.Count, allExportLines.Count, auditState);

        var okResult = new PeriodCalculationResult
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = plan.PeriodStart,
            PeriodEnd = plan.PeriodEnd,
            AgreementCode = profile.AgreementCode,
            // The result-level OkVersion remains the period-start-resolved version for
            // legacy callers that read this field (RetroactiveCorrectionService, /export).
            // Per-line OK-version stamping on PayrollExportLine.OkVersion is the new
            // canonical truth; TASK-2010 will route the export boundary through that.
            OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
            RuleResults = allRuleResults,
            ExportLines = allExportLines,
            Success = true
        };

        return new PeriodCalculationOutcome(okResult, auditState, plan.ManifestId);
    }

    // -------------------------------------------------------------------
    // Back-compat shim — D8 always-on planner. Pre-S20 callers (Program.cs
    // /calculate-and-export endpoint, RetroactiveCorrectionService) call this overload;
    // it hydrates BoundarySources, runs the planner, and re-enters the new entry point.
    // No "skip planner" code path exists.
    //
    // [Obsolete] is intentional: it marks the migration runway from caller-supplied
    // (profile, entries, …) to caller-constructed PlannedCalculation. TASK-2009 and
    // TASK-2010 migrated their own surfaces (RetroactiveCorrectionService internals;
    // /export and /export-period). The /calculate-and-export endpoint at Program.cs
    // is the last surviving customer; its retirement was explicitly out-of-scope per
    // S20 Step 0b W2 (full retirement of PeriodCalculationService is deferred).
    // -------------------------------------------------------------------
    [Obsolete(
        "Use CalculateAsync(PlannedCalculation, …) or CalculateWithOutcomeAsync(PlannedCalculation, …). " +
        "Boundary sources are limited to OK-transitions in this path; full segmentation requires explicit " +
        "PlannedCalculation construction. The single surviving caller is the /calculate-and-export " +
        "endpoint; full retirement is deferred per S20 Step 0b W2.",
        error: false)]
    public Task<PeriodCalculationResult> CalculateAsync(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlexBalance,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var plan = BuildPlanForLegacyCallers(profile, periodStart, periodEnd);

        return CalculateAsync(
            plan, profile, entries, absences,
            previousFlexBalance, authorizationHeader, correlationId, ct);
    }

    /// <summary>
    /// Build a <see cref="PlannedCalculation"/> for callers that haven't migrated to the
    /// PlannedCalculation-first signature yet. Hydrates an OK-version boundary source from
    /// <see cref="OkVersionResolver"/> — S20's end-to-end boundary; agreement-config /
    /// position-override / EU WTD boundary hydration are extension points that
    /// TASK-2009/TASK-2010 callers will populate when they construct plans directly.
    ///
    /// The resolved <see cref="RuleClassification"/> set comes from the same
    /// <see cref="IRuleClassificationProvider"/> used by the main entry point, so D9
    /// rule-side invariants run uniformly across the shim and PlannedCalculation-first
    /// paths (ADR-016 D9).
    /// </summary>
    private PlannedCalculation BuildPlanForLegacyCallers(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        // Extract OK transitions whose dates fall strictly inside (periodStart, periodEnd]
        // — the planner's BoundaryDetector expects "first day of the new segment" semantics.
        var versionPeriods = OkVersionResolver.ResolveVersionsForPeriod(periodStart, periodEnd);
        var okTransitions = new List<(DateOnly Date, string FromVersion, string ToVersion)>();
        for (int i = 1; i < versionPeriods.Count; i++)
        {
            okTransitions.Add((versionPeriods[i].Start, versionPeriods[i - 1].Version, versionPeriods[i].Version));
        }

        var sources = new BoundarySources(
            OkTransitions: okTransitions,
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>());

        // EmployeeId is now string end-to-end (ADR-016 D10 amendment 2026-05-01) — pass
        // profile.EmployeeId directly with no synthetic Guid derivation.
        return PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: periodStart,
            periodEnd: periodEnd,
            calculationKind: "forward-calc",
            ruleSet: _classificationProvider.GetClassifications(),
            sources: sources,
            options: PlannerOptions.Default);
    }

    // -------------------------------------------------------------------
    // Replay primitive (ADR-016 D10)
    // -------------------------------------------------------------------

    /// <summary>
    /// Loads a persisted <see cref="SegmentManifest"/> by id and reconstructs the
    /// <see cref="PlannedCalculation"/> for replay. Returns a <see cref="CalculationResult"/>
    /// envelope carrying the original <see cref="CalculationResult.ManifestId"/>; for full
    /// re-evaluation of rules against the reconstructed plan, use the richer overload that
    /// accepts the original inputs.
    ///
    /// <para>
    /// Load order: the <c>segment_manifests</c> projection table first (single-statement
    /// fetch); if the projection has been truncated or has not yet been populated for this
    /// manifest id, fall back to replaying <see cref="SegmentManifestCreated"/> events from
    /// the event store. Determinism (ADR-016 D10): rules see snapshots from the
    /// reconstructed plan, NOT the live DB. The same
    /// <see cref="IRuleClassificationProvider"/> used by forward calculations supplies the
    /// rule set, so rule-side invariants are re-asserted against today's classifications
    /// (this catches the case where a rule grew a new SnapshotContract after the manifest
    /// was originally persisted).
    /// </para>
    /// </summary>
    public async Task<CalculationResult> ReplayAsync(Guid manifestId, CancellationToken ct = default)
    {
        var manifest = await LoadManifestAsync(manifestId, ct)
            ?? throw new InvalidOperationException(
                $"Manifest {manifestId} not found in segment_manifests projection or event store. " +
                "Cannot replay.");

        var plan = PeriodPlanner.FromManifest(manifest, _classificationProvider.GetClassifications());

        return new CalculationResult
        {
            RuleId = "REPLAY",
            EmployeeId = manifest.EmployeeId,
            Success = true,
            LineItems = new List<CalculationLineItem>(),
            ManifestId = plan.ManifestId,
        };
    }

    /// <summary>
    /// Full replay: loads the manifest, reconstructs the plan, and re-evaluates rules
    /// against the supplied inputs. The returned <see cref="PeriodCalculationResult"/> is
    /// produced by the same rule evaluation pipeline as a forward calculation, so the
    /// per-rule <see cref="CalculationResult.ManifestId"/> stamping and per-line OK-version
    /// stamping are identical — only the <see cref="PlannedCalculation.ManifestId"/> is
    /// the original (replay does NOT mint a new manifest).
    ///
    /// <para>
    /// Note on snapshots: rules see <see cref="SegmentSnapshot"/> values from the
    /// reconstructed plan, NOT live DB reads. Determinism (ADR-016 D10) holds even after
    /// the live DB has changed.
    /// </para>
    /// </summary>
    public async Task<PeriodCalculationResult> ReplayAsync(
        Guid manifestId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        decimal previousFlexBalance,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var manifest = await LoadManifestAsync(manifestId, ct)
            ?? throw new InvalidOperationException(
                $"Manifest {manifestId} not found in segment_manifests projection or event store. " +
                "Cannot replay.");

        var plan = PeriodPlanner.FromManifest(manifest, _classificationProvider.GetClassifications());

        // Replay re-evaluates rules against the reconstructed plan but does NOT mint a
        // new manifest event (ADR-016 D10 immutability). The original SegmentManifestCreated
        // already exists from the forward-calc that produced this manifest id; calling the
        // standard CalculateAsync path would unconditionally append a duplicate event with
        // a fresh createdAt, mutating an immutable audit record (Codex sprint-end review).
        // emitAuditEvents:false skips both EmitManifestAsync and EmitLegacyPeriodCompletedAsync.
        var outcome = await CalculateWithOutcomeAsync(
            plan, profile, entries, absences,
            previousFlexBalance, authorizationHeader, correlationId,
            emitAuditEvents: false, ct);
        return outcome.Result;
    }

    // -------------------------------------------------------------------
    // Audit-context propagation helper (TASK-2008 WARNING fix #2)
    // -------------------------------------------------------------------

    /// <summary>
    /// Stamps the <see cref="PeriodCalculationOutcome.ManifestId"/> (and degraded
    /// <see cref="AuditState"/> when applicable) onto <see cref="HttpContext.Items"/> so
    /// <see cref="AuditLoggingMiddleware"/> picks it up for the <c>audit_log.details</c>
    /// JSONB column. Endpoints that compute a calculation outcome should call this once
    /// after a successful evaluation, before returning the response.
    ///
    /// <para>
    /// Idempotent: calling with <see cref="PeriodCalculationOutcome.ManifestId"/> equal to
    /// <see cref="Guid.Empty"/> is a no-op (no manifest was produced). Re-stamping the same
    /// id overwrites the previous value — this is fine because there is at most one
    /// manifest per HTTP request.
    /// </para>
    ///
    /// <para>
    /// The key for the manifest id is
    /// <see cref="AuditLoggingMiddleware.ManifestIdItemKey"/>; the audit-state key (when
    /// degraded) is <see cref="AuditStateItemKey"/>.
    /// </para>
    /// </summary>
    public static void StampAuditContext(HttpContext context, PeriodCalculationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.ManifestId == Guid.Empty)
            return; // no manifest produced; nothing to surface to the audit chain

        context.Items[AuditLoggingMiddleware.ManifestIdItemKey] = outcome.ManifestId;

        // Only stamp the audit-state when it's degraded — the default Complete value is the
        // assumption made by every existing audit query. Surfacing it explicitly only when
        // it deviates keeps the on-disk JSON identical for the happy path.
        if (outcome.AuditState != AuditState.Complete)
        {
            context.Items[AuditStateItemKey] = outcome.AuditState.ToString();
        }
    }

    // -------------------------------------------------------------------
    // Per-rule MergeStrategy dispatch (TASK-2008 BLOCKER fix #3)
    // -------------------------------------------------------------------

    /// <summary>
    /// Merges per-segment rule results into a single flat list using each rule's resolved
    /// <see cref="MergeStrategy"/> from <paramref name="ruleSet"/>. Rules that don't appear
    /// in <paramref name="ruleSet"/> default to Concatenate with a warning (indicates a
    /// misconfigured registration or an unwired classification provider).
    /// </summary>
    private List<CalculationResult> MergePerSegmentRuleResults(
        IReadOnlyList<List<CalculationResult>> perSegmentResults,
        IReadOnlyList<RuleClassification> ruleSet,
        string employeeId,
        Guid manifestId)
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

            // Per-rule custom merge delegates: the planner's contract is that any rule with
            // SplitBehavior.Mergeable carries MergeStrategy.Custom (or UnionDedupe for
            // compliance). FlexBalanceRule chains carry-state; multi-week / annual norm
            // sums hours across segments. The actual merge arithmetic for these rules is
            // out of S20 wave 1's scope (TASK-2009 wires per-rule delegates as part of
            // RetroactiveCorrectionService cleanup); for now we provide a defensive
            // single-segment-passthrough fallback that preserves the segment-0 row when
            // only one segment exists, and otherwise concatenates with a warning. The
            // CalculationResultMerger.Apply() Custom path throws when no delegate is
            // supplied — wrapping that in a fallback keeps single-segment plans (the
            // overwhelming common case) working without TASK-2009 landing first.
            CalculationResult mergedResult;
            try
            {
                mergedResult = CalculationResultMerger.Apply(
                    segments,
                    strategy,
                    customMerge: strategy.Kind == MergeStrategyKind.Custom
                        ? FallbackCustomMerge
                        : null);
            }
            catch (PlannerInvariantViolation ex)
            {
                // RejectIfMultipleSegments fired with > 1 segment, or Custom without delegate
                // and our fallback failed: surface the failure as a single failed
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
    /// delegates (FlexBalanceRule chained-carry, NormCheckRule.MULTI_WEEK summing) are
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

    private static SegmentManifest BuildManifest(PlannedCalculation plan)
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
            Segments: plan.Segments);
    }

    /// <summary>
    /// Two-step persistence (event-store append + projection insert) returning an explicit
    /// <see cref="AuditState"/> describing which step(s) succeeded. Failures are logged at
    /// Warning level (not Information) and include the manifest id so ops monitoring can
    /// alert on degraded audit chains.
    /// </summary>
    private async Task<AuditState> EmitManifestAsync(
        PlannedCalculation plan,
        SegmentManifest manifest,
        Guid? correlationId,
        CancellationToken ct)
    {
        bool eventOk = false;
        bool projectionOk = false;

        // 1. Append the SegmentManifestCreated domain event (audit-of-record per ADR-016 D10).
        try
        {
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
                CorrelationId = correlationId,
            };

            await _eventStore.AppendAsync(ManifestStreamId(manifest.ManifestId), manifestEvent, ct);
            eventOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to append SegmentManifestCreated event for manifest {ManifestId} employee {EmployeeId}; " +
                "audit chain partially degraded — see AuditState on the calculation outcome.",
                manifest.ManifestId, plan.EmployeeId);
        }

        // 2. Insert into the segment_manifests projection table. The projection is
        //    rebuildable from events (TASK-2011 ops script), so a transient failure here
        //    is recoverable as long as step 1 succeeded.
        try
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);

            var segmentsJson = JsonSerializer.Serialize(manifest.Segments, JsonOptions);

            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO segment_manifests
                    (manifest_id, period_start, period_end, employee_id, calculation_kind,
                     boundary_cause_summary, created_at, segments_jsonb)
                VALUES
                    (@manifestId, @periodStart, @periodEnd, @employeeId, @calculationKind,
                     @boundaryCauseSummary, @createdAt, @segmentsJson::jsonb)
                ON CONFLICT (manifest_id) DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("manifestId", manifest.ManifestId);
            cmd.Parameters.AddWithValue("periodStart", manifest.PeriodStart);
            cmd.Parameters.AddWithValue("periodEnd", manifest.PeriodEnd);
            cmd.Parameters.AddWithValue("employeeId", manifest.EmployeeId);
            cmd.Parameters.AddWithValue("calculationKind", manifest.CalculationKind);
            cmd.Parameters.AddWithValue("boundaryCauseSummary", manifest.BoundaryCauseSummary.ToArray());
            cmd.Parameters.AddWithValue("createdAt", manifest.CreatedAt);
            cmd.Parameters.AddWithValue("segmentsJson", NpgsqlTypes.NpgsqlDbType.Text, segmentsJson);

            await cmd.ExecuteNonQueryAsync(ct);
            projectionOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to insert manifest {ManifestId} into segment_manifests projection; " +
                "rebuild script (TASK-2011) can recover from the SegmentManifestCreated event " +
                "(if step 1 succeeded). Audit chain partially degraded.",
                manifest.ManifestId);
        }

        return (eventOk, projectionOk) switch
        {
            (true, true) => AuditState.Complete,
            (true, false) => AuditState.EventOnly,
            (false, true) => AuditState.ProjectionOnly,
            _ => AuditState.BothFailed,
        };
    }

    private async Task<SegmentManifest?> LoadManifestAsync(Guid manifestId, CancellationToken ct)
    {
        // 1. Try the projection table first — single-row indexed lookup.
        try
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(
                """
                SELECT manifest_id, period_start, period_end, employee_id, calculation_kind,
                       boundary_cause_summary, created_at, segments_jsonb
                FROM segment_manifests
                WHERE manifest_id = @manifestId
                """, conn);
            cmd.Parameters.AddWithValue("manifestId", manifestId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var segmentsJson = reader.GetString(7);
                var segments = JsonSerializer.Deserialize<List<PlannedSegment>>(segmentsJson, JsonOptions)
                    ?? new List<PlannedSegment>();

                return new SegmentManifest(
                    ManifestId: reader.GetGuid(0),
                    PeriodStart: DateOnly.FromDateTime(reader.GetDateTime(1)),
                    PeriodEnd: DateOnly.FromDateTime(reader.GetDateTime(2)),
                    EmployeeId: reader.GetString(3),
                    CalculationKind: reader.GetString(4),
                    BoundaryCauseSummary: ((string[])reader.GetValue(5)).ToList(),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(6),
                    Segments: segments);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Projection lookup for manifest {ManifestId} failed; falling back to event-replay.",
                manifestId);
        }

        // 2. Event-replay fallback — read SegmentManifestCreated events on the manifest's
        //    stream and rehydrate the manifest. This path covers the projection-truncated /
        //    projection-not-yet-rebuilt case and is the same path TASK-2011's rebuild
        //    script will use.
        try
        {
            var events = await _eventStore.ReadStreamAsync(ManifestStreamId(manifestId), ct);
            var created = events.OfType<SegmentManifestCreated>().FirstOrDefault();
            if (created is not null)
            {
                return new SegmentManifest(
                    ManifestId: created.ManifestId,
                    PeriodStart: created.PeriodStart,
                    PeriodEnd: created.PeriodEnd,
                    EmployeeId: created.EmployeeId,
                    CalculationKind: created.CalculationKind,
                    BoundaryCauseSummary: created.BoundaryCauseSummary,
                    CreatedAt: created.CreatedAt,
                    Segments: created.Segments);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Event-replay fallback for manifest {ManifestId} failed.", manifestId);
        }

        return null;
    }

    private async Task EmitLegacyPeriodCompletedAsync(
        PlannedCalculation plan,
        EmploymentProfile profile,
        int ruleResultCount,
        int exportLineCount,
        decimal totalHours,
        Guid? correlationId,
        CancellationToken ct)
    {
        try
        {
            var calcEvent = new PeriodCalculationCompleted
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = plan.PeriodStart,
                PeriodEnd = plan.PeriodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = OkVersionResolver.ResolveVersion(plan.PeriodStart),
                RuleCount = ruleResultCount,
                ExportLineCount = exportLineCount,
                TotalHours = totalHours,
                CorrelationId = correlationId,
            };

            await _eventStore.AppendAsync(
                $"period-calc-{profile.EmployeeId}-{plan.PeriodStart:yyyy-MM-dd}",
                calcEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit PeriodCalculationCompleted legacy event for {EmployeeId} manifest {ManifestId}",
                profile.EmployeeId, plan.ManifestId);
        }
    }

    // -------------------------------------------------------------------
    // Per-segment helpers
    // -------------------------------------------------------------------

    private async Task<(List<CalculationResult> Results, int FailureCount, int RulesAttempted)> EvaluateSegmentAsync(
        HttpClient client,
        EmploymentProfile segmentProfile,
        IReadOnlyList<TimeEntry> segmentEntries,
        IReadOnlyList<AbsenceEntry> segmentAbsences,
        DateOnly segmentStart,
        DateOnly segmentEnd,
        decimal flexBalanceCarry,
        CancellationToken ct)
    {
        var results = new List<CalculationResult>();
        var failureCount = 0;

        // Parallelize independent rules: 4 time rules + absence (FlexBalanceRule has carry-state
        // and runs sequentially after the others).
        var timeRuleIds = new[] { "NORM_CHECK_37H", "SUPPLEMENT_CALC", "OVERTIME_CALC", "ON_CALL_DUTY" };
        var timeRuleTasks = timeRuleIds
            .Select(ruleId => CallTimeRuleAsync(client, ruleId, segmentProfile, segmentEntries, segmentStart, segmentEnd, ct))
            .ToList();
        var absenceTask = CallAbsenceRuleAsync(client, segmentProfile, segmentAbsences, segmentStart, segmentEnd, ct);

        var allTasks = timeRuleTasks.Append(absenceTask).ToArray();
        await Task.WhenAll(allTasks);

        foreach (var task in timeRuleTasks)
        {
            var result = await task;
            if (result is not null)
                results.Add(result);
            else
                failureCount++;
        }

        var absenceResult = await absenceTask;
        if (absenceResult is not null)
            results.Add(absenceResult);
        else
            failureCount++;

        // FlexBalanceRule — sequential; uses the carried balance.
        var flexResult = await CallFlexRuleAsync(
            client, segmentProfile, segmentEntries, segmentAbsences,
            segmentStart, segmentEnd, flexBalanceCarry, ct);
        if (flexResult is not null)
            results.Add(flexResult);
        else
            failureCount++;

        // RulesAttempted: 4 time rules + absence + flex == 6 (computed, not magic).
        var rulesAttempted = timeRuleIds.Length + 1 /* absence */ + 1 /* flex */;
        return (results, failureCount, rulesAttempted);
    }

    private async Task<List<PayrollExportLine>> MapSegmentToExportLinesAsync(
        IReadOnlyList<CalculationResult> segmentRuleResults,
        EmploymentProfile segmentProfile,
        DateOnly segmentStart,
        DateOnly segmentEnd,
        Guid manifestId,
        CancellationToken ct)
    {
        var lines = new List<PayrollExportLine>();

        foreach (var result in segmentRuleResults)
        {
            if (!result.Success) continue;

            foreach (var lineItem in result.LineItems)
            {
                var mapping = await _mappingService.GetMappingAsync(
                    lineItem.TimeType, segmentProfile.OkVersion, segmentProfile.AgreementCode,
                    segmentProfile.Position, ct);

                if (mapping is null)
                {
                    _logger.LogWarning(
                        "No wage type mapping for {TimeType}/{OkVersion}/{Agreement} — skipping line item from rule {RuleId}",
                        lineItem.TimeType, segmentProfile.OkVersion, segmentProfile.AgreementCode, result.RuleId);
                    continue;
                }

                lines.Add(new PayrollExportLine
                {
                    EmployeeId = segmentProfile.EmployeeId,
                    WageType = mapping.WageType,
                    Hours = lineItem.Hours,
                    Amount = lineItem.Hours * lineItem.Rate,
                    PeriodStart = segmentStart,
                    PeriodEnd = segmentEnd,
                    OkVersion = segmentProfile.OkVersion, // segment-resolved, NOT period-level
                    SourceRuleId = result.RuleId,
                    SourceTimeType = lineItem.TimeType,
                    ManifestId = manifestId, // ADR-016 D10: per-line manifest linkage for audit chain (TASK-2010 follow-up)
                });
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

    private static decimal? ExtractFlexDelta(IReadOnlyList<CalculationResult> ruleResults)
    {
        var flexResult = ruleResults.FirstOrDefault(r =>
            r.RuleId.Equals("FLEX_BALANCE", StringComparison.OrdinalIgnoreCase));
        if (flexResult is null || !flexResult.Success)
            return null;
        return flexResult.LineItems.Sum(li => li.Hours);
    }

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
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for {RuleId}: {Body}",
                    (int)response.StatusCode, ruleId, body);
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
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for absence evaluation: {Body}",
                    (int)response.StatusCode, body);
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

    private async Task<CalculationResult?> CallFlexRuleAsync(
        HttpClient client,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                profile,
                entries,
                absences,
                periodStart,
                periodEnd,
                previousBalance
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate-flex", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for flex evaluation: {Body}",
                    (int)response.StatusCode, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for flex evaluation");
            return null;
        }
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
/// <strong>Cross-domain wiring</strong>: the production implementation is HTTP-backed
/// against the Rule Engine's RuleRegistry (cross-domain dep — TASK-2010 wires a
/// <c>GET /api/rules/classifications</c> endpoint on the Rule Engine and the matching
/// HTTP client provider in <c>StatsTid.Integrations.Payroll.Program.cs</c>). Until that
/// lands, <see cref="EmptyRuleClassificationProvider"/> is the default fallback so the
/// service still constructs successfully — at the cost of D9 invariants being silenced
/// (logged on each fallback merge in <see cref="PeriodCalculationService"/>).
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
    /// may cache the response across calls (the Rule Engine's registry is immutable per
    /// process startup), and MUST return a non-null list (use <see cref="Array.Empty{T}"/>
    /// when there is genuinely nothing to return — that disables D9 invariants and produces
    /// a warning at the merge step, but does not throw).
    /// </summary>
    IReadOnlyList<RuleClassification> GetClassifications();
}

/// <summary>
/// Empty fallback used when no <see cref="IRuleClassificationProvider"/> is registered
/// in DI. Returns an empty list, which silences D9 invariants and routes every per-rule
/// MergeStrategy lookup through the "default to Concatenate with warning" fallback in
/// <see cref="PeriodCalculationService"/>.
///
/// <para>
/// <strong>Not for production use</strong>: install a real (HTTP-backed or in-process)
/// implementation in <c>Program.cs</c> as part of TASK-2010 wiring.
/// </para>
/// </summary>
public sealed class EmptyRuleClassificationProvider : IRuleClassificationProvider
{
    public static readonly EmptyRuleClassificationProvider Instance = new();

    public IReadOnlyList<RuleClassification> GetClassifications() =>
        Array.Empty<RuleClassification>();
}
