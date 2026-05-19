using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Exceptions;
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
            IOutboxEnqueue outbox,
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
                await outbox.EnqueueAsync(conn, tx, $"org-{request.OrgId}", @event, ct);

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
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Look up existing org
            var existingOrg = await orgRepo.GetByIdAsync(orgId, ct);
            if (existingOrg is null)
                return Results.NotFound(new { error = "Organization not found" });

            // Validate actor scope covers this org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
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
                await outbox.EnqueueAsync(conn, tx, $"org-{orgId}", @event, ct);

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
            IOutboxEnqueue outbox,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.AdminEndpoints.UsersPost");

            // Validate actor scope covers target org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.PrimaryOrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

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
                        (profile_id, employee_id, weekly_norm_hours, part_time_fraction, position,
                         effective_from)
                    VALUES
                        (@profileId, @employeeId, @weeklyNormHours, @partTimeFraction, NULL,
                         @effectiveFrom)
                    """, conn, tx);
                profileCmd.Parameters.AddWithValue("profileId", profileId);
                profileCmd.Parameters.AddWithValue("employeeId", request.UserId);
                profileCmd.Parameters.AddWithValue("weeklyNormHours", 37.0m);
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
                    weeklyNormHours = 37.0m,
                    partTimeFraction = 1.000m,
                    position = (string?)null,
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
                await outbox.EnqueueAsync(conn, tx, $"user-{request.UserId}", @event, ct);

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
                    WeeklyNormHours = 37.0m,
                    PartTimeFraction = 1.000m,
                    Position = null,
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await outbox.EnqueueAsync(conn, tx, $"employee-profile-{request.UserId}", profileEvent, ct);

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
                await outbox.EnqueueAsync(conn, tx, $"user-{request.UserId}", agreementSeededEvent, ct);

                await tx.CommitAsync(ct);
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
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

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
            IOutboxEnqueue outbox,
            DbConnectionFactory dbFactory,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.AdminEndpoints.UsersPut");

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
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
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
                    var canonicalOldAgreementCode =
                        agreementPredecessor?.AgreementCode ?? existingUser.AgreementCode;
                    if (string.Equals(request.AgreementCode, canonicalOldAgreementCode, StringComparison.Ordinal))
                    {
                        agreementCodeMutated = false;
                    }
                }

                await using var cmd = new NpgsqlCommand(
                    """
                    UPDATE users
                    SET display_name = @displayName,
                        email = @email,
                        primary_org_id = @primaryOrgId,
                        agreement_code = @agreementCode,
                        updated_at = @now
                    WHERE user_id = @userId AND is_active = TRUE
                    """, conn, tx);
                cmd.Parameters.AddWithValue("displayName", request.DisplayName ?? existingUser.DisplayName);
                cmd.Parameters.AddWithValue("email", (object?)(request.Email ?? existingUser.Email) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("primaryOrgId", request.PrimaryOrgId ?? existingUser.PrimaryOrgId);
                cmd.Parameters.AddWithValue("agreementCode", request.AgreementCode ?? existingUser.AgreementCode);
                cmd.Parameters.AddWithValue("now", now);
                cmd.Parameters.AddWithValue("userId", userId);
                await cmd.ExecuteNonQueryAsync(ct);

                // Emit domain event in-tx (BEFORE CommitAsync) so the users row
                // and the outbox row commit atomically per ADR-018 D3. UserUpdated
                // fires on EVERY PUT regardless of agreement_code mutation
                // (preserved S31 / S33 contract).
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
                await outbox.EnqueueAsync(conn, tx, $"user-{userId}", @event, ct);

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
                    // now flow off the same locked row state. Case A safety-net (no live
                    // row exists) falls back to existingUser.AgreementCode.
                    var agreementChangedEvent = new UserAgreementCodeChanged
                    {
                        UserId = userId,
                        OldAgreementCode = agreementPredecessor?.AgreementCode ?? existingUser.AgreementCode,
                        NewAgreementCode = request.AgreementCode!,
                        EffectiveFrom = request.EffectiveFrom,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId
                    };
                    await outbox.EnqueueAsync(conn, tx, $"user-{userId}", agreementChangedEvent, ct);

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
                        await outbox.EnqueueAsync(conn, tx, $"user-{userId}", supersededEvent, ct);
                    }
                }

                await tx.CommitAsync(ct);
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
            IOutboxEnqueue outbox,
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
                await outbox.EnqueueAsync(conn, tx, $"user-{request.UserId}", @event, ct);

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
                await outbox.EnqueueAsync(conn, tx, $"user-{assignmentUserId}", @event, ct);

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
