using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
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

            // S99/TASK-9900: resolve the optional fields server-side so the name-only
            // Create dialog works. AgreementCode/OkVersion are VESTIGIAL on the org tree
            // ("overenskomst is NOT a property of the org tree" per design_handoff_organisation)
            // — they do not drive employee agreements — so default them to the system
            // defaults ('AC'/'OK24', see docker/postgres/init.sql) when blank. An explicit
            // value is honored unchanged (backward-compatible).
            var agreementCode = string.IsNullOrWhiteSpace(request.AgreementCode) ? "AC" : request.AgreementCode;
            var okVersion = string.IsNullOrWhiteSpace(request.OkVersion) ? "OK24" : request.OkVersion;

            // S99/TASK-9900: OrgId is OPTIONAL — when blank the BACKEND owns the
            // id-generation policy (NOT the FE). Format: "ORG" + the first 8 hex chars of
            // a GUID, uppercased (e.g. "ORG3F9A1C7D"). ~4.3e9 of entropy in 8 hex chars;
            // a rare PK/partial-unique collision (23505) is handled by a bounded
            // re-generate retry around the INSERT below. The owner can revisit the format.
            var explicitOrgId = !string.IsNullOrWhiteSpace(request.OrgId);
            var orgId = explicitOrgId ? request.OrgId! : GenerateOrgId();

            // S92/ADR-035 type-scoped parent rules:
            //   MAO          = root         → MUST NOT have a parent.
            //   ORGANISATION = under a MAO  → MUST have a parent, and that parent MUST be a MAO.
            if (orgType == "MAO" && request.ParentOrgId is not null)
                return Results.BadRequest(new { error = "A MAO is a root organization and must not have a parent." });
            if (orgType == "ORGANISATION" && request.ParentOrgId is null)
                return Results.BadRequest(new { error = "An ORGANISATION must have a MAO parent." });

            // Compute materialized path. The path is parameterized on the (possibly
            // generated) orgId — recomputed inside the retry loop if a collision forces a
            // re-generate (an explicit orgId never re-generates → its path is stable).
            string materializedPath;
            string? parentMaterializedPath = null;
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

                parentMaterializedPath = parentOrg.MaterializedPath;
                materializedPath = $"{parentMaterializedPath}{orgId}/";
            }
            else
            {
                // Top-level org (a MAO root) — only GlobalAdmin should create these
                if (!HasGlobalScope(actor))
                    return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can create top-level organizations" }, statusCode: 403);

                materializedPath = $"/{orgId}/";
            }

            // Check if org already exists. For an EXPLICIT orgId this is a hard conflict
            // (the caller chose the id). For a GENERATED orgId a collision here is vanishingly
            // rare and the INSERT's 23505 retry below also covers the race; we still
            // pre-check to surface the explicit-id conflict early.
            var existing = await orgRepo.GetByIdAsync(orgId, ct);
            if (existing is not null)
            {
                if (explicitOrgId)
                    return Results.Conflict(new { error = $"Organization '{orgId}' already exists" });
                // Generated id collided on the pre-check — regenerate and recompute the path.
                orgId = GenerateOrgId();
                materializedPath = parentMaterializedPath is not null
                    ? $"{parentMaterializedPath}{orgId}/"
                    : $"/{orgId}/";
            }

            // Atomic INSERT + outbox-emit per ADR-018 D3 (S26 TASK-2605a prototype):
            // inline organizations INSERT and OrganizationCreated outbox enqueue ride
            // a single explicit transaction; commit at end of try, rollback on throw.
            //
            // S99/TASK-9900: bounded retry around the unique-violation (23505). For a
            // GENERATED orgId a rare PK / is_active-partial-unique collision re-generates
            // the id and re-runs against a FRESH transaction (a rolled-back tx is not
            // reusable). For an EXPLICIT orgId the caller's id is authoritative — a
            // collision is a hard 409 (the pre-check above already returns it; this
            // surfaces a race-window collision as the same conflict).
            const int maxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
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
                    cmd.Parameters.AddWithValue("orgId", orgId);
                    cmd.Parameters.AddWithValue("orgName", request.OrgName);
                    cmd.Parameters.AddWithValue("orgType", orgType);
                    cmd.Parameters.AddWithValue("parentOrgId", (object?)request.ParentOrgId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("materializedPath", materializedPath);
                    cmd.Parameters.AddWithValue("agreementCode", agreementCode);
                    cmd.Parameters.AddWithValue("okVersion", okVersion);
                    cmd.Parameters.AddWithValue("now", now);
                    await cmd.ExecuteNonQueryAsync(ct);

                    // Emit domain event in-tx (BEFORE CommitAsync) so the organizations row
                    // and the outbox row commit atomically per ADR-018 D3.
                    var @event = new OrganizationCreated
                    {
                        OrgId = orgId,
                        OrgName = request.OrgName,
                        OrgType = orgType,
                        ParentOrgId = request.ParentOrgId,
                        MaterializedPath = materializedPath,
                        AgreementCode = agreementCode,
                        OkVersion = okVersion,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write — atomic with the
                    // organizations row + outbox row per ADR-018 D3/D13).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = auditMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                    await tx.CommitAsync(ct);
                    break;
                }
                catch (PostgresException pex) when (pex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    await tx.RollbackAsync(ct);

                    // An explicit caller-chosen id collided → hard conflict (don't re-generate).
                    if (explicitOrgId)
                        return Results.Conflict(new { error = $"Organization '{orgId}' already exists" });

                    // A generated id collided (extremely rare) → regenerate, recompute the
                    // path, and retry against a fresh transaction up to maxAttempts.
                    if (attempt >= maxAttempts)
                        throw;
                    orgId = GenerateOrgId();
                    materializedPath = parentMaterializedPath is not null
                        ? $"{parentMaterializedPath}{orgId}/"
                        : $"/{orgId}/";
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Results.Created($"/api/admin/organizations/{orgId}", new
            {
                orgId,
                orgName = request.OrgName,
                orgType,
                parentOrgId = request.ParentOrgId,
                materializedPath,
                agreementCode,
                okVersion
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

        // 2c. DELETE /api/admin/organizations/{orgId} — S98 / ADR-035 GlobalAdmin org SOFT-delete.
        //
        // GlobalAdmin-floored (the NEW structural ops are GlobalAdmin-only; the existing
        // create/rename stay LocalAdmin+ — S98 WARNING 2). NO If-Match: there is NO version column
        // on organizations (Step-0b BLOCKER A) — these are low-contention GlobalAdmin ops, and the
        // existing rename PUT is last-writer-wins too. Concurrency safety comes from an in-tx
        // SELECT … FOR UPDATE on the org row (serializes concurrent structural ops) + the
        // blocked-if-employees count + the is_active=FALSE flip all in ONE tx.
        //
        // Blocked-if-employees (422 + employeeCount): an ORGANISATION blocks if any active user is
        // homed on it; a MAO blocks if any active user lives anywhere beneath it (the MAO-subtree
        // LIKE). Empty → allowed. A soft-deleted org disappears from GetByIdAsync/GetAllAsync, so
        // the create/transfer home guards already reject it (Step-0b BLOCKER B — no new guard).
        app.MapDelete("/api/admin/organizations/{orgId}", async (
            string orgId,
            OrganizationRepository orgRepo,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OrganizationDeleted> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // GlobalAdmin floor — mirror the MAO-create gate (HasGlobalScope). 403 otherwise.
            if (!HasGlobalScope(actor))
                return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can delete organizations" }, statusCode: 403);

            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // SELECT … FOR UPDATE the ACTIVE org row — serializes concurrent structural ops;
                // null → absent or already soft-deleted → 404.
                var org = await orgRepo.LockActiveByIdAsync(conn, tx, orgId, ct);
                if (org is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Organization not found" });
                }

                // Blocked-if-employees: consistent count under the held org row.
                var employeeCount = await orgRepo.CountActiveEmployeesBlockingDeleteAsync(conn, tx, org, ct);
                if (employeeCount > 0)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Kan ikke slette organisationen — den indeholder {employeeCount} medarbejder(e).",
                        employeeCount,
                    });
                }

                // Flip is_active=FALSE in the same tx.
                await orgRepo.SoftDeleteAsync(conn, tx, orgId, now, ct);

                // Emit OrganizationDeleted + the audit-projection row (ADR-018 D3 + ADR-026 D2).
                var @event = new OrganizationDeleted
                {
                    OrgId = org.OrgId,
                    OrgName = org.OrgName,
                    OrgType = org.OrgType,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
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

            return Results.NoContent();
        }).RequireAuthorization("HROrAbove"); // policy floor; GlobalAdmin enforced in-handler (HasGlobalScope)

        // 2d. PUT /api/admin/organizations/{orgId}/move — S98 / ADR-035 GlobalAdmin org RE-PARENT.
        //
        // GlobalAdmin-floored. ORGANISATION only (a MAO is a root → 422). The target newParentOrgId
        // MUST be an active MAO (else 422). Reject no-op (newParent == current parent → 400) and
        // self-parent (newParent == orgId → 400). NO If-Match (no version column — BLOCKER A).
        //
        // In ONE tx: SELECT … FOR UPDATE the org + read the new parent → set parent_org_id +
        // RECOMPUTE the moved row's OWN materialized_path = newParent.path || orgId || '/'. NO
        // descendant cascade (Organisations are leaves). The path rewrite is LOAD-BEARING (BLOCKER
        // 1): ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync / GetPeriodStatusProjection-
        // ForTreeAsync scope by materialized_path LIKE — an unrewritten path silently drops the
        // org's employees from the tree-roster reads. (The vikar reader is exact-equality → safe.)
        app.MapPut("/api/admin/organizations/{orgId}/move", async (
            string orgId,
            MoveOrganizationRequest request,
            OrganizationRepository orgRepo,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OrganizationMoved> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (!HasGlobalScope(actor))
                return Results.Json(new { error = "Access denied", reason = "Only GlobalAdmin can move organizations" }, statusCode: 403);

            if (request is null || string.IsNullOrWhiteSpace(request.NewParentOrgId))
                return Results.BadRequest(new { error = "newParentOrgId is required." });

            if (string.Equals(request.NewParentOrgId, orgId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "An organization cannot be moved under itself." });

            var now = DateTime.UtcNow;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // SELECT … FOR UPDATE the ACTIVE org being moved; null → 404.
                var org = await orgRepo.LockActiveByIdAsync(conn, tx, orgId, ct);
                if (org is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Organization not found" });
                }

                // Only an ORGANISATION moves — a MAO is a root.
                if (!string.Equals(org.OrgType, "ORGANISATION", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = "Ministeransvarsområder kan ikke flyttes (kun organisationer kan flyttes)." });
                }

                // No-op: already under this parent.
                if (string.Equals(org.ParentOrgId, request.NewParentOrgId, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.BadRequest(new { error = "Organization is already under this parent." });
                }

                // S98 Step-7a FIX 1 (P4 move-vs-delete-of-new-parent race) — LOCK the new parent MAO
                // IN THE SAME TX (SELECT … FOR UPDATE on the ACTIVE row) rather than reading it on a
                // separate connection outside the tx. Otherwise a concurrent GlobalAdmin soft-delete of
                // the target MAO could commit between an out-of-tx read and this move's COMMIT, leaving
                // an active ORGANISATION parented under (and path-rooted at) an is_active=false MAO.
                // With the FOR-UPDATE lock the delete serializes behind us: it blocks until we commit,
                // OR (if it committed first) our LockActiveByIdAsync sees no ACTIVE row → 422.
                //
                // Lock order: the MOVED org is locked first (above), the new parent second. The two are
                // DISTINCT roles (an ORGANISATION can never be its own MAO parent — self-parent already
                // rejected at :423, and a MAO can't be moved at :442), so this fixed moved→parent order
                // is consistent and deadlock-free across concurrent moves (cf. the S95 dual-row id-sorted
                // pin for the symmetric same-Organisation case; here the roles are asymmetric).
                var newParent = await orgRepo.LockActiveByIdAsync(conn, tx, request.NewParentOrgId, ct);
                if (newParent is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = "New parent organization not found." });
                }
                if (!string.Equals(newParent.OrgType, "MAO", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = $"An ORGANISATION's parent must be a MAO (got {newParent.OrgType})." });
                }

                var oldParentOrgId = org.ParentOrgId;
                var oldMaterializedPath = org.MaterializedPath;

                // Re-parent + recompute the moved row's own materialized_path in the same tx.
                var newMaterializedPath = await orgRepo.ReparentAsync(conn, tx, orgId, newParent, now, ct);

                // Emit OrganizationMoved (old+new parent + old+new path for replay) + audit row.
                var @event = new OrganizationMoved
                {
                    OrgId = orgId,
                    OldParentOrgId = oldParentOrgId,
                    NewParentOrgId = newParent.OrgId,
                    OldMaterializedPath = oldMaterializedPath,
                    NewMaterializedPath = newMaterializedPath,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", @event, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                return Results.Ok(new
                {
                    orgId,
                    orgName = org.OrgName,
                    orgType = org.OrgType,
                    parentOrgId = newParent.OrgId,
                    materializedPath = newMaterializedPath,
                    agreementCode = org.AgreementCode,
                    okVersion = org.OkVersion,
                });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove"); // policy floor; GlobalAdmin enforced in-handler (HasGlobalScope)

        // 2e. GET /api/admin/organizations/tree — S98 / ADR-035 aggregated MAO→Organisation forest.
        //
        // Returns the forest with per-node employeeCount (Organisation = its own active users;
        // MAO = Σ its Organisations). SET-BASED — one GROUP BY over users, the orgs
        // fetched active, the forest assembled in C# (NO N+1). Visibility-bounded: GlobalAdmin → all;
        // scoped roles → their GetAccessibleOrgsAsync(LocalHR) set (the same per-org floor as the
        // GET-list endpoint above). HROrAbove read floor.
        app.MapGet("/api/admin/organizations/tree", async (
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Visibility set: null → unrestricted (GlobalAdmin); otherwise the exact accessible-org
            // ids (LocalHR-floored, same gate as the GET-list endpoint). Employees/below-floor → [].
            var accessible = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            HashSet<string>? visible = accessible is null ? null : new HashSet<string>(accessible, StringComparer.Ordinal);

            // Fetch the active orgs + the set-based employee-count aggregate.
            var allOrgs = await orgRepo.GetAllAsync(ct);
            var empCounts = await orgRepo.GetActiveEmployeeCountByOrgAsync(ct);

            // An Organisation is visible if it ∈ the accessible set (or unrestricted). A MAO is
            // visible if unrestricted OR it has at least one visible child Organisation (a scoped
            // HR sees the MAO header for the Organisations it can reach). The MAO count sums ONLY
            // its visible children's counts.
            bool OrgVisible(string id) => visible is null || visible.Contains(id);

            var organisationsByParent = allOrgs
                .Where(o => string.Equals(o.OrgType, "ORGANISATION", StringComparison.Ordinal))
                .GroupBy(o => o.ParentOrgId ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // S101/TASK-10101: the forest nodes are the named OrgTreeMaoNode / OrgTreeOrganisationNode.
            // The visibility filter + the per-MAO rollup are UNCHANGED. S103/TASK-10304: the per-
            // Organisation enhed nesting is retired with the legacy Enhed tables (unit display returns
            // in Enhedsspor Phase 3).
            var forest = new List<OrgTreeMaoNode>();
            foreach (var mao in allOrgs.Where(o => string.Equals(o.OrgType, "MAO", StringComparison.Ordinal))
                                       .OrderBy(o => o.MaterializedPath, StringComparer.Ordinal))
            {
                var children = organisationsByParent.TryGetValue(mao.OrgId, out var kids) ? kids : new List<StatsTid.SharedKernel.Models.Organization>();

                var visibleChildren = children
                    .Where(c => OrgVisible(c.OrgId))
                    .OrderBy(c => c.MaterializedPath, StringComparer.Ordinal)
                    .Select(c => new OrgTreeOrganisationNode(
                        OrgId: c.OrgId,
                        OrgName: c.OrgName,
                        OrgType: c.OrgType,
                        ParentOrgId: c.ParentOrgId,
                        MaterializedPath: c.MaterializedPath,
                        AgreementCode: c.AgreementCode,
                        OkVersion: c.OkVersion,
                        EmployeeCount: empCounts.TryGetValue(c.OrgId, out var n) ? n : 0L))
                    .ToList();

                // GlobalAdmin sees every MAO (even childless); a scoped role only sees a MAO that
                // has at least one visible child Organisation.
                if (visible is not null && visibleChildren.Count == 0)
                    continue;

                forest.Add(new OrgTreeMaoNode(
                    OrgId: mao.OrgId,
                    OrgName: mao.OrgName,
                    OrgType: mao.OrgType,
                    ParentOrgId: mao.ParentOrgId,
                    MaterializedPath: mao.MaterializedPath,
                    AgreementCode: mao.AgreementCode,
                    OkVersion: mao.OkVersion,
                    EmployeeCount: visibleChildren.Sum(c => (long)c.EmployeeCount),
                    Organisations: visibleChildren));
            }

            return Results.Ok(new OrgTreeResponse(forest));
        }).RequireAuthorization("HROrAbove");

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
            UnitRepository unitRepo,
            ManagerVikarRepository vikarRepo,
            IAuditProjectionMapper<UserUpdated> auditMapper,
            IAuditProjectionMapper<UserAgreementCodeChanged> uacChangedMapper,
            IAuditProjectionMapper<UserAgreementCodeSuperseded> uacSupersededMapper,
            IAuditProjectionMapper<UserUnitChanged> userUnitMapper,
            IAuditProjectionMapper<UnitLeaderRemoved> leaderRemovedMapper,
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

                    // S104 / ADR-038 D8 — the TOTAL lock order is all `reporting-org-` advisories (above,
                    // id-sorted) → all `unit-org-` advisories (here, id-sorted) → the users-row FOR UPDATE
                    // (below). The transfer takes BOTH the OLD + NEW Organisations' `unit-org-` advisories
                    // so it serializes with structural unit-moves / leader-designations / same-Org member
                    // moves in EITHER Organisation (the moved user is leaving the OLD unit tree + may land
                    // in the NEW one). Distinct + id-sorted (deadlock-safe across concurrent transfers).
                    foreach (var unitOrg in new[] { existingUser.PrimaryOrgId, request.PrimaryOrgId }
                                 .Distinct(StringComparer.Ordinal)
                                 .OrderBy(o => o, StringComparer.Ordinal))
                    {
                        await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, unitOrg, ct);
                    }
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

                // ═══ S104 / ADR-038 D8 — CROSS-ORGANISATION TRANSFER pre-checks (canonical, post-lock) ═══
                // A transfer is a unit-change that crosses Organisations. Decided against the FOR-UPDATE'd
                // lockedUser (the version-matched canonical home). The unit-org advisories for both the
                // OLD + NEW Organisations are already held (acquired above, after the reporting-org pair).
                var isTransfer = request.PrimaryOrgId is not null &&
                    !string.Equals(request.PrimaryOrgId, lockedUser.PrimaryOrgId, StringComparison.Ordinal);
                var currentUnitId = await unitRepo.GetUserUnitIdInTxAsync(conn, tx, userId, ct);
                var newUnitId = currentUnitId; // non-transfer PUT: unit_id is untouched (no-op write).
                if (isTransfer)
                {
                    // (c) the manager-side fan-out — BLOCK (422) the transfer of a user who still
                    // MANAGES active reports (owner-decided: re-assign the reports first rather than
                    // silently orphan them cross-Organisation).
                    var activeReports = await reportingLineRepo.GetDirectReportsAsync(conn, tx, userId, ct);
                    if (activeReports.Count > 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "Cannot transfer a user who still manages active reports; re-assign their reports first.",
                            activeReportCount = activeReports.Count,
                        });
                    }

                    // The new home unit (null = home directly at the new Organisation). A non-null unit
                    // MUST be ACTIVE + belong to the NEW Organisation (request.PrimaryOrgId). The old
                    // unit_id (in the OLD Organisation) is ALWAYS reset on a transfer.
                    if (request.UnitId is Guid targetUnitId)
                    {
                        var targetUnit = await unitRepo.GetActiveUnitInTxAsync(conn, tx, targetUnitId, ct);
                        if (targetUnit is null)
                        {
                            await tx.RollbackAsync(ct);
                            return Results.UnprocessableEntity(new { error = "The target unit does not exist or is deleted." });
                        }
                        if (!string.Equals(targetUnit.OrganisationId, request.PrimaryOrgId, StringComparison.Ordinal))
                        {
                            await tx.RollbackAsync(ct);
                            return Results.UnprocessableEntity(new { error = "The target unit must belong to the new Organisation." });
                        }
                    }
                    newUnitId = request.UnitId;
                }

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
                        unit_id = @unitId,
                        agreement_code = @agreementCode,
                        is_active = @isActive,
                        version = version + 1,
                        updated_at = @now
                    WHERE user_id = @userId AND is_active = TRUE
                    """, conn, tx);
                cmd.Parameters.AddWithValue("displayName", newDisplayName);
                cmd.Parameters.AddWithValue("email", (object?)newEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("primaryOrgId", newPrimaryOrgId);
                // S104 / ADR-038 D8 — on a TRANSFER newUnitId = request.UnitId (the old-Organisation
                // unit_id is reset); on a non-transfer PUT newUnitId = currentUnitId (an untouched no-op).
                cmd.Parameters.AddWithValue("unitId", (object?)newUnitId ?? DBNull.Value);
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

                // ═══ S104 / ADR-038 D8 — CROSS-ORGANISATION TRANSFER re-sync (the unit + edge fan-out) ═══
                // Runs AFTER the users UPDATE + UserUpdated emit, BEFORE COMMIT. All under the held
                // reporting-org + unit-org advisories for both Organisations (acquired at the top).
                if (isTransfer)
                {
                    var today = DateOnly.FromDateTime(now);

                    // (a) Clear the moved user's OLD-unit `unit_leaders` rows + emit UnitLeaderRemoved per
                    // row (a transferred leader must lose the old-unit designation — the D3 member-invariant
                    // across Organisations). The leadership org is the OLD Organisation (lockedUser's home).
                    var lostLeadership = await unitRepo.RemoveAllLeadershipForUserAsync(conn, tx, userId, ct);
                    foreach (var ledUnitId in lostLeadership)
                    {
                        var leaderRemoved = new UnitLeaderRemoved
                        {
                            UnitId = ledUnitId,
                            UserId = userId,
                            OrganisationId = lockedUser.PrimaryOrgId,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        var lrOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"unit-{ledUnitId}", leaderRemoved, ct);
                        var lrCtx = new AuditProjectionContext(
                            ActorId: actor.ActorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: actor.CorrelationId,
                            OccurredAt: new DateTimeOffset(leaderRemoved.OccurredAt));
                        var lrRow = leaderRemovedMapper.Map(leaderRemoved, lrCtx);
                        await auditRepo.InsertAsync(conn, tx, leaderRemoved.EventId, lrOutboxId, leaderRemoved.EventType, lrRow, lrCtx, ct);
                    }

                    // (b) Re-anchor the user's OWN employee-side reporting edges: a cross-Organisation
                    // PRIMARY/ACTING edge is FORBIDDEN (the ADR-027/S95 same-Organisation reporting
                    // invariant), so close each with NO successor (ReportingLineSuperseded, NewManagerId
                    // null). Plain outbox — reporting lifecycle events carry no audit_projection row
                    // (the ReportingLineManagerDeactivated precedent). The manager-SIDE fan-out is already
                    // blocked (422 with-reports above), so only the user's incoming edges remain.
                    var ownEdges = await reportingLineRepo.GetActiveByEmployeeInTxAsync(conn, tx, userId, ct);
                    foreach (var edge in ownEdges)
                    {
                        var closed = await reportingLineRepo.RemoveAsync(conn, tx, edge.Version, userId, edge.Relationship, ct);
                        var superseded = new ReportingLineSuperseded
                        {
                            ReportingLineId = closed.ReportingLineId,
                            EmployeeId = userId,
                            PreviousManagerId = closed.ManagerId,
                            NewManagerId = null,
                            OrganisationId = closed.OrganisationId,
                            EffectiveFrom = closed.EffectiveFrom,
                            EffectiveTo = closed.EffectiveTo ?? today,
                            RowVersion = closed.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, $"reporting-line-{userId}", superseded, ct);
                    }

                    // (b') Re-anchor the user's vikar rows (as absent approver OR as stand-in): vikar is
                    // same-Organisation-bound (D12), so a transfer closes them (ManagerVikarEnded,
                    // APPROVER_REMOVED). Plain outbox.
                    var closedVikars = await vikarRepo.CloseAllInvolvingUserAsync(conn, tx, userId, today, ct);
                    foreach (var vikar in closedVikars)
                    {
                        var vikarEnded = new ManagerVikarEnded
                        {
                            VikarId = vikar.VikarId,
                            AbsentApproverId = vikar.AbsentApproverId,
                            VikarUserId = vikar.VikarUserId,
                            UntilDate = vikar.UntilDate,
                            Reason = vikar.Reason,
                            OrganisationId = vikar.OrganisationId,
                            EffectiveTo = vikar.EffectiveTo ?? today,
                            EndReason = "APPROVER_REMOVED",
                            RowVersion = vikar.Version,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, $"manager-vikar-{vikar.VikarId}", vikarEnded, ct);
                    }

                    // (d) Emit UserUnitChanged (the structural membership move) + its audit row. The derived
                    // OrganisationId is the NEW Organisation (= the recomputed primary_org_id, newPrimaryOrgId).
                    var userUnitChanged = new UserUnitChanged
                    {
                        UserId = userId,
                        OldUnitId = currentUnitId,
                        NewUnitId = newUnitId,
                        OrganisationId = newPrimaryOrgId,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var uucOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", userUnitChanged, ct);
                    var uucCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(userUnitChanged.OccurredAt));
                    var uucRow = userUnitMapper.Map(userUnitChanged, uucCtx);
                    await auditRepo.InsertAsync(conn, tx, userUnitChanged.EventId, uucOutboxId, userUnitChanged.EventType, uucRow, uucCtx, ct);
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
                    // S106 / TASK-10602 — the unit tag + the nullable reporting-line etag.
                    unitId = e.UnitId,
                    unitName = e.UnitName,
                    leaderIds = e.LeaderIds,
                    primaryReportingLineVersion = e.PrimaryReportingLineVersion,
                }),
                pendingCountByManager = roster.PendingCountByManager,
                // S106 / TASK-10602 — the DISPLAY-ONLY by-id name resolution (upward-ref +
                // cross-unit-leader chips), keyed by user_id.
                nameResolution = roster.NameResolution.ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        userId = kv.Value.UserId,
                        displayName = kv.Value.DisplayName,
                        position = kv.Value.Position,
                        unitName = kv.Value.UnitName,
                    }),
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

            // S103 / TASK-10304 — the S97 optional enhed-id filter is retired with the legacy Enhed
            // tag tables (the search no longer joins them; the unit model returns in a later phase).
            var (items, total) = await approvalRepo.SearchPeopleAsync(
                q ?? string.Empty, accessibleOrgs, excluded, pageLimit, pageOffset, ct);

            return Results.Ok(new
            {
                items = items.Select(i => new
                {
                    userId = i.UserId,
                    displayName = i.DisplayName,
                    primaryOrgName = i.PrimaryOrgName,
                    // S103 — the structured-Enhed display column is retired; now always null.
                    enhedLabel = i.EnhedLabel,
                }),
                total,
                limit = pageLimit,
                offset = pageOffset,
            });
        }).RequireAuthorization("HROrAbove"); // S91 TASK-9102: tree-page picker opened to LocalHR

        // ═══════════════════════════════════════════
        // S106 / TASK-10603 — GET /api/admin/search — the scoped units + people SEARCH read for the
        //   merged-admin overlay. Returns the design's TWO-section shape ({ units, people }) — each
        //   row carrying the node's full PATH (the breadcrumb). Server-side because the FE lazy-loads
        //   per Organisation and a client filter cannot see un-loaded people.
        //
        //   SCOPE (ADR-038 D5 / P7) — the SAME admission the existing users/search + the forest read
        //   use: the accessible-org set via GetAccessibleOrgsAsync(LocalHR) (null = GLOBAL/unrestricted,
        //   [] = nobody). UNITS are bounded by their immutable organisation_id ∈ that set — NO per-unit
        //   visibility predicate (a unit is searchable ONLY because its Organisation is admitted). PEOPLE
        //   are bounded by primary_org_id ∈ that set. A scoped HR gets NO cross-Organisation results.
        //   Both a matched unit's ancestor chain and a matched person's home-unit chain stay WITHIN that
        //   one accessible Organisation (units belong to exactly one org), so the in-memory PATH build
        //   leaks nothing. Paginated/capped per section (default 50 / cap 200), mirroring users/search.
        //   HROrAbove read floor (LocalHR floor enforced via the role-floored accessible-org set).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/search", async (
            string? q,
            int? limit,
            int? offset,
            UnitRepository unitRepo,
            ApprovalPeriodRepository approvalRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            CancellationToken ct,
            HttpContext context) =>
        {
            var actor = context.GetActorContext();

            // Pagination: default 50, cap 200 per section; offset >= 0 (the users/search convention).
            var pageLimit = Math.Clamp(limit ?? 50, 1, 200);
            var pageOffset = Math.Max(offset ?? 0, 0);

            // Scope: the accessible-org set (null = GLOBAL/unrestricted, [] = nobody). HROrAbove policy
            // → LocalHR floor (the SAME role-floored admission as users/search + the forest read) — a
            // mixed-role actor's below-HR scope covering org B must NOT widen the search into B.
            var accessibleOrgs = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);

            var term = q ?? string.Empty;

            // The two scope-bounded SQL reads (each capped + paginated; exact totals computed but the
            // overlay's two-section shape carries the capped lists only).
            var (unitHits, _) = await unitRepo.SearchUnitsAsync(term, accessibleOrgs, pageLimit, pageOffset, ct);
            var (peopleHits, _) = await approvalRepo.SearchPeopleForOverlayAsync(term, accessibleOrgs, pageLimit, pageOffset, ct);

            // Cheap in-memory lookups for the PATH build (units ≪ people; orgs are few) — exactly the
            // forest read's approach. The matched rows are ALREADY scope-filtered; a matched unit's
            // ancestors + a matched person's home-unit chain are in the SAME (accessible) Organisation,
            // so these global maps leak nothing (we only ever index by an admitted org/unit).
            var orgNames = (await orgRepo.GetAllAsync(ct))
                .ToDictionary(o => o.OrgId, o => o.OrgName, StringComparer.Ordinal);
            var unitsById = (await unitRepo.ListAllActiveAsync(ct))
                .ToDictionary(u => u.UnitId, u => u);

            string OrgName(string orgId) => orgNames.TryGetValue(orgId, out var n) ? n : orgId;

            // The ancestor chain of unit names from the unit identified by `startUnitId` UP to a
            // top-level unit, returned ROOT-first (top-level → … → the start unit). A depth backstop
            // guards against a malformed cycle (the cycle guard prevents real ones).
            List<string> UnitNameChain(Guid? startUnitId)
            {
                var chain = new List<string>();
                var pid = startUnitId;
                var guard = 0;
                while (pid is Guid p && unitsById.TryGetValue(p, out var u) && guard++ < 64)
                {
                    chain.Add(u.Name);
                    pid = u.ParentUnitId;
                }
                chain.Reverse();
                return chain;
            }

            // A unit's PATH excludes its OWN name (shown separately): [OrgName, ...ancestor unit names].
            var units = unitHits.Select(u =>
            {
                var path = new List<string> { OrgName(u.OrganisationId) };
                path.AddRange(UnitNameChain(u.ParentUnitId)); // ancestors above the unit
                return new UnitSearchResult(
                    UnitId: u.UnitId,
                    OrganisationId: u.OrganisationId,
                    Type: u.Type,
                    Name: u.Name,
                    Path: path);
            }).ToList();

            // A person's PATH is [OrgName, ...home-unit chain down to + including the home unit] (the
            // unit chain is their container; their displayName is the leaf). UnitName = the home-unit leaf.
            var people = peopleHits.Select(pp =>
            {
                var unitChain = UnitNameChain(pp.UnitId); // includes the home unit (if any)
                var path = new List<string> { OrgName(pp.PrimaryOrgId) };
                path.AddRange(unitChain);
                var unitName = pp.UnitId is Guid uid && unitsById.TryGetValue(uid, out var hu) ? hu.Name : null;
                return new PersonSearchResult(
                    UserId: pp.UserId,
                    // S107 / TASK-10704 — surface the person's primary Organisation id so the merged-admin
                    // FE filters search people by the Afgrænsning scope SET (NOT fragile path text). Already
                    // carried on OverlayPersonRow.PrimaryOrgId (the PATH build uses it); just propagate.
                    OrganisationId: pp.PrimaryOrgId,
                    DisplayName: pp.DisplayName,
                    Position: pp.Position,
                    UnitName: unitName,
                    Path: path);
            }).ToList();

            return Results.Ok(new SearchResponse(units, people));
        }).RequireAuthorization("HROrAbove");

        return app;
    }

    // ── Helper Methods ──

    // S99/TASK-9900: server-side org-id generation for the name-only Create dialog.
    // Format: "ORG" + the first 8 hex chars of a GUID, uppercased (e.g. "ORG3F9A1C7D").
    // The id-generation POLICY lives in the backend (NOT the FE). 8 hex chars give ~4.3e9
    // of entropy → collisions are vanishingly rare; the create handler additionally
    // pre-checks existence and retries on a 23505 unique-violation. The owner can revisit
    // this format later (e.g. a sequence or a name-derived slug) without an FE change.
    private static string GenerateOrgId()
        => "ORG" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

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

    // S101/TASK-10101: named record (OrgListItem) replaces the anonymous shape — BYTE-IDENTICAL
    // wire JSON (camelCase via JsonSerializerDefaults.Web). The members mirror the prior anonymous
    // fields exactly. okVersion is the S76b additive read field (the unified-editor create flow
    // needs CreateUserRequest.OkVersion; consumers ignore unknown fields).
    private static OrgListItem MapOrgResponse(StatsTid.SharedKernel.Models.Organization org) => new(
        OrgId: org.OrgId,
        OrgName: org.OrgName,
        OrgType: org.OrgType,
        ParentOrgId: org.ParentOrgId,
        MaterializedPath: org.MaterializedPath,
        AgreementCode: org.AgreementCode,
        OkVersion: org.OkVersion);

    // ── Request DTOs (co-located) ──

    private sealed class CreateOrganizationRequest
    {
        // S99/TASK-9900: OrgId/AgreementCode/OkVersion are OPTIONAL so the redesigned
        // Organisation page's name-only Create dialog (per design_handoff_organisation)
        // works. The backend generates a stable OrgId and defaults the (vestigial)
        // agreement/ok when these are null/blank. An explicit value still works
        // (backward-compatible). OrgName + OrgType remain required; ParentOrgId follows
        // the existing MAO-root / ORGANISATION-needs-MAO-parent validation.
        public string? OrgId { get; init; }
        public required string OrgName { get; init; }
        public required string OrgType { get; init; }
        public string? ParentOrgId { get; init; }
        public string? AgreementCode { get; init; }
        public string? OkVersion { get; init; }
    }

    private sealed class UpdateOrganizationRequest
    {
        public string? OrgName { get; init; }
        public string? AgreementCode { get; init; }
        public string? OkVersion { get; init; }
    }

    // S98 / ADR-035 — org re-parent request. The target MUST be an active MAO (validated in-handler);
    // the moved org MUST be an ORGANISATION (a MAO is a root). NO version/If-Match (no version column).
    private sealed class MoveOrganizationRequest
    {
        public required string NewParentOrgId { get; init; }
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

        /// <summary>
        /// S104 / ADR-038 D8 (Enhedsspor) — the target structural unit on a CROSS-Organisation
        /// transfer (interpreted ONLY when <see cref="PrimaryOrgId"/> changes — a same-Organisation
        /// unit-change goes through <c>PUT /api/admin/users/{id}/unit</c>, TASK-10403). On a transfer
        /// the user's old <c>unit_id</c> (in the OLD Organisation) is always reset: <c>null</c> homes
        /// them directly at the new Organisation; a non-null unit must be ACTIVE + belong to the NEW
        /// <see cref="PrimaryOrgId"/>. The derived <c>primary_org_id</c> equals the new Organisation
        /// either way.
        /// </summary>
        public Guid? UnitId { get; init; }
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
