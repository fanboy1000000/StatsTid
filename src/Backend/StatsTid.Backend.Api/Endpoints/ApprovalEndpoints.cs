using System.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Config;
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
    /// S87-8701 — camelCase JSON options matching the <c>work_time_projection.intervals</c> JSONB
    /// shape (the same casing <see cref="StatsTid.Infrastructure.WorkTimeProjectionRepository"/>
    /// persists/reads with: <c>[{"start":"08:00","end":"12:00"}]</c>).
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions TeamOverviewIntervalsJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// S87-8701 — reads the subset of <c>approval_periods</c> columns the team-overview row needs
    /// (status / submitted_at / approved_at [the neutral decisionAt — rejects write it too] /
    /// rejection_reason / agreement_code). A local minimal reader so the endpoint can batch the
    /// non-null period ids with one <c>WHERE period_id = ANY(...)</c> query.
    /// </summary>
    private static ApprovalPeriod ReadTeamOverviewPeriod(Npgsql.NpgsqlDataReader reader) => new()
    {
        PeriodId = reader.GetGuid(reader.GetOrdinal("period_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        PeriodStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_start"))),
        PeriodEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_end"))),
        PeriodType = reader.GetString(reader.GetOrdinal("period_type")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        SubmittedAt = reader.IsDBNull(reader.GetOrdinal("submitted_at")) ? null : reader.GetDateTime(reader.GetOrdinal("submitted_at")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        RejectionReason = reader.IsDBNull(reader.GetOrdinal("rejection_reason")) ? null : reader.GetString(reader.GetOrdinal("rejection_reason")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
    };

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
    /// Derives the persisted <c>approval_method</c> for an approve/reject from the resolved designated
    /// approver AND the S105 / ADR-038 D4 unit-leader classification. Precedence (mirrors the D4
    /// CanApprove order): the EDGE first — when the actor IS the resolved designated approver, the
    /// resolver's method (<c>ACTING_MANAGER</c> / <c>DESIGNATED_MANAGER</c>, incl. the edge-manager's
    /// vikar). Otherwise the SECONDARY unit-leader paths — a direct unit-leader of the employee's own
    /// unit → <c>UNIT_LEADER</c>; an active vikar of such a leader → <c>UNIT_LEADER_VIKAR</c>. Else
    /// <c>ORG_SCOPE_FALLBACK</c> (the HR/Admin org-scope fallback, or no designated approver). Run UNDER
    /// the held advisories at the in-tx authoritative re-derivation (S78 BLOCKER 2), so the unit-leader
    /// resolution observes the frozen committed state (a concurrent <c>UnitLeaderRemoved</c>/member-move
    /// is blocked from committing by the held <c>unit-org-</c> advisory).
    /// </summary>
    private static async Task<string> DeriveApprovalMethodAsync(
        DesignatedApproverAuthorizer designatedAuthorizer,
        string? actorId, string employeeId, string? designatedManagerId, string? resolvedMethod,
        DateOnly asOf, CancellationToken ct)
    {
        // (1) The EDGE path — the actor is the single resolved effective approver.
        if (designatedManagerId is not null && actorId == designatedManagerId)
            return resolvedMethod!; // "ACTING_MANAGER" or "DESIGNATED_MANAGER"

        // (2) The SECONDARY unit-leader paths (D4 path-2/3) — classify for an honest audit (NOT the
        //     misleading ORG_SCOPE_FALLBACK, which is HR/Admin scope, not unit-leader authority).
        if (!string.IsNullOrEmpty(actorId))
        {
            var unitKind = await designatedAuthorizer.ResolveUnitLeaderApprovalKindAsync(
                actorId, employeeId, asOf: asOf, ct: ct);
            if (unitKind == UnitLeaderApprovalKind.Direct)
                return "UNIT_LEADER";
            if (unitKind == UnitLeaderApprovalKind.Vikar)
                return "UNIT_LEADER_VIKAR";
        }

        // (3) The HR/Admin org-scope fallback, or no designated approver.
        return "ORG_SCOPE_FALLBACK";
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

            // Authorize (S94 / ADR-035 OQ4/OQ5 — the flat-authority model): EITHER the actor holds
            // HR/Admin scope over the employee's CURRENT Organisation (the org-scope FALLBACK, now
            // FLOORED at LocalHR and bound to the employee's current primary_org via
            // ValidateEmployeeAccessAsync — exactly HasHrAdminScopeOverEmpOrg) OR the actor holds the
            // effective designated-approver edge for this employee RIGHT NOW (S74 / ADR-027 D4 A3 —
            // the edge grants cross-afdeling authority; asOf = today = "who may act NOW"). The
            // unfloored leader-by-org-scope branch is RETIRED: a non-designated in-scope LEADER must
            // now hold the edge. S78 R1: orgScopeAllowed is hoisted so the in-tx re-eval knows whether
            // the actor was admitted by the HR/Admin fallback (JWT-/scope-based, not re-checked in-tx)
            // or purely by the edge.
            var (orgScopeAllowed, orgScopeReason) =
                await scopeValidator.ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct);
            if (!orgScopeAllowed)
            {
                // S105 / ADR-038 D4 — the edge OR the NEW secondary-unit-leader path (incl. a unit
                // leader's vikar), via the centralized predicate. asOf = today = "who may act NOW".
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasEdgeOrUnit = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                if (!hasEdgeOrUnit)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // Resolve designated approver for audit trail (ADR-027 D5). PRE-tx FAST PATH (the in-tx
            // re-derivation under the advisory is the AUTHORITATIVE one — S78 BLOCKER 2).
            var (preDesignatedManagerId, preResolvedMethod, _) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // The treeRoot is request-stable and is still needed for the
            // FallbackTraversalWarning.OrganisationId (depth>3) payload below. S95 / ADR-035 slice 4:
            // the tree-WALK (ResolveOrganisationIdAsync) is RETIRED — post-S92 the period's reporting
            // "tree root" IS period.OrgId directly (the walk always returned the input org), so the
            // warning's OrganisationId field (name kept — no event-shape change) is sourced from
            // period.OrgId. (S94 / TASK-9402 already retired the REQUIRED-mode gate here.)
            var treeRoot = period.OrgId;

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
            // [DELETE /…/vikar] deliberately keys on the PERSISTED manager_vikar.organisation_id for
            // revoke-safety, NOT the employee-current root, so a post-transfer revoke can key on a DIFFERENT
            // tree than this approve — the approve-vs-vikar-revoke post-transfer key-mismatch residual.
            // That residual is non-corrupting: the revoke only ENDS an existing edge, and this in-tx
            // re-eval re-reads the committed manager_vikar state under ReadCommitted regardless of which key
            // either side held.) We re-check ONLY the designated edge / unit-leader path for AUTHORITY (not
            // org-scope: ValidateOrgAccessAsync is JWT-claim-based and cannot be serialized by a DB lock —
            // its pre-tx check remains the gate). If the actor passed the pre-tx check PURELY via the edge /
            // unit-leader path (org-scope denied), a revoke that committed before we got the lock now flips
            // the re-eval to DENY → 403.
            var empCurrentOrg = await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);

            // S105 / ADR-038 D4/D8 (BLOCKER fix) — ALSO acquire the employee's current `unit-org-`
            // advisory (keyed on the employee's current Organisation = the verified tree root above), in
            // the D8 total order `reporting-org-` → `unit-org-` → row FOR UPDATE. The NEW path-2 revokers
            // (`UnitLeaderRemoved` / same-Org member-move) serialize on `unit-org-`, a DIFFERENT key from
            // `reporting-org-`; without this, a just-de-designated unit-leader's approve would NOT
            // serialize against the concurrent removal (a stale-authority window). Taken BEFORE the in-lock
            // re-eval of the extended CanApprove so the revoke either commits-first (re-eval denies) or
            // blocks until we release.
            await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, empCurrentOrg, ct);

            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

            // Compute asOf at action-time. Only re-check the edge / unit-leader path for AUTHORITY when the
            // pre-tx ORG-scope gate did NOT already admit the actor (orgScopeAllowed): an org-scope-admitted
            // approval does not depend on the edge, so a revoked edge must not flip it to 403 (not the
            // authorizing surface).
            if (!orgScopeAllowed)
            {
                var stillAuthorized = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                if (!stillAuthorized)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // S78 BLOCKER 2 — re-resolve the designated approver + re-derive the approval-method
            // classification UNDER the held advisories (the AUTHORITATIVE values for the persisted audit
            // metadata). The resolver opens its own connection, but ReadCommitted + the held advisories mean
            // it observes the FROZEN committed edge/unit-leader state (a concurrent reassignment /
            // UnitLeaderRemoved is blocked from committing until we release), so this re-derivation reflects
            // the locked tree. S94 / TASK-9402: the REQUIRED-mode 428 re-eval is GONE. S105 / ADR-038 D4:
            // a secondary-unit-leader approval now records UNIT_LEADER / UNIT_LEADER_VIKAR (not the
            // misleading ORG_SCOPE_FALLBACK).
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct, asOf: asOf);
            var approvalMethod = await DeriveApprovalMethodAsync(
                designatedAuthorizer, actor.ActorId, period.EmployeeId, designatedManagerId, resolvedMethod, asOf, ct);

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
                    OrganisationId = treeRoot,
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

            // Authorize (S94 / ADR-035 OQ4/OQ5 — same flat-authority model as approve): the HR/Admin
            // fallback (floored at LocalHR, bound to the employee's CURRENT Organisation via
            // ValidateEmployeeAccessAsync) OR the effective designated-approver edge at today (S74 /
            // ADR-027 D4 A3). The unfloored leader-by-org-scope branch is RETIRED. S78 R1: orgScopeAllowed
            // hoisted for the in-tx edge re-eval (same as approve).
            var (orgScopeAllowed, orgScopeReason) =
                await scopeValidator.ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct);
            if (!orgScopeAllowed)
            {
                // S105 / ADR-038 D4 — the edge OR the NEW secondary-unit-leader path, centralized predicate.
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasEdgeOrUnit = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                if (!hasEdgeOrUnit)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // Resolve designated approver for audit trail (ADR-027 D5). PRE-tx FAST PATH; the in-tx
            // re-derivation under the advisory is the AUTHORITATIVE one (S78 BLOCKER 2).
            var (preDesignatedManagerId, preResolvedMethod, _) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct);

            // treeRoot is request-stable and still needed for the FallbackTraversalWarning (depth>3)
            // below. S95 / ADR-035 slice 4: the tree-WALK is RETIRED — the period's "tree root" IS
            // period.OrgId directly (field name kept; no event-shape change). (S94 / TASK-9402 already
            // retired the REQUIRED-mode 428 gate here.)
            var treeRoot = period.OrgId;

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3).
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // S78 R1 — in-lock edge-auth re-evaluation (same shape as approve): advisory FIRST, then
            // re-check the designated edge / unit-leader path under the held lock; org-scope stays a
            // pre-tx-only gate. S105 / ADR-038 D4/D8 — ALSO acquire the employee's current `unit-org-`
            // advisory (D8 order `reporting-org-` → `unit-org-` → row FOR UPDATE) so a concurrent
            // `UnitLeaderRemoved`/member-move serializes against this reject.
            var empCurrentOrg = await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);
            await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, empCurrentOrg, ct);
            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!orgScopeAllowed)
            {
                var stillAuthorized = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                if (!stillAuthorized)
                    return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
            }

            // S78 BLOCKER 2 — re-resolve + re-classify UNDER the held advisories (the authoritative values
            // for the persisted audit metadata). Same rationale as approve: a concurrent reassignment /
            // UnitLeaderRemoved is blocked from committing, so the resolver observes the frozen locked tree.
            // S94 / TASK-9402: the REQUIRED-mode 428 re-eval is GONE. S105 / ADR-038 D4: a
            // secondary-unit-leader reject records UNIT_LEADER / UNIT_LEADER_VIKAR.
            var (designatedManagerId, resolvedMethod, depth) =
                await reportingLineRepo.ResolveDesignatedApproverAsync(period.EmployeeId, ct, asOf: asOf);
            var approvalMethod = await DeriveApprovalMethodAsync(
                designatedAuthorizer, actor.ActorId, period.EmployeeId, designatedManagerId, resolvedMethod, asOf, ct);

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
                    OrganisationId = treeRoot,
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
                else if (scope.ScopeType == "ORG_ONLY" && scope.OrgId is not null)
                {
                    // ORG_ONLY: get pending for that specific org (S93/ADR-035: exact membership,
                    // no subtree).
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
                else if (scope.ScopeType == "ORG_ONLY" && scope.OrgId is not null)
                {
                    // ORG_ONLY: get periods for that specific org (S93/ADR-035: exact membership,
                    // no subtree).
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

        // ── S87-8701 — Team Overview aggregate (leader Teamoversigt) ──
        //
        // GET /api/approval/team-overview?year=&month= (LeaderOrAbove). One row per employee in the
        // ACTOR's designated-act-authority set (ADR-027 D13 see == act): the roster comes from the
        // SAME designated-candidate CTE → R5 predicate the approval queries use, LEFT JOINed to the
        // (year,month) period so a zero-period report still appears as a DRAFT row (periodId=null).
        // It is NOT org-scope and NOT /reports — a non-leader / a leader with no reports gets an
        // empty set. The balance/norm/ferie/flex/warning fields are computed via BATCHED, set-based
        // reads over the team's employee-ids (NOT 40× the full /summary; NOT a re-implementation of
        // the dated-OK/entitlement resolution — the S81 split-brain is left untouched, the
        // authoritative full Saldi stay on /summary, lazy-on-expand = P2/S88). NO rule-engine call:
        // hasWarning mirrors ONLY the ALLOCATION arm of the approve gate (a deliberate P1 narrowing).
        app.MapGet("/api/approval/team-overview", async (
            [FromQuery] int year,
            [FromQuery] int month,
            ApprovalPeriodRepository approvalRepo,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (year < 2020 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year. Must be between 2020 and 2100." });
            if (month < 1 || month > 12)
                return Results.BadRequest(new { error = "Invalid month. Must be between 1 and 12." });

            var actor = context.GetActorContext();
            if (string.IsNullOrEmpty(actor.ActorId))
                return Results.Json(new { error = "Access denied", reason = "No actor identity" }, statusCode: 403);

            // (1) The roster = the actor's designated-act-authority set for (year, month), with
            //     zero-period DRAFT rows. The repo derives it from the candidate CTE → R5 predicate
            //     (NOT org-scope, NOT /reports), so this is inherently designated-approver-scoped: a
            //     non-approver / a leader with no reports gets an empty roster (NOT an org-scope leak).
            var roster = await approvalRepo.GetTeamOverviewRosterAsync(actor.ActorId, year, month, ct);
            if (roster.Count == 0)
                return Results.Ok(new { employees = Array.Empty<object>() });

            var employeeIds = roster.Select(r => r.EmployeeId).Distinct().ToArray();
            var periodIds = roster.Where(r => r.PeriodId is not null).Select(r => r.PeriodId!.Value).ToArray();

            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            // VACATION is schema-pinned to reset_month = 9 (init.sql CHECK), so the ferieår for the
            // requested month is the SAME keying the /summary EntitlementPeriodResolver path uses for
            // VACATION — derived here without re-implementing the dated-config resolution.
            var vacationYear = month >= 9 ? year : year - 1;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // (2) ONE bounded query per field, set-based over the team's employee-ids (≤ ~40 rows) —
            //     NOT a per-employee /summary loop, NOT a per-employee event replay.
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);

            // (2a) The full period rows for the non-null period ids (status / submittedAt /
            //      decisionAt [= approved_at; rejects write it too] / rejectionReason / agreement).
            var periodById = new Dictionary<Guid, ApprovalPeriod>();
            if (periodIds.Length > 0)
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT * FROM approval_periods WHERE period_id = ANY(@ids)", conn);
                cmd.Parameters.AddWithValue("ids", periodIds);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var p = ReadTeamOverviewPeriod(reader);
                    periodById[p.PeriodId] = p;
                }
            }

            // (2b) normRegistered = summed time_entries_projection hours per employee for the month.
            var registeredByEmployee = new Dictionary<string, decimal>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT employee_id, COALESCE(SUM(hours), 0) AS total
                FROM time_entries_projection
                WHERE employee_id = ANY(@ids) AND date >= @start AND date <= @end
                GROUP BY employee_id
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    registeredByEmployee[reader.GetString(0)] = reader.GetDecimal(1);
            }

            // (2c) allocated(NORMAL + non-null TaskId) hours per (employee, date) for the month —
            //      the ALLOCATION arm of the approve gate (ApprovalEndpoints ~:960). Used WITH the
            //      work-time worked hours below to compute hasWarning = (worked − allocated) > tol.
            var allocatedByEmployeeDay = new Dictionary<(string, DateOnly), decimal>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT employee_id, date, COALESCE(SUM(hours), 0) AS allocated
                FROM time_entries_projection
                WHERE employee_id = ANY(@ids) AND date >= @start AND date <= @end
                  AND activity_type = 'NORMAL' AND task_id IS NOT NULL
                GROUP BY employee_id, date
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    allocatedByEmployeeDay[(reader.GetString(0),
                        DateOnly.FromDateTime(reader.GetDateTime(1)))] = reader.GetDecimal(2);
            }

            // (2d) worked(intervals + manual_hours) per (employee, date) from work_time_projection.
            var workedByEmployeeDay = new Dictionary<(string, DateOnly), decimal>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT employee_id, date, intervals, manual_hours
                FROM work_time_projection
                WHERE employee_id = ANY(@ids) AND date >= @start AND date <= @end
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var empId = reader.GetString(0);
                    var date = DateOnly.FromDateTime(reader.GetDateTime(1));
                    var intervalsJson = reader.GetString(2);
                    var manual = reader.GetDecimal(3);
                    var intervals = System.Text.Json.JsonSerializer.Deserialize<List<WorkInterval>>(
                        intervalsJson, TeamOverviewIntervalsJsonOptions) ?? new List<WorkInterval>();
                    var worked = SumIntervalHours(intervals) + manual;
                    var key = (empId, date);
                    workedByEmployeeDay[key] = workedByEmployeeDay.TryGetValue(key, out var ex) ? ex + worked : worked;
                }
            }

            // (2e) ferieUsed/ferieTotal = entitlement_balances VACATION used/total_quota for the
            //      ferieår of the requested month (ADR-032 ferieår-correct — NOT vacationDaysUsed).
            var ferieByEmployee = new Dictionary<string, (decimal Used, decimal Total)>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT employee_id, used, total_quota
                FROM entitlement_balances
                WHERE employee_id = ANY(@ids) AND entitlement_type = 'VACATION' AND entitlement_year = @vacYear
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("vacYear", vacationYear);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    ferieByEmployee[reader.GetString(0)] = (reader.GetDecimal(1), reader.GetDecimal(2));
            }

            // (2f) flexBalance = the latest FlexBalanceUpdated NewBalance per employee. Flex has no
            //      projection (it lives ONLY in the employee-{id} event stream), so this is a BOUNDED,
            //      set-based read: DISTINCT ON (stream_id) over the team's streams, picking the highest
            //      stream_version FlexBalanceUpdated row — NOT a per-employee full-stream replay loop.
            //      The stored JSON is camelCase (EventSerializer) → data->>'newBalance'.
            var flexByEmployee = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var flexStreamIds = employeeIds.Select(id => $"employee-{id}").ToArray();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT DISTINCT ON (stream_id) stream_id, (data->>'newBalance') AS new_balance
                FROM events
                WHERE stream_id = ANY(@streamIds) AND event_type = 'FlexBalanceUpdated'
                ORDER BY stream_id, stream_version DESC
                """, conn))
            {
                cmd.Parameters.AddWithValue("streamIds", flexStreamIds);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var streamId = reader.GetString(0);
                    var empId = streamId.StartsWith("employee-", StringComparison.Ordinal)
                        ? streamId.Substring("employee-".Length)
                        : streamId;
                    if (!reader.IsDBNull(1) && decimal.TryParse(reader.GetString(1),
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out var bal))
                        flexByEmployee[empId] = bal;
                }
            }

            // (2g) awayToday = an absence covering TODAY. PER-EMPLOYEE FAULT-ISOLATED: a failure of
            //      THIS read degrades EVERY row's awayToday to false (never a whole-table 500); a
            //      successful read populates the set and a missing employee is simply false.
            var awayTodaySet = new HashSet<string>(StringComparer.Ordinal);
            var awayTodayAvailable = true;
            try
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    SELECT DISTINCT employee_id
                    FROM absences_projection
                    WHERE employee_id = ANY(@ids) AND date = @today
                    """, conn);
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("today", today);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    awayTodaySet.Add(reader.GetString(0));
            }
            catch (Exception)
            {
                // Fault-isolated: the awayToday signal is best-effort. Degrade the flag to false for
                // ALL rows rather than failing the whole team-overview load.
                awayTodayAvailable = false;
            }

            // (2g2) payrollExported = the employee has a payroll_export_records row for (year, month).
            //       S90/TASK-9005 — a READ-ONLY cross-context lookup of the Payroll-owned lock table
            //       (ADR-034: the Backend reads this, NEVER writes it). The row's EXISTENCE per
            //       (employee_id, year, month) == "sent to lønkørsel" → the FE hides Genåbn for these
            //       rows (the month is corrections-only post-export). ONE batched set-based read over
            //       the team's employee-ids (the same shape as the reads above), NOT a per-row query.
            var payrollExportedByEmployee = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT employee_id, exported_at
                FROM payroll_export_records
                WHERE employee_id = ANY(@ids) AND year = @year AND month = @month
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                cmd.Parameters.AddWithValue("year", year);
                cmd.Parameters.AddWithValue("month", month);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    payrollExportedByEmployee[reader.GetString(0)] = reader.GetDateTime(1);
            }

            // (2h) normExpected = (weekdays/5) × weeklyNorm per employee. weeklyNorm resolves from the
            //      employee's agreement config, cached per distinct (agreement, ok) pair so the
            //      lookups are bounded (≤ #distinct agreements, NOT per-employee). Mirrors the
            //      /summary norm-expected derivation (weekday count × weekly norm / 5) without the
            //      heavy per-employee dated-config resolution.
            var weekdays = 0;
            for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    weekdays++;

            // Resolve each roster employee's (agreement, ok) once (users row), then weekly norm once
            // per distinct pair. The agreement used is the PERIOD's when a period exists (the same
            // dimension the period was submitted under), else the users fallback; ok comes from users.
            var usersInfo = new Dictionary<string, (string Agreement, string OkVersion)>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(
                "SELECT user_id, agreement_code, ok_version FROM users WHERE user_id = ANY(@ids)", conn))
            {
                cmd.Parameters.AddWithValue("ids", employeeIds);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    usersInfo[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
            }

            var weeklyNormCache = new Dictionary<(string, string), decimal>();
            async Task<decimal> ResolveWeeklyNormAsync(string agreement, string okVersion)
            {
                var key = (agreement, okVersion);
                if (weeklyNormCache.TryGetValue(key, out var cached))
                    return cached;
                var dbConfig = await agreementConfigRepo.GetActiveAsync(agreement, okVersion, ct);
                var norm = dbConfig?.WeeklyNormHours
                    ?? CentralAgreementConfigs.TryGetConfig(agreement, okVersion)?.WeeklyNormHours
                    ?? 37.0m;
                weeklyNormCache[key] = norm;
                return norm;
            }

            // (3) Assemble one row per roster employee.
            var employees = new List<object>(roster.Count);
            foreach (var r in roster)
            {
                ApprovalPeriod? period = r.PeriodId is not null && periodById.TryGetValue(r.PeriodId.Value, out var p) ? p : null;
                var status = period?.Status ?? "DRAFT";
                var agreement = period?.AgreementCode ?? r.UsersAgreementCode;

                // decisionAt is NEUTRAL: rejects write approved_at too (no stored rejectedAt), so
                // status disambiguates approve vs reject. Only surfaced for APPROVED/REJECTED rows.
                DateTime? decisionAt = status is "APPROVED" or "REJECTED" ? period?.ApprovedAt : null;
                var rejectionReason = status == "REJECTED" ? period?.RejectionReason : null;

                var (uAgreement, uOk) = usersInfo.TryGetValue(r.EmployeeId, out var ui)
                    ? ui
                    : (r.UsersAgreementCode, "OK24");
                // Norm-expected uses the period's agreement when present (consistent with the row's
                // displayed agreement), else the users agreement; ok is from users (the live cache).
                var weeklyNorm = await ResolveWeeklyNormAsync(agreement, uOk);
                var normExpected = (weekdays / 5.0m) * weeklyNorm;

                var normRegistered = registeredByEmployee.GetValueOrDefault(r.EmployeeId, 0m);
                var overtime = Math.Max(0m, normRegistered - normExpected);

                var (ferieUsed, ferieTotal) = ferieByEmployee.GetValueOrDefault(r.EmployeeId, (0m, 0m));
                var flexBalance = flexByEmployee.GetValueOrDefault(r.EmployeeId, 0m);
                var awayToday = awayTodayAvailable && awayTodaySet.Contains(r.EmployeeId);

                // payrollExported = the month is sent to lønkørsel (a payroll_export_records row
                // exists for this employee + (year, month)). The FE gates the reopen control on this.
                var payrollExported = payrollExportedByEmployee.TryGetValue(r.EmployeeId, out var exportedAt);

                // hasWarning = the cheap allocation-imbalance warning (|worked − allocated| > tol on
                // ANY day in the month). Mirrors the allocation arm of the approve gate SYMMETRICALLY
                // (the gate flags Math.Abs(worked − allocated) ≥ tol — both under- AND over-allocation
                // are un-approvable; S87 Step-7a), — NO rule-engine / compliance call. A named P1
                // narrowing: it does NOT mirror the coverage/uncovered-days arm, so false ≠ submittable.
                var hasWarning = false;
                var daysWithEither = new HashSet<DateOnly>();
                for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
                {
                    if (workedByEmployeeDay.ContainsKey((r.EmployeeId, d)) ||
                        allocatedByEmployeeDay.ContainsKey((r.EmployeeId, d)))
                        daysWithEither.Add(d);
                }
                foreach (var d in daysWithEither)
                {
                    var worked = Math.Round(workedByEmployeeDay.GetValueOrDefault((r.EmployeeId, d), 0m), 2);
                    var allocated = Math.Round(allocatedByEmployeeDay.GetValueOrDefault((r.EmployeeId, d), 0m), 2);
                    if (Math.Abs(worked - allocated) > AllocationTolerance)
                    {
                        hasWarning = true;
                        break;
                    }
                }

                employees.Add(new
                {
                    periodId = r.PeriodId,
                    employeeId = r.EmployeeId,
                    displayName = r.DisplayName,
                    agreement,
                    status,
                    submittedAt = period?.SubmittedAt,
                    decisionAt,
                    rejectionReason,
                    normExpected,
                    normRegistered,
                    flexBalance,
                    overtime,
                    ferieUsed,
                    ferieTotal,
                    awayToday,
                    hasWarning,
                    payrollExported,
                    payrollExportedAt = payrollExported ? exportedAt : (DateTime?)null,
                });
            }

            return Results.Ok(new { employees });
        }).RequireAuthorization("LeaderOrAbove");

        // ── S88-8801 — Allocation breakdown (the leder-oversigt expandable detail's Fordeling) ──
        //
        // GET /api/approval/{employeeId}/allocation-breakdown?year=&month= (LeaderOrAbove). The
        // per-employee project-allocation slice the team-overview detail row lazy-fetches on expand.
        // AUTH (B1/B2 predicate): designated-approver-scoped via DesignatedApproverAuthorizer
        // .IsEffectiveDesignatedApproverAsync(actorId, employeeId, today) — the SAME predicate the S87
        // team-overview roster filters through (ApprovalPeriodRepository:432), so breakdown-authorized
        // == roster: no 403 on a row the leader can see, and no org-scope leak (NOT ValidateEmployeeAccessAsync).
        //
        // The figures REPLICATE the S87 aggregate's per-(employee,day) worked/allocated maps for THIS
        // employee (a per-employee slice of :910-957) so the result is PROVABLY identical to the row:
        //   hasAllocationImbalance — the AUTHORITATIVE per-day ANY check, computed IDENTICALLY to the
        //     aggregate's hasWarning loop (:1102-1119): iterate the days with either worked or allocated;
        //     true iff ANY day has Math.Abs(round(worked_d,2) − round(allocated_d,2)) > AllocationTolerance.
        //     It MUST NOT be derived from the under/over sums — summing sub-tolerance daily deltas could
        //     trip a sum past tol where the per-day ANY check (and thus the table chip) would not.
        //   underAllocated / overAllocated — DISPLAY-only directional sums over the per-rounded-day deltas.
        //   allocations[] — month-sum NORMAL+non-null-TaskId hours grouped by TaskId (a display aid,
        //     sums to allocated).
        app.MapGet("/api/approval/{employeeId}/allocation-breakdown", async (
            string employeeId,
            [FromQuery] int year,
            [FromQuery] int month,
            DesignatedApproverAuthorizer designatedAuthorizer,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (year < 2020 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year. Must be between 2020 and 2100." });
            if (month < 1 || month > 12)
                return Results.BadRequest(new { error = "Invalid month. Must be between 1 and 12." });

            var actor = context.GetActorContext();
            if (string.IsNullOrEmpty(actor.ActorId))
                return Results.Json(new { error = "Access denied", reason = "No actor identity" }, statusCode: 403);

            // AUTH (B1): the designated-approver edge OR the S105 / ADR-038 D4 secondary-unit-leader path
            // — exactly the centralized predicate the team-overview roster filters through, so a row the
            // leader can see (incl. a unit-led member) is always breakdown-authorized (no org-scope leak).
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var authorized = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                actor.ActorId!, employeeId, asOf: today, ct: ct);
            if (!authorized)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);

            // (a) allocated(NORMAL + non-null TaskId) hours per DAY for this employee — same SQL as the
            //     aggregate's per-(employee,day) allocation read (:914-930), filtered to this employee.
            var allocatedByDay = new Dictionary<DateOnly, decimal>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT date, COALESCE(SUM(hours), 0) AS allocated
                FROM time_entries_projection
                WHERE employee_id = @id AND date >= @start AND date <= @end
                  AND activity_type = 'NORMAL' AND task_id IS NOT NULL
                GROUP BY date
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", employeeId);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    allocatedByDay[DateOnly.FromDateTime(reader.GetDateTime(0))] = reader.GetDecimal(1);
            }

            // (b) worked(intervals + manual_hours) per DAY from work_time_projection — same as :932-957.
            var workedByDay = new Dictionary<DateOnly, decimal>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT date, intervals, manual_hours
                FROM work_time_projection
                WHERE employee_id = @id AND date >= @start AND date <= @end
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", employeeId);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var date = DateOnly.FromDateTime(reader.GetDateTime(0));
                    var intervalsJson = reader.GetString(1);
                    var manual = reader.GetDecimal(2);
                    var intervals = System.Text.Json.JsonSerializer.Deserialize<List<WorkInterval>>(
                        intervalsJson, TeamOverviewIntervalsJsonOptions) ?? new List<WorkInterval>();
                    var workedDay = SumIntervalHours(intervals) + manual;
                    workedByDay[date] = workedByDay.TryGetValue(date, out var ex) ? ex + workedDay : workedDay;
                }
            }

            // (c) allocations[] — month-sum NORMAL+non-null-TaskId hours grouped by TaskId (display bars;
            //     sums to allocated). Stable order by taskId for deterministic rendering.
            var allocations = new List<object>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT task_id, COALESCE(SUM(hours), 0) AS hours
                FROM time_entries_projection
                WHERE employee_id = @id AND date >= @start AND date <= @end
                  AND activity_type = 'NORMAL' AND task_id IS NOT NULL
                GROUP BY task_id
                ORDER BY task_id
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", employeeId);
                cmd.Parameters.AddWithValue("start", monthStart);
                cmd.Parameters.AddWithValue("end", monthEnd);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    allocations.Add(new { taskId = reader.GetString(0), hours = reader.GetDecimal(1) });
            }

            // (d) The month totals + the per-day directional sums + the AUTHORITATIVE per-day ANY check.
            var worked = workedByDay.Values.Sum();
            var allocated = allocatedByDay.Values.Sum();

            var daysWithEither = new HashSet<DateOnly>();
            foreach (var d in workedByDay.Keys) daysWithEither.Add(d);
            foreach (var d in allocatedByDay.Keys) daysWithEither.Add(d);

            decimal underAllocated = 0m;
            decimal overAllocated = 0m;
            var hasAllocationImbalance = false;
            foreach (var d in daysWithEither)
            {
                var workedD = Math.Round(workedByDay.GetValueOrDefault(d, 0m), 2);
                var allocatedD = Math.Round(allocatedByDay.GetValueOrDefault(d, 0m), 2);
                underAllocated += Math.Max(0m, workedD - allocatedD);
                overAllocated += Math.Max(0m, allocatedD - workedD);
                // AUTHORITATIVE imbalance = the SAME per-day ANY check the table hasWarning uses (the
                // approve gate, both directions). NOT derived from the summed under/over (B1 drift).
                if (Math.Abs(workedD - allocatedD) > AllocationTolerance)
                    hasAllocationImbalance = true;
            }

            return Results.Ok(new
            {
                allocations,
                worked,
                allocated,
                underAllocated = Math.Round(underAllocated, 2),
                overAllocated = Math.Round(overAllocated, 2),
                hasAllocationImbalance,
            });
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
                // Leader+: authorize (S94 / ADR-035 OQ4/OQ5 — the same flat-authority model as
                // approve/reject) via the HR/Admin fallback (floored at LocalHR, bound to the
                // employee's CURRENT Organisation via ValidateEmployeeAccessAsync) OR the effective
                // designated-approver edge at today (S74 / ADR-027 D4 A3). The unfloored
                // leader-by-org-scope branch is RETIRED. This OR-branch lives ONLY in the Leader+ arm.
                var (allowed2, reason2) =
                    await scopeValidator.ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct);
                orgScopeAdmittedLeaderArm = allowed2;
                orgScopeReason = reason2;
                if (!allowed2)
                {
                    // S105 / ADR-038 D4 — the edge OR the NEW secondary-unit-leader path (Leader arm only).
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var hasEdgeOrUnit = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        actor.ActorId!, period.EmployeeId, asOf: today, ct: ct);
                    if (!hasEdgeOrUnit)
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
                // S105 / ADR-038 D4/D8 — advisory order `reporting-org-` → `unit-org-` → row FOR UPDATE
                // (the payroll-export FOR UPDATE below). The NEW `unit-org-` advisory serializes the
                // reopen against a concurrent `UnitLeaderRemoved`/member-move on the employee's unit tree.
                var empCurrentOrg = await reportingLineRepo.AcquireTreeLockForEmployeeAsync(conn, tx, period.EmployeeId, ct);
                await UnitRepository.AcquireUnitOrgLockAsync(conn, tx, empCurrentOrg, ct);
                if (!orgScopeAdmittedLeaderArm)
                {
                    var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
                    var stillAuthorized = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        actor.ActorId!, period.EmployeeId, asOf: asOf, ct: ct);
                    if (!stillAuthorized)
                        return Results.Json(new { error = "Access denied", reason = orgScopeReason }, statusCode: 403);
                }
            }

            // ── S90 / TASK-9003 (B2) — the PAYROLL-EXPORT LOCK gate (ADR-034) ──
            // Once a month has been sent to payroll (a payroll_export_records row exists for the period's
            // (employee, year, month)), it can NO LONGER be reopened — corrections only, for ALL roles
            // (OQ-2: no recall, no admin reopen). The check is ADDITIVE and lives INSIDE this tx, AFTER the
            // advisory acquire and BEFORE the conditional UPDATE, so it composes with the existing S78/S83
            // hardening without disturbing it.
            //
            // PLACEMENT (B2 — the export↔reopen TOCTOU race): we DO NOT read the lock at the pre-tx load
            // (:1581). We first take a ROW lock on the approval period (SELECT … FOR UPDATE), which
            // SERIALIZES against the TASK-9002 export tx's own `SELECT … FOR UPDATE` on the same row — so an
            // export commit and a reopen can never interleave on the same period; whichever takes the row
            // lock first wins and the other observes the committed outcome. ONLY THEN do we read
            // payroll_export_records, guaranteeing we see the export's committed lock row (or its absence).
            // The row lock is taken for BOTH arms (the employee arm reaches only EMPLOYEE_APPROVED, which is
            // pre-export, so it will rarely match — but we apply the gate UNIFORMLY: cheap and correct).
            await using (var rowLockCmd = new NpgsqlCommand(
                "SELECT status FROM approval_periods WHERE period_id = @pid FOR UPDATE", conn, tx))
            {
                rowLockCmd.Parameters.AddWithValue("pid", periodId);
                await rowLockCmd.ExecuteScalarAsync(ct);
            }

            // ADR-034 READ-ONLY CROSS-CONTEXT CONTRACT — the Backend READS the Payroll-owned
            // payroll_export_records table to resolve the lock; it must NEVER WRITE it (the Payroll service
            // is the sole writer, TASK-9002). Inlined on the existing (conn, tx) — no Payroll project
            // reference (same DB). The lock key is the period's (employee_id, year, month).
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT 1 FROM payroll_export_records
                WHERE employee_id = @emp AND year = @y AND month = @m
                """, conn, tx))
            {
                lockCmd.Parameters.AddWithValue("emp", period.EmployeeId);
                lockCmd.Parameters.AddWithValue("y", period.PeriodStart.Year);
                lockCmd.Parameters.AddWithValue("m", period.PeriodStart.Month);
                var exported = await lockCmd.ExecuteScalarAsync(ct);
                if (exported is not null)
                {
                    // Discriminated 409 (kind="payroll-locked"), distinct from the status-conflict 409
                    // below. Fires for EVERY role (OQ-2 corrections-only). No mutation has run yet.
                    return Results.Json(new
                    {
                        error = "Period locked",
                        kind = "payroll-locked",
                        reason = "Måneden er sendt til lønkørsel — brug en korrektion.",
                    }, statusCode: 409);
                }
            }

            // S78 R2 — the CONDITIONAL status transition is the FIRST (and only) STATE mutation before the
            // audit + outbox; a null (0-row) loser of a concurrent double-transition short-circuits to a
            // clean 409.
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
