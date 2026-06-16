using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S71 / TASK-7104 (ADR-033 D4/D5; SPRINT-71 R1/R3/R4/R6/R10/R12 + owner D-A/D-E) — the
/// operator-authorized settlement REVERSAL service. Reversal is a COMPENSATING entry, never a
/// rollback (the row survives as history with its snapshot intact) and never automatic
/// (ADR-013): the TASK-7102 endpoint drives this service with an explicit operator mode.
///
/// <para><b>ONE atomic tx under the R12 employee advisory lock (lock FIRST, then re-read
/// EVERYTHING — the S70 B1 lesson):</b></para>
/// <list type="number">
///   <item><description>advisory lock → in-lock re-read of the ACTIVE row → fail-closed scope
///   guards (the B1 GENERATION binding: the active row's sequence must equal the commanded
///   <c>ExpectedSettlementSequence</c> BEFORE any version comparison — row versions restart at
///   1 per generation, so a version-only CAS is ABA-prone across generations; D-A
///   zero-bucket-only: a <c>transfer_days &gt; 0</c> carryover-writer refuses; the
///   R4 reconciled-row exclusion: an operator-reconciled §24 disposition refuses — reversing it
///   would re-stage a real line despite the recorded external payment; no active row
///   refuses);</description></item>
///   <item><description>CAS the active row → <c>REVERSED</c> — STATE-ONLY (no new sequence; the
///   snapshot, buckets, disposition and DEFER marker are preserved — the TASK-7100 CHECKs
///   enforce; PENDING_REVIEW → REVERSED is legal) — the CAS predicate is the caller-supplied
///   row version (ADR-019 If-Match) AND <c>settlement_state &lt;&gt; 'REVERSED'</c> in the
///   UPDATE's WHERE (a 0-row update = a clean CAS loss);</description></item>
///   <item><description>R6/D-E — VOID every non-voided <c>termination_payout_requests</c> row
///   bound to the reversed settlement row (BOTH OPEN and LINE_STAGED; HR re-records against the
///   successor);</description></item>
///   <item><description><b>bare mode:</b> the R3 durable not-due marker is set ON the reversed
///   row in the same CAS — TERMINAL in 3b (no clear operation exists; the R1
///   generation-from-history arithmetic below pre-settles a future revival's sequence (next
///   row = 2g−1 of g = highest-recorded-generation + 1) even though the revival OPERATION is
///   the REHIRE follow-up's scope);</description></item>
///   <item><description><b>supersede mode:</b> when a corrected end date rides the command,
///   FIRST run the B2 affected-span settlement guard (the R13 analog, in-lock, post-CAS,
///   BEFORE the lifecycle writer): any OTHER active (non-REVERSED) settlement row across the
///   FULL ferieår span <c>[min(ferieår(old), ferieår(new)) .. max(...)]</c> — old from the
///   in-lock user row, new from the command; the just-reversed row is already REVERSED in-tx
///   and thus excluded — refuses with FULL rollback (it would remain standing on superseded
///   lifecycle facts); then apply the corrected end date via the
///   SHARED <see cref="EmploymentEndDateLifecycleWriter"/> (R4 two-aggregate preconditions:
///   settlement CAS + <c>users.version</c> If-Match; the FULL PUT choreography — lifecycle
///   decision, versioned write, R1(e) side effects, R10 event, ADR-026 + users_audit rows),
///   then re-evaluate eligibility against the CORRECTED in-tx state via
///   <see cref="VacationSettlementService.ResettleSupersedingAsync"/> (trigger-specific
///   predicates + the R1 next-generation sequence + the supersede-side fail-closed carryover
///   guard). ANY failed leg ⇒ FULL rollback — no reversal either; the original row stands and
///   the reason is surfaced;</description></item>
///   <item><description>emit <see cref="SettlementReversed"/> (the R10 payload: original
///   identity + settlement-row sequence, reversal kind + successor sequence, trigger,
///   per-bucket positive quantities incl. nullable CrystallizedDays/ClaimDispositionDays, the
///   preserved snapshot; NO staged-line references — the Payroll consumer derives compensation
///   targets from its own staged-line records per R9) on <c>employee-{id}</c> via the outbox +
///   the ADR-026 audit row, SAME tx. Actor = the OPERATOR (passed in), never a system
///   actor.</description></item>
/// </list>
///
/// <para><b>Failure shape:</b> operator-outcome failures (guards, CAS losses, ineligible
/// supersession) return a structured <see cref="SettlementReversalResult"/> with the tx rolled
/// back — 409/412-shaped for the TASK-7102 endpoint to map; caller-contract violations
/// (malformed commands) throw <see cref="ArgumentException"/>; infrastructure faults propagate
/// after rollback.</para>
///
/// <para><b>Declared decisions:</b> the advisory-lock SQL is inlined (the
/// <see cref="VacationSettlementService.SettleAsync"/> precedent — the Backend.Api
/// <c>EmployeeConsumptionLock</c> helper is out of this assembly's reach; the lock VALUE is
/// what matters). The CAS UPDATE is service-local SQL (the
/// <c>VacationSettlementEndpoints.CasUpdateSettlementAsync</c> precedent — repository delta
/// kept to reads + sequence mechanics). The SUPERSEDING settlement row and its own events keep
/// the deterministic <c>system:settlement-close:*</c> actor (byte-reuse of the settlement
/// internals); the operator's authorship is durably recorded on the <c>SettlementReversed</c>
/// event, the reversal's <c>vacation_settlement_audit</c> row and the lifecycle writer's rows.
/// The reversed-row audit action is <c>UPDATED</c> (the S68
/// <c>VacationSettlementRepository.AppendAuditAsync</c> doc pins UPDATED for the reversal
/// path).</para>
/// </summary>
public sealed class SettlementReversalService
{
    private const string StateReversed = "REVERSED";
    private const string TerminationTrigger = "TERMINATION";

