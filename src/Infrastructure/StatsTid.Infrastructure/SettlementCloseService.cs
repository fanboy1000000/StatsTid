using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6805 (ADR-033 D3) — the deterministic, idempotent vacation period-close
/// BackgroundService. On the <see cref="DelegationExpiryService"/> 5-minute poll cadence it
/// detects every due <c>(employee, VACATION, closed-entitlement-year)</c> tuple — a closed ferieår
/// whose §21/§24 ferieafholdelsesperiode boundary (≈ 31 Dec of the year AFTER the ferieår closes)
/// has passed on the <b>Europe/Copenhagen business date</b> — and runs the atomic settlement pass
/// (<see cref="VacationSettlementService.SettleAsync"/>) once per due tuple, each in its own
/// transaction.
///
/// <para>
/// <b>Trigger-vs-quantity determinism (ADR-033 D3, priority 2/4).</b> The service decides only WHICH
/// tuples are due and WHEN to check — it carries NO settled quantity. The settled quantity is
/// <see cref="VacationSettlementService.SettleAsync"/>'s concern (a pure function of the immutable
/// snapshot it captures). The "is it time to check yet" poll trigger MAY read the wall clock
/// (acceptable per ADR-033 D3 — a trigger, not a replayed quantity); the per-tuple due boundary,
/// however, is derived from the injected <see cref="TimeProvider"/> converted to the
/// Europe/Copenhagen calendar (NEVER raw <c>CURRENT_DATE</c>; Codex Step-0b W4 + ADR-033 D3
/// boundary-timezone / follow-up (v)), so the close fires on the Danish business boundary, not a
/// UTC-midnight boundary that could be ±1 day off near year-end.
/// </para>
///
/// <para>
/// <b>Enumeration is employee/entitlement-config-driven, NOT balance-driven (Codex Step-0b W4).</b>
/// The due set is generated from the active-employee set × their resolved VACATION
/// <c>reset_month</c> geometry, so an employee with NO <c>entitlement_balances</c> row for a closed
/// year is still settled (a configless/empty year still settles to the same zero-state disposition
/// the S66 D9 row renders). The cheap anti-join against active <c>vacation_settlements</c> rows
/// (<c>SETTLED</c>/<c>PENDING_REVIEW</c>) pre-skips already-settled tuples; the in-lock re-check
/// inside <see cref="VacationSettlementService.SettleAsync"/> + the D5 partial-unique key are the
/// correctness backstop (a concurrent poller / missed-then-late poll still settles EXACTLY once).
/// </para>
///
/// <para>
/// <b>Launch-neutral go-live gate (ADR-033 D13; S68 fix-forward).</b> The automated year-end close is
/// bound to <b>"first boundary: 31 Dec AFTER launch; manual fallback until then"</b> — it settles ONLY
/// entitlement-years whose §21/§24 boundary (31 Dec E+1) falls STRICTLY AFTER the configured settlement
/// go-live date (<c>Settlement:GoLiveDate</c>; see <see cref="_goLiveDate"/>). Every boundary that closed
/// before the system was live is the manual operator fallback — NOT auto-settled — because the system
/// has no lawful quantity source for a ferieår it never tracked (the §21 written agreements were never
/// recorded, the absences were never captured; an auto-"forfeit" there is a data artifact, not an
/// entitlement). <b>Unconfigured ⇒ DORMANT</b> (settles nothing): the genuinely launch-neutral posture
/// for this slice-1a infrastructure unit (D13 "infrastructure groundwork, NOT a standalone shippable
/// feature"). Ops sets the real go-live date at launch — no code change.
/// </para>
///
/// <para>
/// <b>S70 / TASK-7005 (ADR-033 slice 3a — TERMINATION foundation).</b> The poll is restructured
/// into two steps per the SPRINT-70 pinned contract:
/// <list type="bullet">
///   <item><description><b>Step A (UNGATED leaver deactivation flip, R2/R12):</b> runs BEFORE the
///   D13 gate check. Per leaver whose <c>employment_end_date</c> has PASSED on the Copenhagen
///   business date (<c>end date &lt; today</c>; the end date is the LAST day employed, R1) and who
///   is still <c>is_active = TRUE</c>: under the employee advisory lock, ONE predicate-guarded
///   UPDATE flips <c>is_active=false</c> + <c>end_date_deactivated=true</c> (provenance) AND bumps
///   <c>users.version</c> (ADR-018 D7), with the R1(e) <c>ReportingLineManagerDeactivated</c> side
///   effects, the <see cref="EmployeeEndDateDeactivationApplied"/> emission, the ADR-026
///   audit-projection row and the <c>users_audit</c> UPDATED row in the SAME tx. The flip is a
///   lifecycle fact, NOT a settlement — the dormant gate must never skip it.</description></item>
///   <item><description><b>Step B (D13-gated settlement, now leaver-inclusive, R3/R4):</b> the due
///   enumeration's ACTIVE branch EXCLUDES passed-end-date leavers (a flip-failed leaver must NEVER
///   traverse the normal §21/§24 auto-partition — R4 pin (a)); a LEAVER branch
///   (<c>is_active=FALSE AND employment_end_date &lt; today</c>, keyed on the end date — NEVER bare
///   <c>is_active=FALSE</c>, R3) generates candidate ferieår capped at the END-DATE ferieår (R6;
///   no post-termination years are ever generated). The end-date ferieår settles with trigger
///   <c>TERMINATION</c> (due when the end date has passed — termination crystallizes AT the end
///   date, not the 31-Dec boundary); every OTHER due ferieår settles with trigger <c>YEAR_END</c>
///   (the service's in-lock leaver re-read produces the fail-closed deferred-disposition
///   PENDING_REVIEW row — no pre-discrimination here beyond trigger selection). The R2 leaver-level
///   gate applies to the WHOLE leaver branch: only leavers whose end date falls STRICTLY AFTER
///   <c>Settlement:GoLiveDate</c> are auto-settled; earlier leavers are pre-launch boundaries the
///   system never tracked and remain the manual fallback (D13).</description></item>
/// </list>
/// Step A and Step B are separate transactions (a Step-A flip must survive a Step-B failure);
/// per-leaver/per-tuple failures stay isolated (rollback, log, continue).
/// </para>
///
/// <para>
/// <b>DI (DECLARED for the Orchestrator to wire in <c>Program.cs</c>):</b>
/// <code>
/// builder.Services.AddHostedService&lt;SettlementCloseService&gt;();
/// </code>
/// All of <see cref="SettlementCloseService"/>'s constructor dependencies —
/// <see cref="DbConnectionFactory"/>, <see cref="VacationSettlementService"/>,
/// <see cref="EntitlementConfigRepository"/>, <see cref="UserRepository"/>,
/// <see cref="ReportingLineRepository"/>, <see cref="IOutboxEnqueue"/>,
/// <see cref="IAuditProjectionMapper{TEvent}"/> for <see cref="EmployeeEndDateDeactivationApplied"/>
/// (Program.cs:218), <see cref="AuditProjectionRepository"/>, <see cref="TimeProvider"/> (already
/// registered as <c>TimeProvider.System</c>) and the logger — are already in the container as
/// singletons. No new registration is required; only the <c>AddHostedService</c> line above.
/// </para>
/// </summary>
public sealed class SettlementCloseService : BackgroundService
{
    /// <summary>The entitlement type this slice settles (slice 1 = VACATION only; ADR-033 D13).</summary>
    private const string VacationType = "VACATION";

