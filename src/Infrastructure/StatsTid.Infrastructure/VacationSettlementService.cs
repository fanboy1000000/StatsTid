using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6804 (ADR-033 D3/D5/D6) — the atomic vacation-settlement pass. Exposes
/// <see cref="SettleAsync"/>, which the TASK-6805 <c>SettlementCloseService</c> BackgroundService
/// invokes once per due <c>(employee, VACATION, entitlement_year)</c> tuple inside a caller-owned
/// transaction.
///
/// <para>
/// <b>The pass (ALL in ONE caller-owned tx under the advisory lock; ADR-018 D3):</b>
/// <list type="number">
///   <item>acquire <c>pg_advisory_xact_lock(hashtext('employee-' || employeeId))</c> FIRST — the
///     SAME key as the ADR-032 D4 consumption lock, so a racing Skema-save / profile revaluation
///     writing <c>used</c> on the closing year's balance cannot interleave with this close writing
///     next-year <c>carryover_in</c>;</item>
///   <item>in-lock idempotency re-check — if an ACTIVE (non-REVERSED) settlement row already exists
///     for this tuple, no-op (a concurrent poller won the race; ADR-033 single-settle);</item>
///   <item>capture the IMMUTABLE snapshot (ADR-033 D3) — recorded per-absence feriedage, the
///     closed-year balance, the dated config, the §21 transfer-agreement days, impediment=false;</item>
///   <item>PARTITION the disposition — a PURE function of the snapshot (the legal core; matches the
///     S66 D9 <c>expiring</c> mapping);</item>
///   <item>WRITE the settlement row + audit, the §21 <c>carryover_in</c> on next year's balance,
///     emit the events on <c>employee-{id}</c> via the outbox, and the <c>audit_projection</c> row
///     sync-in-tx via <see cref="IAuditProjectionMapperRegistry"/> + <see cref="AuditProjectionRepository"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Quantity-determinism (ADR-033 D3, priority 2).</b> Every settled quantity is computed from
/// the captured snapshot — never re-derived after capture. The partition is a static pure function
/// (<see cref="Partition"/>); replay reads the recorded snapshot verbatim and reproduces the buckets
/// byte-identically.
/// </para>
///
/// <para>
/// <b>No <c>used</c> mutation / no balance "zeroing" (ADR-032 D2 + ADR-033 D6 clarification).</b>
/// Only §21 writes <c>carryover_in</c> (next year). The §24/§34 disposition lives on the settlement
/// row; <c>used</c> stays pinned to recorded absences and the closing ferieår's monthly <c>saldo</c>
/// is NOT retroactively zeroed (the balance readers special-case a settled year — TASK-6807).
/// </para>
/// </summary>
public sealed class VacationSettlementService
{
    private readonly EntitlementBalanceRepository _balanceRepo;
    private readonly EntitlementConfigRepository _configRepo;
    private readonly UserRepository _userRepo;
    private readonly UserAgreementCodeRepository _agreementCodeRepo;
    private readonly VacationTransferAgreementRepository _transferRepo;
    private readonly VacationSettlementRepository _settlementRepo;
    private readonly IEmploymentProfileResolver _profileResolver;
    private readonly IOutboxEnqueue _outbox;
    private readonly IAuditProjectionMapperRegistry _auditRegistry;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VacationSettlementService> _logger;

    private const string MonthlyAccrualModel = "MONTHLY_ACCRUAL";

    /// <summary>
    /// S80 / TASK-8002 (ADR-033 Slice 2) — the SPECIAL_HOLIDAY (særlige feriedage) entitlement type.
    /// Mirrors <see cref="EntitlementPeriodResolver.SpecialHolidayType"/>; routed to the DEDICATED
    /// §15 stk.2/§17 godtgørelse-only settle path (R4) — NEVER the shared §21/§24/§34 VACATION
    /// partition (whose <c>overCap = disposable − CarryoverMax</c> with CarryoverMax=0 would flag the
    /// whole balance as a §34 candidate → the exact compliance bug R4 forbids).
    /// </summary>
    private const string SpecialHolidayType = EntitlementPeriodResolver.SpecialHolidayType;

    /// <summary>YEAR_END trigger (ADR-033 D5) — the poll-driven boundary close.</summary>
    private const string YearEndTrigger = "YEAR_END";

    /// <summary>TERMINATION trigger (ADR-033 D5/D9; SPRINT-70 R5) — the crystallized leaver settlement.</summary>
    private const string TerminationTrigger = "TERMINATION";

    private const string StateSettled = "SETTLED";
    private const string StatePendingReview = "PENDING_REVIEW";

    /// <summary>The SPRINT-70 R5 / owner D-B crystallization-basis marker recorded on TERMINATION snapshots.</summary>
    private const string CrystallizationBasisS26WholeMonth = "S26_WHOLE_MONTH";

    /// <summary>Snapshot JSON options — settle-time inputs persisted verbatim (camelCase, enum-as-string off).</summary>
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public VacationSettlementService(
        EntitlementBalanceRepository balanceRepo,
        EntitlementConfigRepository configRepo,
        UserRepository userRepo,
        UserAgreementCodeRepository agreementCodeRepo,
        VacationTransferAgreementRepository transferRepo,
        VacationSettlementRepository settlementRepo,
        IEmploymentProfileResolver profileResolver,
        IOutboxEnqueue outbox,
        IAuditProjectionMapperRegistry auditRegistry,
        AuditProjectionRepository auditRepo,
        TimeProvider timeProvider,
        ILogger<VacationSettlementService> logger)
    {
        _balanceRepo = balanceRepo;
        _configRepo = configRepo;
        _userRepo = userRepo;
        _agreementCodeRepo = agreementCodeRepo;
        _transferRepo = transferRepo;
        _settlementRepo = settlementRepo;
        _profileResolver = profileResolver;
        _outbox = outbox;
        _auditRegistry = auditRegistry;
        _auditRepo = auditRepo;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Settle one closed <c>(employee, entitlementType, entitlementYear)</c> in the caller-owned
    /// transaction. Idempotent: a no-op if an active settlement already exists (the in-lock
    /// re-check, plus the partial-unique-index 23505 backstop). The caller commits or rolls back —
    /// this method does NOT (so the read-your-write / forced-rollback all-or-nothing contract holds
    /// across the row + carryover + events + audit row).
    /// </summary>
    /// <param name="employeeId">The employee being settled.</param>
    /// <param name="entitlementType">The entitlement type (VACATION in slice 1).</param>
    /// <param name="entitlementYear">The entitlement year being closed (its ferieår-end is the boundary).</param>
    /// <param name="trigger">YEAR_END (poll) or TERMINATION.</param>
    /// <param name="conn">The caller's open connection (the advisory lock is taken on THIS connection).</param>
    /// <param name="tx">The caller's active transaction (all writes participate in it; ADR-018 D3).</param>
    /// <param name="leaverGoLiveFloor">S70 Step-7a B1 (SPRINT-70 R2/D13) — the leaver-level
    /// settlement go-live floor (<c>Settlement:GoLiveDate</c>), re-checked IN-LOCK on the re-read
    /// user: a leaver whose <c>employment_end_date</c> is ≤ this floor is a pre-launch boundary the
    /// system never tracked (manual fallback per D13) and yields the benign
    /// <see cref="SettlementOutcome.NotDueUnderLock"/> no-op. Null = the caller supplied no floor
    /// (direct/test drives); <b>the close service ALWAYS supplies it</b> for Step-B calls.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settlement outcome (its settled row, <see cref="SettlementOutcome.AlreadySettled"/>,
    /// or the S70 Step-7a benign <see cref="SettlementOutcome.NotDueUnderLock"/> no-op).</returns>
    public async Task<SettlementOutcome> SettleAsync(
        string employeeId,
        string entitlementType,
        int entitlementYear,
        string trigger,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        DateOnly? leaverGoLiveFloor = null,
        CancellationToken ct = default)
    {
        // (0) S70 / TASK-7004 (SPRINT-70 R5; ADR-033 slice 3a) — the pass now accepts YEAR_END
        // (the S68 auto-partition / the R4 leaver deferred-disposition row) and TERMINATION (the
        // crystallized termination settlement record). Any OTHER trigger still fails loudly.
        if (!string.Equals(trigger, YearEndTrigger, StringComparison.Ordinal)
            && !string.Equals(trigger, TerminationTrigger, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Vacation settlement trigger '{trigger}' is not supported (YEAR_END / TERMINATION).");
        }

        // (1) Advisory lock FIRST, on the caller's tx connection, held to commit (ADR-032 D4). Same
        // key as EmployeeConsumptionLock (hashtext('employee-' || id)) — serializes vs a racing
        // Skema-save / profile revaluation that writes `used` on the closing year's balance row.
        // (The helper lives in Backend.Api, out of this assembly's scope; the lock VALUE is what
        // matters for mutual exclusion, so the identical SQL is inlined here — DECLARED in the report.)
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync(ct);
        }

        // (2) In-lock idempotency re-check — if a concurrent poller already produced an active
        // (non-REVERSED) settlement for this tuple, no-op (ADR-033 single-settle).
        var existing = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
        if (existing is not null)
        {
            // S70 R7b (SPRINT-70) — a TERMINATION whose target ferieår already holds an active row
            // of a DIFFERENT trigger (the YEAR_END close won) is a genuine conflict the 3a reversal
            // gap cannot resolve (reverse-then-re-settle is 3b): REFUSE with a durable loud signal
            // (SettlementManualReviewFlagged + ADR-026 audit + log), write NO vacation_settlements
            // row (the partial-unique single-active index forbids a second active row; the YEAR_END
            // row is left untouched), and return WITHOUT throwing — the close service treats it as
            // handled. Flagged-ONCE idempotency is the R3 any-trigger due-enumeration anti-join
            // (TASK-7005); THIS in-tx guard is the race backstop only. A duplicate TERMINATION
            // (existing.Trigger == TERMINATION) stays the benign AlreadySettled no-op below — the
            // same single-settle race the YEAR_END path swallows.
            if (string.Equals(trigger, TerminationTrigger, StringComparison.Ordinal)
                && !string.Equals(existing.Trigger, TerminationTrigger, StringComparison.Ordinal))
            {
                return await RefuseTerminationConflictAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear, existing, ct);
            }

            _logger.LogInformation(
                "Vacation settlement no-op: active {State} settlement already exists for {EmployeeId}/{Type}/{Year} (in-lock re-check).",
                existing.SettlementState, employeeId, entitlementType, entitlementYear);
            return SettlementOutcome.AlreadySettled(existing);
        }

        // (2b) S71 / TASK-7104 (SPRINT-71 R3) — bare-reversal marker rejection, IN-LOCK. A tuple
        // whose latest reversed row carries `bare_reversal_not_due` is TERMINAL in 3b: the Step-B
        // enumeration's shared anti-join already excludes it, but THIS check is the in-lock twin
        // (the S70 B1 lesson — every enumeration predicate re-evaluated under the lock). Without
        // it, a direct/stale drive would re-settle the tuple at sequence 1 and collide with the
        // REVERSED row's composite PK (a raw 23505 every poll). Benign NotDue: no row, no event,
        // no throw — marker-clearing + the R1 g+1 revival are the REHIRE follow-up's obligation.
        if (await _settlementRepo.HasBareReversalMarkerAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct))
        {
            _logger.LogDebug(
                "Vacation settlement NotDue for {EmployeeId}/{Type}/{Year}: the tuple carries the " +
                "bare-reversal not-due marker (SPRINT-71 R3) — bare reversal is terminal in 3b.",
                employeeId, entitlementType, entitlementYear);
            return SettlementOutcome.NotDueUnderLock();
        }

        // (3) The in-lock employee re-read — UNCONDITIONALLY terminated-inclusive (SPRINT-70 R9d):
        // Step B always runs AFTER Step A's deactivation flip, and the R4 leaver other-ferieår rows
        // settle with trigger=YEAR_END on an inactive employee — an is_active-filtered read would
        // throw on EVERY post-flip settlement. NOT keyed on the trigger (the active-employee case is
        // a strict subset; the pass is system-internal — access control does not ride this filter).
        var user = await _userRepo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: employee {employeeId} not found.");

        // (3·SH) S80 / TASK-8002 — the SPECIAL_HOLIDAY §15 stk.2/§17 godtgørelse fork is handled BELOW,
        // AFTER the in-lock TERMINATION + leaver guards (S80 Step-5a BLOCKER-1: branching it here, before
        // those guards, would settle/pay a tuple that became a leaver/not-due AFTER enumeration but before
        // this lock — a TOCTOU. SPECIAL_HOLIDAY reaches its dedicated godtgørelse path only as an ACTIVE,
        // non-leaver YEAR_END tuple; the leaver fork fails it closed (R12 termination-interaction non-goal)).

