using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S31 / TASK-3107 — Phase 4d-3 Part 1 admin CRUD surface for the authoritative
/// employee profile store added by TASK-3101 (schema) + TASK-3102 (repository).
/// Two endpoints under <c>/api/admin/employee-profiles/{employeeId}</c>:
///   <list type="bullet">
///     <item><description>GET — read the live row, ETag-stamped per ADR-019 D2.</description></item>
///     <item><description>
///       PUT — admin-strict If-Match update inside an atomic transaction; emits
///       <see cref="EmployeeProfileUpdated"/> on the <c>employee-profile-{employeeId}</c>
///       stream + an <c>UPDATED</c> audit row in the same tx (ADR-018 D3 atomic-outbox).
///     </description></item>
///   </list>
///
/// <para>
/// <b>Step 0b cycle 1 Codex BLOCKER fix — cross-org HR data-leak prevention.</b>
/// Both endpoints carry <c>RequireAuthorization("HROrAbove")</c> AND an explicit
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> binding to the target
/// <c>employeeId</c>. The policy alone proves role + scope shape but does NOT bind
/// the actor to the target employee's organisation — without OrgScopeValidator an HR
/// user from org X could read/edit profiles of employees in org Y. The cross-org
/// binding is load-bearing.
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match contract.</b> PUT requires
/// <c>If-Match: "&lt;version&gt;"</c> via <see cref="EtagHeaderHelper.TryParseIfMatch"/>
/// (admin-strict mode rejects <c>If-None-Match: *</c>). 428 on missing/malformed;
/// 412 on stale (with structured <c>expectedVersion</c> + <c>actualVersion</c> body
/// per ADR-019 D2); 404 when no live row exists for the employee.
/// </para>
///
/// <para>
/// <b>S31 scope intentionally narrow.</b> No POST (the seeder at TASK-3106 + the
/// AdminEndpoints POST extension at TASK-3108 own creation), no DELETE (the live
/// row's lifecycle is owned by user-deletion paths). PUT is in-place update only;
/// no cross-day supersession routing — that is forward-compat for S32 per the
/// repository's dormant versioning columns.
/// </para>
/// </summary>
public static class EmployeeProfileEndpoints
{
    public static WebApplication MapEmployeeProfileEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove policy + OrgScopeValidator binding (Step 0b BLOCKER fix).
        // Returns 404 when no live row exists. On success, sets ETag: "<version>" so
        // the admin UI can compose If-Match on the subsequent PUT.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding. HROrAbove alone is not enough;
            // bind the actor's scopes to the target employee's organisation.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Step 7a P2 fix — atomic row+version read. Read body fields AND the
            // `version` column from the same SELECT so the ETag stamped on the
            // response matches the data serialized. Pre-fix used two reads
            // (GetByEmployeeIdAsync + ReadLiveVersionAsync) — a concurrent admin
            // edit between the two could have returned stale fields with a NEWER
            // ETag, letting the next If-Match overwrite the racing change.
            var hit = await repository.GetByEmployeeIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee profile not found" });
            var (profile, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                employeeId = profile.EmployeeId,
                weeklyNormHours = profile.WeeklyNormHours,
                partTimeFraction = profile.PartTimeFraction,
                position = profile.Position,
                isPartTime = profile.IsPartTime,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove + OrgScopeValidator (Step 0b BLOCKER fix).
        // Admin-strict If-Match required (ADR-019 D2). Atomic-outbox semantic:
        // UPDATE row + INSERT audit row + outbox enqueue all happen in one tx
        // (ADR-018 D3). Returns 200 with new ETag on success.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (stale version, ADR-019 D2)
        //   • 404 — KeyNotFoundException (no live row for the employee)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            UpdateEmployeeProfileRequest body,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding (mirrors GET above).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match parse — 428 if missing / malformed / If-None-Match: *
            // (per EtagHeaderHelper.TryParseIfMatch admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var streamId = $"employee-profile-{employeeId}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Snapshot pre-update state for the audit row's `previous_data` JSONB.
                // In-tx read so the audit reflects the same row the UPDATE locks.
                var preUpdate = await repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct);
                if (preUpdate is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Atomic UPDATE with admin-strict If-Match enforcement (repository raises
                // OptimisticConcurrencyException on version mismatch; KeyNotFoundException
                // when no live row matches).
                Guid profileId;
                long newVersion;
                try
                {
                    var upsertRequest = new EmployeeProfileUpsertRequest(
                        EmployeeId: employeeId,
                        WeeklyNormHours: body.WeeklyNormHours,
                        PartTimeFraction: body.PartTimeFraction,
                        Position: body.Position);
                    var result = await repository.UpsertAsync(
                        conn, tx, upsertRequest, expectedVersion, ct);
                    profileId = result.ProfileId;
                    newVersion = result.Version;
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                    }, statusCode: 412);
                }
                catch (KeyNotFoundException)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Audit row (ADR-019 D8 version-transition columns; version_before =
                // expectedVersion, version_after = newVersion = expectedVersion + 1).
                var previousData = JsonSerializer.Serialize(new
                {
                    weeklyNormHours = preUpdate.WeeklyNormHours,
                    partTimeFraction = preUpdate.PartTimeFraction,
                    position = preUpdate.Position,
                });
                var newData = JsonSerializer.Serialize(new
                {
                    weeklyNormHours = body.WeeklyNormHours,
                    partTimeFraction = body.PartTimeFraction,
                    position = body.Position,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, 'UPDATED',
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", expectedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // Atomic-outbox emission (same tx as UPDATE + audit per ADR-018 D3).
                var updatedEvent = new EmployeeProfileUpdated
                {
                    ProfileId = profileId,
                    EmployeeId = employeeId,
                    WeeklyNormHours = body.WeeklyNormHours,
                    PartTimeFraction = body.PartTimeFraction,
                    Position = body.Position,
                    VersionBefore = expectedVersion,
                    VersionAfter = newVersion,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await outbox.EnqueueAsync(conn, tx, streamId, updatedEvent, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                var isPartTime = body.PartTimeFraction < 1.0m;
                return Results.Ok(new
                {
                    employeeId,
                    weeklyNormHours = body.WeeklyNormHours,
                    partTimeFraction = body.PartTimeFraction,
                    position = body.Position,
                    isPartTime,
                    version = newVersion,
                });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove");

        return app;
    }

    // ── Request DTO ──

    /// <summary>
    /// PUT request body. All three S31-authoritative fields are required — admins must
    /// re-state the full edit shape rather than patching individual fields (mirrors the
    /// S29 WTM / S30 EntitlementConfig admin PUT precedent).
    /// </summary>
    private sealed record UpdateEmployeeProfileRequest(
        decimal WeeklyNormHours,
        decimal PartTimeFraction,
        string? Position);
}
