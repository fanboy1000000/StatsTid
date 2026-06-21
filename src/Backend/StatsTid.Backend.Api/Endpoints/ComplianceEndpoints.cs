using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Exceptions;
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
            TimeEntryProjectionRepository timeEntryProjectionRepo,
            IEmploymentProfileResolver profileResolver,
            OrgScopeValidator scopeValidator,
            DesignatedApproverAuthorizer designatedAuthorizer,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Access control
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                // S88-8801 B2 — ADDITIVE designated-approver OR-branch (mirrors the approve endpoint's
                // OR-pattern, ApprovalEndpoints:263-271). The team-overview roster is the DESIGNATED-
                // approver set, which (ADR-027 D13) admits cross-afdeling vikar/escalation approvers
                // whose org-scope does NOT cover the employee; without this branch their lazy Advarsel
                // fetch on the expandable detail row would 403 (a systematic hole masked as a transient
                // fault). org-scope stays the primary gate; the edge only ADDS access — every existing
                // caller (employee-self / HR / org-scope) is preserved.
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var hasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                        actor.ActorId!, employeeId, asOf: today, ct: ct);
                    if (!hasEdge)
                        return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
                }
            }

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            // Fetch time entries from projection (sync-in-tx with the POST that wrote them — read-your-write per ADR-018 D12)
            var timeEntryRows = await timeEntryProjectionRepo.GetByEmployeeAndDateRangeAsync(employeeId, monthStart, monthEnd, ct);
            var timeEntries = timeEntryRows
                .Select(r => new TimeEntry
                {
                    EmployeeId = r.EmployeeId,
                    Date = r.Date,
                    Hours = r.Hours,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    TaskId = r.TaskId,
                    ActivityType = r.ActivityType,
                    AgreementCode = r.AgreementCode,
                    OkVersion = r.OkVersion,
                    VoluntaryUnsocialHours = r.VoluntaryUnsocialHours,
                })
                .ToList();

            // Call Rule Engine via HTTP (PAT-005).
            // S73 / TASK-7300 (R1): the NAMED rule-engine client — BaseAddress +
            // Authorization/X-Correlation-Id forwarding are wired centrally in Program.cs
            // (RuleEngineClient / RuleEngineHeaderForwardingHandler). This was one of the
            // BARE call sites of the S73 incident (no bearer → rule engine 401 → 503 here).
            var httpClient = httpClientFactory.CreateClient(Http.RuleEngineClient.Name);
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            // ADR-023 D1+D3 cutover: resolve fully-hydrated dated profile via
            // EmploymentProfileResolver. Non-PCS rule-engine HTTP caller →
            // fail-closed on null (caller maps to 500 via existing middleware per
            // ADR-023 D3). Replaces hardcoded WeeklyNormHours=37.0m +
            // EmploymentCategory="STANDARD" defaults; dated weekly_norm_hours +
            // live-joined agreement_code/ok_version/employment_category come
            // from the resolver per ADR-023 D2 (employment_category gap is
            // Phase 4e launch-blocking).
            var profile = await profileResolver.GetByEmployeeIdAtAsync(employeeId, monthStart, ct)
                ?? throw new EmployeeProfileNotFoundException(employeeId, monthStart);

            var complianceRequest = new
            {
                profile,
                entries = timeEntries,
                periodStart = monthStart,
                periodEnd = monthEnd,
            };

            var response = await httpClient.PostAsJsonAsync(
                "/api/rules/check-compliance", complianceRequest, jsonOptions, ct);

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