        // (3a) S70 TERMINATION fork (SPRINT-70 R5/R6) — the crystallized termination settlement
        // record. Settles to the END DATE (whole-month §26 basis), never the ferieår end; writes
        // NO carryover and emits NO §21/§24 events.
        if (string.Equals(trigger, TerminationTrigger, StringComparison.Ordinal))
        {
            // S70 Step-7a BLOCKER B1 (Codex, fix-forward) — re-evaluate the FULL termination-due
            // predicate IN-LOCK on the re-read user BEFORE any write (the R12 contract). The
            // enumeration's due decision (SettlementCloseService.ResolveLeaverTupleTrigger) is
            // taken OUTSIDE this lock; an admin end-date correction can win the lock race and
            // commit state under which this tuple is no longer due — R1 REACTIVATES the user on a
            // correct-to-future date (a reactivated user is NOT a leaver), and a correct-to-passed
            // PRE-GO-LIVE date makes the boundary the D13 manual fallback. ANY clause failure ⇒ the
            // benign NotDue no-op: NO row, NO event, NO throw — the tuple is simply no longer due;
            // the next poll re-enumerates against fresh state (no refire loop) — and NEVER a
            // fall-through to any other settlement path. The predicate is the pure unit-pinned
            // IsTerminationDueUnderLock below.
            if (!IsTerminationDueUnderLock(
                    user.IsActive, user.EmploymentEndDate, entitlementYear, leaverGoLiveFloor, CopenhagenToday()))
            {
                _logger.LogDebug(
                    "Vacation settlement NotDue (TERMINATION) for {EmployeeId}/{Type}/{Year}: the in-lock " +
                    "due re-evaluation failed (isActive={IsActive}, endDate={EndDate}, goLiveFloor={Floor}) — " +
                    "no longer due under lock; corrected state wins (SPRINT-70 Step-7a B1).",
                    employeeId, entitlementType, entitlementYear,
                    user.IsActive, user.EmploymentEndDate, leaverGoLiveFloor);
                return SettlementOutcome.NotDueUnderLock();
            }

            return await SettleTerminationAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, user,
                sequence: 1, superseding: false, ct);
        }

        // (3b) S70 R4 leaver fork + leak-proofing pin R4(b) — the leaver/no-partition decision keys
        // on the IN-LOCK RE-READ employment_end_date vs the Copenhagen business date, NEVER on
        // caller-supplied state (a Step-A flip race can never yield a partitioned settlement for a
        // terminated employee). A leaver's OTHER due ferieår (trigger=YEAR_END on an employee whose
        // end date has passed) gets the fail-closed deferred-disposition PENDING_REVIEW row — NO
        // §21/§24 auto-partition arithmetic is invented for leavers, NO carryover write, NO
        // VacationAutoPaidOut into the S69 §24 staging.
        if (user.EmploymentEndDate is { } leaverEndDate && leaverEndDate < CopenhagenToday())
        {
            // S80 Step-5a BLOCKER-1 — a SPECIAL_HOLIDAY tuple whose employee became a leaver (end date
            // passed) under the lock is NOT settled: the SPECIAL_HOLIDAY×termination godtgørelse
            // interaction is an explicit 8002 NON-GOAL (R12). Fail-closed NotDue — NO godtgørelse, NO
            // VACATION-shaped leaver-deferred row, NO event (a future termination-interaction slice owns it).
            if (string.Equals(entitlementType, SpecialHolidayType, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Vacation settlement NotDue (SPECIAL_HOLIDAY leaver) for {EmployeeId}/{Year}: the " +
                    "SPECIAL_HOLIDAY×leaver godtgørelse interaction is a deferred non-goal (S80 R12).",
                    employeeId, entitlementYear);
                return SettlementOutcome.NotDueUnderLock();
            }

            // S70 Step-7a BLOCKER B1 (Codex, fix-forward) — the SAME R2/D13 go-live floor,
            // re-checked IN-LOCK on the re-read end date. A pre-go-live leaver is the manual
            // fallback (the system never tracked that boundary): it must NEITHER get the
            // deferred-disposition row NOR fall through to the ACTIVE §21/§24 auto-partition
            // below — the benign NotDue no-op is the ONLY exit (pinned no-fall-through).
            if (leaverGoLiveFloor is { } goLiveFloor && leaverEndDate <= goLiveFloor)
            {
                _logger.LogDebug(
                    "Vacation settlement NotDue (leaver-deferred) for {EmployeeId}/{Type}/{Year}: in-lock " +
                    "end date {EndDate} is on/before the go-live floor {Floor} — pre-go-live leaver is the " +
                    "D13 manual fallback; no deferred row, no auto-partition (SPRINT-70 Step-7a B1).",
                    employeeId, entitlementType, entitlementYear, leaverEndDate, goLiveFloor);
                return SettlementOutcome.NotDueUnderLock();
            }

            return await SettleLeaverDeferredDispositionAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, user, leaverEndDate, ct);
        }

        // (3·SH) S80 / TASK-8002 (ADR-033 Slice 2, R4 HARD) — the SPECIAL_HOLIDAY §15 stk.2/§17
        // godtgørelse. Reached ONLY for an ACTIVE (non-leaver), non-TERMINATION YEAR_END SPECIAL_HOLIDAY
        // tuple (the TERMINATION + leaver forks above fail it closed). A DEDICATED godtgørelse-only
        // partition + settle that NEVER threads through the shared VACATION Partition()/
        // SettleActiveYearEndAsync — its overCap = disposable − CarryoverMax (CarryoverMax=0 for
        // SPECIAL_HOLIDAY) would flag the whole balance as a §34 candidate → PENDING_REVIEW, the exact
        // compliance bug R4 forbids. NO §21/§24/§34/§22, NO over-cap, NO PENDING_REVIEW: the unused
        // remainder → godtgørelse payout_days; the row settles SETTLED. (The SPECIAL_HOLIDAY auto-close
        // is itself R5-gated DORMANT pre-§15-stk.1 in the close service.)
        if (string.Equals(entitlementType, SpecialHolidayType, StringComparison.Ordinal))
        {
            return await SettleSpecialHolidayGodtgoerelseAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, user, ct);
        }

        // ACTIVE-employee YEAR_END auto-partition — the S68 pass, extracted VERBATIM into
        // SettleActiveYearEndAsync for S71 supersession reuse (TASK-7104). sequence 1 +
        // superseding:false keep this path byte-identical to the pre-S71 behavior.
        return await SettleActiveYearEndAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, user,
            sequence: 1, superseding: false, ct);
    }

    /// <summary>
    /// The ACTIVE-employee YEAR_END auto-partition (the S68 pass, ADR-033 D5/D6/D10 — moved
    /// method-bodily out of <see cref="SettleAsync"/> by S71 / TASK-7104 so
    /// <see cref="ResettleSupersedingAsync"/> can reuse it at the R1 next-generation sequence).
    /// <paramref name="sequence"/> is 1 on the normal first-settlement path;
    /// <paramref name="superseding"/> hardens the supersession context: a 23505 single-settle
    /// collision is RETHROWN (the reversal tx holds the advisory lock and just REVERSED the only
    /// active row — a collision there is a defect, and the whole reversal must roll back), and
    /// the SPRINT-71 R4 supersede-side fail-closed guard refuses a §21 <c>carryover_in</c> write
    /// into a year that itself holds an active settlement row
    /// (<see cref="SupersedingCarryoverConflictException"/> ⇒ FULL rollback by the caller).
    /// </summary>
    private async Task<SettlementOutcome> SettleActiveYearEndAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user,
        int sequence, bool superseding, CancellationToken ct)
    {
        // Capture the immutable snapshot (ADR-033 D3). All inputs are pinned as-of the boundary;
        // quantities below are a pure function of this object (no live re-derivation after capture).
        var (snapshot, snapshotJson) = await CaptureSnapshotAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, user,
            terminationCutoff: null, terminationDate: null, deferredDisposition: false, ct);

        // (4) Partition the disposition — pure fn of the snapshot (the legal core; matches S66 D9).
        var partition = Partition(snapshot);

        // (5) Decide state. A §34 forfeiture-candidate (over_cap > 0) must NOT be auto-forfeited in
        // 1a (ADR-033 D10) — flag for human disposition. The §21/§24 buckets ARE executed in BOTH
        // states. (IsFeriehindret is false in slice 1; impediment modeling lands in slice 4.)
        var hasForfeitCandidate = partition.ForfeitDays > 0m;
        var settlementState = hasForfeitCandidate ? "PENDING_REVIEW" : "SETTLED";

        // (6) Atomic writes — settlement row + audit, §21 carryover, events (outbox), audit_projection.
        // sequence: 1 on the first settlement; the S71 supersession path allocates the R1
        // next-generation sequence (2g−1) and passes it in.
        var row = new VacationSettlementRow
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            SettlementState = settlementState,
            Trigger = YearEndTrigger,
            SnapshotJson = snapshotJson,
            TransferDays = partition.TransferDays,
            PayoutDays = partition.PayoutDays,
            ForfeitDays = partition.ForfeitDays,
            Version = 1,
        };

        var actorId = $"system:settlement-close:{YearEndTrigger}";
        const string actorRole = "System";

        VacationSettlementRow persisted;
        try
        {
            persisted = await _settlementRepo.InsertAsync(conn, tx, row, snapshotJson, actorId, actorRole, ct);
        }
        catch (DuplicateActiveSettlementException)
        {
            // S71 / TASK-7104 — in the SUPERSEDING context this cannot be a benign race: the
            // reversal tx holds the advisory lock and just REVERSED the tuple's only active row,
            // so an active-index collision means a writer bypassed the R12 lock. Rethrow — the
            // reversal service rolls back the WHOLE tx (R4 full-rollback-on-failed-leg).
            if (superseding) throw;

            // The single-settle backstop fired between the in-lock re-check and the INSERT (a
            // concurrent poller committed first). Swallow benignly — exactly one settlement stands.
            _logger.LogInformation(
                "Vacation settlement no-op: 23505 single-settle backstop for {EmployeeId}/{Type}/{Year}.",
                employeeId, entitlementType, entitlementYear);
            var winner = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
            return winner is not null
                ? SettlementOutcome.AlreadySettled(winner)
                : SettlementOutcome.AlreadySettled(row);
        }

        // §21 carryover (ADR-033 D5/D6) — DERIVED from transfer_days; idempotent (overwrite, not add).
        // In slice 1 the next-year carryover total IS the §21 term (the §22 term is 0). seedQuota is
        // next year's annual entitlement (the snapshot's dated annual_quota — VACATION quota is the
        // same across the boundary; the next-year row, if it materializes here, seeds at that quota).
        //
        // WARNING 1 (Codex Step-5a) — SKIP the carryover write when transfer is zero. WriteCarryoverInAsync
        // OVERWRITES next-year carryover_in (SET, not +=); calling it with 0 would CLOBBER any
        // carryover that an independent producer (a future §22 path, a manual seed) may already have
        // written to that next-year row. The §21 term is the ONLY carryover producer in slice 1, and
        // when there is no §21 transfer there is nothing for this pass to contribute — so leave the
        // next-year row untouched. (The >0 case keeps its derived-overwrite shape — re-run idempotent.)
        if (partition.TransferDays > 0m)
        {
            // S71 / TASK-7104 (SPRINT-71 R4) — the supersede-side fail-closed guard: a
            // SUPERSEDING settlement must never write `carryover_in` into a year that itself
            // holds an active settlement row (that year's recorded disposition is final; a
            // retroactive carryover mutation would corrupt it). Throw ⇒ the reversal service
            // rolls back the WHOLE reversal tx (no partial states — the original row stands).
            if (superseding)
            {
                var nextYearActive = await _settlementRepo.GetActiveAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear + 1, ct);
                if (nextYearActive is not null)
                {
                    throw new SupersedingCarryoverConflictException(
                        employeeId, entitlementType, entitlementYear + 1,
                        nextYearActive.SettlementState, nextYearActive.Trigger);
                }
            }

            await _balanceRepo.WriteCarryoverInAsync(
                conn, tx, employeeId, entitlementType, entitlementYear + 1,
                carryoverDays: partition.TransferDays, seedQuota: snapshot.AnnualQuota, ct);
        }

        // Events on employee-{id} (ADR-018 D6) via the outbox + the ADR-026 audit_projection row
        // sync-in-tx per emitted event (ADR-026 D13; the BackgroundService dispatch site — a NEW
        // dispatch pattern that reuses the registry+repo seam, not an endpoint).
        var streamId = $"employee-{employeeId}";
        var auditTargetOrgId = user.PrimaryOrgId; // employee → primary_org_id (TENANT_TARGETED; ADR-033 D-mapping)

        if (partition.TransferDays > 0m)
        {
            var carryoverEvent = new VacationCarryoverExecuted
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                TransferDays = partition.TransferDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, carryoverEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);
        }

        if (partition.PayoutDays > 0m)
        {
            var payoutEvent = new VacationAutoPaidOut
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                PayoutDays = partition.PayoutDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, payoutEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);
        }

        if (hasForfeitCandidate)
        {
            var flaggedEvent = new SettlementManualReviewFlagged
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                FlaggedDays = partition.ForfeitDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, flaggedEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);
        }

        _logger.LogInformation(
            "Vacation settlement {State} for {EmployeeId}/{Type}/{Year}: transfer={Transfer} payout={Payout} forfeit={Forfeit}.",
            settlementState, employeeId, entitlementType, entitlementYear,
            partition.TransferDays, partition.PayoutDays, partition.ForfeitDays);

        return SettlementOutcome.Settled(persisted, partition);
    }

    // ------------------------------------------------------------------
    // S70 / TASK-7004 — the TERMINATION crystallization pass (SPRINT-70 R5/R6/R10/R12).
    // Runs inside SettleAsync's advisory-locked caller tx (the same ADR-032 D4 lock + ADR-018 D3
    // atomicity contract); invoked ONLY from SettleAsync after the in-lock re-check + user re-read.
    // ------------------------------------------------------------------

    /// <summary>
    /// The crystallized TERMINATION settlement record (SPRINT-70 R5). Crystallizes the end-date
    /// ferieår at the employment end date — <c>crystallized = max(0, EarnedToDate(asOf=endDate) +
    /// carryoverIn − consumedToEndDate)</c>, whole-month (owner D-B), flat per ADR-031 — and writes
    /// the row per the PINNED state rule: pre-clamp ≥ 0 ⇒ <c>SETTLED</c> with ALL bucket columns
    /// zero (<c>CrystallizedDays</c> lives in the snapshot ONLY — the source 3b's §26 line reads);
    /// pre-clamp NEGATIVE ⇒ <c>PENDING_REVIEW</c> with <c>forfeit_days = |pre-clamp|</c> (the S68
    /// flag convention; the over-taken §7 modregning question is 3b — the row PARKS, and the manual
    /// resolve endpoint 422-rejects TERMINATION rows). Emits <see cref="TerminationSettled"/> for
    /// BOTH outcomes (emitted-no-consumer, R10) + <see cref="SettlementManualReviewFlagged"/> on
    /// the PENDING_REVIEW outcome (the S68 D10 signal convention) — outbox + row + ADR-026 audit
    /// projection in the ONE caller tx. NO carryover write, NO §21/§24 events, NO payroll line
    /// (money-free, ADR-033 D1; the payout is the manual fallback until 3b).
    /// </summary>
    private async Task<SettlementOutcome> SettleTerminationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user,
        int sequence, bool superseding, CancellationToken ct)
    {
        // R5/R4(b) — the crystallization boundary is the IN-LOCK re-read employment_end_date.
        var endDate = user.EmploymentEndDate
            ?? throw new InvalidOperationException(
                $"Vacation settlement: TERMINATION settlement requested for employee {employeeId} " +
                "with no employment_end_date — fail closed (the leaver model is the quantity source).");

        // R6 — the TERMINATION settles the ferieår CONTAINING the end date. A caller-supplied year
        // that disagrees is a caller defect — fail loud (deterministic; never settle the wrong year).
        var targetYear = ResolveTerminationFerieaar(endDate);
        if (targetYear != entitlementYear)
        {
            throw new InvalidOperationException(
                $"Vacation settlement: TERMINATION for employee {employeeId} targets ferieår {targetYear} " +
                $"(end date {endDate:yyyy-MM-dd}, R6), but entitlementYear {entitlementYear} was requested.");
        }

        // R5 — snapshot at the SAME strictly-dated ferieår-start anchors as YEAR_END (fail-closed,
        // no live fallback), valued at the END DATE: Earned asOf=endDate (whole-month, flat),
        // Used = recorded absences with date ≤ endDate (NOT balance.Used — a post-end-date booking
        // cannot be taken and must not consume; the declared slice-1a divergence).
        var (snapshot, _) = await CaptureSnapshotAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, user,
            terminationCutoff: endDate, terminationDate: endDate, deferredDisposition: false, ct);

        // The crystallization — a PURE function of the captured snapshot (ADR-033 D3); the result is
        // stamped INTO the snapshot (CrystallizedDays) so replay re-derives byte-identically from the
        // stored snapshot alone.
        var crystallization = CrystallizeTermination(snapshot);
        snapshot = snapshot with { CrystallizedDays = crystallization.CrystallizedDays };
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJson);

        // sequence: 1 on the first settlement; the S71 supersession path (ResettleSupersedingAsync)
        // allocates the R1 next-generation sequence (2g−1) and passes it in.
        var row = new VacationSettlementRow
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            SettlementState = crystallization.SettlementState,
            Trigger = TerminationTrigger,
            SnapshotJson = snapshotJson,
            TransferDays = 0m,                                    // R5 row shape: buckets zero;
            PayoutDays = 0m,                                      // CrystallizedDays is snapshot-only.
            ForfeitDays = crystallization.ForfeitFlagDays,        // |pre-clamp| FLAG iff negative (S68 convention).
            Version = 1,
        };

        var actorId = $"system:settlement-close:{TerminationTrigger}";
        const string actorRole = "System";

        VacationSettlementRow persisted;
        try
        {
            persisted = await _settlementRepo.InsertAsync(conn, tx, row, snapshotJson, actorId, actorRole, ct);
        }
        catch (DuplicateActiveSettlementException)
        {
            // S71 / TASK-7104 — in the SUPERSEDING context this cannot be a benign race nor an
            // R7b conflict to flag: the reversal tx holds the advisory lock and just REVERSED
            // the tuple's only active row, so an active-index collision means a writer bypassed
            // the R12 lock. Rethrow — the reversal service rolls back the WHOLE reversal tx
            // (SPRINT-71 R4 full-rollback-on-failed-leg; the original row stands).
            if (superseding) throw;

            // BLOCKER B3 (Codex Step-5a, TASK-7004 fix-forward) — SPRINT-70 R7b in the 23505
            // RECOVERY branch. Tx mechanics (verified): InsertAsync SAVEPOINT-wraps its INSERT and
            // ROLLBACK-TO-SAVEPOINTs before surfacing this exception (VacationSettlementRepository
            // ~L102-176), so the caller tx is USABLE here — the winner re-read and the refusal
            // emissions below ride the SAME tx. Reachability note: a SAME-sequence competing
            // insert violates BOTH the composite PK and the partial-unique ACTIVE index — Postgres
            // guarantees no particular constraint-check order (Codex c2 NOTE), and safety needs
            // none: a PK 23505 is rethrown raw by InsertAsync's discriminating catch (fails loudly
            // to the close service's per-tuple isolation; the NEXT poll's in-lock pre-check
            // refuses it), while an ACTIVE-index 23505 surfaces here and examines the winner.
            // Either path is D10-safe.
            var winner = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
            if (winner is null)
            {
                // The 23505 proved an active row existed at insert time; a null re-read means it
                // vanished mid-race. NEVER report the UNPERSISTED candidate row as settled (a
                // fabricated outcome) — fail loud (D10).
                throw new InvalidOperationException(
                    $"Vacation settlement: 23505 single-active collision for {employeeId}/{entitlementType}/" +
                    $"{entitlementYear} (TERMINATION), but no active settlement row is visible on re-read — " +
                    "refusing to fabricate an AlreadySettled outcome from the unpersisted candidate row.");
            }
            if (string.Equals(winner.Trigger, TerminationTrigger, StringComparison.Ordinal))
            {
                // A concurrent TERMINATION won — the benign single-settle race (the same no-op the
                // YEAR_END path swallows); exactly one settlement stands.
                _logger.LogInformation(
                    "Vacation settlement no-op: 23505 single-settle backstop for {EmployeeId}/{Type}/{Year} (TERMINATION).",
                    employeeId, entitlementType, entitlementYear);
                return SettlementOutcome.AlreadySettled(winner);
            }
            // The concurrent winner is ANOTHER trigger (YEAR_END) — the EXACT race R7b's backstop
            // exists for. A benign swallow here would suppress the conflict signal FOREVER: the R3
            // any-trigger due-enumeration anti-join (TASK-7005) excludes this tuple from every
            // future TERMINATION pass, so no later poll would re-raise it (a silent conflict — a
            // D10 violation). Refuse LOUDLY exactly like the in-lock pre-check path.
            return await RefuseTerminationConflictAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, winner, ct);
        }

        // NO carryover write — a TERMINATION crystallizes the final balance; nothing transfers
        // (the no-carryover-writes invariant, SPRINT-70 R4).

        var streamId = $"employee-{employeeId}";
        var auditTargetOrgId = user.PrimaryOrgId;

        // R10 — TerminationSettled for BOTH outcomes (emitted-no-consumer in 3a; mapper + DI from
        // TASK-7001). The §26/§7 bucket day-counts stay 0 — they are 3b payroll-emission scope; the
        // crystallized quantity rides the snapshot (R5).
        var terminationEvent = new TerminationSettled
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            Snapshot = snapshot,
            PayoutDays = 0m,
            ModregningDays = 0m,
            UnearnedAdvanceDays = 0m,
            ActorId = actorId,
            ActorRole = actorRole,
        };
        await EmitAsync(conn, tx, streamId, terminationEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);

        if (string.Equals(crystallization.SettlementState, StatePendingReview, StringComparison.Ordinal))
        {
            // The S68 D10 signal convention — a PENDING_REVIEW close emits the flagged event with
            // the flagged remainder (here the over-taken |pre-clamp|, parked until 3b's §7/waiver).
            var flaggedEvent = new SettlementManualReviewFlagged
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                FlaggedDays = crystallization.ForfeitFlagDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, flaggedEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);
        }

        _logger.LogInformation(
            "Vacation settlement {State} (TERMINATION) for {EmployeeId}/{Type}/{Year}: end date {EndDate}, " +
            "crystallized={Crystallized} (pre-clamp {PreClamp}), forfeitFlag={ForfeitFlag}.",
            crystallization.SettlementState, employeeId, entitlementType, entitlementYear, endDate,
            crystallization.CrystallizedDays, crystallization.PreClamp, crystallization.ForfeitFlagDays);

        return SettlementOutcome.Settled(persisted, partition: null);
    }

    /// <summary>
    /// SPRINT-70 R4 — a leaver's OTHER due ferieår (trigger=YEAR_END, employment end date passed on
    /// the in-lock re-read): the fail-closed deferred-disposition row. NO §21/§24 auto-partition
    /// arithmetic is invented for leavers — the row is written PENDING_REVIEW with
    /// <c>forfeit_days = the FULL disposable</c> (the S68 flag convention — a FLAG, not a §34
    /// disposition), <c>transfer_days = payout_days = 0</c>, snapshot at the SAME strictly-dated
    /// ferieår-start anchors WITH the <c>DeferredDisposition</c> marker. NO carryover write, NO
    /// <see cref="VacationCarryoverExecuted"/>, NO <see cref="VacationAutoPaidOut"/> (nothing may
    /// leak into the S69 §24 staging). Resolved via the EXISTING CAS manual FORFEIT/DEFER workflow
    /// UNCHANGED (DEFER is marker-only in 3a — no resolve disposition writes carryover_in).
    /// </summary>
    private async Task<SettlementOutcome> SettleLeaverDeferredDispositionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user, DateOnly leaverEndDate,
        CancellationToken ct)
    {
        // Normal YEAR_END boundary valuation (the same operands the D9 reader renders), with the
        // DeferredDisposition marker + the leaver's end date documenting WHY no partition ran.
        var (snapshot, snapshotJson) = await CaptureSnapshotAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, user,
            terminationCutoff: null, terminationDate: leaverEndDate, deferredDisposition: true, ct);

        // The pure partition is computed ONLY for its Disposable figure (the full unpartitioned
        // remainder) — its §21/§24/§34 buckets are deliberately NOT applied (R4 no-partition).
        var disposable = Partition(snapshot).Disposable;

        const int sequence = 1;
        var row = new VacationSettlementRow
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            SettlementState = StatePendingReview,    // fail-closed (ADR-033 D10)
            Trigger = YearEndTrigger,
            SnapshotJson = snapshotJson,
            TransferDays = 0m,
            PayoutDays = 0m,
            ForfeitDays = disposable,                // the FULL disposable as a FLAG (S68 convention)
            Version = 1,
        };

        var actorId = $"system:settlement-close:{YearEndTrigger}";
        const string actorRole = "System";

        VacationSettlementRow persisted;
        try
        {
            persisted = await _settlementRepo.InsertAsync(conn, tx, row, snapshotJson, actorId, actorRole, ct);
        }
        catch (DuplicateActiveSettlementException)
        {
            _logger.LogInformation(
                "Vacation settlement no-op: 23505 single-settle backstop for {EmployeeId}/{Type}/{Year} (leaver deferred-disposition).",
                employeeId, entitlementType, entitlementYear);
            // B3 consistency (DECLARED, TASK-7004 fix-forward): the winner re-read must never
            // fabricate an outcome from the UNPERSISTED candidate row. No trigger discrimination
            // here — R7b is TERMINATION-specific; ANY active winner for the tuple is the benign
            // single-settle no-op for this YEAR_END-trigger pass. The savepoint in InsertAsync
            // keeps this tx usable (same mechanics as the TERMINATION catch).
            var winner = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
            return winner is not null
                ? SettlementOutcome.AlreadySettled(winner)
                : throw new InvalidOperationException(
                    $"Vacation settlement: 23505 single-active collision for {employeeId}/{entitlementType}/" +
                    $"{entitlementYear} (leaver deferred-disposition), but no active settlement row is visible " +
                    "on re-read — refusing to fabricate an AlreadySettled outcome from the unpersisted candidate row.");
        }

        // NO carryover write; NO VacationCarryoverExecuted / VacationAutoPaidOut emissions (R4).
        var streamId = $"employee-{employeeId}";
        var flaggedEvent = new SettlementManualReviewFlagged
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            Snapshot = snapshot,
            FlaggedDays = disposable,
            ActorId = actorId,
            ActorRole = actorRole,
        };
        await EmitAsync(conn, tx, streamId, flaggedEvent, actorId, user.PrimaryOrgId, user.PrimaryOrgId, ct);

        _logger.LogInformation(
            "Vacation settlement PENDING_REVIEW (leaver deferred-disposition) for {EmployeeId}/{Type}/{Year}: " +
            "end date {EndDate} passed — full disposable {Disposable} flagged, NO auto-partition (SPRINT-70 R4).",
            employeeId, entitlementType, entitlementYear, leaverEndDate, disposable);

        return SettlementOutcome.Settled(persisted, partition: null);
    }

    // ------------------------------------------------------------------
    // S80 / TASK-8002 (ADR-033 Slice 2, R4/R8) — the SPECIAL_HOLIDAY (særlige feriedage)
    // §15 stk.2/§17 godtgørelse close. Runs inside SettleAsync's advisory-locked caller tx (the
    // SAME ADR-032 D4 lock + ADR-018 D3 atomicity contract); invoked ONLY from SettleAsync's
    // (3·SH) fork after the in-lock re-check + the user re-read. DEDICATED — it never threads
    // through the shared §21/§24/§34 VACATION Partition()/SettleActiveYearEndAsync (R4 HARD).
    // ------------------------------------------------------------------

    /// <summary>
    /// The §15 stk.2/§17 godtgørelse-only settle (SPRINT-80 R4). The unused remainder of the
    /// SPECIAL_HOLIDAY accrual year — <c>max(0, earned − used − planned)</c> (CarryoverIn is always
    /// 0; SPECIAL_HOLIDAY has NO §15 stk.1 carryover modeled yet) — settles ENTIRELY to the
    /// godtgørelse <c>payout_days</c> bucket. The row settles <c>SETTLED</c> with
    /// <c>payout_days = remainder</c> and ALL other buckets (transfer/forfeit) 0:
    /// NO §21/§24/§34/§22, NO over-cap, NO PENDING_REVIEW (the §34 PENDING_REVIEW path the VACATION
    /// <see cref="Partition"/> takes with CarryoverMax=0 is the exact compliance bug this method
    /// avoids — R4). Emits <see cref="SaerligeFeriedagePaidOut"/> (PayoutDays = the remainder) +
    /// its ADR-026 audit projection in the ONE caller tx (R8). Money-free: a DAY-COUNT, SLS owns the
    /// 2½% (§17 ≠ §10's 2,02%; R11/R12). Replay-deterministic: the remainder is a pure function of
    /// the captured snapshot.
    /// </summary>
    private async Task<SettlementOutcome> SettleSpecialHolidayGodtgoerelseAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user, CancellationToken ct)
    {
        // The SPECIAL_HOLIDAY period geometry (R10 resolver): calendar accrual (1 Jan Y .. 31 Dec Y),
        // taking window 1 May (Y+1) .. 30 Apr (Y+2), godtgørelse boundary 30 Apr (Y+2). reset_month is
        // irrelevant to SPECIAL_HOLIDAY's geometry (fixed by law) — the resolver ignores it for the type.
        var period = EntitlementPeriodResolver.ResolveForYear(entitlementType, resetMonth: 1, entitlementYear);

        // The dated SPECIAL_HOLIDAY config in force at the accrual start (the strictly-dated agreement +
        // the OK version anchored at the accrual start), fail-closed. The quota basis comes from THIS
        // config; the godtgørelse is a day-count, not a value, so no §24 wage-mapping key is needed here
        // (the SLS_TBD_* line is 8003's concern — this method emits the event only).
        var accrualStart = period.AccrualStart;
        var okVersion = OkVersionResolver.ResolveVersion(accrualStart);
        var datedAgreement = await _agreementCodeRepo.GetByUserIdAtAsync(employeeId, accrualStart, ct)
            ?? user.AgreementCode;
        var datedConfig =
            await _configRepo.GetByTypeAtAsync(entitlementType, datedAgreement, okVersion, accrualStart, ct)
            ?? await _configRepo.GetCurrentOpenAsync(entitlementType, datedAgreement, okVersion, ct)
            ?? await _configRepo.GetCurrentOpenAsync(entitlementType, user.AgreementCode, user.OkVersion, ct)
            ?? throw new InvalidOperationException(
                $"SPECIAL_HOLIDAY settlement: no entitlement config resolvable for {entitlementType} under " +
                $"agreement '{datedAgreement}' (ok_version {okVersion}) at accrual start {accrualStart:yyyy-MM-dd} " +
                $"for employee {employeeId}; settlement capture fails closed (§15 godtgørelse needs the quota basis).");

        // Earned at the ACCRUAL END (31 Dec Y) — the resolver's AccrualEnd, NOT the later 30-Apr-(Y+2)
        // settlement boundary (the S80 / TASK-8001 BLOCKER-1 distinction: AccrualMath clamps elapsed
        // months from the accrual start = hire for a mid-year hire but does NOT cap the asOf at the
        // accrual-window end, so feeding the later boundary would over-count a mid-year hire). Flat
        // day-count (fraction 1.0m per ADR-031). MONTHLY_ACCRUAL accrues to the accrual end; an
        // IMMEDIATE config grants the full quota up-front.
        var earned = string.Equals(datedConfig.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
            ? AccrualMath.EarnedToDate(datedConfig.AnnualQuota, 1.0m, accrualStart, user.EmploymentStartDate, period.AccrualEnd)
            : datedConfig.AnnualQuota;

        // Used = the recorded SPECIAL_HOLIDAY feriedage within the TAKING window [1 May Y+1, 30 Apr Y+2]
        // (absence_type SPECIAL_HOLIDAY_ALLOWANCE — the consumption mapping; ReadRecordedFeriedageAsync
        // already maps the type). The taking window — NOT the accrual window — is where særlige
        // feriedage are consumed (Cirkulære 021-24 §12 stk.2). Planned = the balance row's planned (0
        // when no row). CarryoverIn is always 0 for SPECIAL_HOLIDAY (no §15 stk.1 modeled).
        var recordedAbsences = await ReadRecordedFeriedageAsync(
            conn, tx, employeeId, entitlementType, period.TakingStart, period.Boundary, ct);
        var used = recordedAbsences.Sum(a => a.Feriedage);

        var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
        var planned = balance?.Planned ?? 0m;

        var snapshot = new VacationSettlementSnapshot
        {
            RecordedAbsences = recordedAbsences,
            Earned = earned,
            Used = used,
            Planned = planned,
            CarryoverIn = 0m,                              // SPECIAL_HOLIDAY: no §15 stk.1 carryover modeled.
            AnnualQuota = datedConfig.AnnualQuota,
            CarryoverMax = datedConfig.CarryoverMax,       // 0 for SPECIAL_HOLIDAY — but NEVER read as a §34 cap here (R4).
            ResetMonth = datedConfig.ResetMonth,
            OkVersion = okVersion,
            AgreementCode = datedAgreement,
            Position = (await _profileResolver.GetByEmployeeIdAtAsync(employeeId, accrualStart, ct))?.Position,
            // The godtgørelse settlement boundary — 30 Apr (Y+2), the §12 stk.2 afholdelsesperiode end.
            SettlementBoundaryDate = period.Boundary,
            TransferAgreementDays = 0m,                    // no §21 for SPECIAL_HOLIDAY.
            IsFeriehindret = false,                        // no §22 for SPECIAL_HOLIDAY (R4).
        };

        // The godtgørelse remainder — a PURE function of the snapshot (ADR-033 D3 quantity-determinism):
        // the unused, untransferred balance, clamped at ≥ 0. ALL of it is the §15 stk.2/§17 godtgørelse.
        var partition = PartitionSpecialHoliday(snapshot);
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJson);

        var row = new VacationSettlementRow
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = 1,
            SettlementState = StateSettled,                // R4: ALWAYS SETTLED — never PENDING_REVIEW.
            Trigger = YearEndTrigger,                      // the boundary close.
            SnapshotJson = snapshotJson,
            TransferDays = 0m,                             // R4 godtgørelse-only row shape.
            PayoutDays = partition.PayoutDays,             // the §15 stk.2/§17 godtgørelse day-count.
            ForfeitDays = 0m,                              // R4: NO §34 — særlige feriedage are never forfeited.
            Version = 1,
        };

        var actorId = $"system:settlement-close:{YearEndTrigger}";
        const string actorRole = "System";

        VacationSettlementRow persisted;
        try
        {
            persisted = await _settlementRepo.InsertAsync(conn, tx, row, snapshotJson, actorId, actorRole, ct);
        }
        catch (DuplicateActiveSettlementException)
        {
            // The single-settle backstop fired between the in-lock re-check and the INSERT (a concurrent
            // poller committed first). Swallow benignly — exactly one settlement stands (mirrors the
            // YEAR_END path). The winner re-read must never fabricate an outcome from the unpersisted row.
            _logger.LogInformation(
                "SPECIAL_HOLIDAY settlement no-op: 23505 single-settle backstop for {EmployeeId}/{Type}/{Year}.",
                employeeId, entitlementType, entitlementYear);
            var winner = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
            return winner is not null
                ? SettlementOutcome.AlreadySettled(winner)
                : throw new InvalidOperationException(
                    $"SPECIAL_HOLIDAY settlement: 23505 single-active collision for {employeeId}/{entitlementType}/" +
                    $"{entitlementYear}, but no active settlement row is visible on re-read — refusing to " +
                    "fabricate an AlreadySettled outcome from the unpersisted candidate row.");
        }

        // NO carryover write (SPECIAL_HOLIDAY has no §15 stk.1 carryover; R4). Emit the godtgørelse
        // event (R8) — the §15 stk.2/§17 payout day-count off the IMMUTABLE snapshot — + its ADR-026
        // audit projection in the ONE caller tx.
        var streamId = $"employee-{employeeId}";
        var auditTargetOrgId = user.PrimaryOrgId;
        var godtgoerelseEvent = new SaerligeFeriedagePaidOut
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = 1,
            Snapshot = snapshot,
            PayoutDays = partition.PayoutDays,
            ActorId = actorId,
            ActorRole = actorRole,
        };
        await EmitAsync(conn, tx, streamId, godtgoerelseEvent, actorId, auditTargetOrgId, auditTargetOrgId, ct);

        _logger.LogInformation(
            "SPECIAL_HOLIDAY settlement SETTLED (§15 stk.2/§17 godtgørelse) for {EmployeeId}/{Type}/{Year}: " +
            "earned={Earned} used={Used} planned={Planned} → godtgørelse payout_days={Payout} " +
            "(boundary {Boundary}).",
            employeeId, entitlementType, entitlementYear, earned, used, planned,
            partition.PayoutDays, period.Boundary);

        return SettlementOutcome.Settled(persisted, partition);
    }

    /// <summary>
    /// SPRINT-80 R4 — the SPECIAL_HOLIDAY §15 stk.2/§17 godtgørelse partition. PURE function of the
    /// captured snapshot (ADR-033 D3 quantity-determinism; replay-stable). The unused remainder is the
    /// disposable balance — <c>max(0, Earned + CarryoverIn − Used − Planned)</c> (CarryoverIn is 0 for
    /// SPECIAL_HOLIDAY) — and ALL of it is the godtgørelse <c>PayoutDays</c>. There is NO over-cap, NO
    /// §34 forfeiture, NO §21 transfer: <see cref="SettlementPartition.TransferDays"/> and
    /// <see cref="SettlementPartition.ForfeitDays"/> are ALWAYS 0 (the deliberate divergence from the
    /// VACATION <see cref="Partition"/>, whose <c>overCap = disposable − CarryoverMax</c> with
    /// CarryoverMax=0 would forfeit the whole balance — the compliance bug R4 forbids). Day-count only
    /// (NUMERIC(6,2) precision, ToEven — the D9 convention); money-free (SLS owns the 2½%; R11).
    /// </summary>
    internal static SettlementPartition PartitionSpecialHoliday(VacationSettlementSnapshot s)
    {
        var remainder = Math.Max(0m, s.Earned + s.CarryoverIn - s.Used - s.Planned);
        var payout = Round2(remainder);
        return new SettlementPartition(
            Disposable: payout,
            UnderCap: payout,   // the whole disposable is "under cap" in the godtgørelse-only sense (no §34 ceiling).
            OverCap: 0m,        // NEVER an over-cap remainder (R4 — særlige feriedage are not §34-forfeited).
            TransferDays: 0m,   // no §21 transfer.
            PayoutDays: payout, // ALL of it → the §15 stk.2/§17 godtgørelse.
            ForfeitDays: 0m);   // no §34 forfeiture.
    }

    /// <summary>
    /// SPRINT-70 R7b — the TERMINATION-vs-active-YEAR_END conflict refusal (the in-tx race
    /// backstop behind the R3 any-trigger due-enumeration anti-join). Emits the durable loud
    /// signal (<see cref="SettlementManualReviewFlagged"/> on the conflicting row's identity, no
    /// snapshot — nothing was settled) + its ADR-026 audit row + a warning log, writes NO
    /// <c>vacation_settlements</c> row (the partial-unique single-active index forbids a second
    /// active row; the YEAR_END row is left untouched), and returns WITHOUT throwing. The
    /// documented 3a interim deviation from ADR-033's reverse-then-re-settle (R7c) — the reversal
    /// infrastructure is 3b.
    /// </summary>
    private async Task<SettlementOutcome> RefuseTerminationConflictAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        VacationSettlementRow conflicting, CancellationToken ct)
    {
        // Terminated-inclusive read (R9d) — the refusal fires precisely for leavers; the org lookup
        // for the ADR-026 audit context must not dead-end on the is_active filter.
        var user = await _userRepo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: employee {employeeId} not found.");

        var actorId = $"system:settlement-close:{TerminationTrigger}";
        const string actorRole = "System";

        var flaggedEvent = new SettlementManualReviewFlagged
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = conflicting.Sequence, // the CONFLICTING row's identity — no new row exists
            Snapshot = null,                 // nothing settled, nothing captured (the row keeps its own)
            FlaggedDays = 0m,                // a conflict signal, not a quantity disposition
            ActorId = actorId,
            ActorRole = actorRole,
        };
        await EmitAsync(conn, tx, $"employee-{employeeId}", flaggedEvent, actorId, user.PrimaryOrgId, user.PrimaryOrgId, ct);

        _logger.LogWarning(
            "Vacation settlement REFUSED (SPRINT-70 R7b): TERMINATION for {EmployeeId}/{Type}/{Year} conflicts " +
            "with an active {Trigger}/{State} settlement (sequence {Sequence}). SettlementManualReviewFlagged " +
            "emitted; NO row written; reverse-then-re-settle requires the 3b reversal infrastructure.",
            employeeId, entitlementType, entitlementYear,
            conflicting.Trigger, conflicting.SettlementState, conflicting.Sequence);

        return SettlementOutcome.Refused(conflicting);
    }

    /// <summary>
    /// SPRINT-70 R6 — the ferieår CONTAINING a termination end date, executable VERBATIM:
    /// <c>entitlementYear = endDate.Month >= 9 ? endDate.Year : endDate.Year - 1</c> (VACATION
    /// <c>reset_month</c> = 9, uniform by DB CHECK per S68 B1). 31 Jan / 31 Aug → the PRIOR
    /// calendar year's ferieår; 1 Sep → the SAME calendar year's ferieår.
    /// </summary>
    internal static int ResolveTerminationFerieaar(DateOnly endDate) =>
        endDate.Month >= 9 ? endDate.Year : endDate.Year - 1;

    /// <summary>
    /// S70 Step-7a BLOCKER B1 (Codex, fix-forward) — the IN-LOCK termination-due predicate,
    /// re-evaluated by <see cref="SettleAsync"/>'s TERMINATION fork against the re-read user
    /// AFTER the R12 advisory lock is held (the enumeration-time decision in
    /// <c>SettlementCloseService.ResolveLeaverTupleTrigger</c> is pre-lock and may be stale).
    /// PURE; unit-pinned per clause:
    /// <list type="number">
    ///   <item><description><paramref name="isActive"/> is FALSE — a reactivated user is NOT a
    ///   leaver (Step B only ever enumerates flipped leavers; the R1 correct-to-future
    ///   re-evaluation REACTIVATES);</description></item>
    ///   <item><description><paramref name="employmentEndDate"/> is non-null AND has PASSED on the
    ///   Copenhagen business date (<c>endDate &lt; copenhagenToday</c> — the end date is the LAST
    ///   day employed);</description></item>
    ///   <item><description>the D13 go-live floor: <paramref name="leaverGoLiveFloor"/> is null
    ///   (caller supplied no floor — the close service ALWAYS supplies it) OR the end date falls
    ///   STRICTLY AFTER it (a pre-go-live leaver is the manual fallback);</description></item>
    ///   <item><description>R6 — <paramref name="entitlementYear"/> IS the end-date ferieår
    ///   (<see cref="ResolveTerminationFerieaar"/>).</description></item>
    /// </list>
    /// ANY clause failure ⇒ the caller returns the benign
    /// <see cref="SettlementOutcome.NotDueUnderLock"/> no-op (no row, no event, no throw — the
    /// tuple is simply no longer due; the next poll re-enumerates against fresh state).
    /// </summary>
    public static bool IsTerminationDueUnderLock(
        bool isActive, DateOnly? employmentEndDate, int entitlementYear,
        DateOnly? leaverGoLiveFloor, DateOnly copenhagenToday)
    {
        if (isActive) return false;                                              // (1) reactivated ⇒ not a leaver
        if (employmentEndDate is not { } endDate) return false;                  // (2a) no leaver fact
        if (endDate >= copenhagenToday) return false;                            // (2b) not passed
        if (leaverGoLiveFloor is { } floor && endDate <= floor) return false;    // (3) D13 pre-go-live ⇒ manual fallback
        return ResolveTerminationFerieaar(endDate) == entitlementYear;           // (4) R6 tuple match
    }

    /// <summary>
    /// S71 / TASK-7104 (SPRINT-71 R4) — the supersede-as-YEAR_END in-lock eligibility predicate:
    /// the superseding settlement may run the ACTIVE §21/§24 auto-partition for the tuple year iff
    /// (1) the in-tx CORRECTED user is ACTIVE (the S70 R4 ACTIVE-branch leak-proofing: a
    /// manually-inactive user is never auto-partitioned), (2) the user is NOT a passed-end-date
    /// leaver (the second leak-proofing pin — a leaver must never traverse the §21/§24
    /// auto-partition; a future-dated end date is fine, mirroring the enumeration's ACTIVE
    /// branch), (3) the tuple's §21/§24 boundary (31 Dec of the ferieår-end year — the
    /// <c>SettlementCloseService.IsBoundaryPassed</c> geometry verbatim) falls STRICTLY AFTER the
    /// D13 go-live floor (null floor = clause waived, the direct/test-drive shape), and (4) that
    /// boundary has PASSED on the Copenhagen business date. PURE; unit-pinned.
    /// NOTE (declared): a passed-end-date leaver's OTHER-ferieår deferred-disposition row is NOT
    /// supersede-eligible in 3b — re-creating an identical fail-closed row is pointless and the
    /// pinned R4 eligibility list names only TERMINATION and the ACTIVE-branch YEAR_END; such a
    /// state yields NotDue ⇒ the reversal service full-rolls-back (bare reversal or the existing
    /// FORFEIT/DEFER resolve are the operator's channels there).
    /// </summary>
    public static bool IsYearEndSupersedeDueUnderLock(
        string entitlementType, bool isActive, DateOnly? employmentEndDate, int entitlementYear, int resetMonth,
        DateOnly? supersedeGoLiveFloor, DateOnly copenhagenToday)
    {
        if (!isActive) return false;                                             // (1) ACTIVE branch only
        if (employmentEndDate is { } endDate && endDate < copenhagenToday)
            return false;                                                        // (2) leak-proofing: passed-end-date leaver
        // S80 / TASK-8002 (8001 Step-5a WARNING) — the boundary now comes from the shared
        // EntitlementPeriodResolver, type-aware. VACATION reset_month 9 ⇒ 31 Dec E+1 (BEHAVIOR-IDENTICAL
        // to the prior inline `new DateOnly(entitlementYear, resetMonth, 1).AddYears(1).AddDays(-1)` →
        // 31-Dec-of-end-year derivation). The leftover inline `Month>=resetMonth`-shaped geometry would
        // mis-handle SPECIAL_HOLIDAY supersession (30 Apr Y+2, not 31 Dec) now that 8002 introduces
        // SPECIAL_HOLIDAY settlements — routing through the resolver type-guards it correctly.
        var boundary = EntitlementPeriodResolver
            .ResolveForYear(entitlementType, resetMonth, entitlementYear).Boundary;
        if (supersedeGoLiveFloor is { } floor && boundary <= floor)
            return false;                                                        // (3) D13 pre-go-live boundary ⇒ manual fallback
        return copenhagenToday > boundary;                                       // (4) boundary passed
    }

    /// <summary>
    /// S71 / TASK-7104 (SPRINT-71 R1/R4) — the SUPERSEDING settlement entry point, callable ONLY
    /// from <see cref="SettlementReversalService"/> inside the reversal tx (under the R12 advisory
    /// lock, AFTER the active row was CAS-reversed and any end-date correction applied). NOT
    /// top-level <see cref="SettleAsync"/>: its active-row no-op check would mis-handle the
    /// just-REVERSED tuple, its sequence-1 allocation collides with history, and its own due
    /// dispatch (incl. the leaver-deferred fork) is not the pinned supersession eligibility.
    ///
    /// <para>
    /// Re-reads the employee from the CURRENT in-tx state (the corrected facts win — R4) and
    /// dispatches TRIGGER-SPECIFICALLY: supersede-as-TERMINATION via ALL
    /// <see cref="IsTerminationDueUnderLock"/> clauses (incl. the caller-supplied D13 go-live
    /// floor), else supersede-as-YEAR_END via <see cref="IsYearEndSupersedeDueUnderLock"/>
    /// (boundary-passed + post-go-live + the S70 ACTIVE-branch leak-proofing predicates; the
    /// reset_month geometry resolves via the close-service live-config chain — unresolvable
    /// geometry ⇒ ineligible). NEITHER predicate holding ⇒ the benign
    /// <see cref="SettlementOutcome.NotDueUnderLock"/> — the caller treats the supersession leg
    /// as FAILED and rolls back the WHOLE reversal tx (no partial states). The reused internals
    /// run with <c>superseding: true</c>: 23505 rethrown raw, and the R4 supersede-side
    /// fail-closed carryover guard armed (<see cref="SupersedingCarryoverConflictException"/>).
    /// </para>
    /// </summary>
    /// <param name="conn">The reversal service's open connection (holds the R12 advisory lock).</param>
    /// <param name="tx">The reversal tx (the CAS-reverse + request-VOID + any end-date correction already ride it).</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="entitlementType">The entitlement type (the reversed row's).</param>
    /// <param name="entitlementYear">The reversed row's tuple year — the supersession re-settles the SAME tuple.</param>
    /// <param name="supersedingSequence">The R1 next-generation row sequence (2g−1), derived from the tuple's recorded history.</param>
    /// <param name="supersedeGoLiveFloor">The D13 go-live floor (caller-supplied; null = clause waived, the direct/test-drive shape).</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task<SettlementOutcome> ResettleSupersedingAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        int supersedingSequence, DateOnly? supersedeGoLiveFloor, CancellationToken ct)
    {
        // The CURRENT in-tx user state (R9d terminated-inclusive; sees the lifecycle write the
        // reversal tx just applied — the corrected facts decide eligibility, never stale input).
        var user = await _userRepo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement supersession: employee {employeeId} not found.");
        var today = CopenhagenToday();

        // S80 Step-5a BLOCKER-2 — SPECIAL_HOLIDAY supersession is OUT OF SCOPE for 8002. Routing it to
        // SettleActiveYearEndAsync (the YEAR_END supersede branch below) would hit the shared §34
        // Partition (CarryoverMax=0 → forfeit/PENDING_REVIEW), the R4-forbidden path; there is no
        // dedicated SPECIAL_HOLIDAY supersede-at-sequence path yet. Fail-closed: the reversal service
        // performs the R4 full rollback (the operator's alternative is the bare reversal verb). This is
        // unreachable today — the SPECIAL_HOLIDAY close is R5-gated DORMANT, so no SPECIAL_HOLIDAY
        // settlement exists to reverse+supersede — and is a recorded follow-up.
        if (string.Equals(entitlementType, SpecialHolidayType, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Vacation settlement supersession NotDue (SPECIAL_HOLIDAY) for {EmployeeId}/{Year}: " +
                "SPECIAL_HOLIDAY supersession is a deferred non-goal (S80 Step-5a BLOCKER-2); the " +
                "reversal service performs the R4 full rollback.",
                employeeId, entitlementYear);
            return SettlementOutcome.NotDueUnderLock();
        }

        // (a) supersede-as-TERMINATION — ALL clauses incl. the go-live floor (SPRINT-71 R4).
        if (IsTerminationDueUnderLock(
                user.IsActive, user.EmploymentEndDate, entitlementYear, supersedeGoLiveFloor, today))
        {
            return await SettleTerminationAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, user,
                supersedingSequence, superseding: true, ct);
        }

        // (b) supersede-as-YEAR_END — the ACTIVE auto-partition geometry. reset_month resolves on
        // the close-service live-config chain (users.agreement_code + ok_version); no resolvable
        // config ⇒ no geometry ⇒ ineligible (mirrors the enumeration's unconfigured-skip).
        var liveConfig = await _configRepo.GetCurrentOpenAsync(
            entitlementType, user.AgreementCode, user.OkVersion, ct);
        if (liveConfig is not null
            && IsYearEndSupersedeDueUnderLock(
                entitlementType, user.IsActive, user.EmploymentEndDate, entitlementYear,
                liveConfig.ResetMonth, supersedeGoLiveFloor, today))
        {
            return await SettleActiveYearEndAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, user,
                supersedingSequence, superseding: true, ct);
        }

        // Neither trigger-specific predicate holds against the CORRECTED in-tx state ⇒ the
        // supersession leg fails: the reversal service rolls back EVERYTHING (R4 — no reversal
        // either; the original row stands; the operator's alternative is the explicit bare verb).
        _logger.LogDebug(
            "Vacation settlement supersession NotDue for {EmployeeId}/{Type}/{Year}: neither the " +
            "TERMINATION nor the ACTIVE YEAR_END eligibility predicate holds against the corrected " +
            "in-tx state (isActive={IsActive}, endDate={EndDate}, floor={Floor}) — the reversal " +
            "service performs the R4 full rollback.",
            employeeId, entitlementType, entitlementYear,
            user.IsActive, user.EmploymentEndDate, supersedeGoLiveFloor);
        return SettlementOutcome.NotDueUnderLock();
    }

    /// <summary>
    /// SPRINT-70 R5 — the TERMINATION crystallization. PURE function of the captured snapshot
    /// (ADR-033 D3 quantity-determinism; replay-stable): <c>pre-clamp = Earned + CarryoverIn −
    /// Used</c> (Earned = whole-month EarnedToDate asOf the end date; Used = recorded absences
    /// ≤ the end date; carryover_in INCLUDED — a previously transferred balance must not vanish),
    /// rounded to the 2dp storage precision (ToEven, the D9 reader convention) BEFORE the sign
    /// decision so the recorded row/state are mutually derivable from stored 2dp quantities.
    /// PINNED state rule (the agent invents NO legal logic): pre-clamp ≥ 0 ⇒ SETTLED
    /// (<see cref="TerminationCrystallization.CrystallizedDays"/> = pre-clamp; all row buckets
    /// zero); pre-clamp NEGATIVE ⇒ PENDING_REVIEW with the |pre-clamp| forfeit-FLAG (S68
    /// convention) and CrystallizedDays = 0 — parked until 3b's §7/waiver channel.
    /// </summary>
    internal static TerminationCrystallization CrystallizeTermination(VacationSettlementSnapshot s)
    {
        var preClamp = Round2(s.Earned + s.CarryoverIn - s.Used);
        var negative = preClamp < 0m;
        return new TerminationCrystallization(
            PreClamp: preClamp,
            CrystallizedDays: Math.Max(0m, preClamp),
            SettlementState: negative ? StatePendingReview : StateSettled,
            ForfeitFlagDays: negative ? -preClamp : 0m);
    }

    // ------------------------------------------------------------------
    // Snapshot capture (ADR-033 D3). Reuses the EXACT D9 operands (BalanceEndpoints): the dated
    // config + earned-at-boundary via AccrualMath.EarnedToDate; the closed-year balance; the
    // recorded per-absence feriedage (ADR-032 D2). No re-valuation (ADR-033 D2).
    // ------------------------------------------------------------------

    /// <param name="conn">The caller's open connection.</param>
    /// <param name="tx">The caller's active transaction.</param>
    /// <param name="employeeId">The employee being settled.</param>
    /// <param name="entitlementType">The entitlement type.</param>
    /// <param name="entitlementYear">The entitlement year being settled.</param>
    /// <param name="user">The in-lock employee re-read (terminated-inclusive, R9d).</param>
    /// <param name="terminationCutoff">S70 / TASK-7004 (SPRINT-70 R5): when non-null, the
    /// TERMINATION valuation boundary — the employment end date. Earned crystallizes asOf THIS
    /// date (whole-month, flat) and <c>Used</c> becomes the SUM of recorded absences with date ≤
    /// it (NOT <c>balance.Used</c> — the declared divergence from the slice-1a scalar: a booking
    /// after the end date cannot be taken and must not consume). Null = the unchanged YEAR_END
    /// boundary valuation (byte-identical to the S68/S69 capture).</param>
    /// <param name="terminationDate">The employment end date stamped on the snapshot
    /// (TERMINATION crystallization AND the R4 leaver deferred-disposition rows); null on
    /// ordinary active-employee YEAR_END captures.</param>
    /// <param name="deferredDisposition">SPRINT-70 R4 — the no-partition marker on a leaver's
    /// other-ferieår fail-closed PENDING_REVIEW snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<(VacationSettlementSnapshot Snapshot, string Json)> CaptureSnapshotAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user,
        DateOnly? terminationCutoff, DateOnly? terminationDate, bool deferredDisposition,
        CancellationToken ct)
    {
        // BLOCKER 1 (Codex Step-5a) — the dated entitlement-config resolution MUST reproduce the
        // EXACT chain the S66 D9 `expiring` figure uses (BalanceEndpoints.ResolveDatedConfigAsync),
        // so the settlement's annual_quota + carryover_max match D9 BYTE-FOR-BYTE. The prior code
        // keyed liveConfig on user.AgreementCode (the user's CURRENT live code) and used a bare
        // `?? liveConfig` fallback — so a missing dated row could settle on the WRONG (today's)
        // quota/cap. Below is the verbatim D9 chain:
        //
        //   todayAgreementCode = GetByUserIdAt(today) ?? user.AgreementCode
        //   liveConfig         = GetCurrentOpen(type, todayAgreementCode, user.OkVersion)   ← keyed on TODAY's code
        //                        // D9: may be NULL → bootstrapped from a historical-agreement probe
        //                        //     (`altLive`); NEVER throws. Reproduced below (the resetMonth
        //                        //     bootstrap, steps (a)/(b)/(c)) so a configless TODAY agreement
        //                        //     still settles any CLOSED year D9 renders.
        //   closedConfig       = ResolveDatedConfig(type, closedFerieaarStart, closedOk, liveConfig):
        //       agreement = GetByUserIdAt(closedFerieaarStart) ?? user.AgreementCode
        //       dated     = GetByTypeAt(type, agreement, closedOk, closedFerieaarStart)   → if non-null, RETURN
        //       else if agreement == todayAgreementCode → liveConfig
        //       else → GetCurrentOpen(type, agreement, user.OkVersion) ?? liveConfig
        //
        // NOTE (Step-5a P1/P4): the §24 wage-mapping KEY captured into the snapshot below uses the SAME
        // dated agreement but WITHOUT the `?? user.AgreementCode` fallback — it fails closed on a null
        // dated read (a missing dated row must not key a replay-sensitive payout off today's live code).
        // The VALUATION config-resolution above is unaffected (it only runs when the dated read is
        // non-null, so the fallback was already unreachable on that path).
        //
        // DECLARED `today` substitution: this service has no TimeProvider (the D9 reader takes one;
        // the not-yet-built TASK-6805 BackgroundService does not pass a settlement clock to this
        // pass). `todayAgreementCode` is a FALLBACK-branch / liveConfig operand ONLY — the dated
        // PRIMARY path (the legal core that fixes the closed year's quota/cap) is reproduced exactly
        // via GetByUserIdAt(closedFerieaarStart) + GetByTypeAt(…closedFerieaarStart). We substitute
        // todayAgreementCode := GetCurrentAsync (the live `effective_to IS NULL` user_agreement_codes
        // code), which EQUALS D9's GetByUserIdAt(today) for any non-future-dated live agreement —
        // always true when settling a CLOSED past year (no future-dated agreement can be live-as-of a
        // boundary already in the past). DECLARED in the report.
        var todayAgreementCode = await _agreementCodeRepo.GetCurrentAsync(employeeId, ct)
            ?? user.AgreementCode;

        // reset_month bootstrap (Codex cycle-2 BLOCKER fix) — discover ResetMonth so the boundary
        // can be computed BEFORE the dated read, WITHOUT throwing on a configless *today* agreement.
        //
        // The prior code keyed liveConfig on TODAY's code and THREW when it was null. But the S66 D9
        // reader (BalanceEndpoints.year-overview) NEVER throws: when today's agreement is configless
        // it bootstraps ResetMonth (and the ResolveDatedConfigAsync fallback terminal) from a probe
        // under the historical agreement (BalanceEndpoints ~line 755-792 `altLive`). A CLOSED VACATION
        // year that D9 renders via that bootstrap MUST be settleable — so the throw was wrong (it
        // refused a year D9 displays). reset_month is IMMUTABLE per (entitlement_type, ok_version)
        // natural key (ADR-021 Q1), so its VALUE is identical regardless of which agreement's config
        // supplies it; we may therefore read it from ANY resolvable config for the type, preferring
        // today's agreement (exact D9 parity when it IS configured).
        //
        // (a) today's-agreement live config — the D9 happy path (keyed on TODAY's code).
        var liveConfig = await _configRepo.GetCurrentOpenAsync(entitlementType, todayAgreementCode, user.OkVersion, ct);

        // (b) today configless → bootstrap from a YEAR-START anchor probe, replicating the S66 D9
        // reader EXACTLY (BalanceEndpoints ~lines 774-791, the `altLive` resolution). D9 probes a
        // candidate ferieår-START anchor SET — NOT a mid-ferieår date — skipping any anchor whose
        // agreement equals today's (which is already known configless), and takes the FIRST anchor
        // that yields a live config under its in-force agreement. That config seeds BOTH ResetMonth
        // discovery AND the ResolveDatedConfigAsync fallback terminal below.
        //
        // CYCLE-3 FIX (the defect): the prior single Dec-1 probe did NOT skip today's agreement, so a
        // mid-closed-ferieår switch to a CONFIGLESS agreement resolved the LATER (configless) code at
        // Dec-1 → no config → throw, even though D9 probes the YEAR-START (where the CONFIGURED prior
        // agreement was in force) and still renders that closed year. Mirroring D9's year-start anchor
        // set + `continue`-skip + first-hit precedence fixes the divergence: the service now resolves
        // (does not throw) for EVERY closed VACATION year D9 can render, and throws only where D9 would
        // render an empty row (no config under any agreement at any anchor).
        //
        // ANCHOR SET (D9 parity, expressed against the SETTLED entitlementYear): D9 works from a
        // CALENDAR view-year Y and probes {Jan-1 Y, Sep-1 Y−1, Sep-1 Y} — the three ferieår-starts that
        // can intersect calendar year Y under the two seeded reset geometries (1 and 9). This service is
        // handed the exact entitlementYear E; the ONLY ferieår-start that can BE E (and whose agreement
        // the dated PRIMARY read below uses) is Jan-1 E (reset-1 geometry) or Sep-1 E (reset-9 geometry)
        // — a strict SUBSET of D9's anchors for the year being settled (Jan-1 E is D9's first anchor when
        // viewing Y=E; Sep-1 E is D9's third when viewing Y=E and its second when viewing Y=E+1). So this
        // probe can never resolve a config D9 wouldn't. PRECEDENCE matches D9: calendar geometry (Jan-1)
        // first, then the Sep geometry (Sep-1), first hit wins (D9's documented best-effort tie-break).
        // ResetMonth is IMMUTABLE per (entitlement_type, ok_version) natural key (ADR-021 Q1), so its
        // VALUE is identical whichever agreement's config supplies it.
        if (liveConfig is null)
        {
            var probeAnchors = new[]
            {
                new DateOnly(entitlementYear, 1, 1), // calendar (reset-1) ferieår start for E
                new DateOnly(entitlementYear, 9, 1)  // Sep (reset-9) ferieår start for E
            };
            foreach (var anchor in probeAnchors)
            {
                // ResolveAgreementAtAsync parity: GetByUserIdAtAsync(anchor) ?? user.AgreementCode.
                var anchorAgreement = await _agreementCodeRepo.GetByUserIdAtAsync(employeeId, anchor, ct)
                    ?? user.AgreementCode;
                // D9's `continue`: skip today's code — already known configless (step (a) missed on it).
                if (string.Equals(anchorAgreement, todayAgreementCode, StringComparison.Ordinal))
                    continue;
                // ResolveFallbackLiveAsync parity: GetCurrentOpenAsync(type, anchorAgreement, user.OkVersion).
                var altLive = await _configRepo.GetCurrentOpenAsync(entitlementType, anchorAgreement, user.OkVersion, ct);
                if (altLive is not null)
                {
                    liveConfig = altLive; // ResetMonth discovery + ResolveDatedConfigAsync fallback terminal
                    break;                // first hit wins (D9 precedence)
                }
            }
        }

        // (c) No config resolvable for the type under ANY agreement at ANY anchor (today's, OR the
        // year-start agreements at Jan-1/Sep-1 of entitlementYear) — a genuinely unconfigured type;
        // a real error, throw. This is EXACTLY D9's terminal: it renders an empty row here (liveConfig
        // still null after the anchor probe). The service settles a real disposition, so it fails loud
        // rather than rendering empty — but the THROW CONDITION matches D9's empty-row condition
        // precisely (so the service settles every closed year D9 renders non-empty, and only this
        // genuinely-unconfigured case throws).
        if (liveConfig is null)
        {
            throw new InvalidOperationException(
                $"Vacation settlement: no entitlement config resolvable for {entitlementType} under " +
                $"today's agreement '{todayAgreementCode}' or the year-start agreements at Jan-1/Sep-1 " +
                $"{entitlementYear} (employee {employeeId}); the type appears unconfigured " +
                $"(ok_version {user.OkVersion}) — D9 would render an empty row here.");
        }
        var resetMonth = liveConfig.ResetMonth;

        // S80 / TASK-8001 (R10) — the closed accrual-window START for the settled entitlementYear now
        // comes from the shared EntitlementPeriodResolver (BEHAVIOR-IDENTICAL for VACATION: reset_month
        // 9 ⇒ Sep 1 E; reset_month 1 ⇒ Jan 1 E; SPECIAL_HOLIDAY ⇒ Jan 1 E when TASK-8002 settles it).
        //
        // The VALUATION boundary (boundaryDate) is DELIBERATELY the ferieår END date — 31 Aug for
        // VACATION reset_month 9; 31 Dec E for reset_month 1 — NOT the resolver's Boundary (the §21
        // 31-Dec DEADLINE used by IsBoundaryPassed for due-detection). These are two distinct concepts
        // for VACATION: this date is the AccrualMath.EarnedToDate asOf AND the STORED
        // SettlementBoundaryDate snapshot field (the S69 W1 anchor; the §24 wage-mapping asOf). Keeping
        // it the ferieår END preserves the snapshot byte-for-byte (the hard VACATION-unchanged
        // invariant). TASK-8002 supplies SPECIAL_HOLIDAY's own valuation boundary (30 Apr E+2) when it
        // wires the SPECIAL_HOLIDAY settle path — that is OUT of this task's scope.
        var closedFerieaarStart = EntitlementPeriodResolver
            .ResolveForYear(entitlementType, resetMonth, entitlementYear).AccrualStart;
        DateOnly boundaryDate = resetMonth == 1
            ? new DateOnly(entitlementYear, 12, 31)
            : closedFerieaarStart.AddYears(1).AddDays(-1);

        // S70 / TASK-7004 (SPRINT-70 R5) — the VALUATION boundary. YEAR_END values at the ferieår
        // end (unchanged); a TERMINATION values at the employment END DATE (whole-month §26 basis,
        // owner D-B): earned crystallizes asOf the end date, and the recorded-absence window is
        // capped at it. The ANCHOR dates (agreement/position/quota/okVersion below) stay the
        // strictly-dated ferieår START in BOTH cases — only the valuation cutoff moves.
        var valuationBoundary = terminationCutoff ?? boundaryDate;

        var okVersion = OkVersionResolver.ResolveVersion(closedFerieaarStart);

        // The STRICTLY-DATED historical agreement in force at the closed ferieår start. This serves TWO
        // distinct roles with DIFFERENT null semantics:
        //
        //  (a) the snapshot's §24 wage-mapping KEY (ADR-033 D7) — must be the strict dated value, NEVER a
        //      live fallback. A WARNING (Step-5a P1/P4) — the prior code put `?? user.AgreementCode` into
        //      the snapshot, so a missing dated row silently keyed the §24 payout off the employee's
        //      CURRENT live code, breaking replay determinism and risking a wrong-agreement lønart. Fail
        //      CLOSED here (symmetric with the Position fail-closed throw below): a settlement that cannot
        //      pin the dated agreement at the ferieår start must NOT stage a live/empty-keyed payout.
        //  (b) the D9-parity VALUATION's config-resolution agreement — which DELIBERATELY falls back to
        //      user.AgreementCode (the verbatim ResolveDatedConfigAsync chain). Computed below from the
        //      same strict value with the fallback re-applied, so the valuation path is byte-for-byte
        //      identical to today.
        var datedAgreementForSnapshot = await _agreementCodeRepo.GetByUserIdAtAsync(employeeId, closedFerieaarStart, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: no dated user_agreement_codes row covers {closedFerieaarStart:yyyy-MM-dd} " +
                $"(ferieår start) for employee {employeeId}; cannot capture the §24 wage-type-mapping " +
                "agreement_code (ADR-033 D7) — settlement capture fails closed rather than keying the payout " +
                "off the employee's current live agreement.");

        // ResolveDatedConfigAsync (verbatim D9): dated → today-code? liveConfig → fallback-live ?? liveConfig.
        // Byte-identical to the prior `GetByUserIdAtAsync(...) ?? user.AgreementCode`: the fail-closed throw
        // above means control only reaches here when the dated read was non-null, so the `?? user.AgreementCode`
        // fallback was already an unreachable branch — `agreementCode` takes the same value it took before.
        var agreementCode = datedAgreementForSnapshot;
        var dated = await _configRepo.GetByTypeAtAsync(
            entitlementType, agreementCode, okVersion, closedFerieaarStart, ct);
        EntitlementConfig datedConfig;
        if (dated is not null)
        {
            datedConfig = dated;
        }
        // BLOCKER B2 (Codex Step-5a, TASK-7004 fix-forward) — SPRINT-70 R5/R4 + ADR-033 D10: a
        // TERMINATION crystallization AND an R4 leaver-deferred capture resolve their quota at the
        // strictly-dated ferieår-start anchor, FAIL-CLOSED — the D9 live-fallback chain below must
        // never let the CURRENT live quota determine what a leaver crystallized (a later quota
        // amendment must not change a recorded leaver quantity). Same posture as the dated
        // agreement-code throw above. Operational consequence (the D10 posture): a leaver with
        // missing dated config history fails LOUDLY on every settlement poll (per-tuple isolation,
        // retried) until an operator repairs the dated history — never silently valued against
        // today's config. The ACTIVE-employee YEAR_END path below keeps the D9 fallback chain
        // BYTE-IDENTICAL (its established S68 behavior must not change).
        else if (terminationCutoff is not null || deferredDisposition)
        {
            throw new InvalidOperationException(
                $"Vacation settlement: no dated entitlement_configs row covers {closedFerieaarStart:yyyy-MM-dd} " +
                $"(ferieår start) for {entitlementType} under agreement '{agreementCode}' (ok_version {okVersion}) " +
                $"for employee {employeeId}; cannot resolve the dated quota for a TERMINATION/leaver-deferred " +
                "settlement capture — settlement capture fails closed rather than valuing against the " +
                "employee's current live entitlement config.");
        }
        else if (string.Equals(agreementCode, todayAgreementCode, StringComparison.Ordinal))
        {
            datedConfig = liveConfig; // dated miss under today's agreement → the live (open) row
        }
        else
        {
            // dated miss under a DIFFERENT (historical) agreement → that agreement's live-open row,
            // else liveConfig (D9's ResolveFallbackLiveAsync terminal; keyed on user.OkVersion as D9).
            datedConfig = await _configRepo.GetCurrentOpenAsync(entitlementType, agreementCode, user.OkVersion, ct)
                ?? liveConfig;
        }

        // Closed-year balance — the EXACT D9 operands (ADR-032 D2: balance.Used IS the authoritative
        // recorded-feriedage sum written at booking; carryover_in entered the closed year; planned
        // is untaken-at-boundary). A missing balance row ⇒ zero-state (employee with no consumption).
        var closedBalance = await _balanceRepo.GetByEmployeeAndTypeAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
        var closedUsed = closedBalance?.Used ?? 0m;
        var closedCarryoverIn = closedBalance?.CarryoverIn ?? 0m;
        var closedPlanned = closedBalance?.Planned ?? 0m;

        // earned-at-boundary (the D9 operand). Fraction-independent day-count per S63/ADR-031 (1.0m).
        //
        // BLOCKER B1 (Codex Step-5a, TASK-7004 fix-forward) — SPRINT-70 R5 + owner D-B: a
        // TERMINATION capture (terminationCutoff != null) crystallizes Earned via the whole-month
        // EarnedToDate(asOf=endDate) UNCONDITIONALLY — NEVER the accrual-model branch. An IMMEDIATE
        // config grants the full annual quota up-front for CONSUMPTION purposes, but the §26
        // crystallization basis is whole-month earned-to-end-date (D-B): a September leaver on an
        // IMMEDIATE config must crystallize ~2.08 days, not 25. Nothing enforces MONTHLY for
        // VACATION configs (the S68 B1 lesson: uniform by seed ≠ enforced), so the model branch is
        // kept ONLY for terminationCutoff == null — the active-employee YEAR_END close AND the R4
        // leaver-deferred capture, both valued at the ferieår END where MONTHLY's 12/12 accrual and
        // IMMEDIATE's full quota legitimately coincide on the completed-year quantity (MONTHLY
        // accrues to the boundary per AccrualMath, from max(ferieårStart, employment_start_date);
        // IMMEDIATE is the full quota up-front — the unchanged S68 behavior).
        var earnedAtBoundary = terminationCutoff is not null
            ? AccrualMath.EarnedToDate(datedConfig.AnnualQuota, 1.0m, closedFerieaarStart, user.EmploymentStartDate, valuationBoundary)
            : string.Equals(datedConfig.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
                ? AccrualMath.EarnedToDate(datedConfig.AnnualQuota, 1.0m, closedFerieaarStart, user.EmploymentStartDate, valuationBoundary)
                : datedConfig.AnnualQuota;

        // The recorded per-absence feriedage components (ADR-032 D2) within the closed ferieår, capped
        // at the valuation boundary — the auditable breakdown carried in the snapshot. For VACATION the
        // absence_type IS 'VACATION' (EntitlementMapping). The authoritative "used" scalar stays
        // closedBalance.Used (the D9 operand) for YEAR_END; a TERMINATION uses the recorded SUM ≤ the
        // end date instead (SPRINT-70 R5 — the declared divergence: a post-end-date booking cannot be
        // taken and must not consume).
        var recordedAbsences = await ReadRecordedFeriedageAsync(
            conn, tx, employeeId, entitlementType, closedFerieaarStart, valuationBoundary, ct);
        var usedForSnapshot = terminationCutoff is null
            ? closedUsed
            : recordedAbsences.Sum(a => a.Feriedage);

        // §21 written transfer-agreement days (ADR-033 D8; 0 when no agreement — the law's §24 default).
        var transferAgreement = await _transferRepo.GetByKeyAsync(conn, tx, employeeId, entitlementYear, entitlementType, ct);
        var transferAgreementDays = transferAgreement?.TransferDays ?? 0m;

        // §24 wage-type-mapping natural key (ADR-033 D7 / ADR-020 (time_type, ok_version, agreement_code,
        // position)) captured into the immutable snapshot so the S69 §24 Payroll emitter resolves the
        // lønart off THIS snapshot (replay-deterministic, no live lookup). The `position` component is
        // read from the dated employee_profiles row (ADR-023) AS-OF closedFerieaarStart — the SAME
        // instant as agreementCode (line ~436) and okVersion (line ~433), so the four key components are
        // snapshot-internally consistent.
        //
        // FAIL-CLOSED (B3/B5): if no dated profile covers closedFerieaarStart, THROW — capture must fail
        // loudly rather than silently fall back to live/empty profile data and stage a wrong-keyed payout.
        // (A resolved profile whose Position is itself null/empty IS fine — pass it through; the emitter
        // canonicalizes null→"" for the wage_type_mappings.position '' default.)
        var settlementProfile = await _profileResolver.GetByEmployeeIdAtAsync(employeeId, closedFerieaarStart, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: no dated employee_profiles row covers {closedFerieaarStart:yyyy-MM-dd} " +
                $"(ferieår start) for employee {employeeId}; cannot capture the §24 wage-type-mapping " +
                "position (ADR-033 D7) — settlement capture fails closed rather than using live/empty data.");
        var settlementPosition = settlementProfile.Position;

        var snapshot = new VacationSettlementSnapshot
        {
            RecordedAbsences = recordedAbsences,
            Earned = earnedAtBoundary,
            // YEAR_END: the balance.Used scalar (the D9 operand — unchanged). TERMINATION: the
            // recorded-absence sum ≤ the end date (SPRINT-70 R5 declared divergence).
            Used = usedForSnapshot,
            Planned = closedPlanned,
            CarryoverIn = closedCarryoverIn,
            AnnualQuota = datedConfig.AnnualQuota,
            CarryoverMax = datedConfig.CarryoverMax,
            ResetMonth = resetMonth,
            OkVersion = okVersion,
            // §24 wage-type natural key (ADR-033 D7 / ADR-020) — all pinned at closedFerieaarStart.
            AgreementCode = datedAgreementForSnapshot, // STRICTLY-dated ferieår-start agreement (fail-closed, no live fallback — Step-5a P1/P4)
            Position = settlementPosition,             // dated employee_profiles position (ADR-023)
            // The VALUATION boundary. YEAR_END: the FERIEÅR-END accrual boundary (Aug 31 for VACATION
            // reset_month 9; Dec 31 only when reset_month==1) — the inherited S68 valuation boundary,
            // reused as the §24 wage-mapping asOf, UNCHANGED.
            // FOLLOW-UP (S69 Step-7a W1): the LEGAL §24/§21 anchor is 31 Dec (Ferielov §21 stk.2; S65 research
            // docs/references/ferie-transfer-timing-research.md). Inert today (every §24 mapping is open-from-2020,
            // so Aug-31 vs 31-Dec asOf resolve identically); the owner must rule the asOf when the real §24 SLS
            // lønart lands and a dated supersession could fall between Aug 31 and 31 Dec.
            // TERMINATION (S70 R5): the employment END DATE — the crystallization boundary of THIS
            // settlement (no §24 staging ever reads a TERMINATION snapshot; the 3b §26 asOf is 3b scope).
            SettlementBoundaryDate = valuationBoundary,
            TransferAgreementDays = transferAgreementDays,
            IsFeriehindret = false, // slice 1 — §22 not modeled (ADR-033 D10)
            // S70 / TASK-7004 (SPRINT-70 R4/R5) — termination crystallization extensions. All
            // omitted from the serialized JSON when unset, so the ACTIVE-employee YEAR_END snapshot
            // stays byte-identical to its pre-S70 shape. CrystallizedDays is stamped AFTER capture
            // by SettleTerminationAsync (the pure CrystallizeTermination of THIS snapshot).
            TerminationDate = terminationDate,
            CrystallizationBasis = terminationCutoff is null ? null : CrystallizationBasisS26WholeMonth,
            DeferredDisposition = deferredDisposition,
        };

        var json = JsonSerializer.Serialize(snapshot, SnapshotJson);
        return (snapshot, json);
    }

    /// <summary>
    /// In-tx read of the recorded per-absence <c>feriedage</c> components (ADR-032 D2) for the
    /// settled entitlement type within the closed ferieår window. VACATION-only in slice 1 — the
    /// <c>absence_type</c> for VACATION entitlement is the literal <c>'VACATION'</c>
    /// (Backend.Api <c>EntitlementMapping</c>); rows with a NULL feriedage (un-backfilled legacy)
    /// are excluded from the components (the authoritative scalar is the balance's <c>used</c>).
    /// </summary>
    private static async Task<IReadOnlyList<RecordedAbsenceFeriedage>> ReadRecordedFeriedageAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // entitlementType → absence_type. Slice 1 settles VACATION (absence_type 'VACATION'); for
        // forward-safety map the common types, defaulting to the identity (covers VACATION).
        var absenceType = entitlementType switch
        {
            "SPECIAL_HOLIDAY" => "SPECIAL_HOLIDAY_ALLOWANCE",
            _ => entitlementType, // VACATION/CARE_DAY/SENIOR_DAY are identity; CHILD_SICK has 3 variants (not settled in 1a)
        };

        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, feriedage
            FROM absences_projection
            WHERE employee_id = @employeeId
              AND absence_type = @absenceType
              AND date >= @start AND date <= @end
              AND feriedage IS NOT NULL
            ORDER BY outbox_id ASC
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("absenceType", absenceType);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);

        var list = new List<RecordedAbsenceFeriedage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new RecordedAbsenceFeriedage(reader.GetGuid(0), reader.GetDecimal(1)));
        return list;
    }

    // ------------------------------------------------------------------
    // The legal partition (ADR-033 D5/D10). PURE function of the snapshot — the same operands as
    // the S66 D9 `expiring` figure (BalanceEndpoints): over_cap == that D9 figure exactly.
    // ------------------------------------------------------------------

    /// <summary>
    /// Partition the closed-year remainder into the §21/§24/§34 buckets. PURE function of the
    /// captured snapshot (ADR-033 D3 quantity-determinism; replay-stable). The arithmetic is done on
    /// exact decimals and each bucket is rounded to 2dp (the <c>NUMERIC(6,2)</c> storage precision)
    /// — <see cref="SettlementPartition.ForfeitDays"/> is computed identically to the S66 D9
    /// <c>expiring</c> figure (BalanceEndpoints): <c>round(max(0, raw − carryover_max), 2)</c>.
    /// </summary>
    /// <remarks>
    /// disposable = max(0, earnedAtBoundary + carryoverIn − used − planned) — this SUBTRACTS planned
    /// to byte-match the D9 <c>transferableRaw</c> (BalanceEndpoints). For a closed year planned is 0,
    /// so this coincides with the prompt's <c>disposable = max(0, earned + carryoverIn − used)</c>;
    /// keeping the planned term makes <c>over_cap</c> identical to D9's <c>expiring</c> regardless.
    /// </remarks>
    internal static SettlementPartition Partition(VacationSettlementSnapshot s)
    {
        var raw = s.Earned + s.CarryoverIn - s.Used - s.Planned;
        var disposable = Math.Max(0m, raw);

        var underCap = Math.Min(disposable, s.CarryoverMax);     // §21 + §24 tranche (≤ 5th-week ceiling)
        var overCap = Math.Max(0m, disposable - s.CarryoverMax); // §34 forfeiture-candidate (== D9 expiring)

        // WARNING 2 (Codex Step-5a) — clamp the agreement-days operand defensively. The DB CHECK
        // (transfer_days >= 0) now blocks STORING a negative agreement, but the partition stays robust
        // regardless of the snapshot source: transfer = max(0, min(agreementDays, underCap)) so a
        // stray negative can never produce a negative transfer/carryover nor inflate payout
        // (payoutDays = underCap − transferDays would otherwise EXCEED underCap on a negative transfer).
        var transferDays = Math.Max(0m, Math.Min(s.TransferAgreementDays, underCap)); // §21 (0 if no agreement)
        var payoutDays = underCap - transferDays;                                      // §24 (the law's default)
        var forfeitDays = overCap;                                                     // §34-candidate

        return new SettlementPartition(
            Disposable: Round2(disposable),
            UnderCap: Round2(underCap),
            OverCap: Round2(overCap),
            TransferDays: Round2(transferDays),
            PayoutDays: Round2(payoutDays),
            ForfeitDays: Round2(forfeitDays));
    }

    // WARNING 3 (Codex Step-5a) — the bucket rounding MUST match the S66 D9 reader, which rounds
    // with Math.Round(x, 2) (default MidpointRounding.ToEven; BalanceEndpoints ~line 902). Using
    // AwayFromZero here would diverge `over_cap` from D9 `expiring` on a .xx5 midpoint. ToEven so
    // ForfeitDays == D9 expiring byte-for-byte (priority 2 / ADR-033 D3 quantity-determinism).
    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    // ------------------------------------------------------------------
    // S70 / TASK-7004 — Europe/Copenhagen business-date helper (SPRINT-70 R4 leak-proofing pin (b)).
    // The leaver/no-partition decision compares the in-lock re-read employment_end_date against the
    // COPENHAGEN business date (never raw UTC/CURRENT_DATE — the boundary-timezone rule the close
    // service documents). Mirrors SettlementCloseService.CopenhagenToday verbatim, sourced from the
    // injected TimeProvider (the trigger MAY read the clock; the settled QUANTITY stays a pure
    // function of the captured snapshot — ADR-033 D3). Scoped to this file like the close service's
    // copy (the Orchestrator may later hoist both into the follow-up (v) business-timezone helper).
    // ------------------------------------------------------------------

    private static readonly TimeZoneInfo CopenhagenZone = ResolveCopenhagenZone();

    private DateOnly CopenhagenToday()
    {
        var utcNow = _timeProvider.GetUtcNow(); // the injected seam — overridable in tests/hosts.
        var copenhagenNow = TimeZoneInfo.ConvertTime(utcNow, CopenhagenZone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        // IANA id first (Linux CI + ICU-backed Windows), Windows registry id as fallback, UTC as the
        // never-crash terminal (degraded but deterministic) — the SettlementCloseService shape.
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    // ------------------------------------------------------------------
    // Event-emit + audit_projection sync-in-tx (ADR-018 D3 + ADR-026 D13). The audit row is
    // dispatched FROM this BackgroundService-invoked site via the registry (NOT an endpoint).
    // ------------------------------------------------------------------

    // internal (S71 / TASK-7104): SettlementReversalService reuses this exact outbox +
    // audit_projection dispatch shape for the SettlementReversed emission (same assembly).
    // actorOrgId: the OPERATOR's org for operator-driven events (the request-endpoint /
    // lifecycle-writer convention); the close-service (system-actor) sites pass the employee
    // org for BOTH — the S68 recorded convention, a system actor has no org of its own.
    internal async Task EmitAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string streamId, IDomainEvent @event, string actorId, string? actorOrgId, string targetOrgId,
        CancellationToken ct)
    {
        // Enqueue first to capture the outbox_id (aligns audit_projection.outbox_id with the global
        // outbox sequence; ADR-026 D13 ordering), then write the projection row in the SAME tx.
        var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

        var auditCtx = new AuditProjectionContext(
            ActorId: actorId,
            ActorPrimaryOrgId: actorOrgId,
            CorrelationId: @event.CorrelationId,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
            ResolvedTargetOrgId: targetOrgId);

        // Registry dispatch (TASK-6803 mappers register separately via DI; we build against the
        // interface). If no mapper is registered for the event type, TryMap returns null — skip the
        // projection row (the event + outbox still commit; mirrors the backfill skip semantics). The
        // event_id UNIQUE + ON CONFLICT DO NOTHING in the repo keeps a re-run idempotent.
        var rowData = _auditRegistry.TryMap(@event, auditCtx);
        if (rowData is null)
        {
            _logger.LogWarning(
                "No audit-projection mapper registered for settlement event {EventType} ({EventId}); audit_projection row skipped.",
                @event.EventType, @event.EventId);
            return;
        }

        await _auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, rowData, auditCtx, ct);
    }
}

