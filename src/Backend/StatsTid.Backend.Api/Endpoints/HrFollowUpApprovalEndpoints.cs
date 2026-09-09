using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S140 / TASK-14004 (refinement B2) — the HR follow-up reads for the APPROVAL, EMPLOYMENT-LIFECYCLE
/// and ORGANISATION processes. Five endpoints, all <c>HROrAbove</c>, all READ-ONLY.
///
/// <para><b>What this gives HR, in plain language.</b> Every month an employee registers carries two
/// deadlines — the employee should SEND it by month-end + 2, the leader should APPROVE it by
/// month-end + 5. Those dates were stored and then read by nothing, so: the organisation page's
/// "efter frist" ("past deadline") tile actually counted every manager with anything PENDING and
/// captioned it "past deadline"; a departing employee's unapproved final month could sit
/// indefinitely with nobody told; an approved month reached payroll only when somebody remembered
/// to call the export endpoint; an employee with no approver, an expired stand-in, or a missing
/// agreement-code row was visible to no one. These reads surface each of those, with an explicit
/// age anchor and an explicit statement of where that anchor came from.</para>
///
/// <list type="bullet">
///   <item><description><b>GET /api/hr/follow-up/past-deadline</b> (HRP-012) — employee-late and
///   approver-late months, counted separately. Only overdue months are listed.</description></item>
///   <item><description><b>GET /api/hr/follow-up/leaver-final-month</b> (HRP-011) — the month
///   containing a passed employment end date, when it is missing or not APPROVED.</description></item>
///   <item><description><b>GET /api/hr/follow-up/approved-not-exported</b> (HRP-022) — APPROVED
///   months with no payroll export record. A READ-ONLY cross-context lookup (ADR-034 D4).</description></item>
///   <item><description><b>GET /api/hr/follow-up/uncovered-approvers</b> (HRP-013 / HRP-014) —
///   the cross-organisation orphan roll-up plus stand-in delegations the expiry sweep closed in the
///   last 30 days.</description></item>
///   <item><description><b>GET /api/hr/follow-up/cannot-register</b> (HRP-015) — employees whose
///   registration is blocked by a missing agreement-code row.</description></item>
/// </list>
///
/// <para><b>Read-only by ruling.</b> Owner ruling OQ-4: the S140 HR lists carry no write action.
/// Nothing here mutates anything, so nothing here emits an event or an audit row — there is no
/// state change to record. The remedial actions stay where they already live (send / approve /
/// reopen, assign an approver, re-delegate, fix the agreement-code history, call the payroll
/// export), each with its own existing audit trail.</para>
///
/// <para><b>Three shared rules.</b> (1) Org scope is the HR-floored accessible-org set matched
/// against the SUBJECT's CURRENT <c>users.primary_org_id</c> — an EMPTY set is a 403, never an empty
/// 200 that would present a scope problem as "nothing to do". (2) "Today" is the Copenhagen
/// business day from the injected clock, computed ONCE per request and threaded into the repository
/// (PAT-028) — no statement asks the database for a business date. (3) Every list is oldest-first
/// and every item carries its age anchor plus <c>deadlineSource</c> (<c>stored</c> or
/// <c>computed</c>).</para>
///
/// <para><b>Counts across these tiles are NOT additive</b> — a leaver's late final month is
/// deliberately on both the past-deadline and the leaver list, because the two hand work to
/// different roles. Each response's documentation says so.</para>
/// </summary>
public static class HrFollowUpApprovalEndpoints
{
    /// <summary>
    /// The shared 403 for an actor whose token carries no HR-level organisation scope. Returned
    /// INSTEAD of an empty 200: a scope problem must look like a scope problem, not like a clean
    /// worklist (the <c>BackdateWorklistEndpoints</c> precedent).
    /// </summary>
    private static IResult NoHrScopeForbidden() =>
        Results.Json(new { error = "Access denied", reason = "No HR-level organisation scope" }, statusCode: 403);

    public static WebApplication MapHrFollowUpApprovalEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/hr/follow-up/past-deadline?summary=   (HRP-012)
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/follow-up/past-deadline", async (
            bool? summary,
            HrFollowUpApprovalReadRepository followUpRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return NoHrScopeForbidden();

