using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class TimerEndpoints
{
    public static WebApplication MapTimerEndpoints(this WebApplication app)
    {
        // ── POST /api/timer/check-in — Start timer for employee ──

        app.MapPost("/api/timer/check-in", async (
            CheckInRequest request,
            TimerSessionRepository timerRepo,
            IEventStore eventStore,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only check in self
            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only check in self" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Check no active session exists
            var existing = await timerRepo.GetActiveByEmployeeAsync(request.EmployeeId, ct);
            if (existing is not null)
                return Results.Conflict(new { error = "Active timer session already exists", sessionId = existing.SessionId });

            var now = DateTime.UtcNow;
            var session = new TimerSession
            {
                SessionId = Guid.NewGuid(),
                EmployeeId = request.EmployeeId,
                Date = DateOnly.FromDateTime(now),
                CheckInAt = now,
                IsActive = true
            };

            await timerRepo.CheckInAsync(session, ct);

            // Emit TimerCheckedIn event
            var streamId = $"timer-{request.EmployeeId}";
            var @event = new TimerCheckedIn
            {
                EmployeeId = request.EmployeeId,
                Date = session.Date,
                CheckInAt = now,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Ok(new
            {
                sessionId = session.SessionId,
                employeeId = session.EmployeeId,
                date = session.Date,
                checkInAt = session.CheckInAt,
                isActive = true
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── POST /api/timer/check-out — Stop timer for employee ──

        app.MapPost("/api/timer/check-out", async (
            CheckOutRequest request,
            TimerSessionRepository timerRepo,
            IEventStore eventStore,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only check out self
            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only check out self" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Find active session
            var session = await timerRepo.GetActiveByEmployeeAsync(request.EmployeeId, ct);
            if (session is null)
                return Results.NotFound(new { error = "No active timer session found" });

            var now = DateTime.UtcNow;
            var clockedHours = Math.Round((decimal)(now - session.CheckInAt).TotalHours, 2);

            await timerRepo.CheckOutAsync(session.SessionId, now, ct);

            // Emit TimerCheckedOut event
            var streamId = $"timer-{request.EmployeeId}";
            var @event = new TimerCheckedOut
            {
                EmployeeId = request.EmployeeId,
                Date = session.Date,
                CheckOutAt = now,
                ClockedHours = clockedHours,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Ok(new
            {
                sessionId = session.SessionId,
                employeeId = session.EmployeeId,
                date = session.Date,
                checkInAt = session.CheckInAt,
                checkOutAt = now,
                clockedHours,
                isActive = false
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── GET /api/timer/{employeeId} — Get active timer session ──

        app.MapGet("/api/timer/{employeeId}", async (
            string employeeId,
            TimerSessionRepository timerRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only view own timer
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only view own timer" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var session = await timerRepo.GetActiveByEmployeeAsync(employeeId, ct);
            if (session is null)
                return Results.Ok(new { active = false });

            return Results.Ok(new
            {
                active = true,
                sessionId = session.SessionId,
                employeeId = session.EmployeeId,
                date = session.Date,
                checkInAt = session.CheckInAt,
                isActive = session.IsActive
            });
        }).RequireAuthorization("EmployeeOrAbove");

        return app;
    }

    // ── Request DTOs ──

    private sealed class CheckInRequest
    {
        public required string EmployeeId { get; init; }
    }

    private sealed class CheckOutRequest
    {
        public required string EmployeeId { get; init; }
    }
}