/// <summary>
/// S68 / TASK-6804 — the §21/§24/§34 partition of a closed vacation year (the legal core; ADR-033
/// D5/D10). PURE result of <see cref="VacationSettlementService.Partition"/>. All day-counts are
/// rounded to the <c>NUMERIC(6,2)</c> storage precision. <see cref="ForfeitDays"/> equals the S66
/// D9 <c>expiring</c> figure (the over-cap §34-candidate bucket).
/// </summary>
public sealed record SettlementPartition(
    decimal Disposable,
    decimal UnderCap,
    decimal OverCap,
    decimal TransferDays,
    decimal PayoutDays,
    decimal ForfeitDays);

/// <summary>
/// S70 / TASK-7004 (SPRINT-70 R5; ADR-033 D9 slice 3a) — the TERMINATION crystallization result.
/// PURE result of <see cref="VacationSettlementService.CrystallizeTermination"/>:
/// <see cref="PreClamp"/> = <c>round2(Earned + CarryoverIn − Used)</c> of the captured snapshot;
/// <see cref="CrystallizedDays"/> = <c>max(0, PreClamp)</c> (snapshot-only — the row's bucket
/// columns are all zero on SETTLED); <see cref="SettlementState"/> per the PINNED rule (SETTLED
/// unless the pre-clamp is negative); <see cref="ForfeitFlagDays"/> = <c>|PreClamp|</c> iff
/// negative (the S68 forfeit-FLAG convention — parked PENDING_REVIEW until 3b's §7/waiver
/// channel), else 0. Day-counts only, NUMERIC(6,2) precision (money-free, ADR-033 D1).
/// </summary>
public sealed record TerminationCrystallization(
    decimal PreClamp,
    decimal CrystallizedDays,
    string SettlementState,
    decimal ForfeitFlagDays);