    private readonly DbConnectionFactory _connectionFactory;
    private readonly VacationSettlementService _settlementService;
    private readonly VacationSettlementRepository _settlementRepo;
    private readonly TerminationPayoutRequestRepository _requestRepo;
    private readonly EmploymentEndDateLifecycleWriter _lifecycleWriter;
    private readonly UserRepository _userRepo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettlementReversalService> _logger;

    public SettlementReversalService(
        DbConnectionFactory connectionFactory,
        VacationSettlementService settlementService,
        VacationSettlementRepository settlementRepo,
        TerminationPayoutRequestRepository requestRepo,
        EmploymentEndDateLifecycleWriter lifecycleWriter,
        UserRepository userRepo,
        TimeProvider timeProvider,
        ILogger<SettlementReversalService> logger)
    {
        _connectionFactory = connectionFactory;
        _settlementService = settlementService;
        _settlementRepo = settlementRepo;
        _requestRepo = requestRepo;
        _lifecycleWriter = lifecycleWriter;
        _userRepo = userRepo;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Execute one operator-authorized reversal per the command's mode. Owns the connection and
    /// the ONE atomic tx (commit on success; rollback on EVERY failure — R4
    /// full-rollback-on-failed-leg).
    /// </summary>
    public async Task<SettlementReversalResult> ReverseAsync(
        SettlementReversalCommand command, CancellationToken ct = default)
    {
        ValidateCommand(command);

        // R4 self-target exclusion (cycle-2 Reviewer B1, defense-in-depth behind the 7102
        // endpoint guard): every reversal mode that applies an end-date mutation refuses
        // actor == employee exactly like the PUT — the S70 W1 self-reinstatement hole must not
        // reopen through this second lifecycle writer. Checked BEFORE any DB work.
        if (command.HasEndDateCorrection
            && string.Equals(command.ActorId, command.EmployeeId, StringComparison.Ordinal))
        {
            return SettlementReversalResult.Fail(
                SettlementReversalFailure.SelfTarget,
                "Own employment end date cannot be modified through a reversal; a second " +
                "administrator must perform this change (SPRINT-71 R4 self-target exclusion).");
        }

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // ReadCommitted — the advisory-lock wait needs a fresh post-lock snapshot (the
        // SettleAsync / close-service reasoning).
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // (1) R12 — the employee advisory lock FIRST, before ANY read (held to commit; the
            // SAME key SettleAsync / Step A / the end-date PUT / reconcile-payout acquire).
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("employeeId", command.EmployeeId);
                await lockCmd.ExecuteScalarAsync(ct);
            }

            // (2) In-lock re-read of the ACTIVE row — everything below decides off THIS read.
            var active = await _settlementRepo.GetActiveAsync(
                conn, tx, command.EmployeeId, command.EntitlementType, command.EntitlementYear, ct);
            if (active is null)
            {
                return await FailAsync(tx, SettlementReversalFailure.NoActiveRow,
                    "No active (non-REVERSED) settlement exists for this (employee, type, year) — " +
                    "nothing to reverse.", ct);
            }

            // (2b) B1 (Step-5a cycle-1; R2 "CAS operations ALWAYS key on settlement sequence"):
            // the CAS must bind the GENERATION, not just the version. Settlement rows are
            // INSERTed at version 1, so versions RESTART per generation and
            // (employee, type, year, expectedVersion) alone is ABA-prone — a command built
            // against gen-1 (seq 1, v1) would otherwise CAS-reverse a gen-2 successor
            // (seq 3, v1) someone else created in between. Refused fail-closed BEFORE any
            // version comparison; a distinct discriminator for 7102's 409/412 mapping.
            if (active.Sequence != command.ExpectedSettlementSequence)
            {
                return await FailAsync(tx, SettlementReversalFailure.SequenceMismatch,
                    $"The active settlement row for this tuple is sequence {active.Sequence} " +
                    $"(generation {(active.Sequence + 1) / 2}), not the commanded sequence " +
                    $"{command.ExpectedSettlementSequence} — the row this command was built " +
                    "against has been superseded (versions restart at 1 per generation, so a " +
                    "version-only precondition cannot distinguish generations); refresh and " +
                    "re-issue against the current row.",
                    ct, actualSettlementVersion: active.Version,
                    actualSettlementSequence: active.Sequence);
            }

            // (3) D-A zero-bucket-only scope guard: a carryover-WRITING row is NOT reversible — the
            // carryover-compensating reversal is the named follow-up. Fail closed, loudly. Two
            // carryover-writing buckets exist: §21 transfer_days (3b) AND, since S79, §22
            // feriehindring_transfer_days (a FERIEHINDRING resolution wrote §21+§22 into next-year
            // carryover_in). Either > 0 ⇒ irreversible in-slice (SPRINT-79 R7 — the FERIEHINDRING
            // row is irreversible until the compensating-reversal follow-up; the operator's recorded
            // impediment is the legally-sensitive point of accountability).
            if (active.TransferDays > 0m || active.FeriehindringTransferDays > 0m)
            {
                return await FailAsync(tx, SettlementReversalFailure.CarryoverWritingRow,
                    $"Settlement sequence {active.Sequence} wrote carryover " +
                    $"(transfer_days={active.TransferDays}, feriehindring_transfer_days=" +
                    $"{active.FeriehindringTransferDays}) — carryover-writing rows are not reversible " +
                    "(owner D-A zero-bucket-only; the carryover-compensating reversal is a recorded " +
                    "follow-up).", ct);
            }

            // (4) R4 reconciled-row exclusion (cycle-2 Reviewer W2). DETECTION MECHANISM
            // (discovered + declared): the durable operator-reconcile marker is the row's
            // paired vacation_settlements.payout_reconciled_at/_by columns, written by the S69
            // reconcile-payout CAS endpoint (VacationSettlementEndpoints ~L806-836) under this
            // same advisory lock; the S69 §24 emitter keys its terminal SKIPPED_RECONCILED inbox
            // checkpoint OFF this marker — so the row marker is the authoritative source, and
            // checking it here under the lock is race-correct against a concurrent reconcile.
            if (active.PayoutReconciledAt is not null)
            {
                return await FailAsync(tx, SettlementReversalFailure.ReconciledRow,
                    $"Settlement sequence {active.Sequence} carries an operator-reconciled §24 " +
                    $"disposition (payout_reconciled_at={active.PayoutReconciledAt:O} by " +
                    $"'{active.PayoutReconciledBy}') — externally-reconciled dispositions are not " +
                    "reversible in 3b (reversing would re-stage a real line despite the recorded " +
                    "external payment; the delivery-wiring overlap item owns this).", ct);
            }

            // (5) CAS → REVERSED (state-only; ADR-019 If-Match + the state-transition predicate
            // in the WHERE). Bare mode sets the R3 marker in the SAME write.
            var bare = command.Mode == SettlementReversalMode.Bare;
            var reversed = await CasReverseAsync(conn, tx, active, command.ExpectedSettlementVersion, bare, ct);
            if (reversed is null)
            {
                return await FailAsync(tx, SettlementReversalFailure.CasConflict,
                    "Concurrency precondition failed on the settlement row — refresh and retry.",
                    ct, actualSettlementVersion: active.Version);
            }

            // The reversed row's audit trail (vacation_settlement_audit UPDATED; version
            // transition; the OPERATOR as actor).
            await _settlementRepo.AppendAuditAsync(conn, tx, reversed, "UPDATED",
                previousData: SerializeForAudit(active),
                newData: SerializeForAudit(reversed),
                versionBefore: active.Version, versionAfter: reversed.Version,
                command.ActorId, command.ActorRole, ct);

            // (6) R6/D-E — VOID every non-voided request bound to the reversed row, SAME tx.
            var voidedRequestIds = await _requestRepo.VoidBySettlementRowAsync(
                conn, tx, command.EmployeeId, command.EntitlementType, command.EntitlementYear,
                reversed.Sequence, ct);

            // (7) Supersede leg (mode-gated).
            VacationSettlementRow? successor = null;
            long? userVersionAfter = null;
            bool? userIsActiveAfter = null;
            if (command.Mode == SettlementReversalMode.ReverseAndSupersede)
            {
                if (command.HasEndDateCorrection)
                {
                    // (7a) B2 (Step-5a cycle-1) — the affected-span settlement guard INSIDE the
                    // reversal tx (the R13 analog; R4/R12): an end-date correction invalidates
                    // the settlement basis of EVERY ferieår in the FULL span
                    // [min(ferieår(old), ferieår(new)) .. max(...)] (intermediate years
                    // included — the R13 span semantics), not just the year being reversed.
                    // Re-read in-lock, post-CAS: the just-reversed target is already REVERSED
                    // in this tx and thus self-excluded; ANY other active (non-REVERSED) row in
                    // the span — ANY entitlement type, the R7a fail-closed convention — would
                    // remain standing on superseded lifecycle facts ⇒ FULL rollback. The old
                    // end date comes from the in-lock user row, the new from the command; a
                    // null date contributes no ferieår (the AffectedFerieaar convention). A
                    // reversal WITHOUT an end-date correction changes no lifecycle fact and
                    // needs no span guard.
                    var userBefore = await _userRepo.GetByIdIncludingTerminatedAsync(
                        conn, tx, command.EmployeeId, ct);
                    if (userBefore is null)
                    {
                        return await FailAsync(tx, SettlementReversalFailure.UserNotFound,
                            $"Employee '{command.EmployeeId}' not found.", ct);
                    }
                    var blockingRows = await GetOtherActiveRowsInCorrectionSpanAsync(
                        conn, tx, command.EmployeeId,
                        userBefore.EmploymentEndDate, command.CorrectedEndDate, ct);
                    if (blockingRows.Count > 0)
                    {
                        var blockingList = string.Join("; ", blockingRows.Select(b =>
                            $"{b.EntitlementType}/{b.EntitlementYear} sequence {b.Sequence} ({b.State})"));
                        return await FailAsync(tx, SettlementReversalFailure.AffectedSpanConflict,
                            "The end-date correction affects other settled ferieår — active " +
                            $"settlement rows stand in the corrected span: {blockingList}. " +
                            "Reverse those first; a correction across additional settled years " +
                            "is not supported in slice 3b (SPRINT-71 R13 span semantics, " +
                            "fail-closed).", ct);
                    }

                    // R4 — the SUBSUMED end-date mutation via the ONE shared lifecycle writer
                    // (the second aggregate's precondition = the caller-supplied users.version).
                    EmploymentEndDateLifecycleResult lifecycle;
                    try
                    {
                        lifecycle = await _lifecycleWriter.ApplyAsync(
                            conn, tx, command.EmployeeId, command.CorrectedEndDate,
                            command.ExpectedUserVersion!.Value,
                            command.ActorId, command.ActorRole, command.ActorOrgId,
                            command.CorrelationId, CopenhagenToday(), ct);
                    }
                    catch (OptimisticConcurrencyException ex)
                    {
                        return await FailAsync(tx, SettlementReversalFailure.UserVersionConflict,
                            "Concurrency precondition failed on the user row (the R4 two-aggregate " +
                            "precondition's second half) — refresh and retry.",
                            ct, actualUserVersion: ex.ActualVersion);
                    }
                    catch (KeyNotFoundException)
                    {
                        return await FailAsync(tx, SettlementReversalFailure.UserNotFound,
                            $"Employee '{command.EmployeeId}' not found.", ct);
                    }
                    userVersionAfter = lifecycle.VersionAfter;
                    userIsActiveAfter = lifecycle.NewIsActive;
                }

                // R1 — the next-generation row sequence, derived from the tuple's FULL recorded
                // history (the just-REVERSED row included): g = max((s+1)/2) + 1 → 2g−1. Never
                // restarts at 1.
                var sequences = await _settlementRepo.GetSequencesForTupleAsync(
                    conn, tx, command.EmployeeId, command.EntitlementType, command.EntitlementYear, ct);
                var supersedingSequence = NextGenerationRowSequence(sequences);

                SettlementOutcome outcome;
                try
                {
                    outcome = await _settlementService.ResettleSupersedingAsync(
                        conn, tx, command.EmployeeId, command.EntitlementType, command.EntitlementYear,
                        supersedingSequence, command.SupersedeGoLiveFloor, ct);
                }
                catch (SupersedingCarryoverConflictException ex)
                {
                    // The R4 supersede-side fail-closed guard — FULL rollback (no reversal either).
                    return await FailAsync(tx, SettlementReversalFailure.SupersedeCarryoverConflict,
                        ex.Message, ct);
                }

                if (!outcome.DidSettle || outcome.Row is null)
                {
                    // Ineligible against the CORRECTED in-tx state — the supersession leg FAILED
                    // ⇒ FULL rollback (R4: no partial states; parking REVERSED-without-successor
                    // happens ONLY via the explicit bare-reversal verb).
                    return await FailAsync(tx, SettlementReversalFailure.SupersedeNotEligible,
                        "The superseding settlement is not eligible against the corrected in-tx " +
                        "state (neither the TERMINATION due-predicate nor the ACTIVE YEAR_END " +
                        "boundary geometry holds) — the whole reversal rolled back; use the " +
                        "explicit bare-reversal mode to park the tuple instead.", ct);
                }
                successor = outcome.Row;
            }

            // (8) The R10 SettlementReversed emission + ADR-026 audit row, SAME tx. The audit
            // target org comes from the in-tx user re-read (post any correction).
            var user = await _userRepo.GetByIdIncludingTerminatedAsync(conn, tx, command.EmployeeId, ct)
                ?? throw new InvalidOperationException(
                    $"Settlement reversal: employee {command.EmployeeId} not found.");
            var snapshot = DeserializeSnapshot(reversed.SnapshotJson);
            var reversedEvent = new SettlementReversed
            {
                EmployeeId = command.EmployeeId,
                EntitlementType = command.EntitlementType,
                EntitlementYear = command.EntitlementYear,
                SettlementSequence = reversed.Sequence,
                ReversalKind = bare ? SettlementReversed.ReversalKindBare : SettlementReversed.ReversalKindSuperseded,
                SuccessorSequence = successor?.Sequence,
                Trigger = reversed.Trigger,
                Snapshot = snapshot,
                TransferDays = reversed.TransferDays,
                PayoutDays = reversed.PayoutDays,
                ForfeitDays = reversed.ForfeitDays,
                // CrystallizedDays: set on TERMINATION rows (whose buckets are all zero — the
                // quantity lives in the snapshot, SPRINT-70 R5); null elsewhere (R10).
                CrystallizedDays = string.Equals(reversed.Trigger, TerminationTrigger, StringComparison.Ordinal)
                    ? snapshot?.CrystallizedDays
                    : null,
                ClaimDispositionDays = reversed.ClaimDispositionDays,
                ActorId = command.ActorId,      // the OPERATOR — never a system actor (pinned)
                ActorRole = command.ActorRole,
                CorrelationId = command.CorrelationId,
            };
            // Operator-driven emission: ActorPrimaryOrgId = the operator's org (the
            // request-endpoint / lifecycle-writer convention), target = the employee's.
            await _settlementService.EmitAsync(
                conn, tx, $"employee-{command.EmployeeId}", reversedEvent, command.ActorId,
                command.ActorOrgId, user.PrimaryOrgId, ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Settlement reversal {Kind} committed for {EmployeeId}/{Type}/{Year} sequence {Sequence}" +
                " (successor: {Successor}; voided requests: {Voided}; actor {ActorId}).",
                reversedEvent.ReversalKind, command.EmployeeId, command.EntitlementType,
                command.EntitlementYear, reversed.Sequence,
                successor?.Sequence.ToString() ?? "none", voidedRequestIds.Count, command.ActorId);

            return new SettlementReversalResult
            {
                Succeeded = true,
                Failure = SettlementReversalFailure.None,
                ReversedRow = reversed,
                SupersedingRow = successor,
                VoidedRequestIds = voidedRequestIds,
                UserVersionAfter = userVersionAfter,
                UserIsActiveAfter = userIsActiveAfter,
            };
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ───────────────────────────── internals ─────────────────────────────

    private static void ValidateCommand(SettlementReversalCommand command)
    {
        if (command.Mode == SettlementReversalMode.Bare && command.HasEndDateCorrection)
        {
            throw new ArgumentException(
                "A bare reversal carries no end-date correction (the corrected-end-date input " +
                "belongs to the reverse+supersede mode).", nameof(command));
        }
        if (!command.HasEndDateCorrection && command.CorrectedEndDate is not null)
        {
            throw new ArgumentException(
                "CorrectedEndDate was supplied without HasEndDateCorrection — ambiguous " +
                "(null CorrectedEndDate WITH the flag means 'clear the end date').", nameof(command));
        }
        if (command.HasEndDateCorrection && command.ExpectedUserVersion is null)
        {
            throw new ArgumentException(
                "An end-date-correcting reversal carries BOTH expected versions (SPRINT-71 R4 " +
                "two-aggregate preconditions) — ExpectedUserVersion is required.", nameof(command));
        }
    }

    /// <summary>
    /// The state-only CAS: active row → REVERSED. WHERE re-asserts the caller's version
    /// (ADR-019) AND <c>settlement_state &lt;&gt; 'REVERSED'</c> (the state-transition
    /// predicate); 0 rows ⇒ a clean CAS loss (null). Touches ONLY <c>settlement_state</c>,
    /// the R3 marker (bare mode), <c>version</c> and <c>updated_at</c> — snapshot, buckets,
    /// <c>review_disposition</c> (DEFER history preserved) and the reconcile marker are
    /// untouched (the 7100 CHECKs enforce the legal combinations).
    /// </summary>
    private static async Task<VacationSettlementRow?> CasReverseAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationSettlementRow active, long expectedVersion, bool setBareMarker, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE vacation_settlements SET
                settlement_state = 'REVERSED',
                bare_reversal_not_due = @bareMarker,
                version = version + 1,
                updated_at = NOW()
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND sequence = @sequence
              AND version = @expectedVersion
              AND settlement_state <> 'REVERSED'
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("bareMarker", setBareMarker);
        cmd.Parameters.AddWithValue("employeeId", active.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", active.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", active.EntitlementYear);
        cmd.Parameters.AddWithValue("sequence", active.Sequence);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        if (scalar is null)
            return null;
        return active with
        {
            SettlementState = StateReversed,
            BareReversalNotDue = setBareMarker,
            Version = Convert.ToInt64(scalar),
        };
    }

