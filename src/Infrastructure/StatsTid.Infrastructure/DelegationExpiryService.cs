using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Infrastructure;

/// <summary>
/// The system's <b>effective-date poller</b>. Two sweeps on one 5-minute cadence, both of which
/// exist for the same reason: <b>nothing else in this system is triggered by a date arriving.</b>
/// Every other cache refresh and self-heal fires on a WRITE, so anything whose truth changes purely
/// because the calendar moved needs a poller to notice.
///
/// <list type="number">
///   <item><description><b>Vikar expiry</b> — closes EXPIRED approver-owned rows in
///   <c>manager_vikar</c> (S74 / TASK-7401 R4). See
///   <see cref="CloseExpiredDelegationsAsync"/>.</description></item>
///   <item><description><b>Effective-date boundary refresh</b> (S141 / TASK-14105, refinement B2) —
///   re-derives the two denormalised employee caches, <c>users.agreement_code</c> and
///   <c>users.employment_category</c>, from the timeline row that covers TODAY. See
///   <see cref="RefreshEffectiveDateBoundariesAsync"/>.</description></item>
/// </list>
///
/// <para>
/// <b>★ The class name is now a known misnomer, registered rather than hidden.</b> "Delegation
/// expiry" describes sweep 1 only; sweep 2 has nothing to do with stand-ins. It is hosted here
/// because this is the project's one registered date-driven poller and a second five-minute
/// BackgroundService would be duplicate machinery — but a future reader looking for "what refreshes
/// the agreement cache when a scheduled change takes effect" will not think to open a file named for
/// vikarer. Raised as a cohesion quality finding at S141 / TASK-14105 rather than fixed in place:
/// renaming a registered hosted service touches <c>Program.cs</c> and four test files and is not
/// this task's change. If it is ever renamed, <c>EffectiveDatePollingService</c> is the honest name.
/// </para>
///
/// <para>
/// Each expired vikar row is closed atomically (tx → close → outbox
/// <see cref="ManagerVikarEnded"/> → commit; ADR-018 D3), one tx per row.
///
/// <para>
/// R4a inclusive "til og med" fix: <c>until_date</c> is the LAST covered day, so a row
/// expires (closes) the day AFTER — the poll selects <c>until_date &lt; @today</c>
/// (STRICTLY less-than), NOT <c>&lt;=</c>. A vikar whose <c>until_date</c> is today is
/// STILL active today and is NOT closed until tomorrow.
/// </para>
///
/// <para>
/// S140 / TASK-14001 (QUAL-154 / QUAL-156) — <c>@today</c> is the UTC day off the INJECTED
/// <see cref="TimeProvider"/>, computed ONCE per sweep pass and bound as a parameter (PAT-028).
/// The statement previously read the DATABASE clock (<c>CURRENT_DATE</c>). Production behaviour
/// is UNCHANGED: the provider is <see cref="TimeProvider.System"/> and the Postgres session time
/// zone is UTC (<c>docs/operations/legacy-db-upgrade-runbook.md</c> § "S139 — Database session
/// time zone is assumed UTC"), under which <c>CURRENT_DATE</c> already WAS the UTC day. What
/// changes is that a date-sensitive test host can now FIX the sweep's date — a database clock
/// read is unreachable from an injected provider (PAT-008). The UTC-vs-Copenhagen business-day
/// question is QUAL-157 and is deliberately NOT decided here.
/// </para>
/// </summary>
public sealed class DelegationExpiryService : BackgroundService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly ManagerVikarRepository _vikarRepo;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly IAuditProjectionMapper<ManagerVikarEnded> _endedAuditMapper;
    private readonly ILogger<DelegationExpiryService> _logger;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Primary constructor (DI — registered as a hosted service in
    /// <c>Backend.Api/Program.cs</c>). S140 / TASK-14001: <paramref name="timeProvider"/> is the
    /// server-"today" seam, appended LAST and OPTIONAL so PRODUCTION BEHAVIOUR IS UNCHANGED (it
    /// defaults to <see cref="TimeProvider.System"/>) and the existing direct test construction
    /// keeps compiling. DI fills it from the <c>TimeProvider</c> singleton registered in
    /// <c>Program.cs</c>; a date-sensitive test host may register a FIXED provider so the R4a
    /// expiry boundary moves with the suite's clock instead of the database's.
    /// </summary>
    public DelegationExpiryService(
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        ManagerVikarRepository vikarRepo,
        AuditProjectionRepository auditRepo,
        IAuditProjectionMapper<ManagerVikarEnded> endedAuditMapper,
        ILogger<DelegationExpiryService> logger,
        TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _vikarRepo = vikarRepo;
        _auditRepo = auditRepo;
        _endedAuditMapper = endedAuditMapper;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // PAT-028 — ONE clock read for the WHOLE pass, threaded into both sweeps. Reading the
            // provider twice would let the two sweeps land on different days for a pass that
            // straddles midnight UTC: the vikar sweep would close against the 7th while the boundary
            // refresh flipped caches against the 8th, and the poll log would describe one instant
            // that never existed. S140 established the rule for sweep 1; S141 extends the SAME value
            // to sweep 2 rather than adding a second read.
            var today = Today();

            try
            {
                await CloseExpiredDelegationsAsync(stoppingToken, today);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DelegationExpiryService: error closing expired delegations");
            }

            // S141 / TASK-14105 (B2) — the second sweep gets its OWN try/catch on purpose. The two
            // sweeps are unrelated, so a vikar-expiry failure must not cost an employee a whole poll
            // cycle of a stale agreement code (and vice versa). Same reason the vikar loop already
            // catches per row rather than per pass.
            try
            {
                await RefreshEffectiveDateBoundariesAsync(stoppingToken, today);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DelegationExpiryService: error refreshing effective-date boundaries");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// The ONE clock read the poller is allowed (PAT-028 / QUAL-156): the UTC day off the INJECTED
    /// <see cref="TimeProvider"/>. Under <see cref="TimeProvider.System"/> plus a UTC database
    /// session this is exactly the value <c>CURRENT_DATE</c> used to produce, so no boundary moved
    /// when S140 replaced the database clock; what changed is that a test host can now FIX it.
    ///
    /// <para><b>Why UTC and not the Copenhagen business day.</b> Both sweeps compare against dates
    /// that WRITERS produced, and every writer in this system stamps the UTC day. A poller that
    /// asked a different calendar would flip a cache (or expire a stand-in) at a midnight the writer
    /// never used, for the one or two hours a night on which the two disagree. QUAL-157 tracks the
    /// standing question of whether business dates should move to the Danish day EVERYWHERE; the
    /// rule "match the writers" is forward-compatible with that answer, because when the writers
    /// move, this moves with them.</para>
    /// </summary>
    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Runs ONE expiry sweep (the body of the poll loop). Exposed for deterministic
    /// single-shot integration testing of the R4a inclusive-date boundary — production
    /// invokes it from <see cref="ExecuteAsync"/> on the 5-minute cadence.
    /// </summary>
    /// <param name="today">
    /// S141 / TASK-14105 — the pass's ONE date, supplied by <see cref="ExecuteAsync"/> so both
    /// sweeps of a single pass share it (PAT-028). OPTIONAL and trailing so the existing direct
    /// single-shot test constructions keep compiling; when omitted the method falls back to its own
    /// <see cref="Today()"/> read, which is correct for a caller that runs this sweep alone.
    /// </param>
    public async Task CloseExpiredDelegationsAsync(CancellationToken ct, DateOnly? today = null)
    {
        var sweepDate = today ?? Today();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Find expired vikar rows. R4a: until_date < @today (STRICTLY), so the named
        // until_date is the LAST covered day and the row closes the day AFTER: a row whose
        // until_date IS today is still active; one dated yesterday expires. The comparison date
        // is a BOUND PARAMETER, never a SQL clock read (PAT-028 / QUAL-156) — a statement clock
        // is a second read, on a clock no test host can fix.
        var expired = new List<ManagerVikar>();
        await using (var findCmd = new NpgsqlCommand(
            """
            SELECT vikar_id, absent_approver_id, vikar_user_id, until_date, reason,
                   organisation_id, version, created_by, created_at, effective_to
            FROM manager_vikar
            WHERE effective_to IS NULL
              AND until_date < @today
            """, conn))
        {
            findCmd.Parameters.AddWithValue("today", sweepDate);
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                expired.Add(new ManagerVikar
                {
                    VikarId = reader.GetGuid(0),
                    AbsentApproverId = reader.GetString(1),
                    VikarUserId = reader.GetString(2),
                    UntilDate = DateOnly.FromDateTime(reader.GetDateTime(3)),
                    Reason = reader.GetString(4),
                    OrganisationId = reader.GetString(5),
                    Version = reader.GetInt64(6),
                    CreatedBy = reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8),
                    EffectiveTo = reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                });
            }
        }

        if (expired.Count == 0) return;

        _logger.LogInformation("DelegationExpiryService: closing {Count} expired vikar rows", expired.Count);

        // Close each row atomically: tx → close → outbox event → commit (ADR-018 D3).
        foreach (var vikar in expired)
        {
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            try
            {
                // Close the day AFTER until_date (the first uncovered day). The close marker
                // records WHEN the row stopped being active, not the last covered day.
                var effectiveTo = vikar.UntilDate.AddDays(1);
                var closed = await _vikarRepo.CloseAsync(conn, tx, vikar.VikarId, effectiveTo, ct);
                if (closed is null)
                {
                    await tx.RollbackAsync(ct);
                    continue; // Already closed by another process.
                }

                var @event = new ManagerVikarEnded
                {
                    VikarId = closed.VikarId,
                    AbsentApproverId = closed.AbsentApproverId,
                    VikarUserId = closed.VikarUserId,
                    UntilDate = closed.UntilDate,
                    Reason = closed.Reason,
                    OrganisationId = closed.OrganisationId,
                    EffectiveTo = closed.EffectiveTo!.Value,
                    EndReason = "EXPIRED",
                    RowVersion = closed.Version,
                    ActorId = "SYSTEM",
                    ActorRole = "SYSTEM",
                };
                // ADR-018 D3 + ADR-026 D2: event + audit-projection row + state in ONE tx
                // (mirrors the SettlementCloseService system-actor flip site). The SYSTEM actor
                // carries no JWT org / correlation; actor_primary_org_id = the vikar's tree root.
                var outboxId = await _outbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"reporting-line-{closed.AbsentApproverId}", @event, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: @event.ActorId,
                    ActorPrimaryOrgId: @event.OrganisationId,
                    CorrelationId: @event.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: @event.OrganisationId);
                var auditRow = _endedAuditMapper.Map(@event, auditCtx);
                await _auditRepo.InsertAsync(
                    conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "DelegationExpiryService: closed expired vikar {VikarId} for approver {ApproverId}",
                    closed.VikarId, closed.AbsentApproverId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "DelegationExpiryService: failed to close vikar row {VikarId}", vikar.VikarId);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // S141 / TASK-14105 (refinement B2) — THE EFFECTIVE-DATE BOUNDARY REFRESH
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds employees whose denormalised caches no longer agree with the timeline row covering
    /// today. ONE statement, run OUTSIDE any transaction (the vikar sweep's shape): a cheap
    /// set-based scan that decides which employees are worth a transaction at all.
    ///
    /// <para><b>Single-row LATERAL joins, not a plain correlated predicate</b> — the S141 wave-1
    /// Step-5a lesson. Neither "the row covering a date" predicate has a unique index behind it:
    /// non-overlap of dated rows is a WRITER-side invariant only, and the history indexes permit an
    /// overlapping pair. A plain join would then fan out and the refresh would write whichever value
    /// the query planner emitted first. <c>ORDER BY effective_from DESC LIMIT 1</c> is the same
    /// tie-break the sibling reads use, so an overlapping pair resolves the same way everywhere
    /// instead of differently per statement.</para>
    ///
    /// <para><b>The <c>IS NOT NULL</c> halves are the COALESCE rule</b>, not defensive noise. Both
    /// interactive writers refresh their cache with
    /// <c>COALESCE(&lt;today's value&gt;, &lt;cached value&gt;)</c>, so when NO row covers today the
    /// cached value is KEPT rather than nulled (the columns are NOT NULL, and "nothing covers today"
    /// is a state HR can now create — refinement B8). This job must obey the same rule or it would
    /// blank a column the writers deliberately preserve.</para>
    /// </summary>
    private const string SelectDivergedCachesSql =
        """
        SELECT u.user_id,
               u.agreement_code               AS cached_agreement_code,
               u.employment_category          AS cached_employment_category,
               agr.agreement_code             AS today_agreement_code,
               prof.employment_category       AS today_employment_category
        FROM users u
        LEFT JOIN LATERAL (
            SELECT uac.agreement_code
            FROM user_agreement_codes uac
            WHERE uac.user_id = u.user_id
              AND uac.effective_from <= @today
              AND (uac.effective_to IS NULL OR uac.effective_to > @today)
            ORDER BY uac.effective_from DESC
            LIMIT 1
        ) agr ON TRUE
        LEFT JOIN LATERAL (
            SELECT ep.employment_category
            FROM employee_profiles ep
            WHERE ep.employee_id = u.user_id
              AND ep.effective_from <= @today
              AND (ep.effective_to IS NULL OR ep.effective_to > @today)
            ORDER BY ep.effective_from DESC
            LIMIT 1
        ) prof ON TRUE
        WHERE (agr.agreement_code IS NOT NULL
               AND agr.agreement_code IS DISTINCT FROM u.agreement_code)
           OR (prof.employment_category IS NOT NULL
               AND prof.employment_category IS DISTINCT FROM u.employment_category)
        ORDER BY u.user_id
        """;

    /// <summary>
    /// <b>Plain-language what this is for.</b> HR can now enter an employment change in October and
    /// date it 1 November. The change is written correctly on the day it is entered — and then
    /// NOTHING in the system notices 1 November arriving. Every cache refresh and every self-heal in
    /// this codebase fires on a WRITE, so the two live caches HR, login and many read paths depend
    /// on (<c>users.agreement_code</c> and <c>users.employment_category</c>) would keep showing
    /// October's answer until somebody happened to write to that employee again. This sweep is the
    /// thing that notices.
    ///
    /// <para><b>What it does.</b> For every employee whose cached value disagrees with the timeline
    /// row covering today, it re-derives the cache from the timeline — exactly the value the
    /// interactive writers would have written. It is stated as a DIVERGENCE repair rather than as
    /// "apply today's scheduled changes" on purpose: written that way it also self-heals any other
    /// cause of drift (a legacy row, a restored backup, a bug fixed elsewhere), and it is idempotent
    /// — a second pass on the same day finds nothing.</para>
    ///
    /// <para><b>Why it closes a window wave 1 OPENED.</b> Before S141 the canonical dated read was
    /// wrong between the write and the effective date. After wave 1 it is right throughout — but the
    /// CACHE is wrong from the effective date until the next write, and many consumers read the
    /// cache (wave-1 finding K). The window shrank and moved; this sweep removes it, which is why
    /// wave 1 was not allowed to ship alone.</para>
    ///
    /// <para><b>★ The <c>users.version</c> decision, stated at the site as the task required: it
    /// DOES bump.</b> The alternative was a silent cache flip. That was rejected because the
    /// interactive writers bump the token whenever they touch the same cache, so a job that moved
    /// the same value without bumping would make "the token changed" mean two different things
    /// depending on who wrote it — and a client holding a pre-flip ETag would be told nothing had
    /// moved while the value it is showing had. The cost of bumping is that an open HR drawer gets
    /// one stale-token refusal after a boundary crosses; the cost of not bumping is a token that
    /// silently stops being a complete statement about the record. <b>Lost-update risk is nil either
    /// way</b> — neither cache is ever written from a request body (both writers re-derive them from
    /// the timeline), so there is no user value to lose.</para>
    ///
    /// <para><b>Auditability: a <c>users_audit</c> row, and deliberately NO outbox event.</b> Wave 1
    /// established the invariant that every <c>users.version</c> transition has a <c>users_audit</c>
    /// row explaining it, so this write owes one and writes one, actor <c>SYSTEM</c> — the same
    /// actor the vikar close above uses. It does NOT emit a domain event, and that is a judgement
    /// worth reading rather than reversing: the domain fact was decided, evented and audited when HR
    /// wrote the dated row, and that event already carried the future <c>effectiveFrom</c>. Emitting
    /// (say) <c>UserAgreementCodeChanged</c> again today would republish a change subscribers were
    /// already told about, with a different actor and no new content — a duplicate, not a missing
    /// signal. ADR-018 D3 is satisfied by the write that made the decision; this sweep changes no
    /// domain state, it lets a derived projection catch up with a date. <b>What is genuinely owed and
    /// NOT delivered here</b> is a narrow event for the <c>employment_category</c> cache: the
    /// agreement side has <c>UserAgreementCodeChanged</c> as a narrow signal and the category side
    /// has none, an asymmetry that predates this task. Declared to the Orchestrator rather than
    /// invented here, since a new event type is data-model scope.</para>
    ///
    /// <para><b>Not gated on <c>is_active</c> or on employment dates</b>, matching both writers: a
    /// departed employee's cache must stay correctable, and skipping them here would reintroduce the
    /// drift on exactly the population whose payroll corrections need it.</para>
    /// </summary>
    /// <param name="today">
    /// The pass's ONE date (PAT-028), threaded from <see cref="ExecuteAsync"/>. Optional and
    /// trailing for single-shot testing, as on <see cref="CloseExpiredDelegationsAsync"/>.
    /// </param>
    public async Task RefreshEffectiveDateBoundariesAsync(CancellationToken ct, DateOnly? today = null)
    {
        var sweepDate = today ?? Today();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var candidates = new List<(string UserId, string CachedAgreementCode, string CachedCategory,
                                   string? TodayAgreementCode, string? TodayCategory)>();
        await using (var findCmd = new NpgsqlCommand(SelectDivergedCachesSql, conn))
        {
            findCmd.Parameters.AddWithValue("today", sweepDate);
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                candidates.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        if (candidates.Count == 0) return;

        _logger.LogInformation(
            "DelegationExpiryService: refreshing {Count} employee cache(s) whose effective-date boundary passed (as of {Today:yyyy-MM-dd})",
            candidates.Count, sweepDate);

        foreach (var candidate in candidates)
        {
            try
            {
                await RefreshOneEmployeeAsync(conn, candidate.UserId, sweepDate, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One employee's failure must not cost the rest of the sweep. The scan is a pure
                // divergence test, so a skipped employee is simply re-detected on the next pass —
                // there is no progress marker to corrupt and nothing to replay.
                _logger.LogWarning(
                    ex, "DelegationExpiryService: failed to refresh effective-date caches for {UserId}",
                    candidate.UserId);
            }
        }
    }

    /// <summary>
    /// One employee, one transaction: lock the <c>users</c> row, RE-READ the covering values under
    /// that lock, and write only if they still disagree.
    ///
    /// <para><b>Lock order and why there is no deadlock edge.</b> Both interactive writers take the
    /// timeline rows <c>FOR UPDATE</c> first and the <c>users</c> row last. This job takes ONLY the
    /// <c>users</c> row and then READS the timeline with no lock at all, so it can never be the
    /// second half of a waits-for cycle. It is also why the re-read must come AFTER the lock rather
    /// than before: once this transaction holds the users row, any interactive writer that was
    /// mid-flight has necessarily committed (the users row is its last lock), and under READ
    /// COMMITTED the next statement sees that commit. Re-reading first and locking second would
    /// leave a window in which this job overwrites a fresher value with a staler one.</para>
    ///
    /// <para><b>READ COMMITTED, not the vikar sweep's REPEATABLE READ</b> — deliberately different.
    /// The whole point of the re-read is to observe the LATEST committed timeline after the lock is
    /// taken; under REPEATABLE READ the statement would either serve the pre-lock snapshot or abort
    /// with a serialization failure, and neither is the behaviour wanted here.</para>
    ///
    /// <para>The write is skipped entirely when the re-read says the caches already agree, which is
    /// the ordinary outcome when an interactive write beat the sweep to the same boundary. A skipped
    /// employee costs no version bump and no audit row, so the token still moves exactly once per
    /// real change.</para>
    /// </summary>
    private async Task RefreshOneEmployeeAsync(
        NpgsqlConnection conn, string userId, DateOnly today, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

        string cachedAgreementCode;
        string cachedCategory;
        long versionBefore;
        await using (var lockCmd = new NpgsqlCommand(
            """
            SELECT agreement_code, employment_category, version
            FROM users
            WHERE user_id = @userId
            FOR UPDATE
            """, conn, tx))
        {
            lockCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // The user was deleted between the scan and the lock. Nothing to refresh.
                await tx.RollbackAsync(ct);
                return;
            }
            cachedAgreementCode = reader.GetString(0);
            cachedCategory = reader.GetString(1);
            versionBefore = reader.GetInt64(2);
        }

        var todayAgreementCode = await ReadCoveringAgreementCodeAsync(conn, tx, userId, today, ct);
        var todayCategory = await ReadCoveringEmploymentCategoryAsync(conn, tx, userId, today, ct);

        // The COALESCE rule again, in C#: a value only moves when a row actually covers today.
        var newAgreementCode = todayAgreementCode ?? cachedAgreementCode;
        var newCategory = todayCategory ?? cachedCategory;

        if (string.Equals(newAgreementCode, cachedAgreementCode, StringComparison.Ordinal)
            && string.Equals(newCategory, cachedCategory, StringComparison.Ordinal))
        {
            await tx.RollbackAsync(ct);
            return;
        }

        long versionAfter;
        await using (var updateCmd = new NpgsqlCommand(
            """
            UPDATE users
               SET agreement_code = @agreementCode,
                   employment_category = @employmentCategory,
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @userId
            RETURNING version
            """, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("userId", userId);
            updateCmd.Parameters.AddWithValue("agreementCode", newAgreementCode);
            updateCmd.Parameters.AddWithValue("employmentCategory", newCategory);
            var scalar = await updateCmd.ExecuteScalarAsync(ct);
            if (scalar is null || scalar is DBNull)
            {
                throw new InvalidOperationException(
                    $"Effective-date cache refresh for user_id='{userId}' matched no row; " +
                    "FOR UPDATE invariant violated.");
            }
            versionAfter = (long)scalar;
        }

        // users_audit — the transition-explains-itself rule (ADR-018 D7 / ADR-019 D8, and the
        // wave-1 invariant that every users.version move has a row). Both cached fields are written
        // on both sides even when only one moved, so a reader of the audit stream sees the complete
        // before/after state of the cache rather than having to infer the untouched half. The actor
        // is SYSTEM because no person decided anything today — the calendar did.
        var previousData = System.Text.Json.JsonSerializer.Serialize(new
        {
            agreementCode = cachedAgreementCode,
            employmentCategory = cachedCategory,
        });
        var newData = System.Text.Json.JsonSerializer.Serialize(new
        {
            agreementCode = newAgreementCode,
            employmentCategory = newCategory,
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
                'SYSTEM', 'SYSTEM')
            """, conn, tx))
        {
            auditCmd.Parameters.AddWithValue("userId", userId);
            auditCmd.Parameters.AddWithValue("previousData", previousData);
            auditCmd.Parameters.AddWithValue("newData", newData);
            auditCmd.Parameters.AddWithValue("versionBefore", versionBefore);
            auditCmd.Parameters.AddWithValue("versionAfter", versionAfter);
            await auditCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation(
            "DelegationExpiryService: effective-date boundary refreshed for {UserId} — " +
            "agreement_code {OldCode}→{NewCode}, employment_category {OldCategory}→{NewCategory}, " +
            "users.version {VersionBefore}→{VersionAfter}",
            userId, cachedAgreementCode, newAgreementCode, cachedCategory, newCategory,
            versionBefore, versionAfter);
    }

    /// <summary>
    /// The agreement code of the row covering <paramref name="today"/>, re-read inside the
    /// transaction after the users lock. Same predicate and same <c>ORDER BY … LIMIT 1</c> tie-break
    /// as <see cref="SelectDivergedCachesSql"/> and as the interactive writer's own cache refresh —
    /// three statements that must agree about which row wins when rows overlap. <c>null</c> when no
    /// row covers today, which the caller reads as "keep the cached value" (the COALESCE rule).
    /// <para>Written as its own method with the SQL inline rather than sharing one helper that takes
    /// the statement as a parameter: CA2100 (the SQL-injection analyzer) cannot see that a passed-in
    /// string is a constant, and silencing it would cost more than the duplication.</para>
    /// </summary>
    private static async Task<string?> ReadCoveringAgreementCodeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, DateOnly today, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("today", today);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is null || scalar is DBNull ? null : (string)scalar;
    }

    /// <summary>
    /// The employment category of the profile row covering <paramref name="today"/>. The
    /// <see cref="ReadCoveringAgreementCodeAsync"/> sibling, on the other timeline; see that method
    /// for the tie-break and null-handling rationale.
    /// </summary>
    private static async Task<string?> ReadCoveringEmploymentCategoryAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, DateOnly today, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_category
            FROM employee_profiles
            WHERE employee_id = @userId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("today", today);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is null || scalar is DBNull ? null : (string)scalar;
    }
}