    /// <summary>YEAR_END trigger — the S68 auto-partition / the R4 leaver deferred-disposition row.</summary>
    private const string YearEndTrigger = "YEAR_END";

    /// <summary>TERMINATION trigger — the leaver's end-date-ferieår crystallization (S70 R4/R5).</summary>
    private const string TerminationTrigger = "TERMINATION";

    /// <summary>
    /// System actor for the Step-A deactivation flip — the settlement actor convention
    /// (<c>system:settlement-close:&lt;discriminator&gt;</c>, the S68
    /// <see cref="VacationSettlementService"/> precedent) with the DEACTIVATION discriminator
    /// (the flip is a lifecycle fact, not a settlement; DECLARED in the TASK-7005 report).
    /// </summary>
    private const string StepAActorId = "system:settlement-close:DEACTIVATION";

    /// <summary>System actor role — matches the settlement events' <c>"System"</c> convention.</summary>
    private const string SystemActorRole = "System";

    /// <summary>
    /// Poll cadence — mirrors <see cref="DelegationExpiryService"/> (ADR-033 D3 "the
    /// DelegationExpiryService 5-min-poll shape"). The boundary is a calendar boundary; a 5-minute
    /// poll detects a newly-crossed 31-Dec boundary within minutes and is idempotent thereafter.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Hard lower bound on the candidate entitlement-year scan when an employee has no
    /// <c>employment_start_date</c> (nullable; the S60/ADR-030 HR-corrected fact). 2020 predates the
    /// system's earliest modeled OK period (OK24 starts 2024-04-01;
    /// <see cref="StatsTid.SharedKernel.Calendar.OkVersionResolver"/>) and any greenfield ferieår, so
    /// it cannot miss a real closed year while keeping the <c>generate_series</c> band finite and
    /// small. Deterministic (not wall-clock-derived).
    /// </summary>
    private const int CandidateYearFloor = 2020;

    private readonly DbConnectionFactory _connectionFactory;
    private readonly VacationSettlementService _settlementService;
    private readonly EntitlementConfigRepository _configRepo;
    private readonly UserRepository _userRepo;
    private readonly ReportingLineRepository _reportingLineRepo;
    private readonly IOutboxEnqueue _outbox;
    private readonly IAuditProjectionMapper<EmployeeEndDateDeactivationApplied> _flipAuditMapper;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettlementCloseService> _logger;

    /// <summary>
    /// The settlement go-live date — the ADR-033 D13 launch-neutral gate. The automated year-end close
    /// settles ONLY entitlement-years whose §21/§24 boundary (31 Dec E+1) falls STRICTLY AFTER this date;
    /// every earlier boundary is the manual operator fallback (D13 "first boundary: 31 Dec after launch;
    /// manual fallback until then"). Sourced from the <c>Settlement:GoLiveDate</c> configuration key
    /// (ISO <c>yyyy-MM-dd</c>). <b>Null = unconfigured (or present-but-unparseable) = DORMANT</b>: the
    /// service runs but settles nothing — the launch-neutral posture for the slice-1a infrastructure
    /// (D13 "infrastructure groundwork, NOT a standalone shippable feature"). Deterministic — a configured
    /// date, never wall-clock — so replay/idempotency are unaffected (the date use is a TRIGGER bound, the
    /// ADR-033 D3 settled quantity is still a pure function of the captured snapshot).
    /// </summary>
    private readonly DateOnly? _goLiveDate;

