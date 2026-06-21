using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Service that re-runs PeriodCalculationService for a past period and produces
/// correction lines by diffing against a previous export. Supports retroactive
/// recalculation with full traceability and event sourcing.
///
/// <para>
/// Segmentation across boundaries (OK-version transitions, agreement-config
/// promotions, position-override effective dates, EU-WTD ruleset transitions,
/// etc.) is now driven entirely by the planner inside
/// <see cref="PeriodCalculationService"/>. This service does NOT segment
/// the period locally any more — it forwards the period as-is to the
/// calculation service, which calls <c>PeriodPlanner.Plan</c> and produces
/// a per-segment, per-line OK-stamped result (S20 wave 1+2 / TASK-2009 /
/// ADR-016 D6).
/// </para>
///
/// <para>
/// <strong>ADR-013 (no-cascade) preservation:</strong> enforced by the planner's
/// geometric bound — <c>PeriodPlanner.Plan</c> never expands the input window
/// (D4 alignment policy can only shrink it; <c>aligned-window</c> rules either
/// pass or reject) — combined with <c>FlexBalanceRule</c>'s chained-balance
/// hand-off across in-period segments. The planner produces exactly one
/// <c>PlannedCalculation</c> confined to <c>[periodStart, periodEnd]</c>; per-segment
/// rule evaluation never consults a neighbouring period. The merge strategies
/// (RejectIfMultipleSegments for aligned-window rules, UnionDedupe for compliance,
/// Concatenate / Custom for calc rules) are the merge mechanism — not the
/// no-cascade enforcement mechanism.
/// </para>
/// </summary>
public sealed class RetroactiveCorrectionService
{
    private readonly PeriodCalculationService _calculationService;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly IAuditProjectionMapper<RetroactiveCorrectionRequested> _auditMapper;
    private readonly PayrollExportRecordRepository _exportRecordRepo;
    private readonly ILogger<RetroactiveCorrectionService> _logger;

