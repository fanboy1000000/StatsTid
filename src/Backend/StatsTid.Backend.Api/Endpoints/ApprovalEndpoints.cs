using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
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
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodSubmitted> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
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

            // Check if period already exists (read-only — outside the write transaction).
            var existing = await approvalRepo.GetByEmployeeAndPeriodAsync(
                request.EmployeeId, request.PeriodStart, request.PeriodEnd, ct);

            // Only DRAFT or REJECTED periods can be (re-)submitted; reject early.
            if (existing is not null && existing.Status is "SUBMITTED" or "APPROVED")
                return Results.Conflict(new { error = $"Period already exists with status {existing.Status}" });

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3 atomic-outbox shape).
            // Repo writes, audit insert and outbox enqueue commit together; the per-service
            // OutboxPublisher drains outbox_events to the canonical event store at-least-once
            // (ADR-018 D4) under its own ReadCommitted transaction with FOR UPDATE per-stream
            // serialization — replaces the prior post-commit eventStore.AppendAsync shape.
            Guid periodId;
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            if (existing is not null)
            {
                // DRAFT or REJECTED -> transition to SUBMITTED
                await approvalRepo.UpdateStatusAsync(conn, tx, existing.PeriodId, "SUBMITTED", actor.ActorId, ct: ct);
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

                periodId = await approvalRepo.CreateAsync(conn, tx, newPeriod, ct);

                // Immediately transition to SUBMITTED
                await approvalRepo.UpdateStatusAsync(conn, tx, periodId, "SUBMITTED", actor.ActorId, ct: ct);
            }

            // Write approval audit (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "SUBMITTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee, null, ct);

            // Enqueue PeriodSubmitted event in the same transaction.
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
            // S44 TASK-4413: capture outbox_id for audit_projection insert
            // (ADR-026 D2 sync-in-tx projection write — atomic with the
            // approval_periods row + outbox row per ADR-018 D3/D13).
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

            var auditUser = await userRepo.GetByIdAsync(conn, tx, @event.EmployeeId, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actor.ActorId,
                ActorPrimaryOrgId: actor.OrgId,
                CorrelationId: actor.CorrelationId,
                OccurredAt: new DateTimeOffset(@event.OccurredAt),
                ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {@event.EmployeeId} not found or inactive."));
            var auditRow = auditMapper.Map(@event, auditCtx);
            await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { periodId, status = "SUBMITTED" });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Approve Period ──

        app.MapPost("/api/approval/{periodId}/approve", async (
            Guid periodId,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            TreeSettingsRepository treeSettingsRepo,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodApproved> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
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

            // Resolve designated approver for audit trail (ADR-027 D5).
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // Derive approval_method based on who's actually approving vs who should be.
            string approvalMethod;
            if (designatedManagerId is null)
                approvalMethod = "ORG_SCOPE_FALLBACK";
            else if (actor.ActorId == designatedManagerId)
                approvalMethod = resolvedMethod!; // "ACTING_MANAGER" or "DESIGNATED_MANAGER"
            else
                approvalMethod = "ORG_SCOPE_FALLBACK"; // Actor is not the designated approver

            // S50 TASK-5007: Enforcement check.
            var treeRoot = await reportingLineRepo.ResolveTreeRootOrgIdAsync(period.OrgId, ct);
            var enforcementMode = await treeSettingsRepo.GetEnforcementModeAsync(treeRoot, ct);
            var explicitFallback = false;

            if (enforcementMode == "REQUIRED" && approvalMethod == "ORG_SCOPE_FALLBACK")
            {
                if (context.Request.Query.ContainsKey("confirmFallback") &&
                    context.Request.Query["confirmFallback"] == "true")
                {
                    explicitFallback = true;
                }
                else
                {
                    return Results.Json(new
                    {
                        error = "Enforcement enabled — designated manager required",
                        enforcementMode,
                        designatedApproverId = designatedManagerId,
                        treeRootOrgId = treeRoot,
                    }, statusCode: 428);
                }
            }

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Emit FallbackTraversalWarning if depth > 3 (ADR-027 D5).
            if (depth > 3)
            {
                var warning = new FallbackTraversalWarning
                {
                    EmployeeId = period.EmployeeId,
                    ResolvedManagerId = designatedManagerId,
                    Depth = depth,
                    TreeRootOrgId = treeRoot,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await outbox.EnqueueAsync(conn, tx, $"reporting-line-{period.EmployeeId}", warning, ct);
            }

            // Transition to APPROVED
            await approvalRepo.UpdateStatusAsync(conn, tx, periodId, "APPROVED", actor.ActorId,
                rejectionReason: null,
                designatedApproverId: designatedManagerId,
                approvalMethod: approvalMethod,
                explicitFallbackConfirmation: explicitFallback,
                ct: ct);

            // Write approval audit (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "APPROVED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader, null, ct);

            // Enqueue PeriodApproved event in the same transaction.
            var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";
            var @event = new PeriodApproved
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                ApprovedBy = actor.ActorId ?? "unknown",
                ExplicitFallbackConfirmation = explicitFallback,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            // S44 TASK-4413: capture outbox_id for audit_projection insert
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

            var auditUser = await userRepo.GetByIdAsync(conn, tx, @event.EmployeeId, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actor.ActorId,
                ActorPrimaryOrgId: actor.OrgId,
                CorrelationId: actor.CorrelationId,
                OccurredAt: new DateTimeOffset(@event.OccurredAt),
                ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {@event.EmployeeId} not found or inactive."));
            var auditRow = auditMapper.Map(@event, auditCtx);
            await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { periodId, status = "APPROVED" });
        }).RequireAuthorization("LeaderOrAbove");

        // ── Reject Period ──

        app.MapPost("/api/approval/{periodId}/reject", async (
            Guid periodId,
            RejectPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            TreeSettingsRepository treeSettingsRepo,
            OrgScopeValidator scopeValidator,
            OrganizationRepository orgRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodRejected> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
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

            // Resolve designated approver for audit trail (ADR-027 D5).
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // Derive approval_method based on who's actually rejecting vs who should be.
            string approvalMethod;
            if (designatedManagerId is null)
                approvalMethod = "ORG_SCOPE_FALLBACK";
            else if (actor.ActorId == designatedManagerId)
                approvalMethod = resolvedMethod!; // "ACTING_MANAGER" or "DESIGNATED_MANAGER"
            else
                approvalMethod = "ORG_SCOPE_FALLBACK"; // Actor is not the designated approver

            // S50 TASK-5007: Enforcement check.
            var treeRoot = await reportingLineRepo.ResolveTreeRootOrgIdAsync(period.OrgId, ct);
            var enforcementMode = await treeSettingsRepo.GetEnforcementModeAsync(treeRoot, ct);
            var explicitFallback = false;

            if (enforcementMode == "REQUIRED" && approvalMethod == "ORG_SCOPE_FALLBACK")
            {
                if (context.Request.Query.ContainsKey("confirmFallback") &&
                    context.Request.Query["confirmFallback"] == "true")
                {
                    explicitFallback = true;
                }
                else
                {
                    return Results.Json(new
                    {
                        error = "Enforcement enabled — designated manager required",
                        enforcementMode,
                        designatedApproverId = designatedManagerId,
                        treeRootOrgId = treeRoot,
                    }, statusCode: 428);
                }
            }

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Emit FallbackTraversalWarning if depth > 3 (ADR-027 D5).
            if (depth > 3)
            {
                var warning = new FallbackTraversalWarning
                {
                    EmployeeId = period.EmployeeId,
                    ResolvedManagerId = designatedManagerId,
                    Depth = depth,
                    TreeRootOrgId = treeRoot,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await outbox.EnqueueAsync(conn, tx, $"reporting-line-{period.EmployeeId}", warning, ct);
            }

            // Transition to REJECTED
            await approvalRepo.UpdateStatusAsync(conn, tx, periodId, "REJECTED", actor.ActorId,
                rejectionReason: request.Reason,
                designatedApproverId: designatedManagerId,
                approvalMethod: approvalMethod,
                explicitFallbackConfirmation: explicitFallback,
                ct: ct);

            // Write approval audit (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "REJECTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader, request.Reason, ct);

            // Enqueue PeriodRejected event in the same transaction.
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
                ExplicitFallbackConfirmation = explicitFallback,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };
            // S44 TASK-4413: capture outbox_id for audit_projection insert
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

            var auditUser = await userRepo.GetByIdAsync(conn, tx, @event.EmployeeId, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actor.ActorId,
                ActorPrimaryOrgId: actor.OrgId,
                CorrelationId: actor.CorrelationId,
                OccurredAt: new DateTimeOffset(@event.OccurredAt),
                ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {@event.EmployeeId} not found or inactive."));
            var auditRow = auditMapper.Map(@event, auditCtx);
            await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { periodId, status = "REJECTED", reason = request.Reason });
        }).RequireAuthorization("LeaderOrAbove");

        // ── Get Pending Periods ──

        app.MapGet("/api/approval/pending", async (
            [FromQuery(Name = "my-reports")] bool? myReports,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.Scopes is null || actor.Scopes.Length == 0)
                return Results.Json(new { error = "Access denied", reason = "No scopes assigned" }, statusCode: 403);

            // When my-reports=true, return only periods for employees where the actor
            // is the designated approver (ACTING-precedence), intersected with org scope.
            if (myReports == true)
            {
                var myReportPeriods = await approvalRepo.GetPendingForDesignatedReportsAsync(
                    actor.ActorId!, actor.Scopes, ct);

                var myResult = myReportPeriods.Select(p => new
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

                return Results.Ok(myResult);
            }

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
            DbConnectionFactory connectionFactory,
            TimeEntryProjectionRepository timeEntryRepo,
            AbsenceProjectionRepository absenceRepo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodEmployeeApproved> auditMapper,
            AuditProjectionRepository auditRepo,
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

            if (period.Status is not ("DRAFT" or "SUBMITTED" or "REJECTED"))
                return Results.Conflict(new { error = $"Cannot employee-approve period with status {period.Status}. Only DRAFT, SUBMITTED, or REJECTED periods can be employee-approved." });

            // ── Workday coverage validation ──
            // Before allowing employee approval, verify all expected workdays in the
            // period have at least one time entry or absence registration. This is a
            // read-only check — outside the write transaction.

            // 1. Query Danish public holidays in the period range.
            var holidays = new HashSet<DateOnly>();
            await using var holidayConn = connectionFactory.Create();
            await holidayConn.OpenAsync(ct);
            await using var holidayCmd = new NpgsqlCommand(
                "SELECT holiday_date FROM danish_public_holidays WHERE holiday_date >= @start AND holiday_date <= @end",
                holidayConn);
            holidayCmd.Parameters.AddWithValue("start", period.PeriodStart);
            holidayCmd.Parameters.AddWithValue("end", period.PeriodEnd);
            await using var holidayReader = await holidayCmd.ExecuteReaderAsync(ct);
            while (await holidayReader.ReadAsync(ct))
                holidays.Add(holidayReader.GetFieldValue<DateOnly>(0));

            // 2. Build list of expected workdays (weekdays minus public holidays).
            var expectedWorkdays = new List<DateOnly>();
            for (var d = period.PeriodStart; d <= period.PeriodEnd; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                if (holidays.Contains(d))
                    continue;
                expectedWorkdays.Add(d);
            }

            // 3. Query time entries and absences for the employee + date range.
            var timeEntries = await timeEntryRepo.GetByEmployeeAndDateRangeAsync(
                period.EmployeeId, period.PeriodStart, period.PeriodEnd, ct);
            var absences = await absenceRepo.GetByEmployeeAndDateRangeAsync(
                period.EmployeeId, period.PeriodStart, period.PeriodEnd, ct);

            // 4. Determine which workdays have at least one registration.
            var entryDates = new HashSet<DateOnly>(
                timeEntries.Select(e => e.Date));
            var absenceDates = new HashSet<DateOnly>(
                absences.Select(a => a.Date));

            var uncoveredDays = expectedWorkdays
                .Where(d => !entryDates.Contains(d) && !absenceDates.Contains(d))
                .ToList();

            // 5. Reject if any workdays are uncovered.
            if (uncoveredDays.Count > 0)
            {
                var coveredCount = expectedWorkdays.Count - uncoveredDays.Count;
                return Results.UnprocessableEntity(new
                {
                    error = "Ikke alle arbejdsdage er dækket",
                    message = "Følgende arbejdsdage mangler registreringer",
                    missingDays = uncoveredDays.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
                    coveredDays = coveredCount,
                    totalWorkdays = expectedWorkdays.Count,
                });
            }

            // Atomic state-change + deadlines + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Transition to EMPLOYEE_APPROVED
            await approvalRepo.UpdateStatusAsync(conn, tx, periodId, "EMPLOYEE_APPROVED", actor.ActorId, ct: ct);

            // Calculate and set deadlines (in-tx).
            var lastDayOfMonth = new DateOnly(period.PeriodEnd.Year, period.PeriodEnd.Month,
                DateTime.DaysInMonth(period.PeriodEnd.Year, period.PeriodEnd.Month));
            var employeeDeadline = lastDayOfMonth.AddDays(2);
            var managerDeadline = lastDayOfMonth.AddDays(5);
            await approvalRepo.UpdateDeadlinesAsync(conn, tx, periodId, employeeDeadline, managerDeadline, ct);

            // Write audit trail (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "SUBMITTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee,
                "Employee self-approval", ct);

            // Enqueue PeriodEmployeeApproved event in the same transaction.
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
            // S44 TASK-4413: capture outbox_id for audit_projection insert
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

            var auditUser = await userRepo.GetByIdAsync(conn, tx, @event.EmployeeId, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actor.ActorId,
                ActorPrimaryOrgId: actor.OrgId,
                CorrelationId: actor.CorrelationId,
                OccurredAt: new DateTimeOffset(@event.OccurredAt),
                ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {@event.EmployeeId} not found or inactive."));
            var auditRow = auditMapper.Map(@event, auditCtx);
            await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

            await tx.CommitAsync(ct);

            return Results.Ok(new { periodId, status = "EMPLOYEE_APPROVED" });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── Reopen Period ──

        app.MapPost("/api/approval/{periodId}/reopen", async (
            Guid periodId,
            ReopenPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodReopened> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
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

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Transition back to DRAFT
            await approvalRepo.UpdateStatusAsync(conn, tx, periodId, "DRAFT", actor.ActorId, ct: ct);

            // Write audit trail (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "REOPENED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.LocalLeader,
                request.Reason, ct);

            // Enqueue PeriodReopened event in the same transaction.
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
            // S44 TASK-4413: capture outbox_id for audit_projection insert
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

            var auditUser = await userRepo.GetByIdAsync(conn, tx, @event.EmployeeId, ct);
            var auditCtx = new AuditProjectionContext(
                ActorId: actor.ActorId,
                ActorPrimaryOrgId: actor.OrgId,
                CorrelationId: actor.CorrelationId,
                OccurredAt: new DateTimeOffset(@event.OccurredAt),
                ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {@event.EmployeeId} not found or inactive."));
            var auditRow = auditMapper.Map(@event, auditCtx);
            await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

            await tx.CommitAsync(ct);

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