/// <summary>
/// S68 / TASK-6804 — the outcome of <see cref="VacationSettlementService.SettleAsync"/>. Either a
/// fresh settlement (with its persisted row + partition), an idempotent no-op (an active
/// settlement already existed — the in-lock re-check or the 23505 single-settle backstop), or the
/// S70 Step-7a benign <see cref="NotDue"/> no-op (the in-lock due re-evaluation failed — nothing
/// exists and nothing was written).
/// </summary>
public sealed record SettlementOutcome
{
    /// <summary><c>true</c> when this call produced a new settlement; <c>false</c> on the idempotent no-op.</summary>
    public required bool DidSettle { get; init; }

    /// <summary>The active settlement row (freshly inserted, the pre-existing one on a no-op, or
    /// the CONFLICTING row on an R7b refusal). Null ONLY on the <see cref="NotDue"/> outcome —
    /// no row exists for the tuple and none was written.</summary>
    public required VacationSettlementRow? Row { get; init; }

    /// <summary>The computed §21/§24/§34 partition — present only on a PARTITIONED YEAR_END
    /// settlement (<see cref="DidSettle"/> true). Null on the no-op, on a TERMINATION
    /// crystallization, and on an S70 R4 leaver deferred-disposition row (no partition is
    /// computed for leavers — SPRINT-70 R4).</summary>
    public SettlementPartition? Partition { get; init; }

