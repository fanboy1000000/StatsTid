using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S69 / TASK-6904 (ADR-033 D4/D5/D6/D7/D13) — the Payroll-context §24 vacation-auto-payout
/// settlement-export emitter. A <see cref="BackgroundService"/> that consumes the Backend-published
/// <see cref="VacationAutoPaidOut"/> events from the canonical <c>events</c> table and stages a
/// durable, money-free §24 export line + an inbox checkpoint, EXACTLY ONCE, replay-deterministically.
///
/// <para>
/// <b>Naturally dormant pre-launch (ADR-033 D13).</b> The D13-gated Backend
/// <see cref="StatsTid.Infrastructure.SettlementCloseService"/> emits NO <see cref="VacationAutoPaidOut"/>
/// events until <c>Settlement:GoLiveDate</c> is configured and a 31-Dec boundary passes, so this emitter
/// polls and finds nothing pre-launch. It is staging-only: the staged line is NEVER delivered externally
/// this sprint (TASK-6906 disables settlement-line delivery + refuses the sentinel lønart at the outbound
/// boundary).
/// </para>
///
/// <para>
/// <b>Exactly-once (Step-0b B1/C2-B1/C2-B2/C3-B1/C4-B1).</b> The poll selects only events with no
/// terminal <see cref="StatsTid.Integrations.Payroll.Services.SettlementInboxLineRepository"/> row.
/// Each event is processed in ONE transaction under the ADR-032 D4 employee advisory lock (acquired
/// FIRST — the SAME key the Backend close service + the reconcile endpoint take, so emitter-claim and
/// reconcile are mutually exclusive). The success/skip path commits a terminal inbox status (+ the line,
/// unless skipped) by a conditional PROMOTION; a failure rolls that tx back and a SEPARATE terminal-aware
/// diagnostics write records <c>attempts</c>/<c>last_error</c> without ever overwriting a concurrent
/// terminal. Inbox writes move MONOTONICALLY toward terminal.
/// </para>
///
/// <para>
/// <b>Fail-closed + replay-deterministic (Step-0b B4/B5).</b> The §24 lønart is resolved off the
/// IMMUTABLE snapshot via the full ADR-020 dated natural key at <c>asOf =
/// Snapshot.SettlementBoundaryDate</c> — NO live lookup. A missing snapshot / missing key datum / null
/// mapping produces NO line and fails closed (RETRY_PENDING → DEAD_LETTER on budget), never a live,
/// empty-key, or hard-coded fallback. The line is money-free: <c>hours = PayoutDays</c>, <c>amount = 0</c>
/// (pinned in SQL), no rate read (SLS owns the kroner).
/// </para>
///
/// <para>
/// <b>DI (registered in the Payroll <c>Program.cs</c>):</b>
/// <code>builder.Services.AddHostedService&lt;SettlementExportEmitter&gt;();</code>
/// Dependencies — <see cref="SettlementInboxLineRepository"/>,
/// <see cref="WageTypeMappingRepository"/>, and the logger — are registered alongside it.
/// </para>
/// </summary>
public sealed class SettlementExportEmitter : BackgroundService
{
    /// <summary>The §24 wage-type-mapping <c>time_type</c> (TASK-6903 seed — maps to the SLS_TBD_S24 sentinel).</summary>
    private const string SettlementTimeType = "VACATION_SETTLEMENT_PAYOUT";

    /// <summary>The §24 auto-payout bucket — the line/inbox <c>bucket</c> axis (ADR-033 D4; reserves later slices).</summary>
    private const string AutoPayoutBucket = "AUTO_PAYOUT_24";