    public SettlementCloseService(
        DbConnectionFactory connectionFactory,
        VacationSettlementService settlementService,
        EntitlementConfigRepository configRepo,
        UserRepository userRepo,
        ReportingLineRepository reportingLineRepo,
        IOutboxEnqueue outbox,
        IAuditProjectionMapper<EmployeeEndDateDeactivationApplied> flipAuditMapper,
        AuditProjectionRepository auditRepo,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<SettlementCloseService> logger)
    {
        _connectionFactory = connectionFactory;
        _settlementService = settlementService;
        _configRepo = configRepo;
        _userRepo = userRepo;
        _reportingLineRepo = reportingLineRepo;
        _outbox = outbox;
        _flipAuditMapper = flipAuditMapper;
        _auditRepo = auditRepo;
        _timeProvider = timeProvider;
        _logger = logger;

        var rawGoLive = configuration["Settlement:GoLiveDate"];
        if (string.IsNullOrWhiteSpace(rawGoLive))
        {
            _goLiveDate = null; // unconfigured ⇒ dormant (the launch-neutral default; ADR-033 D13).
        }
        else if (DateOnly.TryParseExact(rawGoLive, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            // STRICT ISO only (Step-7a Codex W5): TryParseExact("yyyy-MM-dd") — never the permissive
            // TryParse, which would accept locale/ambiguous forms (e.g. "06/08/2026") and silently ACTIVATE
            // automation on a misinterpreted go-live date. A non-ISO value must fail closed to dormant, not
            // settle against a date we guessed.
            _goLiveDate = parsed;
        }
        else
        {
            // A present-but-unparseable value FAILS CLOSED to dormant (never auto-settle on a garbage
            // date), logged louder than the unconfigured case so a typo is noticed rather than silently
            // settling nothing forever.
            _goLiveDate = null;
            _logger.LogWarning(
                "SettlementCloseService: Settlement:GoLiveDate='{Raw}' is not a valid ISO date (yyyy-MM-dd) — " +
                "treating as unconfigured (DORMANT); no automated settlement runs until corrected.", rawGoLive);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One-time posture log (ADR-033 D13). DORMANT until ops configures the go-live date — the
        // launch-neutral default that keeps the slice-1a infrastructure from auto-settling any
        // pre-launch boundary (where there is no lawful quantity source).
        if (_goLiveDate is null)
            _logger.LogInformation(
                "SettlementCloseService: DORMANT — no Settlement:GoLiveDate configured; automated VACATION " +
                "year-end close settles nothing (pre-launch manual fallback per ADR-033 D13). The Step-A " +
                "leaver deactivation flip is UNGATED and still runs every poll (SPRINT-70 R2).");
        else
            _logger.LogInformation(
                "SettlementCloseService: ACTIVE — auto-closing VACATION year-end boundaries strictly after " +
                "go-live {GoLiveDate} (ADR-033 D13); earlier boundaries remain the manual fallback.",
                _goLiveDate.Value);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseDueSettlementsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A whole-poll failure must not crash the loop (mirrors DelegationExpiryService) —
                // a missed poll still settles exactly once on a later pass (ADR-033 D3).
                _logger.LogError(ex, "SettlementCloseService: error during vacation settlement poll");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CloseDueSettlementsAsync(CancellationToken ct)
    {
        // The Copenhagen BUSINESS date — the §21/§24 boundary AND the Step-A flip-predicate
        // comparison authority (ADR-033 D3 / follow-up (v)). Derived from the injected TimeProvider's
        // UTC instant converted to the Europe/Copenhagen zone, NEVER raw CURRENT_DATE: near a 31-Dec
        // boundary the UTC date and the Danish date differ for ~1h each night, and a UTC-midnight
        // comparison would settle a tuple (or flip a leaver) a day early/late. (The poll being
        // DUE-to-run at all may read the wall clock — that is a trigger, not the boundary; the
        // boundary uses this controlled business date.)
        var copenhagenToday = CopenhagenToday();

        // ── Step A (UNGATED, SPRINT-70 R2) — the leaver deactivation flip runs BEFORE the D13 gate
        // check: the flip is a lifecycle fact, not a settlement, and the dormant gate must not skip
        // it. Its own per-leaver transactions — a Step-A flip survives any Step-B failure.
        await ApplyDueDeactivationFlipsAsync(copenhagenToday, ct);

        // ── Step B gate — DORMANT (ADR-033 D13). With no configured go-live date the automated
        // close SETTLES nothing — the launch-neutral default; the manual operator fallback owns
        // every boundary until ops sets Settlement:GoLiveDate. (Posture logged once in ExecuteAsync.)
        if (_goLiveDate is null) return;

        var dueTuples = await EnumerateDueTuplesAsync(copenhagenToday, _goLiveDate.Value, ct);
        if (dueTuples.Count == 0) return;

        _logger.LogInformation(
            "SettlementCloseService: {Count} due VACATION settlement tuple(s) at Copenhagen date {Date}",
            dueTuples.Count, copenhagenToday);

        foreach (var (employeeId, entitlementYear, trigger) in dueTuples)
        {
            // One transaction per tuple (ADR-018 D3 atomic settlement). SettleAsync takes the
            // ADR-032 D4 employee advisory lock FIRST + does its in-lock single-settle re-check, so
            // concurrent pollers and a late re-poll are safe: exactly one settlement commits. We open
            // the tx; SettleAsync neither commits nor rolls back (its all-or-nothing contract spans
            // the row + carryover + events + audit), so the commit/rollback is ours.
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);
            // ReadCommitted: the advisory-lock wait + the in-lock re-check inside SettleAsync need a
            // fresh post-lock snapshot to observe a concurrent poller's just-committed settlement
            // row (RepeatableRead would pin the pre-lock snapshot and defeat the re-check — the same
            // reasoning OutboxPublisher documents for its publisher tx).
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                // S70 Step-7a B1 — the close service ALWAYS supplies the leaver go-live floor
                // (_goLiveDate is non-null here: the dormant gate returned above), so SettleAsync
                // re-evaluates the R2/D13 leaver gate IN-LOCK on the re-read user (R12) — the
                // enumeration-time ResolveLeaverTupleTrigger decision alone is pre-lock and stale-able.
                var outcome = await _settlementService.SettleAsync(
                    employeeId, VacationType, entitlementYear, trigger, conn, tx,
                    leaverGoLiveFloor: _goLiveDate.Value, ct: ct);
                await tx.CommitAsync(ct);

                if (outcome.DidSettle)
                    _logger.LogInformation(
                        "SettlementCloseService: settled VACATION {Year} for {EmployeeId} ({Trigger} → {State})",
                        entitlementYear, employeeId, trigger, outcome.Row!.SettlementState); // Row non-null when DidSettle
                else if (outcome.NotDue)
                    // S70 Step-7a B1 — the in-lock due re-evaluation found the tuple no longer
                    // due under the lock (a competing end-date correction won the race);
                    // corrected state wins. Nothing written; the next poll re-enumerates fresh.
                    _logger.LogDebug(
                        "SettlementCloseService: VACATION {Year} for {EmployeeId} ({Trigger}) no longer " +
                        "due under lock; corrected state wins (benign NotDue no-op)",
                        entitlementYear, employeeId, trigger);
                else if (outcome.RefusedConflict)
                    // R7b — a TERMINATION colliding with an active row of a different trigger was
                    // REFUSED inside SettleAsync (durable SettlementManualReviewFlagged + audit, no
                    // row written, no throw). The R3 any-trigger anti-join keeps the tuple out of
                    // later due sets, so the refusal signal does not re-fire every poll.
                    _logger.LogWarning(
                        "SettlementCloseService: {Trigger} for {EmployeeId}/{Year} REFUSED on an active " +
                        "conflicting settlement (R7b manual-review flagged); continuing",
                        trigger, employeeId, entitlementYear);
                else
                    // The in-lock re-check / 23505 backstop found an active settlement (a concurrent
                    // poller won, or a TERMINATION already settled this year — ADR-033 D5). Benign.
                    _logger.LogDebug(
                        "SettlementCloseService: VACATION {Year} for {EmployeeId} already settled (no-op)",
                        entitlementYear, employeeId);
            }
            catch (DuplicateActiveSettlementException)
            {
                // The single-settle backstop fired and escaped (defensive — SettleAsync normally
                // swallows this into AlreadySettled). A concurrent poller committed first; exactly one
                // settlement stands. Roll back our empty tx, log, and continue the loop (do NOT crash).
                await SafeRollbackAsync(tx, ct);
                _logger.LogDebug(
                    "SettlementCloseService: VACATION {Year} for {EmployeeId} lost the settle race (benign duplicate); continuing",
                    entitlementYear, employeeId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-tuple failure is isolated: roll back, log, continue (mirrors
                // DelegationExpiryService). A missed tuple is re-detected on the next poll and
                // settles exactly once then (ADR-033 D3).
                await SafeRollbackAsync(tx, ct);
                _logger.LogWarning(ex,
                    "SettlementCloseService: failed to settle VACATION {Year} for {EmployeeId} ({Trigger}); continuing",
                    entitlementYear, employeeId, trigger);
            }
        }
    }

    // ------------------------------------------------------------------
    // Step A — the UNGATED leaver deactivation flip (S70 / TASK-7005; SPRINT-70 R2/R12 + R1(e)).
    // ------------------------------------------------------------------

    /// <summary>
    /// Per leaver whose <c>employment_end_date</c> has PASSED on the Copenhagen business date and who
    /// is still <c>is_active = TRUE</c>: under that leaver's R12 employee advisory lock, ONE
    /// predicate-guarded UPDATE (the FULL predicate re-evaluated in the UPDATE's WHERE) flips
    /// <c>is_active=false</c>, <c>end_date_deactivated=true</c> (lifecycle provenance) AND increments
    /// <c>users.version</c> (ADR-018 D7 — a held ETag must not survive the transition), with the
    /// R1(e) side effects + the <see cref="EmployeeEndDateDeactivationApplied"/> emission + the
    /// ADR-026 audit projection + the <c>users_audit</c> UPDATED row in the SAME tx.
    ///
    /// <para>
    /// The candidate enumeration is a plain SELECT outside any tx — correctness comes from the
    /// per-leaver in-tx lock + the re-evaluated predicate (R2 composition note): 0 rows updated is a
    /// benign no-op (a concurrent clear/correct/manual change won under the lock — rollback, no
    /// event). Per-leaver failures are isolated: rollback, log, continue — a failed flip is retried
    /// on the next poll, and until it lands the Step-B enumeration's ACTIVE branch EXCLUDES the
    /// still-active leaver (R4 pin (a): a flip-failed leaver never traverses the §21/§24
    /// auto-partition).
    /// </para>
    /// </summary>
    private async Task ApplyDueDeactivationFlipsAsync(DateOnly copenhagenToday, CancellationToken ct)
    {
        // Candidate list — a simple snapshot read; NOT a set-based bulk UPDATE (the per-employee
        // advisory lock + per-employee event payload force the per-leaver loop, R2).
        var dueLeavers = new List<string>();
        await using (var enumConn = _connectionFactory.Create())
        {
            await enumConn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT user_id FROM users
                WHERE employment_end_date IS NOT NULL
                  AND employment_end_date < @today
                  AND is_active = TRUE
                ORDER BY user_id
                """, enumConn);
            cmd.Parameters.AddWithValue("today", copenhagenToday);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                dueLeavers.Add(reader.GetString(0));
        }

        if (dueLeavers.Count == 0) return;

        _logger.LogInformation(
            "SettlementCloseService: {Count} leaver(s) due for the Step-A deactivation flip at Copenhagen date {Date}",
            dueLeavers.Count, copenhagenToday);

        foreach (var employeeId in dueLeavers)
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);
            // ReadCommitted for the same post-lock-fresh-snapshot reasoning as the Step-B settle tx.
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                // (1) R12 — the employee advisory lock FIRST (the SAME key the end-date endpoint,
                // SettleAsync and the reconcile retrofit acquire), held to commit.
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("employeeId", employeeId);
                    await lockCmd.ExecuteScalarAsync(ct);
                }

                // (2) In-lock FOR-UPDATE re-read — the event payload operands (end date, version
                // before) + the audit-context org. Terminated-inclusive read shape (R9a family); a
                // vanished row or a no-longer-due state (a concurrent clear/correct/manual
                // deactivation won the lock race) is a benign no-op.
                var hit = await _userRepo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId, ct);
                if (hit is null || !IsDeactivationDue(hit.Value.User.EmploymentEndDate, hit.Value.User.IsActive, copenhagenToday))
                {
                    await SafeRollbackAsync(tx, ct);
                    continue;
                }
                var (user, versionBefore) = hit.Value;

                // (3) The ONE predicate-guarded UPDATE — the FULL Step-A predicate re-evaluated in
                // the WHERE (R2; defense-in-depth behind the in-lock re-read above). 0 rows ⇒ a
                // concurrent writer won — rollback-empty, no event.
                long versionAfter;
                await using (var updateCmd = new NpgsqlCommand(
                    """
                    UPDATE users
                       SET is_active = FALSE,
                           end_date_deactivated = TRUE,
                           version = version + 1,
                           updated_at = NOW()
                     WHERE user_id = @id
                       AND employment_end_date IS NOT NULL
                       AND employment_end_date < @today
                       AND is_active = TRUE
                    RETURNING version
                    """, conn, tx))
                {
                    updateCmd.Parameters.AddWithValue("id", employeeId);
                    updateCmd.Parameters.AddWithValue("today", copenhagenToday);
                    var result = await updateCmd.ExecuteScalarAsync(ct);
                    if (result is not long updatedVersion)
                    {
                        await SafeRollbackAsync(tx, ct);
                        continue;
                    }
                    versionAfter = updatedVersion;
                }

                var endDate = user.EmploymentEndDate!.Value; // non-null: the in-lock re-read + the UPDATE predicate both proved it.

                // (4) R1(e) — the existing user-deactivation side-effect path, SAME tx: emit
                // ReportingLineManagerDeactivated for each active line MANAGED by the leaver
                // (mirrors the TASK-7002 endpoint / the S52 AdminEndpoints precedent).
                var managedLines = await _reportingLineRepo.GetDirectReportsAsync(conn, tx, employeeId, ct);
                foreach (var line in managedLines)
                {
                    var deactivatedEvent = new ReportingLineManagerDeactivated
                    {
                        ReportingLineId = line.ReportingLineId,
                        EmployeeId = line.EmployeeId,
                        ManagerId = line.ManagerId,
                        TreeRootOrgId = line.TreeRootOrgId,
                        ActorId = StepAActorId,
                        ActorRole = SystemActorRole,
                    };
                    await _outbox.EnqueueAsync(conn, tx, $"reporting-line-{line.EmployeeId}", deactivatedEvent, ct);
                }

                // (5) The flip event on employee-{id} (version-before/after; system actor) + the
                // ADR-026 audit-projection row, SAME tx (mirrors the TASK-7002 endpoint pattern;
                // the BackgroundService dispatch site resolves employee → primary_org_id itself).
                var flipEvent = new EmployeeEndDateDeactivationApplied
                {
                    EmployeeId = employeeId,
                    EndDate = endDate,
                    OldIsActive = true,
                    NewIsActive = false,
                    VersionBefore = versionBefore,
                    VersionAfter = versionAfter,
                    ActorId = StepAActorId,
                    ActorRole = SystemActorRole,
                };
                var outboxId = await _outbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"employee-{employeeId}", flipEvent, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: StepAActorId,
                    ActorPrimaryOrgId: user.PrimaryOrgId,
                    CorrelationId: flipEvent.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(flipEvent.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: user.PrimaryOrgId);
                var rowData = _flipAuditMapper.Map(flipEvent, auditCtx);
                await _auditRepo.InsertAsync(
                    conn, tx, flipEvent.EventId, outboxId, flipEvent.EventType, rowData, auditCtx, ct);

                // (6) users_audit UPDATED row — the before/after lifecycle tuple (the SAME JSON
                // field shape as the TASK-7002 endpoint's audit row).
                var previousData = JsonSerializer.Serialize(new
                {
                    employmentEndDate = user.EmploymentEndDate,
                    endDateDeactivated = user.EndDateDeactivated,
                    isActive = user.IsActive,
                });
                var newData = JsonSerializer.Serialize(new
                {
                    employmentEndDate = user.EmploymentEndDate,
                    endDateDeactivated = true,
                    isActive = false,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO users_audit (
                        user_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @userId, 'UPDATED',
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("userId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", versionBefore);
                    auditCmd.Parameters.AddWithValue("versionAfter", versionAfter);
                    auditCmd.Parameters.AddWithValue("actorId", StepAActorId);
                    auditCmd.Parameters.AddWithValue("actorRole", SystemActorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "SettlementCloseService: Step-A deactivation flip applied for {EmployeeId} " +
                    "(end date {EndDate} passed; version {Before} → {After})",
                    employeeId, endDate, versionBefore, versionAfter);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-leaver failure is isolated: rollback, log, continue (R2). The flip is retried
                // on the next poll; until then the Step-B ACTIVE branch excludes this leaver (R4 pin
                // (a)) so nothing auto-partitions in the failure window.
                await SafeRollbackAsync(tx, ct);
                _logger.LogWarning(ex,
                    "SettlementCloseService: Step-A deactivation flip FAILED for {EmployeeId}; continuing",
                    employeeId);
            }
        }
    }

    // ------------------------------------------------------------------
    // Due-tuple enumeration (ADR-033 D3 / Codex Step-0b W4). Employee/entitlement-config-driven —
    // NOT balance-driven — so a closed year with no entitlement_balances row is still settled.
    // ------------------------------------------------------------------

    /// <summary>
    /// Build the due <c>(employeeId, entitlementYear, trigger)</c> set (S70 / TASK-7005 — now
    /// leaver-INCLUSIVE per SPRINT-70 R3/R4):
    /// <list type="bullet">
    ///   <item><description><b>ACTIVE branch:</b> every active employee EXCLUDING passed-end-date
    ///   leavers (R4 pin (a) — a leaver whose Step-A flip FAILED, still <c>is_active=TRUE</c>, must
    ///   NEVER traverse the normal §21/§24 auto-partition; those rows belong exclusively to the
    ///   Step-A retry + the leaver branch). Candidate years from <c>employment_start_date</c> (floor
    ///   <see cref="CandidateYearFloor"/>) up to today's year; trigger <c>YEAR_END</c>; due per the
    ///   unchanged <see cref="IsBoundaryPassed"/> geometry.</description></item>
    ///   <item><description><b>LEAVER branch (R3):</b> keyed on <c>employment_end_date</c> — never
    ///   bare <c>is_active=FALSE</c>: <c>is_active = FALSE AND employment_end_date IS NOT NULL AND
    ///   employment_end_date &lt; today</c> (a manually-deactivated user with no end date is NEVER
    ///   settled as a leaver; future-dated end dates are excluded). Candidate years are CAPPED at the
    ///   end-date ferieår in the SQL itself (R4 — no post-termination years are ever generated). Per
    ///   row, <see cref="ResolveLeaverTupleTrigger"/> applies the R2 leaver-level go-live gate (end
    ///   date STRICTLY after go-live, else the whole branch yields nothing), selects
    ///   <c>TERMINATION</c> for the end-date ferieår (due when the end date has passed — already
    ///   guaranteed by the SQL predicate) and <c>YEAR_END</c> for every other due ferieår (the
    ///   unchanged boundary geometry; the settlement service's in-lock leaver re-read produces the
    ///   deferred-disposition row — no pre-discrimination here beyond trigger selection).</description></item>
    /// </list>
    /// The any-trigger anti-join against active (non-REVERSED) settlement rows is shared by both
    /// branches (R3: a TERMINATION-only anti-join would leave an R7b-conflicted leaver in the due
    /// set forever; R8: SETTLED and PENDING_REVIEW TERMINATION rows suppress a later YEAR_END for
    /// the same tuple) — S71 / TASK-7104 extended that ONE predicate site with the SPRINT-71 R3
    /// bare-reversal marker: a tuple holding a <c>bare_reversal_not_due</c> row is likewise
    /// not-due (a bare-reversed tuple is never re-enumerated by EITHER branch; terminal in 3b).
    /// </summary>
    private async Task<IReadOnlyList<(string EmployeeId, int EntitlementYear, string Trigger)>> EnumerateDueTuplesAsync(
        DateOnly copenhagenToday, DateOnly goLiveDate, CancellationToken ct)
    {
        // Upper bound of the candidate band: the latest entitlement-year whose boundary could already
        // have passed is bounded above by copenhagenToday.Year (for reset_month==1 the boundary is
        // 31 Dec of the SAME year as E; for reset_month>1 it is a year later — both ≤ today's year as
        // an over-approximation). The precise per-row filter below discards the not-yet-due tail.
        // For leavers the band is additionally capped at the END-DATE ferieår (R4) in the SQL.
        var candidateUpperYear = copenhagenToday.Year;

        // Employees paired with their candidate closed years, MINUS any year that already has an
        // active settlement (the pre-skip optimization; SettleAsync's in-lock re-check is the
        // correctness backstop). generate_series yields one row per (employee, candidate year). The
        // anti-join excludes (employee, VACATION, year) tuples with a non-REVERSED settlement of ANY
        // trigger. The leaver year-cap lives in the generate_series upper bound (the R6 ferieår of
        // the end date, mirrored by ResolveLeaverFerieaar — VACATION reset_month = 9 uniform by DB
        // CHECK) so post-termination years are never generated, not merely filtered.
        var rows = new List<(string EmployeeId, int EntitlementYear, string AgreementCode, string OkVersion,
            DateOnly? EndDate, bool IsActive)>();
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT u.user_id,
                       gs.yr AS entitlement_year,
                       u.agreement_code,
                       u.ok_version,
                       u.employment_end_date,
                       u.is_active
                FROM users u
                CROSS JOIN LATERAL generate_series(
                    GREATEST(@floor, COALESCE(EXTRACT(YEAR FROM u.employment_start_date)::int, @floor)),
                    CASE
                        WHEN u.is_active = FALSE AND u.employment_end_date IS NOT NULL
                        THEN LEAST(@upper,
                                   CASE WHEN EXTRACT(MONTH FROM u.employment_end_date)::int >= 9
                                        THEN EXTRACT(YEAR FROM u.employment_end_date)::int
                                        ELSE EXTRACT(YEAR FROM u.employment_end_date)::int - 1
                                   END)
                        ELSE @upper
                    END
                ) AS gs(yr)
                WHERE (
                        -- ACTIVE branch (R4 pin (a): a passed-end-date leaver — e.g. a flip-failed
                        -- one still is_active=TRUE — is EXCLUDED; it belongs to Step-A retry + the
                        -- leaver branch exclusively).
                        (u.is_active = TRUE
                         AND NOT (u.employment_end_date IS NOT NULL AND u.employment_end_date < @today))
                        OR
                        -- LEAVER branch (R3: keyed on the end date, never bare is_active=FALSE;
                        -- future-dated and manually-inactive-without-end-date users excluded).
                        (u.is_active = FALSE
                         AND u.employment_end_date IS NOT NULL
                         AND u.employment_end_date < @today)
                      )
                  -- S71 / TASK-7104 (SPRINT-71 R3) — the SHARED not-due anti-join, ONE predicate
                  -- site covering BOTH the ACTIVE and LEAVER branches above: a tuple is not-due
                  -- when it has a non-REVERSED row (the S70 any-trigger anti-join, unchanged) OR
                  -- a bare-reversal not-due marker row (bare reversal is TERMINAL in 3b — a
                  -- bare-reversed tuple is NEVER re-enumerated; marker-clearing + the R1 g+1
                  -- revival are the REHIRE follow-up's first obligation).
                  AND NOT EXISTS (
                        SELECT 1 FROM vacation_settlements vs
                        WHERE vs.employee_id = u.user_id
                          AND vs.entitlement_type = @type
                          AND vs.entitlement_year = gs.yr
                          AND (vs.settlement_state <> 'REVERSED' OR vs.bare_reversal_not_due)
                  )
                ORDER BY u.user_id, gs.yr
                """, conn);
            cmd.Parameters.AddWithValue("floor", CandidateYearFloor);
            cmd.Parameters.AddWithValue("upper", candidateUpperYear);
            cmd.Parameters.AddWithValue("type", VacationType);
            cmd.Parameters.AddWithValue("today", copenhagenToday);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.GetBoolean(5)));
            }
        }