    /// <summary>
    /// S70 / TASK-7004 (SPRINT-70 R7b) — <c>true</c> when a TERMINATION pass was REFUSED because
    /// the target ferieår already held an active settlement of another trigger (the loud
    /// <c>SettlementManualReviewFlagged</c> signal + audit were emitted; NO row was written;
    /// <see cref="Row"/> carries the untouched conflicting row). The close service treats this as
    /// handled (<see cref="DidSettle"/> is <c>false</c>).
    /// </summary>
    public bool RefusedConflict { get; init; }

    /// <summary>
    /// S70 Step-7a B1 — <c>true</c> when the in-lock due re-evaluation
    /// (<see cref="VacationSettlementService.IsTerminationDueUnderLock"/> on the TERMINATION fork,
    /// or the leaver-deferred go-live floor re-check) found the tuple NO LONGER due: a competing
    /// mutation (an admin end-date correction per R1, typically) won the lock race and the
    /// corrected state wins. NOTHING was written and NO event was emitted; <see cref="Row"/> is
    /// null. The next poll re-enumerates against fresh state.
    /// </summary>
    public bool NotDue { get; init; }

    public static SettlementOutcome Settled(VacationSettlementRow row, SettlementPartition? partition) =>
        new() { DidSettle = true, Row = row, Partition = partition };

