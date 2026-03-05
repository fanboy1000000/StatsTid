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
