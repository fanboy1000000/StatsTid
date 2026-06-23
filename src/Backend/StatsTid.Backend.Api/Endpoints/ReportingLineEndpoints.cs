using System.Data;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ReportingLineEndpoints
{
    public static WebApplication MapReportingLineEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // Endpoint 1: POST /api/admin/reporting-lines — Assign/reassign PRIMARY manager
        // ═══════════════════════════════════════════

        app.MapPost("/api/admin/reporting-lines", async (
            AssignReportingLineRequest request,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 — wrap the whole body in the bounded drift-retry loop: if AcquireTreeLockForEmployeeAsync
        // detects a concurrent cross-styrelse transfer drifted the advisory key, the attempt rolls back
        // (no side effects — the drift check precedes every FOR UPDATE/mutation) and re-runs on a fresh tx.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();
            var relationship = string.IsNullOrWhiteSpace(request.Relationship) ? "PRIMARY" : request.Relationship;

            // 1. Validate org scope: actor must cover employee's org. S91 TASK-9102: this tree-page
            //    surface is opened to LocalHR — HROrAbove policy → LocalHR floor (the admitting scope
            //    must itself be HR-or-above; a below-HR scope covering the employee's styrelse cannot
            //    satisfy this writer gate). Org-scope CONTAINMENT is unchanged — an HR actor stays
            //    bounded to its own org subtree (the floor only drops LocalAdmin → LocalHR).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse ETag concurrency precondition.
            //    If-None-Match: * → first assignment (expectedVersion = null).
            //    If-Match: "<version>" → reassignment (expectedVersion = parsed long).
            if (!EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Fetch predecessor (if any) BEFORE the write tx to determine if this is
            //    a first assignment or a supersession — needed for audit action routing.
            var predecessor = expectedVersion is not null
                ? await repo.GetActiveByEmployeeAndRelationshipAsync(request.EmployeeId, relationship, ct)
                : null;

            // 4. Single-transaction: state + audit + outbox (ADR-018 D3).
            //    S74-7403 B1/B1-companion: the same-tree + manager-active validation now runs
            //    IN-TX, AFTER the tree lock, under ReadCommitted — so a concurrent R10 delete cannot
            //    deactivate the manager between validation and insert and leave an active edge to an
            //    inactive manager (orphan). Order: open tx → resolve employee tree root → acquire
            //    tree lock → validate same-tree/manager-active in-tx → cycle guard → AssignAsync.
            ReportingLine persisted;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // S74-7403 B1 / S78 R9 — TOTAL LOCK ORDER (identical on every path, deadlock-safe):
                    //   (1) tree advisory lock  →  (2) the two user rows id-ordered FOR UPDATE (inside
                    //   ValidateSameTreeAsync)  →  (3) cycle guard  →  (4) AssignAsync's slot FOR UPDATE
                    //   on reporting_lines. NO user row is locked before the advisory, so a transaction
                    //   blocked on the advisory holds no user row and cannot deadlock the advisory holder.
                    //
                    // Step 1 (S78 R9): take the tree-wide advisory lock FIRST via the DRIFT-GUARDED
                    // acquire — it derives the employee's tree root UNLOCKED, acquires the advisory, then
                    // RE-DERIVES under the held lock and throws TreeRootDriftException if a concurrent
                    // cross-styrelse transfer moved the employee in between (the TreeRootDriftRetry.RunAsync
                    // wrapper rolls back + retries on a fresh tx). This CLOSES the S74-7403 stale-key
                    // residual: the held advisory key is provably the employee's current tree root, so two
                    // paths on the same employee genuinely mutually exclude even under a simultaneous
                    // transfer. A transaction parked on the advisory holds NO user rows.
                    var employeeTreeRoot = await repo.AcquireTreeLockForEmployeeAsync(conn, tx, request.EmployeeId, ct);

                    // Step 2: validate same-tree + manager-active IN-TX, UNDER the held advisory, and
                    // PIN BOTH the employee + manager `users` rows FOR UPDATE in id-order (B1 — so
                    // neither party can be transferred between this check and the edge insert: the
                    // cross-tree-edge race, ADR-027 D2). Sees the current committed user state, so a
                    // concurrent R10 that deactivated the manager (committed before us) makes it read
                    // inactive here → 400, no orphan. Returns the AUTHORITATIVE common tree root.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, request.EmployeeId, request.ManagerId, ct);

                    // S78 R9 — the S74-7403 stale-key residual is now CLOSED. The advisory key is no
                    // longer taken from an unguarded unlocked read: AcquireTreeLockForEmployeeAsync (Step 1)
                    // re-derives the root under the held advisory and signals a retry on drift, so the key
                    // this transaction holds IS the employee's current tree root. ValidateSameTreeAsync
                    // (below) still pins BOTH user rows FOR UPDATE in id-order and re-resolves the common
                    // root — defence-in-depth that also rejects a cross-tree manager (the manager may be in
                    // a different tree even with a non-stale employee key) with CrossTreeAssignmentException
                    // → 400. The order stays advisory → user rows → slot (no inversion); the simultaneous-
                    // transfer cycle window the prior pass deferred is eliminated by the drift guard.

                    // Step 3: reject an approver that is the employee or any descendant of the
                    // employee (would form a cycle).
                    await repo.GuardNoCycleAsync(conn, tx, request.EmployeeId, request.ManagerId, ct);

                    // Build the ReportingLine model now that the tree root is validated in-tx.
                    var newLine = new ReportingLine
                    {
                        ReportingLineId = Guid.NewGuid(),
                        EmployeeId = request.EmployeeId,
                        ManagerId = request.ManagerId,
                        TreeRootOrgId = treeRootOrgId,
                        Relationship = relationship,
                        EffectiveFrom = request.EffectiveFrom,
                        EffectiveTo = null,
                        Source = "MANUAL",
                        Version = 1,
                        CreatedBy = actor.ActorId ?? "system",
                        CreatedAt = DateTime.UtcNow,
                    };

                    persisted = await repo.AssignAsync(conn, tx, expectedVersion, newLine, ct);

                    // Determine audit action.
                    var isSupersession = predecessor is not null;
                    var auditAction = isSupersession ? "SUPERSEDED" : "ASSIGNED";

                    // Audit row.
                    await using var auditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO reporting_line_audit
                            (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                        VALUES
                            (@lineId, @action, @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
                        """, conn, tx);
                    auditCmd.Parameters.AddWithValue("lineId", persisted.ReportingLineId);
                    auditCmd.Parameters.AddWithValue("action", auditAction);
                    auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                    auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionBefore", isSupersession ? (object)predecessor!.Version : DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionAfter", (object)persisted.Version);
                    auditCmd.Parameters.AddWithValue("metadata", (object?)null ?? DBNull.Value);
                    await auditCmd.ExecuteNonQueryAsync(ct);

                    // Outbox event: ReportingLineAssigned for the new line.
                    var streamId = $"reporting-line-{request.EmployeeId}";
                    var assignedEvent = new ReportingLineAssigned
                    {
                        ReportingLineId = persisted.ReportingLineId,
                        EmployeeId = persisted.EmployeeId,
                        ManagerId = persisted.ManagerId,
                        TreeRootOrgId = persisted.TreeRootOrgId,
                        Relationship = persisted.Relationship,
                        EffectiveFrom = persisted.EffectiveFrom,
                        Source = persisted.Source,
                        RowVersion = persisted.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, assignedEvent, ct);

                    // If supersession, also emit ReportingLineSuperseded for the closed predecessor.
                    if (isSupersession)
                    {
                        var supersededEvent = new ReportingLineSuperseded
                        {
                            ReportingLineId = predecessor!.ReportingLineId,
                            EmployeeId = predecessor.EmployeeId,
                            PreviousManagerId = predecessor.ManagerId,
                            NewManagerId = persisted.ManagerId,
                            TreeRootOrgId = predecessor.TreeRootOrgId,
                            EffectiveFrom = predecessor.EffectiveFrom,
                            EffectiveTo = persisted.EffectiveFrom,
                            RowVersion = predecessor.Version + 1, // closed predecessor got version bumped
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, streamId, supersededEvent, ct);
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (ReportingCycleException ex)
            {
                // S74 R8 — the chosen manager is the employee or a descendant → 409.
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
            catch (CrossTreeAssignmentException ex)
            {
                // S74-7403 B1-companion — same-tree validation moved in-tx; the manager and
                // employee belong to different reporting trees → 400.
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                // S74-7403 W1 — ValidateSameTreeAsync (now in-tx) throws when a user/org cannot be
                // resolved (missing/inactive user, or no MAO/ORGANISATION ancestor) → clean 400
                // rather than a 500.
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }

            context.Response.Headers.ETag = $"\"{persisted.Version}\"";
            var isFirstAssignment = predecessor is null;
            return isFirstAssignment
                ? Results.Created($"/api/admin/reporting-lines/{persisted.EmployeeId}", MapLineResponse(persisted))
                : Results.Ok(MapLineResponse(persisted));
        })).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR. S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 2: DELETE /api/admin/reporting-lines/{employeeId} — Remove PRIMARY line
        // ═══════════════════════════════════════════

        app.MapDelete("/api/admin/reporting-lines/{employeeId}", async (
            string employeeId,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 — bounded drift-retry wrapper (see Endpoint 1): a concurrent transfer that drifts the
        // removed person's tree key under the advisory rolls back + retries on a fresh tx.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            // 1. Validate scope. S91 TASK-9102: tree-page surface opened to LocalHR — HROrAbove
            //    policy → LocalHR floor. Org-scope containment unchanged (HR stays org-bounded).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse If-Match (strict — must have version for DELETE).
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Atomic tx: remove + audit + outbox.
            ReportingLine closed;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // S74-7403 B1: ReadCommitted — the FOR UPDATE slot lock + version check in RemoveAsync
                // and the in-tx root-invariant census all read committed state correctly.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // ── S74-7403 C2-3 / B1 + S78 R9: acquire the tree advisory lock BEFORE the
                    //    root-invariant census, via the DRIFT-GUARDED acquire (the same shared primitive
                    //    the assigns use). Without the lock the census ran un-serialized: it could read a
                    //    one-root state and then a concurrent assign commits a SECOND root the instant
                    //    afterward (ADR-027 D9 ≤1-root violation). Holding the lock for the whole
                    //    census→close serializes this DELETE against every assign in the same tree. S78
                    //    R9 additionally CLOSES the stale-key residual: AcquireTreeLockForEmployeeAsync
                    //    re-derives the removed person's root under the held advisory and signals a retry
                    //    (TreeRootDriftException → TreeRootDriftRetry.RunAsync) if a concurrent cross-styrelse
                    //    transfer moved them, so the key is never stale. Lock order (the SAME global order
                    //    as every path): tree advisory lock FIRST → RemoveAsync's slot FOR UPDATE →
                    //    census. This DELETE pins NO user row (it inserts no edge, so B1's
                    //    cross-tree-edge race does not apply).
                    var deletedTreeRoot = await repo.AcquireTreeLockForEmployeeAsync(conn, tx, employeeId, ct);

                    closed = await repo.RemoveAsync(conn, tx, expectedVersion, employeeId, "PRIMARY", ct);

                    // Root invariant: reject if this creates a second root (Codex S48 W2, scoped to tree per cycle 2 W1).
                    var treeRoot = closed.TreeRootOrgId;
                    await using var rootCheckCmd = new NpgsqlCommand(
                        """
                        SELECT COUNT(*) FROM (
                            SELECT rl_all.employee_id
                            FROM reporting_lines rl_all
                            WHERE rl_all.tree_root_org_id = @treeRoot
                              AND rl_all.effective_to IS NULL
                              AND rl_all.relationship = 'PRIMARY'
                        ) active_subordinates
                        """, conn, tx);
                    rootCheckCmd.Parameters.AddWithValue("treeRoot", treeRoot);
                    var activeSubordinateCount = (long)(await rootCheckCmd.ExecuteScalarAsync(ct))!;
                    if (activeSubordinateCount > 0)
                    {
                        // Tree still has active lines — the just-deleted employee becomes a second root.
                        // Block unless this was the last subordinate (in which case the tree collapses to just the root).
                        // Check if ANY other employee in this tree also lacks a PRIMARY line (would be a second root).
                        await using var multiRootCmd = new NpgsqlCommand(
                            """
                            SELECT COUNT(*) FROM (
                                SELECT DISTINCT rl.manager_id
                                FROM reporting_lines rl
                                WHERE rl.tree_root_org_id = @treeRoot
                                  AND rl.effective_to IS NULL
                                  AND rl.relationship = 'PRIMARY'
                                  AND rl.manager_id NOT IN (
                                      SELECT rl2.employee_id
                                      FROM reporting_lines rl2
                                      WHERE rl2.tree_root_org_id = @treeRoot
                                        AND rl2.effective_to IS NULL
                                        AND rl2.relationship = 'PRIMARY'
                                  )
                            ) tree_roots
                            """, conn, tx);
                        multiRootCmd.Parameters.AddWithValue("treeRoot", treeRoot);
                        var treeRootCount = (long)(await multiRootCmd.ExecuteScalarAsync(ct))!;
                        if (treeRootCount > 1)
                        {
                            await tx.RollbackAsync(ct);
                            return Results.Json(new { error = "Removing this reporting line would create multiple roots in the tree" }, statusCode: 409);
                        }
                    }

                    // Audit row.
                    await using var auditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO reporting_line_audit
                            (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                        VALUES
                            (@lineId, @action, @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
                        """, conn, tx);
                    auditCmd.Parameters.AddWithValue("lineId", closed.ReportingLineId);
                    auditCmd.Parameters.AddWithValue("action", "SUPERSEDED");
                    auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                    auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionBefore", (object)expectedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", (object)closed.Version);
                    auditCmd.Parameters.AddWithValue("metadata", (object?)null ?? DBNull.Value);
                    await auditCmd.ExecuteNonQueryAsync(ct);

                    // Outbox event: ReportingLineSuperseded with NewManagerId = null.
                    var streamId = $"reporting-line-{employeeId}";
                    var supersededEvent = new ReportingLineSuperseded
                    {
                        ReportingLineId = closed.ReportingLineId,
                        EmployeeId = closed.EmployeeId,
                        PreviousManagerId = closed.ManagerId,
                        NewManagerId = null,
                        TreeRootOrgId = closed.TreeRootOrgId,
                        EffectiveFrom = closed.EffectiveFrom,
                        EffectiveTo = closed.EffectiveTo!.Value,
                        RowVersion = closed.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, supersededEvent, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            context.Response.Headers.ETag = $"\"{closed.Version}\"";
            return Results.NoContent();
        })).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR. S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 3: GET /api/admin/reporting-lines/tree/{treeRootOrgId} — Get tree
        // ═══════════════════════════════════════════

        app.MapGet("/api/admin/reporting-lines/tree/{treeRootOrgId}", async (
            string treeRootOrgId,
            ReportingLineRepository repo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate scope covers tree root org. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var lines = await repo.GetTreeAsync(treeRootOrgId, ct);

            // Enrich with display names: collect unique user IDs (employees + managers),
            // look them up, build a display-name map.
            var userIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                userIds.Add(line.EmployeeId);
                userIds.Add(line.ManagerId);
            }

            var displayNames = await LookupDisplayNamesAsync(connectionFactory, userIds, ct);

            var enriched = lines.Select(l => new
            {
                reportingLineId = l.ReportingLineId,
                employeeId = l.EmployeeId,
                employeeDisplayName = displayNames.GetValueOrDefault(l.EmployeeId),
                managerId = l.ManagerId,
                managerDisplayName = displayNames.GetValueOrDefault(l.ManagerId),
                treeRootOrgId = l.TreeRootOrgId,
                relationship = l.Relationship,
                effectiveFrom = l.EffectiveFrom,
                source = l.Source,
                version = l.Version,
            });

            return Results.Ok(enriched);
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 4: GET /api/admin/reporting-lines/{employeeId} — Get employee's lines + history
        // ═══════════════════════════════════════════

        app.MapGet("/api/admin/reporting-lines/{employeeId}", async (
            string employeeId,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // EmployeeOrAbove: if Employee role, can only view own data.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var active = await repo.GetActiveByEmployeeAsync(employeeId, ct);
            var history = await repo.GetHistoryAsync(employeeId, ct);

            // Set ETag from the first active line's version (PRIMARY takes precedence).
            var primaryLine = active.FirstOrDefault(l => l.Relationship == "PRIMARY");
            if (primaryLine is not null)
                context.Response.Headers.ETag = $"\"{primaryLine.Version}\"";

            return Results.Ok(new
            {
                active = active.Select(MapLineResponse),
                history = history.Select(MapLineResponse),
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 5: GET /api/admin/reporting-lines/{managerId}/reports — Get direct reports
        // ═══════════════════════════════════════════

        app.MapGet("/api/admin/reporting-lines/{managerId}/reports", async (
            string managerId,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate scope covers manager's employee data.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, managerId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var reports = await repo.GetDirectReportsAsync(managerId, ct);

            // Enrich with employee display names.
            var employeeIds = new HashSet<string>(reports.Select(r => r.EmployeeId), StringComparer.Ordinal);
            var displayNames = await LookupDisplayNamesAsync(connectionFactory, employeeIds, ct);

            var enriched = reports.Select(r => new
            {
                reportingLineId = r.ReportingLineId,
                employeeId = r.EmployeeId,
                employeeDisplayName = displayNames.GetValueOrDefault(r.EmployeeId),
                managerId = r.ManagerId,
                treeRootOrgId = r.TreeRootOrgId,
                relationship = r.Relationship,
                effectiveFrom = r.EffectiveFrom,
                source = r.Source,
                version = r.Version,
            });

            return Results.Ok(enriched);
        }).RequireAuthorization("LeaderOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 6: POST /api/admin/reporting-lines/{employeeId}/acting — Assign acting manager
        // ═══════════════════════════════════════════

        app.MapPost("/api/admin/reporting-lines/{employeeId}/acting", async (
            string employeeId,
            AssignActingManagerRequest request,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 — bounded drift-retry wrapper (see Endpoint 1): a concurrent transfer that drifts the
        // employee's tree key under the advisory rolls back + retries on a fresh tx.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            // 1. Validate scope. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse ETag concurrency precondition.
            if (!EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Fetch predecessor for audit routing.
            var predecessor = expectedVersion is not null
                ? await repo.GetActiveByEmployeeAndRelationshipAsync(employeeId, "ACTING", ct)
                : null;

            // 4. Atomic tx.
            //    S74-7403 B1/B1-companion: same-tree + manager-active validation runs IN-TX, after
            //    the tree lock, under ReadCommitted (an ACTING edge to a concurrently-deactivated
            //    manager would be an orphan just like a PRIMARY one). Order: open tx → resolve
            //    employee tree root → acquire tree lock → validate same-tree/manager-active in-tx →
            //    cycle guard → AssignAsync.
            ReportingLine persisted;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // S74-7403 B1 / S78 R9 — TOTAL LOCK ORDER (identical to the PRIMARY assign path):
                    //   advisory → user rows id-ordered FOR UPDATE (in ValidateSameTreeAsync) → cycle
                    //   guard → slot. Step 1: take the tree-wide advisory lock FIRST via the DRIFT-GUARDED
                    //   acquire (the ACTING assign path can form a cycle too). It re-derives the
                    //   employee's root under the held lock and signals a retry on a concurrent transfer
                    //   (TreeRootDriftException → TreeRootDriftRetry.RunAsync), so the held key is non-stale
                    //   (S74-7403 residual closed). Parked → no user rows.
                    var employeeTreeRoot = await repo.AcquireTreeLockForEmployeeAsync(conn, tx, employeeId, ct);

                    // Step 2: same-tree + manager-active in-tx UNDER the lock; pins BOTH user rows
                    // FOR UPDATE in id-order (B1). Returns the authoritative common tree root.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, employeeId, request.ManagerId, ct);

                    // S78 R9 — the S74-7403 stale-key residual is CLOSED on this path too (identical to
                    // the PRIMARY assign): AcquireTreeLockForEmployeeAsync (Step 1) holds the employee's
                    // CURRENT tree root via the drift guard. ValidateSameTreeAsync (run ABOVE, after both
                    // user rows are pinned FOR UPDATE) still rejects a cross-tree manager with
                    // CrossTreeAssignmentException → 400 (defence-in-depth). Order stays advisory → rows →
                    // slot; the simultaneous-transfer window is eliminated by the drift guard.

                    // Step 3: cycle guard.
                    await repo.GuardNoCycleAsync(conn, tx, employeeId, request.ManagerId, ct);

                    // Build ACTING line now that the tree root is validated in-tx.
                    var newLine = new ReportingLine
                    {
                        ReportingLineId = Guid.NewGuid(),
                        EmployeeId = employeeId,
                        ManagerId = request.ManagerId,
                        TreeRootOrgId = treeRootOrgId,
                        Relationship = "ACTING",
                        EffectiveFrom = request.EffectiveFrom,
                        EffectiveTo = null,
                        Source = "MANUAL",
                        Version = 1,
                        CreatedBy = actor.ActorId ?? "system",
                        CreatedAt = DateTime.UtcNow,
                    };

                    persisted = await repo.AssignAsync(conn, tx, expectedVersion, newLine, ct);

                    var isSupersession = predecessor is not null;
                    var auditAction = isSupersession ? "SUPERSEDED" : "ACTING_ASSIGNED";

                    // Audit row.
                    await using var auditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO reporting_line_audit
                            (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                        VALUES
                            (@lineId, @action, @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
                        """, conn, tx);
                    auditCmd.Parameters.AddWithValue("lineId", persisted.ReportingLineId);
                    auditCmd.Parameters.AddWithValue("action", auditAction);
                    auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                    auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionBefore", isSupersession ? (object)predecessor!.Version : DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionAfter", (object)persisted.Version);
                    auditCmd.Parameters.AddWithValue("metadata", (object?)null ?? DBNull.Value);
                    await auditCmd.ExecuteNonQueryAsync(ct);

                    // Outbox event.
                    var streamId = $"reporting-line-{employeeId}";
                    var assignedEvent = new ReportingLineAssigned
                    {
                        ReportingLineId = persisted.ReportingLineId,
                        EmployeeId = persisted.EmployeeId,
                        ManagerId = persisted.ManagerId,
                        TreeRootOrgId = persisted.TreeRootOrgId,
                        Relationship = persisted.Relationship,
                        EffectiveFrom = persisted.EffectiveFrom,
                        Source = persisted.Source,
                        RowVersion = persisted.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, assignedEvent, ct);

                    // If supersession, emit ReportingLineSuperseded for the closed predecessor.
                    if (isSupersession)
                    {
                        var supersededEvent = new ReportingLineSuperseded
                        {
                            ReportingLineId = predecessor!.ReportingLineId,
                            EmployeeId = predecessor.EmployeeId,
                            PreviousManagerId = predecessor.ManagerId,
                            NewManagerId = persisted.ManagerId,
                            TreeRootOrgId = predecessor.TreeRootOrgId,
                            EffectiveFrom = predecessor.EffectiveFrom,
                            EffectiveTo = persisted.EffectiveFrom,
                            RowVersion = predecessor.Version + 1,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, streamId, supersededEvent, ct);
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (ReportingCycleException ex)
            {
                // S74 R8 — the chosen acting manager is the employee or a descendant → 409.
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
            catch (CrossTreeAssignmentException ex)
            {
                // S74-7403 B1-companion — same-tree validation moved in-tx → 400.
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                // S74-7403 W1 — unresolvable user/org from the in-tx ValidateSameTreeAsync → 400.
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }

            context.Response.Headers.ETag = $"\"{persisted.Version}\"";
            var isFirstAssignment = predecessor is null;
            return isFirstAssignment
                ? Results.Created($"/api/admin/reporting-lines/{employeeId}/acting", MapLineResponse(persisted))
                : Results.Ok(MapLineResponse(persisted));
        })).RequireAuthorization("LocalAdminOrAbove"); // S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 7: DELETE /api/admin/reporting-lines/{employeeId}/acting — Remove acting manager
        // ═══════════════════════════════════════════

        app.MapDelete("/api/admin/reporting-lines/{employeeId}/acting", async (
            string employeeId,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Validate scope. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse If-Match (strict).
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Atomic tx.
            ReportingLine closed;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // S74-7403 B1: ReadCommitted — RemoveAsync's FOR UPDATE slot lock + version check
                // read committed state correctly.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    closed = await repo.RemoveAsync(conn, tx, expectedVersion, employeeId, "ACTING", ct);

                    // Audit row.
                    await using var auditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO reporting_line_audit
                            (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                        VALUES
                            (@lineId, @action, @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
                        """, conn, tx);
                    auditCmd.Parameters.AddWithValue("lineId", closed.ReportingLineId);
                    auditCmd.Parameters.AddWithValue("action", "ACTING_ENDED");
                    auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                    auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                    auditCmd.Parameters.AddWithValue("versionBefore", (object)expectedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", (object)closed.Version);
                    auditCmd.Parameters.AddWithValue("metadata", (object?)null ?? DBNull.Value);
                    await auditCmd.ExecuteNonQueryAsync(ct);

                    // Outbox event.
                    var streamId = $"reporting-line-{employeeId}";
                    var supersededEvent = new ReportingLineSuperseded
                    {
                        ReportingLineId = closed.ReportingLineId,
                        EmployeeId = closed.EmployeeId,
                        PreviousManagerId = closed.ManagerId,
                        NewManagerId = null,
                        TreeRootOrgId = closed.TreeRootOrgId,
                        EffectiveFrom = closed.EffectiveFrom,
                        EffectiveTo = closed.EffectiveTo!.Value,
                        RowVersion = closed.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, supersededEvent, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            context.Response.Headers.ETag = $"\"{closed.Version}\"";
            return Results.NoContent();
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 8: POST /api/admin/reporting-lines/import — Bulk HR import
        // ═══════════════════════════════════════════

        app.MapPost("/api/admin/reporting-lines/import", async (
            ImportReportingLinesRequest request,
            ReportingLineRepository repo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Basic payload validation.
            if (string.IsNullOrWhiteSpace(request.TreeRootOrgId))
                return Results.BadRequest(new { error = "treeRootOrgId is required" });
            if (request.Rows is null || request.Rows.Count == 0)
                return Results.BadRequest(new { error = "rows must not be empty" });

            // S76 B1 — bulk-import org-scope gap. The GlobalAdminOnly policy gates the ROLE but
            // this endpoint previously called NEITHER scope validator, so the actor's scope was
            // never bound to the declared tree root. Require that an ADMIN-grade scope
            // (LocalAdmin floor — a GlobalAdmin's GLOBAL scope clears it; a mixed-role non-admin
            // scope does not) actually covers request.TreeRootOrgId before any write.
            var (treeAllowed, treeReason) = await scopeValidator.ValidateOrgAccessAsync(
                actor, request.TreeRootOrgId, StatsTidRoles.LocalAdmin, ct);
            if (!treeAllowed)
                return Results.Json(new { error = "Access denied", reason = treeReason }, statusCode: 403);

            // 2. Pre-validation pass — collect per-row errors without touching the database.
            var errors = new List<object>();

            // Collect all unique user IDs for batch lookup.
            var allUserIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < request.Rows.Count; i++)
            {
                var row = request.Rows[i];
                if (string.IsNullOrWhiteSpace(row.EmployeeId))
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = "employeeId is required" });
                else
                    allUserIds.Add(row.EmployeeId);

                if (string.IsNullOrWhiteSpace(row.ManagerId))
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = "managerId is required" });
                else
                    allUserIds.Add(row.ManagerId);

                if (!string.IsNullOrWhiteSpace(row.EmployeeId) && !string.IsNullOrWhiteSpace(row.ManagerId) && row.EmployeeId == row.ManagerId)
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = "Employee cannot be their own manager" });

                if (string.IsNullOrWhiteSpace(row.EffectiveFrom) || !DateOnly.TryParse(row.EffectiveFrom, out _))
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Invalid effectiveFrom date: '{row.EffectiveFrom}'" });
            }

            // If we already have structural errors, return early.
            if (errors.Count > 0)
                return Results.Json(new { error = "Validation failed", errors }, statusCode: 400);

            // Batch lookup: existence + active status.
            Dictionary<string, (bool IsActive, string PrimaryOrgId)> userLookup;
            {
                await using var lookupConn = connectionFactory.Create();
                await lookupConn.OpenAsync(ct);
                await using var lookupCmd = new NpgsqlCommand(
                    "SELECT user_id, is_active, primary_org_id FROM users WHERE user_id = ANY(@ids)",
                    lookupConn);
                lookupCmd.Parameters.AddWithValue("ids", allUserIds.ToArray());
                userLookup = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
                await using var reader = await lookupCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var userId = reader.GetString(0);
                    var isActive = reader.GetBoolean(1);
                    var primaryOrgId = reader.GetString(2);
                    userLookup[userId] = (isActive, primaryOrgId);
                }
            }

            // Batch resolve unique org IDs to tree roots.
            var uniqueOrgIds = new HashSet<string>(userLookup.Values.Select(v => v.PrimaryOrgId), StringComparer.Ordinal);
            var orgToTreeRoot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var orgId in uniqueOrgIds)
            {
                try
                {
                    var treeRoot = await repo.ResolveTreeRootOrgIdAsync(orgId, ct);
                    orgToTreeRoot[orgId] = treeRoot;
                }
                catch (InvalidOperationException)
                {
                    // Will surface as per-row error below.
                }
            }

            // Validate each row against lookup data.
            for (var i = 0; i < request.Rows.Count; i++)
            {
                var row = request.Rows[i];
                if (string.IsNullOrWhiteSpace(row.EmployeeId) || string.IsNullOrWhiteSpace(row.ManagerId))
                    continue; // Already caught above.

                // Employee exists and active?
                if (!userLookup.TryGetValue(row.EmployeeId, out var empInfo))
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Employee '{row.EmployeeId}' not found" });
                    continue;
                }
                if (!empInfo.IsActive)
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Employee '{row.EmployeeId}' is inactive" });
                    continue;
                }

                // Manager exists and active?
                if (!userLookup.TryGetValue(row.ManagerId, out var mgrInfo))
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Manager '{row.ManagerId}' not found" });
                    continue;
                }
                if (!mgrInfo.IsActive)
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Manager '{row.ManagerId}' is inactive" });
                    continue;
                }

                // Both resolve to the same tree root as treeRootOrgId?
                if (!orgToTreeRoot.TryGetValue(empInfo.PrimaryOrgId, out var empTreeRoot))
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Cannot resolve tree root for employee '{row.EmployeeId}'" });
                    continue;
                }
                if (empTreeRoot != request.TreeRootOrgId)
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Employee '{row.EmployeeId}' belongs to tree '{empTreeRoot}', not '{request.TreeRootOrgId}'" });
                    continue;
                }

                if (!orgToTreeRoot.TryGetValue(mgrInfo.PrimaryOrgId, out var mgrTreeRoot))
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Cannot resolve tree root for manager '{row.ManagerId}'" });
                    continue;
                }
                if (mgrTreeRoot != request.TreeRootOrgId)
                {
                    errors.Add(new { row = i, employeeId = row.EmployeeId, managerId = row.ManagerId, reason = $"Manager '{row.ManagerId}' belongs to tree '{mgrTreeRoot}', not '{request.TreeRootOrgId}'" });
                    continue;
                }
            }

            if (errors.Count > 0)
                return Results.Json(new { error = "Validation failed", errors }, statusCode: 400);

            // 3. Atomic write pass — single ReadCommitted transaction (S74-7403 B1).
            int imported = 0, superseded = 0, skipped = 0;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // S74-7403 B1 total lock order, step 1: take the tree advisory lock ONCE upfront on
                    // the batch's declared tree root, BEFORE any user row is pinned. All rows were
                    // pre-validated to resolve to request.TreeRootOrgId, so the whole import belongs to
                    // one tree and serializes through this one lock — bringing the bulk path to parity
                    // with the guarded single assigns under the SAME global order (advisory → user rows
                    // id-ordered FOR UPDATE → cycle guard → slot): the per-row ValidateSameTreeAsync
                    // below pins each edge's user pair AFTER this advisory, so a concurrent same-tree
                    // assign (also advisory-first) cannot deadlock against the import.
                    await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, request.TreeRootOrgId, ct);

                    foreach (var row in request.Rows)
                    {
                        var effectiveFrom = DateOnly.Parse(row.EffectiveFrom);

                        // Read current active PRIMARY line via the shared conn/tx.
                        ReportingLine? current;
                        {
                            await using var readCmd = new NpgsqlCommand(
                                """
                                SELECT * FROM reporting_lines
                                WHERE employee_id = @empId
                                  AND relationship = 'PRIMARY'
                                  AND effective_to IS NULL
                                """, conn, tx);
                            readCmd.Parameters.AddWithValue("empId", row.EmployeeId);
                            await using var rdr = await readCmd.ExecuteReaderAsync(ct);
                            current = await rdr.ReadAsync(ct) ? MapReaderForImport(rdr) : null;
                        }

                        // ── S74-7403 B2: VALIDATE EVERY edge IN-TX *BEFORE* the skip-existing check, so
                        //    no edge that results in an insert/supersede can bypass the manager-active +
                        //    same-tree validation. Previously a row whose manager matched the current
                        //    active edge was skipped BEFORE this validation; the validation now runs
                        //    first for every row, and only a TRUE no-op (a still-active existing edge to
                        //    the SAME manager — which inserts/supersedes nothing) is skipped afterward.
                        //    AUTHORITATIVE in-tx manager-active + same-tree validation UNDER the held tree
                        //    lock (ReadCommitted ⇒ sees the current committed user state). The out-of-tx
                        //    pre-validation (the early 400) is only a fast pre-check: a concurrent
                        //    R10-delete could have DEACTIVATED the manager (or an org transfer moved either
                        //    party out of the tree) AFTER that pre-check — inserting an edge to a
                        //    now-inactive manager would be a brand-new D9 orphan. ValidateSameTreeAsync
                        //    re-reads is_active = TRUE for both parties (an inactive/missing one →
                        //    InvalidOperationException), that they share a tree root
                        //    (CrossTreeAssignmentException otherwise), AND pins BOTH rows FOR UPDATE in
                        //    id-order (B1 — no cross-tree-edge race; under the held advisory, step 2 of the
                        //    order); either exception propagates and rolls the WHOLE batch back. We
                        //    additionally pin the resolved root to the import's declared TreeRootOrgId (a
                        //    same-tree pair that drifted to ANOTHER tree must not be silently imported).
                        var rowTreeRoot = await repo.ValidateSameTreeAsync(conn, tx, row.EmployeeId, row.ManagerId, ct);
                        if (!string.Equals(rowTreeRoot, request.TreeRootOrgId, StringComparison.Ordinal))
                            throw new CrossTreeAssignmentException(
                                $"Row employee '{row.EmployeeId}'/manager '{row.ManagerId}' now resolves to tree " +
                                $"'{rowTreeRoot}', not the import's declared tree '{request.TreeRootOrgId}'.");

                        if (current is not null && current.ManagerId == row.ManagerId)
                        {
                            // TRUE no-op — same manager already actively assigned (validation above
                            // confirmed both parties active + same-tree). Nothing to insert/supersede.
                            skipped++;
                            continue;
                        }

                        // S74-7403 B2: cycle guard for each imported edge that WILL insert/supersede.
                        // The inserts already applied earlier in this tx are visible here (ReadCommitted,
                        // same tx), so a batch-internal cycle (e.g. row A→B then row B→A) is caught. On a
                        // cycle the ReportingCycleException propagates, rolling the whole import back.
                        await repo.GuardNoCycleAsync(conn, tx, row.EmployeeId, row.ManagerId, ct);

                        var newLine = new ReportingLine
                        {
                            ReportingLineId = Guid.NewGuid(),
                            EmployeeId = row.EmployeeId,
                            ManagerId = row.ManagerId,
                            TreeRootOrgId = request.TreeRootOrgId,
                            Relationship = "PRIMARY",
                            EffectiveFrom = effectiveFrom,
                            EffectiveTo = null,
                            Source = "HR_IMPORT",
                            Version = 1,
                            CreatedBy = actor.ActorId ?? "system",
                            CreatedAt = DateTime.UtcNow,
                        };

                        ReportingLine persisted;
                        if (current is not null)
                        {
                            // Supersede: pass current version for concurrency.
                            persisted = await repo.AssignAsync(conn, tx, current.Version, newLine, ct);
                            superseded++;
                        }
                        else
                        {
                            // First assignment.
                            persisted = await repo.AssignAsync(conn, tx, null, newLine, ct);
                            imported++;
                        }

                        // Audit row for each non-skipped row.
                        await using var auditCmd = new NpgsqlCommand(
                            """
                            INSERT INTO reporting_line_audit
                                (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                            VALUES
                                (@lineId, 'BULK_IMPORTED', @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
                            """, conn, tx);
                        auditCmd.Parameters.AddWithValue("lineId", persisted.ReportingLineId);
                        auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                        auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                        auditCmd.Parameters.AddWithValue("versionBefore", current is not null ? (object)current.Version : DBNull.Value);
                        auditCmd.Parameters.AddWithValue("versionAfter", (object)persisted.Version);
                        auditCmd.Parameters.AddWithValue("metadata", (object?)null ?? DBNull.Value);
                        await auditCmd.ExecuteNonQueryAsync(ct);
                    }

                    // Emit a single batch event via outbox.
                    var batchEvent = new ReportingLineBulkImported
                    {
                        BatchId = Guid.NewGuid(),
                        TreeRootOrgId = request.TreeRootOrgId,
                        LineCount = imported + superseded,
                        Source = "HR_IMPORT",
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, $"reporting-line-import-{request.TreeRootOrgId}", batchEvent, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (CrossTreeAssignmentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ReportingCycleException ex)
            {
                // S74-7403 B2 — an imported edge (or the batch as a whole) would form a cycle. The
                // whole import rolled back; name the offending edge.
                return Results.Json(new
                {
                    error = "Import would create a reporting cycle; the whole import was rolled back",
                    offendingEmployeeId = ex.EmployeeId,
                    offendingManagerId = ex.ManagerId,
                    detail = ex.Message,
                }, statusCode: 409);
            }
            catch (OptimisticConcurrencyException)
            {
                return Results.Json(new { error = "Concurrent modification detected; retry the import" }, statusCode: 409);
            }
            catch (InvalidOperationException ex)
            {
                // S74-7403 C2-2 — the in-tx ValidateSameTreeAsync threw because an edge's manager (or
                // employee) is now inactive/missing, or no MAO/ORGANISATION ancestor resolves. A
                // concurrent R10-delete that deactivated the manager between the out-of-tx pre-check
                // and this in-tx validation lands here → clean 400, whole batch rolled back (no D9
                // orphan inserted to an inactive manager).
                return Results.BadRequest(new { error = "Import failed: a row's manager or employee is no longer active in this tree; refresh and retry", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = "Import failed", detail = ex.Message }, statusCode: 500);
            }

            return Results.Ok(new { imported, superseded, skipped, total = request.Rows.Count });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // Endpoint 8b: POST /api/admin/reporting-lines/{employeeId}/remove — Remove person
        //   from the afgrænsning WITH mandatory reassignment (S74 R10, ADR-027 D9: NO orphans).
        // ═══════════════════════════════════════════
        //
        // "Fjern medarbejder fra afgrænsning" — soft-deactivates the person AND closes the full
        // edge matrix, never leaving an active reference dangling to an inactive user. The request
        // carries a {reportEmployeeId → replacementApproverId} map. PREFLIGHT-409: if any incoming
        // PRIMARY report lacks a replacement, mutate NOTHING and return 409 with the list of report
        // ids needing reassignment. On commit (ONE atomic tx) the 5-step closure matrix runs:
        //   (1) reassign each incoming PRIMARY edge (manager_id = removed) → the supplied
        //       replacement (supersede old → assign new; ReportingLineSuperseded + ReportingLineAssigned
        //       + reporting_line_audit; the replacement passes same-tree + R8 cycle guard);
        //   (2) close incoming ACTING edges (manager_id = removed) — ReportingLineSuperseded + audit;
        //   (3) close the removed person's OWN outgoing PRIMARY/ACTING edges — Superseded + audit;
        //   (4) close manager_vikar rows where removed is absent_approver OR vikar_user —
        //       ManagerVikarEnded (APPROVER_REMOVED) + the ADR-026 sync-in-tx audit trio;
        //   (5) soft-deactivate the user (is_active = FALSE) + UserUpdated event/users_audit.
        app.MapPost("/api/admin/reporting-lines/{employeeId}/remove", async (
            string employeeId,
            RemoveWithReassignmentRequest request,
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AuditProjectionRepository auditRepo,
            IAuditProjectionMapper<ManagerVikarEnded> vikarEndedAuditMapper,
            IAuditProjectionMapper<UserUpdated> userUpdatedAuditMapper,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 — bounded drift-retry wrapper (see Endpoint 1). The 5-step closure derives the removed
        // person's tree root inside the tx (via the drift-guarded acquire); a concurrent transfer that
        // moved them mid-acquire rolls the whole closure back + retries on a fresh tx (the drift check
        // runs BEFORE the in-tx census/closure, so a drifted attempt has NO side effects).
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            // 1. Validate scope: actor must cover the person being removed. S91 TASK-9102: tree-page
            //    surface opened to LocalHR — HROrAbove policy → LocalHR floor. Org-scope containment
            //    unchanged (HR stays bounded to its own org subtree; the replacement-approver edges
            //    below remain same-tree-validated, so reassignment cannot escape the actor's scope).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Build the replacement map (report employee-id → replacement approver-id).
            //    Empty/null map ⇒ no replacements supplied.
            var replacements = request.Replacements ?? new Dictionary<string, string>();

            // 3. PREFLIGHT (read-only, mutate nothing). Find the person's INCOMING active edges
            //    (reports + acting-of) and the person's OWN outgoing active edges; and any
            //    incoming PRIMARY report that lacks a replacement → 409.
            IReadOnlyList<ReportingLine> incomingEdges = await repo.GetDirectReportsAsync(employeeId, ct);
            var incomingPrimary = incomingEdges.Where(e => e.Relationship == "PRIMARY").ToList();
            var incomingActing = incomingEdges.Where(e => e.Relationship == "ACTING").ToList();

            var missing = incomingPrimary
                .Where(e => !replacements.ContainsKey(e.EmployeeId) ||
                            string.IsNullOrWhiteSpace(replacements[e.EmployeeId]))
                .Select(e => e.EmployeeId)
                .ToList();
            if (missing.Count > 0)
            {
                return Results.Json(new
                {
                    error = "Replacement approver required for each active report before removal (ADR-027 D9: no orphans)",
                    reportsNeedingReassignment = missing,
                    reportsNeedingReassignmentCount = missing.Count,
                }, statusCode: 409);
            }

            // 3b. A replacement may not be the removed person themselves, nor one of the reports it
            //     replaces-for (that would just re-orphan / self-cycle). The R8 cycle guard inside
            //     the tx is the authoritative backstop, but reject the trivially-bad ones up front.
            foreach (var rep in incomingPrimary)
            {
                var replacement = replacements[rep.EmployeeId];
                if (string.Equals(replacement, employeeId, StringComparison.Ordinal))
                    return Results.Json(new
                    {
                        error = $"Replacement approver for '{rep.EmployeeId}' cannot be the person being removed ('{employeeId}')",
                    }, statusCode: 422);
            }

            // S74-7403 B4: the AUTHORITATIVE counts come from the in-tx census, not the preflight
            // snapshot. Declared here so the success response reports the edges actually closed.
            int reportsReassignedCount = 0;
            int actingEdgesClosedCount = 0;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // S74-7403 B1: ReadCommitted so each post-lock read sees committed state.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);

                    // ── S74-7403 B4 / S78 R9: acquire the removed person's tree lock FIRST via the
                    //    DRIFT-GUARDED acquire, then RE-READ the incoming edge census IN-TX (the
                    //    out-of-tx preflight above is only a fast pre-check). With the tree lock held for
                    //    the whole census→closure→deactivate, no concurrent assign can interleave a NEW
                    //    report assigned to the removed person after the preflight — which would otherwise
                    //    be left pointing at the now-inactive user (a brand-new orphan; ADR-027 D9). S78
                    //    R9: AcquireTreeLockForEmployeeAsync re-derives the removed person's root under the
                    //    held advisory and signals a retry on a concurrent cross-styrelse transfer
                    //    (TreeRootDriftException → TreeRootDriftRetry.RunAsync), so the lock we hold is on their
                    //    CURRENT tree root (no stale key). We then iterate the AUTHORITATIVE in-tx edge
                    //    set (not the preflight snapshot).
                    var removedTreeRoot = await repo.AcquireTreeLockForEmployeeAsync(conn, tx, employeeId, ct);

                    var incomingEdgesInTx = await repo.GetDirectReportsAsync(conn, tx, employeeId, ct);
                    var incomingPrimaryInTx = incomingEdgesInTx.Where(e => e.Relationship == "PRIMARY").ToList();
                    var incomingActingInTx = incomingEdgesInTx.Where(e => e.Relationship == "ACTING").ToList();
                    reportsReassignedCount = incomingPrimaryInTx.Count;
                    actingEdgesClosedCount = incomingActingInTx.Count;

                    // Re-validate IN-TX (under the lock): every incoming PRIMARY edge must have a
                    // supplied replacement. If a NEW unreplaced report appeared since the preflight,
                    // roll back + 409 — the admin retries and the preflight will then surface it.
                    var missingInTx = incomingPrimaryInTx
                        .Where(e => !replacements.ContainsKey(e.EmployeeId) ||
                                    string.IsNullOrWhiteSpace(replacements[e.EmployeeId]) ||
                                    string.Equals(replacements[e.EmployeeId], employeeId, StringComparison.Ordinal))
                        .Select(e => e.EmployeeId)
                        .ToList();
                    if (missingInTx.Count > 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "A report was assigned to this person concurrently and has no replacement; refresh and retry (ADR-027 D9: no orphans)",
                            reportsNeedingReassignment = missingInTx,
                            reportsNeedingReassignmentCount = missingInTx.Count,
                        }, statusCode: 409);
                    }

                    // ── Step 1: reassign each incoming PRIMARY edge to the supplied replacement.
                    //    Supersede the report's current PRIMARY (held by the removed person) and
                    //    assign a new PRIMARY to the replacement, in the report's own tree. The
                    //    replacement passes same-tree + the R8 cycle guard (tree lock + descendant
                    //    walk). We emit ReportingLineSuperseded (old) + ReportingLineAssigned (new)
                    //    + a reporting_line_audit row, mirroring the normal assign path.
                    foreach (var rep in incomingPrimaryInTx)
                    {
                        var replacement = replacements[rep.EmployeeId];

                        // Same-tree (in-tx so it sees the current committed users) + cycle guard.
                        // S74-7403 B1: ValidateSameTreeAsync pins BOTH the report + replacement `users`
                        // rows FOR UPDATE in id-order (no cross-tree-edge race). The OUTER advisory on
                        // the removed person's tree (acquired above, before the census) is ALREADY held
                        // here, and the report (a direct report) + its same-tree replacement are in that
                        // SAME tree by invariant, so these user-row pins fall UNDER the held advisory —
                        // preserving the global order (advisory → user rows id-ordered → slot).
                        //
                        // S74-7403 fix4 — NO inner per-report advisory re-acquire. The prior pass
                        // re-acquired the advisory on repTreeRoot after pinning the rows; that inverts the
                        // global advisory → rows order (rows then advisory) and can DEADLOCK with an
                        // assign already holding repTreeRoot. We rely on ValidateSameTreeAsync (which pins
                        // both rows FOR UPDATE) to REJECT — with CrossTreeAssignmentException → 400 — any
                        // replacement a concurrent transfer drove cross-tree, so no cross-tree edge is
                        // created and no rows→advisory inversion remains. The residual (a simultaneous
                        // same-new-styrelse transfer) is the same deferred in-lock hardening follow-up.
                        string repTreeRoot;
                        try
                        {
                            repTreeRoot = await repo.ValidateSameTreeAsync(conn, tx, rep.EmployeeId, replacement, ct);
                        }
                        catch (CrossTreeAssignmentException ex)
                        {
                            await tx.RollbackAsync(ct);
                            return Results.Json(new { error = ex.Message, reportEmployeeId = rep.EmployeeId }, statusCode: 400);
                        }

                        // S74-7403 Step-5a c5: the ONLY tree advisory lock held here is the removed
                        // person's (removedTreeRoot). If this report's CURRENT tree (repTreeRoot) differs,
                        // the report was transferred cross-styrelse while still reporting to the removed
                        // person (a latent cross-tree edge from that transfer). Reassigning it would mutate
                        // a DIFFERENT tree while holding only removedTreeRoot's lock — unserialized against
                        // assigns in repTreeRoot's tree (a cycle-serialization gap). REFUSE rather than
                        // mutate under the wrong lock: surface the transferred report for manual handling
                        // (consistent with R10's preflight-reject + the owner's reject-the-rare-transfer
                        // ruling). The general cross-styrelse-transfer serialization is the deferred in-lock
                        // hardening follow-up.
                        if (!string.Equals(repTreeRoot, removedTreeRoot, StringComparison.Ordinal))
                        {
                            await tx.RollbackAsync(ct);
                            return Results.Json(new
                            {
                                error = "Report has been transferred to a different styrelse and cannot be reassigned in this removal; resolve it manually first, then retry.",
                                reportEmployeeId = rep.EmployeeId,
                            }, statusCode: 409);
                        }

                        await repo.GuardNoCycleAsync(conn, tx, rep.EmployeeId, replacement, ct);

                        var newPrimary = new ReportingLine
                        {
                            ReportingLineId = Guid.NewGuid(),
                            EmployeeId = rep.EmployeeId,
                            ManagerId = replacement,
                            TreeRootOrgId = repTreeRoot,
                            Relationship = "PRIMARY",
                            EffectiveFrom = today,
                            EffectiveTo = null,
                            Source = "MANUAL",
                            Version = 1,
                            CreatedBy = actor.ActorId ?? "system",
                            CreatedAt = DateTime.UtcNow,
                        };
                        // AssignAsync supersedes the report's current active PRIMARY (held by the
                        // removed person, version = rep.Version) and inserts the new line.
                        var persisted = await repo.AssignAsync(conn, tx, rep.Version, newPrimary, ct);

                        // Audit: SUPERSEDED on the old line, ASSIGNED on the new.
                        await InsertReportingLineAuditAsync(conn, tx, rep.ReportingLineId, "SUPERSEDED",
                            actor, versionBefore: rep.Version, versionAfter: rep.Version + 1, ct);
                        await InsertReportingLineAuditAsync(conn, tx, persisted.ReportingLineId, "ASSIGNED",
                            actor, versionBefore: null, versionAfter: persisted.Version, ct);

                        var streamId = $"reporting-line-{rep.EmployeeId}";
                        await outbox.EnqueueAsync(conn, tx, streamId, new ReportingLineSuperseded
                        {
                            ReportingLineId = rep.ReportingLineId,
                            EmployeeId = rep.EmployeeId,
                            PreviousManagerId = rep.ManagerId,
                            NewManagerId = persisted.ManagerId,
                            TreeRootOrgId = rep.TreeRootOrgId,
                            EffectiveFrom = rep.EffectiveFrom,
                            EffectiveTo = persisted.EffectiveFrom,
                            RowVersion = rep.Version + 1,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        }, ct);
                        await outbox.EnqueueAsync(conn, tx, streamId, new ReportingLineAssigned
                        {
                            ReportingLineId = persisted.ReportingLineId,
                            EmployeeId = persisted.EmployeeId,
                            ManagerId = persisted.ManagerId,
                            TreeRootOrgId = persisted.TreeRootOrgId,
                            Relationship = persisted.Relationship,
                            EffectiveFrom = persisted.EffectiveFrom,
                            Source = persisted.Source,
                            RowVersion = persisted.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        }, ct);
                    }

                    // ── Step 2: close incoming ACTING edges (manager_id = removed). No replacement —
                    //    an acting assignment simply ends. ReportingLineSuperseded (NewManagerId=null).
                    //    S74-7403 B4: iterate the IN-TX edge set (a concurrently-added ACTING edge is
                    //    captured too, since the lock was taken before the in-tx census).
                    foreach (var act in incomingActingInTx)
                    {
                        var closed = await repo.RemoveAsync(conn, tx, act.Version, act.EmployeeId, "ACTING", ct);
                        await InsertReportingLineAuditAsync(conn, tx, closed.ReportingLineId, "ACTING_ENDED",
                            actor, versionBefore: act.Version, versionAfter: closed.Version, ct);
                        await outbox.EnqueueAsync(conn, tx, $"reporting-line-{act.EmployeeId}", new ReportingLineSuperseded
                        {
                            ReportingLineId = closed.ReportingLineId,
                            EmployeeId = closed.EmployeeId,
                            PreviousManagerId = closed.ManagerId,
                            NewManagerId = null,
                            TreeRootOrgId = closed.TreeRootOrgId,
                            EffectiveFrom = closed.EffectiveFrom,
                            EffectiveTo = closed.EffectiveTo!.Value,
                            RowVersion = closed.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        }, ct);
                    }

                    // ── Step 3: close the removed person's OWN outgoing edges (where THEY are the
                    //    employee). Both PRIMARY and ACTING. Otherwise the removed person stays
                    //    "reporting to" someone while inactive.
                    var ownEdges = await repo.GetActiveByEmployeeInTxAsync(conn, tx, employeeId, ct);
                    foreach (var own in ownEdges)
                    {
                        var closed = await repo.RemoveAsync(conn, tx, own.Version, employeeId, own.Relationship, ct);
                        await InsertReportingLineAuditAsync(conn, tx, closed.ReportingLineId,
                            own.Relationship == "ACTING" ? "ACTING_ENDED" : "SUPERSEDED",
                            actor, versionBefore: own.Version, versionAfter: closed.Version, ct);
                        await outbox.EnqueueAsync(conn, tx, $"reporting-line-{employeeId}", new ReportingLineSuperseded
                        {
                            ReportingLineId = closed.ReportingLineId,
                            EmployeeId = closed.EmployeeId,
                            PreviousManagerId = closed.ManagerId,
                            NewManagerId = null,
                            TreeRootOrgId = closed.TreeRootOrgId,
                            EffectiveFrom = closed.EffectiveFrom,
                            EffectiveTo = closed.EffectiveTo!.Value,
                            RowVersion = closed.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        }, ct);
                    }

                    // ── Step 4: close manager_vikar rows where the removed user is the absent
                    //    approver OR the stand-in (vikar). ManagerVikarEnded(APPROVER_REMOVED) +
                    //    the ADR-026 D2 sync-in-tx audit trio (EnqueueAndReturnId + Map + InsertAsync).
                    var vikarIds = await GetActiveVikarRowsForUserInTxAsync(conn, tx, employeeId, ct);
                    foreach (var vikarId in vikarIds)
                    {
                        var closed = await vikarRepo.CloseAsync(conn, tx, vikarId, today, ct);
                        if (closed is null) continue; // closed concurrently — nothing to emit.

                        var endedEvent = new ManagerVikarEnded
                        {
                            VikarId = closed.VikarId,
                            AbsentApproverId = closed.AbsentApproverId,
                            VikarUserId = closed.VikarUserId,
                            UntilDate = closed.UntilDate,
                            Reason = closed.Reason,
                            TreeRootOrgId = closed.TreeRootOrgId,
                            EffectiveTo = closed.EffectiveTo!.Value,
                            EndReason = "APPROVER_REMOVED",
                            RowVersion = closed.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        var endedOutboxId = await outbox.EnqueueAndReturnIdAsync(
                            conn, tx, $"reporting-line-{closed.AbsentApproverId}", endedEvent, ct);
                        var endedAuditCtx = new AuditProjectionContext(
                            ActorId: actor.ActorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: actor.CorrelationId,
                            OccurredAt: new DateTimeOffset(endedEvent.OccurredAt),
                            ResolvedTargetOrgId: endedEvent.TreeRootOrgId);
                        var endedAuditRow = vikarEndedAuditMapper.Map(endedEvent, endedAuditCtx);
                        await auditRepo.InsertAsync(conn, tx, endedEvent.EventId, endedOutboxId,
                            endedEvent.EventType, endedAuditRow, endedAuditCtx, ct);
                    }

                    // ── Step 5: soft-deactivate the user. is_active = FALSE + users_audit DEACTIVATED
                    //    + UserUpdated event. (The edge work above REPLACES the plain
                    //    ReportingLineManagerDeactivated-only behaviour of the AdminEndpoints PUT
                    //    deactivation path — here we have already closed both directions.)
                    long newUserVersion;
                    {
                        await using var deactCmd = new NpgsqlCommand(
                            """
                            UPDATE users
                               SET is_active = FALSE, version = version + 1, updated_at = NOW()
                             WHERE user_id = @userId AND is_active = TRUE
                            RETURNING version
                            """, conn, tx);
                        deactCmd.Parameters.AddWithValue("userId", employeeId);
                        var versionObj = await deactCmd.ExecuteScalarAsync(ct);
                        if (versionObj is null)
                        {
                            // Already inactive (or gone) — nothing to deactivate; the edge closure
                            // above is still valid (idempotent cleanup). Roll back to keep the op
                            // all-or-nothing and surface a clear 409.
                            await tx.RollbackAsync(ct);
                            return Results.Json(new { error = $"User '{employeeId}' is not an active user." }, statusCode: 409);
                        }
                        newUserVersion = (long)versionObj;
                    }

                    // users_audit row — action 'UPDATED' (the table's CHECK allows
                    // CREATED/UPDATED/DELETED/SUPERSEDED; deactivation is an is_active UPDATE,
                    // mirroring the AdminEndpoints PUT deactivation audit shape).
                    await using (var userAuditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO users_audit (
                            user_id, action, previous_data, new_data,
                            version_before, version_after, actor_id, actor_role)
                        VALUES (
                            @userId, 'UPDATED', @previousData::jsonb, @newData::jsonb,
                            @versionBefore, @versionAfter, @actorId, @actorRole)
                        """, conn, tx))
                    {
                        userAuditCmd.Parameters.AddWithValue("userId", employeeId);
                        userAuditCmd.Parameters.AddWithValue("previousData",
                            System.Text.Json.JsonSerializer.Serialize(new { isActive = true }));
                        userAuditCmd.Parameters.AddWithValue("newData",
                            System.Text.Json.JsonSerializer.Serialize(new { isActive = false, removedViaReassignment = true }));
                        userAuditCmd.Parameters.AddWithValue("versionBefore", newUserVersion - 1);
                        userAuditCmd.Parameters.AddWithValue("versionAfter", newUserVersion);
                        userAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                        userAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                        await userAuditCmd.ExecuteNonQueryAsync(ct);
                    }

                    // UserUpdated event (is_active flip) — the existing user-lifecycle event;
                    // there is no dedicated UserDeactivated event family.
                    // S74-7403 B5: UserUpdated IS cataloged TENANT_TARGETED, so it must carry an
                    // ADR-026 audit_projection row. Use the sync-in-tx trio (EnqueueAndReturnId + Map
                    // + InsertAsync), mirroring the canonical AdminEndpoints PUT user-update path —
                    // a plain EnqueueAsync would emit the event with NO audit_projection row.
                    var userUpdatedEvent = new UserUpdated
                    {
                        UserId = employeeId,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var userUpdatedOutboxId = await outbox.EnqueueAndReturnIdAsync(
                        conn, tx, $"user-{employeeId}", userUpdatedEvent, ct);
                    // ResolvedTargetOrgId: the removed person's tree root (TENANT_TARGETED visibility);
                    // the UserUpdated event carries no PrimaryOrgId here, so the mapper falls back to
                    // this context value (it throws if both are null).
                    var userUpdatedAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(userUpdatedEvent.OccurredAt),
                        ResolvedTargetOrgId: removedTreeRoot);
                    var userUpdatedAuditRow = userUpdatedAuditMapper.Map(userUpdatedEvent, userUpdatedAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, userUpdatedEvent.EventId, userUpdatedOutboxId,
                        userUpdatedEvent.EventType, userUpdatedAuditRow, userUpdatedAuditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (ReportingCycleException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
            catch (CrossTreeAssignmentException ex)
            {
                // S74-7403 — a replacement is in a different reporting tree than its report → 400.
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                // S74-7403 W1 — a replacement (or the removed person) cannot be resolved to an
                // Organisation tree root (missing/inactive user, or no MAO/ORGANISATION ancestor) →
                // clean 400 rather than a 500. The whole closure rolled back.
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version — the edge matrix changed concurrently; refresh and retry",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }

            return Results.Ok(new
            {
                removed = employeeId,
                reportsReassigned = reportsReassignedCount,
                actingEdgesClosed = actingEdgesClosedCount,
            });
        })).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR. S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 9: GET /api/admin/reporting-lines/tree/{treeRootOrgId}/settings — Get tree settings
        // ═══════════════════════════════════════════

        app.MapGet("/api/admin/reporting-lines/tree/{treeRootOrgId}/settings", async (
            string treeRootOrgId,
            TreeSettingsRepository settingsRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            // S91 TASK-9102: tree-page surface opened to LocalHR — HROrAbove policy → LocalHR floor
            // (org-scope containment unchanged; HR stays org-bounded).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var settings = await settingsRepo.GetAsync(treeRootOrgId, ct);
            if (settings is null)
            {
                // Default: PREFERRED, version 0 (no row exists)
                context.Response.Headers.ETag = "\"0\"";
                return Results.Ok(new { enforcementMode = "PREFERRED", version = 0 });
            }

            context.Response.Headers.ETag = $"\"{settings.Version}\"";
            return Results.Ok(new { enforcementMode = settings.EnforcementMode, version = settings.Version });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR

        // ═══════════════════════════════════════════
        // Endpoint 10: PUT /api/admin/reporting-lines/tree/{treeRootOrgId}/settings — Update tree settings
        // ═══════════════════════════════════════════

        app.MapPut("/api/admin/reporting-lines/tree/{treeRootOrgId}/settings", async (
            string treeRootOrgId,
            UpdateTreeSettingsRequest request,
            TreeSettingsRepository settingsRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            // S91 TASK-9102: tree-page surface opened to LocalHR — HROrAbove policy → LocalHR floor
            // (org-scope containment unchanged; HR stays org-bounded).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Validate treeRootOrgId is actually a MAO or ORGANISATION (Codex S50 W2;
            // S92/ADR-035 re-point — a tree root is now a MAO or ORGANISATION row).
            var treeRootOrg = await orgRepo.GetByIdAsync(treeRootOrgId, ct);
            if (treeRootOrg is null)
                return Results.NotFound(new { error = $"Organization {treeRootOrgId} not found" });
            if (treeRootOrg.OrgType is not ("MAO" or "ORGANISATION"))
                return Results.BadRequest(new { error = $"Organization {treeRootOrgId} is type {treeRootOrg.OrgType}, not a tree root (must be MAO or ORGANISATION)" });

            // Parse If-Match
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
            {
                return Results.Json(new { error = headerError }, statusCode: 428);
            }

            // Validate enforcement mode
            if (request.EnforcementMode is not ("PREFERRED" or "REQUIRED"))
                return Results.BadRequest(new { error = "enforcementMode must be PREFERRED or REQUIRED" });

            // Population gate: cannot enable REQUIRED unless tree is fully populated
            if (request.EnforcementMode == "REQUIRED")
            {
                var (isPopulated, unassigned) = await settingsRepo.ValidateTreePopulatedAsync(treeRootOrgId, ct);
                if (!isPopulated)
                {
                    return Results.Json(new
                    {
                        error = "Cannot enable enforcement: some employees have no designated manager",
                        unassignedEmployeeIds = unassigned,
                        unassignedCount = unassigned.Count,
                    }, statusCode: 409);
                }
            }

            // Atomic tx: upsert + outbox event
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
                    var settings = await settingsRepo.UpsertAsync(conn, tx, treeRootOrgId, request.EnforcementMode, expectedVersion, actor.ActorId ?? "system", ct);

                    await tx.CommitAsync(ct);
                    context.Response.Headers.ETag = $"\"{settings.Version}\"";
                    return Results.Ok(new { enforcementMode = settings.EnforcementMode, version = settings.Version });
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                return Results.Json(new
                {
                    error = "Stale version",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR

        // ═══════════════════════════════════════════
        // Endpoint 11: GET /api/reporting-lines/delegate — Get active self-delegation status
        // ═══════════════════════════════════════════

        app.MapGet("/api/reporting-lines/delegate", async (
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId;
            if (string.IsNullOrEmpty(actorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // S74 storage cutover: the active self-delegation is now ONE manager_vikar row
            // owned by the actor (absent_approver_id = actor). The contract is byte-stable.
            var vikar = await vikarRepo.GetActiveByApproverAnyDateAsync(actorId, ct);
            if (vikar is null)
            {
                return Results.Ok(new
                {
                    active = false,
                    actingManagerId = (string?)null,
                    effectiveFrom = (DateOnly?)null,
                    effectiveTo = (DateOnly?)null,
                    delegatedEmployees = Array.Empty<object>(),
                });
            }

            // Re-derive delegatedEmployees[] DYNAMICALLY (R4 / Codex W3): the actor's CURRENT
            // PRIMARY reports for which the vikar is currently effective (until_date >= today),
            // EXCLUDING any report already superseded by an admin (non-self) ACTING line —
            // matching the POST's existing skip. Done in ONE query: actor's active PRIMARY
            // reports LEFT-anti-joined against any active admin ACTING for the same employee.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var isEffectiveNow = vikar.UntilDate >= today;

            var delegatedEmployeeIds = new List<string>();
            if (isEffectiveNow)
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand(
                    """
                    SELECT rl.employee_id
                    FROM reporting_lines rl
                    WHERE rl.manager_id = @actorId
                      AND rl.relationship = 'PRIMARY'
                      AND rl.effective_to IS NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM reporting_lines admin_act
                          WHERE admin_act.employee_id = rl.employee_id
                            AND admin_act.relationship = 'ACTING'
                            AND admin_act.effective_to IS NULL
                            AND admin_act.source <> 'SELF_DELEGATION')
                    ORDER BY rl.employee_id
                    """, conn);
                cmd.Parameters.AddWithValue("actorId", actorId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    delegatedEmployeeIds.Add(reader.GetString(0));
            }

            var employeeIds = new HashSet<string>(delegatedEmployeeIds, StringComparer.Ordinal);
            var displayNames = await LookupDisplayNamesAsync(connectionFactory, employeeIds, ct);

            return Results.Ok(new
            {
                active = true,
                actingManagerId = vikar.VikarUserId,
                effectiveFrom = (DateOnly?)DateOnly.FromDateTime(vikar.CreatedAt),
                effectiveTo = (DateOnly?)vikar.UntilDate,
                delegatedEmployees = delegatedEmployeeIds.Select(empId => new
                {
                    employeeId = empId,
                    displayName = displayNames.GetValueOrDefault(empId),
                }),
            });
        }).RequireAuthorization("LeaderOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 12: POST /api/reporting-lines/delegate — Self-service delegation
        // ═══════════════════════════════════════════

        app.MapPost("/api/reporting-lines/delegate", async (
            DelegateRequest request,
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            RoleAssignmentRepository roleRepo,
            OrganizationRepository orgRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AuditProjectionRepository auditRepo,
            IAuditProjectionMapper<ManagerVikarCreated> vikarCreatedAuditMapper,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 (BLOCKER 1) — the self-service /delegate CREATE is an employee-current-root mutator
        // (it keys on the SELF-DELEGATING MANAGER's CURRENT tree root, same class as the admin-vikar
        // create + the assigns), so it gets the SAME bounded drift-retry: if the drift-guarded acquire
        // (Step 1b below) detects a concurrent cross-styrelse transfer drifted the advisory key, the
        // attempt rolls back (no side effects — the drift check precedes every FOR UPDATE/mutation) and
        // re-runs on a fresh tx re-keyed on the manager's NEW root. (The /delegate DELETE/revoke keys on
        // the PERSISTED tree_root_org_id for revoke-safety and is deliberately NOT re-keyed.)
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId;
            if (string.IsNullOrEmpty(actorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // 1. Parse and validate effectiveTo date.
            if (!DateOnly.TryParse(request.EffectiveTo, out var effectiveTo))
                return Results.BadRequest(new { error = $"Invalid effectiveTo date: '{request.EffectiveTo}'" });

            var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
            if (effectiveTo <= effectiveFrom)
                return Results.BadRequest(new { error = "effectiveTo must be after today" });

            // 2. Cannot delegate to self.
            if (string.Equals(actorId, request.ActingManagerId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Cannot delegate to yourself" });

            // NOTE (S76 / TASK-7601, Step-7a c1 BLOCKER): the active-vikar 409 pre-check was
            // REMOVED from here and moved IN-LOCK (after the no-reports 400 guard). The D15
            // restructure had hoisted the 409 to PRECEDE the (now in-lock) no-reports 400, which
            // flipped the COMBINED state "active vikar AND zero direct reports" from the ORIGINAL
            // 400 ("no reports to delegate") to a 409 — a contract regression for the live S51
            // self-service UI (R2). Restoring the ORIGINAL relative order (no-reports 400 FIRST,
            // then active-vikar 409) keeps the /delegate error contract byte-stable. The 409 is
            // now read in-lock after directReports (see Step 4b below); the in-tx INSERT collision
            // (uq_manager_vikar_active) remains the authoritative 409.

            // NOTE (S76 / TASK-7601 fix-forward, Step-5a c1 B2): the actor's PRIMARY direct-report
            // list (step 3), the "no reports" guard, AND the vikar eligibility/coverage census
            // (steps 5+6) are NOW read IN-LOCK (inside the tx, after the tree advisory lock) — see
            // below. A pre-lock snapshot of the report list left a phantom gap: a report assigned
            // by a concurrent tx (committed after a pre-lock census but before this tx took the
            // lock) would NOT be coverage-checked yet would be auto-exposed through the vikar. The
            // RESPONSE/contract stays byte-stable (the same 400/409 messages, the same
            // {delegatedCount, skippedCount, actingManagerId, effectiveFrom, effectiveTo} shape).

            // 7. Atomic tx (ADR-018 D3): create ONE manager_vikar row + emit ManagerVikarCreated
            //    + audit, replacing the per-report SELF_DELEGATION ACTING fan-out (S74 storage
            //    cutover). The vikar covers the actor's CURRENT + FUTURE reports automatically;
            //    the resolver derives effective coverage at routing time. delegatedCount /
            //    skippedCount stay in the response (contract-stable): delegated = reports the
            //    vikar effectively covers now (no admin ACTING superseding them); skipped =
            //    reports already held by an admin (non-self) ACTING — matching the old skip at
            //    the per-report fan-out (Codex W3).
            // tree_root_org_id: the actor's reporting tree. The AUTHORITATIVE root comes from the
            // in-tx ValidateSameTreeAsync below (S76 / TASK-7601 D15 hardening) — NOT a pre-tx read.
            int delegated = 0, skipped = 0;
            ManagerVikar createdVikar;
            try
            {
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                // S76 / TASK-7601 (Deliverable B) — FULL D15 lock discipline, mirroring the assign
                // path (S74-7403). ReadCommitted (NOT RepeatableRead): the tx's first statement is the
                // SELECT pg_advisory_xact_lock that BLOCKS until the lock is granted; a RepeatableRead
                // snapshot pinned there would not see a competing vikar/edge committed during the wait.
                // ReadCommitted gives each post-lock statement a fresh snapshot — correct for a
                // lock-serialized critical section (the FOR UPDATE pins + uq_manager_vikar_active still
                // hold). The contract is BYTE-STABLE: request {actingManagerId, effectiveTo}, response
                // {delegatedCount, skippedCount, actingManagerId, effectiveFrom, effectiveTo}, the 409
                // on active, and every 400 message are all unchanged.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // ── D15 TOTAL LOCK ORDER (identical to the assign path): (1) tree advisory lock →
                    //    (2) the two user rows id-ordered FOR UPDATE (inside ValidateSameTreeAsync) →
                    //    (3) cycle guard → (4) the manager_vikar INSERT. No user row is locked before the
                    //    advisory, so a tx parked on the advisory holds no user row (deadlock-safe).
                    //
                    // Step 1 (S78 R9 BLOCKER 1): take the tree-wide advisory lock FIRST via the
                    //    DRIFT-GUARDED acquire on the actor's (absent approver's / self-delegating
                    //    manager's) CURRENT tree root. It derives the root UNLOCKED, acquires the advisory,
                    //    then RE-DERIVES under the held lock and throws TreeRootDriftException if a
                    //    concurrent cross-styrelse transfer moved the actor in between (→ the
                    //    TreeRootDriftRetry.RunAsync wrapper rolls back + retries on a fresh tx re-keyed on
                    //    the NEW root). This replaces the prior unguarded ResolveEmployeeTreeRootInTxAsync +
                    //    raw AcquireTreeLockAsync, which could hold a STALE key. The held advisory key is now
                    //    provably the actor's current tree root, so the in-lock D15 discipline below
                    //    (same-tree validation, cycle guard, report/coverage census) all run under a
                    //    non-stale key. The advisory is still taken BEFORE any user-row FOR UPDATE.
                    await repo.AcquireTreeLockForEmployeeAsync(conn, tx, actorId, ct);

                    // Step 2: SECURITY (ADR-027 D2 — S74-7402 B1) — the vikar MUST be in the SAME
                    // styrelse as the absent approver (the actor). Now validated IN-TX under the held
                    // advisory, pinning BOTH user rows FOR UPDATE so neither party can be transferred
                    // between the check and the INSERT (the cross-tree-edge race). Returns the
                    // AUTHORITATIVE common tree root used for the row + event.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, actorId, request.ActingManagerId, ct);

                    // Step 3: cycle guard (D15) — a report/descendant of the actor cannot be the vikar
                    // (it would let a subordinate gain approve-authority over the actor's own reports
                    // via the resolver). The actor is the "employee" anchor; the vikar is the "manager".
                    // A NON-descendant leader (the normal self-delegation target) is NOT rejected.
                    await repo.GuardNoCycleAsync(conn, tx, actorId, request.ActingManagerId, ct);

                    // Step 4 (S76 / TASK-7601 fix-forward, Step-5a c1 B2): the actor's PRIMARY direct
                    // reports read IN-LOCK (same conn/tx, under the held advisory). A report assigned
                    // by a concurrent tx that committed before this tx took the lock is SEEN here, so
                    // the coverage census below cannot leave an UNCOVERED report auto-exposed through
                    // the vikar (the phantom gap). The "no reports" 400 stays byte-stable.
                    var directReports = (await repo.GetDirectReportsAsync(conn, tx, actorId, ct))
                        .Where(r => r.Relationship == "PRIMARY").ToList();
                    if (directReports.Count == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new { error = "You have no direct reports to delegate" });
                    }

                    // Step 4b (S76 / TASK-7601, Step-7a c1 BLOCKER fix): the active-vikar 409
                    // pre-check — relocated to fire AFTER the no-reports 400 guard, restoring
                    // byte-stability with the pre-S76 contract. For an actor with zero direct
                    // reports, /delegate returns 400 ("no reports") REGARDLESS of whether they
                    // hold an active vikar — exactly as the original (S74-close) ordering did. This
                    // is a friendly upfront read (its own connection, same as the original pre-lock
                    // check) — NOT the authority for the 409; the in-tx INSERT collision
                    // (uq_manager_vikar_active, surfaced below) remains the authoritative 409.
                    // Contract-stable message + status unchanged.
                    {
                        var existingVikar = await vikarRepo.GetActiveByApproverAnyDateAsync(actorId, ct);
                        if (existingVikar is not null)
                        {
                            await tx.RollbackAsync(ct);
                            return Results.Json(new { error = "Active self-delegation already exists; revoke it first" }, statusCode: 409);
                        }
                    }

                    // Step 5+6 (IN-LOCK): the acting manager (vikar) holds a qualifying LocalLeader+
                    // role AND its org-scope covers ALL of the actor's CURRENT direct reports. Run on
                    // the SAME conn/tx under the advisory so a concurrent report-assign / role-or-scope
                    // revocation committed before the lock is reflected (byte-identical 400 messages).
                    var (notEligible, uncoveredEmployees) = await EvaluateVikarCoverageInTxAsync(
                        conn, tx, request.ActingManagerId, actorId, repo, ct);
                    if (notEligible)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new { error = $"Acting manager '{request.ActingManagerId}' does not hold a qualifying role (LocalLeader or above)" });
                    }
                    if (uncoveredEmployees.Count > 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new
                        {
                            error = "Acting manager's org-scope does not cover all direct reports",
                            uncoveredEmployeeIds = uncoveredEmployees,
                            uncoveredCount = uncoveredEmployees.Count,
                        });
                    }

                    // delegatedCount / skippedCount computed INSIDE the write tx (same conn/tx,
                    // under ReadCommitted, after the advisory) so the response reflects the committed
                    // state — a concurrent admin-ACTING change cannot make the response stale (Codex W).
                    foreach (var report in directReports)
                    {
                        await using var existingCmd = new NpgsqlCommand(
                            """
                            SELECT 1 FROM reporting_lines
                            WHERE employee_id = @empId
                              AND relationship = 'ACTING'
                              AND effective_to IS NULL
                              AND source <> 'SELF_DELEGATION'
                            LIMIT 1
                            """, conn, tx);
                        existingCmd.Parameters.AddWithValue("empId", report.EmployeeId);
                        var hasAdminActing = await existingCmd.ExecuteScalarAsync(ct);
                        if (hasAdminActing is not null) skipped++;
                        else delegated++;
                    }

                    var newVikar = new ManagerVikar
                    {
                        VikarId = Guid.NewGuid(),
                        AbsentApproverId = actorId,
                        VikarUserId = request.ActingManagerId,
                        UntilDate = effectiveTo,               // INCLUSIVE "til og med" (R4a)
                        Reason = "ANDET",                       // DelegateRequest carries no reason → default
                        TreeRootOrgId = treeRootOrgId,
                        Version = 1,
                        CreatedBy = actorId,
                        CreatedAt = DateTime.UtcNow,
                    };

                    createdVikar = await vikarRepo.CreateAsync(conn, tx, newVikar, ct);

                    var createdEvent = new ManagerVikarCreated
                    {
                        VikarId = createdVikar.VikarId,
                        AbsentApproverId = createdVikar.AbsentApproverId,
                        VikarUserId = createdVikar.VikarUserId,
                        UntilDate = createdVikar.UntilDate,
                        Reason = createdVikar.Reason,
                        TreeRootOrgId = createdVikar.TreeRootOrgId,
                        RowVersion = createdVikar.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // ADR-018 D3 + ADR-026 D2: event + audit-projection row + state in ONE tx.
                    // EnqueueAndReturnIdAsync captures the outbox_id so audit_projection.outbox_id
                    // aligns with the global outbox sequence; the mapper derives target_org_id from
                    // the event's tree_root_org_id (TENANT_TARGETED).
                    var createdOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{actorId}", createdEvent, ct);
                    var createdAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(createdEvent.OccurredAt),
                        ResolvedTargetOrgId: createdEvent.TreeRootOrgId);
                    var createdAuditRow = vikarCreatedAuditMapper.Map(createdEvent, createdAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, createdOutboxId, createdEvent.EventType, createdAuditRow, createdAuditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch (OptimisticConcurrencyException)
                {
                    // Lost the race to a concurrent active create (uq_manager_vikar_active) —
                    // surface the same 409 the upfront check would have.
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Active self-delegation already exists; revoke it first" }, statusCode: 409);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            }
            catch (CrossTreeAssignmentException)
            {
                // S76 / TASK-7601 (Deliverable B) — same-tree validation moved IN-TX; a cross-tree
                // vikar → the SAME byte-stable 400 the pre-tx ValidateSameTreeAsync emitted.
                return Results.BadRequest(new { error = "Vikar must be in the same styrelse (tree) as you" });
            }
            catch (ReportingCycleException)
            {
                // S76 / TASK-7601 — the NEW D15 cycle guard: the chosen vikar is the actor or a
                // descendant. Surfaced as a 400 (a malformed self-delegation choice), consistent with
                // the other in-tx validation rejections on this endpoint (the contract has no 409 for
                // this case — 409 is reserved for the existing active-delegation collision).
                return Results.BadRequest(new { error = "Vikar must not be one of your own reports (a subordinate cannot stand in for you)" });
            }
            catch (InvalidOperationException)
            {
                // ValidateSameTreeAsync / AcquireTreeLockForEmployeeAsync throw when a user/org cannot
                // be resolved to an Organisation tree root (inactive/missing user or org, or no
                // MAO/ORGANISATION ancestor) → clean 400 (byte-stable with the prior pre-tx message),
                // never a 500. Layer 2 (the authority predicate) fails closed on the same condition.
                return Results.BadRequest(new { error = "Could not validate the vikar's styrelse (tree); ensure the vikar is an active user in your styrelse" });
            }

            return Results.Ok(new
            {
                delegatedCount = delegated,
                skippedCount = skipped,
                actingManagerId = request.ActingManagerId,
                effectiveFrom,
                effectiveTo,
            });
        })).RequireAuthorization("LeaderOrAbove"); // S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 13: DELETE /api/reporting-lines/delegate — Revoke self-delegation
        //
        // S83 / ADR-027 D18→D19 — the self-revoke is now SERIALIZED on the revoke subject's tree
        // under the SHARED revoke-safe drift-guarded acquire (AcquireRevokeTreeLocksAsync). Pre-S83 it
        // took NO advisory at all (just a RepeatableRead snapshot), so a concurrent cross-styrelse
        // transfer of the self-delegating manager, or a key-sharing in-tree mutator, could proceed with
        // no mutual exclusion against the revoke. It now ALWAYS locks the PERSISTED
        // manager_vikar.tree_root_org_id (the immutable revoke-authority anchor — survives an
        // inactive/transferred subject) and, when derivable, the subject's CURRENT root too (drift-
        // guarded). The drift guard wraps the body in TreeRootDriftRetry.RunAsync (a concurrent transfer
        // committing mid-acquire → rollback + retry on a fresh tx). The cheap existence probe is taken
        // PRE-lock (it also carries the persisted root used to KEY the advisory); a missing active row
        // → the existing 404 (behavior-equivalent to the prior post-close null check).
        // ═══════════════════════════════════════════

        app.MapDelete("/api/reporting-lines/delegate", async (
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AuditProjectionRepository auditRepo,
            IAuditProjectionMapper<ManagerVikarEnded> vikarEndedAuditMapper,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId;
            if (string.IsNullOrEmpty(actorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // Cheap pre-lock existence probe — a 404 here avoids taking the tree lock when there is no
            // active self-delegation (a double-revoke / stale UI). It carries the PERSISTED tree root
            // used to KEY the revoke-safe advisory (the actor may have transferred/deactivated, so the
            // persisted column — not a live users derivation — is the stable anchor). For self-
            // delegation the absent approver == the actor, so we key on the actor.
            var probe = await vikarRepo.GetActiveByApproverAnyDateAsync(actorId, ct);
            if (probe is null)
                return Results.NotFound(new { error = "No active self-delegation to revoke" });

            // S83 — bounded drift-retry wrapper: the in-tx body re-keys from scratch on a concurrent
            // transfer that drifts the subject's current root under the advisory.
            return await TreeRootDriftRetry.RunAsync(async () =>
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // ReadCommitted: the advisory is the tx's first lock-bearing statement (consistent with
                // the assign/create/admin-revoke paths); a RepeatableRead snapshot pinned before the
                // lock would not observe a competing mutation committed during the lock wait.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // (1) Take the revoke-safe tree advisories FIRST (before any read/mutation): the
                    //     persisted root ALWAYS + the subject's current root when derivable (drift-
                    //     guarded). Serializes this revoke against in-tree mutators / a transfer.
                    await repo.AcquireRevokeTreeLocksAsync(conn, tx, probe.TreeRootOrgId, actorId, ct);

                    // S74 storage cutover: close the actor's single active manager_vikar row + emit
                    // ManagerVikarEnded, one atomic tx (ADR-018 D3). revokedCount stays in the response
                    // (contract-stable) — set to the number of CURRENT PRIMARY reports the vikar covered
                    // (excluding admin-ACTING-superseded ones), or 1 if it covered none but the row existed.
                    // coveredCount read INSIDE the write tx (same conn/tx) so the response reflects the
                    // committed state — no separate-connection staleness (Codex W). It reads active
                    // reporting_lines, unchanged by the vikar close.
                    int coveredCount;
                    await using (var countCmd = new NpgsqlCommand(
                        """
                        SELECT COUNT(*) FROM reporting_lines rl
                        WHERE rl.manager_id = @actorId
                          AND rl.relationship = 'PRIMARY'
                          AND rl.effective_to IS NULL
                          AND NOT EXISTS (
                              SELECT 1 FROM reporting_lines admin_act
                              WHERE admin_act.employee_id = rl.employee_id
                                AND admin_act.relationship = 'ACTING'
                                AND admin_act.effective_to IS NULL
                                AND admin_act.source <> 'SELF_DELEGATION')
                        """, conn, tx))
                    {
                        countCmd.Parameters.AddWithValue("actorId", actorId);
                        coveredCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct) ?? 0);
                    }

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var closed = await vikarRepo.CloseByApproverAsync(conn, tx, actorId, today, ct);
                    if (closed is null)
                    {
                        // Lost a race to a concurrent revoke/expiry between the probe and the lock — the
                        // row is already closed. Byte-stable with the prior post-close null check.
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "No active self-delegation to revoke" });
                    }

                    var endedEvent = new ManagerVikarEnded
                    {
                        VikarId = closed.VikarId,
                        AbsentApproverId = closed.AbsentApproverId,
                        VikarUserId = closed.VikarUserId,
                        UntilDate = closed.UntilDate,
                        Reason = closed.Reason,
                        TreeRootOrgId = closed.TreeRootOrgId,
                        EffectiveTo = closed.EffectiveTo!.Value,
                        EndReason = "REVOKED",
                        RowVersion = closed.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // ADR-018 D3 + ADR-026 D2: event + audit-projection row + state in ONE tx.
                    var endedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{actorId}", endedEvent, ct);
                    var endedAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(endedEvent.OccurredAt),
                        ResolvedTargetOrgId: endedEvent.TreeRootOrgId);
                    var endedAuditRow = vikarEndedAuditMapper.Map(endedEvent, endedAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, endedEvent.EventId, endedOutboxId, endedEvent.EventType, endedAuditRow, endedAuditCtx, ct);

                    await tx.CommitAsync(ct);

                    return Results.Ok(new { revokedCount = coveredCount > 0 ? coveredCount : 1 });
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }).RequireAuthorization("LeaderOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 13b: GET /api/admin/reporting-lines/{managerId}/vikar — the SINGLE-manager active
        // vikar read (S76b / TASK-7603, BLOCKER 3). The unified EditPersonDrawer is opened from the
        // UserManagement LIST (no tree context), so LifecycleSections cannot get `activeVikar` from a
        // tree roster row — without this read an away-manager would show NO vikar and could not revoke
        // it. Serves the manager's OWN active manager_vikar row (effective_to IS NULL) + the stand-in's
        // display name, mirroring the roster's `outgoingVikar` shape; null/200 when none. Read-only /
        // additive. Scope: LocalAdminOrAbove + the admin org-scope covers the manager's primary org
        // (the same floor as the admin-on-behalf POST/DELETE on this path).
        // ═══════════════════════════════════════════

        app.MapGet("/api/admin/reporting-lines/{managerId}/vikar", async (
            string managerId,
            ManagerVikarRepository vikarRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (string.IsNullOrEmpty(actor.ActorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // Authorize against the manager's CURRENT primary org at the LocalHR floor (S91 TASK-9102:
            // tree-page surface opened to LocalHR; a below-HR scope covering the styrelse cannot read
            // this surface — mirrors the POST/DELETE gate). Org-scope containment unchanged.
            var managerUser = await userRepo.GetByIdAsync(managerId, ct);
            if (managerUser is null)
                return Results.NotFound(new { error = $"Manager '{managerId}' not found" });
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(
                actor, managerUser.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var active = await vikarRepo.GetActiveByApproverWithVikarNameAsync(managerId, ct);
            if (active is null)
                return Results.Ok(new { activeVikar = (object?)null });

            var (vikar, vikarDisplayName) = active.Value;
            return Results.Ok(new
            {
                activeVikar = new
                {
                    vikarUserId = vikar.VikarUserId,
                    vikarDisplayName,
                    untilDate = vikar.UntilDate,
                    reason = vikar.Reason,
                },
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR

        // ═══════════════════════════════════════════
        // Endpoint 14: POST /api/admin/reporting-lines/{managerId}/vikar — admin-on-behalf vikar
        // (S76 / TASK-7601, Deliverable A). An admin plants a stand-in (vikar) for an absent
        // manager. This is an ADR-027 D13/D14 AUTHORIZATION surface: the resulting manager_vikar row
        // GRANTS the vikar approve/reject/reopen authority over the manager's reports. The FULL
        // create-authority contract runs in ONE atomic tx under the tree advisory lock.
        // ═══════════════════════════════════════════

        app.MapPost("/api/admin/reporting-lines/{managerId}/vikar", async (
            string managerId,
            AdminVikarRequest request,
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            OrgScopeValidator scopeValidator,
            RoleAssignmentRepository roleRepo,
            OrganizationRepository orgRepo,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AuditProjectionRepository auditRepo,
            IAuditProjectionMapper<ManagerVikarCreated> vikarCreatedAuditMapper,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 — bounded drift-retry wrapper (see Endpoint 1). The vikar-CREATE keys on the absent
        // manager's CURRENT tree root (an employee-current derivation, like the assigns) — so it carries
        // the same S74-7403 stale-key risk and gets the drift guard. (The vikar-REVOKE deliberately keys
        // on the PERSISTED tree_root_org_id for revoke-safety and is NOT re-keyed — see that endpoint.)
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();
            if (string.IsNullOrEmpty(actor.ActorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // 0. Parse + validate the body.
            if (string.IsNullOrWhiteSpace(request.VikarUserId))
                return Results.BadRequest(new { error = "vikarUserId is required" });
            if (!DateOnly.TryParse(request.EffectiveTo, out var effectiveTo))
                return Results.BadRequest(new { error = $"Invalid effectiveTo date: '{request.EffectiveTo}'" });
            var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
            if (effectiveTo <= effectiveFrom)
                return Results.BadRequest(new { error = "effectiveTo must be after today" });
            // A manager cannot stand in for themselves; the vikar must differ from the manager.
            if (string.Equals(request.VikarUserId, managerId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Vikar must differ from the manager" });

            // Reason: the manager_vikar.reason CHECK admits FERIE/SYGDOM/ORLOV/TJENESTEREJSE/ANDET.
            // Default to ANDET when omitted; reject an out-of-set value (422-style 400) rather than
            // surfacing a raw 23514 from the INSERT.
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? "ANDET" : request.Reason!;
            var allowedReasons = new HashSet<string>(StringComparer.Ordinal)
                { "FERIE", "SYGDOM", "ORLOV", "TJENESTEREJSE", "ANDET" };
            if (!allowedReasons.Contains(reason))
                return Results.BadRequest(new { error = $"Invalid reason '{reason}' (FERIE/SYGDOM/ORLOV/TJENESTEREJSE/ANDET)" });

            // S76 / TASK-7601 fix-forward (Step-5a c1 B1) — the manager must EXIST (active). Cheap
            // pre-lock NOT-FOUND so a typo doesn't take the tree lock; the lock-stable org is
            // re-resolved IN-TX below (a transfer cannot move the manager out from under the admin
            // gate between this read and the INSERT).
            var managerUser = await userRepo.GetByIdAsync(managerId, ct);
            if (managerUser is null)
                return Results.NotFound(new { error = $"Manager '{managerId}' not found" });

            // (v-preflight) Fast 409 if the manager already has an active vikar (friendly upfront
            //     check; the DB partial-unique uq_manager_vikar_active is the by-construction
            //     backstop INSIDE the tx). NOTE: this is a fast-path only — the authoritative
            //     409 is the in-tx INSERT collision (OptimisticConcurrencyException), so a vikar
            //     created by a concurrent tx between here and the lock is still rejected.
            if (await vikarRepo.GetActiveByApproverAnyDateAsync(managerId, ct) is not null)
                return Results.Json(new { error = "Manager already has an active vikar; revoke it first" }, statusCode: 409);

            // The atomic create — FULL D15 lock discipline (mirrors the self-delegate + assign paths).
            // S76 / TASK-7601 fix-forward (Step-5a c1 B1): ALL authorization + coverage now runs
            // INSIDE the tx, AFTER the tree advisory lock. Order: root → lock → admin-scope (in-tx
            // manager org) → vikar-coverage census (in-tx report list) → same-tree → cycle → INSERT.
            // A concurrent report-assign / role-or-scope revocation that commits before this tx takes
            // the lock is SEEN by the in-lock census; one still in flight is BLOCKED on the same tree
            // advisory until this tx commits. So no unauthorized vikar grant can be left committed.
            ManagerVikar createdVikar;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // ReadCommitted: the advisory lock is the tx's first statement (see the self-delegate
                // rationale). Lock order: (1) tree advisory → (2) the two user rows id-ordered
                // FOR UPDATE (in ValidateSameTreeAsync) → (3) cycle guard → (4) manager_vikar INSERT.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // Step 1 (S78 R9): take the tree-wide advisory lock FIRST (before ANY auth/census
                    //   read) via the DRIFT-GUARDED acquire on the MANAGER's tree root (the absent
                    //   approver). It re-derives the root under the held lock and signals a retry on a
                    //   concurrent cross-styrelse transfer of the manager (TreeRootDriftException →
                    //   TreeRootDriftRetry.RunAsync), so the in-lock admin-scope/coverage/same-tree checks all
                    //   run under the manager's CURRENT, non-stale tree key.
                    var managerTreeRoot = await repo.AcquireTreeLockForEmployeeAsync(conn, tx, managerId, ct);

                    // (i) IN-LOCK admin-scope check. Resolve the manager's CURRENT primary org IN-TX
                    //     (so a concurrent cross-styrelse transfer cannot move them out from under the
                    //     gate) and run the FLOORED ValidateOrgAccessAsync at the LocalHR floor
                    //     (S91 TASK-9102 — tree-page surface opened to LocalHR): a below-HR scope
                    //     covering the manager's styrelse cannot satisfy this gate. Org-scope
                    //     containment unchanged. A scope revocation that committed before the lock is seen.
                    var managerOrgInTx = await ResolveManagerPrimaryOrgInTxAsync(conn, tx, managerId, ct);
                    if (managerOrgInTx is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = $"Manager '{managerId}' not found" });
                    }
                    var (orgAllowed, orgReason) = await scopeValidator.ValidateOrgAccessAsync(
                        actor, managerOrgInTx, StatsTidRoles.LocalHR, ct);
                    if (!orgAllowed)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Access denied", reason = orgReason }, statusCode: 403);
                    }

                    // (ii)+(iii) IN-LOCK vikar eligibility + report coverage. The report list is read
                    //     IN-TX UNDER the held advisory (EvaluateVikarCoverageInTxAsync), so a
                    //     concurrent report-assign cannot slip an UNCOVERED report past this check —
                    //     the phantom gap the external lens flagged. Active-ness of the vikar is
                    //     enforced by the per-employee is_active = TRUE reads + the same-tree FOR
                    //     UPDATE below.
                    var (notEligible, uncovered) = await EvaluateVikarCoverageInTxAsync(
                        conn, tx, request.VikarUserId, managerId, repo, ct);
                    if (notEligible)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new { error = $"Vikar '{request.VikarUserId}' does not hold a qualifying role (LocalLeader or above)" });
                    }
                    if (uncovered.Count > 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new
                        {
                            error = "Vikar's org-scope does not cover all of the manager's reports",
                            uncoveredEmployeeIds = uncovered,
                            uncoveredCount = uncovered.Count,
                        });
                    }

                    // (iv) Step 2: the vikar MUST be SAME-TREE as the manager — validated IN-TX under
                    //      the advisory, pinning BOTH user rows FOR UPDATE (the cross-tree-edge race).
                    //      Returns the AUTHORITATIVE common tree root for the row + event. A cross-tree
                    //      vikar → CrossTreeAssignmentException → 400.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, managerId, request.VikarUserId, ct);

                    // (iv) Step 3: cycle guard — a report/descendant of the manager cannot be the vikar
                    //      (a subordinate must not gain approve-authority over their own manager's
                    //      reports via the resolver). The manager is the "employee" anchor; the vikar
                    //      is the "manager". → ReportingCycleException → 400.
                    await repo.GuardNoCycleAsync(conn, tx, managerId, request.VikarUserId, ct);

                    var newVikar = new ManagerVikar
                    {
                        VikarId = Guid.NewGuid(),
                        AbsentApproverId = managerId,              // the absent manager (path)
                        VikarUserId = request.VikarUserId,         // the stand-in
                        UntilDate = effectiveTo,                   // INCLUSIVE "til og med" (R4a)
                        Reason = reason,
                        TreeRootOrgId = treeRootOrgId,
                        Version = 1,
                        CreatedBy = actor.ActorId,                 // the ADMIN created it (audit trail)
                        CreatedAt = DateTime.UtcNow,
                    };

                    // (v) Atomic INSERT — a concurrent second active vikar collides on
                    //     uq_manager_vikar_active (23505) → OptimisticConcurrencyException → 409.
                    createdVikar = await vikarRepo.CreateAsync(conn, tx, newVikar, ct);

                    var createdEvent = new ManagerVikarCreated
                    {
                        VikarId = createdVikar.VikarId,
                        AbsentApproverId = createdVikar.AbsentApproverId,
                        VikarUserId = createdVikar.VikarUserId,
                        UntilDate = createdVikar.UntilDate,
                        Reason = createdVikar.Reason,
                        TreeRootOrgId = createdVikar.TreeRootOrgId,
                        RowVersion = createdVikar.Version,
                        ActorId = actor.ActorId,                   // the ADMIN, not the manager
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // (vi) ADR-018 D3 + ADR-026 D2: event + audit row + state in ONE tx. The audit
                    //      context actor = the ADMIN (actor.ActorId / actor.OrgId), NOT {managerId} —
                    //      the S71 lesson (the audit row attributes the operator's org, while the
                    //      TARGET org stays the manager's tree via the event's TreeRootOrgId).
                    var createdOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{managerId}", createdEvent, ct);
                    var createdAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(createdEvent.OccurredAt),
                        ResolvedTargetOrgId: createdEvent.TreeRootOrgId);
                    var createdAuditRow = vikarCreatedAuditMapper.Map(createdEvent, createdAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, createdOutboxId, createdEvent.EventType, createdAuditRow, createdAuditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch (OptimisticConcurrencyException)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Manager already has an active vikar; revoke it first" }, statusCode: 409);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (CrossTreeAssignmentException)
            {
                return Results.BadRequest(new { error = "Vikar must be in the same styrelse (tree) as the manager" });
            }
            catch (ReportingCycleException)
            {
                return Results.BadRequest(new { error = "Vikar must not be one of the manager's own reports (a subordinate cannot stand in for them)" });
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Could not validate the vikar's styrelse (tree); ensure both the manager and the vikar are active users in the same styrelse" });
            }

            return Results.Ok(new
            {
                vikarId = createdVikar.VikarId,
                managerId,
                vikarUserId = createdVikar.VikarUserId,
                effectiveFrom,
                effectiveTo,
                reason = createdVikar.Reason,
            });
        })).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR. S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

        // ═══════════════════════════════════════════
        // Endpoint 15: DELETE /api/admin/reporting-lines/{managerId}/vikar — admin revokes the
        // manager's active vikar (S76 / TASK-7601, Deliverable A). REVOKE-SAFE: it must stay
        // possible to revoke even after the manager or the vikar has gone INACTIVE — so the
        // revoke-authority anchor is the PERSISTED manager_vikar.tree_root_org_id (NOT
        // ValidateSameTreeAsync, which requires ACTIVE users).
        //
        // S76 / TASK-7601 fix-forward (Step-5a c1 B3 + WARNING): the authorize→close pair is now
        // ATOMIC and in-lock. The active row is read FOR UPDATE INSIDE the tx under the tree
        // advisory lock, the actor is authorized against THAT EXACT row's persisted
        // tree_root_org_id (the SOLE anchor — the old "current-manager-org OR persisted-root"
        // fallback is REMOVED; it widened authority across two domains), and only then is the same
        // pinned row closed. A concurrent close/recreate cannot swap the row between authorize and
        // close. Required: (a) the actor's admin scope covers the persisted tree root + (b) an
        // active row exists (else 404). The vikar's current activeness / coverage are NOT re-checked.
        // ═══════════════════════════════════════════

        app.MapDelete("/api/admin/reporting-lines/{managerId}/vikar", async (
            string managerId,
            ReportingLineRepository repo,
            ManagerVikarRepository vikarRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AuditProjectionRepository auditRepo,
            IAuditProjectionMapper<ManagerVikarEnded> vikarEndedAuditMapper,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (string.IsNullOrEmpty(actor.ActorId))
                return Results.Json(new { error = "Actor identity required" }, statusCode: 401);

            // Cheap pre-lock existence probe — a 404 here avoids taking the tree lock for a manager
            // with no active vikar (a typo / double-revoke). It carries the persisted tree root used
            // to KEY the advisory lock (the manager may be inactive, so we cannot re-resolve the tree
            // from the users row). The AUTHORITATIVE row is the in-lock FOR UPDATE re-read below.
            // The probe stays OUTSIDE the drift-retry — it is an existence check + the persisted root,
            // both stable across a retry.
            var probe = await vikarRepo.GetActiveByApproverAnyDateAsync(managerId, ct);
            if (probe is null)
                return Results.NotFound(new { error = "No active vikar to revoke for this manager" });

            // S83 / ADR-027 D18→D19 — bounded drift-retry wrapper. Pre-S83 this revoke took ONLY the
            // PERSISTED-root advisory; a concurrent cross-styrelse transfer of the manager, or a key-
            // sharing mutator on the manager's CURRENT tree, could proceed on a DIFFERENT key with no
            // mutual exclusion. The revoke-safe acquire now ALSO locks the manager's current root (when
            // derivable, drift-guarded) on top of the always-locked persisted anchor, so a concurrent
            // current-root drift → rollback + retry on a fresh tx.
            return await TreeRootDriftRetry.RunAsync(async () =>
            {
                // Close + emit ManagerVikarEnded + audit (admin actor-org), one atomic tx.
                // Lock order: (1) revoke-safe tree advisories (persisted root + current root when
                // derivable) → (2) the active row FOR UPDATE → (3) authorize vs THAT row's persisted
                // tree root → (4) close.
                ManagerVikar closed;
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // ReadCommitted: the advisory lock is the tx's first statement (consistent with the
                // assign/create paths); a RepeatableRead snapshot pinned there would not see a competing
                // close/recreate committed during the wait.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // (1) Take the REVOKE-SAFE tree advisories FIRST: the persisted tree root (the
                    //     revoke-safe anchor; survives an inactive manager/vikar) ALWAYS + the manager's
                    //     CURRENT root when derivable (drift-guarded). Serializes the revoke against
                    //     concurrent tree mutations (assigns / recreates) AND a live transfer.
                    await repo.AcquireRevokeTreeLocksAsync(conn, tx, probe.TreeRootOrgId, managerId, ct);

                    // (2) Re-read the active row FOR UPDATE under the lock — THE authoritative row. A
                    //     concurrent close/recreate that committed before this tx took the lock is seen;
                    //     one still in flight is blocked on the same advisory / row pin.
                    var activeVikar = await vikarRepo.GetActiveByApproverForUpdateInTxAsync(conn, tx, managerId, ct);
                    if (activeVikar is null)
                    {
                        // Lost a race to a concurrent revoke/expiry — the row is already closed.
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "No active vikar to revoke for this manager" });
                    }

                    // (3) Authorize against the PINNED row's PERSISTED tree_root_org_id — the SOLE
                    //     revoke-authority anchor (WARNING: the prior "current-org OR persisted-root"
                    //     fallback is removed). The actor's scope must cover this exact tree root
                    //     (FLOORED at LocalHR — S91 TASK-9102, tree-page surface opened to LocalHR: a
                    //     below-HR scope covering the styrelse cannot revoke. Containment unchanged).
                    var (treeAllowed, treeReason) = await scopeValidator.ValidateOrgAccessAsync(
                        actor, activeVikar.TreeRootOrgId, StatsTidRoles.LocalHR, ct);
                    if (!treeAllowed)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Access denied", reason = treeReason }, statusCode: 403);
                    }

                    // (4) Close the SAME pinned row (keyed by vikar_id, idempotent under effective_to
                    //     IS NULL). The FOR UPDATE above guarantees this is the row we authorized.
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var maybeClosed = await vikarRepo.CloseAsync(conn, tx, activeVikar.VikarId, today, ct);
                    if (maybeClosed is null)
                    {
                        // Cannot happen under the FOR UPDATE pin, but stay fail-safe.
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "No active vikar to revoke for this manager" });
                    }
                    closed = maybeClosed;

                    var endedEvent = new ManagerVikarEnded
                    {
                        VikarId = closed.VikarId,
                        AbsentApproverId = closed.AbsentApproverId,
                        VikarUserId = closed.VikarUserId,
                        UntilDate = closed.UntilDate,
                        Reason = closed.Reason,
                        TreeRootOrgId = closed.TreeRootOrgId,        // the PINNED row's persisted root
                        EffectiveTo = closed.EffectiveTo!.Value,
                        EndReason = "REVOKED",
                        RowVersion = closed.Version,
                        ActorId = actor.ActorId,                       // the ADMIN
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var endedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{managerId}", endedEvent, ct);
                    var endedAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,                // the ADMIN's org (S71 lesson)
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(endedEvent.OccurredAt),
                        ResolvedTargetOrgId: endedEvent.TreeRootOrgId);
                    var endedAuditRow = vikarEndedAuditMapper.Map(endedEvent, endedAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, endedEvent.EventId, endedOutboxId, endedEvent.EventType, endedAuditRow, endedAuditCtx, ct);

                    await tx.CommitAsync(ct);

                    return Results.Ok(new
                    {
                        vikarId = closed.VikarId,
                        managerId,
                        vikarUserId = closed.VikarUserId,
                        revoked = true,
                    });
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR

        return app;
    }

    // ── Helper: MapReaderForImport — inline reader mapper for import endpoint ──

    /// <summary>
    /// Maps a <see cref="NpgsqlDataReader"/> to <see cref="ReportingLine"/>.
    /// Duplicated from <see cref="ReportingLineRepository"/> because the private
    /// MapReader is inaccessible from the endpoint file. Used only by the import
    /// endpoint's in-tx read.
    /// </summary>
    private static ReportingLine MapReaderForImport(NpgsqlDataReader reader) => new()
    {
        ReportingLineId = reader.GetGuid(reader.GetOrdinal("reporting_line_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        ManagerId = reader.GetString(reader.GetOrdinal("manager_id")),
        TreeRootOrgId = reader.GetString(reader.GetOrdinal("tree_root_org_id")),
        Relationship = reader.GetString(reader.GetOrdinal("relationship")),
        EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_from"))),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_to"))),
        Source = reader.GetString(reader.GetOrdinal("source")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
    };

    // ── Helper: reporting_line_audit row insert (S74 R10 closure) ──

    /// <summary>
    /// Inserts a <c>reporting_line_audit</c> row in the caller's transaction. Mirrors the inline
    /// audit-insert the assign/remove endpoints already do (same columns, same table); factored
    /// out so the R10 delete-with-reassignment closure can write the multiple audit rows its
    /// 5-step matrix produces without repeating the SQL. <paramref name="action"/> must be one of
    /// the <c>reporting_line_audit.action</c> CHECK values (ASSIGNED / SUPERSEDED / ACTING_ASSIGNED
    /// / ACTING_ENDED / BULK_IMPORTED / MANAGER_DEACTIVATED).
    /// </summary>
    private static async Task InsertReportingLineAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid reportingLineId, string action, ActorContext actor,
        long? versionBefore, long? versionAfter, CancellationToken ct)
    {
        await using var auditCmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_line_audit
                (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
            VALUES
                (@lineId, @action, @actorId, @correlationId, @versionBefore, @versionAfter, @metadata::jsonb)
            """, conn, tx);
        auditCmd.Parameters.AddWithValue("lineId", reportingLineId);
        auditCmd.Parameters.AddWithValue("action", action);
        auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
        auditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
        auditCmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        auditCmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        auditCmd.Parameters.AddWithValue("metadata", DBNull.Value);
        await auditCmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helper: vikar-eligibility + report-coverage (IN-TX, IN-LOCK; self-delegate + admin-on-behalf) ──

    /// <summary>
    /// S76 / TASK-7601 fix-forward (Step-5a c1 B1/B2) — the IN-TX, IN-LOCK vikar-eligibility +
    /// report-coverage census. The external lens found that running the
    /// vikar-eligibility + report-coverage census OUTSIDE the tree advisory lock leaves a phantom
    /// gap: a concurrent report-assign (committed AFTER the pre-lock census read but BEFORE the
    /// vikar tx acquires the lock) can slip an UNCOVERED report past the check, committing an
    /// unauthorized vikar grant. This overload runs the ENTIRE census on the caller's
    /// <paramref name="conn"/>/<paramref name="tx"/>, AFTER the held
    /// <see cref="ReportingLineRepository.AcquireTreeLockAsync"/> — so every read (the manager's
    /// CURRENT PRIMARY report list, each report's org, the vikar's qualifying roles, each scope's
    /// org path) reflects the lock-serialized committed state. A report assigned by a tx that
    /// committed before this one took the lock is SEEN here (ReadCommitted, post-lock snapshot);
    /// a tx still in flight is BLOCKED on the same tree advisory until this one commits.
    ///
    /// <para>
    /// The eligibility/coverage SEMANTICS preserve the prior pre-lock TASK-5105 pass
    /// (qualifying-role set = GLOBAL_ADMIN/LOCAL_ADMIN/LOCAL_HR/LOCAL_LEADER, GLOBAL short-circuit,
    /// ORG_ONLY = exact-equals — S93/ADR-035 dropped the ORG_AND_DESCENDANTS prefix branch; the role read mirrors
    /// <see cref="RoleAssignmentRepository.GetByUserIdAsync"/>'s active+unexpired predicate). The
    /// ONLY difference is the connection: the report list is read via the in-tx
    /// <see cref="ReportingLineRepository.GetDirectReportsAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>
    /// and every supporting read runs on the same conn/tx (no fresh connections), so the census
    /// is atomic with the lock and the subsequent INSERT.
    /// </para>
    /// </summary>
    private static async Task<(bool NotEligible, List<string> UncoveredEmployeeIds)>
        EvaluateVikarCoverageInTxAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx,
            string vikarUserId,
            string absentApproverId,
            ReportingLineRepository repo,
            CancellationToken ct)
    {
        // (a) The candidate's qualifying role assignments — read IN-LOCK so a concurrent role
        //     REVOCATION (a scope removed mid-grant) cannot be missed. Mirrors
        //     RoleAssignmentRepository.GetByUserIdAsync's active+unexpired predicate.
        var qualifyingRoleIds = new HashSet<string>(StringComparer.Ordinal)
            { "GLOBAL_ADMIN", "LOCAL_ADMIN", "LOCAL_HR", "LOCAL_LEADER" };
        var qualifyingAssignments = new List<(string? OrgId, string ScopeType)>();
        await using (var roleCmd = new NpgsqlCommand(
            """
            SELECT ra.org_id, ra.scope_type, ra.role_id FROM role_assignments ra
            JOIN roles r ON ra.role_id = r.role_id
            WHERE ra.user_id = @vikarId AND ra.is_active = TRUE
              AND (ra.expires_at IS NULL OR ra.expires_at > NOW())
            """, conn, tx))
        {
            roleCmd.Parameters.AddWithValue("vikarId", vikarUserId);
            await using var reader = await roleCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var roleId = reader.GetString(2);
                if (!qualifyingRoleIds.Contains(roleId)) continue;
                qualifyingAssignments.Add((
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1)));
            }
        }

        if (qualifyingAssignments.Count == 0)
            return (NotEligible: true, new List<string>());

        // A GLOBAL-scoped qualifying assignment covers everything.
        if (qualifyingAssignments.Any(ra => string.Equals(ra.ScopeType, "GLOBAL", StringComparison.Ordinal)))
            return (NotEligible: false, new List<string>());

        // (b) Resolve each qualifying scope's org materialized_path IN-LOCK.
        var scopeOrgPaths = new List<(string? MaterializedPath, string ScopeType)>();
        foreach (var (orgId, scopeType) in qualifyingAssignments)
        {
            if (orgId is null) continue;
            string? path;
            await using var orgCmd = new NpgsqlCommand(
                "SELECT materialized_path FROM organizations WHERE org_id = @orgId AND is_active = TRUE", conn, tx);
            orgCmd.Parameters.AddWithValue("orgId", orgId);
            path = (await orgCmd.ExecuteScalarAsync(ct)) as string;
            scopeOrgPaths.Add((path, scopeType));
        }

        // (c) The CRITICAL in-lock read: the absent manager's CURRENT PRIMARY reports. Read on the
        //     same conn/tx UNDER the held advisory lock, so a report assigned by a concurrent tx
        //     that committed before this tx took the lock is SEEN — closing the phantom gap.
        var managerReports = (await repo.GetDirectReportsAsync(conn, tx, absentApproverId, ct))
            .Where(r => r.Relationship == "PRIMARY").ToList();

        var uncovered = new List<string>();
        foreach (var report in managerReports)
        {
            string? empOrgPath;
            await using (var empCmd = new NpgsqlCommand(
                """
                SELECT o.materialized_path FROM users u
                JOIN organizations o ON o.org_id = u.primary_org_id
                WHERE u.user_id = @employeeId AND u.is_active = TRUE AND o.is_active = TRUE
                """, conn, tx))
            {
                empCmd.Parameters.AddWithValue("employeeId", report.EmployeeId);
                empOrgPath = (await empCmd.ExecuteScalarAsync(ct)) as string;
            }

            if (empOrgPath is null)
            {
                uncovered.Add(report.EmployeeId);
                continue;
            }

            var covered = false;
            foreach (var (scopePath, scopeType) in scopeOrgPaths)
            {
                if (scopePath is null) continue;
                // S93 / ADR-035 slice 2 (flat role-scope): ORG_ONLY exact-match only; the
                // ORG_AND_DESCENDANTS prefix branch is dropped (coverage = exact membership).
                if (scopeType == "ORG_ONLY" &&
                    string.Equals(empOrgPath, scopePath, StringComparison.Ordinal))
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
                uncovered.Add(report.EmployeeId);
        }

        return (NotEligible: false, uncovered);
    }

    /// <summary>
    /// S76 / TASK-7601 fix-forward (Step-5a c1 B1) — the IN-TX, IN-LOCK admin-scope check. Resolves
    /// the manager's CURRENT <c>primary_org_id</c> on the caller's conn/tx (under the held tree
    /// advisory lock), so a concurrent cross-styrelse transfer of the manager cannot move them out
    /// from under the admin gate between the check and the INSERT. Returns the manager's in-tx
    /// primary org (so the caller can run the FLOORED
    /// <see cref="OrgScopeValidator.ValidateOrgAccessAsync"/> against the lock-stable value), or
    /// <c>null</c> if the manager is missing/inactive.
    /// </summary>
    private static async Task<string?> ResolveManagerPrimaryOrgInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string managerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @managerId AND is_active = TRUE", conn, tx);
        cmd.Parameters.AddWithValue("managerId", managerId);
        return (await cmd.ExecuteScalarAsync(ct)) as string;
    }

    // ── Helper: in-tx manager_vikar reverse-lookup for the R10 closure ──

    /// <summary>
    /// Returns the active <c>manager_vikar</c> rows (effective_to IS NULL) where the given user is
    /// EITHER the absent approver OR the stand-in vikar — the rows the S74 R10 delete-closure
    /// (step 4) must end. Served by <c>uq_manager_vikar_active</c> (absent side) +
    /// <c>idx_manager_vikar_vikar</c> (stand-in side). In-tx so it participates in the closure tx.
    /// Returns only (vikar_id, version-irrelevant) — the close keys on vikar_id.
    /// </summary>
    private static async Task<List<Guid>> GetActiveVikarRowsForUserInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT vikar_id FROM manager_vikar
            WHERE effective_to IS NULL
              AND (absent_approver_id = @userId OR vikar_user_id = @userId)
            ORDER BY vikar_id
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    // ── Helper: in-tx employee tree-root resolution (S74-7403 B1 advisory-key derivation) ──

    /// <summary>
    /// Resolves the reporting tree root for <paramref name="employeeId"/> WITHIN the caller's tx, so a
    /// path can derive the tree advisory-lock KEY. Reads the employee's <c>primary_org_id</c> in-tx
    /// (an active user) and walks up to the MAO/ORGANISATION root via the repository's in-tx resolver.
    ///
    /// <para>
    /// <b>S74-7403 B1 — total lock order (consistent on EVERY path, to avoid deadlock):</b>
    /// <c>tree advisory lock → user rows (id-ordered FOR UPDATE) → reporting_lines slot FOR UPDATE</c>.
    /// To honour this, NO user row may be locked BEFORE the advisory lock. This helper therefore reads
    /// <c>primary_org_id</c> WITHOUT a <c>FOR UPDATE</c> row lock — it only derives the advisory key.
    /// The earlier C2-1 pin (a <c>FOR UPDATE</c> here, ahead of the advisory) had to be removed: with
    /// B1 also locking the manager row inside <see cref="ReportingLineRepository.ValidateSameTreeAsync"/>,
    /// a pre-advisory employee pin would let a transaction hold one user row while waiting on the
    /// advisory, and a reciprocal transaction (employee/manager swapped) could deadlock against it.
    /// </para>
    ///
    /// <para>
    /// <b>S74-7403 fix4 — stale-key handling is now REJECT, not re-acquire.</b> The advisory key from
    /// this UNLOCKED read may be stale if a concurrent cross-styrelse org-transfer committed between this
    /// read and the advisory. The post-advisory
    /// <see cref="ReportingLineRepository.ValidateSameTreeAsync"/> locks BOTH user rows
    /// <c>FOR UPDATE</c> and re-resolves the authoritative common tree root; if the transfer made the
    /// employee and manager cross-tree it throws <see cref="CrossTreeAssignmentException"/> → 400, so no
    /// cross-tree edge is created. The prior "drift re-acquire" (re-taking the advisory on the
    /// authoritative root AFTER the row pins) was REMOVED: it inverted the global
    /// <c>advisory → user rows</c> order and could deadlock. The accepted, deferred residual — a
    /// simultaneous same-new-styrelse transfer serializing two assigns on different advisory keys
    /// (astronomically rare, non-corrupting) — needs a stable tree id or an org-transfer serialization
    /// lock (the in-lock concurrency-hardening follow-up).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If the employee is missing/inactive, or has no
    /// MAO/ORGANISATION ancestor — surfaced as a clean 400 by the caller (W1).</exception>
    private static async Task<string> ResolveEmployeeTreeRootInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        ReportingLineRepository repo, string employeeId, CancellationToken ct)
    {
        string? primaryOrgId;
        await using (var cmd = new NpgsqlCommand(
            // S74-7403 B1: NO FOR UPDATE — the advisory lock (taken by the caller right after this
            // read) is the first lock; user rows are pinned only later, inside ValidateSameTreeAsync.
            "SELECT primary_org_id FROM users WHERE user_id = @userId AND is_active = TRUE", conn, tx))
        {
            cmd.Parameters.AddWithValue("userId", employeeId);
            primaryOrgId = (string?)await cmd.ExecuteScalarAsync(ct);
        }
        if (primaryOrgId is null)
            throw new InvalidOperationException($"Employee user_id='{employeeId}' not found or inactive.");

        return await repo.ResolveTreeRootOrgIdAsync(conn, tx, primaryOrgId, ct);
    }

    // ── Helper: look up display names for a batch of user IDs ──

    /// <summary>
    /// Lightweight batch lookup of <c>display_name</c> for a set of user IDs.
    /// Returns a dictionary mapping user_id to display_name. Missing users are
    /// silently excluded (the caller gets a null from <c>GetValueOrDefault</c>).
    /// This is the same direct-SQL-in-read-endpoint pattern used by AdminEndpoints.
    /// </summary>
    private static async Task<Dictionary<string, string>> LookupDisplayNamesAsync(
        DbConnectionFactory connectionFactory,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(userIds.Count, StringComparer.Ordinal);
        if (userIds.Count == 0)
            return result;

        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, display_name FROM users WHERE user_id = ANY(@userIds)", conn);
        cmd.Parameters.AddWithValue("userIds", userIds.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }
        return result;
    }

    // ── Response mapper ──

    private static object MapLineResponse(ReportingLine line) => new
    {
        reportingLineId = line.ReportingLineId,
        employeeId = line.EmployeeId,
        managerId = line.ManagerId,
        treeRootOrgId = line.TreeRootOrgId,
        relationship = line.Relationship,
        effectiveFrom = line.EffectiveFrom,
        effectiveTo = line.EffectiveTo,
        source = line.Source,
        version = line.Version,
        createdBy = line.CreatedBy,
        createdAt = line.CreatedAt,
    };

    // ── Request DTOs (co-located per codebase convention) ──

    private sealed class AssignReportingLineRequest
    {
        public required string EmployeeId { get; init; }
        public required string ManagerId { get; init; }
        public required DateOnly EffectiveFrom { get; init; }
        public string? Relationship { get; init; }
    }

    private sealed class AssignActingManagerRequest
    {
        public required string ManagerId { get; init; }
        public required DateOnly EffectiveFrom { get; init; }
    }

    // ── Import DTOs ──

    private sealed record ImportReportingLinesRequest(string TreeRootOrgId, List<ImportRow> Rows);
    private sealed record ImportRow(string EmployeeId, string ManagerId, string EffectiveFrom);

    // ── Settings DTOs ──

    private sealed record UpdateTreeSettingsRequest(string EnforcementMode);

    // ── Self-service delegation DTOs ──

    private sealed record DelegateRequest(string ActingManagerId, string EffectiveTo);

    // ── Admin-on-behalf vikar DTO (S76 / TASK-7601) ──

    /// <summary>
    /// Body for <c>POST /api/admin/reporting-lines/{managerId}/vikar</c>. <see cref="EffectiveTo"/>
    /// is the INCLUSIVE last-covered day ("til og med", R4a) as an ISO yyyy-MM-dd date string;
    /// <see cref="Reason"/> is optional (defaults to ANDET) and must be one of the
    /// <c>manager_vikar.reason</c> CHECK values when supplied.
    /// </summary>
    private sealed record AdminVikarRequest(string VikarUserId, string EffectiveTo, string? Reason);

    // ── Remove-with-reassignment DTO (S74 R10) ──

    /// <summary>
    /// Body for <c>POST /api/admin/reporting-lines/{employeeId}/remove</c>. The
    /// <see cref="Replacements"/> map carries one entry PER active incoming PRIMARY report of the
    /// person being removed: <c>reportEmployeeId → replacementApproverId</c>. The preflight 409
    /// lists any report missing from this map; the closure reassigns each PRIMARY report to its
    /// supplied replacement (same-tree + cycle-guarded) before deactivating the person.
    /// </summary>
    private sealed record RemoveWithReassignmentRequest(Dictionary<string, string>? Replacements);
}