    public static SettlementOutcome AlreadySettled(VacationSettlementRow existing) =>
        new() { DidSettle = false, Row = existing, Partition = null };

    public static SettlementOutcome Refused(VacationSettlementRow conflicting) =>
        new() { DidSettle = false, Row = conflicting, Partition = null, RefusedConflict = true };

    /// <summary>The S70 Step-7a B1 benign no-op — the tuple is no longer due under the lock
    /// (no row, no event, no throw; see <see cref="NotDue"/>).</summary>
    public static SettlementOutcome NotDueUnderLock() =>
        new() { DidSettle = false, Row = null, Partition = null, NotDue = true };
}

/// <summary>
/// S71 / TASK-7104 (SPRINT-71 R4) — raised by the SUPERSEDING settlement path when its §21
/// partition would write <c>carryover_in</c> into a year that itself holds an active
/// (non-REVERSED) settlement row: that year's recorded disposition is final, and a retroactive
/// carryover mutation would corrupt it. The reversal service catches this and performs the R4
/// FULL rollback (no reversal either — the original row stands; the operator is told why).
/// </summary>
public sealed class SupersedingCarryoverConflictException : Exception
{
    public string EmployeeId { get; }
    public string EntitlementType { get; }

    /// <summary>The year the §21 carryover would have been written INTO (reversed-year + 1).</summary>
    public int ConflictingYear { get; }

    public string ConflictingState { get; }
    public string ConflictingTrigger { get; }

    public SupersedingCarryoverConflictException(
        string employeeId, string entitlementType, int conflictingYear,
        string conflictingState, string conflictingTrigger)
        : base(
            $"Superseding settlement for (employee_id='{employeeId}', type='{entitlementType}') would " +
            $"write §21 carryover_in into year {conflictingYear}, which holds an active " +
            $"{conflictingTrigger}/{conflictingState} settlement row — fail closed (SPRINT-71 R4 " +
            "supersede-side guard); the whole reversal rolls back.")
    {
        EmployeeId = employeeId;
        EntitlementType = entitlementType;
        ConflictingYear = conflictingYear;
        ConflictingState = conflictingState;
        ConflictingTrigger = conflictingTrigger;
    }
}
