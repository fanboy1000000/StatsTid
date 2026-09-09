using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S140 / TASK-14003 (refinement B1; HR follow-up register rows HRP-005, HRP-005b, HRP-007,
/// HRP-010) — the SETTLEMENT-FAMILY reads of the HR follow-up surface.
///
/// <para>
/// <b>The problem these solve, in plain language.</b> The settlement machinery repeatedly hands
/// HR a job and then walks away. A vacation settlement is flagged for manual review and nobody
/// is told. A departing employee's holiday payout needs a formal request under §26 of the Danish
/// holiday act, and no screen lists who is waiting. An employee may carry their fifth week of
/// holiday into next year if HR records a written agreement before 31 December — and until now
/// that agreement could only be WRITTEN through the API: nothing could read back what had been
/// recorded, and nothing told HR who still needed one. Sprint 139 catalogued fifteen such
/// hand-offs; these three reads are the settlement family's share of the surface.
/// </para>
///
/// <list type="bullet">
///   <item><description>
///     <b>GET /api/hr/follow-up/settlement-reviews</b> (HRP-005 + HRP-005b) — settlements
///     awaiting manual review, from BOTH sources the system produces: <c>PENDING_REVIEW</c> rows,
///     and refused terminations that write no row at all and exist only as a flag EVENT.
///   </description></item>
///   <item><description>
///     <b>GET /api/hr/follow-up/termination-payouts-unrequested</b> (HRP-007) — settled
///     terminations whose crystallised days still need a §26 payout request recorded.
///   </description></item>
///   <item><description>
///     <b>GET /api/hr/follow-up/transfer-agreements-needed</b> (HRP-010, the list) — employees who
///     still appear to need a §21 stk.2 fifth-week transfer agreement before 31 December.
///   </description></item>
///   <item><description>
///     <b>GET /api/vacation-transfer-agreements/{employeeId}</b> (HRP-010, the record) — what was
///     actually recorded. Its POST/PUT siblings live in <c>VacationSettlementEndpoints</c>; the
///     reader lands here because this is the follow-up surface that needed it (a deliberate,
///     declared split — the write guards and the read have nothing in common but the route).
///   </description></item>
/// </list>
///
/// <para>
/// <b>READ-ONLY in S140 (owner ruling OQ-4, 2026-09-08).</b> None of the four process actions
/// behind these lists (resolve a flagged settlement, reconcile a payout, record a §21 agreement,
/// reverse a settlement) gains a new endpoint here — they already exist and are untouched. No
/// statement reachable from this file performs an INSERT, UPDATE or DELETE.
/// </para>
///
/// <para>
/// <b>Authorisation on every route: <c>HROrAbove</c> PLUS a role-FLOORED org-scope check.</b> The
/// policy proves the actor holds an HR-or-higher role SOMEWHERE; it does not prove they may see
/// THIS employee. So each list filters through
/// <see cref="OrgScopeValidator.GetAccessibleOrgsAsync(ActorContext, string?, CancellationToken)"/>
/// at the <c>LocalHR</c> floor — without the floor, a mixed-role actor (HR in org A, Leader in
/// org B) would have B's rows unioned in via the non-admin scope, which is the leak class S76
/// closed. <c>null</c> = a GLOBAL scope clearing the floor (unrestricted); an EMPTY set is
/// <b>403</b>, never an empty 200 — an empty 200 makes a scope misconfiguration look like "no
/// work to do", which is the worst possible failure mode for a follow-up list.
/// </para>
///
/// <para>
/// <b>The org column, and why it is the one that matters.</b> The scope set is applied to the
/// SUBJECT EMPLOYEE's CURRENT <c>users.primary_org_id</c> — never to an org id stamped onto a
/// settlement, request or agreement row when it was written. A stamped org drifts the moment an
/// employee transfers: the old organisation's HR would keep seeing them and the new one would
/// never see them at all.
/// </para>
///
/// <para>
/// <b>One date per request (PAT-028).</b> Each of the three LIST handlers reads
/// <c>CopenhagenBusinessDate.Today(timeProvider)</c> exactly ONCE and threads that value into
/// every derivation and every SQL parameter; the per-employee record read needs no business date
/// and takes no clock at all. No business date is read from the database clock
/// (<c>CURRENT_DATE</c> / <c>NOW()::date</c>) anywhere on these paths. Wave 1 of this sprint
/// fixed four defects caused by breaking that rule.
/// </para>
/// </summary>
public static class HrFollowUpSettlementEndpoints
{
    /// <summary>The only §21-transferable entitlement type. SPECIAL_HOLIDAY has a 30-April
    /// geometry and would silently misfire, so the type is stated EXPLICITLY, never inferred.</summary>
    private const string VacationType = HrFollowUpSettlementReadRepository.VacationType;

    /// <summary>
    /// The VACATION <c>reset_month</c>. Pinned to 9 at the DATABASE level
    /// (<c>entitlement_configs_vacation_reset_month</c>: <c>entitlement_type &lt;&gt; 'VACATION' OR
    /// reset_month = 9</c>), so the ferieår geometry — and therefore the §21 target year — is
    /// uniform across employees and can be resolved once per request. Mirrors the
    /// <c>VacationSettlementEndpoints.ResolveDeadlineAndCapAsync</c> fallback.
    /// </summary>
    private const int VacationResetMonth = 9;

    /// <summary>The §21 reminder window opens 1 November (owner ruling, 2026-09-08).</summary>
    private const int Section21WindowOpenMonth = 11;
    private const int Section21WindowOpenDay = 1;

    /// <summary>
    /// The declared eventual-consistency statement carried on the HRP-005 response. The
    /// event-sourced half of that list reads the canonical <c>events</c> table, which the outbox
    /// publisher fills one cycle AFTER the domain transaction commits.
    /// </summary>
    private const string EventSourceLagNote =
        "Refused terminations are read from the event stream, which the outbox publisher fills " +
        "one poll cycle after the settlement transaction commits — a termination refused just now " +
        "appears here within a few seconds, not instantly.";

    /// <summary>
    /// The owner-ratified approximation statement carried on the HRP-010 response. Before the
    /// year-end close there is no exact §21 residual, only an accrual-side projection; the
    /// authoritative partition is computed at settlement.
    /// </summary>
    private const string Section21ProjectionNote =
        "Projection-based: before the year-end close the §21 residual is an accrual-side estimate, " +
        "so this list can name an employee the close later clears. It becomes exact once the " +
        "ferieår is settled.";

    public static WebApplication MapHrFollowUpSettlementEndpoints(this WebApplication app)
    {
        MapPendingSettlementReviews(app);          // HRP-005 + HRP-005b
        MapTerminationPayoutsUnrequested(app);     // HRP-007
        MapTransferAgreementsNeeded(app);          // HRP-010 (list)
        MapTransferAgreementRecord(app);           // HRP-010 (record)
        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // 1. HRP-005 + HRP-005b — GET /api/hr/follow-up/settlement-reviews
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static void MapPendingSettlementReviews(WebApplication app)
    {
        app.MapGet("/api/hr/follow-up/settlement-reviews", async (
            HrFollowUpSettlementReadRepository readRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // ONE clock read for the whole request (PAT-028) — every AgeDays below is measured
            // against THIS value, so no two items can be aged against different days.
            var today = CopenhagenBusinessDate.Today(timeProvider);

            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return ForbidNoHrScope();

            var rows = await readRepo.GetPendingSettlementReviewsAsync(accessibleOrgIds, ct);

            var items = rows
                .Select(r => new PendingSettlementReviewItem(
                    EmployeeId: r.EmployeeId,
                    EntitlementType: r.EntitlementType,
                    EntitlementYear: r.EntitlementYear,
                    SettlementSequence: r.SettlementSequence,
                    Source: r.Source,
                    SettlementState: r.SettlementState,
                    Trigger: r.Trigger,
                    ReviewDisposition: r.ReviewDisposition,
                    FlaggedDays: r.FlaggedDays,
                    Version: r.Version,
                    AgeAnchor: r.AgeAnchor,
                    AgeDays: AgeInDays(r.AgeAnchor, today),
                    PrimaryOrgId: r.PrimaryOrgId))
                .ToList();

            return Results.Ok(new PendingSettlementReviewListResponse(
                Items: items,
                Count: items.Count,
                Today: today,
                EventSourceLagNote: EventSourceLagNote));
        }).RequireAuthorization("HROrAbove")
        .Produces<PendingSettlementReviewListResponse>(StatusCodes.Status200OK);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // 2. HRP-007 — GET /api/hr/follow-up/termination-payouts-unrequested
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static void MapTerminationPayoutsUnrequested(WebApplication app)
    {
        app.MapGet("/api/hr/follow-up/termination-payouts-unrequested", async (
            HrFollowUpSettlementReadRepository readRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var today = CopenhagenBusinessDate.Today(timeProvider); // ONE read (PAT-028)

            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return ForbidNoHrScope();

            var rows = await readRepo.GetTerminationPayoutsUnrequestedAsync(accessibleOrgIds, ct);

            var items = rows
                .Select(r => new TerminationPayoutUnrequestedItem(
                    EmployeeId: r.EmployeeId,
                    EntitlementType: r.EntitlementType,
                    EntitlementYear: r.EntitlementYear,
                    SettlementSequence: r.SettlementSequence,
                    CrystallizedDays: r.CrystallizedDays,
                    Version: r.Version,
                    AgeAnchor: r.AgeAnchor,
                    AgeDays: AgeInDays(r.AgeAnchor, today),
                    PrimaryOrgId: r.PrimaryOrgId))
                .ToList();

            return Results.Ok(new TerminationPayoutUnrequestedListResponse(
                Items: items,
                Count: items.Count,
                Today: today));
        }).RequireAuthorization("HROrAbove")
        .Produces<TerminationPayoutUnrequestedListResponse>(StatusCodes.Status200OK);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // 3. HRP-010 (list) — GET /api/hr/follow-up/transfer-agreements-needed
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static void MapTransferAgreementsNeeded(WebApplication app)
    {
        app.MapGet("/api/hr/follow-up/transfer-agreements-needed", async (
            HrFollowUpSettlementReadRepository readRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var today = CopenhagenBusinessDate.Today(timeProvider); // ONE read (PAT-028)

            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgIds is { Count: 0 })
                return ForbidNoHrScope();

            // The target ferieår + its statutory deadline, resolved through the SHARED geometry.
            var targetYear = ResolveSection21TargetYear(VacationResetMonth, today);
            if (targetYear is not { } entitlementYear)
            {
                // Structurally unreachable for the two seeded geometries (reset 1 and reset 9 both
                // put a §21 boundary in every calendar year). Fail LOUD rather than guess a year:
                // a wrong year lists the wrong people in the only window the rule applies.
                return Results.Json(new
                {
                    error = "No VACATION ferieår has its §21 stk.2 deadline in the current calendar year; " +
                            "the transfer-agreement window cannot be resolved.",
                }, statusCode: 500);
            }

            var period = EntitlementPeriodResolver.ResolveForYear(VacationType, VacationResetMonth, entitlementYear);
            var deadline = period.Boundary;                      // 31 December of the ferieår-END year
            var ageAnchorDate = period.AccrualEnd;               // the ferieår end — when the days became candidates
            var windowOpensOn = new DateOnly(today.Year, Section21WindowOpenMonth, Section21WindowOpenDay);
            var windowOpen = today >= windowOpensOn && today <= deadline;
            var daysToDeadline = deadline.DayNumber - today.DayNumber;

            // OUT OF SEASON: an EMPTY list with windowOpen = false. The tile then says "opens
            // 1 November" instead of showing a count — which is precisely the distinction a bare
            // count cannot make, and it also avoids valuing the whole population ten months a year.
            if (!windowOpen)
            {
                return Results.Ok(new VacationTransferAgreementNeededListResponse(
                    Items: Array.Empty<VacationTransferAgreementNeededItem>(),
                    Count: 0,
                    EntitlementYear: entitlementYear,
                    WindowOpen: false,
                    WindowOpensOn: windowOpensOn,
                    Deadline: deadline,
                    DaysToDeadline: daysToDeadline,
                    CannotCompute: Array.Empty<VacationTransferAgreementCannotComputeItem>(),
                    CannotComputeCount: 0,
                    Today: today,
                    ProjectionNote: Section21ProjectionNote));
            }

            var result = await readRepo.GetTransferAgreementsNeededAsync(
                accessibleOrgIds, today, entitlementYear, ct);

            var items = result.Needed
                .Select(r => new VacationTransferAgreementNeededItem(
                    EmployeeId: r.EmployeeId,
                    PrimaryOrgId: r.PrimaryOrgId,
                    EntitlementYear: entitlementYear,
                    UnderCapDays: r.UnderCapDays,
                    CarryoverMax: r.CarryoverMax,
                    Deadline: deadline,
                    DaysToDeadline: daysToDeadline,
                    AgeAnchorDate: ageAnchorDate))
                .ToList();

            var cannotCompute = result.CannotCompute
                .Select(r => new VacationTransferAgreementCannotComputeItem(
                    EmployeeId: r.EmployeeId,
                    PrimaryOrgId: r.PrimaryOrgId,
                    Reason: r.Reason))
                .ToList();

            return Results.Ok(new VacationTransferAgreementNeededListResponse(
                Items: items,
                Count: items.Count,
                EntitlementYear: entitlementYear,
                WindowOpen: true,
                WindowOpensOn: windowOpensOn,
                Deadline: deadline,
                DaysToDeadline: daysToDeadline,
                CannotCompute: cannotCompute,
                CannotComputeCount: cannotCompute.Count,
                Today: today,
                ProjectionNote: Section21ProjectionNote));
        }).RequireAuthorization("HROrAbove")
        .Produces<VacationTransferAgreementNeededListResponse>(StatusCodes.Status200OK);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // 4. HRP-010 (record) — GET /api/vacation-transfer-agreements/{employeeId}
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static void MapTransferAgreementRecord(WebApplication app)
    {
        app.MapGet("/api/vacation-transfer-agreements/{employeeId}", async (
            string employeeId,
            HrFollowUpSettlementReadRepository readRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Per-employee read: bind the scope check to the SUBJECT, at the LocalHR floor, and
            // TERMINATED-INCLUSIVE — a leaver's recorded §21 agreement is exactly the record HR may
            // still need to inspect.
            //
            // DECLARED, because this read is deliberately MORE permissive than its own write
            // siblings: the §21 POST/PUT use the active-only ValidateEmployeeAccessAsync
            // (VacationSettlementEndpoints), so a leaver's agreement can be READ here but not
            // edited there. That asymmetry is intended — you may inspect history you may no longer
            // change — and it follows the settlement family's adjudicated posture, not the write
            // guard's: the backdate worklist's per-employee read is terminated-inclusive ("HR
            // corrects leavers' history too"), and payout-pending deliberately carries no is_active
            // filter (SEC-047, owner-adjudicated: the worklist EXISTS to show settled LEAVERS'
            // unreconciled payouts; per ADR-040 D3 is_active is a LOGIN fact, not a
            // data-visibility fact). The original S68/S76 defect on this family was the opposite
            // one — HR could not reach a departed employee's settlement surfaces at all.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var rows = await readRepo.GetTransferAgreementRecordAsync(employeeId, ct);

            var agreements = rows
                .Select(r => new VacationTransferAgreementRecordItem(
                    EntitlementYear: r.EntitlementYear,
                    EntitlementType: r.EntitlementType,
                    TransferDays: r.TransferDays,
                    AgreementDate: r.AgreementDate,
                    RecordedBy: r.RecordedBy,
                    Version: r.Version,
                    // The §21 deadline of THAT ferieår, from the shared geometry — derived, not
                    // stored, and derived from the ROW's own year (never from today). The type is
                    // the VACATION constant, matching the repository's VACATION-only filter, so the
                    // geometry can never be SPECIAL_HOLIDAY's 30-April one.
                    Deadline: EntitlementPeriodResolver
                        .ResolveForYear(VacationType, VacationResetMonth, r.EntitlementYear)
                        .Boundary))
                .ToList();

            return Results.Ok(new VacationTransferAgreementRecordResponse(
                EmployeeId: employeeId,
                Agreements: agreements,
                Count: agreements.Count));
        }).RequireAuthorization("HROrAbove")
        .Produces<VacationTransferAgreementRecordResponse>(StatusCodes.Status200OK);
    }

    // ── derivations (pure, DB-free, clock-free — every date arrives as a parameter) ──────────

    /// <summary>
    /// The §21 target ferieår: the year <c>E</c> whose <c>EntitlementPeriodResolver</c> boundary
    /// (the 31-December §21 deadline of the ferieår-END year) falls in <paramref name="today"/>'s
    /// calendar year — NOT the ferieår containing today.
    ///
    /// <para>
    /// This is the single easiest thing to get wrong on this surface. Under the state-sector
    /// September reset, ferieår <c>E</c> runs 1 Sep <c>E</c> – 31 Aug <c>E+1</c> and its §21
    /// deadline is 31 Dec <c>E+1</c>. So in November 2025 the year whose deadline is imminent is
    /// <b>2024</b>; the ferieår CONTAINING November 2025 is 2025, whose own deadline is a year
    /// away. Listing 2025 would list the wrong people in the only window the rule applies.
    /// </para>
    ///
    /// <para>
    /// The answer is SEARCHED through the shared resolver rather than hand-derived, so the two
    /// seeded geometries both come out right (reset 9 ⇒ <c>E = today.Year − 1</c>; reset 1 ⇒
    /// <c>E = today.Year</c>) and a future geometry change cannot silently invalidate it. Returns
    /// null when no candidate year matches, which the caller treats as a loud failure rather than
    /// guessing.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately <c>public</c>, not <c>internal</c>.</b> This project's Backend.Api assembly
    /// declares no <c>InternalsVisibleTo</c>, so <c>internal</c> here would be dead accessibility —
    /// effectively private, and untestable. It is the one derivation on this surface that most needs
    /// a cheap pin, and it is a PURE function of (resetMonth, today): exposing it lets a
    /// non-Docker Unit fact assert "November 2025, reset 9 ⇒ 2024" without a database.
    /// </para>
    /// </summary>
    public static int? ResolveSection21TargetYear(int resetMonth, DateOnly today)
    {
        // A ferieår's boundary year is its start year (reset 1) or start year + 1 (reset > 1), so
        // the candidate window only needs to reach two years back.
        for (var candidate = today.Year; candidate >= today.Year - 2; candidate--)
        {
            if (EntitlementPeriodResolver
                    .ResolveForYear(VacationType, resetMonth, candidate)
                    .Boundary.Year == today.Year)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Whole days from an age anchor to the request's business date, floored at 0. The anchor is a
    /// UTC instant and <paramref name="today"/> is a Copenhagen business date, so the anchor's
    /// COPENHAGEN calendar day is used for the subtraction — an item created at 23:30 Copenhagen
    /// time is one day old the next morning, not two.
    /// </summary>
    private static int AgeInDays(DateTimeOffset ageAnchor, DateOnly today)
    {
        // The SHARED DST-correct zone (CopenhagenBusinessDate.Zone) — the same one `Today` uses,
        // so the anchor day and today are derived through one zone resolution, not two. A local
        // fixed +01:00 assumption is the QUAL-005 bug and is not repeated here.
        var anchorDay = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(ageAnchor, CopenhagenBusinessDate.Zone).DateTime);
        var days = today.DayNumber - anchorDay.DayNumber;
        return days < 0 ? 0 : days;
    }

    /// <summary>
    /// The empty-scope refusal, shared by the three org-wide lists. 403, never an empty 200 — an
    /// empty 200 would make a scope misconfiguration read as "no work to do".
    /// </summary>
    private static IResult ForbidNoHrScope() =>
        Results.Json(
            new { error = "Access denied", reason = "No HR-level organisation scope" },
            statusCode: 403);
}