        if (rows.Count == 0) return [];

        // Per-row exact due filter on the Copenhagen boundary, using the employee's resolved VACATION
        // reset_month. reset_month is IMMUTABLE per (entitlement_type, ok_version) natural key
        // (ADR-021 Q1) and uniform (9) across the seeded VACATION configs, so resolution is cached on
        // (agreement_code, ok_version). A row whose VACATION type is genuinely unconfigured under the
        // employee's live agreement is skipped — for BOTH branches (no geometry to settle against —
        // SettleAsync would be the one to fail loud if such a tuple were ever forced; here it is
        // simply not yet due/known; DECLARED in the TASK-7005 report).
        var resetMonthCache = new Dictionary<(string Agreement, string Ok), int?>();
        var due = new List<(string EmployeeId, int EntitlementYear, string Trigger)>();

        foreach (var (employeeId, entitlementYear, agreementCode, okVersion, endDate, isActive) in rows)
        {
            var key = (agreementCode, okVersion);
            if (!resetMonthCache.TryGetValue(key, out var resetMonth))
            {
                var live = await _configRepo.GetCurrentOpenAsync(VacationType, agreementCode, okVersion, ct);
                resetMonth = live?.ResetMonth;
                resetMonthCache[key] = resetMonth;
            }
            if (resetMonth is null) continue; // VACATION unconfigured for this agreement — no boundary.

            if (!isActive)
            {
                // LEAVER branch — the SQL guaranteed employment_end_date non-null and passed.
                var trigger = ResolveLeaverTupleTrigger(
                    entitlementYear, endDate!.Value, resetMonth.Value, copenhagenToday, goLiveDate);
                if (trigger is not null)
                    due.Add((employeeId, entitlementYear, trigger));
                continue;
            }

            // ACTIVE branch — unchanged S68 geometry: due iff the §21/§24 boundary has PASSED on the
            // Copenhagen date AND that boundary falls strictly after the configured go-live date
            // (ADR-033 D13 launch-neutral gate — a pre-launch boundary is the manual operator
            // fallback, never auto-settled).
            if (IsBoundaryPassed(entitlementYear, resetMonth.Value, copenhagenToday, goLiveDate))
                due.Add((employeeId, entitlementYear, YearEndTrigger));
        }

