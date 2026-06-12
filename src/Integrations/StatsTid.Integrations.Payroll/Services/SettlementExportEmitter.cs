using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S69 / TASK-6904 + S71 / TASK-7105 (ADR-033 D4/D5/D6/D7/D13; SPRINT-71 R1/R2/R6/R7/R8/R9) — the
/// Payroll-context settlement-export consumer. A <see cref="BackgroundService"/> with ONE
/// type-discriminated poll over the canonical <c>events</c> table (ordered by
/// <c>global_position</c>) consuming THREE Backend-published settlement event types, staging
/// durable, money-free export lines + inbox checkpoints EXACTLY ONCE, replay-deterministically:
/// <list type="bullet">
///   <item><see cref="VacationAutoPaidOut"/> → the §24 auto-payout ORIGINAL line (slice 1b),
///     now reversal-AWARE (R9 retrofit: the under-lock settlement re-read skips a REVERSED row
///     with a terminal SKIPPED_VOIDED checkpoint — no orphan line);</item>
///   <item><see cref="TerminationPayoutRequested"/> → the §26 termination-payout ORIGINAL line
///     (<c>SLS_TBD_S26</c>, <c>hours = CrystallizedDays</c>) + the consumer-authoritative
///     request <c>OPEN → LINE_STAGED</c> promotion (R6), all one tx;</item>
///   <item><see cref="SettlementReversed"/> → one compensating REVERSAL line per staged ORIGINAL
///     line of the reversed settlement row (R8/R9), every bucket atomically in ONE tx (R7).</item>
/// </list>
///
/// <para>
/// <b>Naturally dormant pre-launch (ADR-033 D13).</b> The D13-gated Backend close emits no §24
/// events until <c>Settlement:GoLiveDate</c> passes; the §26/reversal events exist only via the
/// HROrAbove operator endpoints. Staging-only: NO staged line is ever delivered (the
/// <see cref="PayrollExportService"/> outbound guard refuses every <c>SLS_TBD_*</c> sentinel
/// unconditionally — R11 coverage tests pin this for <c>SLS_TBD_S26</c> too).
/// </para>
///
/// <para>
/// <b>Exactly-once (S69 Step-0b B1/C2-B1/C2-B2/C3-B1/C4-B1; SPRINT-71 R7).</b> The poll selects
/// only events with no terminal <see cref="SettlementInboxLineRepository"/> row (any bucket).
/// Each event is processed in ONE transaction under the ADR-032 D4 employee advisory lock
/// (acquired FIRST — the SAME key the Backend close service + the reversal service + the
/// reconcile endpoint take, R12). The success/skip path commits terminal inbox checkpoint(s) at
/// <c>(source_event_id, bucket)</c> by conditional PROMOTION; a failure rolls the whole tx back
/// (a multi-bucket event never commits a partial subset — R7) and a SEPARATE terminal-aware
/// diagnostics write records <c>attempts</c>/<c>last_error</c> at the event-level
/// <c>'_EVENT'</c> sentinel without ever overwriting a concurrent terminal — and (Step-5a
/// cycle-1 B1) NO-OPs entirely when a competing worker already completed the event, so a late
/// diagnostics write can never strand a non-terminal <c>'_EVENT'</c> row behind a finished
/// event. Inbox writes move MONOTONICALLY toward terminal, per bucket AND at event level.
/// </para>
///
/// <para>
/// <b>Fail-closed + replay-deterministic (S69 Step-0b B4/B5; SPRINT-71 R11).</b> Lønarter resolve
/// off the IMMUTABLE settle-time snapshot via the full ADR-020 dated natural key — §24 at
/// <c>asOf = Snapshot.SettlementBoundaryDate</c> from the event payload, §26 at
/// <c>asOf = TerminationPayoutRequested.SettlementBoundaryDate</c> (R11 — the employment end
/// date) with the key components read from the settlement ROW's immutable snapshot under the
/// lock. A missing snapshot / key datum / mapping produces NO line and fails closed
/// (RETRY_PENDING → DEAD_LETTER on budget), never a live, empty-key, or hard-coded fallback.
/// Lines are money-free: <c>hours</c> = a day-count, <c>amount = 0</c> (pinned in SQL), no rate
/// read (SLS owns the kroner). Compensating lines COPY the compensated line's
/// mapping/period/quantity (R8) — direction is <c>line_kind</c>, never a negative quantity.
/// </para>
///
/// <para>
/// <b>DI (registered in the Payroll <c>Program.cs</c>):</b>
/// <code>builder.Services.AddHostedService&lt;SettlementExportEmitter&gt;();</code>
/// Dependencies — <see cref="SettlementInboxLineRepository"/>,
/// <see cref="WageTypeMappingRepository"/>, and the logger — are registered alongside it.
/// (S71 adds NO new registration: the three consumers ride this one hosted service + the one
/// repository, keeping the poll globally ordered across types.)
/// </para>
/// </summary>
public sealed class SettlementExportEmitter : BackgroundService
{
    /// <summary>The §24 wage-type-mapping <c>time_type</c> (S69 TASK-6903 seed — maps to the SLS_TBD_S24 sentinel).</summary>
    private const string SettlementTimeType = "VACATION_SETTLEMENT_PAYOUT";

