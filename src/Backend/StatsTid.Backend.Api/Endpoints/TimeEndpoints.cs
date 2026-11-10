using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class TimeEndpoints
{
    public static WebApplication MapTimeEndpoints(this WebApplication app)
    {
        // ── Time Entries ──

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

            // Reads time_entries_projection rather than replaying the event stream (S27 TASK-2707).
            // The Skema save, the one route that registers time, writes each entry's projection row
            // in the same transaction as its outbox event (ADR-018 D3), so this read sees a
            // just-saved entry without waiting for the OutboxPublisher's asynchronous drain
            // (read-your-write).
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

            // Reads absences_projection rather than replaying the event stream (S27 TASK-2707).
            // The Skema save, which owns absence writes (above), writes each absence's projection
            // row in the same transaction as its outbox event (ADR-018 D3), so this read sees a
            // just-saved absence (read-your-write).
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

        // S146 / TASK-14607 (refinement C5) — GET /api/flex-balance/{employeeId} is DELETED, with its
        // response record. It read a flex-balance event that nothing in the system ever wrote, so it
        // answered 0 for everyone. The flex balance is now the latest recorded closing of the flex
        // ledger, served (labelled with its month) by GET /api/balance/{employeeId}/summary and the
        // year overview (BalanceEndpoints), and per row by the team overview (ApprovalEndpoints).
        // S120 owner ruling #1 (the branch-normalization class) stands; its first instance — this
        // route's no-history branch — retired with the route.

        return app;
    }
}
