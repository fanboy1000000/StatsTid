using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Validation;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class TimeEndpoints
{
    public static WebApplication MapTimeEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.TimeEndpoints");

        // ── Time Entries ──

        app.MapPost("/api/time-entries", async (RegisterTimeEntryRequest request, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Forbid();

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // OK version MUST be resolved server-side from the entry date (ADR-003).
            // The caller-supplied value is advisory only; mismatches are logged but not rejected.
            var resolvedOkVersion = OkVersionResolver.ResolveVersion(request.Date);
#pragma warning disable CS0618 // RegisterTimeEntryRequest.OkVersion is intentionally obsolete/advisory
            var suppliedOkVersion = request.OkVersion;
#pragma warning restore CS0618
            if (!string.Equals(suppliedOkVersion, resolvedOkVersion, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Caller-supplied OkVersion '{Supplied}' differs from server-resolved '{Resolved}' for time entry on {Date} (employee {EmployeeId}). Using resolved value.",
                    suppliedOkVersion, resolvedOkVersion, request.Date, request.EmployeeId);
            }

            var (isValid, error) = RequestValidator.ValidateTimeEntry(request.EmployeeId, request.Hours, request.AgreementCode, resolvedOkVersion);
            if (!isValid)
                return Results.BadRequest(new { error });

            var @event = new TimeEntryRegistered
            {
                EmployeeId = request.EmployeeId,
                Date = request.Date,
                Hours = request.Hours,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                TaskId = request.TaskId,
                ActivityType = request.ActivityType,
                AgreementCode = request.AgreementCode,
                OkVersion = resolvedOkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };

            var streamId = $"employee-{request.EmployeeId}";
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Created($"/api/time-entries/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
        }).RequireAuthorization("EmployeeOrAbove");

        app.MapGet("/api/time-entries/{employeeId}", async (string employeeId, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Ownership check for Employee role
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var streamId = $"employee-{employeeId}";
            var events = await eventStore.ReadStreamAsync(streamId, ct);

            var entries = events.OfType<TimeEntryRegistered>().Select(e => new TimeEntry
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
                RegisteredAt = e.OccurredAt
            }).ToList();

            return Results.Ok(entries);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Absences ──

        app.MapPost("/api/absences", async (RegisterAbsenceRequest request, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Forbid();

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Absence registration design decision (TASK-1801):
            // The RegisterAbsenceRequest carries a single `Date` (not a range), so OK-version
            // resolution is naturally per-day. If future callers introduce multi-day absence
            // registration that straddles an OK transition, the caller must split the request
            // (one per OK version) — this mirrors the retroactive-correction split pattern
            // (ADR-013) and keeps every persisted AbsenceRegistered event unambiguous.
            var resolvedOkVersion = OkVersionResolver.ResolveVersion(request.Date);
#pragma warning disable CS0618 // RegisterAbsenceRequest.OkVersion is intentionally obsolete/advisory
            var suppliedOkVersion = request.OkVersion;
#pragma warning restore CS0618
            if (!string.Equals(suppliedOkVersion, resolvedOkVersion, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Caller-supplied OkVersion '{Supplied}' differs from server-resolved '{Resolved}' for absence on {Date} (employee {EmployeeId}). Using resolved value.",
                    suppliedOkVersion, resolvedOkVersion, request.Date, request.EmployeeId);
            }

            var (isValid, error) = RequestValidator.ValidateAbsence(request.EmployeeId, request.Hours, request.AbsenceType, request.AgreementCode, resolvedOkVersion);
            if (!isValid)
                return Results.BadRequest(new { error });

            var @event = new AbsenceRegistered
            {
                EmployeeId = request.EmployeeId,
                Date = request.Date,
                AbsenceType = request.AbsenceType,
                Hours = request.Hours,
                AgreementCode = request.AgreementCode,
                OkVersion = resolvedOkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };

            var streamId = $"employee-{request.EmployeeId}";
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Created($"/api/absences/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
        }).RequireAuthorization("EmployeeOrAbove");

        app.MapGet("/api/absences/{employeeId}", async (string employeeId, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var streamId = $"employee-{employeeId}";
            var events = await eventStore.ReadStreamAsync(streamId, ct);

            var absences = events.OfType<AbsenceRegistered>().Select(e => new AbsenceEntry
            {
                EmployeeId = e.EmployeeId,
                Date = e.Date,
                AbsenceType = e.AbsenceType,
                Hours = e.Hours,
                AgreementCode = e.AgreementCode,
                OkVersion = e.OkVersion
            }).ToList();

            return Results.Ok(absences);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Flex Balance ──

        app.MapGet("/api/flex-balance/{employeeId}", async (string employeeId, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var streamId = $"employee-{employeeId}";
            var events = await eventStore.ReadStreamAsync(streamId, ct);

            var latest = events.OfType<FlexBalanceUpdated>().LastOrDefault();

            if (latest is null)
                return Results.Ok(new { employeeId, balance = 0m, message = "No flex balance events found" });

            return Results.Ok(new
            {
                employeeId,
                balance = latest.NewBalance,
                previousBalance = latest.PreviousBalance,
                delta = latest.Delta,
                reason = latest.Reason
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Calculation (composite) ──

        app.MapPost("/api/time-entries/calculate", async (
            CalculateRequest request,
            IEventStore eventStore,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Forbid();

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Resolve OK version from PeriodStart (ADR-003). The caller-supplied value is advisory.
            // Note: for calculation periods that straddle an OK transition, the retroactive-split
            // flow (RetroactiveCorrectionService) is the correct path — see ADR-013.
            var resolvedOkVersion = OkVersionResolver.ResolveVersion(request.PeriodStart);
#pragma warning disable CS0618 // CalculateRequest.OkVersion is intentionally obsolete/advisory
            var suppliedOkVersion = request.OkVersion;
#pragma warning restore CS0618
            if (!string.Equals(suppliedOkVersion, resolvedOkVersion, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Caller-supplied OkVersion '{Supplied}' differs from server-resolved '{Resolved}' for calculate request on period starting {PeriodStart} (employee {EmployeeId}). Using resolved value.",
                    suppliedOkVersion, resolvedOkVersion, request.PeriodStart, request.EmployeeId);
            }

            var (isValid, error) = RequestValidator.ValidateTimeEntry(request.EmployeeId, request.WeeklyNormHours, request.AgreementCode, resolvedOkVersion);
            if (!isValid)
                return Results.BadRequest(new { error });

            if (request.PartTimeFraction is > 0 and <= 1)
            {
                // valid
            }
            else
            {
                var (ptValid, ptError) = RequestValidator.ValidatePartTimeFraction(request.PartTimeFraction);
                if (!ptValid)
                    return Results.BadRequest(new { error = ptError });
            }

            var orchestratorUrl = configuration["ServiceUrls:Orchestrator"]
                ?? "http://orchestrator:8080";

            var payload = new
            {
                taskType = "rule-evaluation",
                parameters = new Dictionary<string, object>
                {
                    ["ruleId"] = "NORM_CHECK_37H",
                    ["profile"] = new EmploymentProfile
                    {
                        EmployeeId = request.EmployeeId,
                        AgreementCode = request.AgreementCode,
                        OkVersion = resolvedOkVersion,
                        WeeklyNormHours = request.WeeklyNormHours,
                        EmploymentCategory = "Standard",
                        PartTimeFraction = request.PartTimeFraction
                    },
                    ["periodStart"] = request.PeriodStart.ToString("yyyy-MM-dd"),
                    ["periodEnd"] = request.PeriodEnd.ToString("yyyy-MM-dd")
                }
            };

            var client = httpClientFactory.CreateClient();
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null)
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", actor.CorrelationId.ToString());

            var response = await client.PostAsJsonAsync($"{orchestratorUrl}/api/orchestrator/execute", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            return response.IsSuccessStatusCode
                ? Results.Ok(JsonSerializer.Deserialize<object>(body))
                : Results.UnprocessableEntity(JsonSerializer.Deserialize<object>(body));
        }).RequireAuthorization("EmployeeOrAbove");

        app.MapPost("/api/time-entries/calculate-week", async (
            WeeklyCalculateRequest request,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Forbid();

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Resolve OK version from WeekStartDate (ADR-003). The caller-supplied value is advisory.
            var resolvedOkVersion = OkVersionResolver.ResolveVersion(request.WeekStartDate);
#pragma warning disable CS0618 // WeeklyCalculateRequest.OkVersion is intentionally obsolete/advisory
            var suppliedOkVersion = request.OkVersion;
#pragma warning restore CS0618
            if (!string.Equals(suppliedOkVersion, resolvedOkVersion, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Caller-supplied OkVersion '{Supplied}' differs from server-resolved '{Resolved}' for weekly calculate on week starting {WeekStart} (employee {EmployeeId}). Using resolved value.",
                    suppliedOkVersion, resolvedOkVersion, request.WeekStartDate, request.EmployeeId);
            }

            var orchestratorUrl = configuration["ServiceUrls:Orchestrator"]
                ?? "http://orchestrator:8080";

            var weekEnd = request.WeekStartDate.AddDays(6);

            var payload = new
            {
                taskType = "weekly-calculation",
                parameters = new Dictionary<string, object>
                {
                    ["employeeId"] = request.EmployeeId,
                    ["agreementCode"] = request.AgreementCode,
                    ["okVersion"] = resolvedOkVersion,
                    ["periodStart"] = request.WeekStartDate.ToString("yyyy-MM-dd"),
                    ["periodEnd"] = weekEnd.ToString("yyyy-MM-dd"),
                    ["weeklyNormHours"] = request.WeeklyNormHours,
                    ["partTimeFraction"] = request.PartTimeFraction,
                    ["previousFlexBalance"] = request.PreviousFlexBalance
                }
            };

            var client = httpClientFactory.CreateClient();
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null)
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", actor.CorrelationId.ToString());

            var response = await client.PostAsJsonAsync($"{orchestratorUrl}/api/orchestrator/execute", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            return response.IsSuccessStatusCode
                ? Results.Ok(JsonSerializer.Deserialize<object>(body))
                : Results.UnprocessableEntity(JsonSerializer.Deserialize<object>(body));
        }).RequireAuthorization("EmployeeOrAbove");

        return app;
    }
}
