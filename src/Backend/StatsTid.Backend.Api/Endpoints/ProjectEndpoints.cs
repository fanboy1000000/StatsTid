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

        // ── POST / DELETE /api/projects/{orgId}/select/{projectId} — RETIRED (S147) ──
        //
        // S147 / TASK-14710 (QUAL-230 T5a; rulings R-23, R-33). The two deprecated single-row
        // project-selection routes (deprecated since S72 / TASK-7201, R14; no screen had used them
        // since the S72 R9 sweep retired ProjectPicker) are DELETED, with their repository methods
        // and ProjectSelectionResponse. Why now: the leaver rule (OQ-2) refuses every own-record write
        // a departed employee's still-valid token could make, and these routes keyed on the caller
        // (actor.ActorId) with no subject read, so the write floor had nothing to check. Both paths
        // now answer 404. The one selection writer is PUT /api/skema/{employeeId}/row-preferences,
        // which carries the floor.

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
