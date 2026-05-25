using System.Data;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
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

            // 3. Validate same tree (employee + manager belong to same MINISTRY/STYRELSE root).
            string treeRootOrgId;
            try
            {
                treeRootOrgId = await repo.ValidateSameTreeAsync(request.EmployeeId, request.ManagerId, ct);
            }
            catch (CrossTreeAssignmentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            // 4. Build the ReportingLine model.
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

            // 5. Fetch predecessor (if any) BEFORE the write tx to determine if this is
            //    a first assignment or a supersession — needed for audit action routing.
            var predecessor = expectedVersion is not null
                ? await repo.GetActiveByEmployeeAndRelationshipAsync(request.EmployeeId, relationship, ct)
                : null;

            // 6. Single-transaction: state + audit + outbox (ADR-018 D3).
            ReportingLine persisted;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
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
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
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

            // 3. Validate same tree.
            string treeRootOrgId;
            try
            {
                treeRootOrgId = await repo.ValidateSameTreeAsync(employeeId, request.ManagerId, ct);
            }
            catch (CrossTreeAssignmentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            // 4. Build ACTING line.
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

            // 5. Fetch predecessor for audit routing.
            var predecessor = expectedVersion is not null
                ? await repo.GetActiveByEmployeeAndRelationshipAsync(employeeId, "ACTING", ct)
                : null;

            // 6. Atomic tx.
            ReportingLine persisted;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
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
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
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

            // 3. Atomic write pass — single RepeatableRead transaction.
            int imported = 0, superseded = 0, skipped = 0;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
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

                        if (current is not null && current.ManagerId == row.ManagerId)
                        {
                            // Idempotent — same manager already assigned.
                            skipped++;
                            continue;
                        }

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
            catch (OptimisticConcurrencyException)
            {
                return Results.Json(new { error = "Concurrent modification detected; retry the import" }, statusCode: 409);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = "Import failed", detail = ex.Message }, statusCode: 500);
            }

            return Results.Ok(new { imported, superseded, skipped, total = request.Rows.Count });
        }).RequireAuthorization("GlobalAdminOnly");

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
}
