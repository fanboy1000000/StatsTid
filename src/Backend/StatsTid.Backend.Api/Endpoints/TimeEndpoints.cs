using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Validation;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
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

        app.MapPost("/api/time-entries", async (
            RegisterTimeEntryRequest request,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            TimeEntryProjectionRepository timeProjectionRepo,
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

            // Atomic in-tx write per ADR-018 D3 + S27 Phase 4c.6 projection design
            // (TASK-2707 — re-attempt of S26 TASK-2606 which was reverted because the
            // event-stream-backed GET could not see the just-written event before the
            // OutboxPublisher async drain). Per-event ordering inside the tx:
            //   1. outbox enqueue FIRST → returns the freshly-allocated outbox_id
            //      (TASK-2703 EnqueueAndReturnIdAsync overload).
            //   2. projection INSERT SECOND → consumes the outbox_id so the
            //      time_entries_projection row is keyed to the global outbox sequence
            //      (per-employee monotonic ordering aligned with the global outbox
            //      sequence, see TimeEntryProjectionRepository ORDER BY outbox_id ASC).
            // Both rows commit or roll back together, so the migrated GET below
            // (which reads from the projection) satisfies read-your-write.
            //
            // Stream id literal `employee-{EmployeeId}` unchanged per ADR-018 D6
            // retabulate (TASK-2601) — consolidated employee stream carrying
            // time-entry + absence + entitlement-balance + compliance events.
            var streamId = $"employee-{request.EmployeeId}";

            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                    await timeProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Results.Created($"/api/time-entries/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
        }).RequireAuthorization("EmployeeOrAbove");

        app.MapGet("/api/time-entries/{employeeId}", async (
            string employeeId,
            TimeEntryProjectionRepository timeProjectionRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
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

            // S27 Phase 4c.6 / TASK-2707: read from time_entries_projection instead of
            // replaying the event stream. The atomic POST above commits the projection
            // row in the same tx as the outbox enqueue, so reads see the just-written
            // entry without waiting for the OutboxPublisher async drain (read-your-write).
            // Full-stream read (no date filter) — uses idx_time_entries_proj_emp_outbox.
            var rows = await timeProjectionRepo.GetByEmployeeAsync(employeeId, ct);

            var entries = rows.Select(r => new TimeEntry
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
                RegisteredAt = r.OccurredAt
            }).ToList();

            return Results.Ok(entries);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Absences ──

        app.MapPost("/api/absences", async (
            RegisterAbsenceRequest request,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            AbsenceProjectionRepository absenceProjectionRepo,
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

            // Atomic in-tx write per ADR-018 D3 + S27 Phase 4c.6 projection design
            // (TASK-2707). Per-event ordering inside the tx:
            //   1. outbox enqueue FIRST → returns the freshly-allocated outbox_id.
            //   2. projection INSERT SECOND → consumes the outbox_id.
            // Both rows commit or roll back together; the migrated GET below
            // (which reads from absences_projection) satisfies read-your-write.
            //
            // Stream id literal `employee-{EmployeeId}` unchanged per ADR-018 D6
            // retabulate (TASK-2601) — consolidated employee stream.
            var streamId = $"employee-{request.EmployeeId}";

            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                    await absenceProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Results.Created($"/api/absences/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
        }).RequireAuthorization("EmployeeOrAbove");

        app.MapGet("/api/absences/{employeeId}", async (
            string employeeId,
            AbsenceProjectionRepository absenceProjectionRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
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

            // S27 Phase 4c.6 / TASK-2707: read from absences_projection instead of
            // replaying the event stream. Atomic POST above commits the projection
            // row in the same tx as the outbox enqueue (read-your-write).
            // Full-stream read (no date filter) — uses idx_absences_proj_emp_outbox.
            var rows = await absenceProjectionRepo.GetByEmployeeAsync(employeeId, ct);

            var absences = rows.Select(r => new AbsenceEntry
            {
                EmployeeId = r.EmployeeId,
                Date = r.Date,
                AbsenceType = r.AbsenceType,
                Hours = r.Hours,
                AgreementCode = r.AgreementCode,
                OkVersion = r.OkVersion
            }).ToList();

            return Results.Ok(absences);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Flex Balance ──
        //
        // OUT OF SCOPE for S27 Phase 4c.6 / TASK-2707 (per refinement Assumption #4).
        // FlexBalanceUpdated is NOT yet projected to a read-model table; this handler
        // continues to read from the event stream. Phase 4d / 4e will introduce a
        // flex_balance_projection if read-your-write is required for this endpoint.

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
