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
        {
            var actor = context.GetActorContext();
            var relationship = string.IsNullOrWhiteSpace(request.Relationship) ? "PRIMARY" : request.Relationship;

            // 1. Validate org scope: actor must cover employee's org.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
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
                    // S74-7403 B1 — TOTAL LOCK ORDER (identical on every path, deadlock-safe):
                    //   (1) tree advisory lock  →  (2) the two user rows id-ordered FOR UPDATE (inside
                    //   ValidateSameTreeAsync)  →  (3) cycle guard  →  (4) AssignAsync's slot FOR UPDATE
                    //   on reporting_lines. NO user row is locked before the advisory, so a transaction
                    //   blocked on the advisory holds no user row and cannot deadlock the advisory holder.
                    //
                    // Step 1a: derive the advisory key from the employee's tree root (UNLOCKED read).
                    var employeeTreeRoot = await ResolveEmployeeTreeRootInTxAsync(conn, tx, repo, request.EmployeeId, ct);

                    // Step 1b: take the tree-wide advisory lock FIRST (serializes all assigns in this
                    // tree through the cycle check; closes the concurrent-first-assign phantom gap that
                    // the slot FOR UPDATE alone leaves). A transaction parked here holds NO user rows.
                    await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, employeeTreeRoot, ct);

                    // Step 2: validate same-tree + manager-active IN-TX, UNDER the held advisory, and
                    // PIN BOTH the employee + manager `users` rows FOR UPDATE in id-order (B1 — so
                    // neither party can be transferred between this check and the edge insert: the
                    // cross-tree-edge race, ADR-027 D2). Sees the current committed user state, so a
                    // concurrent R10 that deactivated the manager (committed before us) makes it read
                    // inactive here → 400, no orphan. Returns the AUTHORITATIVE common tree root.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, request.EmployeeId, request.ManagerId, ct);

                    // S74-7403 fix4 — ACCEPTED RESIDUAL (no drift re-acquire). The advisory key (1a) is
                    // derived from an UNLOCKED org read, so under a concurrent cross-styrelse org-transfer
                    // it may be stale. We do NOT re-acquire the advisory on the post-pin root: that would
                    // take the advisory AFTER the user rows are pinned (an inversion of the global
                    // advisory → rows order) and can DEADLOCK with an assign already holding the corrected
                    // key. Instead the id-ordered two-row FOR UPDATE + ValidateSameTreeAsync (run ABOVE,
                    // after the pins) is the safety net: if a concurrent transfer made the employee and
                    // manager cross-tree, it throws CrossTreeAssignmentException → 400, so no cross-tree
                    // edge is ever created. RESIDUAL (deferred): two assigns serializing on DIFFERENT
                    // advisory keys when BOTH parties transfer to the SAME new styrelse simultaneously
                    // with the assign — a non-serialized cycle window, astronomically rare and
                    // NON-corrupting — needs a stable tree id or an org-transfer serialization lock and is
                    // deferred to the in-lock concurrency-hardening follow-up. No rows→advisory inversion
                    // remains; the deadlock is eliminated.

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
                // resolved (missing/inactive user, or no MINISTRY/STYRELSE ancestor) → clean 400
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
        }).RequireAuthorization("LocalAdminOrAbove");

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
        {
            var actor = context.GetActorContext();

            // 1. Validate scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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
                    // ── S74-7403 C2-3 / B1: acquire the tree advisory lock BEFORE the root-invariant
                    //    census, using the SAME key primitive (ResolveEmployeeTreeRootInTxAsync) +
                    //    AcquireTreeLockAsync the guarded assigns / R10 use. Without it the census ran
                    //    un-serialized: it could read a one-root state and then a concurrent assign
                    //    commits a SECOND root the instant afterward (ADR-027 D9 ≤1-root violation).
                    //    Holding the lock for the whole census→close serializes this DELETE against every
                    //    assign in the same tree. Lock order (the SAME global order as every path):
                    //    tree advisory lock FIRST → RemoveAsync's slot FOR UPDATE → census. This DELETE
                    //    pins NO user row (it inserts no edge, so B1's cross-tree-edge race does not
                    //    apply); the key read is unlocked, consistent with "advisory before user rows".
                    var deletedTreeRoot = await ResolveEmployeeTreeRootInTxAsync(conn, tx, repo, employeeId, ct);
                    await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, deletedTreeRoot, ct);

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
        }).RequireAuthorization("LocalAdminOrAbove");

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

            // Validate scope covers tree root org.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, ct);
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
        {
            var actor = context.GetActorContext();

            // 1. Validate scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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
                    // S74-7403 B1 — TOTAL LOCK ORDER (identical to the PRIMARY assign path):
                    //   advisory → user rows id-ordered FOR UPDATE (in ValidateSameTreeAsync) → cycle
                    //   guard → slot. Step 1a: derive the advisory key (UNLOCKED read).
                    var employeeTreeRoot = await ResolveEmployeeTreeRootInTxAsync(conn, tx, repo, employeeId, ct);

                    // Step 1b: tree-wide advisory lock FIRST, on the ACTING assign path too (an ACTING
                    // manager that is the employee or a descendant forms a cycle). Parked → no user rows.
                    await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, employeeTreeRoot, ct);

                    // Step 2: same-tree + manager-active in-tx UNDER the lock; pins BOTH user rows
                    // FOR UPDATE in id-order (B1). Returns the authoritative common tree root.
                    var treeRootOrgId = await repo.ValidateSameTreeAsync(conn, tx, employeeId, request.ManagerId, ct);

                    // S74-7403 fix4 — ACCEPTED RESIDUAL (no drift re-acquire), identical to the PRIMARY
                    // assign path. The advisory key (1a) is from an UNLOCKED org read and may be stale
                    // under a concurrent cross-styrelse transfer, but we do NOT re-acquire on the post-pin
                    // root (that would invert advisory → rows and can deadlock). ValidateSameTreeAsync
                    // (run ABOVE, after both user rows are pinned FOR UPDATE) rejects any cross-tree drift
                    // with CrossTreeAssignmentException → 400, so no cross-tree edge is created. The
                    // residual (a simultaneous same-new-styrelse transfer serializing two assigns on
                    // different keys — astronomically rare, non-corrupting) is deferred to the in-lock
                    // concurrency-hardening follow-up. No rows→advisory inversion remains.

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
        }).RequireAuthorization("LocalAdminOrAbove");

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

            // 1. Validate scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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
                // employee) is now inactive/missing, or no MINISTRY/STYRELSE ancestor resolves. A
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
        {
            var actor = context.GetActorContext();

            // 1. Validate scope: actor must cover the person being removed.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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

                    // ── S74-7403 B4: acquire the removed person's tree lock FIRST, then RE-READ the
                    //    incoming edge census IN-TX (the out-of-tx preflight above is only a fast
                    //    pre-check). With the tree lock held for the whole census→closure→deactivate,
                    //    no concurrent assign can interleave a NEW report assigned to the removed
                    //    person after the preflight — which would otherwise be left pointing at the
                    //    now-inactive user (a brand-new orphan; ADR-027 D9). We resolve the removed
                    //    person's tree root in-tx, lock on it, then iterate the AUTHORITATIVE in-tx
                    //    edge set (not the preflight snapshot).
                    var removedTreeRoot = await ResolveEmployeeTreeRootInTxAsync(conn, tx, repo, employeeId, ct);
                    await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, removedTreeRoot, ct);

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
                // S74-7403 W1 — a replacement (or the removed person) cannot be resolved to a
                // styrelse tree root (missing/inactive user, or no MINISTRY/STYRELSE ancestor) →
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
        }).RequireAuthorization("LocalAdminOrAbove");

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
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, ct);
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
        }).RequireAuthorization("LocalAdminOrAbove");

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
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, treeRootOrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Validate treeRootOrgId is actually a MINISTRY or STYRELSE (Codex S50 W2).
            var treeRootOrg = await orgRepo.GetByIdAsync(treeRootOrgId, ct);
            if (treeRootOrg is null)
                return Results.NotFound(new { error = $"Organization {treeRootOrgId} not found" });
            if (treeRootOrg.OrgType is not ("MINISTRY" or "STYRELSE"))
                return Results.BadRequest(new { error = $"Organization {treeRootOrgId} is type {treeRootOrg.OrgType}, not a tree root (must be MINISTRY or STYRELSE)" });

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
        }).RequireAuthorization("LocalAdminOrAbove");

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

            // 3. Get actor's PRIMARY direct reports.
            var directReports = (await repo.GetDirectReportsAsync(actorId, ct))
                .Where(r => r.Relationship == "PRIMARY").ToList();
            if (directReports.Count == 0)
                return Results.BadRequest(new { error = "You have no direct reports to delegate" });

            // 4. Check no active self-delegation exists (S74 storage cutover: read manager_vikar,
            //    keyed on the actor = absent_approver_id). Contract-stable 409 on re-delegate —
            //    matches the old POST behaviour (the legacy POST 409'd on an active fan-out;
            //    revoke-first remains the flow). The DB partial-unique uq_manager_vikar_active
            //    is the by-construction backstop (S68-B1).
            {
                var existingVikar = await vikarRepo.GetActiveByApproverAnyDateAsync(actorId, ct);
                if (existingVikar is not null)
                    return Results.Json(new { error = "Active self-delegation already exists; revoke it first" }, statusCode: 409);
            }

            // 5. Validate acting manager exists and has LocalLeader+ role.
            var actingManagerRoles = await roleRepo.GetByUserIdAsync(request.ActingManagerId, ct);
            var qualifyingRoleIds = new HashSet<string>(StringComparer.Ordinal)
                { "GLOBAL_ADMIN", "LOCAL_ADMIN", "LOCAL_HR", "LOCAL_LEADER" };
            var qualifyingAssignments = actingManagerRoles
                .Where(ra => qualifyingRoleIds.Contains(ra.RoleId))
                .ToList();

            if (qualifyingAssignments.Count == 0)
                return Results.BadRequest(new { error = $"Acting manager '{request.ActingManagerId}' does not hold a qualifying role (LocalLeader or above)" });

            // 6. Validate acting manager's org-scope covers ALL direct reports (TASK-5105).
            //    Inline scope validation: for each direct report, resolve their org's
            //    materialized_path and check if any of the acting manager's scopes covers it.
            var hasGlobalScope = qualifyingAssignments.Any(ra =>
                string.Equals(ra.ScopeType, "GLOBAL", StringComparison.Ordinal));

            if (!hasGlobalScope)
            {
                // Build scope org paths for the acting manager's qualifying assignments.
                var scopeOrgPaths = new List<(string OrgId, string? MaterializedPath, string ScopeType)>();
                foreach (var ra in qualifyingAssignments)
                {
                    if (ra.OrgId is null) continue;
                    var scopeOrg = await orgRepo.GetByIdAsync(ra.OrgId, ct);
                    scopeOrgPaths.Add((ra.OrgId, scopeOrg?.MaterializedPath, ra.ScopeType));
                }

                // For each direct report, resolve their org and check coverage.
                var uncoveredEmployees = new List<string>();
                var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var report in directReports)
                {
                    // Resolve employee's org materialized_path.
                    string? empOrgPath;
                    {
                        await using var empConn = connectionFactory.Create();
                        await empConn.OpenAsync(ct);
                        await using var empCmd = new NpgsqlCommand(
                            """
                            SELECT o.materialized_path FROM users u
                            JOIN organizations o ON o.org_id = u.primary_org_id
                            WHERE u.user_id = @employeeId AND u.is_active = TRUE AND o.is_active = TRUE
                            """, empConn);
                        empCmd.Parameters.AddWithValue("employeeId", report.EmployeeId);
                        var result = await empCmd.ExecuteScalarAsync(ct);
                        empOrgPath = result as string;
                    }

                    if (empOrgPath is null)
                    {
                        uncoveredEmployees.Add(report.EmployeeId);
                        continue;
                    }

                    // Check if any scope covers this employee.
                    var covered = false;
                    foreach (var (_, scopePath, scopeType) in scopeOrgPaths)
                    {
                        if (scopePath is null) continue;
                        if (scopeType == "ORG_AND_DESCENDANTS" &&
                            empOrgPath.StartsWith(scopePath, StringComparison.Ordinal))
                        {
                            covered = true;
                            break;
                        }
                        if (scopeType == "ORG_ONLY" &&
                            string.Equals(empOrgPath, scopePath, StringComparison.Ordinal))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (!covered)
                        uncoveredEmployees.Add(report.EmployeeId);
                }

                if (uncoveredEmployees.Count > 0)
                {
                    return Results.BadRequest(new
                    {
                        error = "Acting manager's org-scope does not cover all direct reports",
                        uncoveredEmployeeIds = uncoveredEmployees,
                        uncoveredCount = uncoveredEmployees.Count,
                    });
                }
            }

            // 6b. SECURITY (ADR-027 D2 — S74-7402 B1 fix): the vikar user MUST be in the SAME
            //     reporting tree (styrelse) as the absent approver (the actor). Role + org-scope
            //     coverage alone do NOT guarantee this: a global-scoped user, or a leader holding
            //     a cross-tree scope assignment, could otherwise be planted as a vikar in another
            //     styrelse, become the resolver winner, and gain approve/reject/reopen authority
            //     over the actor's reports cross-styrelse. This brings the vikar-creation path to
            //     parity with the PRIMARY/ACTING assign endpoints (which all enforce
            //     ValidateSameTreeAsync). Layer 2 (the authority predicate) re-checks structurally.
            try
            {
                await repo.ValidateSameTreeAsync(actorId, request.ActingManagerId, ct);
            }
            catch (CrossTreeAssignmentException)
            {
                return Results.BadRequest(new { error = "Vikar must be in the same styrelse (tree) as you" });
            }
            catch (InvalidOperationException)
            {
                // ValidateSameTreeAsync throws InvalidOperationException when a user/org cannot be
                // resolved to a styrelse tree root (inactive/missing user or org, or no
                // MINISTRY/STYRELSE ancestor). Surface a clean 400 rather than a 500 (S74-7402
                // Step-5a c2 robustness fix). Layer 2 (the authority predicate) fails closed on the
                // same condition, so authorization stays safe regardless.
                return Results.BadRequest(new { error = "Could not validate the vikar's styrelse (tree); ensure the vikar is an active user in your styrelse" });
            }

            // 7. Atomic tx (ADR-018 D3): create ONE manager_vikar row + emit ManagerVikarCreated
            //    + audit, replacing the per-report SELF_DELEGATION ACTING fan-out (S74 storage
            //    cutover). The vikar covers the actor's CURRENT + FUTURE reports automatically;
            //    the resolver derives effective coverage at routing time. delegatedCount /
            //    skippedCount stay in the response (contract-stable): delegated = reports the
            //    vikar effectively covers now (no admin ACTING superseding them); skipped =
            //    reports already held by an admin (non-self) ACTING — matching the old skip at
            //    the per-report fan-out (Codex W3).
            // tree_root_org_id: the actor's reporting tree. All same-tree PRIMARY reports
            // share one tree root (ValidateSameTreeAsync invariant), so any report's value is
            // authoritative for the actor's tree.
            var treeRootOrgId = directReports[0].TreeRootOrgId;

            int delegated = 0, skipped = 0;
            ManagerVikar createdVikar;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
                    // delegatedCount / skippedCount computed INSIDE the write tx (same conn/tx,
                    // under RepeatableRead) so the response reflects the committed state — a
                    // concurrent admin-ACTING change cannot make the response stale (Codex W).
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

            return Results.Ok(new
            {
                delegatedCount = delegated,
                skippedCount = skipped,
                actingManagerId = request.ActingManagerId,
                effectiveFrom,
                effectiveTo,
            });
        }).RequireAuthorization("LeaderOrAbove");

        // ═══════════════════════════════════════════
        // Endpoint 13: DELETE /api/reporting-lines/delegate — Revoke self-delegation
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

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            try
            {
                // S74 storage cutover: close the actor's single active manager_vikar row + emit
                // ManagerVikarEnded, one atomic tx (ADR-018 D3). revokedCount stays in the response
                // (contract-stable) — set to the number of CURRENT PRIMARY reports the vikar covered
                // (excluding admin-ACTING-superseded ones), or 1 if it covered none but the row existed.
                // coveredCount read INSIDE the write tx (same conn/tx, under RepeatableRead) so the
                // response reflects the committed state — no separate-connection staleness (Codex W).
                // It reads active reporting_lines, unchanged by the vikar close.
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
        }).RequireAuthorization("LeaderOrAbove");

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
    /// (an active user) and walks up to the MINISTRY/STYRELSE root via the repository's in-tx resolver.
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
    /// MINISTRY/STYRELSE ancestor — surfaced as a clean 400 by the caller (W1).</exception>
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
