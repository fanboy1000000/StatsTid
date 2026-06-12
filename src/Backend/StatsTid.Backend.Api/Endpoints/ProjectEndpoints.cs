using StatsTid.Auth;
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

            return Results.Ok(projects.Select(p => new
            {
                projectId = p.ProjectId,
                projectCode = p.ProjectCode,
                projectName = p.ProjectName,
                sortOrder = p.SortOrder
            }));
        }).RequireAuthorization("EmployeeOrAbove");

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

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
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

            return Results.Created($"/api/projects/{orgId}/{projectId}", new
            {
                projectId,
                projectCode = project.ProjectCode,
                projectName = project.ProjectName,
                sortOrder = project.SortOrder
            });
        }).RequireAuthorization("LocalAdminOrAbove");

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

            return Results.Ok(projects.Select(p => new
            {
                projectId = p.ProjectId,
                projectCode = p.ProjectCode,
                projectName = p.ProjectName,
                sortOrder = p.SortOrder,
                selected = selectedIds.Contains(p.ProjectId),
            }).ToList());
        }).RequireAuthorization("EmployeeOrAbove");

        // ── POST /api/projects/{orgId}/select/{projectId} — Add project to employee selection ──
        //
        // DEPRECATED (S72 / TASK-7201, SPRINT-72 R14): superseded by
        // PUT /api/skema/{employeeId}/row-preferences (the R4 full-replacement write). Kept
        // ONLY because ProjectPicker still consumes it until TASK-7205's R9 sweep retires
        // that component; REMOVAL is a recorded Orchestrator small-task after 7205 lands.
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

            return Results.Ok(new { projectId, selected = true });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── DELETE /api/projects/{orgId}/select/{projectId} — Remove project from employee selection ──
        //
        // DEPRECATED (S72 / TASK-7201, SPRINT-72 R14): superseded by
        // PUT /api/skema/{employeeId}/row-preferences. Kept only for ProjectPicker until the
        // TASK-7205 R9 sweep; removal recorded post-7205. R14-ALIGNED:
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
        }).RequireAuthorization("EmployeeOrAbove");

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

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await projectRepo.UpdateAsync(projectId, request.ProjectName, request.SortOrder, ct);

            return Results.Ok(new { projectId, updated = true });
        }).RequireAuthorization("LocalAdminOrAbove");

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

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await projectRepo.DeactivateAsync(projectId, ct);

            return Results.NoContent();
        }).RequireAuthorization("LocalAdminOrAbove");

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
