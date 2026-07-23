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

            // S120 / TASK-12000 — named record (BYTE-IDENTICAL wire JSON; the 201 receipt).
            return Results.Created($"/api/time-entries/{request.EmployeeId}", new TimeEntryCreatedResponse(
                EventId: @event.EventId,
                StreamId: streamId));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<TimeEntryCreatedResponse>(StatusCodes.Status201Created); // S120 / TASK-12000

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
        }).RequireAuthorization("EmployeeOrAbove")
        // S120 / TASK-12000 — the NAMED SharedKernel model IS the wire shape (the handler
        // serializes TimeEntry instances directly — PAT-012 named-model rule; a BARE ARRAY).
        .Produces<IEnumerable<TimeEntry>>(StatusCodes.Status200OK);

        // ── Absences ──
        //
        // Absence WRITES are owned by the Skema save endpoint (SkemaEndpoints) per
        // ADR-032 D5 (TASK-6606a): the legacy POST /api/absences bypass — which
        // defaulted Hours to a flat 7.4 and carried an advisory OkVersion — was
        // retired so all absence registration flows through Skema consumption
        // valuation. The GET below remains the read surface
        // (WeeklyCalculationPipeline consumes it).

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
        }).RequireAuthorization("EmployeeOrAbove")
        // S120 / TASK-12000 — the NAMED SharedKernel model IS the wire shape (a BARE ARRAY).
        .Produces<IEnumerable<AbsenceEntry>>(StatusCodes.Status200OK);

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

            // S120 / TASK-12000 — OWNER RULING #1 (branch-normalization class, 1st instance,
            // ruled 2026-07-21): the no-history branch serves the ONE 5-member shape with the
            // 3 history members null-filled; the vestigial `message` (no reader existed) DIES.
            // The with-history branch below is BYTE-IDENTICAL to the pre-S120 wire.
            if (latest is null)
                return Results.Ok(new FlexBalanceResponse(
                    EmployeeId: employeeId,
                    Balance: 0m,
                    PreviousBalance: null,
                    Delta: null,
                    Reason: null));

            return Results.Ok(new FlexBalanceResponse(
                EmployeeId: employeeId,
                Balance: latest.NewBalance,
                PreviousBalance: latest.PreviousBalance,
                Delta: latest.Delta,
                Reason: latest.Reason));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<FlexBalanceResponse>(StatusCodes.Status200OK); // S120 / TASK-12000

        return app;
    }
}
