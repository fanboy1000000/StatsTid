using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S71 / TASK-7104 (SPRINT-71 R4 — the ONE-lifecycle-write-implementation rule). The SHARED
/// employment-end-date lifecycle WRITER: the FULL choreography the S70 end-date PUT performs
/// after its guards (<c>EmploymentDateEndpoints</c> PUT handler steps (2)+(4)–(8), ~lines
/// 360-545), extracted so the slice-3b reversal service can apply the SUBSUMED end-date
/// correction inside the reversal tx (R4) without a second, divergent writer:
///
/// <list type="number">
///   <item><description>FOR-UPDATE terminated-inclusive re-read (the canonical snapshot for the
///   version precondition, the R1 decision, the event's old_* payload and the audit
///   previous_data);</description></item>
///   <item><description>admin-strict version precondition (ADR-019 D2 — the caller-supplied
///   <c>users.version</c> If-Match; mismatch throws <see cref="OptimisticConcurrencyException"/>
///   for the caller's 412 mapping, missing row throws <see cref="KeyNotFoundException"/> for
///   404);</description></item>
///   <item><description>the R1 lifecycle decision (<see cref="ComputeEndDateLifecycle"/> — the
///   CANONICAL pure decision, hosted here);</description></item>
///   <item><description>the guarded versioned write
///   (<see cref="UserRepository.SetEmploymentEndDateIncludingTerminatedAsync"/>, ADR-018 D7
///   version bump);</description></item>
///   <item><description>R1(e) — <see cref="ReportingLineManagerDeactivated"/> per active managed
///   line when the write deactivates;</description></item>
///   <item><description>the R10 <see cref="EmployeeEmploymentEndDateSet"/> event on
///   <c>employee-{id}</c> via the outbox + the ADR-026 audit-projection row;</description></item>
///   <item><description>the <c>users_audit</c> UPDATED row (full lifecycle-tuple
///   before/after).</description></item>
/// </list>
///
/// <para>
/// <b>Caller contract:</b> the caller owns the tx and MUST hold the ADR-032 D4 employee advisory
/// lock (R12) BEFORE calling — this writer takes <c>(conn, tx)</c> and never commits/rolls back.
/// The caller also owns every gate that is NOT lifecycle-write choreography: authorization +
/// self-target exclusion, If-Match header parsing, and the R7a/R13 active-settlement correction
/// guards (the reversal tx satisfies R7a differently — it has just CAS-reversed the affected
/// row).
/// </para>
///
/// <para>
/// <b>Transitional duplication (DECLARED, SPRINT-71 R4):</b> until TASK-7102 refactors the PUT
/// (<c>EmploymentDateEndpoints</c>) to delegate here, the lifecycle-write choreography exists
/// TWICE — this writer and the PUT handler — and the pure decision exists twice
/// (<see cref="ComputeEndDateLifecycle"/> here is the CANONICAL host, semantically IDENTICAL to
/// <c>EmploymentDateEndpoints.ComputeEndDateLifecycle</c>; a property-style unit parity test
/// pins them equal over the full input grid). Two DIVERGENT writers are forbidden — any change
/// lands here first and the parity test polices the window.
/// </para>
/// </summary>
public sealed class EmploymentEndDateLifecycleWriter
{
    /// <summary>
    /// S147 / TASK-14710 (QUAL-230 T5a point 1; owner ruling OQ-2) — the leaver's MANAGER GRACE, in
    /// <b>calendar</b> days (owner-confirmed): for this many days after a leaver's last working day the
    /// leaver stays <c>is_active</c>, so their manager can still approve, reject or reopen the final
    /// month as on any other day. The automatic switch-off (<c>is_active = FALSE</c>) therefore lands
    /// on day 6, not day 1.
    ///
    /// <para><b>The three dates of a leaver's final month</b> (refinement R-27), with D = the last day
    /// employed (<c>employment_end_date</c>, inclusive):</para>
    /// <list type="bullet">
    ///   <item><description><b>D+1</b> — the leaver's OWN access ends, whatever their role: login is
    ///   refused (<c>AuthEndpoints</c>) and every own-record write is refused
    ///   (<c>DepartedSubjectWriteFloor</c>, the <c>OrgScopeValidator</c> departed-self clause). Neither
    ///   reads this constant: they key on "the end date has passed"
    ///   (<see cref="StatsTid.SharedKernel.Security.DepartedSubject"/>).</description></item>
    ///   <item><description><b>D+1 .. D+5</b> — the grace: still active, the manager acts as
    ///   usual.</description></item>
    ///   <item><description><b>D+6</b> — deactivated, by whichever path runs first: the Step-A poller
    ///   (<c>SettlementCloseService.IsDeactivationDue</c> and its two SQL copies) or this writer's
    ///   same-transaction path (<see cref="ComputeEndDateLifecycle"/>). Both read THIS constant through
    ///   <see cref="IsPastGrace"/> / <see cref="GraceDeactivationCutoff"/>; there is no second copy of
    ///   the number.</description></item>
    /// </list>
    ///
    /// <para>Nothing stored moves with it: the month's deadline and the payroll export cutoff stay
    /// month-end + 5 (<c>InstitutionalDeadlines</c>), the same for every employee.</para>
    /// </summary>
    public const int ManagerGraceCalendarDays = 5;

