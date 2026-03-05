using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class AdminEndpoints
{
    // ── Valid org types ──
    private static readonly HashSet<string> ValidOrgTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MINISTRY", "STYRELSE", "AFDELING", "TEAM"
    };

    // ── Role ID to hierarchy-level mapping for privilege check ──
    private static readonly Dictionary<string, int> RoleHierarchy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GLOBAL_ADMIN"] = 1,
        ["LOCAL_ADMIN"] = 2,
        ["LOCAL_HR"] = 3,
        ["LOCAL_LEADER"] = 4,
        ["EMPLOYEE"] = 5
    };

    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // Organization Endpoints
        // ═══════════════════════════════════════════

        // 1. GET /api/admin/organizations — List organizations visible to actor
        app.MapGet("/api/admin/organizations", async (
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // GlobalAdmin with GLOBAL scope: return all
            if (HasGlobalScope(actor))
            {
                var all = await orgRepo.GetAllAsync(ct);
                return Results.Ok(all.Select(MapOrgResponse));
            }

            // Scoped roles: filter to orgs within actor's scope
            var allOrgs = await orgRepo.GetAllAsync(ct);
            var visibleOrgs = new List<object>();

            foreach (var org in allOrgs)
            {
                var (allowed, _) = await scopeValidator.ValidateOrgAccessAsync(actor, org.OrgId, ct);
                if (allowed)
                    visibleOrgs.Add(MapOrgResponse(org));
            }

            return Results.Ok(visibleOrgs);
        }).RequireAuthorization("HROrAbove");

        // 2. POST /api/admin/organizations — Create organization
        app.MapPost("/api/admin/organizations", async (
            CreateOrganizationRequest request,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate org type
            if (!ValidOrgTypes.Contains(request.OrgType))
                return Results.BadRequest(new { error = $"Invalid orgType. Must be one of: {string.Join(", ", ValidOrgTypes)}" });

            // Compute materialized path
            string materializedPath;
            if (request.ParentOrgId is not null)
            {
                // Validate actor scope covers parent org
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.ParentOrgId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

                var parentOrg = await orgRepo.GetByIdAsync(request.ParentOrgId, ct);
                if (parentOrg is null)
                    return Results.BadRequest(new { error = "Parent organization not found" });

                materializedPath = $"{parentOrg.MaterializedPath}{request.OrgId}/";
            }
            else
            {
                // Top-level org — only GlobalAdmin should create these
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can create top-level organizations" }, statusCode: 403);

                materializedPath = $"/{request.OrgId}/";
            }

            // Check if org already exists
            var existing = await orgRepo.GetByIdAsync(request.OrgId, ct);
            if (existing is not null)
                return Results.Conflict(new { error = $"Organization '{request.OrgId}' already exists" });

            // Insert via direct Npgsql
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active, created_at, updated_at)
                VALUES (@orgId, @orgName, @orgType, @parentOrgId, @materializedPath, @agreementCode, @okVersion, TRUE, @now, @now)
                """, conn);
            cmd.Parameters.AddWithValue("orgId", request.OrgId);
            cmd.Parameters.AddWithValue("orgName", request.OrgName);
            cmd.Parameters.AddWithValue("orgType", request.OrgType.ToUpperInvariant());
            cmd.Parameters.AddWithValue("parentOrgId", (object?)request.ParentOrgId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("materializedPath", materializedPath);
            cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode);
            cmd.Parameters.AddWithValue("okVersion", request.OkVersion);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync(ct);

            // Emit domain event
            var @event = new OrganizationCreated
            {
                OrgId = request.OrgId,
                OrgName = request.OrgName,
                OrgType = request.OrgType.ToUpperInvariant(),
                ParentOrgId = request.ParentOrgId,
                MaterializedPath = materializedPath,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync($"org-{request.OrgId}", @event, ct);

            return Results.Created($"/api/admin/organizations/{request.OrgId}", new
            {
                orgId = request.OrgId,
                orgName = request.OrgName,
                orgType = request.OrgType.ToUpperInvariant(),
                parentOrgId = request.ParentOrgId,
                materializedPath,
                agreementCode = request.AgreementCode,
                okVersion = request.OkVersion
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // User Endpoints
        // ═══════════════════════════════════════════

        // 3. GET /api/admin/organizations/{orgId}/users — List users in org
        app.MapGet("/api/admin/organizations/{orgId}/users", async (
            string orgId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers target org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var users = await userRepo.GetByOrgAsync(orgId, ct);

            // NEVER return password hashes
            var response = users.Select(u => new
            {
                userId = u.UserId,
                username = u.Username,
                displayName = u.DisplayName,
                email = u.Email,
                agreementCode = u.AgreementCode,
                employmentCategory = u.EmploymentCategory
            });

            return Results.Ok(response);
        }).RequireAuthorization("HROrAbove");

        // 4. POST /api/admin/users — Create user
        app.MapPost("/api/admin/users", async (
            CreateUserRequest request,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers target org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.PrimaryOrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Hash password with BCrypt
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Insert via direct Npgsql
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);

            // Check if user already exists
            await using var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM users WHERE user_id = @userId OR username = @username", conn);
            checkCmd.Parameters.AddWithValue("userId", request.UserId);
            checkCmd.Parameters.AddWithValue("username", request.Username);
            var existingCount = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
            if (existingCount > 0)
                return Results.Conflict(new { error = "User with this ID or username already exists" });

            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, is_active, created_at, updated_at)
                VALUES (@userId, @username, @passwordHash, @displayName, @email, @primaryOrgId, @agreementCode, @okVersion, 'Standard', TRUE, @now, @now)
                """, conn);
            cmd.Parameters.AddWithValue("userId", request.UserId);
            cmd.Parameters.AddWithValue("username", request.Username);
            cmd.Parameters.AddWithValue("passwordHash", passwordHash);
            cmd.Parameters.AddWithValue("displayName", request.DisplayName);
            cmd.Parameters.AddWithValue("email", (object?)request.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("primaryOrgId", request.PrimaryOrgId);
            cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode);
            cmd.Parameters.AddWithValue("okVersion", request.OkVersion);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync(ct);

            // Emit domain event
            var @event = new UserCreated
            {
                UserId = request.UserId,
                Username = request.Username,
                DisplayName = request.DisplayName,
                PrimaryOrgId = request.PrimaryOrgId,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync($"user-{request.UserId}", @event, ct);

            return Results.Created($"/api/admin/users/{request.UserId}", new
            {
                userId = request.UserId,
                username = request.Username,
                displayName = request.DisplayName,
                email = request.Email,
                primaryOrgId = request.PrimaryOrgId,
                agreementCode = request.AgreementCode,
                okVersion = request.OkVersion
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        // 5. PUT /api/admin/users/{userId} — Update user
        app.MapPut("/api/admin/users/{userId}", async (
            string userId,
            UpdateUserRequest request,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            DbConnectionFactory dbFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Look up existing user
            var existingUser = await userRepo.GetByIdAsync(userId, ct);
            if (existingUser is null)
                return Results.NotFound(new { error = "User not found" });

            // Validate actor scope covers user's current org
            var (allowedCurrent, reasonCurrent) = await scopeValidator.ValidateOrgAccessAsync(actor, existingUser.PrimaryOrgId, ct);
            if (!allowedCurrent)
                return Results.Json(new { error = "Access denied", reason = reasonCurrent }, statusCode: 403);

            // If org is changing, validate actor scope covers new org too
            if (request.PrimaryOrgId is not null && request.PrimaryOrgId != existingUser.PrimaryOrgId)
            {
                var (allowedNew, reasonNew) = await scopeValidator.ValidateOrgAccessAsync(actor, request.PrimaryOrgId, ct);
                if (!allowedNew)
                    return Results.Json(new { error = "Access denied", reason = reasonNew }, statusCode: 403);
            }

            // Update via direct Npgsql
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE users
                SET display_name = @displayName,
                    email = @email,
                    primary_org_id = @primaryOrgId,
                    agreement_code = @agreementCode,
                    updated_at = @now
                WHERE user_id = @userId AND is_active = TRUE
                """, conn);
            cmd.Parameters.AddWithValue("displayName", request.DisplayName ?? existingUser.DisplayName);
            cmd.Parameters.AddWithValue("email", (object?)(request.Email ?? existingUser.Email) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("primaryOrgId", request.PrimaryOrgId ?? existingUser.PrimaryOrgId);
            cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode ?? existingUser.AgreementCode);
            cmd.Parameters.AddWithValue("now", now);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(ct);

            // Emit domain event for auditability
            var @event = new UserUpdated
            {
                UserId = userId,
                DisplayName = request.DisplayName ?? existingUser.DisplayName,
                Email = request.Email ?? existingUser.Email,
                PrimaryOrgId = request.PrimaryOrgId ?? existingUser.PrimaryOrgId,
                AgreementCode = request.AgreementCode ?? existingUser.AgreementCode,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync($"user-{userId}", @event, ct);

            return Results.Ok(new
            {
                userId,
                displayName = request.DisplayName ?? existingUser.DisplayName,
                email = request.Email ?? existingUser.Email,
                primaryOrgId = request.PrimaryOrgId ?? existingUser.PrimaryOrgId,
                agreementCode = request.AgreementCode ?? existingUser.AgreementCode
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // Role Assignment Endpoints
        // ═══════════════════════════════════════════

        // 6. GET /api/admin/users/{userId}/roles — List role assignments
        app.MapGet("/api/admin/users/{userId}/roles", async (
            string userId,
            UserRepository userRepo,
            RoleAssignmentRepository roleRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Look up the target user to get their org
            var targetUser = await userRepo.GetByIdAsync(userId, ct);
            if (targetUser is null)
                return Results.NotFound(new { error = "User not found" });

            // Validate actor scope covers user's org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, targetUser.PrimaryOrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var assignments = await roleRepo.GetByUserIdAsync(userId, ct);

            var response = assignments.Select(a => new
            {
                assignmentId = a.AssignmentId,
                roleId = a.RoleId,
                orgId = a.OrgId,
                scopeType = a.ScopeType,
                assignedBy = a.AssignedBy,
                assignedAt = a.AssignedAt,
                expiresAt = a.ExpiresAt
            });

            return Results.Ok(response);
        }).RequireAuthorization("HROrAbove");

        // 7. POST /api/admin/roles/grant — Grant role assignment
        app.MapPost("/api/admin/roles/grant", async (
            GrantRoleRequest request,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers target org
            if (request.OrgId is not null)
            {
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.OrgId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }
            else
            {
                // Granting a GLOBAL scope — only GlobalAdmin
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can grant global-scoped roles" }, statusCode: 403);
            }

            // Validate target user exists
            var targetUser = await userRepo.GetByIdAsync(request.UserId, ct);
            if (targetUser is null)
                return Results.NotFound(new { error = "Target user not found" });

            // Validate role ID is valid
            if (!RoleHierarchy.ContainsKey(request.RoleId))
                return Results.BadRequest(new { error = $"Invalid roleId. Must be one of: {string.Join(", ", RoleHierarchy.Keys)}" });

            // Validate privilege hierarchy: cannot grant a role with higher privilege than actor's own
            var actorLevel = GetActorHighestPrivilegeLevel(actor);
            var targetRoleLevel = RoleHierarchy[request.RoleId];
            if (targetRoleLevel < actorLevel)
                return Results.Json(new { error = "Access denied", reason = "Cannot grant a role with higher privilege than your own" }, statusCode: 403);

            // Validate scope type
            if (request.ScopeType is not ("GLOBAL" or "ORG_ONLY" or "ORG_AND_DESCENDANTS"))
                return Results.BadRequest(new { error = "Invalid scopeType. Must be GLOBAL, ORG_ONLY, or ORG_AND_DESCENDANTS" });

            // Insert role assignment + audit
            var assignmentId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // Insert role assignment
                await using var assignCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignments (assignment_id, user_id, role_id, org_id, scope_type, assigned_by, assigned_at, expires_at, is_active)
                    VALUES (@assignmentId, @userId, @roleId, @orgId, @scopeType, @assignedBy, @assignedAt, @expiresAt, TRUE)
                    """, conn, tx);
                assignCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                assignCmd.Parameters.AddWithValue("userId", request.UserId);
                assignCmd.Parameters.AddWithValue("roleId", request.RoleId);
                assignCmd.Parameters.AddWithValue("orgId", (object?)request.OrgId ?? DBNull.Value);
                assignCmd.Parameters.AddWithValue("scopeType", request.ScopeType);
                assignCmd.Parameters.AddWithValue("assignedBy", actor.ActorId ?? "system");
                assignCmd.Parameters.AddWithValue("assignedAt", now);
                assignCmd.Parameters.AddWithValue("expiresAt", (object?)request.ExpiresAt ?? DBNull.Value);
                await assignCmd.ExecuteNonQueryAsync(ct);

                // Insert audit record
                await using var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignment_audit (audit_id, assignment_id, action, performed_by, performed_at, details)
                    VALUES (@auditId, @assignmentId, 'GRANT', @performedBy, @performedAt, @details)
                    """, conn, tx);
                auditCmd.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                auditCmd.Parameters.AddWithValue("performedBy", actor.ActorId ?? "system");
                auditCmd.Parameters.AddWithValue("performedAt", now);
                auditCmd.Parameters.AddWithValue("details",
                    $"Granted {request.RoleId} on {request.OrgId ?? "GLOBAL"} ({request.ScopeType}) to {request.UserId}");
                await auditCmd.ExecuteNonQueryAsync(ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // Emit domain event
            var @event = new RoleAssignmentGranted
            {
                AssignmentId = assignmentId,
                UserId = request.UserId,
                RoleId = request.RoleId,
                OrgId = request.OrgId,
                ScopeType = request.ScopeType,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync($"user-{request.UserId}", @event, ct);

            return Results.Created($"/api/admin/users/{request.UserId}/roles", new
            {
                assignmentId,
                userId = request.UserId,
                roleId = request.RoleId,
                orgId = request.OrgId,
                scopeType = request.ScopeType,
                assignedBy = actor.ActorId,
                assignedAt = now,
                expiresAt = request.ExpiresAt
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        // 8. POST /api/admin/roles/revoke — Revoke role assignment
        app.MapPost("/api/admin/roles/revoke", async (
            RevokeRoleRequest request,
            RoleAssignmentRepository roleRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Look up the assignment to revoke
            // We need to find the assignment by ID — read it directly via Npgsql
            await using var lookupConn = dbFactory.Create();
            await lookupConn.OpenAsync(ct);
            await using var lookupCmd = new NpgsqlCommand(
                "SELECT assignment_id, user_id, role_id, org_id, scope_type, assigned_by, assigned_at, expires_at, is_active FROM role_assignments WHERE assignment_id = @assignmentId",
                lookupConn);
            lookupCmd.Parameters.AddWithValue("assignmentId", request.AssignmentId);
            await using var reader = await lookupCmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                return Results.NotFound(new { error = "Role assignment not found" });

            var assignmentUserId = reader.GetString(reader.GetOrdinal("user_id"));
            var assignmentRoleId = reader.GetString(reader.GetOrdinal("role_id"));
            var assignmentOrgId = reader.IsDBNull(reader.GetOrdinal("org_id")) ? null : reader.GetString(reader.GetOrdinal("org_id"));
            var isActive = reader.GetBoolean(reader.GetOrdinal("is_active"));
            await reader.CloseAsync();
            await lookupConn.CloseAsync();

            if (!isActive)
                return Results.BadRequest(new { error = "Role assignment is already revoked" });

            // Validate actor scope covers the assignment's org
            if (assignmentOrgId is not null)
            {
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, assignmentOrgId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }
            else
            {
                // Revoking a GLOBAL scope — only GlobalAdmin
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can revoke global-scoped roles" }, statusCode: 403);
            }

            // Deactivate assignment + insert audit
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await using var deactivateCmd = new NpgsqlCommand(
                    "UPDATE role_assignments SET is_active = FALSE WHERE assignment_id = @assignmentId", conn, tx);
                deactivateCmd.Parameters.AddWithValue("assignmentId", request.AssignmentId);
                await deactivateCmd.ExecuteNonQueryAsync(ct);

                await using var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignment_audit (audit_id, assignment_id, action, performed_by, performed_at, details)
                    VALUES (@auditId, @assignmentId, 'REVOKE', @performedBy, @performedAt, @details)
                    """, conn, tx);
                auditCmd.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCmd.Parameters.AddWithValue("assignmentId", request.AssignmentId);
                auditCmd.Parameters.AddWithValue("performedBy", actor.ActorId ?? "system");
                auditCmd.Parameters.AddWithValue("performedAt", now);
                auditCmd.Parameters.AddWithValue("details",
                    $"Revoked {assignmentRoleId} from {assignmentUserId}" + (request.Reason is not null ? $". Reason: {request.Reason}" : ""));
                await auditCmd.ExecuteNonQueryAsync(ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // Emit domain event
            var @event = new RoleAssignmentRevoked
            {
                AssignmentId = request.AssignmentId,
                UserId = assignmentUserId,
                RoleId = assignmentRoleId,
                Reason = request.Reason,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync($"user-{assignmentUserId}", @event, ct);

            return Results.Ok(new
            {
                assignmentId = request.AssignmentId,
                userId = assignmentUserId,
                roleId = assignmentRoleId,
                revoked = true,
                revokedBy = actor.ActorId,
                revokedAt = now,
                reason = request.Reason
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        return app;
    }

    // ── Helper Methods ──

    private static bool HasGlobalScope(ActorContext actor)
    {
        if (actor.Scopes is null || actor.Scopes.Length == 0)
        {
            // Fallback: check if actor role is GlobalAdmin (for non-DB auth mode)
            return string.Equals(actor.ActorRole, StatsTidRoles.GlobalAdmin, StringComparison.Ordinal);
        }

        return actor.Scopes.Any(s => s.ScopeType == "GLOBAL");
    }

    private static int GetActorHighestPrivilegeLevel(ActorContext actor)
    {
        // Check explicit role first
        var roleLevel = StatsTidRoles.GetHierarchyLevel(actor.ActorRole ?? StatsTidRoles.Employee);

        // Check scopes for any higher-privilege role
        if (actor.Scopes is { Length: > 0 })
        {
            foreach (var scope in actor.Scopes)
            {
                var scopeLevel = StatsTidRoles.GetHierarchyLevel(scope.Role);
                if (scopeLevel < roleLevel)
                    roleLevel = scopeLevel;
            }
        }

        return roleLevel;
    }

    private static object MapOrgResponse(StatsTid.SharedKernel.Models.Organization org) => new
    {
        orgId = org.OrgId,
        orgName = org.OrgName,
        orgType = org.OrgType,
        parentOrgId = org.ParentOrgId,
        materializedPath = org.MaterializedPath,
        agreementCode = org.AgreementCode
    };

    // ── Request DTOs (co-located) ──

    private sealed class CreateOrganizationRequest
    {
        public required string OrgId { get; init; }
        public required string OrgName { get; init; }
        public required string OrgType { get; init; }
        public string? ParentOrgId { get; init; }
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
    }

    private sealed class CreateUserRequest
    {
        public required string UserId { get; init; }
        public required string Username { get; init; }
        public required string Password { get; init; }
        public required string DisplayName { get; init; }
        public string? Email { get; init; }
        public required string PrimaryOrgId { get; init; }
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
    }

    private sealed class UpdateUserRequest
    {
        public string? DisplayName { get; init; }
        public string? Email { get; init; }
        public string? PrimaryOrgId { get; init; }
        public string? AgreementCode { get; init; }
    }

    private sealed class GrantRoleRequest
    {
        public required string UserId { get; init; }
        public required string RoleId { get; init; }
        public string? OrgId { get; init; }
        public required string ScopeType { get; init; }
        public DateTime? ExpiresAt { get; init; }
    }

    private sealed class RevokeRoleRequest
    {
        public required Guid AssignmentId { get; init; }
        public string? Reason { get; init; }
    }
}
