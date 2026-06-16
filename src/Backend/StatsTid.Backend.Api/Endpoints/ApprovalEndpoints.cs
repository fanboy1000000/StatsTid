using System.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
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
    /// <summary>
    /// Single shared tolerance for the TASK-5604 allocation-reconciliation gate.
    /// worked and allocated are both rounded to 2 decimals BEFORE comparing; at
    /// that scale the smallest real mismatch is 0.01, so a &lt; 0.005 threshold
    /// treats only pure rounding noise as balanced (7.40 vs 7.4 passes) while a
    /// genuine 0.01 mismatch blocks the approval.
    /// </summary>
    private const decimal AllocationTolerance = 0.005m;

    /// <summary>
    /// Sums work-interval hours for a day, mirroring the frontend grid calc
    /// (SkemaGrid.tsx <c>calcIntervalHours</c>): each {start,end} is parsed as a
    /// wall-clock "HH:mm" / "HH:mm:ss" string into seconds, only positive
    /// (end - start) deltas are counted, and the total is converted to hours.
    /// </summary>
    private static decimal SumIntervalHours(IReadOnlyList<WorkInterval> intervals)
    {
        long totalSec = 0;
        foreach (var iv in intervals)
        {
            if (string.IsNullOrEmpty(iv.Start) || string.IsNullOrEmpty(iv.End))
                continue;
            var startSec = ParseToSeconds(iv.Start);
            var endSec = ParseToSeconds(iv.End);
            var diff = endSec - startSec;
            if (diff > 0)
                totalSec += diff;
        }
        return totalSec / 3600m;
    }

    private static long ParseToSeconds(string hhmmss)
    {
        var parts = hhmmss.Split(':');
        long h = parts.Length > 0 ? long.Parse(parts[0]) : 0;
        long m = parts.Length > 1 ? long.Parse(parts[1]) : 0;
        long s = parts.Length > 2 ? long.Parse(parts[2]) : 0;
        return h * 3600 + m * 60 + s;
    }

    /// <summary>
    /// Derives the persisted <c>approval_method</c> for an approve/reject from the resolved
    /// designated approver: <c>ORG_SCOPE_FALLBACK</c> when no designated approver exists OR the actor is
    /// NOT that designated approver; otherwise the resolver's method (<c>ACTING_MANAGER</c> /
    /// <c>DESIGNATED_MANAGER</c>). Used at BOTH the pre-tx fast path and the in-tx authoritative
    /// re-derivation (S78 BLOCKER 2) so the two cannot drift.
    /// </summary>
    private static string DeriveApprovalMethod(string? actorId, string? designatedManagerId, string? resolvedMethod)
    {
        if (designatedManagerId is null)
            return "ORG_SCOPE_FALLBACK";
        if (actorId == designatedManagerId)
            return resolvedMethod!; // "ACTING_MANAGER" or "DESIGNATED_MANAGER"
        return "ORG_SCOPE_FALLBACK"; // Actor is not the designated approver.
    }

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
            DesignatedApproverAuthorizer designatedAuthorizer,
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
        // S78 R1 — wrap the whole body in the bounded drift-retry loop: if AcquireTreeLockForEmployeeAsync
        // (taken in-tx as the first lock-bearing statement) detects a concurrent cross-styrelse transfer
        // drifted the employee's tree-root advisory key, the attempt rolls back (no side effects — the
        // drift check precedes the conditional UPDATE and every mutation) and re-runs on a fresh tx.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // Both SUBMITTED (legacy) and EMPLOYEE_APPROVED (new flow) can be manager-approved
            if (period.Status is not ("SUBMITTED" or "EMPLOYEE_APPROVED"))
                return Results.Conflict(new { error = $"Cannot approve period with status {period.Status}. Only SUBMITTED or EMPLOYEE_APPROVED periods can be approved." });

            // Authorize: EITHER the actor's RBAC org-scope covers the period's org (existing
            // path) OR the actor holds the effective designated-approver edge for this employee
            // RIGHT NOW (S74 / ADR-027 D4 A3 expansion — the edge grants cross-afdeling
            // authority within the styrelse; asOf = today = "who may act NOW"). The edge is
            // intra-tree by construction, so ADR-027 D2 (cross-styrelse forbidden) holds.
            // S78 R1: orgScopeAllowed is hoisted so the in-tx re-eval knows whether the actor was
            // admitted by org-scope (JWT-claim-based, not re-checked in-tx) or purely by the edge.
            var (orgScopeAllowed, orgScopeReason) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
            if (!orgScopeAllowed)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                    actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                if (!hasEdge)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // Resolve designated approver for audit trail (ADR-027 D5). PRE-tx FAST PATH (the in-tx
            // re-derivation under the advisory is the AUTHORITATIVE one — S78 BLOCKER 2).
            var (preDesignatedManagerId, preResolvedMethod, _) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // S50 TASK-5007: Enforcement check (pre-tx fast path). The treeRoot is request-stable
            // (resolved from period.OrgId); the designated-approver classification is re-derived in-tx.
            var treeRoot = await reportingLineRepo.ResolveTreeRootOrgIdAsync(period.OrgId, ct);
            var enforcementMode = await treeSettingsRepo.GetEnforcementModeAsync(treeRoot, ct);
            var confirmFallbackRequested =
                context.Request.Query.ContainsKey("confirmFallback") &&
                context.Request.Query["confirmFallback"] == "true";

            // Pre-tx 428 fast path: if the pre-tx classification is already a REQUIRED-mode fallback without
            // confirmation, 428 now (no need to take the lock). The in-tx re-derivation re-checks this under
            // the advisory so a concurrent edge reassignment cannot BYPASS the gate (BLOCKER 2).
            var preApprovalMethod = DeriveApprovalMethod(actor.ActorId, preDesignatedManagerId, preResolvedMethod);
            if (enforcementMode == "REQUIRED" && preApprovalMethod == "ORG_SCOPE_FALLBACK" && !confirmFallbackRequested)
            {
                return Results.Json(new
                {
                    error = "Enforcement enabled — designated manager required",
                    enforcementMode,
                    designatedApproverId = preDesignatedManagerId,
                    treeRootOrgId = treeRoot,
                }, statusCode: 428);
            }

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // S78 R1 — IN-LOCK edge-auth re-evaluation. Take the period-employee's tree-wide advisory
            // (drift-guarded) as the FIRST lock-bearing statement, THEN re-evaluate the designated edge
            // STRICTLY AFTER the advisory is held. Because the action tx HOLDS the reporting-tree advisory
            // on the period-employee's CURRENT tree root, the KEY-SHARING revokers — reporting-line remove,
            // admin-vikar CREATE, and the employee-current-root mutators (self-/delegate create, acting
            // assign, the assign/transfer paths) — all take the SAME employee-current tree advisory (7800)
            // and so BLOCK before their commit; this re-read then observes the FROZEN committed edge state
            // → true serialization of the revoke-vs-approve race. (NAMED RESIDUAL: the admin-vikar REVOKE
            // [DELETE /…/vikar] deliberately keys on the PERSISTED manager_vikar.tree_root_org_id for
            // revoke-safety, NOT the employee-current root, so a post-transfer revoke can key on a DIFFERENT
            // tree than this approve — the approve-vs-vikar-revoke post-transfer key-mismatch residual.
            // That residual is non-corrupting: the revoke only ENDS an existing edge, and this in-tx
            // re-eval re-reads the committed manager_vikar state under ReadCommitted regardless of which key
            // either side held.) We re-check ONLY the designated edge for AUTHORITY (not org-scope:
            // ValidateOrgAccessAsync is JWT-claim-based and cannot be serialized by a DB lock — its pre-tx
            // check remains the gate). If the actor passed the pre-tx check PURELY via the edge (org-scope
            // denied), a revoke that committed before we got the lock now flips the re-eval to DENY → 403.
            await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);

            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

            // Compute asOf at action-time. Only re-check the edge for AUTHORITY when the pre-tx ORG-scope
            // gate did NOT already admit the actor (orgScopeAllowed): an org-scope-admitted approval does
            // not depend on the edge, so a revoked edge must not flip it to 403 (not the authorizing surface).
            if (!orgScopeAllowed)
            {
                var stillHasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                    actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                if (!stillHasEdge)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // S78 BLOCKER 2 — re-resolve the designated approver + re-derive the enforcement classification
            // UNDER the held advisory (the AUTHORITATIVE values for the persisted metadata + the gate). The
            // resolver opens its own connection, but ReadCommitted + the held advisory mean it observes the
            // FROZEN committed edge state (a concurrent reassignment is blocked from committing until we
            // release), so this re-derivation reflects the locked tree. This is INDEPENDENT of org-scope
            // admission: org-scope admits AUTHORITY, but the confirmFallback gate is about WHICH approver.
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct, asOf: asOf);
            var approvalMethod = DeriveApprovalMethod(actor.ActorId, designatedManagerId, resolvedMethod);
            var explicitFallback = false;

            // Re-evaluate the REQUIRED-mode gate IN-TX: if the locked state now makes this a fallback
            // approval in REQUIRED mode WITHOUT confirmFallback, return the same 428 — do NOT silently
            // approve. A concurrent edge reassignment that turned the actor into a fallback approver cannot
            // bypass the gate. We do NOT over-deny a legitimately-unchanged approval: when the actor is
            // still the designated/acting manager the method is NOT a fallback, so no 428 fires.
            if (enforcementMode == "REQUIRED" && approvalMethod == "ORG_SCOPE_FALLBACK")
            {
                if (confirmFallbackRequested)
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

            // S78 R2 — the CONDITIONAL status transition is the FIRST mutation in the tx (BEFORE the
            // FallbackTraversalWarning enqueue, audit insert, and action outbox), so a concurrent
            // double-transition loser (null return = 0 rows) short-circuits to a clean 409 with NO side
            // effects. BLOCKER 1: it RETURNs the locked-in pre-update status atomically (unused here — the
            // approve event carries no previousStatus — but proves the accurate old status was captured).
            var oldStatus = await approvalRepo.TryUpdateStatusConditionalAsync(
                conn, tx, periodId, "APPROVED",
                allowedSourceStates: new[] { "SUBMITTED", "EMPLOYEE_APPROVED" },
                actorId: actor.ActorId,
                rejectionReason: null,
                designatedApproverId: designatedManagerId,
                approvalMethod: approvalMethod,
                explicitFallbackConfirmation: explicitFallback,
                ct: ct);
            if (oldStatus is null)
                return Results.Conflict(new { error = "Period status changed concurrently; refresh and retry." });

            // Emit FallbackTraversalWarning if depth > 3 (ADR-027 D5). AFTER the conditional UPDATE so a
            // 0-row loser writes no warning.
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
        })).RequireAuthorization("LeaderOrAbove"); // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync

        // ── Reject Period ──

        app.MapPost("/api/approval/{periodId}/reject", async (
            Guid periodId,
            RejectPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            DesignatedApproverAuthorizer designatedAuthorizer,
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
        // S78 R1 — bounded drift-retry wrapper (same shape as approve).
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // Both SUBMITTED (legacy) and EMPLOYEE_APPROVED (new flow) can be rejected
            if (period.Status is not ("SUBMITTED" or "EMPLOYEE_APPROVED"))
                return Results.Conflict(new { error = $"Cannot reject period with status {period.Status}. Only SUBMITTED or EMPLOYEE_APPROVED periods can be rejected." });

            // Authorize: org-scope (existing) OR the effective designated-approver edge at
            // today (S74 / ADR-027 D4 A3 — same as approve; tree-bound is structural).
            // S78 R1: orgScopeAllowed hoisted for the in-tx edge re-eval (same as approve).
            var (orgScopeAllowed, orgScopeReason) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
            if (!orgScopeAllowed)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                    actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                if (!hasEdge)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // Resolve designated approver for audit trail (ADR-027 D5). PRE-tx FAST PATH; the in-tx
            // re-derivation under the advisory is the AUTHORITATIVE one (S78 BLOCKER 2).
            var (preDesignatedManagerId, preResolvedMethod, _) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // S50 TASK-5007: Enforcement check (pre-tx fast path). treeRoot is request-stable.
            var treeRoot = await reportingLineRepo.ResolveTreeRootOrgIdAsync(period.OrgId, ct);
            var enforcementMode = await treeSettingsRepo.GetEnforcementModeAsync(treeRoot, ct);
            var confirmFallbackRequested =
                context.Request.Query.ContainsKey("confirmFallback") &&
                context.Request.Query["confirmFallback"] == "true";

            // Pre-tx 428 fast path (re-checked in-tx under the advisory — BLOCKER 2).
            var preApprovalMethod = DeriveApprovalMethod(actor.ActorId, preDesignatedManagerId, preResolvedMethod);
            if (enforcementMode == "REQUIRED" && preApprovalMethod == "ORG_SCOPE_FALLBACK" && !confirmFallbackRequested)
            {
                return Results.Json(new
                {
                    error = "Enforcement enabled — designated manager required",
                    enforcementMode,
                    designatedApproverId = preDesignatedManagerId,
                    treeRootOrgId = treeRoot,
                }, statusCode: 428);
            }

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // S78 R1 — in-lock edge-auth re-evaluation (same shape as approve): advisory FIRST, then
            // re-check the designated edge under the held lock; org-scope stays a pre-tx-only gate.
            await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);
            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!orgScopeAllowed)
            {
                var stillHasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                    actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                if (!stillHasEdge)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // S78 BLOCKER 2 — re-resolve + re-classify UNDER the held advisory (the authoritative values for
            // the persisted metadata + the 428 gate). Same rationale as approve: a concurrent reassignment
            // is blocked from committing, so the resolver observes the frozen locked tree. Independent of
            // org-scope admission — the confirmFallback gate is about WHICH approver, not authority.
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct, asOf: asOf);
            var approvalMethod = DeriveApprovalMethod(actor.ActorId, designatedManagerId, resolvedMethod);
            var explicitFallback = false;

            // Re-evaluate the REQUIRED-mode gate IN-TX (no over-denial: an unchanged designated/acting
            // approver is not a fallback, so no 428 fires).
            if (enforcementMode == "REQUIRED" && approvalMethod == "ORG_SCOPE_FALLBACK")
            {
                if (confirmFallbackRequested)
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

            // S78 R2 — the CONDITIONAL status transition is the FIRST mutation (BEFORE the warning, audit,
            // and outbox), so a null (0-row) double-transition loser short-circuits to a clean 409, no side
            // effects. BLOCKER 1: it RETURNs the locked-in pre-update status (the accurate old status).
            var oldStatus = await approvalRepo.TryUpdateStatusConditionalAsync(
                conn, tx, periodId, "REJECTED",
                allowedSourceStates: new[] { "SUBMITTED", "EMPLOYEE_APPROVED" },
                actorId: actor.ActorId,
                rejectionReason: request.Reason,
                designatedApproverId: designatedManagerId,
                approvalMethod: approvalMethod,
                explicitFallbackConfirmation: explicitFallback,
                ct: ct);
            if (oldStatus is null)
                return Results.Conflict(new { error = "Period status changed concurrently; refresh and retry." });

            // Emit FallbackTraversalWarning if depth > 3 (ADR-027 D5). AFTER the conditional UPDATE.
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
        })).RequireAuthorization("LeaderOrAbove"); // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync

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
                    submittedAt = p.SubmittedAt,
                    agreementCode = p.AgreementCode
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
                submittedAt = p.SubmittedAt,
                agreementCode = p.AgreementCode
            }).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("LeaderOrAbove");

        // ── Get Periods by Month ──

        app.MapGet("/api/approval/by-month", async (
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery(Name = "my-reports")] bool? myReports,
            ApprovalPeriodRepository approvalRepo,
            ReportingLineRepository reportingLineRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (year < 2020 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year. Must be between 2020 and 2100." });
            if (month < 1 || month > 12)
                return Results.BadRequest(new { error = "Invalid month. Must be between 1 and 12." });

            var actor = context.GetActorContext();

            if (actor.Scopes is null || actor.Scopes.Length == 0)
                return Results.Json(new { error = "Access denied", reason = "No scopes assigned" }, statusCode: 403);

            // When my-reports=true, return only periods for employees where the actor
            // is the designated approver (ACTING-precedence), intersected with org scope.
            if (myReports == true)
            {
                var myReportPeriods = await approvalRepo.GetByMonthForDesignatedReportsAsync(
                    actor.ActorId!, actor.Scopes, year, month, ct);

                var myResult = myReportPeriods.Select(p => new
                {
                    periodId = p.PeriodId,
                    employeeId = p.EmployeeId,
                    orgId = p.OrgId,
                    periodStart = p.PeriodStart,
                    periodEnd = p.PeriodEnd,
                    periodType = p.PeriodType,
                    status = p.Status,
                    submittedAt = p.SubmittedAt,
                    agreementCode = p.AgreementCode
                }).ToList();

                return Results.Ok(myResult);
            }

            var allPeriods = new List<ApprovalPeriod>();
            var seenIds = new HashSet<Guid>();

            foreach (var scope in actor.Scopes)
            {
                IReadOnlyList<ApprovalPeriod> scopePeriods;

                if (scope.ScopeType == "GLOBAL")
                {
                    // GLOBAL scope: get all periods (use "/" as root path prefix)
                    scopePeriods = await approvalRepo.GetByMonthAndOrgPathAsync("/", year, month, ct);
                }
                else if (scope.ScopeType == "ORG_AND_DESCENDANTS" && scope.OrgId is not null)
                {
                    // Get the scope org's materialized path, then query by path prefix
                    var scopeOrg = await orgRepo.GetByIdAsync(scope.OrgId, ct);
                    if (scopeOrg is null) continue;
                    scopePeriods = await approvalRepo.GetByMonthAndOrgPathAsync(scopeOrg.MaterializedPath, year, month, ct);
                }
                else if (scope.ScopeType == "ORG_ONLY" && scope.OrgId is not null)
                {
                    // ORG_ONLY: get periods for that specific org
                    scopePeriods = await approvalRepo.GetByMonthAndOrgAsync(scope.OrgId, year, month, ct);
                }
                else
                {
                    continue;
                }

                // Deduplicate across scopes
                foreach (var period in scopePeriods)
                {
                    if (seenIds.Add(period.PeriodId))
                        allPeriods.Add(period);
                }
            }

            var result = allPeriods.Select(p => new
            {
                periodId = p.PeriodId,
                employeeId = p.EmployeeId,
                orgId = p.OrgId,
                periodStart = p.PeriodStart,
                periodEnd = p.PeriodEnd,
                periodType = p.PeriodType,
                status = p.Status,
                submittedAt = p.SubmittedAt,
                agreementCode = p.AgreementCode
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
            WorkTimeProjectionRepository workTimeRepo,
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

            // ── Allocation-reconciliation gate (TASK-5604) ──
            // HARD precondition ALONGSIDE coverage: for EVERY day in the period,
            // the recorded worked hours (work_time_projection: interval hours +
            // manual_hours) must match the allocated project hours (NORMAL time
            // entries with a non-null TaskId) within rounding tolerance. This is
            // a deterministic, read-only check on projections — no events, no
            // rule-engine call (P2). Absences are excluded (not in time_entries).
            // The NORMAL + non-null-TaskId allowlist mirrors the grid's allocation
            // predicate so this backend gate and the frontend "Ikke fordelt" row
            // agree (historical activity_type='timer' and null-TaskId rows excluded).

            // worked(day): interval hours + manual_hours from work_time_projection.
            var workTimeRows = await workTimeRepo.GetByEmployeeAndDateRangeAsync(
                period.EmployeeId, period.PeriodStart, period.PeriodEnd, ct);
            var workedByDay = new Dictionary<DateOnly, decimal>();
            foreach (var row in workTimeRows)
            {
                var worked = SumIntervalHours(row.Intervals) + row.ManualHours;
                workedByDay[row.Date] = workedByDay.TryGetValue(row.Date, out var existing)
                    ? existing + worked
                    : worked;
            }

            // allocated(day): reuse the time-entry list already loaded for the
            // coverage check (no re-query); filter to NORMAL + non-null TaskId.
            var allocatedByDay = new Dictionary<DateOnly, decimal>();
            foreach (var entry in timeEntries)
            {
                if (entry.ActivityType != "NORMAL" || entry.TaskId is null)
                    continue;
                allocatedByDay[entry.Date] = allocatedByDay.TryGetValue(entry.Date, out var existing)
                    ? existing + entry.Hours
                    : entry.Hours;
            }

            // Compare every day that has either worked or allocated hours. Days
            // with worked==0 AND allocated==0 are implicitly balanced (skipped).
            var unbalancedDays = new List<object>();
            foreach (var day in workedByDay.Keys.Union(allocatedByDay.Keys).OrderBy(d => d))
            {
                var worked = Math.Round(workedByDay.GetValueOrDefault(day), 2);
                var allocated = Math.Round(allocatedByDay.GetValueOrDefault(day), 2);
                if (Math.Abs(worked - allocated) < AllocationTolerance)
                    continue;
                unbalancedDays.Add(new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    worked,
                    allocated,
                    direction = worked > allocated ? "under" : "over",
                });
            }

            if (unbalancedDays.Count > 0)
            {
                return Results.UnprocessableEntity(new
                {
                    kind = "allocation",
                    unbalancedDays,
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
            ReportingLineRepository reportingLineRepo,
            DesignatedApproverAuthorizer designatedAuthorizer,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PeriodReopened> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        // S78 R1 — bounded drift-retry wrapper. The LEADER arm takes the advisory + in-tx edge re-eval;
        // the EMPLOYEE arm takes NO advisory (it carries no designated-edge authority — a self-action),
        // but BOTH arms get the R2 conditional UPDATE. AcquireTreeLockForEmployeeAsync only runs on the
        // Leader arm, so the drift-retry only ever fires for the Leader arm.
        await TreeRootDriftRetry.RunAsync(async () =>
        {
            var actor = context.GetActorContext();

            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            var isEmployee = actor.ActorRole == StatsTidRoles.Employee;
            // S78 R1: track whether the Leader arm was admitted by org-scope (for the in-tx re-eval) and
            // the allowed conditional-UPDATE source-state set (R2), which differs per arm.
            var orgScopeAdmittedLeaderArm = false;
            string? orgScopeReason = null;
            string[] allowedSourceStates;

            if (isEmployee)
            {
                // Employee can only reopen own EMPLOYEE_APPROVED period. The A3 edge-authority
                // OR-branch is DELIBERATELY ABSENT here — granting it to the employee arm would
                // over-grant employees (a designated edge is a MANAGER privilege).
                var (allowed2, reason2) = await scopeValidator.ValidateEmployeeAccessAsync(actor, period.EmployeeId, ct);
                if (!allowed2)
                    return Results.Json(new { error = "Access denied", reason = reason2 }, statusCode: 403);

                if (period.Status != "EMPLOYEE_APPROVED")
                    return Results.Json(new { error = "Access denied", reason = "Employee can only reopen EMPLOYEE_APPROVED periods" }, statusCode: 403);

                // EMPLOYEE arm: only EMPLOYEE_APPROVED → DRAFT.
                allowedSourceStates = new[] { "EMPLOYEE_APPROVED" };
            }
            else
            {
                // Leader+: authorize via org scope (existing) OR the effective designated-approver
                // edge at today (S74 / ADR-027 D4 A3 — the edge grants cross-afdeling authority
                // within the styrelse; tree-bound is structural). This OR-branch lives ONLY in
                // the Leader+ arm.
                var (allowed2, reason2) = await scopeValidator.ValidateOrgAccessAsync(actor, period.OrgId, ct);
                orgScopeAdmittedLeaderArm = allowed2;
                orgScopeReason = reason2;
                if (!allowed2)
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var hasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                        actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                    if (!hasEdge)
                        return Results.Json(new { error = "Access denied", reason = reason2 }, statusCode: 403);
                }

                if (period.Status is not ("EMPLOYEE_APPROVED" or "APPROVED"))
                    return Results.Conflict(new { error = $"Cannot reopen period with status {period.Status}. Only EMPLOYEE_APPROVED or APPROVED periods can be reopened." });

                // LEADER arm: EMPLOYEE_APPROVED or APPROVED → DRAFT.
                allowedSourceStates = new[] { "EMPLOYEE_APPROVED", "APPROVED" };
            }

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // S78 R1 — the LEADER arm only: advisory FIRST, then in-tx edge re-eval (org-scope stays a
            // pre-tx-only gate). The EMPLOYEE arm carries no designated-edge authority (a self-action
            // gated by ValidateEmployeeAccessAsync), so it takes NEITHER the advisory nor the re-eval.
            if (!isEmployee)
            {
                await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);
                if (!orgScopeAdmittedLeaderArm)
                {
                    var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
                    var stillHasEdge = await designatedAuthorizer.IsEffectiveDesignatedApproverAsync(
                        actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                    if (!stillHasEdge)
                        return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
                }
            }

            // S78 R2 — the CONDITIONAL status transition is the FIRST (and only) mutation before the audit
            // + outbox; a null (0-row) loser of a concurrent double-transition short-circuits to a clean 409.
            // S78 BLOCKER 1 — the conditional UPDATE RETURNs the LOCKED-IN pre-update status (captured
            // atomically with FOR UPDATE), so PeriodReopened.PreviousStatus records the status that was
            // actually present at the locked transition — NOT the stale pre-tx read (period.Status). This
            // resolves the approve-then-reopen flip: when a concurrent approve commits between this request's
            // pre-tx read and its locked UPDATE, the reopen's allowed source set still includes APPROVED so
            // it wins and accurately records previousStatus=APPROVED; if the approve has NOT yet committed it
            // sees the pre-tx status (e.g. EMPLOYEE_APPROVED) — and a row already moved fully out of the
            // allowed set returns null → a clean 409.
            var previousStatus = await approvalRepo.TryUpdateStatusConditionalAsync(
                conn, tx, periodId, "DRAFT", allowedSourceStates, actor.ActorId, ct: ct);
            if (previousStatus is null)
                return Results.Conflict(new { error = "Period status changed concurrently; refresh and retry." });

            // Write audit trail (in-tx).
            await approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "REOPENED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee,
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
                PreviousStatus = previousStatus,
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
        })).RequireAuthorization("EmployeeOrAbove"); // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync

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
