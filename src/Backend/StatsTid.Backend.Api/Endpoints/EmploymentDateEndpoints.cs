using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S60 / TASK-6006 / ADR-030 — HR admin surface for the <c>employment_start_date</c> that
/// pro-rates mid-year hires in the monthly vacation-accrual rule engine. Two endpoints, both
/// <c>HROrAbove</c> + <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> — mirroring
/// the S59 birth-date (DOB) HR-only surface EXACTLY (ADR-029 precedent). Cross-org binding is
/// load-bearing: the <c>HROrAbove</c> policy proves role + scope shape but does NOT bind the
/// actor to the target employee's organisation (FAIL-001: the validator uses
/// <c>FindAll</c>, not <c>FindFirst</c>, on scopes).
///   <list type="bullet">
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/employment-start-date</b> — HR-only read,
///       ETag stamped from <c>users.version</c> so the subsequent PUT can compose If-Match.
///       This is the ONLY read surface; <c>employment_start_date</c> never appears in any
///       Employee-facing DTO / JWT / export. 404 when no active user. 403 on cross-org.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/employment-start-date</b> — HR-only write,
///       admin-strict If-Match (ADR-019 D2). In one transaction (ADR-018 D3): FOR-UPDATE
///       re-read + version check, <see cref="UserRepository.SetEmploymentStartDateAsync"/>
///       (bumps <c>users.version</c>), and a <c>users_audit</c> UPDATED row (full-row JSONB
///       snapshot captures <c>employment_start_date</c> per init.sql — no audit-table change
///       needed). <c>employmentStartDate</c> may be null (clears an unknown start date).
///     </description></item>
///   </list>
/// </summary>
public static class EmploymentDateEndpoints
{
    public static WebApplication MapEmploymentDateEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employees/{employeeId}/employment-start-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. The ONLY employment-start read surface —
        // it never appears in any Employee-facing DTO / JWT / export. ETag stamped from
        // users.version so the subsequent PUT composes If-Match coherently.
        // 404 when no active user. 403 on cross-org.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var hit = await userRepo.GetByIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee not found" });
            var (user, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                employeeId = user.UserId,
                employmentStartDate = user.EmploymentStartDate,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employees/{employeeId}/employment-start-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. Admin-strict If-Match (ADR-019 D2).
        // Atomic: FOR-UPDATE re-read + version check, SetEmploymentStartDateAsync (bumps
        // users.version), users_audit UPDATED row — all one tx (ADR-018 D3).
        // employmentStartDate may be null (clears an unknown start date).
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 412 — OptimisticConcurrencyException (stale version)
        //   • 404 — no active user (pre-check or KeyNotFoundException)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            SetEmploymentStartDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — 428 if missing / malformed / If-None-Match: *.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // FOR-UPDATE re-read — canonical snapshot for the If-Match precondition
                // AND the users_audit previous_data JSONB (closes the stale-snapshot race;
                // mirrors the birth-date PUT pattern).
                var lockedHit = await userRepo.GetByIdWithVersionAsync(conn, tx, employeeId, ct);
                if (lockedHit is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }
                var (lockedUser, lockedVersion) = lockedHit.Value;

                if (lockedVersion != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = lockedVersion,
                    }, statusCode: 412);
                }

                long newVersion;
                try
                {
                    newVersion = await userRepo.SetEmploymentStartDateAsync(
                        conn, tx, employeeId, body.EmploymentStartDate, expectedVersion, ct);
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
                    return Results.NotFound(new { error = "Employee not found" });
                }

                // users_audit UPDATED row — full-row JSONB snapshot captures
                // employment_start_date (init.sql: users_audit stores whole-row
                // previous/new data; no schema change needed). password_hash deliberately
                // excluded — mirrors the birth-date PUT audit row.
                var previousData = JsonSerializer.Serialize(new { employmentStartDate = lockedUser.EmploymentStartDate });
                var newData = JsonSerializer.Serialize(new { employmentStartDate = body.EmploymentStartDate });
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

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                return Results.Ok(new
                {
                    employeeId,
                    employmentStartDate = body.EmploymentStartDate,
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

    /// <summary>PUT employment-start body. <c>EmploymentStartDate</c> may be null
    /// (clear an unknown start date).</summary>
    private sealed record SetEmploymentStartDateRequest
    {
        public DateOnly? EmploymentStartDate { get; init; }
    }
}