    /// <summary>The §26 wage-type-mapping <c>time_type</c> (S71 TASK-7105 R11 seed — maps to the SLS_TBD_S26 sentinel).</summary>
    private const string TerminationTimeType = "VACATION_TERMINATION_PAYOUT";

    /// <summary>The §24 auto-payout bucket — the line/inbox <c>bucket</c> axis (ADR-033 D4).</summary>
    private const string AutoPayoutBucket = "AUTO_PAYOUT_24";

    /// <summary>The §26 termination-payout bucket (S71 — the ADR-033 D2 §26 leg's line/inbox axis).</summary>
    private const string TerminationPayoutBucket = "TERMINATION_PAYOUT_26";

    /// <summary>The consumer identity recorded on staged lines' <c>created_by</c> (originals AND
    /// compensating reversal lines — one BackgroundService stages both).</summary>
    private const string EmitterActor = "settlement-export-emitter";

    /// <summary>
    /// Poll cadence. The work is idle when there are no events (and none exist pre-launch — D13), so a
    /// fixed delay is sufficient; a newly-published settlement event is picked up within one interval.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>Max events drained per poll — small; settlement events are rare.</summary>
    private const int BatchSize = 100;

    /// <summary>
    /// The durable retry budget: a RETRY_PENDING event transitions to terminal DEAD_LETTER once its
    /// persisted <c>attempts</c> reaches this (mirrors the External consumer's <c>MaxRetries = 10</c>).
    /// </summary>
    private const int MaxAttempts = 10;

    private static readonly System.Text.Json.JsonSerializerOptions SnapshotJsonOptions =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    private readonly SettlementInboxLineRepository _repo;
    private readonly WageTypeMappingRepository _wageTypeMappings;
    private readonly ILogger<SettlementExportEmitter> _logger;

    public SettlementExportEmitter(
        SettlementInboxLineRepository repo,
        WageTypeMappingRepository wageTypeMappings,
        ILogger<SettlementExportEmitter> logger)
    {
        _repo = repo;
        _wageTypeMappings = wageTypeMappings;
        _logger = logger;
    }