            // ONE date for the whole response (PAT-028). Both halves of this read — the employee's
            // court and the leader's court — must describe the same day, or a request spanning
            // midnight could age one list against the 7th and the other against the 8th and no
            // instant in time would explain the page.
            var today = CopenhagenBusinessDate.Today(timeProvider);
            var summaryOnly = summary ?? false;

            var employeeLate = await followUpRepo.GetEmployeeMonthsAsync(
                HrApprovalMonthKinds.EmployeeLate, accessibleOrgIds, today, ct);
            var approverLate = await followUpRepo.GetEmployeeMonthsAsync(
                HrApprovalMonthKinds.ApproverLate, accessibleOrgIds, today, ct);

            return Results.Ok(new HrPastDeadlineResponse(
                Today: today,
                LookbackFloor: LookbackFloorFor(today),
                EmployeeLateCount: employeeLate.Count,
                OldestEmployeeLateAnchor: employeeLate.Count == 0 ? null : employeeLate[0].AgeAnchor,
                ApproverLateCount: approverLate.Count,
                OldestApproverLateAnchor: approverLate.Count == 0 ? null : approverLate[0].AgeAnchor,
                EmployeeLate: summaryOnly ? null : employeeLate.Select(ToMonthItem).ToList(),
                ApproverLate: summaryOnly ? null : approverLate.Select(ToMonthItem).ToList()));
        }).RequireAuthorization("HROrAbove")
        .Produces<HrPastDeadlineResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════
        // 2. GET /api/hr/follow-up/leaver-final-month?summary=   (HRP-011)
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/follow-up/leaver-final-month", async (
            bool? summary,
            HrFollowUpApprovalReadRepository followUpRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return NoHrScopeForbidden();

            var today = CopenhagenBusinessDate.Today(timeProvider);
            var items = await followUpRepo.GetEmployeeMonthsAsync(
                HrApprovalMonthKinds.LeaverFinalMonth, accessibleOrgIds, today, ct);

            return Results.Ok(new HrLeaverFinalMonthResponse(
                Today: today,
                LookbackFloor: LookbackFloorFor(today),
                Count: items.Count,
                OldestAnchor: items.Count == 0 ? null : items[0].AgeAnchor,
                Items: (summary ?? false) ? null : items.Select(ToMonthItem).ToList()));
        }).RequireAuthorization("HROrAbove")
        .Produces<HrLeaverFinalMonthResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════
        // 3. GET /api/hr/follow-up/approved-not-exported?summary=   (HRP-022)
        //    A READ-ONLY cross-context lookup of the Payroll-owned export ledger (ADR-034 D4). The
        //    Payroll service is its sole writer; this endpoint reads and reports, nothing more.
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/follow-up/approved-not-exported", async (
            bool? summary,
            HrFollowUpApprovalReadRepository followUpRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return NoHrScopeForbidden();

            var today = CopenhagenBusinessDate.Today(timeProvider);
            var items = await followUpRepo.GetEmployeeMonthsAsync(
                HrApprovalMonthKinds.ApprovedNotExported, accessibleOrgIds, today, ct);

            return Results.Ok(new HrApprovedNotExportedResponse(
                Today: today,
                LookbackFloor: LookbackFloorFor(today),
                Count: items.Count,
                OldestAnchor: items.Count == 0 ? null : items[0].AgeAnchor,
                Items: (summary ?? false) ? null : items.Select(ToMonthItem).ToList()));
        }).RequireAuthorization("HROrAbove")
        .Produces<HrApprovedNotExportedResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════
        // 4. GET /api/hr/follow-up/uncovered-approvers?summary=   (HRP-013 / HRP-014)
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/follow-up/uncovered-approvers", async (
            bool? summary,
            HrFollowUpApprovalReadRepository followUpRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return NoHrScopeForbidden();

            var today = CopenhagenBusinessDate.Today(timeProvider);
            var orphans = await followUpRepo.GetOrphanEmployeesAsync(accessibleOrgIds, ct);
            var expired = await followUpRepo.GetExpiredDelegationsAsync(accessibleOrgIds, today, ct);
            var summaryOnly = summary ?? false;

            return Results.Ok(new HrUncoveredApproversResponse(
                Today: today,
                OrphanCount: orphans.Count,
                Orphans: summaryOnly
                    ? null
                    : orphans.Select(o => new HrOrphanEmployee(o.EmployeeId, o.DisplayName, o.OrgId, o.UnitName)).ToList(),
                ExpiredDelegationCount: expired.Count,
                // The honest "actually uncovered" number: an approver who has been re-delegated
                // since the expiry is not uncovered, and must not be counted as if they were.
                ExpiredWithoutActiveCoverCount: expired.Count(e => !e.ApproverHasActiveCover),
                OldestExpiry: expired.Count == 0 ? null : expired[0].ExpiredAt,
                ExpiredDelegations: summaryOnly
                    ? null
                    : expired.Select(e => new HrExpiredDelegation(
                        VikarId: e.VikarId,
                        AbsentApproverId: e.AbsentApproverId,
                        AbsentApproverName: e.AbsentApproverName,
                        VikarUserId: e.VikarUserId,
                        VikarUserName: e.VikarUserName,
                        OrgId: e.OrgId,
                        UnitName: e.UnitName,
                        Reason: e.Reason,
                        UntilDate: e.UntilDate,
                        ExpiredOn: e.ExpiredOn,
                        ExpiredAt: e.ExpiredAt,
                        DaysSinceExpiry: e.DaysSinceExpiry,
                        ApproverHasActiveCover: e.ApproverHasActiveCover)).ToList(),
                WindowDays: HrFollowUpApprovalReadRepository.ExpiredDelegationWindowDays));
        }).RequireAuthorization("HROrAbove")
        .Produces<HrUncoveredApproversResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════
        // 5. GET /api/hr/follow-up/cannot-register?summary=   (HRP-015)
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/follow-up/cannot-register", async (
            bool? summary,
            HrFollowUpApprovalReadRepository followUpRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return NoHrScopeForbidden();

            var today = CopenhagenBusinessDate.Today(timeProvider);
            var items = await followUpRepo.GetCannotRegisterAsync(accessibleOrgIds, today, ct);

            return Results.Ok(new HrCannotRegisterResponse(
                Today: today,
                Count: items.Count,
                // The oldest DATABLE gap. Undatable gaps (the history-backfill sentinel) are counted
                // and listed last, but they cannot supply an "oldest".
                OldestGapSince: items.FirstOrDefault(i => i.GapSince is not null)?.GapSince,
                Items: (summary ?? false)
                    ? null
                    : items.Select(i => new HrCannotRegisterEmployee(
                        EmployeeId: i.EmployeeId,
                        DisplayName: i.DisplayName,
                        OrgId: i.OrgId,
                        UnitName: i.UnitName,
                        GapSince: i.GapSince,
                        DaysSinceGapStart: i.DaysSinceGapStart)).ToList()));
        }).RequireAuthorization("HROrAbove")
        .Produces<HrCannotRegisterResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>
    /// The rolling lookback floor echoed on every enumeration response so the tile can STATE how
    /// far back it looked (owner ruling OQ-3 (a)) instead of implying completeness. Derived from the
    /// request's single date and the repository's own constant — never a second policy.
    /// </summary>
    private static DateOnly LookbackFloorFor(DateOnly today) =>
        new DateOnly(today.Year, today.Month, 1)
            .AddMonths(-HrFollowUpApprovalReadRepository.LookbackMonths);

    /// <summary>Storage row → wire item. A pure rename; no derivation happens here.</summary>
    private static HrFollowUpMonthItem ToMonthItem(HrApprovalMonthItem row) =>
        new(
            EmployeeId: row.EmployeeId,
            DisplayName: row.DisplayName,
            OrgId: row.OrgId,
            Year: row.Year,
            Month: row.Month,
            PeriodStatus: row.PeriodStatus,
            PeriodId: row.PeriodId,
            AgeAnchor: row.AgeAnchor,
            DaysPastAnchor: row.DaysPastAnchor,
            DeadlineSource: row.DeadlineSource);
}