    /// <summary>
    /// S147 / TASK-14710 — the ONE pure deactivation-due test both deactivation paths call:
    /// <c>true</c> iff <paramref name="today"/> (the Copenhagen business date, read once by the
    /// caller — PAT-028) is past the manager grace that follows <paramref name="endDate"/> (the LAST
    /// day employed). False on D .. D+5, true from D+6.
    /// </summary>
    /// <remarks>
    /// Written as <c>endDate &lt; GraceDeactivationCutoff(today)</c>, which is the same day arithmetic
    /// as <c>endDate.AddDays(ManagerGraceCalendarDays) &lt; today</c> but subtracts from
    /// <paramref name="today"/> rather than adding to a stored date, so a far-future end date can never
    /// overflow <see cref="DateOnly.MaxValue"/>. The SQL copies bind the same cutoff as a parameter, so
    /// C# and SQL share one calculation.
    /// </remarks>
    public static bool IsPastGrace(DateOnly endDate, DateOnly today) =>
        endDate < GraceDeactivationCutoff(today);

    /// <summary>
    /// S147 / TASK-14710 — the cutoff the SQL copies of <see cref="IsPastGrace"/> bind as a parameter
    /// (<c>employment_end_date &lt; @cutoff</c>): <paramref name="today"/> minus
    /// <see cref="ManagerGraceCalendarDays"/>. An end date strictly before it is past the grace.
    /// </summary>
    public static DateOnly GraceDeactivationCutoff(DateOnly today) =>
        today.AddDays(-ManagerGraceCalendarDays);

    private readonly UserRepository _userRepo;
    private readonly ReportingLineRepository _reportingLineRepo;
    private readonly Outbox.IOutboxEnqueue _outbox;
    private readonly IAuditProjectionMapper<EmployeeEmploymentEndDateSet> _endDateAuditMapper;
    private readonly AuditProjectionRepository _auditRepo;

    public EmploymentEndDateLifecycleWriter(
        UserRepository userRepo,
        ReportingLineRepository reportingLineRepo,
        Outbox.IOutboxEnqueue outbox,
        IAuditProjectionMapper<EmployeeEmploymentEndDateSet> endDateAuditMapper,
        AuditProjectionRepository auditRepo)
    {
        _userRepo = userRepo;
        _reportingLineRepo = reportingLineRepo;
        _outbox = outbox;
        _endDateAuditMapper = endDateAuditMapper;
        _auditRepo = auditRepo;
    }

