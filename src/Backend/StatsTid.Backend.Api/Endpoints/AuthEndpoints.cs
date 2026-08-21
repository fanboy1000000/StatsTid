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
            HttpContext httpContext,
            JwtTokenService tokenService,
            UserRepository userRepository,
            RoleAssignmentRepository roleAssignmentRepository,
            UserAgreementCodeRepository userAgreementCodeRepo,
            CancellationToken ct) =>
        {
            // SEC-040 (S132 ← QUAL-063) — failed logins must be OBSERVABLE. Before this,
            // a rejected login produced no application log line and the attempted username
            // reached no store, so ops could not tell an unknown-user attempt from a
            // wrong-password one, nor spot a brute-force pattern; the only trace was the
            // AuditLoggingMiddleware 401 row (IP + null actor, no identifier).
            //
            // On every failure we now emit ONE structured WARNING carrying:
            //   • the attempted USERNAME — an identifier, NEVER a secret. Two DISTINCT
            //     defenses apply: (a) it is a structured message-TEMPLATE placeholder
            //     ({Username}), which stops template injection; and (b) its VALUE is run
            //     through SanitizeForLog, which strips CR/LF + other control chars so an
            //     attacker-supplied newline cannot forge an extra log line at a plain-text/
            //     console sink (log-forging, CWE-117). The placeholder alone does NOT escape
            //     the value — the sanitizer is what closes the CR/LF vector (Step-5a Codex P2);
            //   • a failure-reason CLASS (unknown_user vs invalid_password) so the two are
            //     distinguishable server-side;
            //   • the source IP;
            //   • the request CorrelationId, which JOINS this line to the audit_log 401 row.
            // The PASSWORD (and any secret) is NEVER logged.
            //
            // SECURITY: the reason class lives ONLY in this server-side log. The HTTP
            // response stays a generic 401 (Results.Unauthorized()) for every failure class,
            // so no user-enumeration oracle is exposed to the client.
            void LogFailedLogin(string failureReason) =>
                logger.LogWarning(
                    "Login failed: username={Username} reason={FailureReason} ip={RemoteIp} correlationId={CorrelationId}",
                    SanitizeForLog(request.Username),
                    failureReason,
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.GetActorContext().CorrelationId);

            if (useDbAuth)
            {
                var dbUser = await userRepository.GetByUsernameAsync(request.Username, ct);
                if (dbUser is null)
                {
                    LogFailedLogin("unknown_user");
                    return Results.Unauthorized();
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, dbUser.PasswordHash))
                {
                    LogFailedLogin("invalid_password");
                    return Results.Unauthorized();
                }

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
                    // Values are CR/LF-sanitized (SEC-040 Step-5a P2) to keep this file's
                    // username/identifier log calls uniformly forge-safe alongside LogFailedLogin.
                    logger.LogWarning(
                        "Inconsistent state: user_agreement_codes has no live row for user {UserId}; " +
                        "falling back to users.agreement_code cache value '{CacheValue}'.",
                        SanitizeForLog(dbUser.UserId), SanitizeForLog(dbUser.AgreementCode));
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

                if (!users.TryGetValue(request.Username, out var user))
                {
                    LogFailedLogin("unknown_user");
                    return Results.Unauthorized();
                }

                if (request.Password != user.Password)
                {
                    LogFailedLogin("invalid_password");
                    return Results.Unauthorized();
                }

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
        })
        .Produces<LoginResponse>(StatusCodes.Status200OK); // S118 / TASK-11800 — both success sites already return the named LoginResponse (in-memory branch serializes orgId: null)

        return app;
    }

    /// <summary>
    /// SEC-040 (S132, Step-5a Codex P2) — CR/LF &amp; control-character log sanitizer for
    /// user-supplied identifier values. A structured message-template placeholder (e.g.
    /// <c>{Username}</c>) prevents TEMPLATE injection, but it does NOT escape the VALUE: an
    /// attacker-supplied username containing a newline would render as a forged extra line at a
    /// plain-text/console log sink (log-forging, CWE-117). This strips every Unicode control
    /// character (CR, LF, tab, etc.), collapsing a multi-line value to a single safe line, before
    /// it enters any log call in this file. <c>null</c>/empty pass through unchanged (the
    /// placeholder logs an empty value). The broader app-wide audit of identifier log calls
    /// across the codebase is a separately-tracked follow-up — do NOT widen it here.
    /// </summary>
    private static string? SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Any(char.IsControl)
            ? new string(value.Where(c => !char.IsControl(c)).ToArray())
            : value;
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
