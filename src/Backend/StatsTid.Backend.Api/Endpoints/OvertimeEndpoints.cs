using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class OvertimeEndpoints
{
    public static WebApplication MapOvertimeEndpoints(this WebApplication app)
    {
        // ── GET /api/overtime/{employeeId}/balance — Get overtime balance for employee/year ──
        app.MapGet("/api/overtime/{employeeId}/balance", async (
            string employeeId,
            int year,
            OvertimeBalanceRepository overtimeBalanceRepo,
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

            var balance = await overtimeBalanceRepo.GetByEmployeeAndYearAsync(employeeId, year, ct);
            if (balance is null)
                return Results.NotFound(new { error = "Overtime balance not found" });

            return Results.Ok(new
            {
                balanceId = balance.BalanceId,
                employeeId = balance.EmployeeId,
                agreementCode = balance.AgreementCode,
                periodYear = balance.PeriodYear,
                accumulated = balance.Accumulated,
                paidOut = balance.PaidOut,
                afspadseringUsed = balance.AfspadseringUsed,
                remaining = balance.Remaining,
                compensationModel = balance.CompensationModel,
                updatedAt = balance.UpdatedAt
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── GET /api/overtime/{employeeId}/governance — Check overtime governance via Rule Engine ──
        app.MapGet("/api/overtime/{employeeId}/governance", async (
            string employeeId,
            DateOnly periodStart,
            DateOnly periodEnd,
            decimal overtimeHours,
            bool? hasPreApproval,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
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

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // S34 / TASK-3412 — ADR-023 D2 binding cutover for agreement_code on past-period
            // governance checks. Source the period-effective agreement_code from the dated
            // repository instead of the live `user.AgreementCode` cache: Rule Engine overtime
            // governance rules differ across AC / HK / PROSA, so a past-period governance
            // query must execute against the agreement that was in effect at periodStart, not
            // today's. ADR-023 D3 (Overtime governance = graceful-fallback HTTP consumer):
            // null dated lookup falls through to `user.AgreementCode` (defensive — shouldn't
            // happen post-backfill).
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, periodStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            var ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
            var client = httpClientFactory.CreateClient();
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader.ToString());

            var payload = new
            {
                profile = new
                {
                    employeeId,
                    agreementCode,
                    okVersion = user.OkVersion,
                },
                entries = Array.Empty<object>(),
                periodStart,
                periodEnd,
                overtimeHoursInPeriod = overtimeHours,
                hasPreApproval = hasPreApproval ?? false,
            };

            var response = await client.PostAsJsonAsync(
                $"{ruleEngineUrl}/api/rules/check-overtime-governance", payload, jsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                return Results.Json(new { error = "Overtime governance check service unavailable" }, statusCode: 503);

            var result = await response.Content.ReadFromJsonAsync<ComplianceCheckResult>(jsonOptions, ct);
            return Results.Ok(result);
        }).RequireAuthorization("EmployeeOrAbove");

        // ── POST /api/overtime/pre-approval — Create pre-approval request ──
        app.MapPost("/api/overtime/pre-approval", async (
            OvertimePreApprovalRequest request,
            OvertimePreApprovalRepository preApprovalRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OvertimePreApprovalCreated> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Leaders create pre-approvals for their employees — validate scope
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, request.EmployeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (request.PeriodStart >= request.PeriodEnd)
                return Results.BadRequest(new { error = "periodStart must be before periodEnd" });

            if (request.MaxHours <= 0)
                return Results.BadRequest(new { error = "maxHours must be greater than 0" });

            var approval = new OvertimePreApproval
            {
                Id = Guid.NewGuid(),
                EmployeeId = request.EmployeeId,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                MaxHours = request.MaxHours,
                Status = "PENDING",
                Reason = request.Reason,
            };

            // Atomic in-tx repo write + outbox enqueue (ADR-018 D3, Pattern C — no audit row).
            // Replaces the prior post-commit eventStore.AppendAsync shape so a process crash
            // between the DB insert and event emission can no longer leave a created
            // pre-approval row without its corresponding OvertimePreApprovalCreated event.
            // The OutboxPublisher drains outbox_events to the canonical event store under its
            // own ReadCommitted transaction (ADR-018 D4) at-least-once.
            var evt = new OvertimePreApprovalCreated
            {
                EmployeeId = request.EmployeeId,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                MaxHours = request.MaxHours,
                Status = "PENDING",
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = Guid.NewGuid(),
            };
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    await preApprovalRepo.CreateAsync(conn, tx, approval, ct);
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx — TENANT_TARGETED, resolve employee org).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(
                        conn, tx, $"overtime-preapproval-{approval.Id}", evt, ct);
                    var auditUser = await userRepo.GetByIdAsync(conn, tx, request.EmployeeId, ct);
                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(evt.OccurredAt),
                        ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                            ?? throw new InvalidOperationException(
                                $"Audit projection: employee {request.EmployeeId} not found or inactive."));
                    var auditRow = auditMapper.Map(evt, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, evt.EventId, outboxId, evt.EventType, auditRow, auditCtx, ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Results.Created($"/api/overtime/pre-approval/{approval.Id}", new
            {
                id = approval.Id,
                employeeId = approval.EmployeeId,
                periodStart = approval.PeriodStart,
                periodEnd = approval.PeriodEnd,
                maxHours = approval.MaxHours,
                status = approval.Status,
                reason = approval.Reason,
            });
        }).RequireAuthorization("LeaderOrAbove");

        // ── GET /api/overtime/{employeeId}/pre-approvals — List pre-approvals ──
        app.MapGet("/api/overtime/{employeeId}/pre-approvals", async (
            string employeeId,
            DateOnly? periodStart,
            DateOnly? periodEnd,
            OvertimePreApprovalRepository preApprovalRepo,
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

            var approvals = await preApprovalRepo.GetByEmployeeAndPeriodAsync(
                employeeId,
                periodStart ?? new DateOnly(2000, 1, 1),
                periodEnd ?? new DateOnly(2100, 12, 31),
                ct);

            return Results.Ok(approvals.Select(a => new
            {
                id = a.Id,
                employeeId = a.EmployeeId,
                periodStart = a.PeriodStart,
                periodEnd = a.PeriodEnd,
                maxHours = a.MaxHours,
                approvedBy = a.ApprovedBy,
                approvedAt = a.ApprovedAt,
                status = a.Status,
                reason = a.Reason,
                createdAt = a.CreatedAt,
            }));
        }).RequireAuthorization("EmployeeOrAbove");

        // ── PUT /api/overtime/pre-approval/{id}/approve — Approve pre-approval ──
        app.MapPut("/api/overtime/pre-approval/{id}/approve", async (
            Guid id,
            OvertimeApprovalRequest? request,
            OvertimePreApprovalRepository preApprovalRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OvertimePreApprovalApproved> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var existing = await preApprovalRepo.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Pre-approval not found" });

            // Validate scope access to the employee
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, existing.EmployeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (existing.Status != "PENDING")
                return Results.BadRequest(new { error = $"Pre-approval is already {existing.Status}" });

            // Atomic in-tx status mutation + outbox enqueue (ADR-018 D3, Pattern C — no audit
            // row; the OvertimePreApprovalApproved event itself carries actor metadata via
            // DomainEventBase per PAT-004 and is the audit-of-record for this state change).
            // Pre-S26 the status flipped silently with no event emitted; post-S26 the status
            // change + new-event emission ride a single tx so a process crash between the
            // UPDATE and event emission cannot leave the row in APPROVED state without its
            // corresponding event in the canonical event store.
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await preApprovalRepo.UpdateStatusAsync(conn, tx, id, "APPROVED", actor.ActorId, request?.Reason, ct);
                var @event = new OvertimePreApprovalApproved
                {
                    PreApprovalId = id,
                    EmployeeId = existing.EmployeeId,
                    ApprovedBy = actor.ActorId ?? "system",
                    Reason = request?.Reason,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx — TENANT_TARGETED, resolve employee org).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"overtime-preapproval-{id}", @event, ct);
                var auditUser = await userRepo.GetByIdAsync(conn, tx, existing.EmployeeId, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                            ?? throw new InvalidOperationException(
                                $"Audit projection: employee {existing?.EmployeeId ?? "unknown"} not found or inactive."));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.Ok(new { id, status = "APPROVED", approvedBy = actor.ActorId, reason = request?.Reason });
        }).RequireAuthorization("LeaderOrAbove");

        // ── PUT /api/overtime/pre-approval/{id}/reject — Reject pre-approval ──
        app.MapPut("/api/overtime/pre-approval/{id}/reject", async (
            Guid id,
            OvertimeApprovalRequest? request,
            OvertimePreApprovalRepository preApprovalRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<OvertimePreApprovalRejected> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var existing = await preApprovalRepo.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Pre-approval not found" });

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, existing.EmployeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (existing.Status != "PENDING")
                return Results.BadRequest(new { error = $"Pre-approval is already {existing.Status}" });

            // Atomic in-tx status mutation + outbox enqueue (ADR-018 D3, Pattern C). See
            // approve handler above for rationale; symmetric shape with the rejection event.
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await preApprovalRepo.UpdateStatusAsync(conn, tx, id, "REJECTED", actor.ActorId, request?.Reason, ct);
                var @event = new OvertimePreApprovalRejected
                {
                    PreApprovalId = id,
                    EmployeeId = existing.EmployeeId,
                    RejectedBy = actor.ActorId ?? "system",
                    Reason = request?.Reason,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx — TENANT_TARGETED, resolve employee org).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"overtime-preapproval-{id}", @event, ct);
                var auditUser = await userRepo.GetByIdAsync(conn, tx, existing.EmployeeId, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                            ?? throw new InvalidOperationException(
                                $"Audit projection: employee {existing?.EmployeeId ?? "unknown"} not found or inactive."));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return Results.Ok(new { id, status = "REJECTED", reason = request?.Reason });
        }).RequireAuthorization("LeaderOrAbove");

        // ── POST /api/overtime/{employeeId}/compensate — Apply compensation (payout or afspadsering) ──
        app.MapPost("/api/overtime/{employeeId}/compensate", async (
            string employeeId,
            OvertimeCompensateRequest request,
            OvertimeBalanceRepository overtimeBalanceRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (request.Hours <= 0)
                return Results.BadRequest(new { error = "hours must be greater than 0" });

            if (request.CompensationType is not ("PAYOUT" or "AFSPADSERING"))
                return Results.BadRequest(new { error = "compensationType must be PAYOUT or AFSPADSERING" });

            // Check balance exists
            var balance = await overtimeBalanceRepo.GetByEmployeeAndYearAsync(employeeId, request.PeriodYear, ct);
            if (balance is null)
                return Results.NotFound(new { error = "Overtime balance not found for the given period year" });

            // Check sufficient remaining hours
            if (request.Hours > balance.Remaining)
                return Results.BadRequest(new { error = $"Insufficient remaining hours. Available: {balance.Remaining}" });

            // Atomic in-tx balance adjustment + outbox enqueue (ADR-018 D3, Pattern C — no
            // audit row). Replaces the prior post-commit eventStore.AppendAsync shape so a
            // process crash between the balance UPDATE and event emission can no longer leave
            // an updated balance row without its corresponding OvertimeCompensationApplied
            // event. The OutboxPublisher drains outbox_events to the canonical event store
            // under its own ReadCommitted transaction (ADR-018 D4) at-least-once.
            var evt = new OvertimeCompensationApplied
            {
                EmployeeId = employeeId,
                PeriodYear = request.PeriodYear,
                Hours = request.Hours,
                ConvertedHours = request.Hours,
                CompensationType = request.CompensationType,
                OvertimeType = "OVERTIME",
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = Guid.NewGuid(),
            };
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    if (request.CompensationType == "PAYOUT")
                    {
                        await overtimeBalanceRepo.AdjustPaidOutAsync(
                            conn, tx, employeeId, request.PeriodYear, request.Hours, ct);
                    }
                    else
                    {
                        await overtimeBalanceRepo.AdjustAfspadseringAsync(
                            conn, tx, employeeId, request.PeriodYear, request.Hours, ct);
                    }

                    await outbox.EnqueueAsync(
                        conn,
                        tx,
                        $"overtime-balance-{employeeId}-{request.PeriodYear}",
                        evt,
                        ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return Results.Ok(new
            {
                employeeId,
                periodYear = request.PeriodYear,
                hours = request.Hours,
                compensationType = request.CompensationType,
                applied = true,
            });
        }).RequireAuthorization("LeaderOrAbove");

        // ── GET /api/overtime/{employeeId}/compensation-choice — Get employee's compensation choice ──
        app.MapGet("/api/overtime/{employeeId}/compensation-choice", async (
            string employeeId,
            int periodYear,
            OvertimeBalanceRepository overtimeBalanceRepo,
            ConfigResolutionService configService,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
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

            var balance = await overtimeBalanceRepo.GetByEmployeeAndYearAsync(employeeId, periodYear, ct);

            if (balance is not null)
            {
                return Results.Ok(new
                {
                    employeeId,
                    periodYear,
                    compensationModel = balance.CompensationModel,
                    source = "balance"
                });
            }

            // Fall back to default from config
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // S34 / TASK-3412 — ADR-023 D2 binding cutover for agreement_code on past-period
            // compensation-model fallback selection. AC vs HK vs PROSA compensation rules
            // differ; sourcing the agreement that was in effect at the START of the period
            // year (Jan 1) ensures past-year queries return the correct DefaultCompensationModel
            // for that year's agreement. ADR-023 D3 graceful fallback to live cache if dated
            // lookup is null.
            var periodYearStart = new DateOnly(periodYear, 1, 1);
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, periodYearStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            var config = await configService.GetActiveConfigAsync(agreementCode, user.OkVersion, ct);
            var defaultModel = config?.DefaultCompensationModel ?? "AFSPADSERING";

            return Results.Ok(new
            {
                employeeId,
                periodYear,
                compensationModel = defaultModel,
                source = "config_default"
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── PUT /api/overtime/{employeeId}/compensation-choice — Set compensation choice ──
        app.MapPut("/api/overtime/{employeeId}/compensation-choice", async (
            string employeeId,
            CompensationChoiceRequest request,
            OvertimeBalanceRepository overtimeBalanceRepo,
            ConfigResolutionService configService,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Only own data
            if (employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employees can only set their own compensation choice" }, statusCode: 403);

            if (request.CompensationModel is not ("AFSPADSERING" or "UDBETALING"))
                return Results.BadRequest(new { error = "compensationModel must be AFSPADSERING or UDBETALING" });

            // Check if employee compensation choice is allowed
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // S34 / TASK-3412 — ADR-023 D2 binding cutover for agreement_code on past-period
            // compensation-choice config rule evaluation + new balance row creation. The
            // "EmployeeCompensationChoice enabled?" rule may differ across AC/HK/PROSA, and
            // a balance row persisted for a past period must record the agreement that was
            // effective at the START of that year (Jan 1) — not today's. ADR-023 D3 graceful
            // fallback to live cache when dated lookup returns null.
            var periodYearStart = new DateOnly(request.PeriodYear, 1, 1);
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, periodYearStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            var config = await configService.GetActiveConfigAsync(agreementCode, user.OkVersion, ct);
            if (config?.EmployeeCompensationChoice != true)
                return Results.BadRequest(new { error = "Employee compensation choice is not enabled for this agreement" });

            // Upsert the balance record with the new compensation model
            var balance = await overtimeBalanceRepo.GetByEmployeeAndYearAsync(employeeId, request.PeriodYear, ct);
            if (balance is null)
            {
                // Create a new balance record with zero values
                var newBalance = new OvertimeBalance
                {
                    BalanceId = Guid.NewGuid(),
                    EmployeeId = employeeId,
                    AgreementCode = agreementCode,
                    PeriodYear = request.PeriodYear,
                    Accumulated = 0m,
                    PaidOut = 0m,
                    AfspadseringUsed = 0m,
                    CompensationModel = request.CompensationModel,
                    UpdatedAt = DateTime.UtcNow,
                };
                await overtimeBalanceRepo.UpsertAsync(newBalance, ct);
            }
            else
            {
                var updated = new OvertimeBalance
                {
                    BalanceId = balance.BalanceId,
                    EmployeeId = balance.EmployeeId,
                    AgreementCode = balance.AgreementCode,
                    PeriodYear = balance.PeriodYear,
                    Accumulated = balance.Accumulated,
                    PaidOut = balance.PaidOut,
                    AfspadseringUsed = balance.AfspadseringUsed,
                    CompensationModel = request.CompensationModel,
                    UpdatedAt = DateTime.UtcNow,
                };
                await overtimeBalanceRepo.UpsertAsync(updated, ct);
            }

            return Results.Ok(new
            {
                employeeId,
                periodYear = request.PeriodYear,
                compensationModel = request.CompensationModel,
            });
        }).RequireAuthorization("EmployeeOrAbove");

        return app;
    }

    // ── Request DTOs ──

    public record OvertimePreApprovalRequest(
        string EmployeeId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        decimal MaxHours,
        string? Reason);

    public record OvertimeApprovalRequest(string? Reason);

    public record OvertimeCompensateRequest(
        int PeriodYear,
        decimal Hours,
        string CompensationType);

    public record CompensationChoiceRequest(
        int PeriodYear,
        string CompensationModel);
}