        return due;
    }

    /// <summary>
    /// The §21/§24 ferieafholdelsesperiode boundary test for VACATION entitlement-year
    /// <paramref name="entitlementYear"/> (E, the calendar year of the ferieår START — the
    /// <c>ResolveEntitlementYear</c> convention) under <paramref name="resetMonth"/>, evaluated on the
    /// Europe/Copenhagen business date <paramref name="copenhagenToday"/>. Returns <c>true</c> once the
    /// boundary has PASSED (the close is due).
    ///
    /// <para>
    /// The closed ferieår ends on <c>(reset_month/1/E).AddYears(1).AddDays(-1)</c>; the
    /// ferieafholdelsesperiode (taking window) extends to <b>31 Dec of the calendar year in which that
    /// ferieår-end falls</b> — the verified §21 "31 Dec" deadline (ADR-033 D8, line-18 §-spine). For
    /// VACATION (reset_month 9) that is 31 Dec of E+1 (ferieår Sep 1 E .. Aug 31 E+1 ⇒ boundary
    /// 31 Dec E+1), exactly "≈ 31 Dec of the year AFTER the ferieår closes". The tuple is due strictly
    /// AFTER that date (on/after 1 Jan of the following year).
    /// </para>
    ///
    /// <para>
    /// <b>Launch-neutral gate (ADR-033 D13):</b> the tuple is due only if that boundary ALSO falls
    /// strictly after <paramref name="goLiveDate"/> — the automated close owns "the first boundary 31 Dec
    /// AFTER launch" onward; every boundary that fell before go-live is the manual operator fallback (the
    /// system has no lawful quantity source for a ferieår it never tracked). The D13-literal reading: a
    /// ferieår whose taking-window straddles go-live but whose DEADLINE is after go-live IS auto-settled.
    /// </para>
    /// </summary>
    private static bool IsBoundaryPassed(int entitlementYear, int resetMonth, DateOnly copenhagenToday, DateOnly goLiveDate)
    {
        var ferieaarStart = new DateOnly(entitlementYear, resetMonth, 1);
        var ferieaarEnd = ferieaarStart.AddYears(1).AddDays(-1);
        var boundary = new DateOnly(ferieaarEnd.Year, 12, 31); // §21 31-Dec deadline of the ferieår-end year.
        return boundary > goLiveDate          // ADR-033 D13: only boundaries strictly after go-live auto-settle.
            && copenhagenToday > boundary;     // …and the deadline has actually passed on the Copenhagen date.
    }

    // ------------------------------------------------------------------
    // S70 / TASK-7005 — pure leaver decision helpers (unit-pinned; no I/O). Public statics per the
    // ComputeEndDateLifecycle precedent (EmploymentDateEndpoints).
    // ------------------------------------------------------------------

    /// <summary>
    /// The Step-A flip due-predicate (SPRINT-70 R2; R1 semantics): a deactivation flip is due iff the
    /// user is still active AND <paramref name="employmentEndDate"/> (the LAST day employed) has
    /// PASSED on the Copenhagen business date — <c>employment_end_date &lt; today</c>, so
    /// end date == today ⇒ still employed (no flip), end date == today − 1 ⇒ due. A null end date is
    /// never due (a manually-deactivated user with no end date is never lifecycle-flipped).
    /// </summary>
    public static bool IsDeactivationDue(DateOnly? employmentEndDate, bool isActive, DateOnly copenhagenToday) =>
        isActive && employmentEndDate is { } endDate && endDate < copenhagenToday;

    /// <summary>
    /// R6 ferieår resolution — the VACATION entitlement year containing <paramref name="endDate"/>
    /// (<c>reset_month = 9</c>, uniform by the S68 B1 DB CHECK): <c>endDate.Month &gt;= 9 ?
    /// endDate.Year : endDate.Year - 1</c>. Mirrors <c>EmploymentDateEndpoints.FerieaarOf</c> and the
    /// SQL year-cap in the due enumeration.
    /// </summary>
    public static int ResolveLeaverFerieaar(DateOnly endDate) =>
        endDate.Month >= 9 ? endDate.Year : endDate.Year - 1;

    /// <summary>
    /// The leaver tuple-trigger decision (SPRINT-70 R2/R4/R6 composition), for a leaver whose end
    /// date has PASSED on the Copenhagen business date (the enumeration's leaver branch guarantees
    /// that). Returns the trigger to settle with, or null when the tuple is not (yet) due:
    /// <list type="bullet">
    ///   <item><description><b>R2 leaver-level go-live gate:</b> the WHOLE leaver branch settles
    ///   ONLY when <paramref name="endDate"/> falls STRICTLY AFTER <paramref name="goLiveDate"/> —
    ///   an earlier leaver is a pre-launch boundary the system never tracked (manual fallback per
    ///   D13); a pre-go-live leaver gets NOTHING auto-settled (not even prior-year deferred
    ///   rows).</description></item>
    ///   <item><description><b>R4 year-cap:</b> ferieår after the end-date ferieår are never due
    ///   (defense-in-depth behind the SQL generation cap).</description></item>
    ///   <item><description><b>End-date ferieår ⇒ TERMINATION:</b> due when the end date has PASSED
    ///   (<paramref name="copenhagenToday"/> &gt; end date) — termination crystallizes AT the end
    ///   date, NOT at the 31-Dec boundary.</description></item>
    ///   <item><description><b>Every OTHER due ferieår ⇒ YEAR_END:</b> the EXISTING
    ///   <see cref="IsBoundaryPassed"/> geometry unchanged (31-Dec deadline passed AND boundary
    ///   strictly after go-live); the settlement service detects the leaver in-lock and writes the
    ///   fail-closed deferred-disposition PENDING_REVIEW row — callers do NOT pre-discriminate
    ///   beyond this trigger selection.</description></item>
    /// </list>
    /// </summary>
    public static string? ResolveLeaverTupleTrigger(
        int entitlementYear, DateOnly endDate, int resetMonth, DateOnly copenhagenToday, DateOnly goLiveDate)
    {
        // R2 — the TERMINATION go-live anchor is the END DATE, strictly after go-live; the gate
        // applies to the WHOLE leaver branch.
        if (endDate <= goLiveDate) return null;

        var endDateFerieaar = ResolveLeaverFerieaar(endDate);
        if (entitlementYear > endDateFerieaar) return null; // R4 — no post-termination years.

        if (entitlementYear == endDateFerieaar)
            return copenhagenToday > endDate ? TerminationTrigger : null;

        return IsBoundaryPassed(entitlementYear, resetMonth, copenhagenToday, goLiveDate)
            ? YearEndTrigger
            : null;
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A rollback failure (e.g. the tx already aborted) is non-fatal — the connection is
            // disposed by the enclosing await-using regardless. Swallowed so the poll loop continues.
        }
    }

    // ------------------------------------------------------------------
    // Europe/Copenhagen business-date helper (ADR-033 D3 boundary-timezone / follow-up (v)).
    // Scoped to this file (no SharedKernel/shared-helper edit; the Orchestrator may later hoist this
    // into the (v) business-timezone helper). Cross-platform zone-id lookup: IANA "Europe/Copenhagen"
    // on Linux/macOS (and .NET 6+ on Windows via ICU), Windows "Romance Standard Time" as the fallback.
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
        // Prefer the IANA id (canonical; works on Linux CI + the .NET ICU-backed Windows runtime).
        // Fall back to the Windows registry id. If neither resolves (a stripped container with no tz
        // database AND no ICU), fall back to UTC — the boundary is then UTC-based (degraded but never a
        // crash); production/CI both carry one of the two id sets.
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