    /// <summary>
    /// SPRINT-71 R1/R2 — the compensating-line EXPORT sequence for a reversed settlement row.
    /// Settlement generation <c>g</c> uses settlement-ROW sequence <c>2g−1</c> (odd: 1, 3, 5 …);
    /// the compensating reversal lines for generation <c>g</c> use export sequence <c>2g</c>
    /// (even: 2, 4, 6 …) — i.e. <c>settlementRowSequence + 1</c>. ORIGINAL lines carry the
    /// settlement-row sequence itself (their export sequence EQUALS the odd row sequence); ONLY
    /// compensating lines take the even slot. PURE; unit-pinned.
    /// </summary>
    public static int ReversalExportSequence(int settlementRowSequence)
    {
        if (settlementRowSequence < 1 || settlementRowSequence % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settlementRowSequence), settlementRowSequence,
                "Settlement-row sequences are odd (2g−1) per SPRINT-71 R1 — even sequences are " +
                "export-side compensating slots and never appear on vacation_settlements.");
        }
        return settlementRowSequence + 1;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SettlementExportEmitter started — staging settlement export lines from VacationAutoPaidOut / " +
            "TerminationPayoutRequested / SettlementReversed (delivery disabled; SLS_TBD_* sentinels never leave the system).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A whole-poll failure must not crash the loop (mirrors SettlementCloseService) — a
                // missed event is re-selected next poll (its inbox row is absent or RETRY_PENDING).
                _logger.LogError(ex, "SettlementExportEmitter: error during settlement-export poll");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task DrainOnceAsync(CancellationToken ct)
    {
        var events = await _repo.GetUnconsumedSettlementEventsAsync(BatchSize, ct);
        if (events.Count == 0) return;

        _logger.LogInformation("SettlementExportEmitter: {Count} unconsumed settlement event(s) to stage", events.Count);

        foreach (var pending in events)
        {
            try
            {
                switch (pending.EventType)
                {
                    case "VacationAutoPaidOut":
                        await ProcessAutoPaidOutAsync(pending, ct);
                        break;
                    case "TerminationPayoutRequested":
                        await ProcessTerminationPayoutRequestedAsync(pending, ct);
                        break;
                    case "SettlementReversed":
                        await ProcessSettlementReversedAsync(pending, ct);
                        break;
                    default:
                        // Unreachable (the poll's event_type IN (...) filter) — log, never throw.
                        _logger.LogWarning(
                            "SettlementExportEmitter: poll returned unexpected event type {EventType} ({EventId}) — skipping.",
                            pending.EventType, pending.EventId);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A transient/unexpected failure for ONE event is isolated: record diagnostics
                // (terminal-aware, lock-re-acquired) and continue. The event was deserialized (its
                // identity is known); if we cannot even identify it, fall through to a poison dead-letter.
                await HandleFailureAsync(pending, ex, isCollision: false, ct);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // §24 — VacationAutoPaidOut (S69 slice 1b; S71 R9 reversal-awareness retrofit).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process ONE §24 event end-to-end. Deserializes, then runs the success/skip path under the
    /// employee advisory lock in ONE transaction; a deterministic validation failure (missing
    /// snapshot/key/mapping) or a line collision routes to the terminal-aware diagnostics write.
    /// Throws on a transient/unexpected fault so the caller records RETRY_PENDING diagnostics.
    /// </summary>
    private async Task ProcessAutoPaidOutAsync(SettlementInboxLineRepository.PendingEvent pending, CancellationToken ct)
    {
        // (−1) Poison guard (S69 Step-7a FIX 1 — the BLOCKER). A row whose payload cannot be
        //      deserialized has NO recoverable identity, so it can never be claimed/skipped/retried
        //      by the locked path below. Dead-letter it terminally at (source_event_id, '_EVENT')
        //      (no employee id ⇒ no advisory lock), so the poll then excludes it.
        VacationAutoPaidOut @event;
        try
        {
            @event = (VacationAutoPaidOut)EventSerializer.Deserialize(pending.EventType, pending.Data);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            await DeadLetterPoisonAsync(pending, deserEx, ct);
            return;
        }

        var identity = new SettlementIdentity(
            @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear, @event.Sequence, AutoPayoutBucket);

        // Open the locked tx (advisory lock FIRST, held to commit) for the success/skip path.
        var (conn, tx) = await _repo.BeginLockedAsync(identity.EmployeeId, ct);
        try
        {
            // (0) Terminal re-check UNDER THE LOCK (S69 Step-5a BLOCKER — the select→lock TOCTOU).
            //     Bucket-aware since S71: a terminal row at the §24 bucket OR a terminal event-level
            //     '_EVENT' row both finalize this event. Only absent/RETRY_PENDING proceeds.
            var terminal = await _repo.GetTerminalStatusAsync(conn, tx, pending.EventId, AutoPayoutBucket, ct);
            if (terminal is not null)
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: event {EventId} already {Status} (finalized between poll-selection " +
                    "and lock) — skipping.", pending.EventId, terminal);
                return;
            }

            // (0b) R9 RETROFIT (SPRINT-71 — the emitter-vs-reversal race, R12): re-read the
            //      settlement row at the EVENT's exact sequence under the lock. REVERSED ⇒ the row
            //      this event was emitted from has been compensated away — staging now would create
            //      a live, never-compensated ORPHAN line (the SettlementReversed consumer derives
            //      its targets from lines staged BEFORE the reversal; it cannot see this one).
            //      Stage NOTHING, write a terminal SKIPPED_VOIDED checkpoint. A MISSING row or any
            //      non-REVERSED state proceeds (preserves the S69 behavior byte-identically — the
            //      §24 event is the authority; the close service may legitimately have emitted
            //      before this consumer observes the row in another fixture shape).
            var rowProbe = await _repo.GetSettlementRowAsync(
                conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, identity.Sequence, ct);
            if (rowProbe is { State: "REVERSED" })
            {
                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "SKIPPED_VOIDED", ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: settlement {EmployeeId}/{Type}/{Year} seq {Sequence} is REVERSED — " +
                    "§24 event {EventId} SKIPPED_VOIDED (no line; R9 reversal-awareness).",
                    identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, identity.Sequence, pending.EventId);
                return;
            }

            // (1) Reconciled-skip (S69 B2): an operator already reconciled the §24 bucket ⇒ stage NO
            //     line, write a terminal SKIPPED_RECONCILED checkpoint, commit.
            if (await _repo.IsPayoutReconciledAsync(conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, identity.Sequence, ct))
            {
                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "SKIPPED_RECONCILED", ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: §24 bucket already reconciled for {EmployeeId}/{Type}/{Year} — SKIPPED_RECONCILED (no line).",
                    identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear);
                return;
            }

            // (2) Fail-closed validation (S69 B5) — a missing snapshot / agreement code / boundary date
            //     is a DETERMINISTIC failure (never a live/empty/hard-coded fallback).
            var snapshot = @event.Snapshot;
            if (snapshot is null
                || string.IsNullOrEmpty(snapshot.AgreementCode)
                || string.IsNullOrEmpty(snapshot.OkVersion)
                || snapshot.SettlementBoundaryDate == default)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    "VacationAutoPaidOut snapshot is missing a required wage-mapping key datum " +
                    "(snapshot/AgreementCode/OkVersion/SettlementBoundaryDate); cannot stage a §24 line (fail-closed, no fallback).",
                    ct);
                return;
            }

            // (3) Resolve the §24 lønart off the SNAPSHOT (replay-deterministic; no live lookup) — the full
            //     ADR-020 dated natural key at asOf = the captured settlement boundary date.
            var position = snapshot.Position ?? "";
            var mapping = await _wageTypeMappings.GetByKeyAtAsync(
                SettlementTimeType, snapshot.OkVersion!, snapshot.AgreementCode!, position, snapshot.SettlementBoundaryDate, ct);
            if (mapping is null)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    $"No §24 wage_type_mapping for (time_type={SettlementTimeType}, ok_version={snapshot.OkVersion}, " +
                    $"agreement_code={snapshot.AgreementCode}, position='{position}') as-of {snapshot.SettlementBoundaryDate:yyyy-MM-dd}; " +
                    "cannot stage a §24 line (fail-closed, no fallback).",
                    ct);
                return;
            }

            // (4) Build the money-free line (B5: hours = PayoutDays, amount pinned to 0 in SQL, NO rate read).
            var line = new SettlementExportLineInput
            {
                EmployeeId = identity.EmployeeId,
                EntitlementType = identity.EntitlementType,
                EntitlementYear = identity.EntitlementYear,
                Sequence = identity.Sequence,
                Bucket = AutoPayoutBucket,
                WageType = mapping.WageType,
                Hours = @event.PayoutDays,
                OkVersion = snapshot.OkVersion!,
                AgreementCode = snapshot.AgreementCode!,
                Position = position,
                PeriodStart = snapshot.SettlementBoundaryDate,
                PeriodEnd = snapshot.SettlementBoundaryDate,
                SourceEventId = pending.EventId,
                CreatedBy = EmitterActor,
            };

            // (5) Insert with verify-on-conflict (C2-B2; Step-5a cycle-1 B2: the FULL immutable
            //     shape is compared, not just the source event). A non-identical collision must NOT
            //     silently no-op — roll back, dead-letter, report the enumerated mismatches.
            var insert = await _repo.InsertLineAsync(conn, tx, line, ct);
            if (insert.Outcome == LineInsertOutcome.Collision)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleFailureAsync(pending, new InvalidOperationException(
                    $"settlement_export_lines bucket {AutoPayoutBucket} for {identity.EmployeeId}/{identity.EntitlementType}/" +
                    $"{identity.EntitlementYear} (seq {identity.Sequence}) already holds a line that does NOT match " +
                    $"event {pending.EventId}'s immutable line shape ({insert.CollisionDetail}); refusing to mask a " +
                    "settlement-line collision."),
                    isCollision: true, ct);
                return;
            }

            // (6) Promote the inbox to terminal PROCESSED (Inserted OR BenignRedelivery — same immutable
            //     event ⇒ same payload). Conditional promotion: promotes a RETRY_PENDING row left by a
            //     prior transient failure; idempotent no-op against an already-terminal row.
            await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "PROCESSED", ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "SettlementExportEmitter: staged §24 line for {EmployeeId}/{Type}/{Year} (seq {Sequence}): " +
                "{Days} day(s) → {WageType} (amount=0, money-free), as-of {AsOf:yyyy-MM-dd}; inbox PROCESSED ({Outcome}).",
                identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, identity.Sequence,
                @event.PayoutDays, mapping.WageType, snapshot.SettlementBoundaryDate, insert.Outcome);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct);
            throw; // transient/unexpected — caller records RETRY_PENDING diagnostics.
        }
        finally
        {
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // §26 — TerminationPayoutRequested (S71 R6: the request, not the settlement, drives the line).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process ONE §26 anmodning event. Under the employee lock in ONE tx (R6/R12): re-read the
    /// EXACT settlement row + the request row — request OPEN (or LINE_STAGED on a redelivery) +
    /// settlement active (SETTLED, same sequence) ⇒ stage the <c>SLS_TBD_S26</c> ORIGINAL line
    /// (<c>hours = CrystallizedDays</c>, export sequence = the odd settlement-row sequence per
    /// R1/R2) + checkpoint + promote the request <c>OPEN → LINE_STAGED</c> (consumer promotion is
    /// authoritative); request VOIDED or settlement REVERSED ⇒ NO line + a terminal
    /// <c>SKIPPED_VOIDED</c> checkpoint. The lønart resolves fail-closed off the settlement ROW's
    /// immutable snapshot key at <c>asOf = the event's SettlementBoundaryDate</c> (R11).
    /// </summary>
    private async Task ProcessTerminationPayoutRequestedAsync(
        SettlementInboxLineRepository.PendingEvent pending, CancellationToken ct)
    {
        TerminationPayoutRequested @event;
        try
        {
            @event = (TerminationPayoutRequested)EventSerializer.Deserialize(pending.EventType, pending.Data);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            await DeadLetterPoisonAsync(pending, deserEx, ct);
            return;
        }

        // R1/R2: an ORIGINAL line's export sequence IS the (odd) settlement-row sequence.
        var identity = new SettlementIdentity(
            @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
            @event.SettlementSequence, TerminationPayoutBucket);

        var (conn, tx) = await _repo.BeginLockedAsync(identity.EmployeeId, ct);
        try
        {
            // (0) Terminal re-check under the lock (bucket-aware; the S69 TOCTOU guard).
            var terminal = await _repo.GetTerminalStatusAsync(conn, tx, pending.EventId, TerminationPayoutBucket, ct);
            if (terminal is not null)
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: §26 event {EventId} already {Status} — skipping.",
                    pending.EventId, terminal);
                return;
            }

            // (1) R6 under-lock settlement re-read at the EXACT sequence the request targets.
            var rowProbe = await _repo.GetSettlementRowAsync(
                conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear,
                @event.SettlementSequence, ct);
            if (rowProbe is null)
            {
                // The request table FKs the settlement row, so a missing row is a broken contract —
                // fail closed (deterministic), never skip-as-voided.
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    $"No vacation_settlements row at ({identity.EmployeeId}, {identity.EntitlementType}, " +
                    $"{identity.EntitlementYear}, seq {@event.SettlementSequence}) for TerminationPayoutRequested " +
                    "(the request-table FK contract is broken); cannot stage a §26 line (fail-closed).",
                    ct);
                return;
            }
            if (string.Equals(rowProbe.Value.State, "REVERSED", StringComparison.Ordinal))
            {
                // The settlement was reversed after the request was recorded (R6/D-E: the reversal
                // VOIDs the request in the same Backend tx; this branch also covers the race where
                // the reversal committed between event publication and this consumption).
                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "SKIPPED_VOIDED", ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: settlement {EmployeeId}/{Type}/{Year} seq {Sequence} is REVERSED — " +
                    "§26 event {EventId} SKIPPED_VOIDED (no line).",
                    identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear,
                    @event.SettlementSequence, pending.EventId);
                return;
            }
            if (!string.Equals(rowProbe.Value.State, "SETTLED", StringComparison.Ordinal))
            {
                // A §26 request can only exist against a SETTLED TERMINATION row (the 7102 guards);
                // any other state here is an unexpected contract breach — fail closed.
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    $"Settlement row ({identity.EmployeeId}, {identity.EntitlementType}, {identity.EntitlementYear}, " +
                    $"seq {@event.SettlementSequence}) is '{rowProbe.Value.State}' (expected SETTLED) for " +
                    "TerminationPayoutRequested; cannot stage a §26 line (fail-closed).",
                    ct);
                return;
            }

            // (2) R6 under-lock request re-read (the consumer's authoritative state source).
            var requestProbe = await _repo.GetRequestProbeAsync(
                conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear,
                @event.SettlementSequence, ct);
            if (requestProbe.LiveState is null)
            {
                if (requestProbe.TotalCount > 0)
                {
                    // Every request for the row is VOIDED_BY_REVERSAL ⇒ stage nothing, terminal skip.
                    await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "SKIPPED_VOIDED", ct);
                    await tx.CommitAsync(ct);
                    _logger.LogInformation(
                        "SettlementExportEmitter: §26 request for {EmployeeId}/{Type}/{Year} seq {Sequence} is VOIDED — " +
                        "event {EventId} SKIPPED_VOIDED (no line).",
                        identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear,
                        @event.SettlementSequence, pending.EventId);
                    return;
                }

                // No request row AT ALL: the emitting endpoint writes the OPEN row + the event in one
                // tx, so this is a broken contract — fail closed (deterministic).
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    $"No termination_payout_requests row at ({identity.EmployeeId}, {identity.EntitlementType}, " +
                    $"{identity.EntitlementYear}, seq {@event.SettlementSequence}) for TerminationPayoutRequested " +
                    "(the endpoint writes the OPEN row + the event in one tx); cannot stage a §26 line (fail-closed).",
                    ct);
                return;
            }
            // LiveState is OPEN (first consumption) or LINE_STAGED (a redelivery whose checkpoint was
            // lost — the line insert below resolves as BenignRedelivery and the promotion no-ops):
            // proceed. Replay parity either way.

            // (3) Fail-closed key validation off the ROW's immutable snapshot (the event carries no
            //     wage-key components by design — the row snapshot is the replay-deterministic source).
            VacationSettlementSnapshot? snapshot;
            try
            {
                snapshot = System.Text.Json.JsonSerializer.Deserialize<VacationSettlementSnapshot>(
                    rowProbe.Value.SnapshotJson, SnapshotJsonOptions);
            }
            catch (System.Text.Json.JsonException)
            {
                snapshot = null;
            }
            if (snapshot is null
                || string.IsNullOrEmpty(snapshot.AgreementCode)
                || string.IsNullOrEmpty(snapshot.OkVersion)
                || @event.SettlementBoundaryDate == default)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    "TerminationPayoutRequested: the settlement row snapshot is missing a required wage-mapping " +
                    "key datum (snapshot/AgreementCode/OkVersion) or the event carries no SettlementBoundaryDate; " +
                    "cannot stage a §26 line (fail-closed, no fallback).",
                    ct);
                return;
            }

            // (4) Resolve the §26 lønart — the ADR-020 dated natural key at asOf = the EVENT's
            //     SettlementBoundaryDate (R11: for TERMINATION snapshots that IS the employment end
            //     date — the legally coherent anchor), key components from the row snapshot.
            var position = snapshot.Position ?? "";
            var mapping = await _wageTypeMappings.GetByKeyAtAsync(
                TerminationTimeType, snapshot.OkVersion!, snapshot.AgreementCode!, position,
                @event.SettlementBoundaryDate, ct);
            if (mapping is null)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleDeterministicFailureAsync(pending, identity,
                    $"No §26 wage_type_mapping for (time_type={TerminationTimeType}, ok_version={snapshot.OkVersion}, " +
                    $"agreement_code={snapshot.AgreementCode}, position='{position}') as-of {@event.SettlementBoundaryDate:yyyy-MM-dd}; " +
                    "cannot stage a §26 line (fail-closed, no fallback).",
                    ct);
                return;
            }

            // (5) Build the money-free §26 ORIGINAL line: hours = the EVENT's CrystallizedDays
            //     (snapshot-copied at request time — ADR-033 D3, never re-derived here).
            var line = new SettlementExportLineInput
            {
                EmployeeId = identity.EmployeeId,
                EntitlementType = identity.EntitlementType,
                EntitlementYear = identity.EntitlementYear,
                Sequence = @event.SettlementSequence,
                Bucket = TerminationPayoutBucket,
                WageType = mapping.WageType,
                Hours = @event.CrystallizedDays,
                OkVersion = snapshot.OkVersion!,
                AgreementCode = snapshot.AgreementCode!,
                Position = position,
                PeriodStart = @event.SettlementBoundaryDate,
                PeriodEnd = @event.SettlementBoundaryDate,
                SourceEventId = pending.EventId,
                CreatedBy = EmitterActor,
            };

            var insert = await _repo.InsertLineAsync(conn, tx, line, ct);
            if (insert.Outcome == LineInsertOutcome.Collision)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleFailureAsync(pending, new InvalidOperationException(
                    $"settlement_export_lines bucket {TerminationPayoutBucket} for {identity.EmployeeId}/" +
                    $"{identity.EntitlementType}/{identity.EntitlementYear} (seq {@event.SettlementSequence}) already " +
                    $"holds a line that does NOT match event {pending.EventId}'s immutable line shape " +
                    $"({insert.CollisionDetail}); refusing to mask a collision."),
                    isCollision: true, ct);
                return;
            }

            // (6) R6 — the consumer-authoritative request promotion OPEN → LINE_STAGED, SAME tx as
            //     the line + checkpoint. Conditional (idempotent on a LINE_STAGED redelivery).
            var promoted = await _repo.PromoteRequestToLineStagedAsync(
                conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear,
                @event.SettlementSequence, ct);

            // (7) Terminal PROCESSED checkpoint + commit (one atomic unit: line + request + inbox).
            await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "PROCESSED", ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "SettlementExportEmitter: staged §26 line for {EmployeeId}/{Type}/{Year} (seq {Sequence}): " +
                "{Days} day(s) → {WageType} (amount=0, money-free), as-of {AsOf:yyyy-MM-dd}; request promotion " +
                "rows={Promoted}; inbox PROCESSED ({Outcome}).",
                identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, @event.SettlementSequence,
                @event.CrystallizedDays, mapping.WageType, @event.SettlementBoundaryDate, promoted, insert.Outcome);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct);
            throw;
        }
        finally
        {
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Reversal — SettlementReversed (S71 R8/R9: compensating lines, never mutation).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process ONE reversal event. Under the employee lock in ONE tx (R9/R12): derive the
    /// compensation TARGETS from the Payroll-side staged ORIGINAL lines for the reversed
    /// settlement row (its own records — never the payload, never a live re-derivation); per
    /// target, stage a compensating line that COPIES the original's mapping/period/quantity with
    /// <c>line_kind = 'REVERSAL'</c> + <c>reverses_line_id</c> (R8) at the R1 EVEN export sequence
    /// <c>2g</c>, plus its <c>(source_event_id, bucket)</c> checkpoint — ALL buckets atomically
    /// (R7: a failure rolls the whole set back; no partial subset commits). No staged lines ⇒ a
    /// terminal no-op checkpoint at <c>(source_event_id, '_EVENT')</c> with status
    /// <c>PROCESSED</c> (DECLARED — the event was consumed; there was nothing to compensate).
    /// Originals are NEVER mutated or deleted.
    /// </summary>
    private async Task ProcessSettlementReversedAsync(
        SettlementInboxLineRepository.PendingEvent pending, CancellationToken ct)
    {
        SettlementReversed @event;
        try
        {
            @event = (SettlementReversed)EventSerializer.Deserialize(pending.EventType, pending.Data);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            await DeadLetterPoisonAsync(pending, deserEx, ct);
            return;
        }

        // R1/R2: compensating lines for generation g = (seq+1)/2 take the EVEN export sequence 2g.
        var exportSequence = ReversalExportSequence(@event.SettlementSequence);
        var eventIdentity = new SettlementIdentity(
            @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
            exportSequence, SettlementInboxLineRepository.EventLevelBucket);

        var (conn, tx) = await _repo.BeginLockedAsync(@event.EmployeeId, ct);
        try
        {
            // (0) EVENT-LEVEL terminal re-check under the lock: ANY terminal row finalizes this
            //     event (sound under R7 — the per-bucket checkpoints only ever commit as a
            //     complete atomic set, so one terminal bucket row implies all of them).
            var terminal = await _repo.GetTerminalStatusAsync(conn, tx, pending.EventId, bucket: null, ct);
            if (terminal is not null)
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: SettlementReversed event {EventId} already {Status} — skipping.",
                    pending.EventId, terminal);
                return;
            }

            // (1) R9 — compensation targets from the Payroll context's OWN staged ORIGINAL lines
            //     for the reversed row (identity + the odd settlement-row sequence).
            var targets = await _repo.GetOriginalLinesForSettlementAsync(
                conn, tx, @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
                @event.SettlementSequence, ct);

            if (targets.Count == 0)
            {
                // Nothing was ever staged for this row (the common case: the reversal won the race —
                // the §24/§26 consumers' R9/R6 under-lock re-checks will SKIPPED_VOIDED their events).
                // Terminal no-op checkpoint at (event, '_EVENT'), status PROCESSED (declared).
                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, eventIdentity, "PROCESSED", ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: SettlementReversed {EmployeeId}/{Type}/{Year} seq {Sequence} — no staged " +
                    "lines to compensate; event {EventId} checkpointed PROCESSED at '_EVENT' (no-op).",
                    @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
                    @event.SettlementSequence, pending.EventId);
                return;
            }

            // (2) One compensating line + checkpoint per staged bucket — ALL in THIS one tx (R7).
            foreach (var target in targets)
            {
                var line = new SettlementExportLineInput
                {
                    EmployeeId = @event.EmployeeId,
                    EntitlementType = @event.EntitlementType,
                    EntitlementYear = @event.EntitlementYear,
                    Sequence = exportSequence,
                    Bucket = target.Bucket,
                    WageType = target.WageType,           // R8: copy the compensated line's mapping…
                    Hours = target.Hours,                 // …and quantity (positive; direction = line_kind)
                    OkVersion = target.OkVersion,
                    AgreementCode = target.AgreementCode,
                    Position = target.Position,
                    PeriodStart = target.PeriodStart,     // …and period
                    PeriodEnd = target.PeriodEnd,
                    SourceEventId = pending.EventId,
                    CreatedBy = EmitterActor,
                    LineKind = "REVERSAL",
                    ReversesLineId = target.LineId,       // R8: the FK is the unambiguous reference
                };

                var insert = await _repo.InsertLineAsync(conn, tx, line, ct);
                if (insert.Outcome == LineInsertOutcome.Collision)
                {
                    // FULL rollback (R7 — no partial multi-bucket subset may stand), then dead-letter.
                    // Step-5a cycle-1 B2: this also catches a SAME-event row whose immutable shape
                    // deviates (e.g. a wrong reverses_line_id) — never PROCESSED, mismatches reported.
                    await SafeRollbackAsync(tx, ct);
                    await HandleFailureAsync(pending, new InvalidOperationException(
                        $"settlement_export_lines ({@event.EmployeeId}/{@event.EntitlementType}/{@event.EntitlementYear}, " +
                        $"export seq {exportSequence}, bucket {target.Bucket}) already holds a line that does NOT match " +
                        $"event {pending.EventId}'s immutable compensating-line shape ({insert.CollisionDetail}); " +
                        "refusing to mask a compensating-line collision (whole multi-bucket set rolled back)."),
                        isCollision: true, ct);
                    return;
                }

                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId,
                    new SettlementIdentity(@event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
                        exportSequence, target.Bucket),
                    "PROCESSED", ct);
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "SettlementExportEmitter: compensated {Count} staged line(s) for reversed settlement " +
                "{EmployeeId}/{Type}/{Year} seq {Sequence} (export seq {ExportSequence}, kind {Kind}); " +
                "all checkpoints PROCESSED in one tx.",
                targets.Count, @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear,
                @event.SettlementSequence, exportSequence, @event.ReversalKind);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct);
            throw; // transient — full rollback already removed every partial write; '_EVENT' RETRY_PENDING follows.
        }
        finally
        {
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Shared failure machinery (S69 Step-7a FIX 1 / Step-5a P2; S71 type-generalized).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A DETERMINISTIC validation failure (missing snapshot/key/mapping/contract row). The identity
    /// is known, so the diagnostics write is terminal-aware (RETRY_PENDING at '_EVENT', or
    /// DEAD_LETTER on budget). Logged at Warning.
    /// </summary>
    private async Task HandleDeterministicFailureAsync(
        SettlementInboxLineRepository.PendingEvent pending, SettlementIdentity identity, string error, CancellationToken ct)
    {
        _logger.LogWarning(
            "SettlementExportEmitter: deterministic failure staging event {EventId} ({EventType}) for {EmployeeId}/{Type}/{Year}: {Error}",
            pending.EventId, pending.EventType, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, error);
        await RecordDiagnosticsAsync(pending, identity, error, isCollision: false, ct);
    }

    /// <summary>
    /// A transient/unexpected/collision failure where we may or may not already know the identity.
    /// If the event deserializes (type-discriminated), the diagnostics row carries the identity;
    /// otherwise dead-letter it as poison at <c>(source_event_id, '_EVENT')</c>. A collision
    /// dead-letters immediately (non-self-healing).
    /// </summary>
    private async Task HandleFailureAsync(
        SettlementInboxLineRepository.PendingEvent pending, Exception ex, bool isCollision, CancellationToken ct)
    {
        SettlementIdentity identity;
        try
        {
            identity = ResolveIdentity(pending);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            // Cannot identify the event — no coherent identity-keyed inbox row to write. Same poison
            // case as the per-processor (−1) guard (a deserialize that succeeded there could only
            // fail here under genuine corruption mid-drain): dead-letter terminally at '_EVENT'
            // rather than log-and-skip — a bare skip would leave the poll re-selecting it forever.
            _logger.LogError(deserEx,
                "SettlementExportEmitter: could not deserialize event {EventId} ({EventType}) to record diagnostics; " +
                "dead-lettering at '_EVENT'. Original error: {Original}", pending.EventId, pending.EventType, ex.Message);
            try
            {
                await _repo.DeadLetterPoisonEventAsync(pending.EventId, deserEx.Message, ct);
            }
            catch (Exception dlEx) when (dlEx is not OperationCanceledException)
            {
                _logger.LogWarning(dlEx,
                    "SettlementExportEmitter: failed to dead-letter poison event {EventId}; will retry on next poll.",
                    pending.EventId);
            }
            return;
        }

        if (isCollision)
            _logger.LogError(ex, "SettlementExportEmitter: settlement-line COLLISION for event {EventId} — DEAD_LETTER", pending.EventId);
        else
            _logger.LogWarning(ex, "SettlementExportEmitter: transient failure staging event {EventId} — recording diagnostics", pending.EventId);

        await RecordDiagnosticsAsync(pending, identity, ex.Message, isCollision, ct);
    }

    /// <summary>
    /// Performs the terminal-aware (lock-re-acquired) diagnostics write at the event-level
    /// <c>'_EVENT'</c> sentinel (R7). The RETRY_PENDING-vs-DEAD_LETTER decision is computed
    /// ATOMICALLY SERVER-SIDE inside the locked upsert off the post-increment <c>attempts</c>
    /// (S69 Step-5a P2): a collision is a non-self-healing failure ⇒ DEAD_LETTER immediately;
    /// otherwise the row stays RETRY_PENDING until the incremented <c>attempts</c> reaches
    /// <see cref="MaxAttempts"/>, then DEAD_LETTER. Never overwrites a committed terminal.
    /// </summary>
    private async Task RecordDiagnosticsAsync(
        SettlementInboxLineRepository.PendingEvent pending, SettlementIdentity identity, string error, bool isCollision, CancellationToken ct)
    {
        try
        {
            var written = await _repo.WriteDiagnosticsAsync(pending.EventId, identity, isCollision, MaxAttempts, error, ct);
            if (!written)
            {
                // SPRINT-71 Step-5a cycle-1 B1: a competing worker COMPLETED the event between this
                // worker's rollback and the re-locked diagnostics write — the terminal row already
                // covers the event, so the late diagnostics were a deliberate no-op (writing them
                // would strand a non-terminal '_EVENT' row forever).
                _logger.LogInformation(
                    "SettlementExportEmitter: diagnostics for event {EventId} skipped — the event was completed " +
                    "by a competing worker (terminal inbox row present); late diagnostics are moot.",
                    pending.EventId);
            }
        }
        catch (Exception diagEx) when (diagEx is not OperationCanceledException)
        {
            // The diagnostics write itself failed (e.g. DB blip). Non-fatal: the event re-selects next
            // poll (its inbox row is absent or still RETRY_PENDING) and is retried.
            _logger.LogWarning(diagEx,
                "SettlementExportEmitter: failed to record diagnostics for event {EventId}; will retry on next poll.",
                pending.EventId);
        }
    }

    /// <summary>Dead-letters an un-deserializable (poison) event at <c>(source_event_id, '_EVENT')</c>
    /// — no recoverable identity ⇒ no advisory lock (S69 Step-7a FIX 1).</summary>
    private async Task DeadLetterPoisonAsync(
        SettlementInboxLineRepository.PendingEvent pending, Exception deserEx, CancellationToken ct)
    {
        _logger.LogError(deserEx,
            "SettlementExportEmitter: POISON event {EventId} — {EventType} payload could not be deserialized; " +
            "dead-lettering at '_EVENT' (no recoverable identity, no advisory lock).",
            pending.EventId, pending.EventType);
        await _repo.DeadLetterPoisonEventAsync(pending.EventId, deserEx.Message, ct);
    }

    /// <summary>
    /// Re-derives the diagnostics identity for a failed event, type-discriminated. The
    /// <c>Sequence</c> is the EXPORT sequence the consumer would have keyed its writes at (R2);
    /// the <c>Bucket</c> field is informational (diagnostics key at '_EVENT' regardless).
    /// Throws when the payload cannot be deserialized (the caller poisons it).
    /// </summary>
    private static SettlementIdentity ResolveIdentity(SettlementInboxLineRepository.PendingEvent pending)
    {
        switch (pending.EventType)
        {
            case "VacationAutoPaidOut":
            {
                var e = (VacationAutoPaidOut)EventSerializer.Deserialize(pending.EventType, pending.Data);
                return new SettlementIdentity(e.EmployeeId, e.EntitlementType, e.EntitlementYear, e.Sequence, AutoPayoutBucket);
            }
            case "TerminationPayoutRequested":
            {
                var e = (TerminationPayoutRequested)EventSerializer.Deserialize(pending.EventType, pending.Data);
                return new SettlementIdentity(e.EmployeeId, e.EntitlementType, e.EntitlementYear, e.SettlementSequence, TerminationPayoutBucket);
            }
            case "SettlementReversed":
            {
                var e = (SettlementReversed)EventSerializer.Deserialize(pending.EventType, pending.Data);
                return new SettlementIdentity(e.EmployeeId, e.EntitlementType, e.EntitlementYear,
                    ReversalExportSequence(e.SettlementSequence), SettlementInboxLineRepository.EventLevelBucket);
            }
            default:
                throw new InvalidOperationException(
                    $"SettlementExportEmitter: no identity resolver for event type '{pending.EventType}'.");
        }
    }

    private async Task SafeRollbackAsync(Npgsql.NpgsqlTransaction tx, CancellationToken ct)
    {
        try
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A rollback failure (tx already aborted) is non-fatal — the connection is disposed by the
            // caller's finally regardless. Swallowed so the drain loop continues.
            _logger.LogDebug(ex, "SettlementExportEmitter: rollback no-op (tx already closed).");
        }
    }
}
