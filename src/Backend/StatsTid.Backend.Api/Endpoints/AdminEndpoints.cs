using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class AdminEndpoints
{
    // ── Valid org types (S92/ADR-035 flatten: MAO root -> ORGANISATION) ──
    private static readonly HashSet<string> ValidOrgTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAO", "ORGANISATION"
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
                // S76 B1: HROrAbove policy → LocalHR floor (a non-HR scope covering the org
                // cannot widen this admin list).
                var (allowed, _) = await scopeValidator.ValidateOrgAccessAsync(actor, org.OrgId, StatsTidRoles.LocalHR, ct);
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
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OrganizationCreated> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate org type
            if (!ValidOrgTypes.Contains(request.OrgType))
                return Results.BadRequest(new { error = $"Invalid orgType. Must be one of: {string.Join(", ", ValidOrgTypes)}" });

            var orgType = request.OrgType.ToUpperInvariant();

            // S92/ADR-035 type-scoped parent rules:
            //   MAO          = root         → MUST NOT have a parent.
            //   ORGANISATION = under a MAO  → MUST have a parent, and that parent MUST be a MAO.
            if (orgType == "MAO" && request.ParentOrgId is not null)
                return Results.BadRequest(new { error = "A MAO is a root organization and must not have a parent." });
            if (orgType == "ORGANISATION" && request.ParentOrgId is null)
                return Results.BadRequest(new { error = "An ORGANISATION must have a MAO parent." });

            // Compute materialized path
            string materializedPath;
            if (request.ParentOrgId is not null)
            {
                // Validate actor scope covers parent org. S76 B1: LocalAdminOrAbove policy →
                // LocalAdmin floor (the admitting scope must itself be admin).
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.ParentOrgId, StatsTidRoles.LocalAdmin, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

                var parentOrg = await orgRepo.GetByIdAsync(request.ParentOrgId, ct);
                if (parentOrg is null)
                    return Results.BadRequest(new { error = "Parent organization not found" });

                // S92/ADR-035: an ORGANISATION's parent must be a MAO (the only valid
                // non-root level). MAO never reaches this branch (rejected above).
                if (orgType == "ORGANISATION" && parentOrg.OrgType != "MAO")
                    return Results.BadRequest(new { error = $"An ORGANISATION's parent must be a MAO (got {parentOrg.OrgType})." });

                materializedPath = $"{parentOrg.MaterializedPath}{request.OrgId}/";
            }
            else
            {
                // Top-level org (a MAO root) — only GlobalAdmin should create these
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can create top-level organizations" }, statusCode: 403);

                materializedPath = $"/{request.OrgId}/";
            }

            // Check if org already exists
            var existing = await orgRepo.GetByIdAsync(request.OrgId, ct);
            if (existing is not null)
                return Results.Conflict(new { error = $"Organization '{request.OrgId}' already exists" });

            // Atomic INSERT + outbox-emit per ADR-018 D3 (S26 TASK-2605a prototype):
            // inline organizations INSERT and OrganizationCreated outbox enqueue ride
            // a single explicit transaction; commit at end of try, rollback on throw.
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active, created_at, updated_at)
                    VALUES (@orgId, @orgName, @orgType, @parentOrgId, @materializedPath, @agreementCode, @okVersion, TRUE, @now, @now)
                    """, conn, tx);
                cmd.Parameters.AddWithValue("orgId", request.OrgId);
                cmd.Parameters.AddWithValue("orgName", request.OrgName);
                cmd.Parameters.AddWithValue("orgType", request.OrgType.ToUpperInvariant());
                cmd.Parameters.AddWithValue("parentOrgId", (object?)request.ParentOrgId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("materializedPath", materializedPath);
                cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode);
                cmd.Parameters.AddWithValue("okVersion", request.OkVersion);
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync(ct);

                // Emit domain event in-tx (BEFORE CommitAsync) so the organizations row
                // and the outbox row commit atomically per ADR-018 D3.
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
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — atomic with the
                // organizations row + outbox row per ADR-018 D3/D13).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{request.OrgId}", @event, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

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

        // 2b. PUT /api/admin/organizations/{orgId} — Update organization
        app.MapPut("/api/admin/organizations/{orgId}", async (
            string orgId,
            UpdateOrganizationRequest request,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OrganizationUpdated> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Look up existing org
            var existingOrg = await orgRepo.GetByIdAsync(orgId, ct);
            if (existingOrg is null)
                return Results.NotFound(new { error = "Organization not found" });

            // Validate actor scope covers this org. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Atomic UPDATE + outbox-emit per ADR-018 D3 (S26 TASK-2605b):
            // inline organizations UPDATE and OrganizationUpdated outbox enqueue ride
            // a single explicit transaction; commit at end of try, rollback on throw.
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    UPDATE organizations
                    SET org_name = @orgName,
                        agreement_code = @agreementCode,
                        ok_version = @okVersion,
                        updated_at = @now
                    WHERE org_id = @orgId AND is_active = TRUE
                    """, conn, tx);
                cmd.Parameters.AddWithValue("orgName", request.OrgName ?? existingOrg.OrgName);
                cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode ?? existingOrg.AgreementCode);
                cmd.Parameters.AddWithValue("okVersion", request.OkVersion ?? existingOrg.OkVersion);
                cmd.Parameters.AddWithValue("now", now);
                cmd.Parameters.AddWithValue("orgId", orgId);
                await cmd.ExecuteNonQueryAsync(ct);

                // Emit domain event in-tx (BEFORE CommitAsync) so the organizations row
                // and the outbox row commit atomically per ADR-018 D3.
                var @event = new OrganizationUpdated
                {
                    OrgId = orgId,
                    OrgName = request.OrgName,
                    AgreementCode = request.AgreementCode,
                    OkVersion = request.OkVersion,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId
                };
                // S44 TASK-4413: capture outbox_id + audit_projection insert
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", @event, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.Ok(new
            {
                orgId,
                orgName = request.OrgName ?? existingOrg.OrgName,
                orgType = existingOrg.OrgType,
                parentOrgId = existingOrg.ParentOrgId,
                materializedPath = existingOrg.MaterializedPath,
                agreementCode = request.AgreementCode ?? existingOrg.AgreementCode,
                okVersion = request.OkVersion ?? existingOrg.OkVersion
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

            // Validate actor scope covers target org. S76 B1: HROrAbove policy → LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // S35 Step 7a cycle 1 absorption (Codex WARNING-1) — list rows now
            // carry primaryOrgId (for the UI table render at L368) AND version
            // (so the frontend `User` interface in useAdmin.ts stays honest
            // about its row-version field). The edit flow still re-fetches via
            // the per-user GET endpoint to bind the ETag header before PUT;
            // list-row version is for type-honesty and forward-compat, not the
            // source of truth on the next PUT.
            var users = await userRepo.GetByOrgWithVersionAsync(orgId, ct);

            // NEVER return password hashes
            var response = users.Select(row => new
            {
                userId = row.User.UserId,
                username = row.User.Username,
                displayName = row.User.DisplayName,
                email = row.User.Email,
                primaryOrgId = row.User.PrimaryOrgId,
                agreementCode = row.User.AgreementCode,
                employmentCategory = row.User.EmploymentCategory,
                version = row.Version,
            });

            return Results.Ok(response);
        }).RequireAuthorization("HROrAbove");

        // 3b. GET /api/admin/users/{userId} — Read single user with ETag
        //
        // S35 / TASK-3506 — admin-strict If-Match GET partner per ADR-019 D2
        // (4th application after S25 agreement_configs / position_override_configs /
        // wage_type_mappings; closest sibling precedent at
        // EmployeeProfileEndpoints.cs:105-142 GET). Stamps `ETag: "<version>"` from
        // the same atomic snapshot it serializes (TASK-3505's non-tx
        // GetByIdWithVersionAsync overload) so the admin UI's subsequent PUT can
        // compose a coherent If-Match. RBAC HROrAbove matches the existing PUT +
        // POST on the same resource (cycle-1 Reviewer WARNING absorption: NOT
        // LocalAdminOrAbove — keeps read access aligned with the GET-list endpoint
        // above; admin-strict If-Match enforcement happens on the PUT below).
        // NEVER returns password_hash — body shape mirrors the list-endpoint
        // projection above (L280-288) plus okVersion/employmentCategory/version.
        app.MapGet("/api/admin/users/{userId}", async (
            string userId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var hit = await userRepo.GetByIdWithVersionAsync(userId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "User not found" });
            var (user, version) = hit.Value;

            // S76 B1: HROrAbove policy → LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, user.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            context.Response.Headers.ETag = $"\"{version}\"";

            return Results.Ok(new
            {
                userId = user.UserId,
                username = user.Username,
                displayName = user.DisplayName,
                email = user.Email,
                primaryOrgId = user.PrimaryOrgId,
                agreementCode = user.AgreementCode,
                okVersion = user.OkVersion,
                employmentCategory = user.EmploymentCategory,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // 4. POST /api/admin/users — Create user
        app.MapPost("/api/admin/users", async (
            CreateUserRequest request,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ReportingLineRepository reportingLineRepo,
            IAuditProjectionMapper<UserCreated> auditMapper,
            IAuditProjectionMapper<EmployeeProfileCreated> profileCreatedMapper,
            IAuditProjectionMapper<UserAgreementCodeSeeded> uacSeededMapper,
            AuditProjectionRepository auditRepo,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.AdminEndpoints.UsersPost");

            // Validate actor scope covers target org. S91 TASK-9102: tree-page surface opened to
            // LocalHR — HROrAbove policy → LocalHR floor. Org-scope containment unchanged (HR stays
            // bounded to its own org subtree; the optional create+assign approver below remains
            // same-tree-validated, so the new edge cannot escape the actor's scope).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // S95 / ADR-035 slice 4 — ORGANISATION-HOME GUARD. A user must sit on an ORGANISATION
            // (employees live on Organisations, not on MAOs — the flat-authority model). Reject a
            // primary_org_id whose org_type is not ORGANISATION (mirrors the S93 grant-MAO-reject).
            // This makes "users sit on Organisations" invariant by-construction via the guarded
            // endpoint; raw-SQL seeds (init.sql / demo) comply by construction.
            var homeOrg = await orgRepo.GetByIdAsync(request.PrimaryOrgId, ct);
            if (homeOrg is null)
                return Results.BadRequest(new { error = "Primary org not found." });
            if (!string.Equals(homeOrg.OrgType, "ORGANISATION", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "A user's primary org must be an Organisation (a MAO holds no employees)." });

            // Hash password with BCrypt
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Open the connection up-front: pre-flight existence check runs OUTSIDE the
            // tx (read-only, no atomicity benefit and would only extend tx duration —
            // S26 TASK-2605b Reviewer NOTE 1+2). The same connection then carries the
            // tx for the atomic INSERT + outbox emit per ADR-018 D3.
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);

            // Check if user already exists (pre-flight, outside tx)
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM users WHERE user_id = @userId OR username = @username", conn))
            {
                checkCmd.Parameters.AddWithValue("userId", request.UserId);
                checkCmd.Parameters.AddWithValue("username", request.Username);
                var existingCount = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
                if (existingCount > 0)
                    return Results.Conflict(new { error = "User with this ID or username already exists" });
            }

            // Atomic 4-way INSERT + outbox-emit per ADR-018 D3 + S31 TASK-3108:
            // (1) users INSERT, (2) employee_profiles INSERT, (3) UserCreated outbox
            // enqueue, (4) EmployeeProfileCreated outbox enqueue — all ride a single
            // explicit transaction on the same connection; commit at end of try,
            // rollback on throw. S31 invariant: every active user has exactly one
            // live employee_profiles row. Defaults mirror EmployeeProfileSeeder
            // (TASK-3106): weekly_norm_hours=37.0, part_time_fraction=1.000,
            // position=NULL. EffectiveFrom uses 0001-01-01 anchor (same as backfill)
            // for consistent "always here" semantics; HR overrides via TASK-3107
            // PUT /api/admin/employee-profiles/{employeeId}.
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // (1) users INSERT
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, is_active, created_at, updated_at)
                    VALUES (@userId, @username, @passwordHash, @displayName, @email, @primaryOrgId, @agreementCode, @okVersion, 'Standard', TRUE, @now, @now)
                    """, conn, tx);
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

                // (1b) users_audit CREATED row in-tx — S35 / TASK-3506 closes the
                // last unprotected admin write surface under the ADR-019 D8
                // every-CREATE-gets-a-CREATED-audit-row invariant. Mirrors the
                // employee_profile_audit CREATED row below (L411-422) + the
                // user_agreement_codes_audit CREATED row at L456-477. users.version
                // is schema DEFAULT 1 from TASK-3501; version_before NULL (no
                // predecessor); version_after = 1. password_hash deliberately
                // EXCLUDED from new_data — audit JSONB must never carry credentials.
                var userNewData = JsonSerializer.Serialize(new
                {
                    displayName = request.DisplayName,
                    email = request.Email,
                    primaryOrgId = request.PrimaryOrgId,
                    agreementCode = request.AgreementCode,
                });
                await using (var userAuditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO users_audit (
                        user_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @userId, 'CREATED',
                        NULL, @newData::jsonb,
                        NULL, 1,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    userAuditCmd.Parameters.AddWithValue("userId", request.UserId);
                    userAuditCmd.Parameters.AddWithValue("newData", userNewData);
                    userAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "unknown");
                    userAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                    await userAuditCmd.ExecuteNonQueryAsync(ct);
                }

                // (2) employee_profiles INSERT — S31 invariant: every active user has
                // exactly one live profile row (effective_to IS NULL).
                // S33 in-flight defect fix: stamp effective_from = today (UTC) instead of
                // using the schema DEFAULT '0001-01-01'. Under TASK-3302's new 3-case
                // routing, default-seeded rows would trigger Case C cross-day supersession
                // on the first PUT (because '0001-01-01' < today), creating a brand-new
                // successor row at version=1 instead of UPDATE-in-place at version=2.
                // Stamping today aligns same-day-edit semantics with admin expectations.
                var profileId = Guid.NewGuid();
                await using var profileCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profiles
                        (profile_id, employee_id, part_time_fraction, position,
                         effective_from)
                    VALUES
                        (@profileId, @employeeId, @partTimeFraction, NULL,
                         @effectiveFrom)
                    """, conn, tx);
                profileCmd.Parameters.AddWithValue("profileId", profileId);
                profileCmd.Parameters.AddWithValue("employeeId", request.UserId);
                profileCmd.Parameters.AddWithValue("partTimeFraction", 1.000m);
                profileCmd.Parameters.AddWithValue("effectiveFrom", DateOnly.FromDateTime(DateTime.UtcNow));
                await profileCmd.ExecuteNonQueryAsync(ct);

                // (2b) employee_profile_audit CREATED row in-tx (Step 7a P2 fix —
                // every admin-created profile MUST have an origin audit row to keep
                // the audit chain complete from day one). Mirrors the UPDATED audit
                // shape at EmployeeProfileEndpoints.cs PUT path. previous_data is
                // NULL (no predecessor), version_before is NULL (no prior version),
                // version_after = 1.
                var profileNewData = JsonSerializer.Serialize(new
                {
                    partTimeFraction = 1.000m,
                    position = (string?)null,
                    // S74 / TASK-7400 — admin-create profiles carry a null enhed_label
                    // (the column defaults NULL; the FE falls back to the primary_org
                    // name). The PUT path is the primary write surface for the label.
                    enhedLabel = (string?)null,
                });
                await using (var profileAuditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, 'CREATED',
                        NULL, @newData::jsonb,
                        NULL, 1,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    profileAuditCmd.Parameters.AddWithValue("profileId", profileId);
                    profileAuditCmd.Parameters.AddWithValue("employeeId", request.UserId);
                    profileAuditCmd.Parameters.AddWithValue("newData", profileNewData);
                    profileAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "unknown");
                    profileAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                    await profileAuditCmd.ExecuteNonQueryAsync(ct);
                }

                // (2c) user_agreement_codes Case A INSERT in-tx (S34 / TASK-3407,
                // ADR-023 D2 option (b)) — extends the existing S31 4-way atomicity
                // to 6-way: users + employee_profiles + employee_profile_audit +
                // user_agreement_codes + user_agreement_codes_audit + 3 outboxes.
                // Routes through UserAgreementCodeRepository.SupersedeAndCreateAsync
                // with expectedVersion=null → Case A (Created) because POST creates a
                // brand-new user with no predecessor row. EffectiveFrom = today (UTC)
                // mirrors the employee_profiles today-stamp convention at L383 (S33
                // in-flight defect fix — keeps same-day-edit semantics aligned).
                // Diverges from the seeder's '0001-01-01' anchor because admin-POST
                // is a steady-state path, not a history-covering bootstrap.
                var agreementToday = DateOnly.FromDateTime(DateTime.UtcNow);
                var agreementResult = await userAgreementCodeRepo.SupersedeAndCreateAsync(
                    conn, tx,
                    new UserAgreementCodeSupersedeRequest(
                        UserId: request.UserId,
                        AgreementCode: request.AgreementCode,
                        EffectiveFrom: agreementToday),
                    expectedVersion: null,
                    ct);

                // (2d) user_agreement_codes_audit CREATED row in-tx. Mirrors the
                // backfill seeder's audit shape (UserAgreementCodeBackfillSeeder) so
                // the admin-POST path and the seeder path leave audit rows of the
                // same shape. previous_data NULL (no predecessor); version_before
                // NULL; version_after = 1 (Case A baseline per ADR-020 D2).
                var agreementNewData = JsonSerializer.Serialize(new
                {
                    userId = request.UserId,
                    agreementCode = request.AgreementCode,
                    effectiveFrom = agreementToday.ToString("yyyy-MM-dd"),
                });
                await using (var agreementAuditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO user_agreement_codes_audit (
                        assignment_id, user_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @assignmentId, @userId, 'CREATED',
                        NULL, @newData::jsonb,
                        NULL, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    agreementAuditCmd.Parameters.AddWithValue("assignmentId", agreementResult.AssignmentId);
                    agreementAuditCmd.Parameters.AddWithValue("userId", request.UserId);
                    agreementAuditCmd.Parameters.AddWithValue("newData", agreementNewData);
                    agreementAuditCmd.Parameters.AddWithValue("versionAfter", agreementResult.Version);
                    agreementAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "unknown");
                    agreementAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                    await agreementAuditCmd.ExecuteNonQueryAsync(ct);
                }

                // (3) UserCreated outbox emit in-tx (BEFORE CommitAsync) so the
                // users row and the outbox row commit atomically per ADR-018 D3.
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
                // S44 TASK-4413: UserCreated enqueue converted to capture outbox_id +
                // audit_projection insert (ADR-026 D2 sync-in-tx). EmployeeProfileCreated
                // below converted in S44c TASK-4413c. UserAgreementCodeSeeded below
                // converted in S44b TASK-4413b.
                var userCreatedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{request.UserId}", @event, ct);
                var userCreatedAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var userCreatedAuditRow = auditMapper.Map(@event, userCreatedAuditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, userCreatedOutboxId, @event.EventType, userCreatedAuditRow, userCreatedAuditCtx, ct);

                // (4) EmployeeProfileCreated outbox emit in-tx. Stream
                // employee-profile-{employeeId} per ADR-018 D6 + S31. EffectiveFrom
                // matches EmployeeProfileSeeder's 0001-01-01 anchor for consistent
                // S32 replay semantics.
                // S33 Step 7a cycle 2 convergent BLOCKER absorption: event's EffectiveFrom
                // must match the row's stamped effective_from (ADR-018 D3 atomic-outbox
                // row/event parity). After TASK-3312b's admin-POST today-stamp, the row at
                // L383 carries today; pre-fix this event claimed '0001-01-01' (seeder
                // convention from S31 TASK-3108). Phase 4e replay consumers reconstructing
                // employee profile timelines from the event stream now see consistent state.
                var profileEvent = new EmployeeProfileCreated
                {
                    ProfileId = profileId,
                    EmployeeId = request.UserId,
                    PartTimeFraction = 1.000m,
                    Position = null,
                    // S74 / TASK-7400 — null at create; the PUT path sets the label.
                    EnhedLabel = null,
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44c TASK-4413c: EmployeeProfileCreated cutover to
                // EnqueueAndReturnIdAsync + audit_projection insert (ADR-026 D2
                // sync-in-tx). TENANT_TARGETED — ResolvedTargetOrgId from the
                // request's PrimaryOrgId (the user being created).
                var profileOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-profile-{request.UserId}", profileEvent, ct);
                var profileAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(profileEvent.OccurredAt),
                    ResolvedTargetOrgId: request.PrimaryOrgId);
                var profileAuditRow = profileCreatedMapper.Map(profileEvent, profileAuditCtx);
                await auditRepo.InsertAsync(conn, tx, profileEvent.EventId, profileOutboxId, profileEvent.EventType, profileAuditRow, profileAuditCtx, ct);

                // (5) UserAgreementCodeSeeded outbox emit in-tx (S34 / TASK-3407,
                // ADR-023 D2). Same canonical user-{userId} stream as UserCreated
                // (TASK-3309 + backfill seeder convention) so the per-user lineage
                // replays in one walk. Seeded — NOT Changed/Superseded — because this
                // is the FIRST-EVER agreement-code assignment for the user (Step 0b
                // BLOCKER 1 absorption: no predecessor; matches the backfill seeder's
                // bootstrap semantic). EffectiveFrom = today (UTC) mirrors the row's
                // stamped effective_from at L432 (ADR-018 D3 atomic-outbox row/event
                // parity).
                var agreementSeededEvent = new UserAgreementCodeSeeded
                {
                    UserId = request.UserId,
                    AgreementCode = request.AgreementCode,
                    EffectiveFrom = agreementToday,
                    RowVersion = agreementResult.Version,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44b TASK-4413b: UserAgreementCodeSeeded cutover to
                // EnqueueAndReturnIdAsync + audit_projection insert (ADR-026 D2
                // sync-in-tx). TENANT_TARGETED — ResolvedTargetOrgId from the
                // request's PrimaryOrgId (the user being created).
                var uacSeededOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{request.UserId}", agreementSeededEvent, ct);
                var uacSeededCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(agreementSeededEvent.OccurredAt),
                    ResolvedTargetOrgId: request.PrimaryOrgId);
                var uacSeededRow = uacSeededMapper.Map(agreementSeededEvent, uacSeededCtx);
                await auditRepo.InsertAsync(conn, tx, agreementSeededEvent.EventId, uacSeededOutboxId, agreementSeededEvent.EventType, uacSeededRow, uacSeededCtx, ct);

                // (6) S74 R9 — OPTIONAL atomic create+assign. When an approverId is supplied,
                //     create the new person's PRIMARY reporting line under it in the SAME tx, so
                //     the create+assign is all-or-nothing and the person is never an orphan via
                //     this path. ValidateSameOrganisationAsync/cycle-guard run in-tx (the (conn,tx)
                //     overloads see the just-inserted user row, which fresh-connection reads
                //     could not). Emits ReportingLineAssigned + a reporting_line_audit row,
                //     EXACTLY like the normal assign path (ReportingLineEndpoints assign).
                if (!string.IsNullOrWhiteSpace(request.ApproverId))
                {
                    // S74-7403 B1 — TOTAL LOCK ORDER (identical to the assign endpoints, deadlock-safe):
                    //   advisory → user rows id-ordered FOR UPDATE (in ValidateSameOrganisationAsync) →
                    //   cycle guard → slot. NO user row is locked before the advisory.
                    //
                    // Step 1a: derive the advisory key from the NEW user's Organisation. S95 / ADR-035
                    // slice 4: the tree-WALK is RETIRED — the new user's Organisation IS its
                    // primary_org_id (request.PrimaryOrgId), fixed by THIS tx's INSERT above (already
                    // exclusively held, invisible to any concurrent transfer until COMMIT) — so the key
                    // cannot drift and no post-validation re-acquire is needed.
                    string rlTreeRoot;
                    try
                    {
                        var newUserTreeRoot = request.PrimaryOrgId;

                        // Step 1b: take the tree advisory lock FIRST (parked → no user rows held).
                        await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, newUserTreeRoot, ct);

                        // Step 2: same-Organisation validation UNDER the advisory — pins BOTH the new user
                        // AND the approver `users` rows FOR UPDATE in id-order (B1: a concurrent
                        // cross-Organisation transfer of the approver cannot create a cross-Organisation
                        // edge, ADR-027 D2). Returns the authoritative common Organisation (==
                        // newUserTreeRoot, since the new user's org is fixed). W1: throws
                        // InvalidOperationException when the approver (or the new user's org) cannot be
                        // resolved → clean 400 below.
                        rlTreeRoot = await reportingLineRepo.ValidateSameOrganisationAsync(
                            conn, tx, request.UserId, request.ApproverId!, ct);
                    }
                    catch (InvalidOperationException ioEx)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new { error = ioEx.Message });
                    }

                    // Step 3: R8 cycle guard — descendant walk under the held tree lock. A brand-new
                    // person has no descendants yet, but we run it for consistency (and it self-cycle-
                    // rejects approverId == userId with a friendly 4xx instead of a DB CHECK 23514).
                    await reportingLineRepo.GuardNoCycleAsync(conn, tx, request.UserId, request.ApproverId!, ct);

                    var newLine = new SharedKernel.Models.ReportingLine
                    {
                        ReportingLineId = Guid.NewGuid(),
                        EmployeeId = request.UserId,
                        ManagerId = request.ApproverId!,
                        OrganisationId = rlTreeRoot,
                        Relationship = "PRIMARY",
                        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                        EffectiveTo = null,
                        Source = "MANUAL",
                        Version = 1,
                        CreatedBy = actor.ActorId ?? "system",
                        CreatedAt = DateTime.UtcNow,
                    };
                    // First assignment for a brand-new user — expectedCurrentVersion = null.
                    var persistedLine = await reportingLineRepo.AssignAsync(conn, tx, null, newLine, ct);

                    await using (var rlAuditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO reporting_line_audit
                            (reporting_line_id, action, actor_id, correlation_id, version_before, version_after, metadata)
                        VALUES
                            (@lineId, 'ASSIGNED', @actorId, @correlationId, NULL, @versionAfter, NULL)
                        """, conn, tx))
                    {
                        rlAuditCmd.Parameters.AddWithValue("lineId", persistedLine.ReportingLineId);
                        rlAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                        rlAuditCmd.Parameters.AddWithValue("correlationId", (object?)actor.CorrelationId ?? DBNull.Value);
                        rlAuditCmd.Parameters.AddWithValue("versionAfter", (object)persistedLine.Version);
                        await rlAuditCmd.ExecuteNonQueryAsync(ct);
                    }

                    await outbox.EnqueueAsync(conn, tx, $"reporting-line-{request.UserId}", new ReportingLineAssigned
                    {
                        ReportingLineId = persistedLine.ReportingLineId,
                        EmployeeId = persistedLine.EmployeeId,
                        ManagerId = persistedLine.ManagerId,
                        OrganisationId = persistedLine.OrganisationId,
                        Relationship = persistedLine.Relationship,
                        EffectiveFrom = persistedLine.EffectiveFrom,
                        Source = persistedLine.Source,
                        RowVersion = persistedLine.Version,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    }, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch (CrossOrganisationAssignmentException ex)
            {
                // S74 R9 — the supplied approver is in a different Organisation → 400, nothing
                // committed (the whole create+assign rolls back atomically).
                await tx.RollbackAsync(ct);
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
            catch (ReportingCycleException ex)
            {
                // S74 R9 — the supplied approver is the new person (self-cycle) → 409, nothing
                // committed.
                await tx.RollbackAsync(ct);
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
            catch (ConcurrentSeedConflictException ex)
            {
                // S35 / TASK-3502 — Case A concurrent-create race on
                // user_agreement_codes (partial-unique-index 23505). Two concurrent
                // admin POSTs for the same user_id can both route through
                // UserAgreementCodeRepository.SupersedeAndCreateAsync Case A (no
                // predecessor) and collide on idx_user_agreement_codes_live; the
                // loser surfaces here. Map to 409 Conflict per RFC 7232 §4.1 —
                // symmetric to OptimisticConcurrencyException → 412 elsewhere.
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "Admin POST /api/admin/users lost a concurrent-create race on user_agreement_codes for user_id='{UserId}' (SqlState 23505); returning 409 Conflict",
                    ex.UserId);
                return Results.Conflict(new
                {
                    error = "User agreement-code assignment exists due to a concurrent-create race; refresh and retry.",
                    userId = ex.UserId,
                    hint = "retry — concurrent seed/create raced"
                });
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                // S35 Step 7a close — in-flight defect absorption. The pre-flight
                // existence check at L382-390 runs OUTSIDE the tx, so two concurrent
                // admin POSTs for the same user_id can both pass it before either
                // commits. The users INSERT then races at users_pkey (or
                // users_username_key) BEFORE the user_agreement_codes INSERT has
                // a chance to fire — so the TASK-3502 ConcurrentSeedConflictException
                // catch above never runs on this path. Map the typed PostgresException
                // (users_pkey 23505 or users_username_key 23505) to 409 Conflict for
                // symmetry with the pre-flight check at L389 and the
                // ConcurrentSeedConflictException path above; ConstraintName
                // disambiguates the two races in the log line.
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "Admin POST /api/admin/users lost a concurrent-create race on users for user_id='{UserId}' (constraint={ConstraintName}, SqlState 23505); returning 409 Conflict",
                    request.UserId, pgEx.ConstraintName);
                return Results.Conflict(new
                {
                    error = "User with this ID or username already exists due to a concurrent-create race; refresh and retry.",
                    userId = request.UserId,
                    hint = "retry — concurrent users-row create raced"
                });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // S35 / TASK-3506 — stamp ETag: "1" on the 201 response so the admin
            // UI's subsequent PUT can compose a coherent If-Match without a
            // round-trip through the new GET. version=1 is the schema DEFAULT from
            // TASK-3501. Precedents: S25 AgreementConfigEndpoints L78/104/143 +
            // PositionOverrideEndpoints L64.
            context.Response.Headers.ETag = "\"1\"";
            return Results.Created($"/api/admin/users/{request.UserId}", new
            {
                userId = request.UserId,
                username = request.Username,
                displayName = request.DisplayName,
                email = request.Email,
                primaryOrgId = request.PrimaryOrgId,
                agreementCode = request.AgreementCode,
                okVersion = request.OkVersion,
                version = 1L,
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR

        // 5. PUT /api/admin/users/{userId} — Update user
        //
        // S34 / TASK-3407 (ADR-023 D2 option (b)) — extends the S33 / TASK-3309
        // UserAgreementCodeChanged emission with full versioned-history routing
        // when agreement_code mutates. The DTO grows a required
        // EffectiveFrom: DateOnly validated as today (UTC); on mutation the
        // handler routes through UserAgreementCodeRepository.SupersedeAndCreateAsync
        // (Case B same-day in-place vs Case C cross-day supersession against the
        // seeder-backfilled '0001-01-01' predecessor) and emits:
        //   • UserAgreementCodeChanged — ALWAYS when mutated (preserved S33
        //     narrow-signal precedent for Phase 4e replay-data trail).
        //   • UserAgreementCodeSuperseded — ADDITIONALLY on Case C (dual emission
        //     per S25 publish-supersession precedent).
        // The audit row's action discriminates Updated vs Superseded vs Created.
        // The users.agreement_code denormalized cache UPDATE rides the same
        // atomic tx per the UserAgreementCodeRepository canonical-write contract.
        // No-agreement_code-mutation path is UNCHANGED from S33 — just users
        // UPDATE + UserUpdated outbox in one tx.
        app.MapPut("/api/admin/users/{userId}", async (
            string userId,
            UpdateUserRequest request,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            IOutboxEnqueue outbox,
            DbConnectionFactory dbFactory,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ReportingLineRepository reportingLineRepo,
            EnhedRepository enhedRepo,
            IAuditProjectionMapper<UserUpdated> auditMapper,
            IAuditProjectionMapper<UserAgreementCodeChanged> uacChangedMapper,
            IAuditProjectionMapper<UserAgreementCodeSuperseded> uacSupersededMapper,
            IAuditProjectionMapper<UserEnhederChanged> enhederChangedMapper,
            AuditProjectionRepository auditRepo,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R9 (R3) — bounded drift-retry wrapper. When this PUT is a cross-styrelse TRANSFER (it
        // changes primary_org_id) it acquires the reporting-tree advisory for BOTH the OLD + NEW roots
        // via the drift-guarded acquire BEFORE the users-row FOR UPDATE; if a concurrent transfer of the
        // same user drifts the OLD root under the advisory, the attempt rolls back (no side effects — the
        // drift check precedes the FOR UPDATE + the UPDATE) and retries on a fresh tx.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();
            var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.AdminEndpoints.UsersPut");

            // Look up existing user
            var existingUser = await userRepo.GetByIdAsync(userId, ct);
            if (existingUser is null)
                return Results.NotFound(new { error = "User not found" });

            // S35 / TASK-3506 — admin-strict If-Match parse per ADR-019 D2 (4th
            // application after S25 agreement_configs / position_override_configs /
            // wage_type_mappings; closest sibling precedent at
            // EmployeeProfileEndpoints.cs:206-208). Rejects missing / malformed /
            // If-None-Match: * with 428. Parsed BEFORE the scope check is
            // intentional ordering — header validation has no side effects and
            // mirrors the EmployeeProfileEndpoints PUT shape; the FOR-UPDATE re-read
            // + version comparison happens INSIDE the tx below so the locked-row
            // snapshot is canonical (closes audit-trail race where pre-tx
            // existingUser becomes stale by commit time).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // Validate actor scope covers user's current org. S91 TASK-9102: tree-page surface opened
            // to LocalHR — HROrAbove policy → LocalHR floor. Org-scope containment unchanged.
            var (allowedCurrent, reasonCurrent) = await scopeValidator.ValidateOrgAccessAsync(actor, existingUser.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowedCurrent)
                return Results.Json(new { error = "Access denied", reason = reasonCurrent }, statusCode: 403);

            // If org is changing, validate actor scope covers new org too (same LocalHR floor). A
            // transfer still requires the actor to cover BOTH the old AND the new org — an HR actor
            // cannot move a user into a styrelse it does not cover (containment preserved on transfer).
            if (request.PrimaryOrgId is not null && request.PrimaryOrgId != existingUser.PrimaryOrgId)
            {
                var (allowedNew, reasonNew) = await scopeValidator.ValidateOrgAccessAsync(actor, request.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
                if (!allowedNew)
                    return Results.Json(new { error = "Access denied", reason = reasonNew }, statusCode: 403);

                // S95 / ADR-035 slice 4 — ORGANISATION-HOME GUARD (transfer). The new home must be an
                // ORGANISATION (employees live on Organisations, not MAOs). Reject a transfer to a
                // non-ORGANISATION primary_org_id (mirrors the create guard + the S93 grant-MAO-reject).
                var newHomeOrg = await orgRepo.GetByIdAsync(request.PrimaryOrgId, ct);
                if (newHomeOrg is null)
                    return Results.BadRequest(new { error = "Primary org not found." });
                if (!string.Equals(newHomeOrg.OrgType, "ORGANISATION", StringComparison.Ordinal))
                    return Results.BadRequest(new { error = "A user's primary org must be an Organisation (a MAO holds no employees)." });
            }

            // S34 / TASK-3407 — agreement_code mutation predicate (null-safe + Ordinal
            // compare per S33 TASK-3309 precedent — codes are identifiers, not
            // culture-sensitive text). When false, the agreement_code routing branch
            // below is skipped entirely and behaviour matches S33 verbatim.
            var agreementCodeMutated = request.AgreementCode is not null &&
                !string.Equals(request.AgreementCode, existingUser.AgreementCode, StringComparison.Ordinal);

            // S34 / TASK-3407 — EffectiveFrom validator (ADR-023 D8 same-day-only-edit
            // narrowing). Only gated on the agreement_code mutation path: when the
            // admin is not editing agreement_code (e.g. just updating display_name or
            // email), EffectiveFrom is irrelevant and skipping the validator preserves
            // the no-mutation path's S33 behaviour unchanged. DateTime.UtcNow (not
            // local time) aligns with the frontend's
            // `new Date().toISOString().slice(0,10)` UTC extraction (TASK-3409 sync).
            // Rejects both backdated AND future-dated values with 422.
            if (agreementCodeMutated)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                if (request.EffectiveFrom != today)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = "EffectiveFrom must equal today (UTC).",
                        provided = request.EffectiveFrom,
                        expected = today,
                    });
                }
            }

            // Atomic UPDATE + outbox-emit per ADR-018 D3 (S26 TASK-2605b):
            // inline users UPDATE and UserUpdated outbox enqueue ride a single
            // explicit transaction; commit at end of try, rollback on throw.
            //
            // S34 / TASK-3407 extends the tx with — when agreement_code mutated —
            // user_agreement_codes routing via SupersedeAndCreateAsync + audit row +
            // UserAgreementCodeChanged + (on Case C) UserAgreementCodeSuperseded all
            // in the SAME atomic tx (ADR-018 D3 atomic-outbox contract). The
            // users.agreement_code denormalized cache UPDATE is part of the same tx
            // per the canonical-write contract on UserAgreementCodeRepository.
            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            // S78 R5 — EXPLICIT ReadCommitted (do not rely on the Npgsql driver default). REPEATABLE
            // READ would pin the snapshot BEFORE the advisory and silently defeat the tree lock (the
            // S74-7403 lesson): the post-acquire OLD-root re-derivation must see a transfer that
            // committed during the advisory wait, which only ReadCommitted provides.
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // S35 Step 7a cycle 1 absorption (Reviewer W2) — hoist the four
            // post-update field values out of the try block so the 200 response
            // body sources them from the FOR-UPDATE'd lockedUser snapshot
            // (assigned below inside the try) rather than the pre-tx existingUser
            // snapshot. Mirrors the EmployeeProfileEndpoints PUT precedent which
            // builds the response from canonical post-update state. The seed
            // values from existingUser are dead in any 200 path — every commit
            // overwrites them inside the try before the response builder runs;
            // they exist only so the compiler can prove definite assignment.
            // 412/409/throw all return early before the response builder.
            string newDisplayName = existingUser.DisplayName;
            string? newEmail = existingUser.Email;
            string newPrimaryOrgId = existingUser.PrimaryOrgId;
            string newAgreementCode = existingUser.AgreementCode;

            try
            {
                // S78 R3/R9 — CROSS-STYRELSE TRANSFER serialization. When this PUT moves primary_org_id
                // to a different org, acquire the reporting-tree advisory for BOTH the OLD (current) and
                // NEW (target) tree roots — id-ordered, deadlock-safe — BEFORE the users-row FOR UPDATE
                // below (advisory → rows order, matching the assign/remove paths so transfer-vs-assign
                // cannot invert). AcquireTreeLocksForTransferAsync re-derives the OLD root under the held
                // locks and throws TreeRootDriftException (→ the drift-retry wrapper rolls back + retries)
                // if a concurrent transfer of THIS user committed in the unlocked-read → advisory window.
                // This closes the S74-7403 stale-key residual from the TRANSFER side: an assign/remove in
                // either tree now blocks against the move. We use request.PrimaryOrgId vs the pre-tx
                // existingUser snapshot only to DECIDE whether to lock; the locked-row FOR UPDATE +
                // If-Match check below remains the authoritative version gate, and the in-tree
                // serialization holds regardless (a stale "no change" read just skips the lock for a
                // no-op org write).
                if (request.PrimaryOrgId is not null &&
                    !string.Equals(request.PrimaryOrgId, existingUser.PrimaryOrgId, StringComparison.Ordinal))
                {
                    await reportingLineRepo.AcquireTreeLocksForTransferAsync(conn, tx, userId, request.PrimaryOrgId, ct);
                }

                // S35 / TASK-3506 — in-tx FOR-UPDATE re-read of the users row.
                // Atomic row + version snapshot under a row-level lock prevents the
                // audit-trail race where the pre-tx existingUser snapshot at the top
                // of the handler is stale by commit time. Mirrors the
                // EmployeeProfileEndpoints PUT pattern: lockedUser is canonical for
                // (a) the If-Match precondition check below, (b) null-fallback
                // resolution on the UPDATE statement, and (c) the users_audit row's
                // previous_data JSONB. Closes item #4 stale-snapshot pattern noted
                // in S34 deferred — pre-S34 inherited outer-users-UPDATE used
                // pre-tx existingUser for null-coalescing fallbacks, which could
                // pick up stale data under concurrent admin edits.
                //
                // Lock order (S78): tree advisory (above, on transfer) → users (this
                // read) → user_agreement_codes (the agreementPredecessor read below, on
                // the mutation branch). users is the foreign-key parent in conventional
                // admin write flows; locking the parent first avoids deadlocks against the
                // UserAgreementCodeBackfillSeeder (UserAgreementCodeBackfillSeeder.cs:107-117)
                // which reads users plain (no lock) before INSERTing into
                // user_agreement_codes. The seeder cannot deadlock against this
                // handler because it never holds a users row lock.
                var lockedHit = await userRepo.GetByIdWithVersionAsync(conn, tx, userId, ct);
                if (lockedHit is null)
                    throw new OptimisticConcurrencyException(
                        $"User '{userId}' no longer exists (concurrent soft-delete?)",
                        expectedVersion: expectedVersion,
                        actualVersion: null);
                var (lockedUser, lockedVersion) = lockedHit.Value;

                if (lockedVersion != expectedVersion)
                    throw new OptimisticConcurrencyException(
                        $"User '{userId}' version is {lockedVersion}, but If-Match: \"{expectedVersion}\"; refresh and retry.",
                        expectedVersion: expectedVersion,
                        actualVersion: lockedVersion);

                // S34 / TASK-3407 — predecessor snapshot read in-tx (only when
                // agreement_code mutated). Captures the full row state for the
                // UserAgreementCodeSuperseded event payload (PredecessorAssignmentId,
                // PredecessorEffectiveFrom, OldAgreementCode, VersionBefore) on Case C
                // and the audit row's version_before on Case B/C. Mirrors the
                // EmployeeProfileEndpoints PUT pre-tx-read pattern (PredecessorSnapshot).
                //
                // S34 Step 7a cycle 1 absorption (Codex BLOCKER-1+2 / Reviewer WARNING-1
                // convergent): this SELECT uses FOR UPDATE so the row-level lock is held
                // from snapshot through to SupersedeAndCreateAsync's own AcquireLockAsync
                // (re-entrant within the same tx). Combined with passing
                // expectedVersion=predecessor.Version below, audit `previous_data` JSONB
                // + `version_before` column + the UserAgreementCodeSuperseded event payload
                // are guaranteed to reflect the same row state that SupersedeAndCreateAsync
                // operates on — no audit-trail drift under concurrent admin edits.
                // Skipped on the no-mutation path.
                AgreementPredecessorSnapshot? agreementPredecessor = null;
                if (agreementCodeMutated)
                {
                    await using var preCmd = new NpgsqlCommand(
                        """
                        SELECT assignment_id, agreement_code, effective_from, version
                        FROM user_agreement_codes
                        WHERE user_id = @userId AND effective_to IS NULL
                        FOR UPDATE
                        """, conn, tx);
                    preCmd.Parameters.AddWithValue("userId", userId);
                    await using var preReader = await preCmd.ExecuteReaderAsync(ct);
                    if (await preReader.ReadAsync(ct))
                    {
                        agreementPredecessor = new AgreementPredecessorSnapshot(
                            AssignmentId: preReader.GetGuid(0),
                            AgreementCode: preReader.GetString(1),
                            EffectiveFrom: preReader.GetFieldValue<DateOnly>(2),
                            Version: preReader.GetInt64(3));
                    }
                    // If null: no live row exists — backfill seeder didn't run for
                    // this user, or the user was created pre-S34 and somehow missed
                    // the seeder. SupersedeAndCreateAsync will route to Case A and
                    // INSERT a fresh row at v=1 since expectedVersion is null. The
                    // safety-net branch keeps the PUT path resilient against
                    // pre-S34 stragglers; admin POST always seeds the row explicitly.

                    // S34 Step 7a cycle 2 absorption (Codex BLOCKER-1) — re-validate
                    // agreementCodeMutated against the FOR-UPDATE'd canonical source.
                    // The pre-tx existingUser.AgreementCode snapshot at L610-611 can
                    // be stale under concurrent admin edits; once we hold the row
                    // lock, the predecessor's agreement_code is the authoritative
                    // "before" value. If a peer admin already applied the requested
                    // change while we waited on the lock, this PUT becomes a no-op
                    // on the agreement-code dimension — skip SupersedeAndCreateAsync
                    // + audit + Changed/Superseded emission to avoid (a) a spurious
                    // version bump, (b) emitting UserAgreementCodeChanged with stale
                    // OldAgreementCode that misreports the lineage as <pre-lock>→<new>
                    // when the actual transition is <new>→<new>. Case A safety-net
                    // (predecessor null) falls back to existingUser.AgreementCode
                    // since there is no canonical row to read from.
                    // S35 / TASK-3506 — fallback resolves against the locked users
                    // row (TASK-3506 Step 2) rather than the pre-tx existingUser
                    // snapshot, so the entire canonical path operates off locked
                    // state. agreementPredecessor still wins when its row exists
                    // (it's also FOR-UPDATE'd above); the safety-net fallback
                    // (predecessor null) reads from lockedUser, never the stale
                    // pre-tx snapshot.
                    var canonicalOldAgreementCode =
                        agreementPredecessor?.AgreementCode ?? lockedUser.AgreementCode;
                    if (string.Equals(request.AgreementCode, canonicalOldAgreementCode, StringComparison.Ordinal))
                    {
                        agreementCodeMutated = false;
                    }
                }

                // S35 / TASK-3506 — UPDATE bumps users.version per ADR-018 D7 row-
                // version contract. Null-fallbacks resolve against the locked row
                // (lockedUser), NOT the stale pre-tx existingUser — closes audit-
                // trail drift item #4 from S34 deferred. newVersion = lockedVersion + 1
                // is bound to a local for both the users_audit row below and the
                // ETag/version stamped on the 200 response. The four newX values
                // (declared outside the try block per Step 7a cycle 1 Reviewer W2
                // absorption) are assigned here so the response builder at L1230+
                // can source them from the locked-row snapshot.
                var newVersion = lockedVersion + 1;
                newDisplayName = request.DisplayName ?? lockedUser.DisplayName;
                newEmail = request.Email ?? lockedUser.Email;
                newPrimaryOrgId = request.PrimaryOrgId ?? lockedUser.PrimaryOrgId;
                newAgreementCode = request.AgreementCode ?? lockedUser.AgreementCode;

                // S52 / ADR-027 deferred — detect user deactivation (was active,
                // request explicitly sets is_active = false). lockedUser.IsActive is
                // always true here (the FOR-UPDATE read above filtered is_active = TRUE).
                var newIsActive = request.IsActive ?? lockedUser.IsActive;
                var isDeactivating = lockedUser.IsActive && !newIsActive;

                await using var cmd = new NpgsqlCommand(
                    """
                    UPDATE users
                    SET display_name = @displayName,
                        email = @email,
                        primary_org_id = @primaryOrgId,
                        agreement_code = @agreementCode,
                        is_active = @isActive,
                        version = version + 1,
                        updated_at = @now
                    WHERE user_id = @userId AND is_active = TRUE
                    """, conn, tx);
                cmd.Parameters.AddWithValue("displayName", newDisplayName);
                cmd.Parameters.AddWithValue("email", (object?)newEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("primaryOrgId", newPrimaryOrgId);
                cmd.Parameters.AddWithValue("agreementCode", newAgreementCode);
                cmd.Parameters.AddWithValue("isActive", newIsActive);
                cmd.Parameters.AddWithValue("now", now);
                cmd.Parameters.AddWithValue("userId", userId);
                await cmd.ExecuteNonQueryAsync(ct);

                // S35 / TASK-3506 — users_audit UPDATED row in-tx (ADR-018 D3
                // atomic-outbox + ADR-019 D8 version-transition columns). Mirrors
                // the employee_profile_audit shape at EmployeeProfileEndpoints.cs
                // L347-371 + the user_agreement_codes_audit shape at L858-884 below.
                // previous_data JSONB = locked row state (pre-UPDATE);
                // new_data JSONB = post-UPDATE state; version_before = lockedVersion
                // = predecessor; version_after = newVersion = lockedVersion + 1.
                // password_hash deliberately EXCLUDED — audit JSONB must never
                // carry credentials.
                var previousUserData = JsonSerializer.Serialize(new
                {
                    displayName = lockedUser.DisplayName,
                    email = lockedUser.Email,
                    primaryOrgId = lockedUser.PrimaryOrgId,
                    agreementCode = lockedUser.AgreementCode,
                });
                var newUserData = JsonSerializer.Serialize(new
                {
                    displayName = newDisplayName,
                    email = newEmail,
                    primaryOrgId = newPrimaryOrgId,
                    agreementCode = newAgreementCode,
                });
                await using (var userAuditCmd = new NpgsqlCommand(
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
                    userAuditCmd.Parameters.AddWithValue("userId", userId);
                    userAuditCmd.Parameters.AddWithValue("previousData", previousUserData);
                    userAuditCmd.Parameters.AddWithValue("newData", newUserData);
                    userAuditCmd.Parameters.AddWithValue("versionBefore", lockedVersion);
                    userAuditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    userAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "unknown");
                    userAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                    await userAuditCmd.ExecuteNonQueryAsync(ct);
                }

                // Emit domain event in-tx (BEFORE CommitAsync) so the users row
                // and the outbox row commit atomically per ADR-018 D3. UserUpdated
                // fires on EVERY PUT regardless of agreement_code mutation
                // (preserved S31 / S33 contract).
                var @event = new UserUpdated
                {
                    UserId = userId,
                    DisplayName = newDisplayName,
                    Email = newEmail,
                    PrimaryOrgId = newPrimaryOrgId,
                    AgreementCode = newAgreementCode,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId
                };
                // S44 TASK-4413: UserUpdated enqueue converted to capture outbox_id +
                // audit_projection insert (ADR-026 D2 sync-in-tx). UserAgreementCodeChanged +
                // UserAgreementCodeSuperseded enqueues below converted in S44b TASK-4413b.
                // ResolvedTargetOrgId fallback to newPrimaryOrgId — PUT may carry null
                // PrimaryOrgId (no change requested); mapper requires SOME non-null value.
                var userUpdatedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", @event, ct);
                var userUpdatedAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: newPrimaryOrgId);
                var userUpdatedAuditRow = auditMapper.Map(@event, userUpdatedAuditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, userUpdatedOutboxId, @event.EventType, userUpdatedAuditRow, userUpdatedAuditCtx, ct);

                // S34 / TASK-3407 (ADR-023 D2 option (b)) — agreement_code routing.
                // Extends the S33 / TASK-3309 narrow-signal UserAgreementCodeChanged
                // emission with full versioned-history routing through
                // SupersedeAndCreateAsync. The repo's ADR-020 D2 3-case routing
                // selects:
                //   • Case B (Updated)    — live row's effective_from == today (UTC);
                //                           UPDATE-in-place with version bump.
                //   • Case C (Superseded) — live row's effective_from < today (e.g.
                //                           seeder-backfilled '0001-01-01' or earlier
                //                           admin edit); close predecessor + INSERT
                //                           new live row at predecessor.Version + 1.
                //   • Case A (Created)    — no live row exists (pre-S34 straggler);
                //                           INSERT fresh row at v=1.
                // Audit row action mirrors the outcome (UPDATED / SUPERSEDED /
                // CREATED). UserAgreementCodeChanged emits ALWAYS when mutated
                // (preserved S33 contract); UserAgreementCodeSuperseded emits
                // ADDITIONALLY on Case C (dual emission per S25 publish-supersession
                // precedent — Phase 4e replay consumers see the "agreement code
                // changed" signal AND the cross-day supersession lifecycle event).
                if (agreementCodeMutated)
                {
                    // S34 Step 7a cycle 1 absorption — pass
                    // expectedVersion=predecessor.Version (defense-in-depth on top of the
                    // FOR UPDATE lock above). If the lock somehow released between the
                    // predecessor read and this call, the repository's optimistic
                    // concurrency check at UserAgreementCodeRepository.cs:266-273 would
                    // throw OptimisticConcurrencyException → 412 rather than silently
                    // proceeding with stale audit metadata.
                    var agreementResult = await userAgreementCodeRepo.SupersedeAndCreateAsync(
                        conn, tx,
                        new UserAgreementCodeSupersedeRequest(
                            UserId: userId,
                            AgreementCode: request.AgreementCode!,
                            EffectiveFrom: request.EffectiveFrom),
                        expectedVersion: agreementPredecessor?.Version,
                        ct);

                    // Audit row — action + version-transition columns discriminated
                    // by outcome:
                    //   Case B Updated:    action=UPDATED,    version_before=predecessor.Version, version_after=result.Version
                    //   Case C Superseded: action=SUPERSEDED, version_before=predecessor.Version, version_after=result.Version
                    //   Case A Created:    action=CREATED,    version_before=NULL,                version_after=result.Version
                    // version_before is the predecessor's row-version (NOT NULL on
                    // B/C because we snapshotted it above; NULL on A because there
                    // is no predecessor). Mirrors EmployeeProfileEndpoints PUT
                    // precedent (L366-367) — the audit chain narrates the visible
                    // state delta on the user's agreement-code lineage.
                    string auditAction;
                    long? auditVersionBefore;
                    switch (agreementResult.Outcome)
                    {
                        case SaveUserAgreementCodeOutcome.Updated:
                            auditAction = "UPDATED";
                            auditVersionBefore = agreementPredecessor!.Version;
                            break;
                        case SaveUserAgreementCodeOutcome.Superseded:
                            auditAction = "SUPERSEDED";
                            auditVersionBefore = agreementPredecessor!.Version;
                            break;
                        case SaveUserAgreementCodeOutcome.Created:
                            auditAction = "CREATED";
                            auditVersionBefore = null;
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Unhandled SaveUserAgreementCodeOutcome value '{agreementResult.Outcome}'.");
                    }
                    var previousData = agreementPredecessor is null
                        ? null
                        : JsonSerializer.Serialize(new
                        {
                            userId,
                            agreementCode = agreementPredecessor.AgreementCode,
                            effectiveFrom = agreementPredecessor.EffectiveFrom.ToString("yyyy-MM-dd"),
                        });
                    var newData = JsonSerializer.Serialize(new
                    {
                        userId,
                        agreementCode = request.AgreementCode,
                        effectiveFrom = request.EffectiveFrom.ToString("yyyy-MM-dd"),
                    });
                    await using (var agreementAuditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO user_agreement_codes_audit (
                            assignment_id, user_id, action,
                            previous_data, new_data,
                            version_before, version_after,
                            actor_id, actor_role)
                        VALUES (
                            @assignmentId, @userId, @action,
                            @previousData::jsonb, @newData::jsonb,
                            @versionBefore, @versionAfter,
                            @actorId, @actorRole)
                        """, conn, tx))
                    {
                        agreementAuditCmd.Parameters.AddWithValue("assignmentId", agreementResult.AssignmentId);
                        agreementAuditCmd.Parameters.AddWithValue("userId", userId);
                        agreementAuditCmd.Parameters.AddWithValue("action", auditAction);
                        agreementAuditCmd.Parameters.AddWithValue("previousData",
                            previousData is null ? (object)DBNull.Value : previousData);
                        agreementAuditCmd.Parameters.AddWithValue("newData", newData);
                        agreementAuditCmd.Parameters.AddWithValue("versionBefore",
                            auditVersionBefore is null ? (object)DBNull.Value : auditVersionBefore.Value);
                        agreementAuditCmd.Parameters.AddWithValue("versionAfter", agreementResult.Version);
                        agreementAuditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "unknown");
                        agreementAuditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                        await agreementAuditCmd.ExecuteNonQueryAsync(ct);
                    }

                    // S33 / TASK-3309 (preserved) — UserAgreementCodeChanged narrow
                    // signal emits ALWAYS when agreement_code mutated, regardless of
                    // outcome (Case A / B / C). Phase 4e replay-data trail consumers
                    // pattern-match on this event type without parsing every
                    // UserUpdated event's old-vs-new diff.
                    //
                    // S34 Step 7a cycle 2 absorption (Codex BLOCKER-1) — OldAgreementCode
                    // is sourced from the FOR-UPDATE'd predecessor row (canonical "before"
                    // value), not from the pre-tx existingUser snapshot. Mirrors the
                    // identical fix on the audit `previous_data` JSONB above + the
                    // UserAgreementCodeSuperseded event payload below — all three sites
                    // now flow off the same locked row state.
                    // S35 Step 7a cycle 1 absorption (Reviewer W1) — Case A safety-net
                    // (no live row exists) now falls back to lockedUser.AgreementCode,
                    // never the pre-tx existingUser snapshot. Closes the remaining
                    // outer-users-UPDATE stale-snapshot residual on item #4 from S34
                    // deferred. Mirrors the canonicalOldAgreementCode fallback at L882
                    // exactly — both fallback sites now flow off the FOR-UPDATE'd
                    // users row.
                    var agreementChangedEvent = new UserAgreementCodeChanged
                    {
                        UserId = userId,
                        OldAgreementCode = agreementPredecessor?.AgreementCode ?? lockedUser.AgreementCode,
                        NewAgreementCode = request.AgreementCode!,
                        EffectiveFrom = request.EffectiveFrom,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId
                    };
                    // S44b TASK-4413b: UserAgreementCodeChanged cutover to
                    // EnqueueAndReturnIdAsync + audit_projection insert (ADR-026 D2
                    // sync-in-tx). TENANT_TARGETED — ResolvedTargetOrgId from
                    // newPrimaryOrgId (the user's effective PrimaryOrgId after this PUT).
                    var uacChangedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", agreementChangedEvent, ct);
                    var uacChangedCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(agreementChangedEvent.OccurredAt),
                        ResolvedTargetOrgId: newPrimaryOrgId);
                    var uacChangedRow = uacChangedMapper.Map(agreementChangedEvent, uacChangedCtx);
                    await auditRepo.InsertAsync(conn, tx, agreementChangedEvent.EventId, uacChangedOutboxId, agreementChangedEvent.EventType, uacChangedRow, uacChangedCtx, ct);

                    // S34 / TASK-3407 (NEW) — UserAgreementCodeSuperseded emits
                    // ADDITIONALLY on Case C (cross-day supersession). Dual emission
                    // with UserAgreementCodeChanged per the S25 publish-supersession
                    // precedent — Phase 4e replay consumers reconstruct the
                    // predecessor close + successor insert lifecycle without
                    // inferring it from (effective_from, effective_to) deltas. Same
                    // canonical user-{userId} stream per TASK-3309 + backfill seeder
                    // convention. Under end-exclusive semantics (ADR-018 D9), the
                    // predecessor's effective_to == new row's effective_from.
                    if (agreementResult.Outcome == SaveUserAgreementCodeOutcome.Superseded)
                    {
                        var supersededEvent = new UserAgreementCodeSuperseded
                        {
                            PredecessorAssignmentId = agreementPredecessor!.AssignmentId,
                            NewAssignmentId = agreementResult.AssignmentId,
                            UserId = userId,
                            PredecessorEffectiveFrom = agreementPredecessor.EffectiveFrom,
                            PredecessorEffectiveTo = request.EffectiveFrom,
                            NewEffectiveFrom = request.EffectiveFrom,
                            OldAgreementCode = agreementPredecessor.AgreementCode,
                            NewAgreementCode = request.AgreementCode!,
                            VersionBefore = agreementPredecessor.Version,
                            VersionAfter = agreementResult.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        // S44b TASK-4413b: UserAgreementCodeSuperseded cutover to
                        // EnqueueAndReturnIdAsync + audit_projection insert (ADR-026
                        // D2 sync-in-tx). TENANT_TARGETED — same ResolvedTargetOrgId
                        // as UserAgreementCodeChanged above (newPrimaryOrgId). Only
                        // emitted on Case C (cross-day supersession).
                        var uacSupersededOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", supersededEvent, ct);
                        var uacSupersededCtx = new AuditProjectionContext(
                            ActorId: actor.ActorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: actor.CorrelationId,
                            OccurredAt: new DateTimeOffset(supersededEvent.OccurredAt),
                            ResolvedTargetOrgId: newPrimaryOrgId);
                        var uacSupersededRow = uacSupersededMapper.Map(supersededEvent, uacSupersededCtx);
                        await auditRepo.InsertAsync(conn, tx, supersededEvent.EventId, uacSupersededOutboxId, supersededEvent.EventType, uacSupersededRow, uacSupersededCtx, ct);
                    }
                }

                // S52 / ADR-027 deferred — when deactivating a user who is a manager,
                // emit ReportingLineManagerDeactivated events for each active reporting
                // line where they are the manager. The read + enqueue happen inside the
                // same atomic tx so the events are consistent with the user's new
                // is_active = false state.
                if (isDeactivating)
                {
                    var managedLines = await reportingLineRepo.GetDirectReportsAsync(conn, tx, userId, ct);
                    foreach (var line in managedLines)
                    {
                        var deactivatedEvent = new ReportingLineManagerDeactivated
                        {
                            ReportingLineId = line.ReportingLineId,
                            EmployeeId = line.EmployeeId,
                            ManagerId = line.ManagerId,
                            OrganisationId = line.OrganisationId,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, $"reporting-line-{line.EmployeeId}", deactivatedEvent, ct);
                    }
                }

                // S97 / ADR-035 (BLOCKER B) — TRANSFER-CLEARS-TAGS. An Enhed tag never crosses
                // Organisations (the same-Organisation invariant). When this PUT moves the user to
                // a different Organisation, CLEAR the user's enhed tags IN THIS SAME TX (Enhed is
                // throwaway display metadata — CLEAR, not block). GATED on the EXISTING org-change
                // predicate (reused verbatim from the transfer-lock decision at L1083-1084) so a
                // NON-transfer PUT (display_name/email/agreement-only edit) does NOT touch
                // user_enheder. DELETE the rows + enqueue UserEnhederChanged(empty) + the
                // audit-projection row, atomically with the UserUpdated above (mirrors the
                // L1304-1312 outbox+audit pattern). ResolvedTargetOrgId = newPrimaryOrgId (the
                // POST-transfer org).
                if (request.PrimaryOrgId is not null &&
                    !string.Equals(request.PrimaryOrgId, existingUser.PrimaryOrgId, StringComparison.Ordinal))
                {
                    var enhederClearedEvent = new UserEnhederChanged
                    {
                        UserId = userId,
                        EnhedIds = Array.Empty<Guid>(),
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await enhedRepo.ApplyUserEnhederChangedAsync(conn, tx, enhederClearedEvent, ct);

                    var enhederClearedOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", enhederClearedEvent, ct);
                    var enhederClearedCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(enhederClearedEvent.OccurredAt),
                        ResolvedTargetOrgId: newPrimaryOrgId);
                    var enhederClearedRow = enhederChangedMapper.Map(enhederClearedEvent, enhederClearedCtx);
                    await auditRepo.InsertAsync(conn, tx, enhederClearedEvent.EventId, enhederClearedOutboxId, enhederClearedEvent.EventType, enhederClearedRow, enhederClearedCtx, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch (OptimisticConcurrencyException ex)
            {
                // S35 / TASK-3506 — explicit If-Match enforcement (admin-strict per
                // ADR-019 D2). S34 cycle-1 absorption added expectedVersion threading
                // through SupersedeAndCreateAsync but never the endpoint mapping;
                // this completes the contract — 412 Precondition Failed with
                // structured expectedVersion/actualVersion body, rather than the
                // generic 500 the older catch-all would produce. Closes the latent
                // finding from TASK-3502. Mirrors EmployeeProfileEndpoints PUT
                // L282-291 precedent — explicit `await tx.RollbackAsync(ct)` BEFORE
                // returning 412, per S35 cycle-1 absorption (don't rely on
                // disposal-time rollback; users_audit + outbox emits may have
                // happened on the agreement-code branch and disciplined rollback is
                // the canonical pattern).
                await tx.RollbackAsync(ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                }, statusCode: 412);
            }
            catch (ConcurrentSeedConflictException ex)
            {
                // S35 / TASK-3502 — Case A concurrent-create race on
                // user_agreement_codes (partial-unique-index 23505). Reachable on
                // the PUT path only via the pre-S34 straggler safety-net
                // (agreementPredecessor null → SupersedeAndCreateAsync routes Case A
                // with expectedVersion=null per L786); two concurrent PUTs both
                // observing "no live row" can collide on idx_user_agreement_codes_live.
                // Map to 409 Conflict — symmetric to OptimisticConcurrencyException
                // → 412 (version-mismatch path on Cases B/C).
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "Admin PUT /api/admin/users/{UserId} lost a concurrent-create race on user_agreement_codes (SqlState 23505); returning 409 Conflict",
                    ex.UserId);
                return Results.Conflict(new
                {
                    error = "User agreement-code assignment exists due to a concurrent-create race; refresh and retry.",
                    userId = ex.UserId,
                    hint = "retry — concurrent seed/create raced"
                });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // S35 / TASK-3506 — stamp ETag: "<newVersion>" + carry `version` in
            // the body so the admin UI can compose the next If-Match without
            // re-GETting. ADR-019 D2 explicit ETag contract. newVersion is
            // mechanically expectedVersion + 1: the If-Match precondition has
            // already been validated against lockedVersion inside the tx (412
            // would otherwise have short-circuited via the explicit OCE catch),
            // and the UPDATE statement bumps version by exactly 1.
            //
            // S35 Step 7a cycle 1 absorption (Reviewer W2) — body fields are
            // sourced from the four newX locals (hoisted above the try block,
            // assigned inside the try from `request.X ?? lockedUser.X`), NOT
            // from `request.X ?? existingUser.X`. This makes the response
            // unambiguously consistent with the UPDATE statement, the
            // users_audit `new_data` JSONB, and the UserUpdated event payload
            // — all four sites now flow off the same FOR-UPDATE'd lockedUser
            // snapshot rather than relying on the global If-Match invariant
            // to make existingUser converge with lockedUser. Mirrors the
            // EmployeeProfileEndpoints PUT precedent (builds response inside tx).
            var newUserVersion = expectedVersion + 1;
            context.Response.Headers.ETag = $"\"{newUserVersion}\"";
            return Results.Ok(new
            {
                userId,
                displayName = newDisplayName,
                email = newEmail,
                primaryOrgId = newPrimaryOrgId,
                agreementCode = newAgreementCode,
                version = newUserVersion,
            });
        })).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page surface opened to LocalHR. S78 R9: extra ) closes TreeRootDriftRetry.RunAsync

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

            // Validate actor scope covers user's org. S76 B1: HROrAbove policy → LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, targetUser.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
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
            OrganizationRepository orgRepo,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<RoleAssignmentGranted> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate scope type first — the shape gate below keys off ScopeType, not OrgId.
            // S93 / ADR-035 slice 2 (flat role-scope): ORG_AND_DESCENDANTS is dropped.
            if (request.ScopeType is not ("GLOBAL" or "ORG_ONLY"))
                return Results.BadRequest(new { error = "Invalid scopeType. Must be GLOBAL or ORG_ONLY" });

            // S85 / TASK-8501 (P7 privilege-escalation fix). Gate "global" on ScopeType, NOT
            // on `OrgId is null`. The pre-S85 guard keyed global-ness off OrgId, so a LocalAdmin
            // could grant {scopeType:'GLOBAL', orgId:'STY01'} — a non-null org let the
            // ValidateOrgAccessAsync branch admit it, yet RoleScope.CoversOrg treats ANY GLOBAL
            // scope as all-org access → effective global escalation. Now:
            //   (A) shape: GLOBAL ⟹ OrgId IS NULL + HasGlobalScope(actor); non-GLOBAL ⟹ OrgId
            //       non-null + the org-scope check.
            if (request.ScopeType == "GLOBAL")
            {
                if (request.OrgId is not null)
                    return Results.BadRequest(new { error = "A GLOBAL-scoped role assignment must not carry an orgId (org_id must be null)." });
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can grant global-scoped roles" }, statusCode: 403);
            }
            else
            {
                if (request.OrgId is null)
                    return Results.BadRequest(new { error = "A non-GLOBAL-scoped role assignment requires an orgId." });
                // Validate actor scope covers target org. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.OrgId, StatsTidRoles.LocalAdmin, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

                // S93 / ADR-035 slice 2 — OQ1 (Codex BLOCKER). An ORG_ONLY grant's org_id MUST
                // resolve to an ORGANISATION. A MAO is not an authority unit: a MAO-typed scope
                // confers org-STRUCTURE admin (it passes the create-under-MAO / MAO-update gates),
                // not inert roster reach. Reject it at grant.
                var targetOrg = await orgRepo.GetByIdAsync(request.OrgId, ct);
                if (targetOrg is null)
                    return Results.BadRequest(new { error = "Target org not found." });
                if (!string.Equals(targetOrg.OrgType, "ORGANISATION", StringComparison.Ordinal))
                    return Results.BadRequest(new { error = "A non-GLOBAL (ORG_ONLY) role assignment requires an Organisation org_id (a MAO is not a valid scope target)." });
            }

            // S85 / TASK-8501 (B) role↔scope coupling. AuthEndpoints.MapRoleIdToName maps an
            // inherently-global role_id (GLOBAL_ADMIN) → the JWT primary role (ActorRole), and the
            // GlobalAdminOnly policy checks the ROLE, not the scope. So a GLOBAL_ADMIN row with a
            // non-GLOBAL scope would still mint an effective GlobalAdmin on the holder's next login.
            // Require GLOBAL_ADMIN ⟹ ScopeType=='GLOBAL' + OrgId IS NULL + HasGlobalScope(actor).
            if (string.Equals(request.RoleId, "GLOBAL_ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                if (request.ScopeType != "GLOBAL" || request.OrgId is not null)
                    return Results.BadRequest(new { error = "GLOBAL_ADMIN must be granted with scopeType=GLOBAL and no orgId." });
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can grant the GLOBAL_ADMIN role" }, statusCode: 403);
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

                // Insert audit record. S85 / TASK-8501: aligned to the real role_assignment_audit
                // schema (init.sql:663) — audit_id is BIGSERIAL (auto), timestamp DEFAULT NOW()
                // (omit both); action 'GRANTED' (the CHECK vocabulary, not 'GRANT'); actor_id +
                // NOT-NULL actor_role columns (matching the users_audit idiom); details is JSONB
                // (a small object via ::jsonb, not a free-text string — the original 22P02 cause).
                var grantDetails = JsonSerializer.Serialize(new
                {
                    summary = $"Granted {request.RoleId} on {request.OrgId ?? "GLOBAL"} ({request.ScopeType}) to {request.UserId}",
                    roleId = request.RoleId,
                    orgId = request.OrgId,
                    scopeType = request.ScopeType,
                    userId = request.UserId,
                });
                await using var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignment_audit (assignment_id, action, actor_id, actor_role, details)
                    VALUES (@assignmentId, 'GRANTED', @actorId, @actorRole, @details::jsonb)
                    """, conn, tx);
                auditCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                auditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                auditCmd.Parameters.AddWithValue("details", grantDetails);
                await auditCmd.ExecuteNonQueryAsync(ct);

                // Emit domain event in-tx (BEFORE CommitAsync) so role_assignments
                // + role_assignment_audit + outbox commit atomically per ADR-018 D3
                // (S26 TASK-2605b narrower variant — existing tx already wraps state
                // + audit; only the emission moves inside).
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
                // S44 TASK-4413: RoleAssignmentGranted cutover. Mapper requires
                // ResolvedTargetOrgId = user's primary_org_id (NOT in event payload —
                // event carries the scope OrgId distinct from user's primary org per
                // catalog L59). targetUser fetched at L1410 carries PrimaryOrgId.
                var grantOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{request.UserId}", @event, ct);
                var grantAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: targetUser.PrimaryOrgId);
                var grantAuditRow = auditMapper.Map(@event, grantAuditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, grantOutboxId, @event.EventType, grantAuditRow, grantAuditCtx, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

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
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<RoleAssignmentRevoked> auditMapper,
            AuditProjectionRepository auditRepo,
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
            var assignmentScopeType = reader.GetString(reader.GetOrdinal("scope_type"));
            var isActive = reader.GetBoolean(reader.GetOrdinal("is_active"));
            await reader.CloseAsync();
            await lookupConn.CloseAsync();

            if (!isActive)
                return Results.BadRequest(new { error = "Role assignment is already revoked" });

            // Validate actor scope covers the assignment's org. S76 B1: LocalAdminOrAbove policy → LocalAdmin floor.
            // S85 / TASK-8501: gate global-ness on the stored scope_type, NOT on `org_id is null`
            // (the shape CHECK keeps them equivalent, but keying on scope_type matches the grant
            // guard and is robust to any legacy/divergent row).
            if (assignmentScopeType == "GLOBAL")
            {
                // Revoking a GLOBAL scope — only GlobalAdmin
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can revoke global-scoped roles" }, statusCode: 403);
            }
            else if (assignmentOrgId is not null)
            {
                var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, assignmentOrgId, StatsTidRoles.LocalAdmin, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }
            else
            {
                // Defensive: a non-GLOBAL scope with a null org_id is a malformed row (the shape
                // CHECK forbids it). Refuse rather than silently admit a de-privileging operation.
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Cannot determine org scope for this assignment" }, statusCode: 403);
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

                // Insert audit record. S85 / TASK-8501: aligned to the real role_assignment_audit
                // schema — action 'REVOKED' (CHECK vocabulary), actor_id + NOT-NULL actor_role,
                // details as JSONB (::jsonb), audit_id/timestamp auto. See the grant-path note.
                var revokeDetails = JsonSerializer.Serialize(new
                {
                    summary = $"Revoked {assignmentRoleId} from {assignmentUserId}"
                        + (request.Reason is not null ? $". Reason: {request.Reason}" : ""),
                    roleId = assignmentRoleId,
                    userId = assignmentUserId,
                    reason = request.Reason,
                });
                await using var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignment_audit (assignment_id, action, actor_id, actor_role, details)
                    VALUES (@assignmentId, 'REVOKED', @actorId, @actorRole, @details::jsonb)
                    """, conn, tx);
                auditCmd.Parameters.AddWithValue("assignmentId", request.AssignmentId);
                auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                auditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? "unknown");
                auditCmd.Parameters.AddWithValue("details", revokeDetails);
                await auditCmd.ExecuteNonQueryAsync(ct);

                // Emit domain event in-tx (BEFORE CommitAsync) so role_assignments
                // + role_assignment_audit + outbox commit atomically per ADR-018 D3
                // (S26 TASK-2605b narrower variant — existing tx already wraps state
                // + audit; only the emission moves inside).
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
                // S44 TASK-4413: RoleAssignmentRevoked cutover. Mapper requires
                // ResolvedTargetOrgId = the affected user's primary_org_id (catalog
                // L59). Look up the user record now to obtain it (the original
                // assignment lookup at L1532 only carried user_id, not org).
                var revokeOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{assignmentUserId}", @event, ct);
                var affectedUser = await userRepo.GetByIdAsync(conn, tx, assignmentUserId, ct);
                var revokeAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: affectedUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"RoleAssignmentRevoked: affected user {assignmentUserId} disappeared mid-revoke; cannot resolve primary_org_id for audit projection."));
                var revokeAuditRow = auditMapper.Map(@event, revokeAuditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, revokeOutboxId, @event.EventType, revokeAuditRow, revokeAuditCtx, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

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

        // ═══════════════════════════════════════════
        // S74-7404 R11a — GET /api/admin/reporting-lines/tree/{organisationId}/period-status
        //   Per-styrelse period-status projection for the redesigned Medarbejder-administration
        //   tree (FE Phases 2-3): each employee's last-closed-month status (OPEN/SUBMITTED/
        //   APPROVED) for the status badge + the per-manager pending count for the filter tiles.
        //   Read-only / additive. Scope: LocalAdminOrAbove + org-scope covers the tree root
        //   (mirrors the sibling GET .../tree/{organisationId}).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/reporting-lines/tree/{organisationId}/period-status", async (
            string organisationId,
            ApprovalPeriodRepository approvalRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Scope must cover the tree-root org (same gate as the tree read). S76 B1:
            // LocalAdminOrAbove policy → LocalAdmin floor.
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, organisationId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Resolve the tree-root org → its materialized_path prefix (the styrelse subtree the
            // projection is scoped over).
            var treeRootOrg = await orgRepo.GetByIdAsync(organisationId, ct);
            if (treeRootOrg is null)
                return Results.NotFound(new { error = $"Organization {organisationId} not found" });

            var projection = await approvalRepo.GetPeriodStatusProjectionForTreeAsync(
                treeRootOrg.MaterializedPath, ct);

            return Results.Ok(new
            {
                employees = projection.Employees.Select(e => new
                {
                    employeeId = e.EmployeeId,
                    displayName = e.DisplayName,
                    status = e.Status,
                }),
                pendingCountByManager = projection.PendingCountByManager,
            });
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // S75-7500 (R1-R3) — GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere
        //   The consolidated medarbejder-roster read backing the redesigned Medarbejder-
        //   administration STRUCTURAL tree (FE Phase 2, tasks 7501/7502 consume this contract).
        //   Per active styrelse user: { employeeId, displayName, enhedLabel (?? primaryOrgName),
        //   position, structuralApproverId (the RAW active PRIMARY edge — the TREE KEY, NO
        //   resolver), periodStatus (OPEN/SUBMITTED/APPROVED — the same last-closed-month rule as
        //   the period-status read), outgoingVikar (the person's OWN active manager_vikar row |
        //   null), isRoot, isOrphan (the R3 deterministic rule) } + the styrelse
        //   pendingCountByManager (the existing S74 gated tally, reused as-is). Read-only /
        //   additive. Scope: LocalAdminOrAbove + org-scope covers the tree root (mirrors the
        //   sibling GET .../tree/{organisationId} + .../period-status).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/reporting-lines/tree/{organisationId}/medarbejdere", async (
            string organisationId,
            ApprovalPeriodRepository approvalRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Scope must cover the tree-root org (same gate as the tree + period-status reads).
            // S91 TASK-9102: tree-page roster opened to LocalHR — HROrAbove policy → LocalHR floor.
            // Org-scope containment unchanged (HR sees only its own org subtree's roster).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, organisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Resolve the tree-root org → its materialized_path prefix (the styrelse subtree the
            // roster is scoped over).
            var treeRootOrg = await orgRepo.GetByIdAsync(organisationId, ct);
            if (treeRootOrg is null)
                return Results.NotFound(new { error = $"Organization {organisationId} not found" });

            var roster = await approvalRepo.GetMedarbejderRosterForTreeAsync(
                treeRootOrg.MaterializedPath, ct);

            return Results.Ok(new
            {
                employees = roster.Employees.Select(e => new
                {
                    employeeId = e.EmployeeId,
                    displayName = e.DisplayName,
                    enhedLabel = e.EnhedLabel,
                    position = e.Position,
                    structuralApproverId = e.StructuralApproverId,
                    periodStatus = e.PeriodStatus,
                    outgoingVikar = e.OutgoingVikar is null ? null : new
                    {
                        vikarUserId = e.OutgoingVikar.VikarUserId,
                        vikarDisplayName = e.OutgoingVikar.VikarDisplayName,
                        untilDate = e.OutgoingVikar.UntilDate,
                        reason = e.OutgoingVikar.Reason,
                    },
                    isRoot = e.IsRoot,
                    isOrphan = e.IsOrphan,
                }),
                pendingCountByManager = roster.PendingCountByManager,
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page roster opened to LocalHR

        // ═══════════════════════════════════════════
        // S74-7404 R11b — GET /api/admin/users/search — server-side person-search (the 2000+
        //   approver/person picker). Case-insensitive q on display_name/username; scope-filtered
        //   to the caller's RBAC org-scope; paginated (limit/offset, sane default+cap); excludes
        //   self + descendants server-side when excludeEmployeeId is supplied (the cycle-prevention
        //   mirror for the picker — a person cannot pick themselves or a subordinate as approver).
        //   Read-only. minRole LocalAdmin (admin surface). A REAL paginated DB query.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/users/search", async (
            string? q,
            string? excludeEmployeeId,
            string? enhedId,
            int? limit,
            int? offset,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Pagination: default 50, cap 200; offset >= 0.
            var pageLimit = Math.Clamp(limit ?? 50, 1, 200);
            var pageOffset = Math.Max(offset ?? 0, 0);

            // Scope filter: the accessible-org set (null = GLOBAL/unrestricted, [] = nobody).
            // S91 TASK-9102: tree-page picker opened to LocalHR — HROrAbove policy → LocalHR floor —
            // a mixed-role actor's below-HR scope covering org B must NOT widen the picker into B's
            // roster. Org-scope containment unchanged (the picker stays bounded to HR's own subtree).
            var accessibleOrgs = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);

            // Self + descendant exclusion (cycle-prevention mirror): reuse 7403's bounded
            // descendant walk via the new read-only GetDescendantIdsAsync sibling.
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(excludeEmployeeId))
            {
                excluded.Add(excludeEmployeeId);
                foreach (var d in await reportingLineRepo.GetDescendantIdsAsync(excludeEmployeeId, ct))
                    excluded.Add(d);
            }

            // S97 / WARNING E — optional enhed-id filter (still org-bounded inside SearchPeopleAsync;
            // a name-equal enhed in another org cannot widen results). A malformed value 400s.
            Guid? enhedFilter = null;
            if (!string.IsNullOrWhiteSpace(enhedId))
            {
                if (!Guid.TryParse(enhedId, out var parsedEnhedId))
                    return Results.BadRequest(new { error = "Invalid enhedId." });
                enhedFilter = parsedEnhedId;
            }

            var (items, total) = await approvalRepo.SearchPeopleAsync(
                q ?? string.Empty, accessibleOrgs, excluded, pageLimit, pageOffset, ct, enhedFilter);

            return Results.Ok(new
            {
                items = items.Select(i => new
                {
                    userId = i.UserId,
                    displayName = i.DisplayName,
                    primaryOrgName = i.PrimaryOrgName,
                    enhedLabel = i.EnhedLabel,
                }),
                total,
                limit = pageLimit,
                offset = pageOffset,
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page picker opened to LocalHR

        // ═══════════════════════════════════════════
        // S97 / ADR-035 (TASK-9703) — structured Enhed CRUD + set-user-tags.
        //   Enhed is PURE DISPLAY metadata: ZERO authority/scope/approval meaning. Every
        //   endpoint is org-scope-FLOORED via ValidateOrgAccessAsync(actor, org, LocalHR)
        //   (the S76/S91 per-scope floor — org-scope containment preserved; cross-org blocked).
        //   The Enhed surface is ABSENT from OrgScopeValidator/RoleScope.CoversOrg/
        //   DesignatedApproverAuthorizer — tagging two users the same enhed grants neither any
        //   authority over the other (an explicit RED test in TASK-9707 pins this).
        // ═══════════════════════════════════════════

        // GET /api/admin/enheder?organisationId=… — list ACTIVE enheder for ONE Organisation.
        // The Organisation must be ∈ the actor's accessible orgs (ValidateOrgAccessAsync LocalHR
        // is the exact containment check; GLOBAL admits any existing org).
        app.MapGet("/api/admin/enheder", async (
            string? organisationId,
            EnhedRepository enhedRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (string.IsNullOrWhiteSpace(organisationId))
                return Results.BadRequest(new { error = "organisationId is required." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, organisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var rows = await enhedRepo.ListActiveByOrgAsync(organisationId, ct);
            return Results.Ok(new
            {
                enheder = rows.Select(e => new
                {
                    enhedId = e.EnhedId,
                    organisationId = e.OrganisationId,
                    name = e.Name,
                    version = e.Version,
                }),
            });
        }).RequireAuthorization("HROrAbove");

        // POST /api/admin/enheder { organisationId, name } — create. Floored on the target
        // Organisation. Rejects (400) a non-ORGANISATION org_type (mirrors the primary_org guard
        // at the users POST — an Enhed belongs to an ORGANISATION, not a MAO). 409 on active-name
        // dup (23505 on idx_enheder_active_name). Emits EnhedCreated (plain outbox — display
        // metadata, no audit-projection row, mirroring ReportingLineManagerDeactivated).
        app.MapPost("/api/admin/enheder", async (
            CreateEnhedRequest request,
            EnhedRepository enhedRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null || string.IsNullOrWhiteSpace(request.OrganisationId) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "organisationId and name are required." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // ORGANISATION-typed guard (mirror the users POST primary_org guard, same 400 shape).
            var org = await orgRepo.GetByIdAsync(request.OrganisationId, ct);
            if (org is null)
                return Results.BadRequest(new { error = "Organisation not found." });
            if (!string.Equals(org.OrgType, "ORGANISATION", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "An Enhed must belong to an Organisation (a MAO holds no enheder)." });

            var enhedId = Guid.NewGuid();
            var @event = new EnhedCreated
            {
                EnhedId = enhedId,
                OrganisationId = request.OrganisationId,
                Name = request.Name.Trim(),
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await enhedRepo.ApplyEnhedCreatedAsync(conn, tx, @event, ct);
                await outbox.EnqueueAsync(conn, tx, $"enhed-{enhedId}", @event, ct);
                await tx.CommitAsync(ct);
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { error = "An active enhed with this name already exists in this Organisation." });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            context.Response.Headers.ETag = "\"1\"";
            return Results.Created($"/api/admin/enheder/{enhedId}", new
            {
                enhedId,
                organisationId = @event.OrganisationId,
                name = @event.Name,
                version = 1L,
            });
        }).RequireAuthorization("HROrAbove");

        // PUT /api/admin/enheder/{id} { name } (If-Match) — rename. Floored on the enhed's
        // owning Organisation. 409 on active-name dup. Emits EnhedRenamed.
        app.MapPut("/api/admin/enheder/{id}", async (
            string id,
            RenameEnhedRequest request,
            EnhedRepository enhedRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });
            if (!Guid.TryParse(id, out var enhedId))
                return Results.BadRequest(new { error = "Invalid enhed id." });

            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var existing = await enhedRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Enhed not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (existing.Version != expectedVersion)
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion,
                    actualVersion = existing.Version,
                }, statusCode: 412);

            var @event = new EnhedRenamed
            {
                EnhedId = enhedId,
                Name = request.Name.Trim(),
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // BLOCKER 2 — IN-UPDATE optimistic concurrency: the version predicate lives in the
                // UPDATE itself, so two concurrent If-Match:"N" renames cannot both commit. On a
                // 0-row update we re-read (in-tx) to distinguish 404 (absent/soft-deleted) from 412
                // (version drift) and emit NOTHING (no event on a no-op write).
                var affected = await enhedRepo.ApplyEnhedRenamedAsync(conn, tx, @event, expectedVersion, ct);
                if (affected == 0)
                {
                    var current = await enhedRepo.GetByIdInTxAsync(conn, tx, enhedId, ct);
                    await tx.RollbackAsync(ct);
                    if (current is null || current.IsDeleted)
                        return Results.NotFound(new { error = "Enhed not found." });
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = current.Version,
                    }, statusCode: 412);
                }

                await outbox.EnqueueAsync(conn, tx, $"enhed-{enhedId}", @event, ct);
                await tx.CommitAsync(ct);
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { error = "An active enhed with this name already exists in this Organisation." });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            var newVersion = expectedVersion + 1;
            context.Response.Headers.ETag = $"\"{newVersion}\"";
            return Results.Ok(new
            {
                enhedId,
                organisationId = existing.OrganisationId,
                name = @event.Name,
                version = newVersion,
            });
        }).RequireAuthorization("HROrAbove");

        // DELETE /api/admin/enheder/{id} (If-Match) — SOFT delete. Floored on the enhed's owning
        // Organisation. NO fan-out untag write — memberships are projection-FILTERED. Emits
        // EnhedDeleted.
        app.MapDelete("/api/admin/enheder/{id}", async (
            string id,
            EnhedRepository enhedRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (!Guid.TryParse(id, out var enhedId))
                return Results.BadRequest(new { error = "Invalid enhed id." });

            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var existing = await enhedRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Enhed not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (existing.Version != expectedVersion)
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion,
                    actualVersion = existing.Version,
                }, statusCode: 412);

            var @event = new EnhedDeleted
            {
                EnhedId = enhedId,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // BLOCKER 2 — IN-UPDATE optimistic concurrency: a stale If-Match or an
                // already-deleted/absent row matches 0 rows. On 0 rows we re-read (in-tx) to map
                // 404 (absent/already-deleted) vs 412 (version drift) and emit NOTHING.
                var affected = await enhedRepo.ApplyEnhedDeletedAsync(conn, tx, @event, expectedVersion, ct);
                if (affected == 0)
                {
                    var current = await enhedRepo.GetByIdInTxAsync(conn, tx, enhedId, ct);
                    await tx.RollbackAsync(ct);
                    if (current is null || current.IsDeleted)
                        return Results.NotFound(new { error = "Enhed not found." });
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = current.Version,
                    }, statusCode: 412);
                }

                await outbox.EnqueueAsync(conn, tx, $"enhed-{enhedId}", @event, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.NoContent();
        }).RequireAuthorization("HROrAbove");

        // PUT /api/admin/users/{userId}/enheder { enhedIds: [] } — set the user's full tag set.
        // Floored via ValidateEmployeeAccessAsync(actor, userId, LocalHR) (org-scope over the
        // user's CURRENT org). TOCTOU-safe (WARNING C): in the tx, SELECT primary_org_id ...
        // FOR UPDATE (lock the user row); validate EACH enhedId ∈ {active enheder WHERE
        // organisation_id = the-locked-org AND deleted_at IS NULL} (a dead/foreign enhed → 400);
        // overwrite user_enheder; emit UserEnhederChanged (+ audit-projection). A concurrent
        // transfer serializes before (the new org's enheder fail validation) or after (the
        // transfer's clear wins).
        app.MapPut("/api/admin/users/{userId}/enheder", async (
            string userId,
            SetUserEnhederRequest request,
            EnhedRepository enhedRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UserEnhederChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var requestedIds = (request?.EnhedIds ?? Array.Empty<Guid>()).Distinct().ToArray();

            // Floor: org-scope (LocalHR) over the user's CURRENT org (ValidateEmployeeAccessAsync
            // resolves the target's primary_org and checks containment).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, userId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                // WARNING C — lock the user row; the locked primary_org is the authoritative
                // validation org (a concurrent transfer either committed before this lock — then
                // the new org's enheder fail validation — or blocks until after — then its
                // empty-set clear wins on commit).
                var lockedOrg = await enhedRepo.LockUserPrimaryOrgAsync(conn, tx, userId, ct);
                if (lockedOrg is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "User not found." });
                }

                // BLOCKER 1 — TOCTOU floor re-check UNDER the lock. The pre-lock
                // ValidateEmployeeAccessAsync floored against the user's PRE-LOCK org; if the user
                // was transferred between that check and this FOR UPDATE, an actor scoped only to
                // the OLD org could set/clear tags on a user now in a NEW org they don't cover.
                // Re-validate the floor against the LOCKED (current) org — mirrors the in-lock
                // re-eval discipline in the reporting-line write paths. 403 if the actor does not
                // cover the user's current Organisation.
                var (lockedAllowed, lockedReason) =
                    await scopeValidator.ValidateOrgAccessAsync(actor, lockedOrg, StatsTidRoles.LocalHR, ct);
                if (!lockedAllowed)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Access denied", reason = lockedReason }, statusCode: 403);
                }

                // Validate EACH requested enhed_id ∈ the locked org's ACTIVE enheder. Any
                // missing id is a dead/foreign enhed → 400 (the same-Organisation invariant).
                var valid = await enhedRepo.FilterValidActiveEnhedIdsForOrgAsync(conn, tx, lockedOrg, requestedIds, ct);
                if (valid.Count != requestedIds.Length)
                {
                    await tx.RollbackAsync(ct);
                    var validSet = new HashSet<Guid>(valid);
                    var rejected = requestedIds.Where(x => !validSet.Contains(x)).ToArray();
                    return Results.BadRequest(new
                    {
                        error = "One or more enheder are not active enheder of the user's Organisation.",
                        rejected,
                    });
                }

                var @event = new UserEnhederChanged
                {
                    UserId = userId,
                    EnhedIds = requestedIds,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await enhedRepo.ApplyUserEnhederChangedAsync(conn, tx, @event, ct);

                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", @event, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: lockedOrg);
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.Ok(new { userId, enhedIds = requestedIds });
        }).RequireAuthorization("HROrAbove");

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

        // S76 / TASK-7600 B3: a GLOBAL scope only admits a GlobalAdmin operation if the scope
        // ITSELF carries GlobalAdmin role. Pre-fix this returned true for ANY GLOBAL scope
        // regardless of role — the mixed-role leak class (a non-admin GLOBAL scope, e.g.
        // GLOBAL+LocalLeader, would pass the GlobalAdmin-only gates this helper guards:
        // top-level org create, global role grant/revoke). The no-scopes fallback above is
        // already role-correct (checks ActorRole == GlobalAdmin).
        return actor.Scopes.Any(s =>
            s.ScopeType == "GLOBAL" && StatsTidRoles.IsAtLeast(s.Role, StatsTidRoles.GlobalAdmin));
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
        agreementCode = org.AgreementCode,
        // S76b: serve the org's denormalized okVersion so the unified-editor create
        // flow can supply the backend-required CreateUserRequest.OkVersion (additive
        // read field; consumers ignore unknown fields). Declared read-gap fill.
        okVersion = org.OkVersion
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

    private sealed class UpdateOrganizationRequest
    {
        public string? OrgName { get; init; }
        public string? AgreementCode { get; init; }
        public string? OkVersion { get; init; }
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

        // S74 R9 — OPTIONAL atomic create+assign. When supplied, the create tx ALSO creates the
        // new person's PRIMARY reporting line under this approver (same tree, cycle-guarded),
        // emitting ReportingLineAssigned + a reporting_line_audit row — all in the SAME tx, so a
        // person is never left an orphan via the admin create path (the FE always supplies it per
        // OQ-2a). Omitted ⇒ behaviour unchanged (no reporting line).
        public string? ApproverId { get; init; }
    }

    private sealed class UpdateUserRequest
    {
        public string? DisplayName { get; init; }
        public string? Email { get; init; }
        public string? PrimaryOrgId { get; init; }
        public string? AgreementCode { get; init; }
        /// <summary>
        /// S34 / TASK-3407 (ADR-023 D2 option (b)) — required.
        /// Validator narrows to <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> per
        /// ADR-023 D8 same-day-only-edit narrowing. Always sent by the frontend
        /// (TASK-3409 — <c>new Date().toISOString().slice(0,10)</c> UTC extraction);
        /// drives ADR-020 D2 3-case routing in
        /// <c>UserAgreementCodeRepository.SupersedeAndCreateAsync</c> when
        /// <c>AgreementCode</c> mutates against an existing live row (Case B
        /// same-day in-place vs Case C cross-day supersession against a seeder-
        /// backfilled <c>'0001-01-01'</c> predecessor).
        /// </summary>
        public DateOnly EffectiveFrom { get; init; }
        /// <summary>
        /// S52 / ADR-027 deferred item — optional. When explicitly set to <c>false</c>
        /// on a currently-active user, the handler emits
        /// <see cref="ReportingLineManagerDeactivated"/> events for each active
        /// reporting line where the user is the manager.
        /// </summary>
        public bool? IsActive { get; init; }
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

    // ── S97 / ADR-035 — structured Enhed request DTOs ──
    private sealed class CreateEnhedRequest
    {
        public required string OrganisationId { get; init; }
        public required string Name { get; init; }
    }

    private sealed class RenameEnhedRequest
    {
        public required string Name { get; init; }
    }

    private sealed class SetUserEnhederRequest
    {
        /// <summary>The FULL active Enhed-id set the user should be tagged with (an empty /
        /// omitted array clears all tags). Idempotent overwrite — validated against the user's
        /// locked primary_org's active enheder before the projection write.</summary>
        public IReadOnlyList<Guid>? EnhedIds { get; init; }
    }

    /// <summary>
    /// S34 / TASK-3407 — local snapshot record for the user_agreement_codes
    /// predecessor row state read in-tx by the PUT handler BEFORE routing through
    /// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>. Carries
    /// enough fields to (a) build the user_agreement_codes_audit row's
    /// <c>previous_data</c> JSONB + <c>version_before</c> column, and (b) hydrate
    /// the <see cref="UserAgreementCodeSuperseded"/> event payload on Case C
    /// (cross-day supersession) without a second SELECT. Mirrors the
    /// EmployeeProfileEndpoints PUT path's <c>PredecessorSnapshot</c>.
    /// </summary>
    private sealed record AgreementPredecessorSnapshot(
        Guid AssignmentId,
        string AgreementCode,
        DateOnly EffectiveFrom,
        long Version);
}
