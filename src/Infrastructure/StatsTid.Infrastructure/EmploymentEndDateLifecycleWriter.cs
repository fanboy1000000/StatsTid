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
    /// <param name="copenhagenToday">The Copenhagen business date (the R1 "passed" comparison
    /// authority — the caller resolves it from its injected TimeProvider; PAT-008).</param>
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
    /// is the LAST day employed, so "passed" means <paramref name="copenhagenToday"/> is STRICTLY
    /// after it. The R1(a)–(d) mapping:
    /// (a) set, already-passed, active row → deactivate with provenance;
    /// (b) set, future-dated (incl. == today) → store only, no flip (the Step-A poller flips);
    /// (c) clear → reactivate ONLY on lifecycle provenance (then reset it); a manually-deactivated
    ///     user keeps <c>is_active=false</c>; provenance always resets;
    /// (d) set on a manually-inactive row → record the date, claim NO provenance;
    /// correction on a lifecycle-deactivated row re-evaluates the SAME rule deterministically
    /// (still-passed → stays deactivated; unpassed → reactivate + reset — the S70 TASK-7002
    /// accepted deviation).
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

        var passed = copenhagenToday > newEndDate.Value;

        if (oldIsActive)
        {
            // R1(a) already-passed → same-tx deactivate with provenance;
            // R1(b) future-dated (incl. endDate == today: still the last EMPLOYED day) → no flip.
            return passed ? (false, true) : (true, false);
        }

        if (oldEndDateDeactivated)
        {
            // Correction on a lifecycle-deactivated row: deterministic re-evaluation of the
            // SAME rule. Still-passed → stays deactivated (provenance kept); unpassed → the
            // lifecycle basis is gone → reactivate + reset (the Step-A poller re-flips later).
            return passed ? (false, true) : (true, false);
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