    public RetroactiveCorrectionService(
        PeriodCalculationService calculationService,
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        AuditProjectionRepository auditRepo,
        IAuditProjectionMapper<RetroactiveCorrectionRequested> auditMapper,
        PayrollExportRecordRepository exportRecordRepo,
        ILogger<RetroactiveCorrectionService> logger)
    {
        _calculationService = calculationService;
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _auditRepo = auditRepo;
        _auditMapper = auditMapper;
        _exportRecordRepo = exportRecordRepo;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates a past period and produces correction lines by diffing against
    /// the previously-exported lines. All segmentation is delegated to the planner
    /// inside <see cref="PeriodCalculationService"/>.
    ///
    /// <para>
    /// <strong>S90 / TASK-9004 (ADR-034 / B3) — the manifest IS the diff baseline.</strong>
    /// The diff baseline is NO LONGER caller-supplied: it is READ from
    /// <c>payroll_export_records.current_effective_lines</c> for the (employee, year, month) the
    /// period falls in (the month derived from <paramref name="periodStart"/>). A month with NO
    /// export record (never sent to payroll) is rejected with
    /// <see cref="PayrollExportNotFoundException"/> — a never-exported month is reopened/edited, NOT
    /// corrected (corrections-only-post-lock). After the correction computes its new effective lines,
    /// the baseline is UPDATED to that corrected state IN THE SAME tx that emits the correction event
    /// + audit row, so a SECOND correction diffs against the FIRST correction's result (no
    /// double-count). <c>original_lines</c> stays immutable.
    /// </para>
    ///
    /// <para>
    /// <paramref name="okTransitionDate"/> and <paramref name="previousOkVersion"/>
    /// are kept on the public signature for backward compatibility (the
    /// <c>/api/payroll/recalculate</c> endpoint contract), but they no longer drive
    /// a manual two-segment recalculation. The planner derives segmentation from
    /// <see cref="OkVersionResolver"/> on its own. The two parameters are now
    /// purely informational and feed into <see cref="OkVersionCanonicalization.Resolve"/>
    /// so the audit event records the canonical (date-resolved) OK versions
    /// instead of (potentially stale) caller-supplied values.
    /// </para>
    ///
    /// <para>
    /// <strong>ADR-013 (no-cascade)</strong>: the planner produces one
    /// <c>PlannedCalculation</c> bounded by <c>[periodStart, periodEnd]</c>;
    /// nothing in the call graph cascades into neighbouring periods. ADR-013's
    /// constraint is enforced by the planner's geometric bound (Plan never
    /// expands the input window) and FlexBalanceRule's in-period chained-balance
    /// hand-off — NOT by this service, and NOT by any merge strategy. The merge
    /// strategies in PCS handle within-period segment merging only.
    /// </para>
    /// <para>
    /// <strong>ADR-016 D6</strong>: the previous local two-segment
    /// recalculation (<c>RecalculateWithVersionSplitAsync</c>) was retired by
    /// TASK-2009; segmentation is the planner's responsibility, end-to-end.
    /// </para>
    /// </summary>
    public async Task<RetroactiveCorrectionResult> RecalculateAsync(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlexBalance,
        string reason,
        string actorId,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        Guid? idempotencyToken = null,
        DateOnly? okTransitionDate = null,
        string? previousOkVersion = null,
        CancellationToken ct = default)
    {
        // Coherent-pair validation: either both split inputs are present or neither.
        // Mismatched inputs indicate a caller bug and must not silently fall through.
        // This guard is independent of segmentation — it pins the public contract of
        // the /api/payroll/recalculate endpoint and is unaffected by the planner-driven
        // rewrite.
        if (okTransitionDate.HasValue ^ previousOkVersion is not null)
        {
            return new RetroactiveCorrectionResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CorrectionLines = [],
                Success = false,
                ErrorMessage = "OkTransitionDate and PreviousOkVersion must both be provided or both omitted."
            };
        }

        // S90 / TASK-9004 (B3) — the diff baseline is the PERSISTED manifest, not caller input.
        // The (employee, year, month) this period falls in. The month/year are derived from periodStart
        // (consistent with how PayrollExportService keys a record by line.PeriodStart).
        var corrYear = periodStart.Year;
        var corrMonth = periodStart.Month;

        // Pre-compute existence gate (corrections-only-post-lock): a month with NO export record was
        // never sent to payroll → there is nothing to correct; reject BEFORE the (expensive) rule-engine
        // compute so the operator reopens/edits instead. This is a cheap, non-locking probe; the
        // AUTHORITATIVE baseline used for the diff is re-read under FOR UPDATE inside the correction tx
        // below (BLOCKER 3 — so two concurrent /recalculate calls serialize on the same row).
        var preCheckBaseline = await _exportRecordRepo.TryReadCurrentEffectiveLinesAsync(
            profile.EmployeeId, corrYear, corrMonth, ct);
        if (preCheckBaseline is null)
        {
            throw new PayrollExportNotFoundException(
                $"Måneden er ikke sendt til lønkørsel (medarbejder '{profile.EmployeeId}', {corrYear}-{corrMonth:D2}) " +
                $"— der er intet at korrigere; genåbn i stedet.");
        }

        // Canonicalise OK versions from the period/transition dates so the audit event
        // reflects what the calculation actually used (ADR-003 / TASK-1904). The pure
        // resolution lives in OkVersionCanonicalization so the per-branch behaviour can
        // be unit-tested without standing up the service stack — Codex first-pass S19
        // review flagged that pinning OkVersionResolver alone left the service-level
        // choice (resolver vs. profile.OkVersion) untested.
        var canonical = OkVersionCanonicalization.Resolve(
            profile.OkVersion, periodStart, okTransitionDate, previousOkVersion);
        string canonicalCurrentOkVersion = canonical.CurrentOkVersion;
        string? canonicalPreviousOkVersion = canonical.PreviousOkVersion;

        if (canonical.PreviousDrifted)
        {
            _logger.LogWarning(
                "Caller-supplied PreviousOkVersion '{Supplied}' differs from date-resolved '{Resolved}' for transition {Transition}. Using resolved value.",
                previousOkVersion, canonical.PreviousOkVersion, okTransitionDate);
        }
        if (canonical.CurrentDrifted)
        {
            _logger.LogWarning(
                "Caller-supplied profile.OkVersion '{Supplied}' differs from date-resolved '{Resolved}' (path={Path}). Using resolved value in audit event.",
                profile.OkVersion, canonical.CurrentOkVersion,
                okTransitionDate.HasValue && previousOkVersion is not null ? "split" : "single-version");
        }

        // ---------------------------------------------------------------
        // Planner-driven recalculation (S20 wave 1+2 / ADR-016 D6).
        //
        // The shim overload of CalculateAsync internally calls
        // PeriodPlanner.Plan(...) and returns a PeriodCalculationResult whose
        // ExportLines are already per-segment, per-line OK-stamped. There is
        // no need (and it would be wrong) to segment locally here — the
        // planner detects boundaries from OkVersionResolver and produces 1
        // segment for non-straddling periods or 2+ segments for straddling
        // ones. The previously-hand-coded two-segment recalculation
        // (RecalculateWithVersionSplitAsync) was retired by TASK-2009.
        //
        // The shim overload is [Obsolete]-marked because TASK-2010 will
        // migrate the export boundary to construct PlannedCalculation
        // directly. We are inside the migration path here, so the call is
        // intentional and the warning is suppressed locally only — do NOT
        // suppress at file scope.
        // ---------------------------------------------------------------
#pragma warning disable CS0618 // RetroactiveCorrectionService intentionally calls the [Obsolete] shim overload during the TASK-2009 / TASK-2010 migration; remove once the service constructs PlannedCalculation explicitly.
        var newResult = await _calculationService.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd,
            previousFlexBalance, authorizationHeader, correlationId, ct);
#pragma warning restore CS0618