    /// <summary>
    /// Apply one employment-end-date mutation (set / clear / correction) with the FULL R1/R10
    /// lifecycle choreography, on the caller's advisory-locked tx. See the class doc for the
    /// step list and the caller contract.
    /// </summary>
    /// <param name="conn">The caller's open connection (the R12 lock is held on it).</param>
    /// <param name="tx">The caller's active tx (all writes participate; never committed here).</param>
    /// <param name="employeeId">The target employee.</param>
    /// <param name="newEndDate">The new <c>employment_end_date</c> (null = clear — R1(c)).</param>
    /// <param name="expectedUserVersion">The caller-supplied <c>users.version</c> If-Match
    /// (ADR-019 D2 admin-strict; the R4 two-aggregate precondition's user half).</param>
    /// <param name="actorId">The operator (JWT actor) — events + audit rows; never a system actor.</param>
    /// <param name="actorRole">The operator's role.</param>
    /// <param name="actorOrgId">The operator's primary org (the audit-projection actor context).</param>
    /// <param name="correlationId">The request correlation id threaded onto the events.</param>
    /// <param name="copenhagenToday">The Copenhagen business date (the R1 grace comparison
    /// authority, <see cref="IsPastGrace"/> — the caller resolves it from its injected TimeProvider;
    /// PAT-008).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version pair + the lifecycle outcome (old/new tuple).</returns>
    /// <exception cref="KeyNotFoundException">No <c>users</c> row exists at all (caller maps 404).</exception>
    /// <exception cref="OptimisticConcurrencyException">The row exists but
    /// <paramref name="expectedUserVersion"/> is stale (caller maps 412).</exception>
    public async Task<EmploymentEndDateLifecycleResult> ApplyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly? newEndDate, long expectedUserVersion,
        string actorId, string actorRole, string? actorOrgId, Guid? correlationId,
        DateOnly copenhagenToday, CancellationToken ct = default)
    {
        // (2) FOR-UPDATE terminated-inclusive re-read — the canonical snapshot (the PUT's step (2)).
        var lockedHit = await _userRepo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId, ct)
            ?? throw new KeyNotFoundException($"User '{employeeId}' not found.");
        var (lockedUser, lockedVersion) = lockedHit;

        if (lockedVersion != expectedUserVersion)
        {
            throw new OptimisticConcurrencyException(
                $"User '{employeeId}' version is {lockedVersion}, but the caller sent expected " +
                $"version {expectedUserVersion}; refresh and retry.",
                expectedVersion: expectedUserVersion,
                actualVersion: lockedVersion);
        }

        // (4) The R1 lifecycle decision — pure function of the LOCKED row + the Copenhagen date.
        var (newIsActive, newEndDateDeactivated) = ComputeEndDateLifecycle(
            newEndDate, lockedUser.IsActive, lockedUser.EndDateDeactivated, copenhagenToday);
        var isDeactivating = lockedUser.IsActive && !newIsActive;

        // (5) Guarded versioned write (ADR-018 D7 — a held ETag never survives the transition).
        var newVersion = await _userRepo.SetEmploymentEndDateIncludingTerminatedAsync(
            conn, tx, employeeId, newEndDate,
            newEndDateDeactivated, newIsActive, expectedUserVersion, ct);

        // (6) R1(e) — every lifecycle deactivation reuses the EXISTING user-deactivation
        // side-effect path: ReportingLineManagerDeactivated per active line managed by the
        // leaver, SAME tx (the PUT's step (6) / the S52 AdminEndpoints precedent).
        if (isDeactivating)
        {
            var managedLines = await _reportingLineRepo.GetDirectReportsAsync(conn, tx, employeeId, ct);
            foreach (var line in managedLines)
            {
                var deactivatedEvent = new ReportingLineManagerDeactivated
                {
                    ReportingLineId = line.ReportingLineId,
                    EmployeeId = line.EmployeeId,
                    ManagerId = line.ManagerId,
                    OrganisationId = line.OrganisationId,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = correlationId,
                };
                await _outbox.EnqueueAsync(conn, tx, $"reporting-line-{line.EmployeeId}", deactivatedEvent, ct);
            }
        }

        // (7) R10 — EmployeeEmploymentEndDateSet (set/clear/correction discriminated by the
        // old/new pair) + the ADR-026 audit-projection row, SAME tx (the PUT's step (7)).
        var endDateEvent = new EmployeeEmploymentEndDateSet
        {
            EmployeeId = employeeId,
            OldEndDate = lockedUser.EmploymentEndDate,
            NewEndDate = newEndDate,
            OldIsActive = lockedUser.IsActive,
            NewIsActive = newIsActive,
            VersionBefore = lockedVersion,
            VersionAfter = newVersion,
            ActorId = actorId,
            ActorRole = actorRole,
            CorrelationId = correlationId,
        };
        var outboxId = await _outbox.EnqueueAndReturnIdAsync(
            conn, tx, $"employee-{employeeId}", endDateEvent, ct);
        var auditCtx = new AuditProjectionContext(
            ActorId: actorId,
            ActorPrimaryOrgId: actorOrgId,
            CorrelationId: endDateEvent.CorrelationId,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(endDateEvent.OccurredAt, DateTimeKind.Utc)),
            ResolvedTargetOrgId: lockedUser.PrimaryOrgId);
        var rowData = _endDateAuditMapper.Map(endDateEvent, auditCtx);
        await _auditRepo.InsertAsync(
            conn, tx, endDateEvent.EventId, outboxId, endDateEvent.EventType, rowData, auditCtx, ct);

        // (8) users_audit UPDATED row — the full lifecycle-tuple before/after snapshot (the PUT's
        // step (8); byte-identical JSON field shape).
        var previousData = JsonSerializer.Serialize(new
        {
            employmentEndDate = lockedUser.EmploymentEndDate,
            endDateDeactivated = lockedUser.EndDateDeactivated,
            isActive = lockedUser.IsActive,
        });
        var newData = JsonSerializer.Serialize(new
        {
            employmentEndDate = newEndDate,
            endDateDeactivated = newEndDateDeactivated,
            isActive = newIsActive,
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
            auditCmd.Parameters.AddWithValue("versionBefore", lockedVersion);
            auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
            auditCmd.Parameters.AddWithValue("actorId", actorId);
            auditCmd.Parameters.AddWithValue("actorRole", actorRole);
            await auditCmd.ExecuteNonQueryAsync(ct);
        }

        return new EmploymentEndDateLifecycleResult
        {
            VersionBefore = lockedVersion,
            VersionAfter = newVersion,
            OldEndDate = lockedUser.EmploymentEndDate,
            OldIsActive = lockedUser.IsActive,
            OldEndDateDeactivated = lockedUser.EndDateDeactivated,
            NewIsActive = newIsActive,
            NewEndDateDeactivated = newEndDateDeactivated,
        };
    }

    /// <summary>
    /// The R1 deactivation-lifecycle decision — the CANONICAL pure function (SPRINT-71 R4 hosts
    /// it HERE; semantically IDENTICAL to <c>EmploymentDateEndpoints.ComputeEndDateLifecycle</c>,
    /// which TASK-7102 will refactor to delegate to this writer — the unit parity test pins the
    /// two equal over the full input grid during the transitional window). Returns the
    /// (<c>is_active</c>, <c>end_date_deactivated</c>) tuple to persist. <c>employment_end_date</c>
    /// is the LAST day employed. <b>S147 / TASK-14710 (OQ-2):</b> the flip is due only once the
    /// manager grace has passed — <see cref="IsPastGrace"/>, true from D+6 (more than
    /// <see cref="ManagerGraceCalendarDays"/> calendar days after the end date), the SAME helper the
    /// Step-A poller calls. Before S147 it was due from D+1. The R1(a)–(d) mapping:
    /// (a) set, past the grace, active row → deactivate with provenance in the same transaction;
    /// (b) set, future-dated, == today, or within the grace (D+1 .. D+5) → store only, no flip (the
    ///     Step-A poller flips on D+6);
    /// (c) clear → reactivate ONLY on lifecycle provenance (then reset it); a manually-deactivated
    ///     user keeps <c>is_active=false</c>; provenance always resets;
    /// (d) set on a manually-inactive row → record the date, claim NO provenance;
    /// correction on a lifecycle-deactivated row re-evaluates the SAME rule deterministically
    /// (still past the grace → stays deactivated; not past it — future, today, or a correction INTO
    /// the grace → reactivate + reset, and the poller re-flips on the new D+6; the S70 TASK-7002
    /// accepted deviation, now on the grace rule).
    /// </summary>
    public static (bool IsActive, bool EndDateDeactivated) ComputeEndDateLifecycle(
        DateOnly? newEndDate, bool oldIsActive, bool oldEndDateDeactivated, DateOnly copenhagenToday)
    {
        if (newEndDate is null)
        {
            // R1(c) clear: reactivate ONLY on lifecycle provenance (then reset it); a
            // manually-deactivated user keeps is_active=false. Provenance always resets —
            // with no end date there is nothing for it to claim.
            return oldEndDateDeactivated ? (true, false) : (oldIsActive, false);
        }

        // S147 / TASK-14710 — the one grace helper (D+6), shared with Step A's IsDeactivationDue.
        var pastGrace = IsPastGrace(newEndDate.Value, copenhagenToday);

        if (oldIsActive)
        {
            // R1(a) past the grace → same-tx deactivate with provenance;
            // R1(b) future-dated, endDate == today (still the last EMPLOYED day), or within the
            // manager grace (D+1 .. D+5) → store only; the Step-A poller flips on D+6.
            return pastGrace ? (false, true) : (true, false);
        }

        if (oldEndDateDeactivated)
        {
            // Correction on a lifecycle-deactivated row: deterministic re-evaluation of the
            // SAME rule. Still past the grace → stays deactivated (provenance kept); otherwise
            // (a future date, today, or a correction INTO the grace) the lifecycle basis is gone
            // → reactivate + reset (the Step-A poller re-flips on the corrected D+6).
            return pastGrace ? (false, true) : (true, false);
        }

        // R1(d) manually-inactive: record the date, claim NO provenance, leave is_active alone.
        return (false, false);
    }
}

/// <summary>
/// S71 / TASK-7104 — the outcome of one <see cref="EmploymentEndDateLifecycleWriter.ApplyAsync"/>
/// call: the <c>users.version</c> transition pair + the R1 lifecycle outcome (the old tuple for
/// the caller's own records, the new tuple for response shaping / supersession eligibility).
/// Init-only record (PAT-001).
/// </summary>
public sealed record EmploymentEndDateLifecycleResult
{
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
    public DateOnly? OldEndDate { get; init; }
    public required bool OldIsActive { get; init; }
    public required bool OldEndDateDeactivated { get; init; }
    public required bool NewIsActive { get; init; }
    public required bool NewEndDateDeactivated { get; init; }
}