    /// <summary>
    /// SPRINT-71 R1 — the next-generation settlement-ROW sequence, derived from the tuple's
    /// recorded row sequences. Settlement generation <c>g</c> uses row sequence <c>2g−1</c>
    /// (original=1, superseding=3, next=5 …; even sequences are EXPORT-side per R1/R2 and never
    /// appear on <c>vacation_settlements</c>). A new settlement ALWAYS allocates
    /// <c>g = (highest recorded generation) + 1</c> — never restarts at 1 — so a future
    /// post-bare-reversal revival's arithmetic is already settled (row seq 3 after a
    /// bare-reversed gen-1). Empty history (the from-1 scheme) ⇒ sequence 1. PURE; unit-pinned
    /// on both schemes.
    /// </summary>
    public static int NextGenerationRowSequence(IReadOnlyCollection<int> existingRowSequences)
    {
        if (existingRowSequences.Count == 0)
            return 1; // from-1: the first settlement of a virgin tuple (SettleAsync's path).
        var highestGeneration = existingRowSequences.Max(s => (s + 1) / 2);
        return 2 * (highestGeneration + 1) - 1;
    }

    /// <summary>
    /// B2 (Step-5a cycle-1) — the in-lock affected-span re-read: ALL active (non-REVERSED)
    /// settlement rows for the employee — ANY entitlement type (the R7a fail-closed
    /// convention) — across the FULL ferieår span of the end-date correction,
    /// <c>[min(ferieår(old), ferieår(new)) .. max(...)]</c> per the 9-pivot helper
    /// (<see cref="SettlementCloseService.ResolveLeaverFerieaar"/>). Runs AFTER the target
    /// row's CAS in the same tx, so the just-reversed row is self-excluded by the
    /// non-REVERSED predicate. A null date contributes no pivot (the
    /// <c>EmploymentDateEndpoints.AffectedFerieaar</c> convention); both null ⇒ empty span ⇒
    /// no blockers. Returns EVERY blocker so the fail-closed error can NAME them.
    /// </summary>
    private static async Task<IReadOnlyList<BlockingSettlementRow>> GetOtherActiveRowsInCorrectionSpanAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId,
        DateOnly? oldEndDate, DateOnly? newEndDate, CancellationToken ct)
    {
        var pivots = new List<int>(2);
        if (oldEndDate is { } oldDate)
            pivots.Add(SettlementCloseService.ResolveLeaverFerieaar(oldDate));
        if (newEndDate is { } newDate)
            pivots.Add(SettlementCloseService.ResolveLeaverFerieaar(newDate));
        if (pivots.Count == 0)
            return [];

        await using var cmd = new NpgsqlCommand(
            """
            SELECT entitlement_type, entitlement_year, sequence, settlement_state
            FROM vacation_settlements
            WHERE employee_id = @employeeId
              AND entitlement_year BETWEEN @spanLow AND @spanHigh
              AND settlement_state <> 'REVERSED'
            ORDER BY entitlement_year, entitlement_type, sequence
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("spanLow", pivots.Min());
        cmd.Parameters.AddWithValue("spanHigh", pivots.Max());
        var rows = new List<BlockingSettlementRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new BlockingSettlementRow(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
        }
        return rows;
    }

    private sealed record BlockingSettlementRow(
        string EntitlementType, int EntitlementYear, int Sequence, string State);

    private async Task<SettlementReversalResult> FailAsync(
        NpgsqlTransaction tx, SettlementReversalFailure failure, string reason, CancellationToken ct,
        long? actualSettlementVersion = null, long? actualUserVersion = null,
        int? actualSettlementSequence = null)
    {
        await tx.RollbackAsync(ct);
        _logger.LogInformation(
            "Settlement reversal refused ({Failure}): {Reason}", failure, reason);
        return SettlementReversalResult.Fail(
            failure, reason, actualSettlementVersion, actualUserVersion, actualSettlementSequence);
    }

    /// <summary>Tolerant snapshot read (the VacationSettlementEndpoints.DeserializeSnapshot
    /// shape) — a malformed stored snapshot must degrade to a null event payload, never throw.</summary>
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private static VacationSettlementSnapshot? DeserializeSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;
        try { return JsonSerializer.Deserialize<VacationSettlementSnapshot>(snapshotJson, SnapshotJsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Audit payload (the VacationSettlementEndpoints.SerializeSettlementForAudit field
    /// set + the S71 columns this service transitions/reads).</summary>
    private static string SerializeForAudit(VacationSettlementRow row) =>
        JsonSerializer.Serialize(new
        {
            row.EmployeeId,
            row.EntitlementType,
            row.EntitlementYear,
            row.Sequence,
            row.SettlementState,
            row.Trigger,
            row.TransferDays,
            row.PayoutDays,
            row.ForfeitDays,
            row.ReviewDisposition,
            row.ClaimDispositionDays,
            row.BareReversalNotDue,
            PayoutReconciledAt = row.PayoutReconciledAt,
            row.PayoutReconciledBy,
            row.Version,
        }, SnapshotJsonOptions);

    // ── Europe/Copenhagen business-date helper (the file-scoped convention; PAT-008 seam). ──

    private static readonly TimeZoneInfo CopenhagenZone = ResolveCopenhagenZone();

    private DateOnly CopenhagenToday()
    {
        var copenhagenNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), CopenhagenZone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
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
}

/// <summary>The operator-explicit reversal mode (SPRINT-71 R4 — the endpoint never infers it).</summary>
public enum SettlementReversalMode
{
    /// <summary>Bare reversal: REVERSED + the R3 durable not-due marker; TERMINAL in 3b.</summary>
    Bare,

    /// <summary>Reverse-then-re-settle: REVERSED + a superseding settlement at the R1
    /// next-generation sequence in the SAME tx (optionally with the subsumed end-date correction).</summary>
    ReverseAndSupersede,
}

/// <summary>The structured failure discriminator (409/412/403-shaped for TASK-7102's mapping).</summary>
public enum SettlementReversalFailure
{
    None = 0,

    /// <summary>No active (non-REVERSED) row exists for the tuple (409).</summary>
    NoActiveRow,

    /// <summary>B1/R2: the active row's sequence ≠ the commanded ExpectedSettlementSequence —
    /// the targeted generation was superseded (versions restart per generation; the ABA
    /// guard fires BEFORE any version comparison) (409).</summary>
    SequenceMismatch,

    /// <summary>D-A zero-bucket-only: the row wrote §21 carryover (transfer_days &gt; 0) (409).</summary>
    CarryoverWritingRow,

    /// <summary>R4 reconciled-row exclusion: the §24 disposition was operator-reconciled (409).</summary>
    ReconciledRow,

    /// <summary>The settlement-row CAS lost (stale If-Match or a concurrent transition) (409/412).</summary>
    CasConflict,

    /// <summary>R4 self-target exclusion: an end-date-mutating reversal with actor == employee (403).</summary>
    SelfTarget,

    /// <summary>The users.version precondition failed (the R4 two-aggregate second half) (412).</summary>
    UserVersionConflict,

    /// <summary>No users row exists at all (404).</summary>
    UserNotFound,

    /// <summary>B2 (the R13 analog): another ACTIVE settlement row stands in the corrected
    /// end-date ferieår span — the correction would leave it on superseded lifecycle facts;
    /// FULL rollback, the blockers named in the reason (409).</summary>
    AffectedSpanConflict,

    /// <summary>The supersession is ineligible against the corrected in-tx state — FULL rollback (409).</summary>
    SupersedeNotEligible,

    /// <summary>The R4 supersede-side carryover guard fired — FULL rollback (409).</summary>
    SupersedeCarryoverConflict,
}

/// <summary>
/// S71 / TASK-7104 — one reversal command (built by the TASK-7102 endpoint from the operator's
/// explicit request). <see cref="ExpectedSettlementSequence"/> is the settlement-row sequence
/// the operator's read was built against (B1/R2: the CAS binds the GENERATION — row versions
/// restart at 1 per generation, so the version alone cannot distinguish a superseded row from
/// its successor); <see cref="ExpectedSettlementVersion"/> is the settlement row's ADR-019
/// If-Match; <see cref="HasEndDateCorrection"/> + <see cref="CorrectedEndDate"/> express the R4
/// subsumed end-date mutation (the flag disambiguates "no change" from "clear the date" — null
/// WITH the flag clears, triggering the R1(c) provenance-guarded reactivation), which then
/// REQUIRES <see cref="ExpectedUserVersion"/> (the two-aggregate precondition).
/// <see cref="SupersedeGoLiveFloor"/> is the D13 settlement go-live date the caller supplies
/// (the close-service parity input to the trigger-specific eligibility predicates).
/// </summary>
public sealed record SettlementReversalCommand
{
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }

    /// <summary>B1/R2 — the settlement-row SEQUENCE this command was built against. Checked
    /// in-lock against the active row BEFORE any version comparison (the ABA guard).</summary>
    public required int ExpectedSettlementSequence { get; init; }
    public required long ExpectedSettlementVersion { get; init; }
    public required SettlementReversalMode Mode { get; init; }

    public bool HasEndDateCorrection { get; init; }
    public DateOnly? CorrectedEndDate { get; init; }
    public long? ExpectedUserVersion { get; init; }

    public DateOnly? SupersedeGoLiveFloor { get; init; }

    /// <summary>The OPERATOR (JWT actor) — stamped on the reversal audit trail + the
    /// SettlementReversed event (pinned: never a system actor).</summary>
    public required string ActorId { get; init; }
    public required string ActorRole { get; init; }
    public string? ActorOrgId { get; init; }
    public Guid? CorrelationId { get; init; }
}

/// <summary>
/// S71 / TASK-7104 — the structured reversal outcome. On failure the tx was FULLY rolled back
/// (nothing persisted — R4); <see cref="Failure"/> + <see cref="FailureReason"/> carry the
/// 409/412/403-shaped detail for the endpoint, with the actual versions where a precondition
/// lost. On success: the REVERSED row (state-only transition, snapshot preserved), the
/// superseding row when the mode produced one, the VOIDed request ids (R6/D-E) and the user
/// lifecycle outcome when an end-date correction was applied.
/// </summary>
public sealed record SettlementReversalResult
{
    public required bool Succeeded { get; init; }
    public required SettlementReversalFailure Failure { get; init; }
    public string? FailureReason { get; init; }
    public long? ActualSettlementVersion { get; init; }
    public long? ActualUserVersion { get; init; }

    /// <summary>The ACTIVE row's actual sequence when the B1 generation binding refused
    /// (<see cref="SettlementReversalFailure.SequenceMismatch"/>) — 7102's response detail.</summary>
    public int? ActualSettlementSequence { get; init; }

    public VacationSettlementRow? ReversedRow { get; init; }
    public VacationSettlementRow? SupersedingRow { get; init; }
    public IReadOnlyList<long> VoidedRequestIds { get; init; } = [];
    public long? UserVersionAfter { get; init; }
    public bool? UserIsActiveAfter { get; init; }

    public static SettlementReversalResult Fail(
        SettlementReversalFailure failure, string reason,
        long? actualSettlementVersion = null, long? actualUserVersion = null,
        int? actualSettlementSequence = null) =>
        new()
        {
            Succeeded = false,
            Failure = failure,
            FailureReason = reason,
            ActualSettlementVersion = actualSettlementVersion,
            ActualUserVersion = actualUserVersion,
            ActualSettlementSequence = actualSettlementSequence,
        };
}
