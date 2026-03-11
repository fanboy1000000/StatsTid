using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ComplianceEndpoints
{
    public static WebApplication MapComplianceEndpoints(this WebApplication app)
    {
        // ── GET /api/compliance/{employeeId}/period — Check compliance for a period ──
        app.MapGet("/api/compliance/{employeeId}/period", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IEventStore eventStore,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Access control
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            // Fetch time entries from event store
            var streamId = $"employee-{employeeId}";
            var allEvents = await eventStore.ReadStreamAsync(streamId, ct);

            var timeEntries = allEvents.OfType<TimeEntryRegistered>()
                .Where(e => e.Date >= monthStart && e.Date <= monthEnd)
                .Select(e => new TimeEntry
                {
                    EmployeeId = e.EmployeeId,
                    Date = e.Date,
                    Hours = e.Hours,
                    StartTime = e.StartTime,
                    EndTime = e.EndTime,
                    TaskId = e.TaskId,
                    ActivityType = e.ActivityType,
                    AgreementCode = e.AgreementCode,
                    OkVersion = e.OkVersion,
                    VoluntaryUnsocialHours = e.VoluntaryUnsocialHours,
                })
                .ToList();

            // Call Rule Engine via HTTP (PAT-005)
            var ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
            var httpClient = httpClientFactory.CreateClient();
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var profile = new EmploymentProfile
            {
                EmployeeId = employeeId,
                AgreementCode = user.AgreementCode,
                OkVersion = user.OkVersion,
                WeeklyNormHours = 37.0m,
                EmploymentCategory = "STANDARD",
            };

            var complianceRequest = new
            {
                profile,
                entries = timeEntries,
                periodStart = monthStart,
                periodEnd = monthEnd,
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{ruleEngineUrl}/api/rules/check-compliance", complianceRequest, jsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                return Results.Json(new { error = "Compliance check service unavailable" }, statusCode: 503);

            var result = await response.Content.ReadFromJsonAsync<ComplianceCheckResult>(jsonOptions, ct);
            return Results.Ok(result);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── GET /api/compliance/{employeeId}/compensatory-rest — Get compensatory rest entries ──
        app.MapGet("/api/compliance/{employeeId}/compensatory-rest", async (
            string employeeId,
            CompensatoryRestRepository compensatoryRestRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var entries = await compensatoryRestRepo.GetByEmployeeAsync(employeeId, ct);
            return Results.Ok(entries.Select(e => new
            {
                id = e.Id,
                employeeId = e.EmployeeId,
                sourceDate = e.SourceDate,
                compensatoryDate = e.CompensatoryDate,
                hours = e.Hours,
                status = e.Status,
                createdAt = e.CreatedAt,
            }));
        }).RequireAuthorization("EmployeeOrAbove");

        return app;
    }
}