        if (!newResult.Success)
        {
            return new RetroactiveCorrectionResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CorrectionLines = [],
                Success = false,
                ErrorMessage = $"Recalculation failed: {newResult.ErrorMessage}"
            };
        }

        // Flex delta is extracted from the merged rule results; FlexBalanceRule's
        // own chained-balance hand-off (in-period across segments) means
        // newResult.RuleResults already carries the across-segment net delta.
        decimal? flexDelta = ExtractFlexDelta(newResult.RuleResults);

        var resolvedOrgId = profile.OrgId
            ?? throw new InvalidOperationException(
                $"Audit projection: employee {profile.EmployeeId} has no OrgId; cannot resolve target_org_id.");
        var manifestId = newResult.RuleResults.FirstOrDefault()?.ManifestId ?? Guid.Empty;

        // ── BLOCKER 2 + 3 — the baseline read + diff + event + audit + baseline-UPDATE are now ONE
        // MANDATORY, SERIALIZED transactional unit. ──
        //
        // BLOCKER 3: the AUTHORITATIVE diff baseline is re-read under SELECT … FOR UPDATE INSIDE this
        // tx and the row lock is HELD through the diff compute + the UpdateCurrentEffectiveLinesAsync
        // + the COMMIT. Two concurrent /recalculate calls for the same (employee, month) therefore
        // SERIALIZE: the second blocks on the FOR UPDATE until the first commits, then reads the
        // FIRST correction's updated baseline (no last-update-wins / no diff against a stale baseline).
        //
        // BLOCKER 2: the event enqueue + audit insert + baseline UPDATE are LOAD-BEARING (they advance
        // the diff baseline so the NEXT correction does not double-count). A failure of ANY of them
        // must FAIL the whole call (rethrow → the endpoint maps a 5xx) and roll the tx back — it must
        // NEVER be swallowed into a phantom Success=true (which would hand the caller correction lines
        // while leaving the baseline un-advanced → the exact B3 double-count this sprint prevents).
        IReadOnlyList<CorrectionExportLine> correctionLines;
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // BLOCKER 3 — the authoritative baseline, read + locked inside the tx. A null here means
            // the record vanished between the pre-check probe and this lock (concurrent deletion) →
            // fail loud (corrections-only-post-lock; nothing is committed).
            var lockedBaseline = await PayrollExportRecordRepository.TryReadCurrentEffectiveLinesForUpdateAsync(
                conn, tx, profile.EmployeeId, corrYear, corrMonth, ct);
            if (lockedBaseline is null)
            {
                throw new PayrollExportNotFoundException(
                    $"Payroll-eksportrækken for medarbejder '{profile.EmployeeId}' ({corrYear}-{corrMonth:D2}) " +
                    $"forsvandt under korrektionen; korrektionen blev rullet tilbage.");
            }

            // Diff new export lines against the LOCKED baseline. ProduceCorrectionLines already honours
            // each line's per-line OkVersion (S14), which is exactly what the planner-driven path emits
            // — so OK-version-aware diffing works whether the planner produced 1 or N segments.
            correctionLines = ProduceCorrectionLines(
                profile, lockedBaseline, newResult.ExportLines, periodStart, periodEnd,
                flexDelta, okTransitionDate, canonicalPreviousOkVersion);

            // Emit RetroactiveCorrectionRequested (MANDATORY — see BLOCKER 2 above).
            // Persist the canonical (date-resolved) OK versions and the profile Position so the audit
            // trail reflects what actually ran, not what the caller sent. ManifestId joins this event
            // to SegmentManifestCreated for the actual N-segment plan (closes Codex P2 / Reviewer
            // audit-event NOTE: the OkVersion/PreviousOkVersion pair only describes the canonicalized
            // 2-version view — full segmentation is recoverable via the manifest).
            var correctionEvent = new RetroactiveCorrectionRequested
            {
                EmployeeId = profile.EmployeeId,
                OriginalPeriodStart = periodStart,
                OriginalPeriodEnd = periodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = canonicalCurrentOkVersion,
                Reason = reason,
                CorrectedByActorId = actorId,
                CorrectionLineCount = correctionLines.Count,
                TotalDifferenceHours = correctionLines.Sum(l => Math.Abs(l.DifferenceHours)),
                CorrelationId = correlationId,
                IdempotencyToken = idempotencyToken,
                PreviousOkVersion = canonicalPreviousOkVersion,
                Position = profile.Position,
                ManifestId = manifestId
            };

            var streamId = $"retro-correction-{profile.EmployeeId}-{periodStart:yyyy-MM-dd}";
            var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, correctionEvent, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actorId,
                ActorPrimaryOrgId: resolvedOrgId,
                CorrelationId: correlationId,
                OccurredAt: new DateTimeOffset(correctionEvent.OccurredAt),
                ResolvedTargetOrgId: resolvedOrgId);
            var auditRow = _auditMapper.Map(correctionEvent, auditCtx);
            await _auditRepo.InsertAsync(conn, tx, correctionEvent.EventId, outboxId, correctionEvent.EventType, auditRow, auditCtx, ct);

            // B3 — evolve the diff baseline IN THE SAME tx (still under the held FOR UPDATE lock):
            // current_effective_lines := the corrected lines, so a SECOND correction diffs against THIS
            // correction's result (no double-count). original_lines is left immutable. The corrected
            // lines are newResult.ExportLines (what a future correction must diff against). A 0-row
            // update means the record vanished mid-tx — fail loud rather than silently desync.
            var rowsUpdated = await PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync(
                conn, tx, profile.EmployeeId, corrYear, corrMonth, newResult.ExportLines, ct);
            if (rowsUpdated == 0)
            {
                throw new PayrollExportNotFoundException(
                    $"Payroll-eksportrækken for medarbejder '{profile.EmployeeId}' ({corrYear}-{corrMonth:D2}) " +
                    $"forsvandt under korrektionen; korrektionen blev rullet tilbage.");
            }

            // COMMIT — releases the FOR UPDATE lock; the baseline is durably advanced. Any throw above
            // disposes the tx (rollback) WITHOUT this commit and propagates (BLOCKER 2: no swallow).
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation(
            "Retroactive correction for {EmployeeId} period {PeriodStart}-{PeriodEnd}: {LineCount} correction lines{SplitInfo}",
            profile.EmployeeId, periodStart, periodEnd, correctionLines.Count,
            okTransitionDate.HasValue ? $" (split at {okTransitionDate.Value:yyyy-MM-dd}, {canonicalPreviousOkVersion} → {canonicalCurrentOkVersion})" : "");

        return new RetroactiveCorrectionResult
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CorrectionLines = correctionLines,
            Success = true
        };
    }

    /// <summary>
    /// Extracts the flex balance delta from rule results.
    /// Looks for the FLEX_BALANCE rule result and computes delta from its line items.
    /// Returns null if no flex rule result found.
    /// </summary>
    private static decimal? ExtractFlexDelta(IReadOnlyList<CalculationResult> ruleResults)
    {
        var flexResult = ruleResults.FirstOrDefault(r =>
            r.RuleId.Equals("FLEX_BALANCE", StringComparison.OrdinalIgnoreCase));

        if (flexResult is null || !flexResult.Success)
            return null;

        // The flex rule produces line items representing the delta.
        // Sum all flex line item hours as the delta.
        return flexResult.LineItems.Sum(li => li.Hours);
    }

    private static IReadOnlyList<CorrectionExportLine> ProduceCorrectionLines(
        EmploymentProfile profile,
        IReadOnlyList<PayrollExportLine> previousLines,
        IReadOnlyList<PayrollExportLine> newLines,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal? flexDelta,
        DateOnly? okTransitionDate,
        string? previousOkVersion)
    {
        var corrections = new List<CorrectionExportLine>();

        // Index previous lines by wage type
        var previousByWageType = previousLines
            .GroupBy(l => l.WageType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Index new lines by wage type
        var newByWageType = newLines
            .GroupBy(l => l.WageType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // All wage types from both sets
        var allWageTypes = previousByWageType.Keys
            .Union(newByWageType.Keys)
            .Distinct();

        foreach (var wageType in allWageTypes)
        {
            var prevItems = previousByWageType.GetValueOrDefault(wageType, []);
            var newItems = newByWageType.GetValueOrDefault(wageType, []);

            var originalHours = prevItems.Sum(l => l.Hours);
            var originalAmount = prevItems.Sum(l => l.Amount);
            var correctedHours = newItems.Sum(l => l.Hours);
            var correctedAmount = newItems.Sum(l => l.Amount);
            var diffHours = correctedHours - originalHours;
            var diffAmount = correctedAmount - originalAmount;

            // Only produce correction line if there's a difference
            if (diffHours != 0 || diffAmount != 0)
            {
                // Pick traceability from new lines if available, else from previous
                var sourceItem = newItems.FirstOrDefault() ?? prevItems.FirstOrDefault();

                // Determine the OK version for this correction line:
                // - In split mode, use the OkVersion from the new export line (which reflects
                //   the correct version per segment)
                // - In single mode, use profile.OkVersion
                var lineOkVersion = newItems.FirstOrDefault()?.OkVersion
                    ?? prevItems.FirstOrDefault()?.OkVersion
                    ?? profile.OkVersion;

                // Attach flex delta only to flex-related wage types
                var isFlexWageType = sourceItem?.SourceRuleId?.Equals("FLEX_BALANCE", StringComparison.OrdinalIgnoreCase) == true;

                corrections.Add(new CorrectionExportLine
                {
                    EmployeeId = profile.EmployeeId,
                    WageType = wageType,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OkVersion = lineOkVersion,
                    OriginalHours = originalHours,
                    OriginalAmount = originalAmount,
                    CorrectedHours = correctedHours,
                    CorrectedAmount = correctedAmount,
                    DifferenceHours = diffHours,
                    DifferenceAmount = diffAmount,
                    SourceRuleId = sourceItem?.SourceRuleId,
                    SourceTimeType = sourceItem?.SourceTimeType,
                    FlexDelta = isFlexWageType ? flexDelta : null
                });
            }
        }

        return corrections;
    }
}

public sealed class RetroactiveCorrectionResult
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required IReadOnlyList<CorrectionExportLine> CorrectionLines { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// S90 / TASK-9004 (ADR-034 / B3) — raised when <c>/recalculate</c> is asked to correct a month
/// that has NO <c>payroll_export_records</c> row (never sent to payroll). A never-exported month is
/// reopened/edited, not corrected (corrections-only-post-lock) — the endpoint maps this to a clean
/// 4xx ("Måneden er ikke sendt til lønkørsel — der er intet at korrigere; genåbn i stedet.").
/// </summary>
public sealed class PayrollExportNotFoundException : Exception
{
    public PayrollExportNotFoundException(string message) : base(message) { }
}
