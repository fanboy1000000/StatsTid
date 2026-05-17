using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app, bool useDbAuth)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.AuthEndpoints");

        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            JwtTokenService tokenService,
            UserRepository userRepository,
            RoleAssignmentRepository roleAssignmentRepository,
            UserAgreementCodeRepository userAgreementCodeRepo,
            CancellationToken ct) =>
        {
            if (useDbAuth)
            {
                var dbUser = await userRepository.GetByUsernameAsync(request.Username, ct);
                if (dbUser is null)
                    return Results.Unauthorized();

                if (!BCrypt.Net.BCrypt.Verify(request.Password, dbUser.PasswordHash))
                    return Results.Unauthorized();

                var assignments = await roleAssignmentRepository.GetByUserIdAsync(dbUser.UserId, ct);
                var scopes = assignments.Select(a =>
                    new RoleScope(MapRoleIdToName(a.RoleId), a.OrgId, a.ScopeType)).ToList();

                var primaryRole = scopes.Count > 0 ? scopes[0].Role : StatsTidRoles.Employee;

                // S34 / TASK-3408 — JWT agreement_code MUST come from the canonical
                // bitemporal store `user_agreement_codes` (ADR-023 D2). `users.agreement_code`
                // is a denormalized cache kept in sync by the admin PUT path (TASK-3407);
                // reading it here would let a stale cache bleed into freshly-minted tokens
                // if the cache-update side of the dual-write ever falls behind the canonical
                // write (defense-in-depth — the dual-write is atomic in tx, but the JWT path
                // should never depend on cache freshness). Adds 1 SELECT per login; login
                // is rare relative to general traffic, so the pre-launch perf budget is
                // unaffected per Step 0b cycle 1 Codex WARNING 2 absorption.
                var canonicalAgreementCode = await userAgreementCodeRepo.GetCurrentAsync(dbUser.UserId, ct);
                if (canonicalAgreementCode is null)
                {
                    // Defensive fallback. Post-backfill (TASK-3403) every user MUST have a
                    // live row in user_agreement_codes; a missing row indicates an
                    // inconsistency between the canonical store and the denormalized cache
                    // (or a user created outside the canonical-write path — bug). Fall back
                    // to the cache to keep login working and warn loudly so ops can
                    // reconcile.
                    logger.LogWarning(
                        "Inconsistent state: user_agreement_codes has no live row for user {UserId}; " +
                        "falling back to users.agreement_code cache value '{CacheValue}'.",
                        dbUser.UserId, dbUser.AgreementCode);
                }
                var agreementCodeForToken = canonicalAgreementCode ?? dbUser.AgreementCode;

                var token = tokenService.GenerateToken(
                    dbUser.UserId, dbUser.DisplayName, primaryRole, agreementCodeForToken,
                    dbUser.PrimaryOrgId, scopes);
                var expiration = DateTime.UtcNow.AddMinutes(480);

                return Results.Ok(new LoginResponse
                {
                    Token = token,
                    ExpiresAt = expiration,
                    EmployeeId = dbUser.UserId,
                    Role = primaryRole,
                    OrgId = dbUser.PrimaryOrgId
                });
            }
            else
            {
                var users = new Dictionary<string, (string Name, string Role, string AgreementCode, string Password)>
                {
                    ["admin01"] = ("Global Administrator", StatsTidRoles.GlobalAdmin, "AC", "admin"),
                    ["ladm01"] = ("Lokal Administrator", StatsTidRoles.LocalAdmin, "HK", "manager"),
                    ["hr01"] = ("HR Medarbejder", StatsTidRoles.LocalHR, "HK", "hr"),
                    ["mgr01"] = ("Team Leder", StatsTidRoles.LocalLeader, "HK", "manager"),
                    ["emp001"] = ("AC Medarbejder", StatsTidRoles.Employee, "AC", "employee"),
                    ["emp002"] = ("HK Medarbejder", StatsTidRoles.Employee, "HK", "employee"),
                    ["emp003"] = ("PROSA Medarbejder", StatsTidRoles.Employee, "PROSA", "employee"),
                };

                if (!users.TryGetValue(request.Username, out var user) || request.Password != user.Password)
                    return Results.Unauthorized();

                var token = tokenService.GenerateToken(request.Username, user.Name, user.Role, user.AgreementCode);
                var expiration = DateTime.UtcNow.AddMinutes(480);

                return Results.Ok(new LoginResponse
                {
                    Token = token,
                    ExpiresAt = expiration,
                    EmployeeId = request.Username,
                    Role = user.Role
                });
            }
        });

        return app;
    }

    private static string MapRoleIdToName(string roleId) => roleId switch
    {
        "GLOBAL_ADMIN" => StatsTidRoles.GlobalAdmin,
        "LOCAL_ADMIN" => StatsTidRoles.LocalAdmin,
        "LOCAL_HR" => StatsTidRoles.LocalHR,
        "LOCAL_LEADER" => StatsTidRoles.LocalLeader,
        "EMPLOYEE" => StatsTidRoles.Employee,
        _ => StatsTidRoles.Employee
    };
}
