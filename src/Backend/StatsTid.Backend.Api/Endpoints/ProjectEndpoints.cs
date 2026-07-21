using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ProjectEndpoints
{
    public static WebApplication MapProjectEndpoints(this WebApplication app)
    {
        // ── GET /api/projects/{orgId} — List active projects for org ──

        app.MapGet("/api/projects/{orgId}", async (
            string orgId,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var projects = await projectRepo.GetByOrgAsync(orgId, ct);

            return Results.Ok(projects.Select(p => new ProjectResponse(
                ProjectId: p.ProjectId,
                ProjectCode: p.ProjectCode,
                ProjectName: p.ProjectName,
                SortOrder: p.SortOrder)));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<ProjectResponse>>(StatusCodes.Status200OK); // S119 / TASK-11900 — BARE array (unmaterialized Select)

        // ── POST /api/projects/{orgId} — Create project ──

        app.MapPost("/api/projects/{orgId}", async (
            string orgId,
            CreateProjectRequest request,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 / TASK-7600 B1: LocalAdminOrAbove writer → LocalAdmin floor (mixed-role
            // scope-leak close; a covering non-admin scope cannot satisfy this admin gate).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var project = new Project
            {
                ProjectId = Guid.NewGuid(),
                OrgId = orgId,
                ProjectCode = request.ProjectCode,
                ProjectName = request.ProjectName,
                SortOrder = request.SortOrder,
                CreatedBy = actor.ActorId ?? "system"
            };

            var projectId = await projectRepo.CreateAsync(project, ct);

            return Results.Created($"/api/projects/{orgId}/{projectId}", new ProjectResponse(
                ProjectId: projectId,
                ProjectCode: project.ProjectCode,
                ProjectName: project.ProjectName,
                SortOrder: project.SortOrder));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<ProjectResponse>(StatusCodes.Status201Created); // S119 / TASK-11900 — the list-row sibling (S112 rule)

        // ── GET /api/projects/{orgId}/available — List projects with per-employee selection flag ──

        app.MapGet("/api/projects/{orgId}/available", async (
            string orgId,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var projects = await projectRepo.GetByOrgAsync(orgId, ct);
            var selectedIds = await projectRepo.GetSelectionIdsAsync(actor.ActorId!, ct);

            return Results.Ok(projects.Select(p => new AvailableProjectResponse(
                ProjectId: p.ProjectId,
                ProjectCode: p.ProjectCode,
                ProjectName: p.ProjectName,
                SortOrder: p.SortOrder,
                Selected: selectedIds.Contains(p.ProjectId))).ToList());
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<AvailableProjectResponse>>(StatusCodes.Status200OK); // S119 / TASK-11900 — BARE array

        // ── POST /api/projects/{orgId}/select/{projectId} — Add project to employee selection ──
        //
        // DEPRECATED-BUT-LIVE (S72 / TASK-7201, SPRINT-72 R14): superseded by
        // PUT /api/skema/{employeeId}/row-preferences (the R4 full-replacement write).
        // S119 comment correction: ProjectPicker (the component this was kept for) was
        // RETIRED at the S72 TASK-7205 R9 sweep and is absent from the tree — the op has NO
        // FE consumer today. It stays live (SkemaLegacySelectionAlignmentTests exercises its
        // semantics); retirement remains a recorded future Orchestrator small-task, NOT the
        // S119 pass (typed under the S117 zero-caller precedent).
        // R14-ALIGNED to the R4 model: ProjectRepository.AddSelectionAsync now initializes
        // the user_skema_preferences container on first write and maintains DENSE sort_order
        // (append-at-end). The HTTP response contract below is byte-identical to pre-S72.

        app.MapPost("/api/projects/{orgId}/select/{projectId:guid}", async (
            string orgId,
            Guid projectId,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Validate the project belongs to this org
            var projects = await projectRepo.GetByOrgAsync(orgId, ct);
            if (!projects.Any(p => p.ProjectId == projectId))
                return Results.NotFound(new { error = "Project not found in this organization" });

            await projectRepo.AddSelectionAsync(actor.ActorId!, projectId, ct);

            return Results.Ok(new ProjectSelectionResponse(ProjectId: projectId, Selected: true));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<ProjectSelectionResponse>(StatusCodes.Status200OK); // S119 / TASK-11900 — declared body-less POST (openapi-bodyless-declared.txt); EmployeeOrAbove write BY DESIGN (self-service)

        // ── DELETE /api/projects/{orgId}/select/{projectId} — Remove project from employee selection ──
        //
        // DEPRECATED-BUT-LIVE (S72 / TASK-7201, SPRINT-72 R14): superseded by
        // PUT /api/skema/{employeeId}/row-preferences. S119 comment correction: ProjectPicker
        // (the component this was kept for) was RETIRED at the S72 TASK-7205 R9 sweep and is
        // absent from the tree — no FE consumer today; kept live (the legacy-selection
        // alignment tests exercise it), removal recorded as a future small-task. R14-ALIGNED:
        // ProjectRepository.RemoveSelectionAsync now initializes the container (a remove IS
        // a preference write — removing the LAST selection deliberately yields ZERO visible
        // rows under R4, not the legacy all-projects fallback) and dense-reindexes the
        // remaining rows. The 204 response contract is byte-identical to pre-S72.

        app.MapDelete("/api/projects/{orgId}/select/{projectId:guid}", async (
            string orgId,
            Guid projectId,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await projectRepo.RemoveSelectionAsync(actor.ActorId!, projectId, ct);

            return Results.NoContent();
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces(StatusCodes.Status204NoContent); // S119 / TASK-11900 — declared-204 body-less; EmployeeOrAbove write BY DESIGN (self-service)

        // ── PUT /api/projects/{orgId}/{projectId} — Update project ──

        app.MapPut("/api/projects/{orgId}/{projectId}", async (
            string orgId,
            Guid projectId,
            UpdateProjectRequest request,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 / TASK-7600 B1: LocalAdminOrAbove writer → LocalAdmin floor (mixed-role
            // scope-leak close; a covering non-admin scope cannot satisfy this admin gate).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await projectRepo.UpdateAsync(projectId, request.ProjectName, request.SortOrder, ct);

            return Results.Ok(new ProjectUpdateResponse(ProjectId: projectId, Updated: true));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<ProjectUpdateResponse>(StatusCodes.Status200OK); // S119 / TASK-11900

        // ── DELETE /api/projects/{orgId}/{projectId} — Soft deactivate project ──

        app.MapDelete("/api/projects/{orgId}/{projectId}", async (
            string orgId,
            Guid projectId,
            ProjectRepository projectRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 / TASK-7600 B1: LocalAdminOrAbove writer → LocalAdmin floor (mixed-role
            // scope-leak close; a covering non-admin scope cannot satisfy this admin gate).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await projectRepo.DeactivateAsync(projectId, ct);

            return Results.NoContent();
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces(StatusCodes.Status204NoContent); // S119 / TASK-11900 — declared-204 body-less

        return app;
    }

    // ── Request DTOs ──

    private sealed class CreateProjectRequest
    {
        public required string ProjectCode { get; init; }
        public required string ProjectName { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class UpdateProjectRequest
    {
        public required string ProjectName { get; init; }
        public int SortOrder { get; init; }
    }
}
