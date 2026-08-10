using System.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;
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
    // S127 / TASK-12705 — the allocation-reconciliation tolerance and the per-day predicate that
    // used to live here as a private const plus three hand-copied inline expressions now live in
    // StatsTid.Backend.Api.AllocationBalance. The three call sites below (the send command's gate,
    // the team-overview hasWarning chip, the allocation-breakdown imbalance flag) all evaluate the
    // rule through AllocationBalance.Evaluate; each still builds its OWN set of days to compare,
    // because the shapes they hold differ and hoisting that loop would cost the read surfaces a
    // roster-wide scan per employee. See AllocationBalance's summary for the full argument.

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
        // ── Send Period (S127 / TASK-12703) ──
        //
        // POST /api/approval/send — the MONTH-KEYED send act, and the FIRST of the two adapters over
        // the one shared command (<see cref="ExecuteSendAsync"/>).
        //
        // RETIRED HERE: POST /api/approval/submit. It took a caller-supplied {periodStart, periodEnd}
        // and wrote SUBMITTED — manager-visible — with NEITHER the workday-coverage check NOR the
        // allocation-reconciliation gate. Two defects fell out of that shape:
        //   • defect 1 — an employee could make a month manager-visible without allocating an hour;
        //   • defect 3 — period identity is the exact tuple (employee_id, period_start, period_end)
        //     (init.sql:892), but the manager's team-overview resolves a period by OVERLAP
        //     (ApprovalPeriodRepository.cs:493-494) while Skema resolves the EXACT month
        //     (SkemaEndpoints.cs:502). A caller-supplied range therefore let a single balanced
        //     weekday stand in for a whole month in the manager's view.
        // The server deriving [monthStart, monthEnd] from (year, month) is what closes defect 3 on
        // the create path; the by-id adapter's whole-month guard closes it on the transition path.
        //
        // Also retired with it: caller-supplied org_id / agreement_code / ok_version / period_type
        // (the old request carried all four). They are SERVER-RESOLVED now — see §3.3 in
        // ExecuteSendAsync — which is a P4 (version-correctness) fix, not a convenience.
        //
        // PeriodSubmitted is RETAINED for replay but is NO LONGER EMITTED by any route: one user
        // action, one event (ADR-012:60) — the send emits PeriodEmployeeApproved only.
        app.MapPost("/api/approval/send", async (
            SendPeriodRequest request,
            ApprovalPeriodRepository approvalRepo,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
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
            // Same (year, month) admission the month-keyed READ surfaces use (:657-659, :1150-1152).
            if (request.Year < 2020 || request.Year > 2100)
                return Results.BadRequest(new { error = "Invalid year. Must be between 2020 and 2100." });
            if (request.Month < 1 || request.Month > 12)
                return Results.BadRequest(new { error = "Invalid month. Must be between 1 and 12." });

            // (1) THE ADAPTER'S period resolution. The range is derived HERE, from (year, month) —
            //     never accepted from the caller. By construction it is a whole calendar month, which
            //     is why this adapter carries no whole-month guard: it cannot produce a partial range.
            var monthStart = new DateOnly(request.Year, request.Month, 1);
            var monthEnd = new DateOnly(request.Year, request.Month,
                DateTime.DaysInMonth(request.Year, request.Month));

            return await ExecuteSendAsync(
                context.GetActorContext(), request.EmployeeId, monthStart, monthEnd,
                new SendCommandServices(
                    approvalRepo, userRepo, userAgreementCodeRepo, scopeValidator, connectionFactory,
                    timeEntryRepo, absenceRepo, workTimeRepo, outbox, auditMapper, auditRepo),
                ct);
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<PeriodActionResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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

            // S116 / TASK-11600 — named record, swapped INSIDE the retry lambda (S115 precedent).
            return Results.Ok(new PeriodActionResponse(PeriodId: periodId, Status: "APPROVED"));
        })).RequireAuthorization("LeaderOrAbove") // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync
        .Produces<PeriodActionResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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

            // S116 / TASK-11600 — named record, swapped INSIDE the retry lambda (S115 precedent).
            return Results.Ok(new PeriodRejectResponse(PeriodId: periodId, Status: "REJECTED", Reason: request.Reason));
        })).RequireAuthorization("LeaderOrAbove") // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync
        .Produces<PeriodRejectResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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

                // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON; the shared
                // pending/by-month 9-field element).
                var myResult = myReportPeriods.Select(p => new ApprovalPeriodListItem(
                    PeriodId: p.PeriodId,
                    EmployeeId: p.EmployeeId,
                    OrgId: p.OrgId,
                    PeriodStart: p.PeriodStart,
                    PeriodEnd: p.PeriodEnd,
                    PeriodType: p.PeriodType,
                    Status: p.Status,
                    SubmittedAt: p.SubmittedAt,
                    AgreementCode: p.AgreementCode)).ToList();

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

            // S116 / TASK-11600 — named record (the SAME shared element as the my-reports branch).
            var result = allPending.Select(p => new ApprovalPeriodListItem(
                PeriodId: p.PeriodId,
                EmployeeId: p.EmployeeId,
                OrgId: p.OrgId,
                PeriodStart: p.PeriodStart,
                PeriodEnd: p.PeriodEnd,
                PeriodType: p.PeriodType,
                Status: p.Status,
                SubmittedAt: p.SubmittedAt,
                AgreementCode: p.AgreementCode)).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("LeaderOrAbove")
        .Produces<IEnumerable<ApprovalPeriodListItem>>(StatusCodes.Status200OK); // S116 / TASK-11600 — a BARE ARRAY

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

                // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON; the shared
                // pending/by-month 9-field element).
                var myResult = myReportPeriods.Select(p => new ApprovalPeriodListItem(
                    PeriodId: p.PeriodId,
                    EmployeeId: p.EmployeeId,
                    OrgId: p.OrgId,
                    PeriodStart: p.PeriodStart,
                    PeriodEnd: p.PeriodEnd,
                    PeriodType: p.PeriodType,
                    Status: p.Status,
                    SubmittedAt: p.SubmittedAt,
                    AgreementCode: p.AgreementCode)).ToList();

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

            // S116 / TASK-11600 — named record (the SAME shared element as the my-reports branch).
            var result = allPeriods.Select(p => new ApprovalPeriodListItem(
                PeriodId: p.PeriodId,
                EmployeeId: p.EmployeeId,
                OrgId: p.OrgId,
                PeriodStart: p.PeriodStart,
                PeriodEnd: p.PeriodEnd,
                PeriodType: p.PeriodType,
                Status: p.Status,
                SubmittedAt: p.SubmittedAt,
                AgreementCode: p.AgreementCode)).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("LeaderOrAbove")
        .Produces<IEnumerable<ApprovalPeriodListItem>>(StatusCodes.Status200OK); // S116 / TASK-11600 — a BARE ARRAY

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
                // S116 / TASK-11600 — named record; the empty-roster early return serializes the
                // SAME envelope shape ({ employees: [] }) as the assembled site below.
                return Results.Ok(new TeamOverviewResponse(Array.Empty<TeamOverviewEmployeeRow>()));

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
            //
            //      S126 / F5 — this WAS that query, hand-rolled inline with a `data->>'newBalance'`
            //      extraction and its own NumberStyles/InvariantCulture parse. It is now the batch
            //      shape of the shared reader, so the four flex readers in this codebase share ONE
            //      encoding of "latest event of this type" and the camelCase JSON key stays inside
            //      EventSerializer instead of being restated here.
            var flexByEmployee = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var flexStreamIds = employeeIds.Select(id => $"employee-{id}").ToArray();
            var latestFlexByStream = await PostgresEventStore
                .ReadLatestOfTypePerStreamAsync<FlexBalanceUpdated>(conn, tx: null, flexStreamIds, ct);
            foreach (var (streamId, flexEvent) in latestFlexByStream)
            {
                var empId = streamId.StartsWith("employee-", StringComparison.Ordinal)
                    ? streamId.Substring("employee-".Length)
                    : streamId;
                flexByEmployee[empId] = flexEvent.NewBalance;
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
            var employees = new List<TeamOverviewEmployeeRow>(roster.Count);
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

                // S124 / TASK-12402 — the manager-visibility rule. `submittedToManager` is keyed on
                // STATUS, not on `r.PeriodId`: a REAL DRAFT period row (created-but-not-submitted, or
                // one that came back to DRAFT) must be blanked exactly like the synthetic zero-period
                // row. Keying on periodId would blank only the synthetic case and leak the real one.
                //
                // ── S127 / TASK-12703 — AMENDING THE S124 REJECTED RULING (owner ruling R1) ──
                // S124 decided the opposite of what this code now does, and its reasoning was written
                // here verbatim:
                //
                //     "REJECTED counts as submitted: the employee DID send it, the leader decided on
                //      these very numbers, and hiding them afterwards would erase the basis of that
                //      decision."
                //
                // That is answered, not deleted. The answer is that a REJECTED month is not a decided
                // month — it is one the employee is actively editing again. `/api/time-entries` and
                // Skema both accept writes into a REJECTED period (only EMPLOYEE_APPROVED and APPROVED
                // lock the grid), so the figures a manager reads off a rejected row are NOT "these very
                // numbers" the decision was taken on; they are whatever the employee has typed since.
                // Showing them presents in-progress work as if it were a submission, which is exactly
                // what ruling R1 forbids ("a manager never sees a month the employee could not
                // certify"). The BASIS of the decision is preserved by what stays visible on the row —
                // the row still carries Status, SubmittedAt, DecisionAt and RejectionReason (see the
                // TeamOverviewEmployeeRow construction below, where only the five month-derived
                // figures are gated on `submittedToManager`) — and by the audit trail, which is the
                // durable record of what was decided. What is withheld is only the live,
                // still-moving month figures.
                //
                // Scope of the reversal, stated because it is narrower than it looks (ruling R5): this
                // predicate governs TWO display surfaces — this row and the Skema leader tier
                // (SkemaEndpoints.cs:515). The sibling read endpoints (allocation-breakdown,
                // compliance, balance, raw time entries, absences) authorize the same manager
                // population and read the projections with NO period-status gate, so a manager who
                // calls them directly still sees a rejected month's current figures. That gap is
                // RECORDED in RES-002 as its open follow-up — it is deliberately not closed here, and
                // this change must not be described as access-control enforcement.
                var submittedToManager = ApprovalVisibility.IsSubmittedToManager(status);

                var normRegistered = registeredByEmployee.GetValueOrDefault(r.EmployeeId, 0m);
                var overtime = Math.Max(0m, normRegistered - normExpected);

                var (ferieUsed, ferieTotal) = ferieByEmployee.GetValueOrDefault(r.EmployeeId, (0m, 0m));
                var flexBalance = flexByEmployee.GetValueOrDefault(r.EmployeeId, 0m);
                var awayToday = awayTodayAvailable && awayTodaySet.Contains(r.EmployeeId);

                // payrollExported = the month is sent to lønkørsel (a payroll_export_records row
                // exists for this employee + (year, month)). The FE gates the reopen control on this.
                var payrollExported = payrollExportedByEmployee.TryGetValue(r.EmployeeId, out var exportedAt);

                // hasWarning = the cheap allocation-imbalance warning (ANY day in the month is
                // imbalanced by the shared per-day predicate). Mirrors the allocation arm of the send
                // gate SYMMETRICALLY — both under- AND over-allocation are un-sendable (S87 Step-7a) —
                // and since S127/TASK-12705 that is no longer a claim about two copies agreeing: the
                // verdict comes from AllocationBalance.Evaluate, the same call the gate makes. NO
                // rule-engine / compliance call. A named P1 narrowing: it does NOT mirror the
                // coverage/uncovered-days arm, so false ≠ sendable.
                //
                // The day-set construction stays local and stays SHAPED FOR THIS SURFACE: the two
                // dictionaries are keyed by (employeeId, date) because they were batched across the
                // whole roster in one query, so the cheap probe is to walk the month's ~31 days and
                // look each candidate up. Asking a shared helper for "the days either map mentions for
                // this employee" would mean scanning the roster-wide key set once per employee.
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
                    var day = AllocationBalance.Evaluate(
                        workedByEmployeeDay.GetValueOrDefault((r.EmployeeId, d), 0m),
                        allocatedByEmployeeDay.GetValueOrDefault((r.EmployeeId, d), 0m));
                    if (day.IsImbalanced)
                    {
                        hasWarning = true;
                        break;
                    }
                }

                // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON; the 18-field
                // handler-assembled Teamoversigt row).
                employees.Add(new TeamOverviewEmployeeRow(
                    PeriodId: r.PeriodId,
                    EmployeeId: r.EmployeeId,
                    DisplayName: r.DisplayName,
                    Agreement: agreement,
                    Status: status,
                    SubmittedAt: period?.SubmittedAt,
                    DecisionAt: decisionAt,
                    RejectionReason: rejectionReason,
                    NormExpected: normExpected,
                    // The five month-derived fields are withheld unless the employee actually sent
                    // the period. Withheld SERVER-side, so the value never reaches the client at all —
                    // blanking in the view would leave it readable in the network response, which is
                    // presentation, not access control.
                    NormRegistered: submittedToManager ? normRegistered : null,
                    FlexBalance: submittedToManager ? flexBalance : null,
                    Overtime: submittedToManager ? overtime : null,
                    FerieUsed: submittedToManager ? ferieUsed : null,
                    FerieTotal: ferieTotal,
                    AwayToday: awayToday,
                    HasWarning: submittedToManager ? hasWarning : null,
                    PayrollExported: payrollExported,
                    PayrollExportedAt: payrollExported ? exportedAt : (DateTime?)null));
            }

            return Results.Ok(new TeamOverviewResponse(employees));
        }).RequireAuthorization("LeaderOrAbove")
        .Produces<TeamOverviewResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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
        //   hasAllocationImbalance — the AUTHORITATIVE per-day ANY check: iterate the days with either
        //     worked or allocated and ask AllocationBalance.Evaluate — the SAME call the aggregate's
        //     hasWarning loop and the send gate make (S127/TASK-12705; before that it was a third
        //     hand-copy of the expression, held to the other two by this comment alone).
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
            var allocations = new List<AllocationBreakdownItem>();
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
                    allocations.Add(new AllocationBreakdownItem(TaskId: reader.GetString(0), Hours: reader.GetDecimal(1)));
            }

            // (d) The month totals + the per-day directional sums + the AUTHORITATIVE per-day ANY check.
            //
            // ⚠ The two month totals are reported RAW — summed before any rounding — and TASK-12700's
            // characterization baseline PINS them that way (case C5 is the only row where raw and
            // rounded diverge). Rounding happens per day, inside the comparison below, and must stay
            // there: routing these two sums through the shared per-day predicate would change the wire
            // response and turn that baseline red.
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
                // AUTHORITATIVE imbalance = the SAME per-day call the table hasWarning and the send
                // gate make (both directions). NOT derived from the summed under/over (B1 drift) —
                // which is why the directional sums are taken from the SAME evaluated day rather than
                // recomputed: one rounding, one verdict, three figures.
                var day = AllocationBalance.Evaluate(
                    workedByDay.GetValueOrDefault(d, 0m),
                    allocatedByDay.GetValueOrDefault(d, 0m));
                underAllocated += day.UnderAllocated;
                overAllocated += day.OverAllocated;
                if (day.IsImbalanced)
                    hasAllocationImbalance = true;
            }

            // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON).
            return Results.Ok(new AllocationBreakdownResponse(
                Allocations: allocations,
                Worked: worked,
                Allocated: allocated,
                UnderAllocated: Math.Round(underAllocated, 2),
                OverAllocated: Math.Round(overAllocated, 2),
                HasAllocationImbalance: hasAllocationImbalance));
        }).RequireAuthorization("LeaderOrAbove")
        .Produces<AllocationBreakdownResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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

            // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON; the WIDER 14-field
            // per-employee period row — deliberately NOT the shared pending/by-month element).
            var result = periods.Select(p => new EmployeePeriodItem(
                PeriodId: p.PeriodId,
                EmployeeId: p.EmployeeId,
                OrgId: p.OrgId,
                PeriodStart: p.PeriodStart,
                PeriodEnd: p.PeriodEnd,
                PeriodType: p.PeriodType,
                Status: p.Status,
                AgreementCode: p.AgreementCode,
                OkVersion: p.OkVersion,
                SubmittedAt: p.SubmittedAt,
                ApprovedBy: p.ApprovedBy,
                ApprovedAt: p.ApprovedAt,
                RejectionReason: p.RejectionReason,
                CreatedAt: p.CreatedAt)).ToList();

            return Results.Ok(result);
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<EmployeePeriodItem>>(StatusCodes.Status200OK); // S116 / TASK-11600 — a BARE ARRAY

        // ── Employee Approve Period — the BY-ID send adapter (S127 / TASK-12703) ──
        //
        // The SECOND adapter over the same command, not a second implementation. Its only job is to
        // turn a period_id into the (employeeId, monthStart, monthEnd) triple the command is keyed on,
        // and to refuse ranges the command cannot honestly represent.
        //
        // Its caller is the re-send button on *Mine perioder*, which renders on DRAFT or REJECTED
        // (MyPeriods.tsx:324) — SUBMITTED rows reach it too, because a legacy row's only route to the
        // certified state is through this command (§3.2).
        app.MapPost("/api/approval/{periodId}/employee-approve", async (
            Guid periodId,
            ApprovalPeriodRepository approvalRepo,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
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
            // (1) THE ADAPTER PRE-READ. /send carries employeeId in the body; this route carries only
            //     a period_id, so it must read the row to learn (a) the employee — the ADVISORY LOCK
            //     KEY, which must be known BEFORE the lock is taken — and (b) the range, for the
            //     whole-month guard below.
            //
            //     This read is deliberately OUTSIDE the lock and needs NO drift guard, and that is a
            //     property of the schema rather than an assumption: employee_id, period_start and
            //     period_end are written ONLY by an INSERT. No production UPDATE touches them —
            //     BuildUpdateStatusCommand's status switch writes status/timestamps/decision fields,
            //     StampSendAsync writes the send stamp + the three resolved dimensions, and
            //     UpdateDeadlinesAsync writes the two deadlines. There is no production DELETE at all.
            //     So the triple this read yields cannot go stale, and the AUTHORITATIVE read of
            //     everything that CAN change (status, and the row's existence) happens inside the lock
            //     in the shared command.
            var period = await approvalRepo.GetByIdAsync(periodId, ct);
            if (period is null)
                return Results.NotFound(new { error = "Period not found" });

            // (1b) THE WHOLE-MONTH GUARD (defect 3, transition path). Exact-tuple uniqueness
            //      (init.sql:892) lets a canonical full-month row and an overlapping partial row
            //      coexist, and the manager's overlap join takes ORDER BY ap.period_start DESC
            //      (ApprovalPeriodRepository.cs:493-495) — so a partial row that transitioned would
            //      be what the manager sees for the whole month.
            //
            //      A BOUNDARY check, deliberately NOT a period_type check: a WEEKLY row that happens
            //      to span an exact calendar month is ACCEPTED. Checking the type instead would make
            //      every legacy WEEKLY row permanently unsendable, which is a different decision with
            //      its own migration cost (refinement AC-10).
            //
            //      This guard runs BEFORE the command's role floor, because it is part of resolving
            //      the period rather than of deciding it. The body therefore echoes NO period data:
            //      the 404 above is pre-existing pre-authorization disclosure, and there is no reason
            //      to widen it. The only caller already holds the row it is sending.
            if (!IsWholeCalendarMonth(period.PeriodStart, period.PeriodEnd))
                return Results.Conflict(new
                {
                    error = "Cannot send a period that is not a whole calendar month.",
                    kind = "not-whole-month",
                });

            return await ExecuteSendAsync(
                context.GetActorContext(), period.EmployeeId, period.PeriodStart, period.PeriodEnd,
                new SendCommandServices(
                    approvalRepo, userRepo, userAgreementCodeRepo, scopeValidator, connectionFactory,
                    timeEntryRepo, absenceRepo, workTimeRepo, outbox, auditMapper, auditRepo),
                ct);
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<PeriodActionResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

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

            // S116 / TASK-11600 — named record, swapped INSIDE the retry lambda (S115 precedent).
            return Results.Ok(new PeriodActionResponse(PeriodId: periodId, Status: "DRAFT"));
        })).RequireAuthorization("EmployeeOrAbove") // S78 R1: extra ) closes TreeRootDriftRetry.RunAsync
        .Produces<PeriodActionResponse>(StatusCodes.Status200OK); // S116 / TASK-11600

        return app;
    }

    // ── The shared send command (S127 / TASK-12703) ──────────────────────────────────────────────

    /// <summary>
    /// S127 / TASK-12703 — the source states a send may transition FROM.
    ///
    /// <para><c>SUBMITTED</c> is a member deliberately: it is the ONLY route the 138 legacy rows
    /// written by the retired <c>/submit</c> have to the certified state, and admitting them is also
    /// what makes the follow-up UPDATE's dimension correction (§3.3) reach them. It introduces no
    /// downgrade — <c>EMPLOYEE_APPROVED</c> and <c>APPROVED</c> stay excluded, so a second send of an
    /// already-sent month is a 409, never a walk backwards.</para>
    /// </summary>
    private static readonly string[] AllowedSendSourceStates = { "DRAFT", "SUBMITTED", "REJECTED" };

    /// <summary>
    /// Is <paramref name="start"/>..<paramref name="end"/> exactly one calendar month — first day to
    /// last day, same month? A BOUNDARY predicate: it says nothing about <c>period_type</c>, so a
    /// WEEKLY row spanning an exact month passes (refinement AC-10).
    /// </summary>
    private static bool IsWholeCalendarMonth(DateOnly start, DateOnly end) =>
        start.Day == 1
        && end.Year == start.Year
        && end.Month == start.Month
        && end.Day == DateTime.DaysInMonth(start.Year, start.Month);

    /// <summary>
    /// The services <see cref="ExecuteSendAsync"/> needs, bundled so the two adapters can hand the
    /// command one argument instead of eleven. Each adapter builds it from its OWN injected minimal-API
    /// parameters — this record is never model-bound, so it is invisible to the OpenAPI generator.
    /// </summary>
    private sealed record SendCommandServices(
        ApprovalPeriodRepository ApprovalRepo,
        UserRepository UserRepo,
        UserAgreementCodeRepository UserAgreementCodeRepo,
        OrgScopeValidator ScopeValidator,
        DbConnectionFactory ConnectionFactory,
        TimeEntryProjectionRepository TimeEntryRepo,
        AbsenceProjectionRepository AbsenceRepo,
        WorkTimeProjectionRepository WorkTimeRepo,
        IOutboxEnqueue Outbox,
        IAuditProjectionMapper<PeriodEmployeeApproved> AuditMapper,
        AuditProjectionRepository AuditRepo);

    /// <summary>
    /// S127 / TASK-12703 — THE send command. Both routes end here: <c>POST /api/approval/send</c>
    /// (month-keyed) and <c>POST /api/approval/{periodId}/employee-approve</c> (by id). Everything
    /// after period resolution — lock, floor, validation, transition, timestamps, resolved dimensions,
    /// event, audit — is this one code path, with NO route-specific branches.
    ///
    /// <para><b>The invariant it exists to hold:</b> every transition into a manager-visible state
    /// goes through one validated command, keyed on a whole month, executed under the per-employee
    /// advisory lock that every request-path projection writer holds. Three qualifications, each
    /// stated because leaving one out would overclaim: <c>ProjectionBackfillService</c> is NOT
    /// enrolled (a deliberate carve-out, refinement §3.4); legacy <c>SUBMITTED</c> rows stay
    /// manager-approvable WITHOUT validation (owner ruling R6 — the hole is accepted, not closed);
    /// and <c>POST /api/time-entries</c> has no approval-status check, so the lock stops a write
    /// racing INSIDE a send but not one landing AFTER it.</para>
    ///
    /// <para><b>Ordering is the contract.</b> The by-id route's checks all used to run BEFORE its
    /// transaction opened, which made them advisory. Required order, both adapters:
    /// (1) adapter pre-read → (2) tx at READ COMMITTED → (3) advisory lock, first statement →
    /// (4) authoritative re-read by natural key → (5) role floor, then coverage, then allocation →
    /// (6) conditional transition, follow-up UPDATE, event, audit.</para>
    /// </summary>
    /// <param name="monthStart">The first day of the month. Server-derived on both adapters.</param>
    /// <param name="monthEnd">The last day of that same month.</param>
    private static async Task<IResult> ExecuteSendAsync(
        ActorContext actor,
        string employeeId,
        DateOnly monthStart,
        DateOnly monthEnd,
        SendCommandServices svc,
        CancellationToken ct)
    {
        // ── (2) THE TRANSACTION — ISOLATION PINNED EXPLICITLY, NOT THE DEFAULT OVERLOAD ──────────
        //
        // READ COMMITTED (not RepeatableRead) is load-bearing, and the whole concurrency argument
        // below rests on it: the tx's first statement is a pg_advisory_xact_lock that BLOCKS until the
        // lock is granted, and the loser of a create race must, on the very next read, SEE the
        // winner's committed INSERT. A RepeatableRead snapshot is pinned BEFORE the lock is granted,
        // so the loser would still miss that row, take the create arm, and collide. READ COMMITTED
        // gives each post-lock statement a fresh snapshot — correct for a lock-serialized critical
        // section. Same hazard, same pin, as ReportingLineEndpoints.cs:1787 / :2470,
        // ReportingLineRepository.cs:216-223 and SettlementCloseService.cs:363-367.
        //
        // Corollary, and a real trap: NEVER pass an ApprovalAuthorityContext on this transaction.
        // DesignatedApproverAuthorizer.EnsureContextIsSnapshotBound (:465) THROWS unless it is given
        // RepeatableRead or stronger, because memoized authority answers are only equivalent to
        // re-querying inside a pinned snapshot. This command therefore resolves no approval authority
        // at all — its authorization is org-scope + the role floor, which is a per-request verdict.
        await using var conn = svc.ConnectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // ── (3) THE PER-EMPLOYEE ADVISORY LOCK — FIRST STATEMENT IN THE TRANSACTION ──────────────
        //
        // The SAME lock Skema's writer takes (ADR-032 D4). Not a second lock: a second advisory key
        // would add a lock-order edge for nothing. Held to commit.
        //
        // What it buys: every read this command makes afterwards observes state that no enrolled
        // request-path writer can move before we commit. The projection reads below run on their
        // repositories' OWN connections, which is fine and is the same reasoning ADR-032 D4 records
        // for the profile resolver — the lock serializes the WRITERS, and because it is acquired
        // BEFORE those reads are issued, whatever they see committed is still what is committed when
        // this transaction commits.
        await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

        // ── (4) THE AUTHORITATIVE RE-READ, INSIDE THE LOCK, BY NATURAL KEY ───────────────────────
        //
        // By natural key and not by id, on BOTH adapters. The loser of a create race has no id to
        // read by — "what row exists for (employee, month)?" is the only question that survives the
        // race, and it is the question the unique constraint answers (init.sql:892).
        var existing = await svc.ApprovalRepo.GetByEmployeeAndPeriodAsync(
            conn, tx, employeeId, monthStart, monthEnd, ct);

        // ── (5a) THE ROLE FLOOR (owner ruling R4) ────────────────────────────────────────────────
        //
        // "A leader may not send for another employee." Self, or LocalHR-and-above acting for
        // another. A LocalLeader sending for someone else gets 403 — an AUTHORIZATION decision, not a
        // 422: they are not permitted to perform the act at all, which is a different statement from
        // "the month is not ready".
        //
        // Mechanism: the per-scope roleFloor parameter (OrgScopeValidator.cs:56-112) — a scope below
        // the floor never admits, so a mixed-role actor's LEADER scope cannot carry the send while
        // their HR scope, if any, still can. SELF IS EXEMPT and that exemption is load-bearing: a
        // LocalLeader is also an employee who sends their OWN month; they are not Employee-role, so
        // they fall through to this branch, and an unconditional floor would lock every leader out of
        // their own timesheet. The live idiom is SkemaEndpoints.cs:637-641.
        if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
            return Results.Json(
                new { error = "Access denied", reason = "Employee can only send own periods" },
                statusCode: 403);

        if (actor.ActorRole != StatsTidRoles.Employee)
        {
            var sendFloor = string.Equals(employeeId, actor.ActorId, StringComparison.Ordinal)
                ? null
                : StatsTidRoles.LocalHR;
            var (allowed, reason) = await svc.ScopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, sendFloor, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
        }

        // ── (5b) THE SOURCE-STATE GATE ───────────────────────────────────────────────────────────
        //
        // Read off the AUTHORITATIVE in-lock row, so the message names the status that is really
        // there. This is NOT the enforcement point — the conditional UPDATE at (6) is, and it stays
        // load-bearing: writers that do NOT hold the employee- advisory (manager approve/reject, the
        // leader arm of reopen) can still commit a status change between this read and that UPDATE.
        // This gate exists to give the common case an honest 409 instead of the generic one.
        if (existing is not null && !AllowedSendSourceStates.Contains(existing.Status))
            return Results.Conflict(new
            {
                error = $"Cannot send period with status {existing.Status}. " +
                        "Only DRAFT, SUBMITTED, or REJECTED periods can be sent.",
            });

        // ── (5c) THE SERVER-RESOLVED DIMENSIONS (P4) ─────────────────────────────────────────────
        //
        // org_id / agreement_code / ok_version are resolved HERE and carried through the follow-up
        // UPDATE at (6), so they are written on the create arm AND corrected on the transition arm.
        // That second half is the point: the retired /submit took all three straight off the request
        // body and INSERTed them, and AllowedSendSourceStates admits exactly those rows (SUBMITTED) as
        // sources — so a re-send now REPAIRS a wrong stored value rather than preserving it.
        //
        //   • agreement_code — AT THE MONTH BEING SENT, mirroring Skema (SkemaEndpoints.cs:675-677).
        //     users.agreement_code is a live-only cache; reading it would stamp today's agreement onto
        //     a March month sent in April. The dated lookup is the authority, with the cache as the
        //     documented graceful fallback (ADR-023 D3) for a user created after the period.
        //   • ok_version   — OkVersionResolver at monthStart, NOT today. The OK24→OK26 boundary is
        //     2026-04-01, so a March 2026 month sent in April must record OK24.
        //   • org_id       — the employee's CURRENT primary org (deliberate). It is the same value the
        //     audit projection resolves as ResolvedTargetOrgId, so the row and its audit trail cannot
        //     disagree about which organisation the month belongs to.
        var user = await svc.UserRepo.GetByIdAsync(conn, tx, employeeId, ct);
        if (user is null)
            return Results.NotFound(new { error = "Employee not found" });

        var orgId = user.PrimaryOrgId;
        var agreementCode =
            await svc.UserAgreementCodeRepo.GetByUserIdAtAsync(employeeId, monthStart, ct)
            ?? user.AgreementCode;
        var okVersion = OkVersionResolver.ResolveVersion(monthStart);

        // ── (5d) WORKDAY COVERAGE VALIDATION ─────────────────────────────────────────────────────
        // Every expected workday in the month must carry at least one time entry or absence
        // registration. Fires BEFORE the allocation gate; its {missingDays} 422 shape is unchanged.

        // 1. Danish public holidays in range. On the tx connection — static reference data, and it
        //    saves a pooled connection.
        var holidays = new HashSet<DateOnly>();
        await using (var holidayCmd = new NpgsqlCommand(
            "SELECT holiday_date FROM danish_public_holidays WHERE holiday_date >= @start AND holiday_date <= @end",
            conn, tx))
        {
            holidayCmd.Parameters.AddWithValue("start", monthStart);
            holidayCmd.Parameters.AddWithValue("end", monthEnd);
            await using var holidayReader = await holidayCmd.ExecuteReaderAsync(ct);
            while (await holidayReader.ReadAsync(ct))
                holidays.Add(holidayReader.GetFieldValue<DateOnly>(0));
        }

        // 2. Expected workdays (weekdays minus public holidays).
        var expectedWorkdays = new List<DateOnly>();
        for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            if (holidays.Contains(d))
                continue;
            expectedWorkdays.Add(d);
        }

        // 3. Time entries and absences for the employee + month.
        var timeEntries = await svc.TimeEntryRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, monthStart, monthEnd, ct);
        var absences = await svc.AbsenceRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, monthStart, monthEnd, ct);

        // 4. Which workdays carry at least one registration.
        var entryDates = new HashSet<DateOnly>(timeEntries.Select(e => e.Date));
        var absenceDates = new HashSet<DateOnly>(absences.Select(a => a.Date));

        var uncoveredDays = expectedWorkdays
            .Where(d => !entryDates.Contains(d) && !absenceDates.Contains(d))
            .ToList();

        // 5. Reject if any workday is uncovered.
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

        // ── (5e) ALLOCATION-RECONCILIATION GATE (TASK-5604; the owner's reported defect) ─────────
        // HARD precondition ALONGSIDE coverage: for EVERY day in the month, recorded worked hours
        // (work_time_projection: interval hours + manual_hours) must match allocated project hours
        // (NORMAL time entries with a non-null TaskId) within rounding tolerance. Deterministic and
        // read-only over projections — no events, no rule-engine call (P2). Absences are excluded
        // (they are not time_entries). The NORMAL + non-null-TaskId allowlist mirrors the grid's
        // allocation predicate so this gate and the frontend "Ikke fordelt" row agree (historical
        // activity_type='timer' and null-TaskId rows excluded).

        // worked(day): interval hours + manual_hours from work_time_projection.
        var workTimeRows = await svc.WorkTimeRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, monthStart, monthEnd, ct);
        var workedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var row in workTimeRows)
        {
            var worked = SumIntervalHours(row.Intervals) + row.ManualHours;
            workedByDay[row.Date] = workedByDay.TryGetValue(row.Date, out var existingWorked)
                ? existingWorked + worked
                : worked;
        }

        // allocated(day): reuse the time-entry list already loaded for coverage (no re-query);
        // filter to NORMAL + non-null TaskId.
        var allocatedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var entry in timeEntries)
        {
            if (entry.ActivityType != "NORMAL" || entry.TaskId is null)
                continue;
            allocatedByDay[entry.Date] = allocatedByDay.TryGetValue(entry.Date, out var existingAlloc)
                ? existingAlloc + entry.Hours
                : entry.Hours;
        }

        // Compare every day with either worked or allocated hours. Days with worked==0 AND
        // allocated==0 are implicitly balanced (skipped). The verdict, the rounded figures the 422
        // echoes back and the direction all come from ONE AllocationBalance.Evaluate call — the same
        // call the team-overview chip and the allocation-breakdown flag make, so a month that shows a
        // warning to the manager is exactly a month this gate refuses (S127/TASK-12705).
        var unbalancedDays = new List<object>();
        foreach (var date in workedByDay.Keys.Union(allocatedByDay.Keys).OrderBy(d => d))
        {
            var day = AllocationBalance.Evaluate(
                workedByDay.GetValueOrDefault(date),
                allocatedByDay.GetValueOrDefault(date));
            if (day.IsBalanced)
                continue;
            unbalancedDays.Add(new
            {
                date = date.ToString("yyyy-MM-dd"),
                worked = day.Worked,
                allocated = day.Allocated,
                direction = day.Direction,
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

        // ── (6) THE STATE CHANGE — create-if-absent, then ONE conditional transition ─────────────
        //
        // Nothing above this line has mutated anything, so every rejection path leaves the row (or
        // the absence of a row) exactly as it found it: the transaction is disposed without a commit
        // and rolls back, and no audit or outbox row was written either.
        Guid periodId;
        if (existing is null)
        {
            // The create arm. The row is born DRAFT and transitions below, so there is exactly ONE
            // transition statement for both arms rather than two spellings of the same rule.
            //
            // ON CONFLICT … DO NOTHING RETURNING, never catch(23505)-and-continue: a unique violation
            // ABORTS the PostgreSQL transaction (25P02 on every subsequent statement), and this
            // command must keep working after losing a race — it still has audit and outbox rows to
            // write. PAT-013.
            //
            // Because the existence read at (4) is INSIDE the lock, a null here is defence in depth
            // rather than the primary loser path: after this change the only production writer of
            // approval_periods rows is this command, and it holds the lock. The honest disposition
            // for a null is a 409 — some other transaction owns that natural key now.
            var created = await svc.ApprovalRepo.TryCreateIfAbsentAsync(conn, tx, new ApprovalPeriod
            {
                PeriodId = Guid.NewGuid(), // ignored — TryCreateIfAbsentAsync mints the id it returns
                EmployeeId = employeeId,
                OrgId = orgId,
                PeriodStart = monthStart,
                PeriodEnd = monthEnd,
                PeriodType = "MONTHLY",
                Status = "DRAFT",
                AgreementCode = agreementCode,
                OkVersion = okVersion,
            }, ct);

            if (created is null)
                return Results.Conflict(new { error = "Period status changed concurrently; refresh and retry." });

            periodId = created.Value;
        }
        else
        {
            periodId = existing.PeriodId;
        }

        // THE conditional transition. Replaces the unconditional UpdateStatusAsync the by-id route
        // used to call: the guard is what makes a concurrent decision a clean 409 instead of a silent
        // overwrite of someone else's transition. It row-locks (FOR UPDATE in its subselect, plus the
        // UPDATE itself) and PostgreSQL holds row locks to end-of-transaction — which is what lets
        // the follow-up UPDATE below carry no source-state guard of its own.
        //
        // On the create arm this transitions the row this transaction just INSERTed; FOR UPDATE on
        // one's own uncommitted row is legal and returns 'DRAFT'.
        var previousStatus = await svc.ApprovalRepo.TryUpdateStatusConditionalAsync(
            conn, tx, periodId, "EMPLOYEE_APPROVED", AllowedSendSourceStates, actor.ActorId, ct: ct);
        if (previousStatus is null)
            return Results.Conflict(new { error = "Period status changed concurrently; refresh and retry." });

        // THE FOLLOW-UP UPDATE. Two jobs the status switch cannot do:
        //   (a) the EMPLOYEE_APPROVED SET branch leaves submitted_at NULL (it writes only
        //       employee_approved_at/by), and submitted_at means THE SEND ACT, not "the old endpoint
        //       ran" — ADR-012:60 makes the employee's self-approval the submission act, and the
        //       deadlines a few lines down already re-stamp on every send. BOTH adapters stamp,
        //       including a reopen → re-send: reopen NULLs the whole decision record and DRAFT is not
        //       manager-visible, so a re-send is a genuinely new send.
        //   (b) it carries the three server-resolved dimensions onto the transition arm, where they
        //       would otherwise be computed and discarded (§3.3).
        // No source-state guard here is correct, not an omission: the conditional statement above
        // holds this row's lock to end-of-transaction.
        await svc.ApprovalRepo.StampSendAsync(
            conn, tx, periodId, actor.ActorId!, orgId, agreementCode, okVersion, ct);

        // Deadlines (in-tx). monthEnd IS the month's last day — both adapters guarantee it — so this
        // is the same +2 / +5 the by-id route has always written.
        await svc.ApprovalRepo.UpdateDeadlinesAsync(
            conn, tx, periodId, monthEnd.AddDays(2), monthEnd.AddDays(5), ct);

        // Audit trail (in-tx). The action stays the LITERAL "SUBMITTED": approval_audit.action's
        // CHECK (init.sql:903) has no EMPLOYEE_APPROVED member, and this route has always written
        // "SUBMITTED" here. A new vocabulary would need the CHECK widened first.
        //
        // The COMMENT is conditional, and only because R4 makes the non-self case a SANCTIONED path:
        // an HR user may now legitimately send for another employee, and writing the unconditional
        // "Employee self-approval" onto that row would be a false audit statement (P3). The self
        // comment is unchanged, so the dominant path stays byte-stable.
        var isSelfSend = string.Equals(employeeId, actor.ActorId, StringComparison.Ordinal);
        await svc.ApprovalRepo.AppendAuditAsync(
            conn, tx, periodId, "SUBMITTED", actor.ActorId!, actor.ActorRole ?? StatsTidRoles.Employee,
            isSelfSend
                ? "Employee self-approval"
                : $"Sent on behalf of {employeeId}",
            ct);

        // ONE outbox event: PeriodEmployeeApproved. One user action, one event (ADR-012:60).
        // PeriodSubmitted is retained for replay and no longer emitted. PeriodType is deliberately
        // NOT added to this event — nothing consumes details.periodType, and adding it would make the
        // audit-detail shape non-uniform across replayed history.
        var streamId = $"approval-{employeeId}-{monthStart:yyyy-MM-dd}";
        var @event = new PeriodEmployeeApproved
        {
            PeriodId = periodId,
            EmployeeId = employeeId,
            // The RESOLVED org, matching the row this transaction just corrected — not a stale
            // caller-supplied value read back off a legacy row.
            OrgId = orgId,
            PeriodStart = monthStart,
            PeriodEnd = monthEnd,
            ActorId = actor.ActorId,
            ActorRole = actor.ActorRole,
            CorrelationId = actor.CorrelationId
        };
        // S44 TASK-4413: capture outbox_id for the audit_projection insert (ADR-026 D2 sync-in-tx
        // projection write — atomic with the approval_periods row + outbox row per ADR-018 D3/D13).
        var outboxId = await svc.Outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

        var auditCtx = new AuditProjectionContext(
            ActorId: actor.ActorId,
            ActorPrimaryOrgId: actor.OrgId,
            CorrelationId: actor.CorrelationId,
            OccurredAt: new DateTimeOffset(@event.OccurredAt),
            // The employee row was read in-tx at (5c) and its absence already returned 404, so this
            // is the same value org_id was just set to — no second lookup, no way to disagree.
            ResolvedTargetOrgId: user.PrimaryOrgId);
        var auditRow = svc.AuditMapper.Map(@event, auditCtx);
        await svc.AuditRepo.InsertAsync(
            conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

        await tx.CommitAsync(ct);

        // S116 / TASK-11600 — named record (BYTE-IDENTICAL wire JSON; the shared action receipt).
        return Results.Ok(new PeriodActionResponse(PeriodId: periodId, Status: "EMPLOYEE_APPROVED"));
    }

    // ── Request DTOs ──

    private sealed class ReopenPeriodRequest
    {
        public string? Reason { get; init; }
    }

    /// <summary>
    /// S127 / TASK-12703 — the <c>POST /api/approval/send</c> body. Replaces the retired
    /// <c>SubmitPeriodRequest</c>, which carried SEVEN caller-supplied fields; five of them are gone
    /// because the server is the authority on all five:
    /// <c>periodStart</c>/<c>periodEnd</c> → derived from (year, month); <c>orgId</c>,
    /// <c>agreementCode</c>, <c>okVersion</c> → resolved in <see cref="ExecuteSendAsync"/>;
    /// <c>periodType</c> → always MONTHLY on a created row.
    /// </summary>
    private sealed class SendPeriodRequest
    {
        public required string EmployeeId { get; init; }
        public required int Year { get; init; }
        public required int Month { get; init; }
    }

    private sealed class RejectPeriodRequest
    {
        public required string Reason { get; init; }
    }
}
