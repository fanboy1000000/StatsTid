using Npgsql;
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
    /// <para>
    /// <strong>S145 / TASK-14508 (rulings R14 and R25) — no hours parameters.</strong> The correction
    /// recalculates on the employee's CURRENT recorded hours, which
    /// <see cref="PeriodCalculationService"/> reads itself (through its
    /// <see cref="IRecordedHoursReader"/>) when this service calls it — before the correction
    /// transaction opens, so no hours read ever runs under the export record's FOR UPDATE lock.
    /// Keeping an hours-taking overload would keep the defect class R14 removes: a caller could leave
    /// out a day and nobody would notice.
    /// </para>
    /// <para>
    /// <strong>S145 / TASK-14512 — admission, and the next month.</strong> A recalculation is a forward
    /// calculation, so <see cref="PeriodCalculationService"/>'s admission runs in full: the planner's
    /// split refusal, the week check (<see cref="PeriodNotCalculableException"/>) and the months-in-order
    /// check (<see cref="PriorMonthNotApprovedException"/>) all propagate from the calculation call below,
    /// BEFORE the correction transaction opens, so a refused correction writes nothing (no baseline change,
    /// event, audit row or idempotency marker). And Q7 (b) (R37): when the corrected month does not end on
    /// a Sunday and the next month has an export record, the result, the event and the audit row name the
    /// next month's pay period (<see cref="RetroactiveCorrectionResult.NextPeriodToRecalculateStart"/> /
    /// <c>…End</c>), because the next month already paid this month's closing week.
    /// </para>
    /// <para>
    /// <strong>S146 / TASK-14604 (QUAL-205) — no correction without its proof.</strong> The correction event links
    /// the recalculation's manifest (S145 R-19), so the manifest must exist. When it was not persisted,
    /// <see cref="PeriodCalculationService.PeriodCalculationOutcome.ThrowIfNotProvable"/> throws
    /// <see cref="ManifestNotPersistedException"/> before the correction transaction opens: nothing is written.
    /// </para>
    /// <para>
    /// <strong>S146 / TASK-14605 (QUAL-188; R-14, R-15, R-10) — the flex ledger.</strong> No opening balance is
    /// passed: the recalculation's admission derives it from the recorded ledger (and may refuse with
    /// <see cref="FlexLedgerRefusalException"/> before the transaction opens). The correction transaction takes the
    /// per-employee ledger lock as its FIRST statement, before the record's <c>FOR UPDATE</c>, then re-runs the
    /// derivation on in-transaction reads and requires the (opening, provenance) its OWN calculation used
    /// (<c>payroll-ledger-changed</c> otherwise). It records the month's corrected balances beside
    /// <c>current_effective_lines</c>; the event and the result carry the opening used and the closing before and
    /// after; and the next month is named when the closing changed (C4), Sunday or not. Under <c>UNTRACKED</c> (a raw
    /// month before the ledger start) <c>FLEX_PAYOUT</c> is excluded from both sides of the diff and the flex delta is
    /// null, so no flex correction line is emitted and a payout already made is never clawed back; the balances stay
    /// null. A month that lost every EMPLOYED day (a windows edit) derives no opening (the function's precondition):
    /// its line diff is corrected, its recorded balances stay unchanged, and its event says so (no opening used,
    /// no provenance, the same closing before and after; reviewer c3 N7, decided: leave unchanged, not refuse).
    /// </para>
    /// </summary>
    /// <exception cref="ManifestNotPersistedException">The recalculation's manifest was not persisted (or a
    /// successful result carries none); nothing was written.</exception>
    /// <exception cref="FlexLedgerRefusalException">The month's opening could not be derived (admission), or the
    /// ledger changed while the correction was computed (the under-lock recheck); nothing was written.</exception>
    public async Task<RetroactiveCorrectionResult> RecalculateAsync(
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
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
        //
        // S145 TASK-14508 (R14): no hours are passed — PCS reads the employee's current recorded
        // rows itself over [the day before the opening week's Monday, periodEnd]. This call still
        // runs BEFORE the correction transaction below, so the hours read (like planning) holds no
        // lock and a refusal or failure here leaves the baseline, the event and the marker untouched.
        //
        // S145 ruling R-11: the OUTCOME-returning sibling (the same CalculatePlanlessAsync body, so the
        // result and its export lines are bit-identical to the planless CalculateAsync — the switch
        // /calculate-and-export made in S134, QUAL-003). The outcome carries the calculation's frozen
        // inputs (PlanInputs), which is where the correction event's agreement and position come from.
        // ---------------------------------------------------------------
        //
        // S146 TASK-14605 (R-14): no opening balance is passed. Admission derives it from the recorded ledger,
        // after the existing checks and before any rule call; a refusal (FlexLedgerRefusalException, 409) leaves
        // nothing behind, like every other admission refusal above.
#pragma warning disable CS0618 // RetroactiveCorrectionService intentionally calls the [Obsolete] shim overload during the TASK-2009 / TASK-2010 migration; remove once the service constructs PlannedCalculation explicitly.
        var outcome = await _calculationService.CalculateWithOutcomeAsync(
            profile, periodStart, periodEnd,
            authorizationHeader, correlationId, ct);
#pragma warning restore CS0618

        // S146 TASK-14604 (QUAL-205; rulings R-22, R-27): the recalculation's proof — its manifest, written in one
        // transaction with the legacy summary event — must be stored before anything is corrected from it.
        // ThrowIfNotProvable is the ONE predicate both calculating routes call; it throws
        // ManifestNotPersistedException (the endpoint answers 503 payroll-manifest-not-persisted) when the manifest
        // was not persisted, or when a successful result carries no manifest. It runs BEFORE the correction
        // transaction below opens, so a refusal writes nothing: no correction event, no ADR-026 audit row, no
        // baseline change and no idempotency marker. A failed recalculation (Success == false) passes and is
        // answered below as before.
        outcome.ThrowIfNotProvable();

        var newResult = outcome.Result;

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

        // S146 TASK-14605 (R-14): the opening admission derived. null when the month holds no EMPLOYED day (the
        // function's precondition, reviewer c3 N7): no flex is settled and the record's balances stay unchanged.
        var flexOpening = outcome.FlexOpening;
        var untracked = flexOpening?.IsUntracked == true;

        // Flex delta is extracted from the merged rule results. Flex is evaluated once
        // across the whole plan (S145 B1: TimelineAware → SinglePlanEvaluation), so
        // newResult.RuleResults already carries the period's net delta.
        // S146 TASK-14605 (R-14 case 3b; reviewer c3 W4): under UNTRACKED the flex is not tracked, so there is no
        // flex delta and no flex correction line (FLEX_PAYOUT is also excluded from both sides of the diff below).
        decimal? flexDelta = untracked ? null : ExtractFlexDelta(newResult.RuleResults);

        var resolvedOrgId = profile.OrgId
            ?? throw new InvalidOperationException(
                $"Audit projection: employee {profile.EmployeeId} has no OrgId; cannot resolve target_org_id.");
        // S145 ruling R-19: the manifest this recalculation persisted, straight from the outcome. Never derived
        // from the first rule result: a month with no EMPLOYED day evaluates nothing and has no rule results,
        // yet its manifest is still written, and the event must still link it.
        var manifestId = outcome.ManifestId;

        // S145 rulings R-10 / R-11 — what the calculation actually RAN under: the frozen piece of the first
        // EMPLOYED segment (null when no segment is EMPLOYED, so nothing ran under any agreement or position).
        // Every successful calculation freezes its inputs, so a success without them is a programmer error:
        // refuse loudly here, BEFORE the correction transaction opens (nothing is written), never fall back to
        // the caller's profile and never dereference blindly.
        var frozenInputs = outcome.PlanInputs
            ?? throw new InvalidOperationException(
                $"RetroactiveCorrectionService invariant violated: the recalculation for employee " +
                $"{profile.EmployeeId} succeeded without frozen inputs (PlanInputs), so the correction event " +
                $"cannot record what ran. ManifestId={outcome.ManifestId}.");
        var ran = PeriodCalculationService.FirstEmployedSegmentPiece(frozenInputs.Pieces, periodStart);

        // ── BLOCKER 2 + 3 — the baseline read + diff + event + audit + baseline-UPDATE + idempotency-
        // marker (S132/QUAL-006) are now ONE MANDATORY, SERIALIZED transactional unit. ──
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
        (DateOnly Start, DateOnly End)? nextPeriod;
        decimal? closingBefore;
        decimal? closingAfter;
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // S146 TASK-14605 (R-15; C3) — the per-employee ledger lock is the FIRST statement, before the record's
            // FOR UPDATE below, as in every writer of payroll_export_records: a correction of M and an export of M+1
            // (which reads M's closing) serialize here, whichever comes first.
            await PayrollExportRecordRepository.AcquireFlexLedgerLocksAsync(conn, tx, new[] { profile.EmployeeId }, ct);

            // S146 TASK-14605 (R-14) — the under-lock recheck: the same function on in-transaction reads must give
            // the (opening, provenance) THIS correction's calculation used — not M's recorded values, which are
            // what a correction may be correcting. A month with no EMPLOYED day derived nothing and has nothing to
            // re-check. Throws FlexLedgerRefusalException payroll-ledger-changed (the transaction rolls back).
            if (flexOpening is not null)
            {
                await _calculationService.RecheckFlexOpeningUnderLockAsync(
                    conn, tx, profile.EmployeeId, new DateOnly(corrYear, corrMonth, 1), flexOpening,
                    PeriodCalculationService.FlexLedgerRoute.Correction, ct);
            }

            // BLOCKER 3 — the authoritative baseline, read + locked inside the tx. A null here means
            // the record vanished between the pre-check probe and this lock (concurrent deletion) →
            // fail loud (corrections-only-post-lock; nothing is committed).
            // S146 TASK-14605 (C4, reviewer c3 N4): the same locked read now also returns the month's recorded
            // balances, so the closing BEFORE this correction is read inside this transaction.
            var lockedRecord = await PayrollExportRecordRepository.TryReadRecordForUpdateAsync(
                conn, tx, profile.EmployeeId, corrYear, corrMonth, ct);
            if (lockedRecord is null)
            {
                throw new PayrollExportNotFoundException(
                    $"Payroll-eksportrækken for medarbejder '{profile.EmployeeId}' ({corrYear}-{corrMonth:D2}) " +
                    $"forsvandt under korrektionen; korrektionen blev rullet tilbage.");
            }
            var lockedBaseline = lockedRecord.Lines;

            // S146 TASK-14605 (R-14 case 3b; reviewer c3 W4) — under UNTRACKED, FLEX_PAYOUT leaves BOTH sides of the
            // diff: the recalculation already dropped it (PCS), and the baseline's is set aside here, so a payout
            // that was really paid produces no correction line and is never clawed back. The set-aside payout lines
            // stay in the new effective baseline: nothing reversed them, so they are still what payroll holds
            // (current_effective_lines = the original plus every correction line, wage type by wage type).
            var diffBaseline = lockedBaseline;
            var diffNew = newResult.ExportLines;
            var newEffectiveLines = newResult.ExportLines;
            if (untracked)
            {
                diffBaseline = lockedBaseline.Where(l => !IsFlexPayout(l)).ToList();
                diffNew = newResult.ExportLines.Where(l => !IsFlexPayout(l)).ToList();
                newEffectiveLines = diffNew.Concat(lockedBaseline.Where(IsFlexPayout)).ToList();
            }

            // Diff new export lines against the LOCKED baseline. ProduceCorrectionLines already honours
            // each line's per-line OkVersion (S14), which is exactly what the planner-driven path emits
            // — so OK-version-aware diffing works whether the planner produced 1 or N segments.
            correctionLines = ProduceCorrectionLines(
                profile, diffBaseline, diffNew, periodStart, periodEnd,
                flexDelta, okTransitionDate, canonicalPreviousOkVersion);

            // S146 TASK-14605 (C1) — the month's closing before and after this correction. Tracked: the engine's
            // closing. UNTRACKED: none (never stored). No EMPLOYED day: unchanged (nothing was settled).
            closingBefore = lockedRecord.ClosingBalance;
            closingAfter = flexOpening is null
                ? closingBefore
                : untracked ? null : newResult.ClosingFlexBalance;

            // S145 TASK-14512 — Q7 (b), ruling R37: does this correction leave the NEXT month stale?
            // A plain read inside this tx (READ COMMITTED: it sees every export committed before it runs);
            // it takes no lock and writes nothing to the next month — the response, the event and the
            // audit row only name it. S146 TASK-14605 (C4, R-10): also when this correction changed M's closing,
            // which M+1 opened at.
            nextPeriod = await NextPeriodToRecalculateAsync(
                conn, tx, profile.EmployeeId, corrYear, corrMonth, closingChanged: closingBefore != closingAfter, ct);

            // Emit RetroactiveCorrectionRequested (MANDATORY — see BLOCKER 2 above).
            // The audit trail records what actually ran, never what the request body said:
            //   • AgreementCode and Position come from the calculation's frozen inputs — the first EMPLOYED
            //     segment's piece (`ran`, above), the agreement and position the engine was actually sent
            //     (S145 rulings R-10, R-11). Empty agreement and null position when no segment is EMPLOYED;
            //   • the OK versions are the canonical (date-resolved) ones.
            // ManifestId joins this event to SegmentManifestCreated for the actual N-segment plan (closes
            // Codex P2 / Reviewer audit-event NOTE: the OkVersion/PreviousOkVersion pair only describes the
            // canonicalized 2-version view — full segmentation is recoverable via the manifest).
            var correctionEvent = new RetroactiveCorrectionRequested
            {
                EmployeeId = profile.EmployeeId,
                OriginalPeriodStart = periodStart,
                OriginalPeriodEnd = periodEnd,
                AgreementCode = ran?.AgreementCode ?? string.Empty,
                OkVersion = canonicalCurrentOkVersion,
                Reason = reason,
                CorrectedByActorId = actorId,
                CorrectionLineCount = correctionLines.Count,
                TotalDifferenceHours = correctionLines.Sum(l => Math.Abs(l.DifferenceHours)),
                CorrelationId = correlationId,
                IdempotencyToken = idempotencyToken,
                PreviousOkVersion = canonicalPreviousOkVersion,
                Position = ran?.Position,
                ManifestId = manifestId,
                // S145 TASK-14512 — Q7 (b), R37: the next month's pay period, when it must be recalculated
                // too (null otherwise). The audit mapper carries both into the audit row's details.
                NextPeriodToRecalculateStart = nextPeriod?.Start,
                NextPeriodToRecalculateEnd = nextPeriod?.End,
                // S146 TASK-14605 (C1; makes ADR-013 item 2 true): the opening this recalculation used, the month's
                // closing before and after, and the opening's provenance. UNTRACKED: no opening, no closing after,
                // provenance UNTRACKED (flex not tracked). No EMPLOYED day: no opening, no provenance, and the same
                // closing before and after (the balances were left unchanged, reviewer c3 N7).
                OpeningFlexBalanceUsed = flexOpening is null || untracked ? null : flexOpening.Balance,
                ClosingFlexBalanceBefore = closingBefore,
                ClosingFlexBalanceAfter = closingAfter,
                FlexProvenance = flexOpening?.Provenance,
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
            // S146 TASK-14605 (C1): a tracked month's balances are written in the same statement (a raw month after
            // the ledger start fills its nulls here, R-14 4a); under UNTRACKED and for a month with no EMPLOYED day
            // the balances are left as they are (null, resp. unchanged) and only the lines move.
            var rowsUpdated = flexOpening is not null && !untracked
                ? await PayrollExportRecordRepository.UpdateCorrectedRecordAsync(
                    conn, tx, profile.EmployeeId, corrYear, corrMonth, newEffectiveLines,
                    flexOpening.Balance,
                    closingAfter ?? throw new InvalidOperationException(
                        $"RetroactiveCorrectionService invariant violated: the recalculation for employee " +
                        $"{profile.EmployeeId} derived a {flexOpening.Provenance} opening but carries no closing " +
                        $"balance. ManifestId={outcome.ManifestId}."),
                    flexOpening.Provenance, ct)
                : await PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync(
                    conn, tx, profile.EmployeeId, corrYear, corrMonth, newEffectiveLines, ct);
            if (rowsUpdated == 0)
            {
                throw new PayrollExportNotFoundException(
                    $"Payroll-eksportrækken for medarbejder '{profile.EmployeeId}' ({corrYear}-{corrMonth:D2}) " +
                    $"forsvandt under korrektionen; korrektionen blev rullet tilbage.");
            }

            // ── QUAL-006 (S132) — mark the idempotency token delivered IN THIS SAME tx ──
            // The duplicate-prevention marker now commits ATOMICALLY with the correction event +
            // audit row + baseline advance it guards. Pre-S132 this INSERT lived in the
            // /api/payroll/recalculate endpoint in a SEPARATE tx that ran AFTER this one committed,
            // and its failure was swallowed by an empty catch — silently disarming the guard for that
            // token, so a same-token retry re-ran the correction and emitted a PHANTOM second
            // correction event + audit row (auditability + idempotency invariants; ADR-034). Folding
            // it in here makes the marker load-bearing under the same BLOCKER 2 contract as the event
            // and audit insert above:
            //   • a failed marker write ROLLS BACK the whole correction (rethrow → the endpoint maps a
            //     5xx) — nothing half-applied, so a clean retry re-runs the correction from scratch;
            //   • the UNIQUE constraint on outbox_messages.idempotency_token fails a concurrent /
            //     duplicate same-token correction CLOSED here (23505 → rollback) instead of letting it
            //     double-emit — the guard is now enforced at the write boundary, not only by the
            //     endpoint's best-effort pre-check.
            // A null token (non-endpoint callers / test fixtures that opt out of idempotency) writes
            // no marker — the same contract as before this change.
            if (idempotencyToken.HasValue)
            {
                try
                {
                    await using var markCmd = new Npgsql.NpgsqlCommand(
                        """
                        INSERT INTO outbox_messages (destination, payload, status, delivered_at, idempotency_token)
                        VALUES ('retroactive-correction', '{}', 'delivered', NOW(), @token)
                        """, conn, tx);
                    markCmd.Parameters.AddWithValue("token", idempotencyToken.Value);
                    await markCmd.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex)
                {
                    // LOUD, not swallowed (the QUAL-006 defect was an empty catch here-in-spirit): record
                    // the token + employee/period + the exception so the failure is diagnosable, then
                    // rethrow so the tx rolls back and the whole correction fails closed. A Postgres
                    // 23505 unique_violation here means a concurrent/duplicate same-token correction was
                    // fail-closed (the guard doing its job); any other exception is a real marker-write
                    // fault — either way the correction MUST NOT be reported as succeeded.
                    _logger.LogError(ex,
                        "Idempotency-mark write failed for retroactive correction (token {IdempotencyToken}, employee {EmployeeId}, period {PeriodStart}..{PeriodEnd}); rolling back the correction — no event, audit row, or baseline change was committed.",
                        idempotencyToken.Value, profile.EmployeeId, periodStart, periodEnd);
                    throw;
                }
            }

            // COMMIT — releases the FOR UPDATE lock; the baseline is durably advanced and (for a
            // tokened call) the idempotency marker is set. Any throw above disposes the tx (rollback)
            // WITHOUT this commit and propagates (BLOCKER 2: no swallow).
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
            Success = true,
            NextPeriodToRecalculateStart = nextPeriod?.Start,
            NextPeriodToRecalculateEnd = nextPeriod?.End,
            // S146 TASK-14605: the same flex members as the event, plus a plain statement when flex was not
            // tracked (R-14 case 3b) or not settled (no EMPLOYED day, reviewer c3 N7).
            OpeningFlexBalanceUsed = flexOpening is null || untracked ? null : flexOpening.Balance,
            ClosingFlexBalanceBefore = closingBefore,
            ClosingFlexBalanceAfter = closingAfter,
            FlexProvenance = flexOpening?.Provenance,
            FlexNote = flexOpening is null
                ? NoEmployedDayFlexNote
                : untracked ? FlexLedgerOpening.UntrackedNote(flexOpening.LedgerStart) : null,
        };
    }

    /// <summary>
    /// S146 / TASK-14605 (reviewer c3 N7) — the statement a correction of a month with no EMPLOYED day carries: the
    /// opening derivation's precondition does not hold, so no flex is settled and the record's balances are left
    /// as they were.
    /// </summary>
    public const string NoEmployedDayFlexNote =
        "flex not settled: the month holds no employed day; the recorded balances are unchanged";

    /// <summary>True for an export line that pays out flex above the cap (<c>FLEX_PAYOUT</c>).</summary>
    private static bool IsFlexPayout(PayrollExportLine line) =>
        string.Equals(line.SourceTimeType, FlexLedgerOpening.FlexPayoutTimeType, StringComparison.Ordinal);

    /// <summary>
    /// S145 / TASK-14512 — Q7 (b), owner ruling; R37. The pay period of the month AFTER the corrected one,
    /// when this correction can have changed a week that month already paid; otherwise <c>null</c>.
    ///
    /// <para>
    /// <b>Why (plain language).</b> A week that crosses a month boundary is settled — and its overtime paid
    /// — by the month holding its Sunday (owner ruling Q3 = F1). So when September does not end on a
    /// Sunday, its last week (Monday 28 September – Sunday 4 October in 2026) was paid with October.
    /// Correcting September corrects September only (ADR-034 D5), so October's figure for that shared week
    /// may now be stale. The system cannot correct October by itself; it tells the admin to recalculate it
    /// too (the HR follow-up HRP-003 points there).
    /// </para>
    ///
    /// <para>
    /// <b>The rule (R37).</b> Name M+1 if and only if M's last day is not a Sunday AND a
    /// <c>payroll_export_records</c> row exists for M+1 (there is nothing to go stale in a month never
    /// exported). M is the calendar month of the period start, as for the baseline. For a leaver whose last
    /// week was settled inside M, naming M+1 over-names: it is advisory and harmless, because nothing is
    /// written to M+1 — the response, the event and the audit row only point at it.
    /// </para>
    ///
    /// <para>
    /// <b>S146 / TASK-14605 (C4, R-10) — the closing test, in addition.</b> M+1 opened at M's recorded closing
    /// balance (R-14 case 4a). When this correction changed that closing (<paramref name="closingChanged"/>,
    /// any difference including a first value where there was none), M+1's opening is stale too, so M+1 is
    /// named whenever it is exported, even when M ends on a Sunday (May 2026 ends on Sunday 31 May). The Sunday
    /// test is the F1 trigger the overtime/flex model program removes; the closing test is the one that stays.
    /// ADR-013 holds: nothing recalculates M+1 automatically.
    /// </para>
    /// </summary>
    private static async Task<(DateOnly Start, DateOnly End)?> NextPeriodToRecalculateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int year, int month, bool closingChanged, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // The F1 trigger (owner ruling Q3 = F1; the model program removes it): the closing week ends inside M (M's
        // last day is a Sunday), so M settled it and M+1 paid none of it. Unless M's closing balance changed (C4):
        // then M+1's opening is stale whatever weekday M ends on.
        if (WeeklyContext.WeekEnd(monthEnd) == monthEnd && !closingChanged)
            return null;

        var nextStart = monthStart.AddMonths(1);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM payroll_export_records
                WHERE employee_id = @employeeId AND year = @year AND month = @month)
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", nextStart.Year);
        cmd.Parameters.AddWithValue("month", nextStart.Month);
        var nextExported = await cmd.ExecuteScalarAsync(ct) is true;

        return nextExported ? (nextStart, nextStart.AddMonths(1).AddDays(-1)) : null;
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

    /// <summary>
    /// S145 / TASK-14512 — Q7 (b), R37: the first day of the NEXT month's pay period when this correction
    /// can have changed a week that month already paid (the corrected month does not end on a Sunday, and
    /// the next month has a payroll export record) — the admin should recalculate that month too.
    /// <c>null</c> otherwise. A pay-period date, never an employment date. Serialized as
    /// <c>nextPeriodToRecalculateStart</c>.
    /// </summary>
    public DateOnly? NextPeriodToRecalculateStart { get; init; }

    /// <summary>
    /// S145 / TASK-14512 — Q7 (b), R37: the last day of the next month's pay period; set together with
    /// <see cref="NextPeriodToRecalculateStart"/>. Serialized as <c>nextPeriodToRecalculateEnd</c>.
    /// </summary>
    public DateOnly? NextPeriodToRecalculateEnd { get; init; }

    /// <summary>
    /// S146 / TASK-14605 — the opening flex balance the recalculation used (derived from the ledger). <c>null</c>
    /// when flex is not tracked (<c>UNTRACKED</c>) or the month holds no EMPLOYED day. Serialized as
    /// <c>openingFlexBalanceUsed</c>.
    /// </summary>
    public decimal? OpeningFlexBalanceUsed { get; init; }

    /// <summary>S146 / TASK-14605 — the month's recorded closing balance before this correction (<c>null</c> on a raw record).</summary>
    public decimal? ClosingFlexBalanceBefore { get; init; }

    /// <summary>
    /// S146 / TASK-14605 — the month's closing balance after this correction (the engine's clamped closing);
    /// <c>null</c> under <c>UNTRACKED</c>; equal to <see cref="ClosingFlexBalanceBefore"/> for a month with no
    /// EMPLOYED day (left unchanged).
    /// </summary>
    public decimal? ClosingFlexBalanceAfter { get; init; }

    /// <summary>
    /// S146 / TASK-14605 — where the opening came from: <c>LEDGER_START</c>, <c>CHAINED</c>, <c>REHIRE</c> or
    /// <c>UNTRACKED</c>; <c>null</c> for a month with no EMPLOYED day.
    /// </summary>
    public string? FlexProvenance { get; init; }

    /// <summary>
    /// S146 / TASK-14605 — a plain statement when the month's flex was not tracked
    /// ("flex not tracked: the month precedes the ledger start S", R-14 case 3b) or not settled (no EMPLOYED day);
    /// <c>null</c> otherwise.
    /// </summary>
    public string? FlexNote { get; init; }
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
