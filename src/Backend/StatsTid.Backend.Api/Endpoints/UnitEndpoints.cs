using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S104 / ADR-038 D3/D8/D10 (Enhedsspor Phase 1b) — the typed <c>units</c> admin surface: create /
/// rename / move / delete, leader designate / remove, and the SAME-Organisation person unit-assign.
///
/// <para>
/// Every mutator is <c>RequireAuthorization("HROrAbove")</c> (ADR-007) + <c>LocalHR</c>-floored over
/// the unit's owning Organisation (<c>ValidateOrgAccessAsync</c>, multi-org via the scope validator;
/// the S76 per-scope floor — not weakened), takes the per-Organisation <c>unit-org-</c> advisory lock
/// + the recursive-CTE cycle guard (the S100 spine), enforces If-Match optimistic concurrency, and
/// emits its event ATOMICALLY with the write + a per-event <c>audit_projection</c> row (ADR-018 D2/D3,
/// ADR-026/PAT-004). Units carry NO scope (the LOCKED D5 boundary) — these endpoints mutate structure
/// + membership only.
/// </para>
///
/// <para>The CROSS-Organisation person unit-change is a TRANSFER and lives on the existing user PUT
/// (<c>AdminEndpoints</c> — TASK-10402), NOT here.</para>
/// </summary>
public static class UnitEndpoints
{
    // The 5 sub-Organisation unit types + their RANK (depth order). The CHILD ordering is
    // PARTIAL-RANK (ADR-038 D1, clarified S104): a child's rank must be strictly DEEPER than its
    // parent's; level-skips are ALLOWED (an `omrade` may directly parent a `team`). A top-level unit
    // (parent = the Organisation) has the implicit Organisation rank 0 — so any of the 5 types is a
    // valid top-level unit. This makes the delete re-parent-UP type-safe by construction.
    private static readonly IReadOnlyDictionary<string, int> TypeRank = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["direktion"] = 1,
        ["omrade"] = 2,
        ["kontor"] = 3,
        ["team"] = 4,
        ["enhed"] = 5,
    };

    public static WebApplication MapUnitEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════════════════════════════
        //  GET /api/admin/units?organisationId=… — list ACTIVE units for ONE Organisation.
        //  Envelope { units: [...] } (NOT a bare array — the S97/S99 fetchEnheder distinction).
        // ═══════════════════════════════════════════════════════════════════
        app.MapGet("/api/admin/units", async (
            string? organisationId,
            UnitRepository unitRepo,
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

            var rows = await unitRepo.ListActiveByOrgAsync(organisationId, ct);
            var items = rows
                .Select(r => new UnitListItem(r.UnitId, r.OrganisationId, r.ParentUnitId, r.Type, r.Name, r.Version))
                .ToList();
            return Results.Ok(new UnitListResponse(items));
        }).RequireAuthorization("HROrAbove")
        // S115 / TASK-11501 — the record was ALREADY named (S104); only this .Produces was missing.
        // UnitListItem.Type's dormant [AllowedValues] auto-emits as a spec enum on regen (S114).
        .Produces<UnitListResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════════════════════════════
        //  GET /api/admin/units/forest — S106 / TASK-10601 (ADR-038 D1/D5) the unified scoped FOREST:
        //  MAO → Organisation (from `organizations`) → direktion…enhed (from `units` beneath each
        //  visible Organisation), with per-unit + rolled-up active-member counts.
        //
        //  THE KEYSTONE (D5 / P7): units carry NO scope. The visibility set is the EXACT accessible-org
        //  ids (LocalHR-floored) from GetAccessibleOrgsAsync — null = unrestricted (GlobalAdmin). A unit
        //  node is admitted SOLELY because its parent Organisation ∈ that set; there is NO per-unit
        //  visibility predicate and NO descendant/sibling widening. MAO ancestors render as read-only
        //  context (a scoped HR sees the MAO header only for the Organisations it can reach — exactly
        //  the S98 /organizations/tree behaviour). The MAO/Organisation counts sum ONLY visible
        //  children, so a scoped HR's totals never leak a sibling Organisation's members (count
        //  non-leakage). Set-based reads (1 org list + 1 unit list + 2 GROUP BY counts) → the forest +
        //  roll-up assembled in memory (units ≪ people → no recursive CTE). HROrAbove read floor.
        // ═══════════════════════════════════════════════════════════════════
        app.MapGet("/api/admin/units/forest", async (
            OrganizationRepository orgRepo,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // D5 admission: the exact accessible-org ids (LocalHR floor). null → GlobalAdmin (all);
            // an empty set (Employee/below-floor) → an empty forest.
            var accessible = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            HashSet<string>? visible = accessible is null ? null : new HashSet<string>(accessible, StringComparer.Ordinal);
            bool OrgVisible(string id) => visible is null || visible.Contains(id);

            var allOrgs = await orgRepo.GetAllAsync(ct);
            var allUnits = await unitRepo.ListAllActiveAsync(ct);
            // S106 Step-7a (Codex P2): the user-table count GROUP BYs are bounded to the accessible
            // org set (scoped HR) so the forest read does not scan the whole population; `accessible`
            // is null for GlobalAdmin (unrestricted).
            var memberByUnit = await unitRepo.GetActiveMemberCountByUnitAsync(accessible, ct);
            var homedByOrg = await unitRepo.GetActiveOrgHomedCountByOrgAsync(accessible, ct);

            // Units bucketed by their (immutable) Organisation. We only ever assemble the sub-forest
            // for a VISIBLE Organisation — units in a non-visible Organisation are never touched.
            var unitsByOrg = allUnits
                .GroupBy(u => u.OrganisationId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // Organisations bucketed by parent MAO.
            var organisationsByParent = allOrgs
                .Where(o => string.Equals(o.OrgType, "ORGANISATION", StringComparison.Ordinal))
                .GroupBy(o => o.ParentOrgId ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var forest = new List<ForestMaoNode>();
            foreach (var mao in allOrgs.Where(o => string.Equals(o.OrgType, "MAO", StringComparison.Ordinal))
                                       .OrderBy(o => o.MaterializedPath, StringComparer.Ordinal))
            {
                var children = organisationsByParent.TryGetValue(mao.OrgId, out var kids)
                    ? kids
                    : new List<Organization>();

                var visibleOrgs = new List<ForestOrganisationNode>();
                foreach (var org in children.Where(c => OrgVisible(c.OrgId))
                                            .OrderBy(c => c.MaterializedPath, StringComparer.Ordinal))
                {
                    var orgUnits = unitsByOrg.TryGetValue(org.OrgId, out var us) ? us : new List<UnitRow>();
                    var (topUnits, unitTotal) = BuildUnitForest(orgUnits, memberByUnit);
                    var homed = homedByOrg.TryGetValue(org.OrgId, out var h) ? h : 0L;

                    visibleOrgs.Add(new ForestOrganisationNode(
                        OrgId: org.OrgId,
                        OrgName: org.OrgName,
                        OrgType: org.OrgType,
                        ParentOrgId: org.ParentOrgId,
                        MaterializedPath: org.MaterializedPath,
                        AgreementCode: org.AgreementCode,
                        OkVersion: org.OkVersion,
                        // Reconciles to the S98 employeeCount by primary_org_id: every unit member's
                        // unit lives in THIS Organisation (units.organisation_id is the derived
                        // primary_org_id), so Σ rolled-up units + org-homed-NULL == COUNT(primary_org).
                        MemberCount: unitTotal + homed,
                        DirectMemberCount: homed,
                        Units: topUnits));
                }

                // GlobalAdmin (unrestricted) sees every MAO (even childless); a scoped role only sees a
                // MAO that has ≥1 visible child Organisation. The MAO count sums ONLY visible children.
                if (visible is not null && visibleOrgs.Count == 0)
                    continue;

                forest.Add(new ForestMaoNode(
                    OrgId: mao.OrgId,
                    OrgName: mao.OrgName,
                    OrgType: mao.OrgType,
                    ParentOrgId: mao.ParentOrgId,
                    MaterializedPath: mao.MaterializedPath,
                    MemberCount: visibleOrgs.Sum(o => o.MemberCount),
                    Organisations: visibleOrgs));
            }

            return Results.Ok(new ForestResponse(forest));
        }).RequireAuthorization("HROrAbove")
        .Produces<ForestResponse>(StatusCodes.Status200OK); // S111 / TASK-11101 — envelope { forest: [...] }

        // ═══════════════════════════════════════════════════════════════════
        //  POST /api/admin/units { organisationId, parentUnitId?, type, name } — create.
        // ═══════════════════════════════════════════════════════════════════
        app.MapPost("/api/admin/units", async (
            CreateUnitRequest request,
            UnitRepository unitRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitCreated> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null || string.IsNullOrWhiteSpace(request.OrganisationId) ||
                string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "organisationId, type and name are required." });

            if (!TypeRank.ContainsKey(request.Type))
                return Results.BadRequest(new { error = $"Invalid unit type '{request.Type}'. Must be one of: {string.Join(", ", TypeRank.Keys)}." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, request.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // The owning org must be an ORGANISATION (a unit lives beneath an Organisation, not a MAO).
            var org = await orgRepo.GetByIdAsync(request.OrganisationId, ct);
            if (org is null)
                return Results.BadRequest(new { error = "Organisation not found." });
            if (!string.Equals(org.OrgType, "ORGANISATION", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "A unit must belong to an Organisation (a MAO holds no units)." });

            var unitId = Guid.NewGuid();
            var @event = new UnitCreated
            {
                UnitId = unitId,
                OrganisationId = request.OrganisationId,
                ParentUnitId = request.ParentUnitId,
                Type = request.Type,
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
                // Advisory lock FIRST so a concurrent parent-delete/move serializes before the in-tx
                // parent validation (the S100 spine).
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, request.OrganisationId, ct);

                if (request.ParentUnitId is Guid parentId)
                {
                    var parent = await unitRepo.GetActiveUnitInTxAsync(conn, tx, parentId, ct);
                    if (parent is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new { error = "The parent unit does not exist or is deleted." });
                    }
                    if (!string.Equals(parent.OrganisationId, request.OrganisationId, StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new { error = "The parent unit belongs to a different Organisation." });
                    }
                    // PARTIAL-RANK CHILD ordering: rank(child) must be strictly deeper than rank(parent).
                    if (TypeRank[request.Type] <= TypeRank[parent.Type])
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = $"A '{request.Type}' cannot sit under a '{parent.Type}' (a child's type must be deeper in the hierarchy)."
                        });
                    }
                }

                await unitRepo.ApplyUnitCreatedAsync(conn, tx, @event, ct);
                await EmitAsync(outbox, auditRepo, auditMapper, conn, tx, actor, $"unit-{unitId}", @event, ct);
                await tx.CommitAsync(ct);
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { error = "An active unit with this name already exists under this parent." });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            context.Response.Headers.ETag = "\"1\"";
            return Results.Created($"/api/admin/units/{unitId}",
                new UnitResponse(unitId, @event.OrganisationId, @event.ParentUnitId, @event.Type, @event.Name, 1L));
        }).RequireAuthorization("HROrAbove")
        // S111 / TASK-11101 — the request-side convention proof: a named Contracts/ DTO + .Accepts<T>
        // (spec≡DTO; weaker than the response-side spec≡runtime gate — web JSON is case-insensitive on input).
        .Accepts<CreateUnitRequest>("application/json")
        .Produces<UnitResponse>(StatusCodes.Status201Created);

        // ═══════════════════════════════════════════════════════════════════
        //  PUT /api/admin/units/{id} { name } (If-Match) — rename.
        // ═══════════════════════════════════════════════════════════════════
        app.MapPut("/api/admin/units/{id}", async (
            string id,
            RenameUnitRequest request,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitRenamed> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });
            if (!Guid.TryParse(id, out var unitId))
                return Results.BadRequest(new { error = "Invalid unit id." });
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var existing = await unitRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Unit not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            if (existing.Version != expectedVersion)
                return PreconditionFailed(expectedVersion, existing.Version);

            var @event = new UnitRenamed
            {
                UnitId = unitId,
                NewName = request.Name.Trim(),
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, existing.OrganisationId, ct);

                var affected = await unitRepo.ApplyUnitRenamedAsync(conn, tx, unitId, @event.NewName, expectedVersion, ct);
                if (affected == 0)
                {
                    var current = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                    await tx.RollbackAsync(ct);
                    return current is null || current.IsDeleted
                        ? Results.NotFound(new { error = "Unit not found." })
                        : PreconditionFailed(expectedVersion, current.Version);
                }

                // UnitRenamed carries no org in payload → the audit mapper reads ResolvedTargetOrgId.
                await EmitAsync(outbox, auditRepo, auditMapper, conn, tx, actor, $"unit-{unitId}", @event, ct,
                    resolvedTargetOrgId: existing.OrganisationId);
                await tx.CommitAsync(ct);
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { error = "An active unit with this name already exists under this parent." });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            var newVersion = expectedVersion + 1;
            context.Response.Headers.ETag = $"\"{newVersion}\"";
            return Results.Ok(new UnitResponse(
                unitId, existing.OrganisationId, existing.ParentUnitId, existing.Type, @event.NewName, newVersion));
        }).RequireAuthorization("HROrAbove")
        .Produces<UnitResponse>(StatusCodes.Status200OK); // S112 / TASK-11201 — already the S104 named record; declared for the spec

        // ═══════════════════════════════════════════════════════════════════
        //  PUT /api/admin/units/{id}/move { newParentUnitId|null } (If-Match) — re-parent within
        //  the same Organisation (organisation_id IMMUTABLE).
        // ═══════════════════════════════════════════════════════════════════
        app.MapPut("/api/admin/units/{id}/move", async (
            string id,
            MoveUnitRequest request,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitMoved> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null)
                return Results.BadRequest(new { error = "Request body is required." });
            if (!Guid.TryParse(id, out var unitId))
                return Results.BadRequest(new { error = "Invalid unit id." });
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var existing = await unitRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Unit not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            if (existing.Version != expectedVersion)
                return PreconditionFailed(expectedVersion, existing.Version);

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, existing.OrganisationId, ct);

                var current = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                if (current is null || current.IsDeleted)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Unit not found." });
                }

                if (request.NewParentUnitId is Guid newParentId)
                {
                    var parent = await unitRepo.GetActiveUnitInTxAsync(conn, tx, newParentId, ct);
                    if (parent is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new { error = "The target parent unit does not exist or is deleted." });
                    }
                    if (!string.Equals(parent.OrganisationId, current.OrganisationId, StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new { error = "Cannot move a unit to a different Organisation." });
                    }
                    if (TypeRank[current.Type] <= TypeRank[parent.Type])
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = $"A '{current.Type}' cannot sit under a '{parent.Type}' (a child's type must be deeper in the hierarchy)."
                        });
                    }
                    await unitRepo.GuardNoUnitCycleAsync(conn, tx, unitId, newParentId, ct);
                }

                var affected = await unitRepo.ApplyUnitMovedAsync(conn, tx, unitId, request.NewParentUnitId, expectedVersion, ct);
                if (affected == 0)
                {
                    var after = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                    await tx.RollbackAsync(ct);
                    return after is null || after.IsDeleted
                        ? Results.NotFound(new { error = "Unit not found." })
                        : PreconditionFailed(expectedVersion, after.Version);
                }

                var movedEvent = new UnitMoved
                {
                    UnitId = unitId,
                    OrganisationId = current.OrganisationId,
                    OldParentUnitId = current.ParentUnitId,
                    NewParentUnitId = request.NewParentUnitId,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await EmitAsync(outbox, auditRepo, auditMapper, conn, tx, actor, $"unit-{unitId}", movedEvent, ct);
                await tx.CommitAsync(ct);
            }
            catch (UnitCycleException cycleEx)
            {
                await tx.RollbackAsync(ct);
                return Results.UnprocessableEntity(new { error = cycleEx.Message });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            var newVersion = expectedVersion + 1;
            context.Response.Headers.ETag = $"\"{newVersion}\"";
            return Results.Ok(new UnitResponse(
                unitId, existing.OrganisationId, request.NewParentUnitId, existing.Type, existing.Name, newVersion));
        }).RequireAuthorization("HROrAbove")
        .Produces<UnitResponse>(StatusCodes.Status200OK); // S112 / TASK-11201 — already the S104 named record; declared for the spec

        // ═══════════════════════════════════════════════════════════════════
        //  DELETE /api/admin/units/{id} (If-Match) — SOFT delete + re-parent surviving children UP
        //  (per-child UnitMoved) + re-home direct members UP (per-member UserUnitChanged) + clear the
        //  unit's leader rows (per-row UnitLeaderRemoved).
        // ═══════════════════════════════════════════════════════════════════
        app.MapDelete("/api/admin/units/{id}", async (
            string id,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitDeleted> deletedMapper,
            IAuditProjectionMapper<UnitMoved> movedMapper,
            IAuditProjectionMapper<UserUnitChanged> userUnitMapper,
            IAuditProjectionMapper<UnitLeaderRemoved> leaderRemovedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (!Guid.TryParse(id, out var unitId))
                return Results.BadRequest(new { error = "Invalid unit id." });
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var existing = await unitRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Unit not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            if (existing.Version != expectedVersion)
                return PreconditionFailed(expectedVersion, existing.Version);

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, existing.OrganisationId, ct);

                var current = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                if (current is null || current.IsDeleted)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Unit not found." });
                }

                var affected = await unitRepo.ApplyUnitDeletedAsync(conn, tx, unitId, expectedVersion, ct);
                if (affected == 0)
                {
                    var after = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                    await tx.RollbackAsync(ct);
                    return after is null || after.IsDeleted
                        ? Results.NotFound(new { error = "Unit not found." })
                        : PreconditionFailed(expectedVersion, after.Version);
                }

                var org = current.OrganisationId;
                var deletedEvent = new UnitDeleted
                {
                    UnitId = unitId,
                    OrganisationId = org,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await EmitAsync(outbox, auditRepo, deletedMapper, conn, tx, actor, $"unit-{unitId}", deletedEvent, ct);

                // Re-parent surviving children UP to the deleted unit's own parent (root → roots).
                var movedChildren = await unitRepo.ReparentChildrenOnDeleteAsync(conn, tx, unitId, current.ParentUnitId, ct);
                foreach (var childId in movedChildren)
                {
                    var childMoved = new UnitMoved
                    {
                        UnitId = childId,
                        OrganisationId = org,
                        OldParentUnitId = unitId,
                        NewParentUnitId = current.ParentUnitId,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await EmitAsync(outbox, auditRepo, movedMapper, conn, tx, actor, $"unit-{childId}", childMoved, ct);
                }

                // Re-home direct members UP to the deleted unit's parent (or NULL → home at the Org).
                var rehomed = await unitRepo.RehomeMembersOnDeleteAsync(conn, tx, unitId, current.ParentUnitId, ct);
                foreach (var memberId in rehomed)
                {
                    var memberChanged = new UserUnitChanged
                    {
                        UserId = memberId,
                        OldUnitId = unitId,
                        NewUnitId = current.ParentUnitId,
                        OrganisationId = org, // primary_org_id is unchanged (same Organisation).
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await EmitAsync(outbox, auditRepo, userUnitMapper, conn, tx, actor, $"user-{memberId}", memberChanged, ct);
                }

                // Clear the unit's leader rows (the designations vanish with the unit).
                var clearedLeaders = await unitRepo.RemoveAllLeadershipForUnitAsync(conn, tx, unitId, ct);
                foreach (var leaderUserId in clearedLeaders)
                {
                    var leaderRemoved = new UnitLeaderRemoved
                    {
                        UnitId = unitId,
                        UserId = leaderUserId,
                        OrganisationId = org,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await EmitAsync(outbox, auditRepo, leaderRemovedMapper, conn, tx, actor, $"unit-{unitId}", leaderRemoved, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.NoContent();
        }).RequireAuthorization("HROrAbove")
        .Produces(StatusCodes.Status204NoContent); // S112 / TASK-11201 — declared-204 (no body, intentionally)

        // ═══════════════════════════════════════════════════════════════════
        //  POST /api/admin/units/{id}/leaders { userId } — designate a unit leader (D3).
        //  The designee MUST be a member of the unit (the member-invariant), enforced under the
        //  unit-org advisory so a concurrent member-move serializes.
        // ═══════════════════════════════════════════════════════════════════
        app.MapPost("/api/admin/units/{id}/leaders", async (
            string id,
            DesignateLeaderRequest request,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitLeaderDesignated> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null || string.IsNullOrWhiteSpace(request.UserId))
                return Results.BadRequest(new { error = "userId is required." });
            if (!Guid.TryParse(id, out var unitId))
                return Results.BadRequest(new { error = "Invalid unit id." });

            var existing = await unitRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Unit not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, existing.OrganisationId, ct);

                var current = await unitRepo.GetByIdInTxAsync(conn, tx, unitId, ct);
                if (current is null || current.IsDeleted)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Unit not found." });
                }

                // Member-invariant (D3): the designee must be an active member of THIS unit.
                var member = await unitRepo.GetUserUnitForUpdateAsync(conn, tx, request.UserId, ct);
                if (member is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = "The user does not exist or is inactive." });
                }
                if (member.UnitId != unitId)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = "A leader must be a member of the unit they lead." });
                }

                var inserted = await unitRepo.DesignateLeaderAsync(conn, tx, unitId, request.UserId, ct);
                if (inserted > 0)
                {
                    var @event = new UnitLeaderDesignated
                    {
                        UnitId = unitId,
                        UserId = request.UserId,
                        OrganisationId = current.OrganisationId,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await EmitAsync(outbox, auditRepo, auditMapper, conn, tx, actor, $"unit-{unitId}", @event, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // S112 / TASK-11201 — named record (UnitLeaderResponse) replaces the anonymous shape;
            // BYTE-IDENTICAL wire JSON (same member names/order, camelCase Web default).
            return Results.Ok(new UnitLeaderResponse(unitId, request.UserId, existing.OrganisationId));
        }).RequireAuthorization("HROrAbove")
        .Produces<UnitLeaderResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════════════════════════════
        //  DELETE /api/admin/units/{id}/leaders/{userId} — remove a unit-leader designation.
        // ═══════════════════════════════════════════════════════════════════
        app.MapDelete("/api/admin/units/{id}/leaders/{userId}", async (
            string id,
            string userId,
            UnitRepository unitRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UnitLeaderRemoved> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (!Guid.TryParse(id, out var unitId))
                return Results.BadRequest(new { error = "Invalid unit id." });

            var existing = await unitRepo.GetByIdAsync(id, ct);
            if (existing is null || existing.IsDeleted)
                return Results.NotFound(new { error = "Unit not found." });

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, existing.OrganisationId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, existing.OrganisationId, ct);

                var removed = await unitRepo.RemoveLeaderAsync(conn, tx, unitId, userId, ct);
                if (removed == 0)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "No such leader designation on this unit." });
                }

                var @event = new UnitLeaderRemoved
                {
                    UnitId = unitId,
                    UserId = userId,
                    OrganisationId = existing.OrganisationId,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await EmitAsync(outbox, auditRepo, auditMapper, conn, tx, actor, $"unit-{unitId}", @event, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.NoContent();
        }).RequireAuthorization("HROrAbove")
        .Produces(StatusCodes.Status204NoContent); // S112 / TASK-11201 — declared-204 (no body, intentionally)

        // ═══════════════════════════════════════════════════════════════════
        //  PUT /api/admin/users/{userId}/unit { unitId|null } (If-Match) — the SAME-Organisation
        //  person unit-assign. A target unit in a DIFFERENT Organisation is a TRANSFER and is rejected
        //  here (422) → it must go through PUT /api/admin/users/{id} with primaryOrgId (TASK-10402).
        // ═══════════════════════════════════════════════════════════════════
        app.MapPut("/api/admin/users/{userId}/unit", async (
            string userId,
            AssignUserUnitRequest request,
            UnitRepository unitRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory dbFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<UserUnitChanged> userUnitMapper,
            IAuditProjectionMapper<UnitLeaderRemoved> leaderRemovedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            if (request is null)
                return Results.BadRequest(new { error = "Request body is required." });
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var user = await userRepo.GetByIdAsync(userId, ct);
            if (user is null)
                return Results.NotFound(new { error = "User not found." });

            // Floor over the user's CURRENT Organisation (== the unit's Organisation in the same-Org
            // case; a cross-Org unit is rejected below).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, user.PrimaryOrgId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            long newVersion;
            await using var conn = dbFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, user.PrimaryOrgId, ct);

                var locked = await unitRepo.GetUserUnitForUpdateAsync(conn, tx, userId, ct);
                if (locked is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "User not found." });
                }
                if (locked.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return PreconditionFailed(expectedVersion, locked.Version);
                }

                // Validate the target unit (null = home at the Organisation): ACTIVE + SAME Organisation.
                if (request.UnitId is Guid targetUnitId)
                {
                    var target = await unitRepo.GetActiveUnitInTxAsync(conn, tx, targetUnitId, ct);
                    if (target is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new { error = "The target unit does not exist or is deleted." });
                    }
                    if (!string.Equals(target.OrganisationId, locked.PrimaryOrgId, StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "The target unit is in a different Organisation — use the transfer path (PUT /api/admin/users/{id} with primaryOrgId)."
                        });
                    }
                }

                var unitChanged = locked.UnitId != request.UnitId;

                // primary_org_id is UNCHANGED (same Organisation).
                var affected = await unitRepo.ApplyUserUnitChangedAsync(
                    conn, tx, userId, request.UnitId, locked.PrimaryOrgId, expectedVersion, ct);
                if (affected == 0)
                {
                    await tx.RollbackAsync(ct);
                    return PreconditionFailed(expectedVersion, locked.Version);
                }

                // Member-invariant re-sync (D3): a moved member loses any old-unit leadership.
                if (unitChanged)
                {
                    var lostLeadership = await unitRepo.RemoveAllLeadershipForUserAsync(conn, tx, userId, ct);
                    foreach (var ledUnitId in lostLeadership)
                    {
                        var leaderRemoved = new UnitLeaderRemoved
                        {
                            UnitId = ledUnitId,
                            UserId = userId,
                            OrganisationId = locked.PrimaryOrgId,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await EmitAsync(outbox, auditRepo, leaderRemovedMapper, conn, tx, actor, $"unit-{ledUnitId}", leaderRemoved, ct);
                    }
                }

                var @event = new UserUnitChanged
                {
                    UserId = userId,
                    OldUnitId = locked.UnitId,
                    NewUnitId = request.UnitId,
                    OrganisationId = locked.PrimaryOrgId,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await EmitAsync(outbox, auditRepo, userUnitMapper, conn, tx, actor, $"user-{userId}", @event, ct);

                newVersion = expectedVersion + 1;
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            context.Response.Headers.ETag = $"\"{newVersion}\"";
            // S112 / TASK-11201 — named record (UserUnitResponse) replaces the anonymous shape;
            // BYTE-IDENTICAL wire JSON (same member names/order/nullability, camelCase Web default).
            return Results.Ok(new UserUnitResponse(userId, request.UnitId, user.PrimaryOrgId, newVersion));
        }).RequireAuthorization("HROrAbove")
        .Produces<UserUnitResponse>(StatusCodes.Status200OK);

        return app;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>S106 / TASK-10601 — assembles ONE Organisation's nested unit forest with rolled-up
    /// member counts, IN MEMORY (units ≪ people → no recursive SQL CTE). Returns the TOP-LEVEL unit
    /// nodes (parent_unit_id NULL) + the Organisation's total rolled-up unit-member count (Σ the
    /// top-level nodes' rolled-up counts = Σ every unit's direct members). The depth-≤5 partial-rank
    /// ordering + the move-time cycle guard keep the tree acyclic; a defensive visited-set still
    /// bounds the walk so a (hypothetically) malformed edge can never loop.</summary>
    private static (IReadOnlyList<ForestUnitNode> Roots, long Total) BuildUnitForest(
        List<UnitRow> orgUnits, IReadOnlyDictionary<Guid, long> memberByUnit)
    {
        if (orgUnits.Count == 0)
            return (Array.Empty<ForestUnitNode>(), 0L);

        // Split top-level units (parent_unit_id NULL = directly under the Organisation) from the rest,
        // and index the children by their non-null parent id (a nullable key can't be a dictionary
        // key). Name-ordered so siblings render deterministically.
        var topLevel = new List<UnitRow>();
        var childrenByParent = new Dictionary<Guid, List<UnitRow>>();
        foreach (var u in orgUnits.OrderBy(u => u.Name, StringComparer.Ordinal))
        {
            if (u.ParentUnitId is Guid parentId)
            {
                if (!childrenByParent.TryGetValue(parentId, out var siblings))
                    childrenByParent[parentId] = siblings = new List<UnitRow>();
                siblings.Add(u);
            }
            else
            {
                topLevel.Add(u);
            }
        }

        var visited = new HashSet<Guid>();
        var roots = new List<ForestUnitNode>();
        long total = 0;
        foreach (var top in topLevel)
        {
            var node = BuildUnitNode(top, level: 1, childrenByParent, memberByUnit, visited);
            total += node.MemberCount;
            roots.Add(node);
        }
        return (roots, total);
    }

    /// <summary>S106 — recursively builds one unit node + its descendants, deriving <c>level</c>
    /// (depth, top-level unit = 1) and the rolled-up <c>MemberCount</c> (this unit's direct members +
    /// Σ descendants). The <paramref name="visited"/> set is a defensive cycle backstop only.</summary>
    private static ForestUnitNode BuildUnitNode(
        UnitRow unit, int level,
        IReadOnlyDictionary<Guid, List<UnitRow>> childrenByParent,
        IReadOnlyDictionary<Guid, long> memberByUnit,
        HashSet<Guid> visited)
    {
        visited.Add(unit.UnitId);
        var direct = memberByUnit.TryGetValue(unit.UnitId, out var d) ? d : 0L;

        var childNodes = new List<ForestUnitNode>();
        long childTotal = 0;
        if (childrenByParent.TryGetValue(unit.UnitId, out var kids))
        {
            foreach (var kid in kids)
            {
                if (!visited.Add(kid.UnitId)) // defensive: skip an already-seen unit (no real cycle exists)
                    continue;
                var childNode = BuildUnitNode(kid, level + 1, childrenByParent, memberByUnit, visited);
                childTotal += childNode.MemberCount;
                childNodes.Add(childNode);
            }
        }

        return new ForestUnitNode(
            UnitId: unit.UnitId,
            OrganisationId: unit.OrganisationId,
            ParentUnitId: unit.ParentUnitId,
            Type: unit.Type,
            Name: unit.Name,
            Level: level,
            Version: unit.Version,
            DirectMemberCount: direct,
            MemberCount: direct + childTotal,
            Children: childNodes);
    }

    private static IResult PreconditionFailed(long expectedVersion, long actualVersion) =>
        Results.Json(new
        {
            error = "Concurrency precondition failed",
            expectedVersion,
            actualVersion,
        }, statusCode: 412);

    /// <summary>Enqueues <paramref name="event"/> in the caller's tx AND writes its per-event
    /// <c>audit_projection</c> row synchronously in the same tx (ADR-018 D2/D3, ADR-026/PAT-004).
    /// <paramref name="resolvedTargetOrgId"/> is supplied only for events that carry no Organisation
    /// id in their payload (UnitRenamed); the others resolve target_org_id from the event itself.</summary>
    private static async Task EmitAsync<TEvent>(
        IOutboxEnqueue outbox,
        AuditProjectionRepository auditRepo,
        IAuditProjectionMapper<TEvent> mapper,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        ActorContext actor,
        string streamId,
        TEvent @event,
        CancellationToken ct,
        string? resolvedTargetOrgId = null)
        where TEvent : DomainEventBase
    {
        var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
        var ctx = new AuditProjectionContext(
            ActorId: actor.ActorId,
            ActorPrimaryOrgId: actor.OrgId,
            CorrelationId: actor.CorrelationId,
            OccurredAt: new DateTimeOffset(@event.OccurredAt),
            ResolvedTargetOrgId: resolvedTargetOrgId);
        var row = mapper.Map(@event, ctx);
        await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, row, ctx, ct);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  Request DTOs
// ──────────────────────────────────────────────────────────────────────────

// S111 / TASK-11101 — CreateUnitRequest moved to StatsTid.Backend.Api.Contracts (the named request-DTO
// convention for the proof mutation + .Accepts<CreateUnitRequest>). It binds here via the file's
// `using StatsTid.Backend.Api.Contracts;`.

/// <summary>PUT /api/admin/units/{id} (rename) body.</summary>
public sealed record RenameUnitRequest(string Name);

/// <summary>PUT /api/admin/units/{id}/move body. <c>NewParentUnitId</c> null = make top-level.</summary>
public sealed record MoveUnitRequest(Guid? NewParentUnitId);

/// <summary>POST /api/admin/units/{id}/leaders body.</summary>
public sealed record DesignateLeaderRequest(string UserId);

/// <summary>PUT /api/admin/users/{userId}/unit body. <c>UnitId</c> null = home directly at the
/// Organisation.</summary>
public sealed record AssignUserUnitRequest(Guid? UnitId);