    /// <summary>The emitter identity recorded on the staged line's <c>created_by</c>.</summary>
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SettlementExportEmitter started — staging §24 auto-payout lines from VacationAutoPaidOut " +
            "(dormant until the D13-gated close emits events; delivery disabled this sprint).");

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
        var events = await _repo.GetUnconsumedAutoPaidOutEventsAsync(BatchSize, ct);
        if (events.Count == 0) return;

        _logger.LogInformation("SettlementExportEmitter: {Count} unconsumed VacationAutoPaidOut event(s) to stage", events.Count);

        foreach (var pending in events)
        {
            try
            {
                await ProcessEventAsync(pending, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A transient/unexpected failure for ONE event is isolated: record diagnostics
                // (terminal-aware, lock-re-acquired) and continue. The event was deserialized (its
                // identity is known); if we cannot even identify it, fall through to a log-only skip.
                await HandleFailureAsync(pending, ex, isCollision: false, ct);
            }
        }
    }

    /// <summary>
    /// Process ONE event end-to-end. Deserializes, then runs the success/skip path under the employee
    /// advisory lock in ONE transaction; a deterministic validation failure (missing snapshot/key/mapping)
    /// or a line collision routes to the terminal-aware diagnostics write. Throws on a transient/unexpected
    /// fault so the caller records RETRY_PENDING diagnostics.
    /// </summary>
    private async Task ProcessEventAsync(SettlementInboxLineRepository.PendingEvent pending, CancellationToken ct)
    {
        // (−1) Poison guard (Step-7a FIX 1 — the BLOCKER). A VacationAutoPaidOut row whose payload cannot
        //      be deserialized has NO recoverable identity, so it can never be claimed/skipped/retried by
        //      the locked path below. Previously the throw bubbled to a log-only skip that wrote NO inbox
        //      checkpoint, so the poll re-selected the poison event every 30s FOREVER (the promised
        //      poison→DEAD_LETTER lifecycle never landed). Dead-letter it terminally here keyed by
        //      source_event_id ONLY (no employee id ⇒ no advisory lock), so the poll then excludes it and
        //      the consumer is unstalled. This is rare — the SQL event_type filter matched the type, but
        //      a malformed body (corruption / a schema drift) is still possible.
        VacationAutoPaidOut @event;
        try
        {
            @event = DeserializeAutoPaidOut(pending);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            _logger.LogError(deserEx,
                "SettlementExportEmitter: POISON event {EventId} — VacationAutoPaidOut payload could not be " +
                "deserialized; dead-lettering (no recoverable identity, no advisory lock).", pending.EventId);
            await _repo.DeadLetterPoisonEventAsync(pending.EventId, deserEx.Message, ct);
            return;
        }

        var identity = new SettlementIdentity(
            @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear, @event.Sequence, AutoPayoutBucket);

        // Open the locked tx (advisory lock FIRST, held to commit) for the success/skip path.
        var (conn, tx) = await _repo.BeginLockedAsync(identity.EmployeeId, ct);
        try
        {
            // (0) Terminal re-check UNDER THE LOCK (Step-5a BLOCKER — the select→lock TOCTOU). The poll
            //     selection filtered terminal rows, but it ran OUTSIDE this advisory lock: a concurrent
            //     worker may have finalized this event (PROCESSED / SKIPPED_RECONCILED / DEAD_LETTER)
            //     between selection and lock-acquisition. Re-read the inbox status now; if already
            //     terminal, do nothing and commit (skip) — staging a line here would pair it with a
            //     terminal (e.g. DEAD_LETTER) checkpoint that the terminal-aware PROMOTE then leaves
            //     un-promoted (0 rows). Only an absent or RETRY_PENDING row proceeds to stage.
            var inboxStatus = await _repo.GetInboxStatusAsync(conn, tx, pending.EventId, ct);
            if (inboxStatus is "PROCESSED" or "SKIPPED_RECONCILED" or "DEAD_LETTER")
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: event {EventId} already {Status} (concurrent worker finalized it " +
                    "between poll-selection and lock) — skipping.", pending.EventId, inboxStatus);
                return;
            }

            // (1) Reconciled-skip (B2): an operator already reconciled the §24 bucket ⇒ stage NO line,
            //     write a terminal SKIPPED_RECONCILED checkpoint, commit.
            if (await _repo.IsPayoutReconciledAsync(conn, tx, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, identity.Sequence, ct))
            {
                await _repo.PromoteToTerminalAsync(conn, tx, pending.EventId, identity, "SKIPPED_RECONCILED", ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "SettlementExportEmitter: §24 bucket already reconciled for {EmployeeId}/{Type}/{Year} — SKIPPED_RECONCILED (no line).",
                    identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear);
                return;
            }

            // (2) Fail-closed validation (B5) — a missing snapshot / agreement code / boundary date is a
            //     DETERMINISTIC failure (never a live/empty/hard-coded fallback). Roll back, record
            //     diagnostics terminal-aware.
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
            //     period_start = period_end = the settlement boundary date (provisional — the SLS line format
            //     is unverified). The line carries the full wage-type natural-key components from the snapshot.
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

            // (5) Insert with verify-on-conflict (C2-B2). A non-identical collision must NOT silently
            //     no-op — roll back, dead-letter, report.
            var outcome = await _repo.InsertLineAsync(conn, tx, line, ct);
            if (outcome == LineInsertOutcome.Collision)
            {
                await SafeRollbackAsync(tx, ct);
                await HandleFailureAsync(pending, new InvalidOperationException(
                    $"settlement_export_lines bucket {AutoPayoutBucket} for {identity.EmployeeId}/{identity.EntitlementType}/" +
                    $"{identity.EntitlementYear} (seq {identity.Sequence}) already holds a line from a DIFFERENT source event " +
                    $"than {pending.EventId}; refusing to mask a settlement-line collision."),
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
                @event.PayoutDays, mapping.WageType, snapshot.SettlementBoundaryDate, outcome);
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

    /// <summary>
    /// A DETERMINISTIC validation failure (missing snapshot/key/mapping). The identity is known, so the
    /// diagnostics write is terminal-aware (RETRY_PENDING, or DEAD_LETTER on budget). Logged at Warning.
    /// </summary>
    private async Task HandleDeterministicFailureAsync(
        SettlementInboxLineRepository.PendingEvent pending, SettlementIdentity identity, string error, CancellationToken ct)
    {
        _logger.LogWarning(
            "SettlementExportEmitter: deterministic failure staging event {EventId} for {EmployeeId}/{Type}/{Year}: {Error}",
            pending.EventId, identity.EmployeeId, identity.EntitlementType, identity.EntitlementYear, error);
        await RecordDiagnosticsAsync(pending, identity, error, isCollision: false, ct);
    }

    /// <summary>
    /// A transient/unexpected/collision failure where we may or may not already know the identity. If the
    /// event deserializes, the diagnostics row carries the identity; otherwise we log-and-skip (it will be
    /// re-selected next poll — its inbox row is absent). A collision dead-letters immediately.
    /// </summary>
    private async Task HandleFailureAsync(
        SettlementInboxLineRepository.PendingEvent pending, Exception ex, bool isCollision, CancellationToken ct)
    {
        SettlementIdentity identity;
        try
        {
            var @event = DeserializeAutoPaidOut(pending);
            identity = new SettlementIdentity(
                @event.EmployeeId, @event.EntitlementType, @event.EntitlementYear, @event.Sequence, AutoPayoutBucket);
        }
        catch (Exception deserEx) when (deserEx is not OperationCanceledException)
        {
            // Cannot identify the event — no coherent identity-keyed inbox row to write. This is the same
            // poison case the ProcessEventAsync (−1) guard already handles (a deserialize that succeeded
            // there could only fail here under genuine corruption mid-drain), so dead-letter it terminally
            // keyed by source_event_id ONLY (Step-7a FIX 1) rather than log-and-skip — a bare skip would
            // leave the poll re-selecting it every poll forever.
            _logger.LogError(deserEx,
                "SettlementExportEmitter: could not deserialize VacationAutoPaidOut event {EventId} to record diagnostics; " +
                "dead-lettering by source_event_id. Original error: {Original}", pending.EventId, ex.Message);
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
    /// Performs the terminal-aware (lock-re-acquired) diagnostics write. The RETRY_PENDING-vs-DEAD_LETTER
    /// decision is computed ATOMICALLY SERVER-SIDE inside the locked upsert off the post-increment
    /// <c>attempts</c> (Step-5a WARNING P2 — no unlocked pre-read drives the status): a collision is a
    /// non-self-healing failure ⇒ <c>forceDeadLetter</c> = DEAD_LETTER immediately; otherwise the row
    /// stays RETRY_PENDING until the incremented <c>attempts</c> reaches <see cref="MaxAttempts"/>, then
    /// DEAD_LETTER. The write never overwrites a concurrently-committed terminal status (the
    /// <c>WHERE … = 'RETRY_PENDING'</c> guard).
    /// </summary>
    private async Task RecordDiagnosticsAsync(
        SettlementInboxLineRepository.PendingEvent pending, SettlementIdentity identity, string error, bool isCollision, CancellationToken ct)
    {
        try
        {
            // isCollision ⇒ forceDeadLetter (the non-self-healing case dead-letters at once); otherwise
            // the server-side CASE flips to DEAD_LETTER once the post-increment attempts reaches MaxAttempts.
            await _repo.WriteDiagnosticsAsync(pending.EventId, identity, isCollision, MaxAttempts, error, ct);
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

    private static VacationAutoPaidOut DeserializeAutoPaidOut(SettlementInboxLineRepository.PendingEvent pending)
    {
        var domainEvent = EventSerializer.Deserialize("VacationAutoPaidOut", pending.Data);
        return (VacationAutoPaidOut)domainEvent;
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
