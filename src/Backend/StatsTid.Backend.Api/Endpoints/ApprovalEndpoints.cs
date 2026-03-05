using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static WebApplication MapApprovalEndpoints(this WebApplication app)
    {
        // ── Submit Period ──

        app.MapPost("/api/approval/submit", async (
            SubmitPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only submit own periods
            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only submit own periods" }, statusCode: 403);

            // Higher roles: validate scope covers the employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Check if period already exists
            var existing = await approvalRepo.GetByEmployeeAndPeriodAsync(
                request.EmployeeId, request.PeriodStart, request.PeriodEnd, ct);

            Guid periodId;

            if (existing is not null)
            {
                // Only DRAFT or REJECTED periods can be (re-)submitted
                if (existing.Status is "SUBMITTED" or "APPROVED")
                    return Results.Conflict(new { error = $"Period already exists with status {existing.Status}" });

                // DRAFT or REJECTED -> transition to SUBMITTED
                await approvalRepo.UpdateStatusAsync(existing.PeriodId, "SUBMITTED", actor.ActorId, ct: ct);
                periodId = existing.PeriodId;
            }
            else
            {
                // Create new period with DRAFT status, then immediately submit
                var newPeriod = new ApprovalPeriod
                {
                    PeriodId = Guid.NewGuid(),
                    EmployeeId = request.EmployeeId,
                    OrgId = request.OrgId,
                    PeriodStart = request.PeriodStart,
                    PeriodEnd = request.PeriodEnd,
                    PeriodType = request.PeriodType,
                    Status = "DRAFT",
                    AgreementCode = request.AgreementCode,
                    OkVersion = request.OkVersion
                };

                periodId = await approvalRepo.CreateAsync(newPeriod, ct);

                // Immediately transition to SUBMITTED
                await approvalRepo.UpdateStatusAsync(periodId, "SUBMITTED", actor.ActorId, ct: ct);
            }

            // Emit PeriodSubmitted event
            var streamId = $"approval-{request.EmployeeId}-{request.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodSubmitted
            {
                PeriodId = periodId,
                EmployeeId = request.EmployeeId,
                OrgId = request.OrgId,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                PeriodType = request.PeriodType,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // Write approval audit
            await approvalRepo.AppendAuditAsync(
                periodId, "SUBMITTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee, null, ct);

            return Results.Ok(new { periodId, status = "SUBMITTED" });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Approve Period ──

        app.MapPost("/api/approval/{periodId}/approve", async (
            Guid periodId,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // Both SUBMITTED (legacy) and EMPLOYEE_APPROVED (new flow) can be manager-approved
            if (period.Status is not ("SUBMITTED" or "EMPLOYEE_APPROVED"))
                return Results.Conflict(new { error = $"Cannot approve period with status {period.Status}. Only SUBMITTED or EMPLOYEE_APPROVED periods can be approved." });

            // Validate actor scope covers the period's org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Transition to APPROVED
            await approvalRepo.UpdateStatusAsync(periodId, "APPROVED", actor.ActorId, ct: ct);

            // Emit PeriodApproved event
            var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodApproved
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                ApprovedBy = actor.ActorId ?? "unknown",
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // Write approval audit
            await approvalRepo.AppendAuditAsync(
                periodId, "APPROVED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader, null, ct);

            return Results.Ok(new { periodId, status = "APPROVED" });
        }).RequireAuthorization("LeaderOrAbove");

        // ── Reject Period ──

        app.MapPost("/api/approval/{periodId}/reject", async (
            Guid periodId,
            RejectPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // Both SUBMITTED (legacy) and EMPLOYEE_APPROVED (new flow) can be rejected
            if (period.Status is not ("SUBMITTED" or "EMPLOYEE_APPROVED"))
                return Results.Conflict(new { error = $"Cannot reject period with status {period.Status}. Only SUBMITTED or EMPLOYEE_APPROVED periods can be rejected." });

            // Validate actor scope covers the period's org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Transition to REJECTED
            await approvalRepo.UpdateStatusAsync(periodId, "REJECTED", actor.ActorId, request.Reason, ct);

            // Emit PeriodRejected event
            var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodRejected
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                RejectedBy = actor.ActorId ?? "unknown",
                RejectionReason = request.Reason,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // Write approval audit
            await approvalRepo.AppendAuditAsync(
                periodId, "REJECTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader, request.Reason, ct);

            return Results.Ok(new { periodId, status = "REJECTED", reason = request.Reason });
        }).RequireAuthorization("LeaderOrAbove");

        // ── Get Pending Periods ──

        app.MapGet("/api/approval/pending", async (
            ApprovalPeriodRepository approvalRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.Scopes is null || actor.Scopes.Length == 0)
                return Results.Json(new { error = "Access denied", reason = "No scopes assigned" }, statusCode: 403);

            var allPending = new List<ApprovalPeriod>();
            var seenIds = new HashSet<Guid>();

            foreach (var scope in actor.Scopes)
            {
                IReadOnlyList<ApprovalPeriod> scopePending;

                if (scope.ScopeType == "GLOBAL")
                {
                    // GLOBAL scope: get all pending periods (use "/" as root path prefix)
                    scopePending = await approvalRepo.GetPendingByOrgPathAsync("/", ct);
                }
                else if (scope.ScopeType == "ORG_AND_DESCENDANTS" && scope.OrgId is not null)
                {
                    // Get the scope org's materialized path, then query by path prefix
                    var scopeOrg = await orgRepo.GetByIdAsync(scope.OrgId, ct);
                    if (scopeOrg is null) continue;
                    scopePending = await approvalRepo.GetPendingByOrgPathAsync(scopeOrg.MaterializedPath, ct);
                }
                else if (scope.ScopeType == "ORG_ONLY" && scope.OrgId is not null)
                {
                    // ORG_ONLY: get pending for that specific org
                    scopePending = await approvalRepo.GetPendingByOrgAsync(scope.OrgId, ct);
                }
                else
                {
                    continue;
                }

                // Deduplicate across scopes
                foreach (var period in scopePending)
                {
                    if (seenIds.Add(period.PeriodId))
                        allPending.Add(period);
                }
            }

            var result = allPending.Select(p => new
            {
                periodId = p.PeriodId,
                employeeId = p.EmployeeId,
                orgId = p.OrgId,
                periodStart = p.PeriodStart,
                periodEnd = p.PeriodEnd,
                periodType = p.PeriodType,
                status = p.Status,
                submittedAt = p.SubmittedAt
            }).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("LeaderOrAbove");

        // ── Get Employee Periods ──

        app.MapGet("/api/approval/{employeeId}", async (
            string employeeId,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee: only own periods
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only view own periods" }, statusCode: 403);

            // Higher roles: validate scope covers the employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var periods = await approvalRepo.GetByEmployeeAsync(employeeId, ct);

            var result = periods.Select(p => new
            {
                periodId = p.PeriodId,
                employeeId = p.EmployeeId,
                orgId = p.OrgId,
                periodStart = p.PeriodStart,
                periodEnd = p.PeriodEnd,
                periodType = p.PeriodType,
                status = p.Status,
                agreementCode = p.AgreementCode,
                okVersion = p.OkVersion,
                submittedAt = p.SubmittedAt,
                approvedBy = p.ApprovedBy,
                approvedAt = p.ApprovedAt,
                rejectionReason = p.RejectionReason,
                createdAt = p.CreatedAt
            }).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Employee Approve Period ──

        app.MapPost("/api/approval/{periodId}/employee-approve", async (
            Guid periodId,
            ApprovalPeriodRepository approvalRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);

            if (period is null)
            {
                // Period doesn't exist — cannot employee-approve a non-existent period
                return Results.NotFound(new { error = "Period not found" });
            }

            // Employee can only approve own periods
            if (actor.ActorRole == StatsTidRoles.Employee && period.EmployeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only approve own periods" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, period.EmployeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Only DRAFT or REJECTED periods can be employee-approved
            if (period.Status is not ("DRAFT" or "REJECTED"))
                return Results.Conflict(new { error = $"Cannot employee-approve period with status {period.Status}. Only DRAFT or REJECTED periods can be employee-approved." });

            // Transition to EMPLOYEE_APPROVED
            await approvalRepo.UpdateStatusAsync(periodId, "EMPLOYEE_APPROVED", actor.ActorId, ct: ct);

            // Calculate and set deadlines
            var lastDayOfMonth = new DateOnly(period.PeriodEnd.Year, period.PeriodEnd.Month,
                DateTime.DaysInMonth(period.PeriodEnd.Year, period.PeriodEnd.Month));
            var employeeDeadline = lastDayOfMonth.AddDays(2);
            var managerDeadline = lastDayOfMonth.AddDays(5);
            await approvalRepo.UpdateDeadlinesAsync(periodId, employeeDeadline, managerDeadline, ct);

            // Emit PeriodEmployeeApproved event
            var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodEmployeeApproved
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // Write audit trail
            await approvalRepo.AppendAuditAsync(
                periodId, "SUBMITTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee,
                "Employee self-approval", ct);

            return Results.Ok(new { periodId, status = "EMPLOYEE_APPROVED" });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Reopen Period ──

        app.MapPost("/api/approval/{periodId}/reopen", async (
            Guid periodId,
            ReopenPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // Validate actor scope covers the period's org
            var (allowed2, reason2) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
            if (!allowed2)
                return Results.Json(new { error = "Access denied", reason = reason2 }, statusCode: 403);

            // Only EMPLOYEE_APPROVED periods can be reopened (not APPROVED — once manager approves it's final)
            if (period.Status != "EMPLOYEE_APPROVED")
                return Results.Conflict(new { error = $"Cannot reopen period with status {period.Status}. Only EMPLOYEE_APPROVED periods can be reopened." });

            // Transition back to DRAFT
            await approvalRepo.UpdateStatusAsync(periodId, "DRAFT", actor.ActorId, ct: ct);

            // Emit PeriodReopened event
            var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodReopened
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                Reason = request.Reason,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // Write audit trail
            await approvalRepo.AppendAuditAsync(
                periodId, "REOPENED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader,
                request.Reason, ct);

            return Results.Ok(new { periodId, status = "DRAFT" });
        }).RequireAuthorization("LeaderOrAbove");

        return app;
    }

    // ── Request DTOs ──

    private sealed class ReopenPeriodRequest
    {
        public string? Reason { get; init; }
    }

    private sealed class SubmitPeriodRequest
    {
        public required string EmployeeId { get; init; }
        public required string OrgId { get; init; }
        public required DateOnly PeriodStart { get; init; }
        public required DateOnly PeriodEnd { get; init; }
        public required string PeriodType { get; init; }  // WEEKLY, MONTHLY
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
    }

    private sealed class RejectPeriodRequest
    {
        public required string Reason { get; init; }
    }
}
