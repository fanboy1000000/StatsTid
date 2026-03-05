using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app, bool useDbAuth)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            JwtTokenService tokenService,
            UserRepository userRepository,
            RoleAssignmentRepository roleAssignmentRepository,
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

                var token = tokenService.GenerateToken(
                    dbUser.UserId, dbUser.DisplayName, primaryRole, dbUser.AgreementCode,
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
