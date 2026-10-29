using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
// ── Danish absence type labels ──
// S147 / TASK-14701 (QUAL-235) — Skema and the admin visibility list (ConfigEndpoints) read the
// ONE shared map in Services/AbsenceTypeLabels.cs, so a code labelled there is labelled on every
// absence-type surface. S147 / TASK-14708 (review follow-up A-2) — every label site below calls
// its LabelFor (label, else the code itself), so that fallback is spelled in one place; this alias
// replaces the former private dictionary field of the same name, which shadowed the class.
using AbsenceTypeLabels = StatsTid.Backend.Api.Services.AbsenceTypeLabels;

namespace StatsTid.Backend.Api.Endpoints;

public static class SkemaEndpoints
{
    // ── Base absence types common to all agreements ──
    // S147 / TASK-14708 (QUAL-231): the codes are the SharedKernel AbsenceTypes constants (the
    // stored spelling; the engine credits by the same vocabulary).
    // S147 / TASK-14701 (QUAL-235): SPECIAL_HOLIDAY_ALLOWANCE (særlige feriedage) joins the base
    // list, beside VACATION. It is listed for every agreement, but ComputeAbsenceCatalogAsync
    // serves it only where a SPECIAL_HOLIDAY entitlement config resolves at the catalogue anchor
    // (gate (c) there). That gate is a DISPLAY affordance, mirroring the SENIOR_DAY gate's dated
    // read; it is not the check that values a save. The save's quota check keys on the live open
    // config row instead (GetCurrentOpenAsync in the save path); with no live row the save records
    // HasNoLiveConfig (QuotaPreCheckPlan.WithoutLiveConfig): the absence is still saved, no quota
    // is checked and no balance moves.
    private static readonly string[] BaseAbsenceTypes =
    [
        AbsenceTypes.SickDay, AbsenceTypes.Vacation, AbsenceTypes.SpecialHolidayAllowance,
        AbsenceTypes.CareDay, AbsenceTypes.ChildSick1, AbsenceTypes.ParentalLeave,
        AbsenceTypes.SeniorDay, AbsenceTypes.LeaveWithPay, AbsenceTypes.LeaveWithoutPay
    ];

    /// <summary>
    /// Returns the absence types available for a given agreement code.
    /// Child sick days vary: AC=1, HK=2, PROSA=3.
    /// </summary>
    private static HashSet<string> GetAbsenceTypesForAgreement(string agreementCode)
    {
        var types = new HashSet<string>(BaseAbsenceTypes, StringComparer.Ordinal);

        // HK gets 2 child sick days, PROSA gets 3
        // (S147 / TASK-14708: the codes read the constants; WHICH agreements get day 2 and day 3
        // is unchanged here — that is QUAL-236's, S148.)
        if (agreementCode is "HK" or "PROSA")
            types.Add(AbsenceTypes.ChildSick2);

        if (agreementCode is "PROSA")
            types.Add(AbsenceTypes.ChildSick3);

        return types;
    }

    // ── Absence type → entitlement type mapping + StandardDayHours ──
    // S65 / TASK-6502 — promoted to the shared Backend.Api EntitlementMapping so the new
    // Balance year-overview read consumes the SAME map / divisor (no second copy, no second
    // 7.4 literal; Step-0b Codex W1). Local aliases keep the existing call sites below
    // unchanged.
    private static readonly IReadOnlyDictionary<string, string?> AbsenceToEntitlementType =
        StatsTid.Backend.Api.Services.EntitlementMapping.AbsenceToEntitlementType;

    private const decimal StandardDayHours = StatsTid.Backend.Api.Services.EntitlementMapping.StandardDayHours;

    /// <summary>
    /// Resolve an absence type to its entitlement type (null = no entitlement gating),
    /// delegating to the shared <see cref="StatsTid.Backend.Api.Services.EntitlementMapping"/>.
    /// </summary>
    private static string? GetEntitlementType(string absenceType)
        => StatsTid.Backend.Api.Services.EntitlementMapping.GetEntitlementType(absenceType);

    /// <summary>
    /// S59 / TASK-5907 — pure integer-age computation as-of a date from a birth date.
    /// No <c>DateTime.Now</c>; deterministic so both the GET display filter (anchor =
    /// month-end) and the POST gate (anchor = absence date) read the same way. Returns the
    /// completed years lived as-of <paramref name="asOf"/> (i.e. the birthday must have
    /// already occurred on/before <paramref name="asOf"/> in that year).
    /// </summary>
    private static int AgeAsOf(DateOnly birthDate, DateOnly asOf)
    {
        var age = asOf.Year - birthDate.Year;
        // Subtract one if this year's birthday has not yet been reached on the as-of date.
        if (asOf < birthDate.AddYears(age))
            age--;
        return age;
    }

    // S80 / TASK-8001 (R10) — the Backend-local ResolveEntitlementYear / ResolveEntitlementYearStart
    // helpers were removed. The entitlement (accrual) year + its accrual-window start now come from the
    // single shared StatsTid.SharedKernel.Calendar.EntitlementPeriodResolver (already imported via the
    // `using StatsTid.SharedKernel.Calendar;` above), which expresses the SPECIAL_HOLIDAY third geometry
    // (calendar accrual + 1 May–30 Apr taking window) the raw reset_month branches could not. VACATION +
    // every other type stay BEHAVIOR-IDENTICAL (the "Month ≥ reset_month" keying lives in the resolver).

    // ── S60 / TASK-6005 — monthly-accrual model constant ──

    /// <summary>The "MONTHLY_ACCRUAL" accrual_model string (ADR-030).</summary>
    private const string MonthlyAccrualModel = "MONTHLY_ACCRUAL";

    // ── S73 / TASK-7301 — the full-day-only basis projection (R2 + R3) ──

    /// <summary>
    /// S73 / TASK-7301 — the ONE 2-decimal projection of the ADR-032 consumption basis
    /// (<see cref="StatsTid.Backend.Api.Services.ConsumptionCalculator"/> <c>fullDayHours</c>),
    /// shared by BOTH the full-day-only guard's <c>requiredHours</c> (pre-tx + in-lock sites)
    /// and the month GET's served <c>consumptionBasis</c> array — the SPRINT-73 R3
    /// served==guard IDENTITY holds by construction because both surfaces route the same
    /// calculator value through this single rounding site. 2-dec AwayFromZero matches the
    /// ADR-032 D1 rounding convention (<c>ConsumptionCalculator.ToFeriedage</c>); the weekday
    /// norm path is already 2-dec (DailyNormCalculator), so rounding only materially touches
    /// the academic 7.4 × fraction fallback. Null (no dated profile) passes through — the
    /// guard skips it (the anchor-422 family owns it) and the GET serves null per R3.
    /// </summary>
    private static decimal? RoundBasis(decimal? fullDayHours)
        // S73 Step-7a B1 — ONE rounding convention shared with the consumption divisor: delegate
        // to the calculator's RoundBasisTwoDp so the guard's requiredHours, the served
        // consumptionBasis, and the full-day-only consumption divisor are mutually consistent.
        // (Null / non-positive pass through; positive ⇒ 2-dec AwayFromZero, ADR-032 D1.)
        => StatsTid.Backend.Api.Services.ConsumptionCalculator.RoundBasisTwoDp(fullDayHours);

    // S128 / TASK-12803 — the S127/TASK-12704 IsPeriodLockedForSave / PeriodLockedForSaveConflict
    // pair moved VERBATIM to the shared StatsTid.Backend.Api.ApprovalPeriodSaveLock (the
    // ApprovalVisibility precedent: one predicate, one 409 construction site) so
    // POST /api/time-entries enforces the SAME lock without a hand-copied status list. The two
    // call sites below (pre-transaction fast path + in-lock authoritative re-read) are unchanged
    // in behavior — same predicate body, same 409 body, byte-identical wire.

    // S61 / TASK-6101 — the Backend-local EarnedToDate/MonthIndex mirror was removed. The pure
    // earned-to-date math is now the single shared copy in StatsTid.SharedKernel.Calendar.AccrualMath
    // (already imported via the `using StatsTid.SharedKernel.Calendar;` above). PAT-005 is unaffected:
    // AccrualMath is a dependency-free SharedKernel leaf both the Backend and the Rule Engine already
    // reference, NOT the RuleEngine assembly (the validate-entitlement boundary stays HTTP-only).

    public static WebApplication MapSkemaEndpoints(this WebApplication app)
    {
        // ── GET /api/skema/{employeeId}/month — Composite monthly spreadsheet data ──

        app.MapGet("/api/skema/{employeeId}/month", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ProjectRepository projectRepo,
            // S72 / TASK-7201 — R4 row-preference container + absence selections (read side
            // for the new `rowPreferences` / `catalogs` fields below).
            SkemaRowPreferenceRepository rowPreferenceRepo,
            AbsenceTypeVisibilityRepository visibilityRepo,
            ApprovalPeriodRepository approvalRepo,
            // S59 / TASK-5907 — per-employee CHILD_SICK eligibility (dated read) and the
            // resolved SENIOR_DAY config (MinAge) for the DOB-derived senior age gate.
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            // S27 TASK-2706 GET migration: Skema month reads now serve from
            // time_entries_projection + absences_projection (TASK-2702 schema,
            // TASK-2704 repos) instead of the event stream. Projections commit
            // in the same tx as the events (POST atomic-tx wrap below) so
            // read-your-write is preserved without waiting for the publisher
            // drain. The Skema GET handler does NOT consume FlexBalanceUpdated
            // (Time/Balance own that stream); IEventStore is no longer needed
            // by this handler.
            TimeEntryProjectionRepository timeEntryProjectionRepo,
            AbsenceProjectionRepository absenceProjectionRepo,
            // TASK-5603 — self-recorded work time read-model + per-day norm resolution.
            WorkTimeProjectionRepository workTimeProjectionRepo,
            // S65 / TASK-6502 — per-day norm resolution extracted into the shared
            // DailyNormCalculator (consumed here + by the Balance year-overview read). The
            // prior inline IEmploymentProfileResolver + ConfigResolutionService loop now lives
            // in that service; the behavior is preserved byte-for-byte.
            StatsTid.Backend.Api.Services.DailyNormCalculator dailyNormCalculator,
            // S73 / TASK-7301 (R3) — the served per-day consumptionBasis array is derived from
            // THE SAME ConsumptionCalculator path the full-day-only save guard uses (the
            // served==guard identity; never a second copy of the ADR-032 formula).
            StatsTid.Backend.Api.Services.ConsumptionCalculator consumptionCalculator,
            OrgScopeValidator scopeValidator,
            DesignatedApproverAuthorizer designatedAuthorizer,
            // S140 / TASK-14001 — the server-"today" seam (TimeProvider.System in production); it
            // backs the leader-tier authority `asOf` below. Every other date this handler serves is
            // derived from the requested (year, month), not from "today".
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only access own data
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only access own data" }, statusCode: 403);

            // S124 / TASK-12405 — true when the caller reached this read as a LEADER over someone
            // else. Self and HR-or-above are false: neither is month-gated.
            //
            // S128 / TASK-12804 (RES-002) — the tier decision itself (self / HR-or-above corrective
            // tier / leader tier, incl. the deliberate non-Employee self-read scope-resolution skip)
            // moved VERBATIM into the shared ApprovalReadTier.IsLeaderTierReadAsync — the
            // ApprovalVisibility / ApprovalPeriodSaveLock lift pattern — so the RES-002 sibling
            // reads (allocation-breakdown, compliance /period, balance /summary) classify actors
            // through the SAME member instead of hand-copying this block. Same checks, same order,
            // same scopeValidator calls: the behavior diff here is EMPTY. The tier rationale
            // (TASK-12403/12404 and the Step-7a Codex BLOCKER) lives on ApprovalReadTier.
            var leaderTierRead = await ApprovalReadTier.IsLeaderTierReadAsync(
                scopeValidator, actor, employeeId, ct);

            if (leaderTierRead)
            {
                // The leader tier's ACCESS decision (unchanged; only the tier CLASSIFICATION moved):
                // a covering below-HR scope admits, else the designated-approver / unit-leader edge —
                // the S88-8801 B2 shape both sibling reads on this expander already carry
                // (ComplianceEndpoints; ApprovalEndpoints allocation-breakdown): the Teamoversigt
                // roster is the DESIGNATED-APPROVER set (ADR-027 D13 / ADR-038 D4), which admits
                // cross-afdeling vikar + escalation approvers and peer unit-leaders whose ORG_ONLY
                // role-scope does NOT name the employee's org. Without it the leader's grid 403s for
                // exactly that population — a hole masked as a transient "could not load".
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                {
                    // S140 / TASK-14001 — "who may act NOW" reads the injected TimeProvider (PAT-008
                    // seam); the only business-date read on this handler's path. S142 / TASK-14203 —
                    // it is the COPENHAGEN calendar day, the same day the Teamoversigt roster and the
                    // allocation-breakdown gate derive, so this expander cannot admit or deny against
                    // a different calendar than the sibling reads it mirrors.
                    var today = CopenhagenBusinessDate.Today(timeProvider);
                    var hasEdgeOrUnit = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        actor.ActorId!, employeeId, asOf: today, ct: ct);
                    if (!hasEdgeOrUnit)
                        return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
                }
            }

            // Get employee profile
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            // S34 / TASK-3411 — ADR-023 D2 binding cutover for agreement_code on past-month
            // queries. The absence-types list shown for a past month must reflect the
            // agreement that was in effect at the START of that month (e.g. a user who
            // switched AC → HK today should still see only 1 child-sick-day option when
            // viewing September; HK's CHILD_SICK_DAY_2 should NOT appear retroactively).
            //
            // ADR-023 D3 (Skema = graceful-fallback consumer on the GET path): null dated
            // lookup falls through to live `user.AgreementCode`. Defensive only — the
            // TASK-3403 backfill seeder guarantees every user has a covering row, but the
            // fallback keeps the GET informational even if a user was created after the
            // period being viewed.
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, monthStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            // ── The employee's project rows (S72 / Step-5a B1+B2 rework) ──
            // The R4 container state is read FIRST because BOTH the legacy `projects`
            // field's condition and the selection read's ORDER BY depend on it.
            var preferencesConfigured = await rowPreferenceRepo.ContainerExistsAsync(employeeId, ct);

            // B2: container present ⇒ the PER-USER order (ups.sort_order, project_code —
            // the user's chosen order, frozen against later org reorders); container-less ⇒
            // the LIVE org order (p.sort_order, project_code) — pre-S72-identical even
            // after an admin reorders org sort_order post-migration (the TASK-7200
            // backfilled ups.sort_order is a one-shot snapshot, deliberately NOT consulted
            // for container-less users). The named R4 zero-behavior-change regression class
            // pins both behaviors.
            var selectedProjects = await projectRepo.GetSelectedByEmployeeAsync(
                employeeId, user.PrimaryOrgId, orderByUserPreference: preferencesConfigured, ct);

            // B1: when the container EXISTS the selections are authoritative EVEN WHEN
            // EMPTY (R4) — the legacy `projects` field serves the SAME visible set as
            // `rowPreferences.projects` below (catalog ∩ selections, possibly EMPTY; no
            // fallback). The all-org fallback applies ONLY when the container does not
            // exist (the pre-S72 population, whose behavior is unchanged). Pre-B1 the
            // fallback keyed off selectedProjects.Count alone, so a configured-empty (or
            // configured-with-only-stale-selections) user was served ALL org projects on
            // this row-serving read path — violating container-authoritative-even-empty.
            var projects = preferencesConfigured || selectedProjects.Count > 0
                ? selectedProjects
                : await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct);

            // Build absence types list (filtered by agreement, org visibility, then
            // per-employee eligibility). S72 / TASK-7201: the chain itself moved VERBATIM
            // into ComputeAbsenceCatalogAsync (one copy, also consumed by the new
            // row-preferences PUT validation + the `catalogs` field below); the served shape
            // and order are byte-identical to the pre-S72 inline block. Anchor = MONTH-END
            // (the established S59 display anchor).
            var absenceCatalog = await ComputeAbsenceCatalogAsync(
                employeeId, agreementCode, user, monthEnd,
                visibilityRepo, eligibilityRepo, entitlementConfigRepo, ct);
            // S73 / TASK-7301 (R3): every absence-type DTO surface carries fullDayOnly — this
            // legacy field is the same projection as catalogs.absenceTypes below (one
            // computation, two projections; keeping them identical prevents the S72-B1
            // cross-surface-drift class).
            var absenceTypes = absenceCatalog
                .Select(c => new SkemaAbsenceTypeRow(c.Type, c.Label, c.FullDayOnly))
                .ToList();

            // S27 TASK-2706 GET migration: read time entries + absences from the
            // sync-in-tx projections (committed in the same atomic tx as the
            // outbox enqueue per ADR-018 D12) instead of the event stream. This
            // eliminates the post-S26 read-your-write gap that surfaced when the
            // POST handler was made atomic but reads still waited for publisher
            // drain. ORDER BY outbox_id ASC (inside the repos) preserves
            // per-employee monotonic ordering across rows on the same date.
            var entriesRows = await timeEntryProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var entries = entriesRows
                .Select(e => new SkemaEntryRow(
                    Date: e.Date,
                    ProjectCode: e.TaskId,
                    Hours: e.Hours))
                .ToList();

            var absencesRows = await absenceProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var absences = absencesRows
                .Select(e => new SkemaAbsenceRow(
                    Date: e.Date,
                    AbsenceType: e.AbsenceType,
                    Hours: e.Hours,
                    // S72 / TASK-7201 (R10) — the ADR-032 recorded per-absence feriedage,
                    // served verbatim from absences_projection (nullable passthrough: ADR-032
                    // persists null on zero-norm days / non-entitlement rows; the FE skips
                    // null-valued rows when summing — SPRINT-72 R10 / Reviewer N4).
                    Feriedage: e.Feriedage))
                .ToList();

            // ── TASK-5603 self-recorded work time ("Arbejdstid") ──
            // Latest-wins per-day rows from work_time_projection (read-your-write
            // satisfied: the atomic POST upserts in the same tx as the
            // WorkTimeRegistered event). Intervals are already deserialized by the repo.
            var workTimeRows = await workTimeProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var workTime = workTimeRows
                .Select(w => new SkemaWorkTimeDayRow(
                    Date: w.Date,
                    Intervals: w.Intervals.Select(i => new SkemaWorkIntervalRow(i.Start, i.End)).ToList(),
                    ManualHours: w.ManualHours))
                .ToList();

            // ── TASK-5603 per-day norm (dailyNorm) — S65 / TASK-6502 extraction ──
            // PURE READ — no rule-engine HTTP call, no rule logic (P2). The per-day norm
            // resolution (dated profile per day + merged config + per-day OkVersion +
            // weekends 0 + ANNUAL_ACTIVITY/no-profile → null + WeeklyNorm × fraction / 5
            // rounded to 2dp) now lives in the shared DailyNormCalculator so the Skema month
            // read and the Balance year-overview read agree by construction. Behavior is
            // byte-for-byte preserved; the response projection shape ({ date, hours }) is kept.
            var dailyNormEntries = await dailyNormCalculator.ComputeRangeAsync(
                employeeId, monthStart, monthEnd, user.PrimaryOrgId, ct);
            var dailyNorm = dailyNormEntries
                .Select(n => new SkemaDayHoursRow(n.Date, n.Hours))
                .ToList();

            // ── S72 / TASK-7201 — R4 catalog-vs-visible row preferences ──
            // TWO server-computed sets (SPRINT-72 R4):
            //   • CATALOG (addable) — selection-INDEPENDENT: the org's active projects (the
            //     existing org read) + the existing filtered absence-type chain exactly as
            //     served in `absenceTypes` above. Removed rows stay re-addable.
            //   • VISIBLE — container present ⇒ catalog ∩ selections (authoritative EVEN WHEN
            //     EMPTY; stale selections never resurrect org-hidden/inactive/ineligible
            //     rows); container absent ⇒ today's fallback (selections if any, else the
            //     full catalog — exactly what the legacy `projects` field serves).
            // Served sortOrder = the DENSE effective position (0..n-1) in the R4 read order,
            // not the raw stored value (raw legacy-backfilled values may carry duplicates).
            // (preferencesConfigured was read ABOVE, before the selection read — B1+B2.)

            // Catalog projects: always the full org read. `projects` can be reused ONLY in
            // the container-less-no-selections case, where it already IS the org read (no
            // second query); for configured users `projects` is the visible set (possibly
            // empty) and must never masquerade as the catalog.
            var catalogProjects = preferencesConfigured || selectedProjects.Count > 0
                ? await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct)
                : projects;

            // Visible projects: identical to the legacy `projects` field BY CONSTRUCTION
            // post-B1 (configured ⇒ selections authoritative even when empty — the
            // GetSelectedByEmployeeAsync JOIN is catalog ∩ selections; container-less ⇒
            // today's fallback). One set, two projections.
            var visibleProjects = projects;

            // Visible absence rows: container present ⇒ selections ∩ catalog in selection
            // order; container absent ⇒ the full filtered catalog in its served order.
            var catalogTypeSet = new HashSet<string>(
                absenceCatalog.Select(c => c.Type), StringComparer.Ordinal);
            var visibleAbsenceTypes = preferencesConfigured
                ? (await rowPreferenceRepo.GetAbsenceSelectionsAsync(employeeId, ct))
                    .Where(s => catalogTypeSet.Contains(s.AbsenceType))
                    .Select(s => s.AbsenceType)
                    .ToList()
                : absenceCatalog.Select(c => c.Type).ToList();

            // S73 / TASK-7301 (R3): the visible rows carry the SAME fullDayOnly the catalog
            // serves — one resolution (inside ComputeAbsenceCatalogAsync), N projections.
            // Every visible type is in the catalog by construction (the ∩ above), so the
            // lookup always hits; the false fallback is defensive only.
            var fullDayOnlyByType = absenceCatalog.ToDictionary(
                c => c.Type, c => c.FullDayOnly, StringComparer.Ordinal);

            // S120 / TASK-12000 — the project rows REUSE the S119 ProjectResponse (byte-identical
            // 4-member shape; sibling rule); the container record is SHARED with the
            // row-preferences PUT's 200 body (one shape, two surfaces).
            var rowPreferences = new SkemaRowPreferencesResponse(
                Configured: preferencesConfigured,
                Projects: visibleProjects
                    .Select((p, i) => new ProjectResponse(
                        ProjectId: p.ProjectId,
                        ProjectCode: p.ProjectCode,
                        ProjectName: p.ProjectName,
                        SortOrder: i))
                    .ToList(),
                AbsenceTypes: visibleAbsenceTypes
                    .Select((t, i) => new SkemaRowPreferenceAbsenceRow(
                        Type: t,
                        Label: AbsenceTypeLabels.LabelFor(t),
                        FullDayOnly: fullDayOnlyByType.TryGetValue(t, out var fdo) && fdo,
                        SortOrder: i))
                    .ToList());

            var catalogs = new SkemaCatalogs(
                // Addable projects in the org read's order; sortOrder here is the ORG-level
                // sort (mirrors the existing `projects` field's meaning for this value).
                Projects: catalogProjects
                    .Select(p => new ProjectResponse(
                        ProjectId: p.ProjectId,
                        ProjectCode: p.ProjectCode,
                        ProjectName: p.ProjectName,
                        SortOrder: p.SortOrder))
                    .ToList(),
                // The SAME filtered chain (and order) the existing `absenceTypes` field
                // serves — one computation, two projections. S73 / TASK-7301 (R3): the
                // catalog surface carries fullDayOnly.
                AbsenceTypes: absenceCatalog
                    .Select(c => new SkemaAbsenceTypeRow(c.Type, c.Label, c.FullDayOnly))
                    .ToList());

            // ── S72 / TASK-7201 — boundary-day workTime (SPRINT-72 R6 input) ──
            // EXACTLY two extra days — the last day of the previous month and the first day
            // of the next month — so the client-side §J 11-hour rest analysis has the
            // adjacent registrations at month edges. Same row shape as `workTime`; days
            // without a registration serve no row (consistent with the month array).
            var prevBoundaryDay = monthStart.AddDays(-1);
            var nextBoundaryDay = monthEnd.AddDays(1);
            var prevBoundaryRows = await workTimeProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, prevBoundaryDay, prevBoundaryDay, ct);
            var nextBoundaryRows = await workTimeProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, nextBoundaryDay, nextBoundaryDay, ct);
            var boundaryWorkTime = prevBoundaryRows.Concat(nextBoundaryRows)
                .Select(w => new SkemaWorkTimeDayRow(
                    Date: w.Date,
                    Intervals: w.Intervals.Select(i => new SkemaWorkIntervalRow(i.Start, i.End)).ToList(),
                    ManualHours: w.ManualHours))
                .ToList();

            // ── S72 / TASK-7201 — R10 fullDayNormAtMonthEnd scalar ──
            // The employee's WEEKDAY full-day norm (WeeklyNorm × fraction / 5, 2 decimals)
            // resolved via the dated profile + config at the viewed month's LAST day,
            // INDEPENDENT of that day's weekend placement (the calculator applies no
            // weekend-0 short-circuit here). Null when no dated profile/config covers the
            // day or for ANNUAL_ACTIVITY — fail-soft, the FE em-dashes the hours headline.
            // PURE READ (P2): dated stores only, no rule-engine call.
            var fullDayNormAtMonthEnd = await dailyNormCalculator.ComputeWeekdayNormAtAsync(
                employeeId, monthEnd, user.PrimaryOrgId, ct);

            // ── S73 / TASK-7301 — the R3 per-day dated consumptionBasis array ──
            // One entry per day of the viewed month: { date, hours|null } where hours is the
            // ADR-032 consumption basis from THE SAME ConsumptionCalculator path the full-day
            // save guard demands (weekday norm / weekend 0 / academic 7.4 × fraction fallback /
            // null where no dated profile covers the day), rounded through the SHARED
            // RoundBasis site — the served value IS the guard's requiredHours (the R3
            // served==guard identity). The FE snap reads consumptionBasis[date]; it never does
            // client norm math. Neither dailyNorm (display basis, null for academics) nor the
            // fullDayNormAtMonthEnd scalar is overloaded for this (R3). Fail-soft like the R10
            // scalar above: the S34 data-integrity fail-loud (profile row without a covering
            // agreement row) must never 500 a read — that day serves null and the save-path
            // guards keep fail-louding.
            var consumptionBasis = new List<SkemaDayHoursRow>(daysInMonth);
            for (var basisDay = monthStart; basisDay <= monthEnd; basisDay = basisDay.AddDays(1))
            {
                decimal? basis;
                try
                {
                    basis = await consumptionCalculator.FullDayHoursAsync(
                        employeeId, basisDay, user.PrimaryOrgId, ct);
                }
                catch (EmployeeProfileNotFoundException)
                {
                    basis = null;
                }
                consumptionBasis.Add(new SkemaDayHoursRow(basisDay, RoundBasis(basis)));
            }

            // Get approval period for this month. S120 / TASK-12000 — the nullable-complex
            // member (the S117 allOf wrapper's application #4); minted new after the
            // sibling-CHECK against the S116 ApprovalResponses family (no 6-key sibling).
            var period = await approvalRepo.GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd, ct);

            // ── S124 / TASK-12405 — THE LEADER MONTH GATE (Step-7a Codex BLOCKER) ──────────────
            // A leader may read this month's grid ONLY once the employee has SENT it. Both this gate
            // and the team-overview withholding call the SAME `ApprovalVisibility` member, so the two
            // surfaces cannot drift: a row whose figures are withheld must not have its full grid
            // readable through the back door. (Step-7a internal lens: these had been two hand-copied
            // literal lists bound only by a comment — the shared member is the actual mechanism.)
            //   • no period at all → nothing was ever sent → denied.
            //   • DRAFT (i.e. a reopened month, since the create path submits in the same tx) → denied.
            // Fail-CLOSED: an unrecognised future status is NOT readable by a leader.
            if (leaderTierRead)
            {
                var sentToLeader = ApprovalVisibility.IsSubmittedToManager(period?.Status);
                if (!sentToLeader)
                {
                    // S128 / TASK-12804 — the 403 body moved VERBATIM into the shared construction
                    // site (one wire shape across this gate + the RES-002 sibling gates).
                    return ApprovalReadTier.MonthNotSubmittedForbidden();
                }
            }

            SkemaApprovalInfo? approval = period is not null
                ? new SkemaApprovalInfo(
                    PeriodId: period.PeriodId,
                    Status: period.Status,
                    EmployeeDeadline: period.EmployeeDeadline,
                    ManagerDeadline: period.ManagerDeadline,
                    EmployeeApprovedAt: period.EmployeeApprovedAt,
                    RejectionReason: period.RejectionReason)
                : null;

            // Compute deadlines. S140 / TASK-14004: the +2 / +5 offsets are no longer written
            // literally here — they come from InstitutionalDeadlines (SharedKernel), the ONE named
            // source of truth the send flow's stamping site and the new HR follow-up reads' computed
            // fallback also consume. The values are the owner-ratified PROVISIONAL institutional
            // defaults (SYSTEM_TARGET §G names cutoff dates as per-institution configuration that
            // does not exist yet); nothing about this read's behaviour changes.
            var (employeeDeadline, managerDeadline) = InstitutionalDeadlines.ForMonthEnd(monthEnd);

            // S120 / TASK-12000 — named record (BYTE-IDENTICAL wire JSON; the skema family
            // carries NO ruled delta).
            return Results.Ok(new SkemaMonthResponse(
                Year: year,
                Month: month,
                DaysInMonth: daysInMonth,
                Projects: projects.Select(p => new ProjectResponse(
                    ProjectId: p.ProjectId,
                    ProjectCode: p.ProjectCode,
                    ProjectName: p.ProjectName,
                    SortOrder: p.SortOrder)).ToList(),
                AbsenceTypes: absenceTypes,
                Entries: entries,
                Absences: absences,
                WorkTime: workTime,
                DailyNorm: dailyNorm,
                Approval: approval,
                EmployeeDeadline: employeeDeadline,
                ManagerDeadline: managerDeadline,
                // ── S72 / TASK-7201 additive fields (ALL existing fields above are
                // byte-unchanged; the redesigned FE consumes these four) ──
                RowPreferences: rowPreferences,
                Catalogs: catalogs,
                BoundaryWorkTime: boundaryWorkTime,
                FullDayNormAtMonthEnd: fullDayNormAtMonthEnd,
                // ── S73 / TASK-7301 additive field (SPRINT-73 R3) ──
                ConsumptionBasis: consumptionBasis));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<SkemaMonthResponse>(StatusCodes.Status200OK); // S120 / TASK-12000

        // ── POST /api/skema/{employeeId}/save — Batch save entries and absences ──

        app.MapPost("/api/skema/{employeeId}/save", async (
            string employeeId,
            SaveSkemaRequest request,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ApprovalPeriodRepository approvalRepo,
            // S136 / TASK-13603 — the in-lock employment-window read (ADR-040 D1–D3; the
            // (conn, tx) surface only — the SharedKernel self-managed twin would read OUTSIDE
            // the advisory lock). See the in-lock gate inside the transaction below.
            IEmploymentWindowResolverInTx employmentWindowResolver,
            EntitlementConfigRepository entitlementConfigRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
            // S59 / TASK-5907 — per-employee CHILD_SICK eligibility (dated read) for the
            // pre-transaction absence eligibility gate.
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            IHttpClientFactory httpClientFactory,
            // S27 TASK-2706 atomic POST: state-change site moved off
            // IEventStore.AppendAsync and onto IOutboxEnqueue.EnqueueAndReturnIdAsync
            // (ADR-018 D3). The outer transaction wraps every event emit + the
            // entitlement_balances UPDATE + the projection INSERTs together so
            // a quota breach (SkemaQuotaBreachException) rolls back the entire
            // bundle (TASK-2706 (d) bundle-rollback semantic). Projection writes
            // happen inside the same tx (sync-in-tx, ADR-018 D12) so the GET
            // handler above sees this save's rows immediately on the next read.
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            TimeEntryProjectionRepository timeEntryProjectionRepo,
            AbsenceProjectionRepository absenceProjectionRepo,
            WorkTimeProjectionRepository workTimeProjectionRepo,
            // S60 / TASK-6005 — dated employment profile for the part_time_fraction used in
            // the MONTHLY_ACCRUAL earned-to-date / bookableLimit computation.
            IEmploymentProfileResolver profileResolver,
            // S81 / TASK-8102 (R1) — year-start-aligned dated entitlement-config resolution for the
            // pre-transaction quota validation (the GRACEFUL family; mirrors the year-overview path).
            DatedEntitlementConfigResolverFactory datedConfigResolverFactory,
            // S66 / TASK-6603 — ADR-032 consumption (feriedage) calculator. Used in TWO places:
            // (1) the D3 per-day norm cap + the provisional pre-lock requestedDays (advisory),
            // (2) the AUTHORITATIVE in-lock re-derivation that stamps AbsenceRegistered.Feriedage
            // and drives the guard delta. Composes DailyNormCalculator (shared norm seam).
            StatsTid.Backend.Api.Services.ConsumptionCalculator consumptionCalculator,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only save own data
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only save own data" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                // ── S124 / TASK-12404 — owner ruling 2026-07-30 ──────────────────────────────
                // "A manager can never edit an employee's registrations. Only HR and admins can."
                //
                // Before this, ANY non-Employee actor whose org-scope covered the target could write
                // another employee's time data — which includes LocalLeader. The floor lifts that to
                // LocalHR-or-above (LocalHR / LocalAdmin / GlobalAdmin) via the EXISTING per-scope
                // roleFloor mechanism (OrgScopeValidator:88-91): a scope below the floor never admits,
                // so a mixed-role actor's LEADER scope cannot carry the write while their HR scope,
                // if any, still can.
                //
                // SELF IS EXEMPT, and that exemption is load-bearing: a LocalLeader is also an
                // employee who registers their OWN time. They are not Employee-role, so they fall
                // through to this scope branch — applying the HR floor unconditionally would have
                // locked every leader out of their own timesheet.
                //
                // READS are untouched. A leader must still review the full month grid of a submitted
                // period (TASK-12403) — this narrows WRITES only.
                //
                // ── S136 / TASK-13603 — TERMINATED-INCLUSIVE validator swap (ADR-040 D3) ─────────
                // Surface 3 of the S70 R9c allowlist extension this task makes (mandatory
                // Security-invariant review; surfaces 1+2 are on POST /api/time-entries). The shared
                // ValidateEmployeeAccessAsync resolves its target ACTIVE-ONLY, so a deactivated
                // leaver 403'd ("Target employee not found") even for in-scope HR BEFORE the subject
                // read below could 404 — locking HR out of the routine final-month correction (the
                // leaver's last in-window facts). The IncludingTerminated validator is
                // behavior-identical for ACTIVE targets under the same writeFloor (the S124 floor +
                // self-exemption above are byte-preserved) and admits a TERMINATED subject only via
                // its R9b/R9f1 gates: the ADMITTING scope must itself be LocalHR or above.
                // Employee-role actors never reach this branch (handled above); Employee-SHAPED
                // actors (no role / all-Employee scopes) are denied outright by the validator's
                // no-own-data-branch decision (R9b) — strictly fail-closed vs the old validator.
                var writeFloor = string.Equals(employeeId, actor.ActorId, StringComparison.Ordinal)
                    ? null
                    : StatsTidRoles.LocalHR;
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                    actor, employeeId, writeFloor, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Get employee profile for agreement info.
            //
            // ── S136 / TASK-13603 — terminated-INCLUSIVE subject read (ADR-040 D3 / SEC-046) ─────
            // Was the ACTIVE-ONLY GetByIdAsync, which 404'd a deactivated leaver for EVERYONE — HR
            // included (the accident the validator swap above un-blocks), and 404'd the terminated
            // SELF only by that same accident rather than by decision. The R9c terminated-inclusive
            // read resolves the leaver's row so HR CAN write in-window facts for them; the explicit
            // D3 role floor below is what refuses everyone else, deliberately.
            var user = await userRepo.GetByIdIncludingTerminatedAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // ADR-040 D3: the SELF-EXEMPTION YIELDS TO SUBJECT DEACTIVATION. is_active governs
            // login/session for the ACTOR; the SUBJECT's deactivation state selects the write
            // floor — HROrAbove, even when subject == actor (a deactivated self is not exempt; the
            // SEC-046 rule, one predicate across both registration writers). Employee-role actors
            // bypass the scope validator entirely (the own-data branch above), so THIS check is the
            // load-bearing one for them; for non-Employee actors the IncludingTerminated validator
            // already enforced the stronger PER-SCOPE floor (R9f1) — this primary-role re-check is
            // defense-in-depth there and can never deny an actor that validator admitted (its R9b
            // gate already requires primary role ≥ LocalHR for a terminated subject).
            //
            // S136 Step-5a (Codex BLOCKER 1) — this pre-transaction check is the ADVISORY FAST
            // PATH (advisory-then-authoritative, the house pattern the month-lock fast path below
            // already follows): it reads on a pooled connection, outside the write transaction and
            // outside EmployeeConsumptionLock, so a deactivation that commits while this request
            // waits on the lock is invisible to it. The AUTHORITATIVE D3 floor is the in-lock
            // subject-state re-check inside the transaction below.
            if (!user.IsActive &&
                (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
            {
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Writes for a deactivated employee require LocalHR or above"
                }, statusCode: 403);
            }

            // Check approval period status
            var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);
            var monthStart = new DateOnly(request.Year, request.Month, 1);
            var monthEnd = new DateOnly(request.Year, request.Month, daysInMonth);

            // S34 / TASK-3411 — ADR-023 D2 binding cutover for agreement_code on past-month
            // saves. Employees can edit prior-period Skema entries before manager approval;
            // for those saves, the quota/entitlement validation + the AgreementCode field
            // stamped onto TimeEntryRegistered / AbsenceRegistered events MUST reflect the
            // agreement that was in effect on the first day of the month being saved — not
            // today's live cache. Without this, an AC → HK switch today would cause last
            // month's vacation save to validate against (and be stamped with) HK's quota
            // rules instead of AC's.
            //
            // ADR-023 D3 (Skema POST = graceful-fallback consumer): null dated lookup falls
            // through to live `user.AgreementCode`. Defensive only — TASK-3403 backfill
            // covers every user — but the fallback keeps the POST functional in the edge
            // case where a user is created after the period being saved.
            //
            // Quota-breach 422 trichotomy (S26 / ADR-018 D13 — atomic-rollback + 422 + clean
            // state + read-your-write) is preserved: this cutover only changes which row of
            // entitlement_configs is selected for validation; the atomic-tx wrap, the
            // SkemaQuotaBreachException → 422 mapping, and the projection RYW guarantee are
            // unchanged.
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, monthStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            // S127 / TASK-12704 — this is the PRE-TRANSACTION fast path, and it is advisory: it reads
            // on its own connection, outside the write transaction and outside EmployeeConsumptionLock,
            // so a send that commits between here and the write is invisible to it. The AUTHORITATIVE
            // check is the in-lock re-read inside the transaction below. Both route through
            // IsPeriodLockedForSave / PeriodLockedForSaveConflict so they cannot drift.
            var period = await approvalRepo.GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd, ct);
            if (ApprovalPeriodSaveLock.IsPeriodLockedForSave(period))
                return ApprovalPeriodSaveLock.PeriodLockedForSaveConflict(period!);

            // ── S145 / TASK-14507 (A0, R24 + the Step-5a ProjectCode finding) — cell shape ──
            // A Skema save REPLACES each posted cell (see the insert loops below), so every cell
            // must carry a real value. Two shapes are refused with a plain 400, for EVERY cell of
            // the save BEFORE anything else happens (before the rule-engine call, the advisory lock
            // and the transaction), so one bad cell fails the whole save and nothing is written:
            //   (1) hours <= 0, for an entry or an absence. Mirrors the `hours <= 0` half of
            //       RequestValidator.ValidateTimeEntry (the additive route's rule). Why: clearing a
            //       cell is out of S145 (SPRINT-72 R17 stays open), and a 0-hour CURRENT row would be
            //       counted by the strand check, served by the GETs and emitted as a 0-hour
            //       NORMAL_HOURS line. The grid already drops empty and zero cells before posting
            //       (useSkema.ts), so this refuses only a hand-built request. The storage design still
            //       allows a later retraction (mark the current row superseded, insert nothing).
            //   (2) an entry whose projectCode is null, empty or whitespace (IsNullOrWhiteSpace, the
            //       RequestValidator idiom). `required string` only demands that the property is
            //       PRESENT, so an explicit JSON null passes model binding and would become a NULL
            //       task_id, i.e. a cell keyed by no task at all.
            // The cell date echoed back is the request's own registration date, never an
            // employment date (ADR-040 D7).
            foreach (var entry in request.Entries ?? Array.Empty<SkemaEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.ProjectCode))
                    return Results.BadRequest(new
                    {
                        error = "skema_entry_project_code_missing",
                        date = entry.Date,
                        message = $"Registreringen for {entry.Date:dd-MM-yyyy} mangler en projektkode."
                    });
                if (entry.Hours <= 0m)
                    return Results.BadRequest(new
                    {
                        error = "skema_cell_hours_not_positive",
                        date = entry.Date,
                        projectCode = entry.ProjectCode,
                        hours = entry.Hours,
                        message = $"Timer for {entry.Date:dd-MM-yyyy} skal være større end 0."
                    });
            }
            foreach (var absence in request.Absences ?? Array.Empty<SkemaAbsence>())
            {
                if (absence.Hours <= 0m)
                    return Results.BadRequest(new
                    {
                        error = "skema_cell_hours_not_positive",
                        date = absence.Date,
                        absenceType = absence.AbsenceType,
                        hours = absence.Hours,
                        message = $"Timer for {absence.Date:dd-MM-yyyy} skal være større end 0."
                    });
            }

            // ── Entry/absence date-range validation (S145 / Step 7a B-1, R-128) ──
            // The sibling of the S56 work-time check below, for the same reason: the approval-lock
            // check above is scoped to the REQUESTED month, so a date outside [monthStart, monthEnd]
            // would be written without that lock governing it. Since S145 (TASK-14507) a save
            // REPLACES the cell it posts, and the mark-superseded statements
            // (TimeEntryProjectionRepository / AbsenceProjectionRepository.MarkSupersededSkemaCellSql)
            // key on employee, date, task or absence type, and source, with NO period condition. So
            // an October save carrying a September cell would retire and rewrite a cell of an
            // already APPROVED or exported September, whose recorded hours payroll reads (R14).
            // Refused here, before the rule-engine call, the advisory lock and the transaction, so
            // nothing is written and no event is emitted. With the work-time check below, all three
            // arrays (Entries, Absences, WorkTime) are now month-bounded. The date in the message is
            // the request's own registration date, never an employment date (ADR-040 D7).
            foreach (var entry in request.Entries ?? Array.Empty<SkemaEntry>())
            {
                if (entry.Date < monthStart || entry.Date > monthEnd)
                    return Results.BadRequest(new
                    {
                        error = "skema_cell_date_out_of_range",
                        message = $"Skema date {entry.Date:yyyy-MM-dd} is outside the requested period {request.Year}-{request.Month:D2}."
                    });
            }
            foreach (var absence in request.Absences ?? Array.Empty<SkemaAbsence>())
            {
                if (absence.Date < monthStart || absence.Date > monthEnd)
                    return Results.BadRequest(new
                    {
                        error = "skema_cell_date_out_of_range",
                        message = $"Skema date {absence.Date:yyyy-MM-dd} is outside the requested period {request.Year}-{request.Month:D2}."
                    });
            }

            // ── Work-time date-range validation (S56 / Step 7a BLOCKER fix) ──
            // The approval-lock check above is scoped to the REQUESTED month. Reject any
            // work-time day outside [monthStart, monthEnd] up front (before the tx) so that
            // lock governs every written date. Without this, a save targeting an unlocked
            // month could smuggle in a date belonging to an already-approved/locked month and
            // overwrite its work_time_projection row, bypassing the period lock. (Since S145
            // Step 7a the Entries and Absences arrays are held to the same month, just above.)
            if (request.WorkTime is not null)
            {
                foreach (var day in request.WorkTime)
                {
                    if (day.Date < monthStart || day.Date > monthEnd)
                        return Results.BadRequest(new
                        {
                            error = "work_time_date_out_of_range",
                            message = $"Work-time date {day.Date:yyyy-MM-dd} is outside the requested period {request.Year}-{request.Month:D2}."
                        });

                    // ── S58 TASK-5802 per-day work-time bounds (Arbejdstid only) ──
                    // Authoritative guard mirroring the inline absence "exceed norm day"
                    // cap below: a physical input invariant (24h/day), NOT an agreement/OK
                    // rule, so it stays in the Backend endpoint — no rule-engine hop.
                    // Three checks, in order: (1) manual hours cannot be negative — without
                    // this, large interval hours could net under 24 via a negative manual
                    // value while still persisting >24 worked hours; (2) intervals on a day
                    // cannot overlap (each interval would otherwise double-count); (3) the
                    // day's total worked time (interval hours + manual hours) cannot exceed
                    // 24h. Exactly 24,0 t is allowed. Interval-hour computation mirrors the
                    // frontend calcIntervalHours (positive end>start diffs, total seconds
                    // rounded to 2 decimals) so UI and server agree.

                    // (1) Negative manual hours.
                    if (day.ManualHours < 0m)
                        return Results.Json(new
                        {
                            error = "work_time_negative_manual_hours",
                            date = day.Date,
                            manualHours = day.ManualHours,
                            message = $"Manuelt registrerede timer for {day.Date:dd-MM-yyyy} kan ikke være negative."
                        }, statusCode: 422);

                    // Parse to (startSec, endSec) keeping only positive-duration intervals,
                    // matching the frontend's `if (diff > 0)` filter.
                    var parsedIntervals = new List<(int Start, int End)>();
                    foreach (var iv in day.Intervals ?? Array.Empty<SkemaWorkInterval>())
                    {
                        if (TryParseTimeToSeconds(iv.Start, out var startSec)
                            && TryParseTimeToSeconds(iv.End, out var endSec)
                            && endSec > startSec)
                        {
                            parsedIntervals.Add((startSec, endSec));
                        }
                    }

                    // (2) Overlapping intervals: sort by start, reject if any interval starts
                    // strictly before the previous one ends. Touching boundaries (==) are OK.
                    parsedIntervals.Sort((a, b) => a.Start.CompareTo(b.Start));
                    for (var i = 1; i < parsedIntervals.Count; i++)
                    {
                        if (parsedIntervals[i].Start < parsedIntervals[i - 1].End)
                            return Results.Json(new
                            {
                                error = "work_time_intervals_overlap",
                                date = day.Date,
                                message = $"Arbejdsperioderne for {day.Date:dd-MM-yyyy} overlapper hinanden."
                            }, statusCode: 422);
                    }

                    // (3) Total worked hours > 24.
                    var intervalSeconds = parsedIntervals.Sum(p => p.End - p.Start);
                    var periodHours = Math.Round(intervalSeconds / 3600m, 2);
                    var totalHours = Math.Round(periodHours + day.ManualHours, 2);
                    if (totalHours > 24m)
                        return Results.Json(new
                        {
                            error = "work_time_exceeds_day",
                            date = day.Date,
                            totalHours,
                            maxHours = 24,
                            message = $"Arbejdstid for {day.Date:dd-MM-yyyy} må ikke overstige 24 timer (registreret: {FormatHoursMinutes(totalHours)})."
                        }, statusCode: 422);
                }
            }

            // ── The absence vocabulary gate (S147 / TASK-14708, QUAL-231; NR-4) ──
            // Every absence code must be one of the SharedKernel vocabulary (AbsenceTypes), each
            // classified exactly once in AbsenceClassification. Why: payroll passes the stored code
            // to the Rule Engine unchanged, and the engine credits or reduces by that one
            // classification. A code outside it would be saved, exported, and then silently count
            // as neither — exactly the kind of drift QUAL-231 was (Skema stored SICK_DAY and
            // CHILD_SICK_DAY while the engine's list spelled other codes). The grid only posts
            // codes it was served, so this refuses a hand-built or stale request. Refused here,
            // before any absence lookup, the rule-engine call, the advisory lock and the
            // transaction, so nothing is written. The body names the code, never a date (the gate
            // is about the code, not the day).
            foreach (var absence in request.Absences ?? Array.Empty<SkemaAbsence>())
            {
                if (!AbsenceClassification.IsKnown(absence.AbsenceType))
                    return Results.Json(new
                    {
                        error = "absence_type_unknown",
                        absenceType = absence.AbsenceType,
                        message = $"Fraværstypen '{absence.AbsenceType}' findes ikke."
                    }, statusCode: 422);
            }

            // ── Per-day absence validation (S47 TASK-2C) ──
            // Reject early if the request contains duplicate absence types on the same day
            // or if total absence hours for any single day exceed the standard norm (7.4h).
            if (request.Absences is not null && request.Absences.Length > 0)
            {
                var absencesByDateAndType = request.Absences
                    .GroupBy(a => new { a.Date, a.AbsenceType });
                foreach (var group in absencesByDateAndType)
                {
                    if (group.Count() > 1)
                    {
                        return Results.Json(new
                        {
                            error = "Duplicate absence type on same day",
                            date = group.Key.Date,
                            absenceType = group.Key.AbsenceType,
                            count = group.Count(),
                        }, statusCode: 422);
                    }
                }

                // ── S66 / TASK-6603 — ADR-032 D3 per-day norm cap (replaces the flat 7.4 cap) ──
                // The day's total absence hours (all types — today's grouping) is capped at the
                // employee's REAL per-day norm (fullDayHours), not the flat 7.4h. A half-time
                // employee's day caps at 3.7h; a full-time 5-day employee still caps at 7.4h
                // (byte-identical). fullDayHours resolution (ADR-032 D3):
                //   • weekday WEEKLY_HOURS norm > 0 ⇒ that norm is the cap;
                //   • ANNUAL_ACTIVITY (academic) ⇒ 7.4 × fraction (calculator fallback) — vacation
                //     stays bookable;
                //   • weekend (norm == 0) ⇒ entitlement-consuming rows are rejected (you cannot
                //     consume a feriedag on a non-working day); NON-entitlement rows keep today's
                //     legacy flat-7.4 behavior (a sick/leave hour on a Saturday is not gated by an
                //     entitlement);
                //   • no dated profile covering the day (null) ⇒ NOT enforced here — the existing
                //     anchor-422 family below (employment_profile_missing) owns that rejection;
                //     do NOT relax it.
                // This guard runs PRE-TX as fast advisory UX (a clean 422 before opening the tx);
                // the AUTHORITATIVE enforcement is the in-lock re-derivation + CheckAndAdjustAsync
                // (a stale norm here is benign — the in-lock guard re-enforces). Kept pre-tx (not
                // folded into the lock) so the common reject path stays a cheap pre-tx 422,
                // coherent with the sibling duplicate-type / day-norm / eligibility guards already
                // here; no coherence is lost because the in-lock path is the source of truth.
                {
                    var absencesByDate = request.Absences.GroupBy(a => a.Date);
                    foreach (var dayGroup in absencesByDate)
                    {
                        var date = dayGroup.Key;
                        var fullDayHours = await consumptionCalculator.FullDayHoursAsync(
                            employeeId, date, user.PrimaryOrgId, ct);

                        // No-profile day: defer to the anchor-422 family (do not enforce here).
                        if (fullDayHours is null)
                            continue;

                        if (fullDayHours.Value > 0m)
                        {
                            // Working day: total hours (all types) capped at the day's real norm.
                            // S73 / TASK-7301 (H1) — cap against the SAME RoundBasis-rounded basis
                            // (ADR-032 D1 AwayFromZero 2-dec) the full-day rule + served
                            // consumptionBasis use. The raw norm (e.g. academic 7.4 × 0.335 =
                            // 2.4790) under-rounds the bookable full day (RoundBasis ⇒ 2.48),
                            // making the exact full omsorgsdag unbookable. The cap was the
                            // D1-inconsistent side; reuse the existing RoundBasis helper (one
                            // rounding site, no second convention).
                            var cappedHours = RoundBasis(fullDayHours)!.Value;
                            var totalHours = dayGroup.Sum(a => a.Hours);
                            if (totalHours > cappedHours)
                                return Results.Json(new
                                {
                                    error = "Total absence hours exceed norm day",
                                    date,
                                    totalHours,
                                    maxHours = cappedHours,
                                }, statusCode: 422);
                        }
                        else
                        {
                            // Zero-norm (weekend) day: entitlement-consuming rows cannot be booked
                            // (no feriedag is consumable on a non-working day). Non-entitlement
                            // rows keep today's behavior under the legacy flat-7.4 cap.
                            var offendingRows = dayGroup
                                .Where(a => GetEntitlementType(a.AbsenceType) is not null)
                                .ToList();
                            if (offendingRows.Count > 0)
                                return Results.Json(new
                                {
                                    error = "Entitlement absence on a non-working day",
                                    date,
                                    absenceTypes = offendingRows.Select(a => a.AbsenceType).ToArray(),
                                    message = $"Ferie/særlige feriedage kan ikke registreres på en arbejdsfri dag ({date:dd-MM-yyyy}).",
                                }, statusCode: 422);

                            var nonEntitlementHours = dayGroup.Sum(a => a.Hours);
                            if (nonEntitlementHours > StandardDayHours)
                                return Results.Json(new
                                {
                                    error = "Total absence hours exceed norm day",
                                    date,
                                    totalHours = nonEntitlementHours,
                                    maxHours = StandardDayHours,
                                }, statusCode: 422);
                        }
                    }
                }

                // ── S73 / TASK-7301 — the R2 FULL-DAY-ONLY guard (pre-tx advisory mirror) ──
                // Owner ruling D-A (2026-06-13): CARE_DAY + SENIOR_DAY are whole days. For an
                // absence row whose entitlement type carries entitlement_configs.full_day_only
                // (read DATED per ROW at the absence date via the SENIOR_DAY GetByTypeAtAsync
                // anchor precedent — NOT the quota read's year-start anchor), the row's hours
                // must equal the day's ADR-032 consumption basis EXACTLY (2-dec via the shared
                // RoundBasis site; the SAME ConsumptionCalculator path as the D3 cap above,
                // academic 7.4 × fraction fallback included). Otherwise: typed 422
                // `absence_full_day_only` carrying requiredHours (== the month GET's served
                // consumptionBasis value for the day — the R3 identity).
                //
                // Placement (deliberate): AFTER the D3 norm cap, so a full day PLUS any other
                // same-day absence keeps surfacing the total-cap 422 (the D-A ratified
                // arithmetic consequence: a full omsorgsdag/seniordag is the day's ONLY
                // absence; same-day WORK hours remain legal — work entries are not absences
                // and never enter this guard). Skips what the sibling guards own: a null basis
                // (no dated profile) → the anchor-422 employment_profile_missing family below;
                // a zero-norm (weekend) basis → the D3 non-working-day 422 above already
                // rejected every entitlement row. Like its D3 sibling this pre-tx site is the
                // cheap advisory mirror; the in-lock B3 re-check inside the save tx is the
                // AUTHORITATIVE enforcement point (the S70 B1 in-lock re-evaluation lesson).
                {
                    var fullDayFlagCache = new Dictionary<(string EntitlementType, DateOnly Date), bool>();
                    var fullDayBasisCache = new Dictionary<DateOnly, decimal?>();
                    foreach (var absence in request.Absences)
                    {
                        if (GetEntitlementType(absence.AbsenceType) is not { } fdEntitlementType)
                            continue; // non-entitlement rows are never full-day gated.

                        if (!fullDayFlagCache.TryGetValue((fdEntitlementType, absence.Date), out var isFullDayOnly))
                        {
                            // S73 / TASK-7301 (H2) — resolve the OK version FROM the absence date
                            // (OkVersionResolver), NOT the live user.OkVersion: an OK26-current
                            // employee editing an OK24-dated absence must read the OK24 config row
                            // (the same temporal anchor as the AbsenceRegistered.OkVersion stamp at
                            // the emission site). The asOf date stays absence.Date.
                            var fdConfig = await entitlementConfigRepo.GetByTypeAtAsync(
                                fdEntitlementType, agreementCode,
                                OkVersionResolver.ResolveVersion(absence.Date), absence.Date, ct);
                            isFullDayOnly = fdConfig?.FullDayOnly ?? false;
                            fullDayFlagCache[(fdEntitlementType, absence.Date)] = isFullDayOnly;
                        }
                        if (!isFullDayOnly)
                            continue;

                        if (!fullDayBasisCache.TryGetValue(absence.Date, out var basis))
                        {
                            basis = await consumptionCalculator.FullDayHoursAsync(
                                employeeId, absence.Date, user.PrimaryOrgId, ct);
                            fullDayBasisCache[absence.Date] = basis;
                        }
                        if (basis is null || basis.Value <= 0m)
                            continue; // null → anchor-422 family; zero-norm → D3 weekend 422 above.

                        var requiredHours = RoundBasis(basis)!.Value;
                        if (absence.Hours != requiredHours)
                            return Results.Json(new
                            {
                                error = "absence_full_day_only",
                                absenceType = absence.AbsenceType,
                                date = absence.Date,
                                requiredHours,
                                message = $"{AbsenceTypeLabels.LabelFor(absence.AbsenceType)} kan kun registreres som hel dag ({requiredHours} timer) den {absence.Date:dd-MM-yyyy}.",
                            }, statusCode: 422);
                    }
                }

                // ── S59 / TASK-5907 per-employee eligibility gate (pre-transaction, atomic) ──
                // Authoritative reject-before-write check, alongside the duplicate/day-norm
                // guards above. Reads are dated as-of EACH absence's own date — NOT wall-clock
                // save time — so past-period saves and replays are deterministic (P4, ADR-020).
                // Anchor difference vs the GET filter is INTENTIONAL: GET (display) uses
                // month-end; POST (authoritative) uses absence.Date per row. They are not
                // required to agree mid-month (refinement line 19/20, SPRINT-59 TASK-5907).
                {
                    // SENIOR_DAY age gate is decided BY the rule engine (PAT-005 / ADR-002):
                    // the Backend only derives the integer age and passes MinAge +
                    // EmployeeAgeAsOfAbsenceDate; DOB never crosses the rule-engine boundary.
                    // S73 / TASK-7300 (R1): the NAMED rule-engine client — BaseAddress +
                    // Authorization/X-Correlation-Id forwarding are wired centrally in
                    // Program.cs (RuleEngineClient / RuleEngineHeaderForwardingHandler).
                    var seniorHttpClient = httpClientFactory.CreateClient(Http.RuleEngineClient.Name);
                    var seniorJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                    foreach (var absence in request.Absences)
                    {
                        var gateEntitlementType = GetEntitlementType(absence.AbsenceType);

                        // (a) CHILD_SICK — Backend fact-gate (not rule-engine logic): reject if
                        //     the employee is ineligible as-of THIS absence's date. Absent row ⇒
                        //     ineligible (same opt-in default as the GET filter).
                        if (string.Equals(gateEntitlementType, "CHILD_SICK", StringComparison.Ordinal))
                        {
                            var eligible = (await eligibilityRepo
                                .GetEligibleAsOfAsync(employeeId, "CHILD_SICK", absence.Date, ct)).Eligible;
                            if (!eligible)
                                return Results.Json(new
                                {
                                    error = "absence_type_not_eligible",
                                    absenceType = absence.AbsenceType,
                                    entitlementType = "CHILD_SICK",
                                    date = absence.Date,
                                    message = $"Medarbejderen er ikke berettiget til {AbsenceTypeLabels.LabelFor(absence.AbsenceType)} på {absence.Date:dd-MM-yyyy}."
                                }, statusCode: 422);
                        }

                        // (b) SENIOR_DAY — DOB-derived age gate, validated PER ROW (a 62nd
                        //     birthday falling mid-month correctly allows later-dated rows and
                        //     rejects earlier ones in the same save). Null DOB ⇒ fail-closed:
                        //     pass a null age so the rule engine rejects (it already fail-closes
                        //     on null age when MinAge is set, TASK-5904).
                        else if (string.Equals(gateEntitlementType, "SENIOR_DAY", StringComparison.Ordinal))
                        {
                            // S59 / Step-7a BLOCKER 2 — resolve the SENIOR_DAY config AS-OF THIS
                            // row's absence date (not the live/open row), so min_age is read at
                            // the same anchor as the per-row age computation below (determinism,
                            // P4 / ADR-020). Mirrors the dated quota read (GetByTypeAtAsync)
                            // further down. agreementCode is the past-effective code already
                            // resolved for this save (ADR-023 D2).
                            var seniorConfig = await entitlementConfigRepo.GetByTypeAtAsync(
                                "SENIOR_DAY", agreementCode, user.OkVersion, absence.Date, ct);
                            int? seniorMinAge = seniorConfig?.MinAge;

                            if (seniorMinAge is not null)
                            {
                                // Derived integer age as-of THIS row's date; null when DOB unknown.
                                int? ageAsOf = user.BirthDate is { } dob ? AgeAsOf(dob, absence.Date) : null;

                                var seniorRequest = new
                                {
                                    annualQuota = 0m,
                                    used = 0m,
                                    planned = 0m,
                                    carryoverIn = 0m,
                                    requestedDays = 0m,
                                    partTimeFraction = 1.0m,
                                    proRateByPartTime = false,
                                    isPerEpisode = false,
                                    perEpisodeLimit = (decimal?)null,
                                    minAge = seniorMinAge,
                                    employeeAgeAsOfAbsenceDate = ageAsOf
                                };

                                var seniorResponse = await seniorHttpClient.PostAsJsonAsync(
                                    "/api/rules/validate-entitlement", seniorRequest, seniorJsonOptions, ct);
                                if (!seniorResponse.IsSuccessStatusCode)
                                    return Results.Json(new { error = "Entitlement validation service unavailable" }, statusCode: 503);

                                var seniorResult = await seniorResponse.Content
                                    .ReadFromJsonAsync<EntitlementValidationResult>(seniorJsonOptions, ct);
                                if (seniorResult is null)
                                    return Results.Json(new { error = "Invalid entitlement validation response" }, statusCode: 502);

                                if (!seniorResult.Allowed)
                                    return Results.Json(new
                                    {
                                        error = "absence_type_not_eligible",
                                        absenceType = absence.AbsenceType,
                                        entitlementType = "SENIOR_DAY",
                                        date = absence.Date,
                                        message = ageAsOf is null
                                            ? $"Seniordag kan ikke registreres på {absence.Date:dd-MM-yyyy}: fødselsdato er ikke registreret."
                                            : $"Seniordag kan ikke registreres på {absence.Date:dd-MM-yyyy}: medarbejderen er under {seniorMinAge.Value} år."
                                    }, statusCode: 422);
                            }
                        }
                    }
                }
            }

            // ── Pre-compute entitlement data for validation and post-save adjustment ──
            // Aggregate requested hours per entitlement type.
            // S60 / TASK-6005 — the atomic guard now takes a carryover-EXCLUDED GuardCap and a
            // separate SeedQuota (= annual entitlement). GuardCap is the per-type business
            // bookableLimit MINUS carryover (for IMMEDIATE types it equals the annual
            // effectiveQuota, so behavior is unchanged); SeedQuota seeds total_quota on a
            // first-INSERT with the annual entitlement (its invariant meaning, ADR-021 D6 / P3).
            var entitlementData = new Dictionary<string, (decimal RequestedDays, int EntitlementYear, decimal GuardCap, decimal SeedQuota)>(StringComparer.Ordinal);

            // S73 / TASK-7301 Step-7a B1 — resolve which absence rows are FULL-DAY-ONLY
            // (CARE_DAY / SENIOR_DAY under SPRINT-73 R2), so their consumption divides by the
            // SAME 2-dec rounded basis the full-day guard requires (an exact full day → 1.0
            // feriedage, never 1.0004). Reads the dated entitlement config per (type, date),
            // OK version resolved FROM the absence date (the H2 anchor) — the SAME predicate the
            // full-day guard evaluates. The (conn, tx) overload reads under the in-lock tx so a
            // config supersession that committed before the lock is visible to the authoritative
            // valuation; the pre-lock provisional call passes null,null (advisory only). A null
            // dated config (or false flag) ⇒ NOT full-day-only ⇒ raw-norm division (unchanged).
            async Task<IReadOnlySet<int>> ResolveFullDayOnlyIndicesAsync(
                NpgsqlConnection? conn, NpgsqlTransaction? tx)
            {
                var indices = new HashSet<int>();
                if (request.Absences is null)
                    return indices;
                var flagCache = new Dictionary<(string EntitlementType, DateOnly Date), bool>();
                for (var i = 0; i < request.Absences.Length; i++)
                {
                    var absence = request.Absences[i];
                    if (GetEntitlementType(absence.AbsenceType) is not { } entitlementType)
                        continue; // non-entitlement rows are never full-day gated.
                    if (!flagCache.TryGetValue((entitlementType, absence.Date), out var isFullDayOnly))
                    {
                        var version = OkVersionResolver.ResolveVersion(absence.Date);
                        var cfg = conn is not null
                            ? await entitlementConfigRepo.GetByTypeAtAsync(
                                conn, tx!, entitlementType, agreementCode, version, absence.Date, ct)
                            : await entitlementConfigRepo.GetByTypeAtAsync(
                                entitlementType, agreementCode, version, absence.Date, ct);
                        isFullDayOnly = cfg?.FullDayOnly ?? false;
                        flagCache[(entitlementType, absence.Date)] = isFullDayOnly;
                    }
                    if (isFullDayOnly)
                        indices.Add(i);
                }
                return indices;
            }

            // ── S145 / TASK-14507 (A0, R23 as amended by W1) — the replaced cells' stored feriedage ──
            // A Skema save REPLACES each posted absence cell (employee, date, absence type): the
            // cell's CURRENT Skema row, if there is one, is marked superseded by the insert below.
            // So the quota must move by the SIGNED change, per entitlement type:
            //     delta = Σ new feriedage − Σ stored feriedage of the current rows this save replaces.
            // "Stored" is absences_projection.feriedage, i.e. after any ADR-032 D4 revaluation: exactly
            // what entitlement_balances.used holds for those rows. An unchanged re-save therefore
            // moves nothing, and a reduction gives days back. Read twice, like the valuation, and
            // both times on the caller's transaction: first in phase 2 of the pre-check, on its short
            // read-only REPEATABLE READ snapshot, which the pre-check's balance reads share, so the
            // advisory subtraction is over one real state; then the AUTHORITATIVE re-read under the
            // consumption lock, which drives the balance write. A null stored feriedage counts 0,
            // mirroring the `?? 0m` with which the replaced save added it to `used`. Additive
            // (TIME_POST) rows are never replaced, so the repository read returns Skema rows only.
            async Task<Dictionary<string, decimal>> ReadReplacedFeriedageByTypeAsync(
                NpgsqlConnection conn, NpgsqlTransaction tx)
            {
                var byType = new Dictionary<string, decimal>(StringComparer.Ordinal);
                if (request.Absences is not { Length: > 0 }
                    || !request.Absences.Any(a => GetEntitlementType(a.AbsenceType) is not null))
                    return byType; // nothing posted consumes quota: no replaced quota to read.
                var postedCells = new HashSet<(DateOnly Date, string AbsenceType)>(
                    request.Absences.Select(a => (a.Date, a.AbsenceType)));
                var from = request.Absences.Min(a => a.Date);
                var to = request.Absences.Max(a => a.Date);
                var currentRows = await absenceProjectionRepo.GetCurrentSkemaRowsAsync(
                    conn, tx, employeeId, from, to, ct);
                foreach (var row in currentRows)
                {
                    if (!postedCells.Contains((row.Date, row.AbsenceType)))
                        continue; // a current cell this save does not post stays exactly as it is.
                    if (!AbsenceToEntitlementType.TryGetValue(row.AbsenceType, out var replacedType) || replacedType is null)
                        continue; // non-entitlement absence types consume no quota.
                    byType[replacedType] = byType.GetValueOrDefault(replacedType) + (row.Feriedage ?? 0m);
                }
                return byType;
            }

            if (request.Absences is not null && request.Absences.Length > 0)
            {
                // ── S66 / TASK-6603 — ADR-032 D2 PROVISIONAL per-row feriedage (pre-lock) ──
                // requestedDays for the rule-engine advisory validation is now the Σ of PER-ROW
                // feriedage (hours ÷ that day's real fullDayHours), NOT totalHours ÷ 7.4. The
                // provisional values are computed OUTSIDE the lock (the rule-engine HTTP call must
                // never sit inside the lock — ADR-032 D2). A stale provisional advisory result is
                // benign: the in-lock guard re-derives the AUTHORITATIVE values and re-enforces
                // (the :910-area TOCTOU comment, extended below for the D2 two-phase contract).
                // The full-day-only divisor consistency (B1) applies here too so the advisory
                // requestedDays sees the same 1.0-per-full-day total the authoritative path will.
                var provisionalFullDayOnly = await ResolveFullDayOnlyIndicesAsync(null, null);
                var provisional = await consumptionCalculator.ComputeAsync(
                    employeeId,
                    request.Absences.Select(a => (a.Date, a.Hours)).ToList(),
                    user.PrimaryOrgId, ct, provisionalFullDayOnly);

                var requestedByEntitlementType = new Dictionary<string, decimal>(StringComparer.Ordinal);
                for (var i = 0; i < request.Absences.Length; i++)
                {
                    var absence = request.Absences[i];
                    if (!AbsenceToEntitlementType.TryGetValue(absence.AbsenceType, out var entitlementType) || entitlementType is null)
                        continue;
                    // S66 / TASK-6603 FIX-FORWARD (B1 pre-tx mirror) — fail-CLOSED for entitlement
                    // rows on a no-profile day: a null fullDayHours (⇒ null Feriedage) means this
                    // consuming row cannot be valued, so reject NOW with the employment_profile_missing
                    // 422 family (cheap UX — a clean pre-tx reject) instead of letting it contribute a
                    // silent 0 to the provisional sum. The in-lock B1 check remains the authoritative
                    // enforcement point (a profile may still race away between here and the lock); this
                    // pre-tx mirror just fast-fails the common no-profile case.
                    if (provisional[i].FullDayHours is null)
                        return Results.Json(new
                        {
                            error = "employment_profile_missing",
                            absenceType = entitlementType,
                            date = absence.Date,
                            message = $"Kan ikke validere Ferie/særlige feriedage for {absence.Date:dd-MM-yyyy}: ansættelsesprofil mangler."
                        }, statusCode: 422);
                    var rowFeriedage = provisional[i].Feriedage ?? 0m;
                    if (!requestedByEntitlementType.ContainsKey(entitlementType))
                        requestedByEntitlementType[entitlementType] = 0m;
                    requestedByEntitlementType[entitlementType] += rowFeriedage;
                }

                // S73 / TASK-7300 (R1): the NAMED rule-engine client (see the senior-gate site
                // above) — bearer + correlation forwarding live in the central handler.
                var httpClient = httpClientFactory.CreateClient(Http.RuleEngineClient.Name);
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                // ── S145 / TASK-14507 (Step-5a cycles 1-2) — the quota pre-check runs in THREE phases ──
                // In plain terms: the pre-check needs two numbers that must agree with each other (the
                // balance, and the feriedage of the cells this save replaces), plus a call to the rule
                // engine. Reading the two numbers on separate connections at separate moments could pair
                // values that were never true at once (cycle-1 BLOCKER); holding one transaction open
                // across the rule-engine HTTP call would pin a pooled connection while waiting on another
                // service (cycle-2 WARNING). So:
                //   Phase 1 — per entitlement type, in the dictionary's order, every read and derivation
                //             that needs no balance, on pooled connections as before. It returns NOTHING:
                //             where the single loop used to `continue` (no live config) or answer the
                //             profile-missing 422, it RECORDS that outcome on the type's plan. It stops at
                //             the first recorded refusal, because the single loop never reached a later
                //             type either.
                //   Phase 2 — one short read-only REPEATABLE READ snapshot: the replaced-cells read and
                //             the balance of every type phase 1 planned (all but the no-config skips).
                //             Nothing else runs inside it: no config read, no profile read, no HTTP.
                //   Phase 3 — per type, in the same order, the single loop's decisions replayed with no
                //             connection held: the recorded skip, the recorded 422, the signed delta, the
                //             rule-engine call for delta > 0 (503 / 502 / 422 unchanged), entitlementData.
                // The refusal order is the single loop's: for type k, the profile-missing 422 answers only
                // if no earlier type's rule-engine call refused first, and before type k's own call.
                var preCheckPlans = new List<QuotaPreCheckPlan>();

                // ── Phase 1 ──────────────────────────────────────────────────────────────────────────
                foreach (var (entitlementType, totalRequestedDays) in requestedByEntitlementType)
                {
                    var firstAbsenceDate = request.Absences
                        .Where(a => AbsenceToEntitlementType.TryGetValue(a.AbsenceType, out var et) && et == entitlementType)
                        .Select(a => a.Date)
                        .Min();

                    // ── S30 TASK-3008 two-step pattern (ADR-021 D2 + ADR-016 D5b "fifth pattern") ──
                    // ── S81 / TASK-8102 (R1/R2) — dated-anchor correctness cutover ──
                    // The two-step read is now anchored on the OPERATION DATE (firstAbsenceDate), not
                    // today's live key, on BOTH dimensions (OK version + agreement code):
                    //   Step 1 (R2): read the LIVE (open) row to derive ResetMonth at the OPERATION-DATE
                    //     key — OkVersionResolver.ResolveVersion(firstAbsenceDate) + the agreement dated
                    //     at firstAbsenceDate (NOT user.OkVersion + the month-start agreement). reset_month
                    //     is frozen WITHIN a natural key by the TASK-3007 admin-scope 422 guard (ADR-021
                    //     D5), so reading the live-open row of the operation-date key is safe — and for the
                    //     unconstrained IMMEDIATE calendar types, whose reset_month CAN diverge across keys
                    //     after an OK/agreement change (D5 freezes only EDITS, not new-key CREATEs), the
                    //     operation-date key is the correct-by-construction source of the consumed
                    //     entitlement year (SPRINT-81 §Design decision — re-derivation, not the seed
                    //     type-uniformity assumption).
                    //   Step 2 (R1): the quota config is read at the entitlement-year-START key — the
                    //     year-start-dated OK version AND the year-start-dated agreement — via the shared
                    //     DatedEntitlementConfigResolver (ADR-023 D3 graceful chain: dated row →
                    //     operation-date-agreement liveConfig → historical-agreement live row → liveConfig).
                    // This unifies the (formerly misaligned: live OK + month-start agreement) Skema quota
                    // validation onto the year-overview's year-start-aligned convention. PRE-transaction
                    // check (runs before BeginTransactionAsync below), so the non-tx read overload suffices.
                    var opDateOkVersion = OkVersionResolver.ResolveVersion(firstAbsenceDate);
                    var opDateAgreement = await userAgreementCodeRepo.GetByUserIdAtAsync(
                        employeeId, firstAbsenceDate, ct) ?? user.AgreementCode;

                    var liveConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                        entitlementType, opDateAgreement, opDateOkVersion, ct);
                    if (liveConfig is null)
                    {
                        // Recorded: the single loop's `continue` (no pre-check, no entitlementData).
                        preCheckPlans.Add(QuotaPreCheckPlan.WithoutLiveConfig(entitlementType));
                        continue;
                    }

                    // S80 / TASK-8001 (R2/R10) — the entitlement (accrual) year + its accrual-window
                    // start come from the SHARED EntitlementPeriodResolver. VACATION + every other
                    // type are BEHAVIOR-IDENTICAL to the old ResolveEntitlementYear/Start (the
                    // "Month ≥ reset_month" ferieår keying). SPECIAL_HOLIDAY uses the two-calendar-year
                    // taking-window mapping (1 May Y+1 .. 30 Apr Y+2): a May–Dec booking keys to accrual
                    // year T−1, a Jan–Apr booking to T−2 — and accrual is anchored to 1 Jan (R1), so the
                    // EarnedToDate ferieaarStart below is the resolver's AccrualStart, NOT reset_month/1.
                    var entPeriod = EntitlementPeriodResolver.Resolve(
                        entitlementType, liveConfig.ResetMonth, firstAbsenceDate);
                    var entitlementYear = entPeriod.EntitlementYear;
                    var entitlementYearStart = entPeriod.AccrualStart;

                    // Step 2 (R1): dated read at the entitlement-year-START key (year-start OK +
                    // year-start agreement), via the shared resolver. This is the config row that was
                    // IN EFFECT on the day the current entitlement year started — what quota validation
                    // must use (annual_quota / carryover_max as they stood at year-start). The resolver
                    // encapsulates the ADR-023 D3 graceful fallback (never 500): a dated miss for a
                    // year-start OK that came into existence mid-year, or a year-start agreement with no
                    // dated row, falls through to the live config rather than silently skipping the check.
                    // todayAgreementCode operand = opDateAgreement (the agreement liveConfig was read
                    // under): so when the year-start agreement equals it, the resolver returns this exact
                    // liveConfig; when it differs, it values against the historical agreement's own row.
                    var datedConfigResolver = datedConfigResolverFactory.Create(
                        employeeId, opDateOkVersion, user.AgreementCode, opDateAgreement);
                    var config = await datedConfigResolver.ResolveDatedConfigAsync(
                        entitlementType, entitlementYearStart,
                        OkVersionResolver.ResolveVersion(entitlementYearStart), liveConfig, ct);

                    // (The type's balance is read in phase 2, on the snapshot.)

                    // ── S60 / TASK-6005 — dated part-time fraction at the per-type batch anchor ──
                    // Source the part_time_fraction from the dated employment profile AS-OF
                    // firstAbsenceDate (the per-type batch anchor, the MIN absence date for this
                    // entitlement type), dropping the prior hard-coded 1.0m.
                    //
                    // S60 Step-7a fix (W2): the fraction is only load-bearing for MONTHLY_ACCRUAL
                    // types (it scales EarnedToDate) or pro-rated types (ProRateByPartTime). For
                    // IMMEDIATE, non-pro-rated types (e.g. CARE_DAY) the fraction is irrelevant to
                    // the cap, so a missing profile must NOT newly block registration (it didn't
                    // pre-S60) — fall back to 1.0 gracefully. Only hard-fail (422) when the fraction
                    // actually matters, so part-time accrual is never silently mis-computed against a
                    // fabricated full-time fraction (the Balance summary seam stays graceful in all
                    // cases per ADR-023 D3; this is the per-seam asymmetry).
                    var isMonthlyAccrual = string.Equals(
                        config.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal);
                    var fractionMatters = isMonthlyAccrual || config.ProRateByPartTime;
                    // Only resolve the dated profile when the fraction is load-bearing. For IMMEDIATE
                    // non-pro-rated types (e.g. CARE_DAY) the fraction is irrelevant, so SKIP the
                    // resolver entirely (Step-7a cycle-2 fix) — it can THROW
                    // EmployeeProfileNotFoundException (employee_profiles row but no dated
                    // user_agreement_codes row), which would otherwise 500 those absences where
                    // pre-S60 the hard-coded 1.0 let them through. When the fraction DOES matter, a
                    // null OR a throw ⇒ a clean 422 (cannot validate the cap without the fraction).
                    EmploymentProfile? datedProfile = null;
                    if (fractionMatters)
                    {
                        try
                        {
                            datedProfile = await profileResolver.GetByEmployeeIdAtAsync(
                                employeeId, firstAbsenceDate, ct);
                        }
                        catch (EmployeeProfileNotFoundException)
                        {
                            datedProfile = null;
                        }
                        if (datedProfile is null)
                        {
                            // Recorded, not returned: phase 3 answers it at this type's turn, after any
                            // earlier type's rule-engine refusal, exactly as the single loop did. No
                            // later type was ever reached, so phase 1 stops here.
                            preCheckPlans.Add(QuotaPreCheckPlan.Refused(entitlementType, entitlementYear,
                                Results.Json(new
                                {
                                    error = "employment_profile_missing",
                                    absenceType = entitlementType,
                                    date = firstAbsenceDate,
                                    message = $"Kan ikke validere Ferie/særlige feriedage for {firstAbsenceDate:dd-MM-yyyy}: ansættelsesprofil mangler."
                                }, statusCode: 422)));
                            break;
                        }
                    }
                    var partTimeFraction = datedProfile?.PartTimeFraction ?? 1.0m;

                    var effectiveQuota = config.ProRateByPartTime
                        ? config.AnnualQuota * partTimeFraction
                        : config.AnnualQuota;

                    // ── S60 / TASK-6005 — per-type business bookableLimit (carryover-INCLUSIVE) ──
                    // For MONTHLY_ACCRUAL types the rejection cap is dynamic and per-type (ADR-030):
                    //   VACATION        = earned + stillAccruableInFerieår + carryoverIn (forskud OK).
                    //                     earned + stillAccruable == EarnedToDate evaluated at the
                    //                     LAST day of the ferieår (clamps to the full accruable amount
                    //                     for this employee — full annual×fraction for a whole-ferieår
                    //                     hire; pro-rated for a mid-ferieår hire who can't borrow into
                    //                     the next ferieår). Manager approval of the period IS the §7
                    //                     forskudsferie agreement (ADR-030 / refinement New-Q-A).
                    //   SPECIAL_HOLIDAY = earned-to-date AS-OF firstAbsenceDate + carryoverIn — NO
                    //                     forskud (ferieaftale §13 stk.4). Booking beyond earned is
                    //                     rejected (422) at both the pre-tx check and the atomic guard.
                    //   IMMEDIATE types = effectiveQuota + carryoverIn (unchanged).
                    // asOf = firstAbsenceDate (the existing per-type batch anchor). ferieaarStart =
                    // entitlementYearStart (derived from reset_month). employmentStart = the HR-managed
                    // User.EmploymentStartDate (null ⇒ full-ferieår, never fail-closed — ADR-030).
                    // The repo guardCap is carryover-EXCLUDED (CheckAndAdjustAsync re-adds carryover
                    // once); the rule-engine BookableLimit below is the carryover-INCLUSIVE business cap.
                    // (isMonthlyAccrual computed above with the profile-missing guard.)
                    // S145 / TASK-14507 (cycle 2) — the accruable cap is computed here; the carryover it
                    // is added to comes from the balance, so bookableLimit is formed in phase 3.
                    decimal accruableCap = 0m;
                    decimal guardCap;
                    if (isMonthlyAccrual)
                    {
                        // ── S63 / TASK-6302 — ADR-031 flat (fraction-independent) accrual cutover ──
                        // The accruable cap is a FLAT day-count — annualQuota × monthsElapsed / 12 —
                        // INDEPENDENT of the part-time fraction (Ferieloven §5 stk.1: a part-timer
                        // earns the same NUMBER of vacation days as a full-timer). ADR-031 supersedes
                        // ADR-030 D8's per-month piecewise fraction weighting, so the S62
                        // fraction-history fetch + its belt-and-suspenders empty-history 422 are gone.
                        // The 1.0m literal below is the DELIBERATE identity fraction (the day-count
                        // never scales by fraction). The surviving fail-closed guard is the ANCHOR
                        // profile-missing 422 above (driven by GetByEmployeeIdAtAsync under
                        // fractionMatters, which stays TRUE for these types via isMonthlyAccrual) —
                        // an accrual-window guard, not a fraction guard (ADR-031 D4; ADR-023 D3 Skema
                        // fail-closed polarity). The window/D6 logic is unchanged; only the fraction
                        // drops out.
                        if (string.Equals(entitlementType, "VACATION", StringComparison.Ordinal))
                        {
                            // earned + stillAccruable == accruable over the whole ferieår ==
                            // EarnedToDate at the ferieår's last day (forskud over the whole ferieår;
                            // manager approval IS the §7 forskudsferie agreement). With the identity
                            // 1.0 fraction this equals the FLAT annual quota for a whole-ferieår hire
                            // and the months-elapsed pro-rated amount for a mid-ferieår hire (D6).
                            var ferieaarEnd = entitlementYearStart.AddYears(1).AddDays(-1);
                            accruableCap = AccrualMath.EarnedToDate(
                                config.AnnualQuota, 1.0m, entitlementYearStart,
                                user.EmploymentStartDate, ferieaarEnd);
                        }
                        else
                        {
                            // SPECIAL_HOLIDAY (and any other MONTHLY_ACCRUAL type) — no forskud
                            // (ferieaftale §13 stk.4): capped at earned-to-date AS-OF the absence
                            // date (asOf stays firstAbsenceDate, NOT the ferieår end).
                            // S137 / ADR-040 D9 — user.EmploymentEndDate threaded for uniformity with
                            // the Balance reads; idempotent here post-Increment-1 (bookings are
                            // window-gated, so firstAbsenceDate <= end whenever this runs).
                            accruableCap = AccrualMath.EarnedToDate(
                                config.AnnualQuota, 1.0m, entitlementYearStart,
                                user.EmploymentStartDate, firstAbsenceDate, user.EmploymentEndDate);
                        }

                        guardCap = accruableCap;                       // carryover-EXCLUDED
                    }
                    else
                    {
                        // IMMEDIATE types: cap unchanged (annual effectiveQuota). No BookableLimit
                        // override — the rule keys off (effectiveQuota + carryover) as before.
                        guardCap = effectiveQuota;
                    }

                    preCheckPlans.Add(new QuotaPreCheckPlan
                    {
                        EntitlementType = entitlementType,
                        RequestedDays = totalRequestedDays,
                        FirstAbsenceDate = firstAbsenceDate,
                        EntitlementYear = entitlementYear,
                        EntitlementYearStart = entitlementYearStart,
                        Config = config,
                        PartTimeFraction = partTimeFraction,
                        EffectiveQuota = effectiveQuota,
                        IsMonthlyAccrual = isMonthlyAccrual,
                        AccruableCap = accruableCap,
                        GuardCap = guardCap,
                    });
                }

                // ── Phase 2: one short read-only snapshot ─────────────────────────────────────────────
                // S145 / TASK-14507 (R23; Step-5a cycles 1-2). The advisory read of what this save
                // replaces and EVERY balance read of the pre-check share this one REPEATABLE READ
                // snapshot, so `balance.Used − Σ replaced` is a subtraction over one real state of the
                // database. Read on separate connections, a concurrent save of the same cell landing
                // between the two reads would pair numbers that were never true at once, and since S145
                // that can make the pre-check STRICTER than the truth (a stale refusal), not only more
                // lenient. Explicitly READ ONLY (SET TRANSACTION READ ONLY as its first statement; it
                // takes no snapshot, so the first SELECT still does). It takes no advisory lock and no
                // row lock: only the ACCESS SHARE table lock that any read takes, which conflicts only
                // with ACCESS EXCLUSIVE (most DDL, TRUNCATE, VACUUM FULL, an explicit LOCK TABLE) and
                // never with a save. Only these reads run inside it, and it is
                // disposed at the end of this scope: no config read, no profile read and no HTTP call
                // happens while it is open. Skipped when no type was planned (nothing to read).
                var provisionalReplacedByEntitlementType = new Dictionary<string, decimal>(StringComparer.Ordinal);
                var preCheckBalances = new Dictionary<(string EntitlementType, int EntitlementYear), EntitlementBalance?>();
                if (preCheckPlans.Any(p => !p.HasNoLiveConfig))
                {
                    await using var preCheckConn = connectionFactory.Create();
                    await preCheckConn.OpenAsync(ct);
                    await using var preCheckTx = await preCheckConn.BeginTransactionAsync(
                        System.Data.IsolationLevel.RepeatableRead, ct);
                    await using (var readOnlyCmd = new NpgsqlCommand("SET TRANSACTION READ ONLY", preCheckConn, preCheckTx))
                        await readOnlyCmd.ExecuteNonQueryAsync(ct);

                    provisionalReplacedByEntitlementType = await ReadReplacedFeriedageByTypeAsync(preCheckConn, preCheckTx);
                    foreach (var plan in preCheckPlans)
                    {
                        if (plan.HasNoLiveConfig)
                            continue;
                        preCheckBalances[(plan.EntitlementType, plan.EntitlementYear)] =
                            await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                                preCheckConn, preCheckTx, employeeId, plan.EntitlementType, plan.EntitlementYear, ct);
                    }
                }

                // ── Phase 3: the decisions, in the single loop's order, no connection held ───────────
                foreach (var plan in preCheckPlans)
                {
                    if (plan.HasNoLiveConfig)
                        continue; // the single loop's `continue`: no pre-check, no entitlementData.
                    if (plan.Refusal is not null)
                        return plan.Refusal; // the single loop's profile-missing 422, at the same turn.

                    var entitlementType = plan.EntitlementType;
                    var requestedDays = plan.RequestedDays;
                    var config = plan.Config!;

                    // S145 / TASK-14507 (R23, W1 ruling) — the signed change this save makes to the
                    // type's quota: new feriedage minus the stored feriedage of the replaced cells.
                    var replacedDays = provisionalReplacedByEntitlementType.GetValueOrDefault(entitlementType);
                    var signedDelta = requestedDays - replacedDays;

                    var balance = preCheckBalances[(entitlementType, plan.EntitlementYear)];
                    var carryoverIn = balance?.CarryoverIn ?? 0m;
                    // carryover-INCLUSIVE business cap for MONTHLY_ACCRUAL types (see phase 1); IMMEDIATE
                    // types carry no BookableLimit override.
                    decimal? bookableLimit = plan.IsMonthlyAccrual ? plan.AccruableCap + carryoverIn : null;

                    // ── S145 / TASK-14507 (R23 as amended by W1; cycle-5 ruling) — the quota pre-check
                    // runs ONLY for a type whose signed delta is > 0 ─────────────────────────────────
                    // Why: an unchanged re-save (delta 0) or a reduction (delta < 0) cannot overdraw the
                    // quota, and must never be refused: not at an exhausted quota, and not when an
                    // ADR-032 D4 revaluation left `used` above the cap. Asking the rule anyway would
                    // refuse it, because the rule rejects whenever remainingAfter < 0, which is already
                    // true with `used` above the cap and nothing requested. Nothing else is lost by the
                    // skip: this call carries no age gate or per-episode limit (perEpisodeLimit is null;
                    // the SENIOR_DAY age gate is its own call above). The other validation of the
                    // pre-check (the dated config, the profile-missing 422) and the AUTHORITATIVE in-lock
                    // check still run for every type.
                    //
                    // For delta > 0 the mirror: `used` excludes the replaced cells
                    // (balance.Used − Σ replaced) and requestedDays is the full Σ new, so the rule sees
                    // exactly "does the CHANGE fit" (identical to used = balance.Used with
                    // requestedDays = delta).
                    if (signedDelta > 0m)
                    {
                        // Call Rule Engine via HTTP (PAT-005 compliance). Accrual inputs are carried so
                        // the rule applies the per-type cap; the warning-threshold + per-episode branches
                        // stay on the ANNUAL quota (no spurious early-ferieår warnings).
                        var validationRequest = new
                        {
                            annualQuota = config.AnnualQuota,
                            used = (balance?.Used ?? 0m) - replacedDays,
                            planned = balance?.Planned ?? 0m,
                            carryoverIn,
                            requestedDays,
                            partTimeFraction = plan.PartTimeFraction,
                            proRateByPartTime = config.ProRateByPartTime,
                            isPerEpisode = config.IsPerEpisode,
                            perEpisodeLimit = (decimal?)null,
                            accrualModel = config.AccrualModel,
                            ferieaarStart = (DateOnly?)plan.EntitlementYearStart,
                            employmentStart = user.EmploymentStartDate,
                            asOfDate = (DateOnly?)plan.FirstAbsenceDate,
                            bookableLimit
                        };

                        var response = await httpClient.PostAsJsonAsync(
                            "/api/rules/validate-entitlement", validationRequest, jsonOptions, ct);

                        if (!response.IsSuccessStatusCode)
                            return Results.Json(new { error = "Entitlement validation service unavailable" }, statusCode: 503);

                        var validationResult = await response.Content.ReadFromJsonAsync<EntitlementValidationResult>(jsonOptions, ct);
                        if (validationResult is null)
                            return Results.Json(new { error = "Invalid entitlement validation response" }, statusCode: 502);

                        if (!validationResult.Allowed)
                        {
                            return Results.Json(new
                            {
                                error = "Entitlement quota exceeded",
                                absenceType = entitlementType,
                                remaining = Math.Round(validationResult.RemainingAfter + requestedDays, 2),
                                requested = Math.Round(requestedDays, 2),
                                message = validationResult.Message
                            }, statusCode: 422);
                        }
                    }

                    // GuardCap is carryover-EXCLUDED (CheckAndAdjustAsync re-adds carryover once);
                    // SeedQuota = annual effectiveQuota (seeds total_quota on a first-INSERT only).
                    entitlementData[entitlementType] = (requestedDays, plan.EntitlementYear, plan.GuardCap, plan.EffectiveQuota);
                }
            }

            var streamId = $"employee-{employeeId}";
            var savedCount = 0;

            // ── S27 TASK-2706 atomic outer transaction (ADR-018 D3 + D12) ──
            // All TimeEntryRegistered + AbsenceRegistered + EntitlementBalanceAdjusted
            // outbox enqueues, the matching time_entries_projection +
            // absences_projection INSERTs, and the entitlement_balances UPDATE
            // commit together — or roll back together (e.g. on a concurrent-
            // modification quota breach surfaced inside the loop, which throws
            // SkemaQuotaBreachException and triggers tx.RollbackAsync).
            //
            // Per-event ordering invariant: outbox enqueue FIRST (returning the
            // freshly-allocated outbox_id BIGSERIAL), projection INSERT SECOND
            // (consuming outbox_id). This keeps the projection's per-employee
            // monotonic ordering aligned with the global outbox sequence so
            // replay-from-events backfill (TASK-2705) joins cleanly.
            //
            // BEHAVIOR CHANGE vs S26 silent-skip (deferred from S26 Step 7a
            // cycle 2; see SPRINT-27.md TASK-2706 description): pre-S27 a
            // post-validation quota race silently `continue`d the failing
            // entitlement type, returning 200 OK with events committed but
            // no matching balance adjustment. Post-S27 the same race throws
            // SkemaQuotaBreachException → outer tx.RollbackAsync → 422 with
            // body shape matching the pre-validation 422 path so the frontend
            // useSkema.ts retry contract is unchanged. Bundle-rollback: ALL
            // prior TimeEntryRegistered events from the same handler call
            // are rolled back too (whole save is atomic), even quota-unrelated
            // time entries the user typed alongside the breaching absence.
            //
            // S66 / TASK-6603 — ADR-032 D2 TWO-PHASE consumption contract extends this TOCTOU
            // story: the provisional per-row feriedage + rule-engine advisory validation above run
            // PRE-LOCK (the HTTP call must never sit inside the lock). Inside the tx we FIRST take
            // the per-employee advisory lock (D4), THEN re-derive the AUTHORITATIVE per-row
            // feriedage from the dated profile/norm read NOW (so a racing profile-PUT that
            // committed before our lock is visible). The authoritative values stamp
            // AbsenceRegistered.Feriedage AND drive the CheckAndAdjustAsync delta (one valuation,
            // reused — Σ event Feriedage == guard delta). A stale PROVISIONAL advisory result is
            // benign for the WRITE, because the in-lock guard re-enforces the cap against the fresh
            // values. S145 / TASK-14507: the pre-check's phase-2 snapshot is closed before its
            // rule-engine calls (phase 3) and before this transaction opens, and since a re-save can
            // now REDUCE `used`, a reduction can land between that snapshot and our lock. So a stale
            // advisory result can also be a refusal the fresh state would not make: a spurious 422
            // the client may retry, never a wrong write.
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // ── S127 / TASK-12704 — PIN ReadCommitted EXPLICITLY (PAT-015) ────────────────────
                // Behaviour-neutral (this is what the default overload already resolves to), but the
                // in-lock re-reads below now DEPEND on it and must not depend on a default. This tx's
                // first statement is the blocking `SELECT pg_advisory_xact_lock(...)`; under
                // REPEATABLE READ the snapshot would be taken BEFORE the lock is granted, so the
                // winner's commit — the exact thing we blocked for — would be invisible, and BOTH
                // in-lock re-reads (the S127 approval-status re-read and the S66/D2 authoritative
                // feriedage re-derive) would silently read pre-lock state. ReadCommitted gives each
                // post-lock statement a fresh snapshot. Corollary: never pass an
                // ApprovalAuthorityContext on this transaction (it throws unless RepeatableRead).
                await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
                try
                {
                    // ── S66 / TASK-6603 — ADR-032 D2/D4 two-phase consumption cutover ──
                    // PHASE 2 (in-lock authoritative). FIRST acquire the per-employee advisory
                    // lock — before ANY read/write in this tx, so it precedes any FOR UPDATE and is
                    // held to commit (D4). This serializes against a racing profile-PUT revaluation
                    // (TASK-6604, which reuses this same helper).
                    await StatsTid.Backend.Api.Services.EmployeeConsumptionLock.AcquireAsync(
                        conn, tx, employeeId, ct);

                    // ── S136 Step-5a (Codex BLOCKER 1 — the SEC-046 race) — THE AUTHORITATIVE
                    // SUBJECT-STATE RE-CHECK, IN-LOCK ─────────────────────────────────────────────
                    // The D3 write floor above was decided from the UNLOCKED pre-transaction subject
                    // read (the advisory fast path). The deactivation writers — the employment-date
                    // PUTs' R1 lifecycle and the Step-A settlement flip — commit under this SAME
                    // EmployeeConsumptionLock, so a deactivation can land while this save waits on
                    // the acquire above; without this re-read a still-live Employee token would
                    // write for a just-deactivated subject. Re-read is_active on THIS (conn, tx) —
                    // under the lock, seeing the winner's commit via the ReadCommitted pin — and
                    // re-enforce the SAME D3 floor, same 403 shape as the pre-check. Only IsActive
                    // is consulted here: the pre-tx `user` snapshot keeps feeding the agreement /
                    // norm inputs exactly as before (this fix is access-control only).
                    var lockedSubject = await userRepo.GetByIdIncludingTerminatedAsync(
                        conn, tx, employeeId, ct);
                    if (lockedSubject is null)
                    {
                        // The row vanished between the pre-check and the lock (no production path
                        // hard-deletes users; defensive symmetry with the pre-check's 404).
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "Employee not found" });
                    }
                    if (!lockedSubject.IsActive &&
                        (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
                    {
                        // No write has been issued yet; rollback explicitly and refuse with the
                        // pre-check's exact 403 shape (one contract, whichever check fires).
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "Access denied",
                            reason = "Writes for a deactivated employee require LocalHR or above"
                        }, statusCode: 403);
                    }

                    // ── S136 / TASK-13603 — THE EMPLOYMENT-WINDOW GATE (ADR-040 D1–D3), IN-LOCK ────
                    // Every date this save would write — across ALL THREE arrays: Entries, Absences,
                    // AND WorkTime — must lie inside the subject's employment window. When S136 added
                    // it, this was the FIRST date gate Entries and Absences had (only WorkTime was
                    // month-bounded then, by the S56 pre-tx check above). Since S145 Step 7a (B-1,
                    // R-128) all three arrays are month-bounded by the pre-tx checks above, so every
                    // date reaching this gate lies in the requested month. Refusal is date-free via
                    // the shared EmploymentWindowGate (ONE predicate + ONE 422 construction site with
                    // POST /api/time-entries — see that class for the do-not-add-date-fields rule).
                    //
                    // Placement is the LOCK REGIME (S136 refinement, Codex-B1): the employment-date
                    // PUTs acquire this SAME EmployeeConsumptionLock as their first in-tx statement,
                    // so evaluating the window HERE — after our acquire, on the (conn, tx) resolver
                    // overload, under this tx's ReadCommitted pin — is the authoritative check: a
                    // concurrent window-edit/registration pair cannot interleave into out-of-window
                    // data (the same three-ingredient PAT-015 story as the approval re-read below).
                    //
                    // IN-TX ORDER RULE (fixed total order, shared verbatim with the time-entry POST
                    // so two writers can never deadlock): EmployeeConsumptionLock acquisition FIRST,
                    // then the D3 subject-state re-check above, then this window gate, then the
                    // approval-period status check. WITHIN THIS TRANSACTION the window outranks the
                    // approval status deliberately — non-employment is the strongest fact about a
                    // date (the ADR-040 D5 tie-break spirit).
                    //
                    // S136 Step-5a truth-up (Codex WARNING): that precedence holds only INSIDE the
                    // tx. This endpoint (unlike the time-entry POST) also runs a PRE-TX month-lock
                    // fast path (the advisory approval read further up), so an out-of-window date
                    // in a locked month is answered 409 THERE, before this gate ever runs — a
                    // CHOSEN divergence, not an accident: both paths refuse fail-closed, and the
                    // fast path saves opening a transaction for the common locked-month case. Do
                    // not "fix" the fast path to re-establish window-first precedence end-to-end.
                    //
                    // Evaluated PER DISTINCT DATE through the resolver rather than min/max-collapsed:
                    // interval reasoning ("min and max employed ⇒ all between employed") is only
                    // valid for the current single-spell storage, and ADR-040 D1's whole point is
                    // that consumers must not bake that assumption in — when the spells increment
                    // ships (re-hire), gaps between spells make it wrong. The resolver call is a
                    // single-row indexed read on this tx's connection; a save carries at most 31
                    // distinct dates (all three arrays are month-bounded, see above).
                    var registrationDates = new SortedSet<DateOnly>();
                    foreach (var entry in request.Entries ?? Array.Empty<SkemaEntry>())
                        registrationDates.Add(entry.Date);
                    foreach (var absence in request.Absences ?? Array.Empty<SkemaAbsence>())
                        registrationDates.Add(absence.Date);
                    foreach (var day in request.WorkTime ?? Array.Empty<SkemaWorkTimeDay>())
                        registrationDates.Add(day.Date);
                    foreach (var registrationDate in registrationDates)
                    {
                        var windowStatus = await employmentWindowResolver.GetStatusAsync(
                            conn, tx, employeeId, registrationDate, ct);
                        if (StatsTid.Backend.Api.Services.EmploymentWindowGate.IsOutsideEmploymentWindow(windowStatus))
                        {
                            // No write has been issued yet (this precedes every enqueue/INSERT in
                            // the tx); rollback explicitly and return the shared date-free 422.
                            await tx.RollbackAsync(ct);
                            return StatsTid.Backend.Api.Services.EmploymentWindowGate.OutsideEmploymentPeriod();
                        }
                    }

                    // ── S127 / TASK-12704 — THE AUTHORITATIVE APPROVAL-STATUS RE-READ ─────────────
                    // After the lock, before ANY write. Holding the lock is necessary but NOT
                    // sufficient: the pre-transaction check above ran on its own connection, outside
                    // this transaction and outside the lock. A send command — which takes THIS SAME
                    // lock — can have validated and committed EMPLOYEE_APPROVED while we blocked on
                    // the acquire; without re-reading we would commit entries into a month that is
                    // already manager-visible, on a read that is now stale. That is the write half of
                    // refinement §1 defect 4.
                    //
                    // The (conn, tx) overload is REQUIRED (TASK-12702): the connection-owning overload
                    // opens a private connection, which sits outside this transaction and therefore
                    // outside the lock's protection — it would re-introduce the same staleness.
                    //
                    // Exact month, natural key ([monthStart, monthEnd]) — identical arguments to the
                    // pre-transaction read, so the two askings resolve the same row. It sees the
                    // winner's commit only because this transaction pins ReadCommitted above
                    // (PAT-015) — under RepeatableRead this re-read would be a no-op.
                    //
                    // Returns the pre-transaction path's 409 verbatim via the shared construction site
                    // — the frontend sees one contract regardless of which check fired. Returning here
                    // rather than throwing leaves the transaction uncommitted; `await using var tx`
                    // disposes on the way out and rolls back, before any write has been issued.
                    var inLockPeriod = await approvalRepo.GetByEmployeeAndPeriodAsync(
                        conn, tx, employeeId, monthStart, monthEnd, ct);
                    if (ApprovalPeriodSaveLock.IsPeriodLockedForSave(inLockPeriod))
                    {
                        await tx.RollbackAsync(ct);
                        return ApprovalPeriodSaveLock.PeriodLockedForSaveConflict(inLockPeriod!);
                    }

                    // THEN re-derive the AUTHORITATIVE per-row feriedage INSIDE the lock. This
                    // re-read is the whole point of D2's two phases: a profile PUT that committed
                    // before we acquired the lock MUST now be visible. The profile resolver opens
                    // its OWN connection (not enrolled in this tx) — that is fine: the advisory
                    // lock serializes the WRITERS, so the resolver observes whatever committed
                    // before the lock, never a half-written racing PUT (D4 separate-connection
                    // rationale). These authoritative values feed BOTH AbsenceRegistered.Feriedage
                    // and the guard delta (the single-valuation identity: Σ event Feriedage ==
                    // guard delta, computed once here and reused).
                    // S73 / TASK-7301 Step-7a B1 — resolve the full-day-only rows UNDER the lock
                    // (in-tx dated config read) so the authoritative consumption divisor for those
                    // rows is the SAME rounded basis the in-lock full-day guard requires: an exact
                    // full day records EXACTLY 1.0 feriedage (Σ over two full days = 2.0, never
                    // 2.0008 that would spuriously breach the 2-day CARE_DAY quota).
                    var authoritativeFullDayOnly = request.Absences is { Length: > 0 }
                        ? await ResolveFullDayOnlyIndicesAsync(conn, tx)
                        : (IReadOnlySet<int>)new HashSet<int>();
                    var authoritativeConsumption = request.Absences is { Length: > 0 }
                        ? await consumptionCalculator.ComputeAsync(
                            employeeId,
                            request.Absences.Select(a => (a.Date, a.Hours)).ToList(),
                            user.PrimaryOrgId, ct, authoritativeFullDayOnly)
                        : (IReadOnlyList<StatsTid.Backend.Api.Services.ConsumptionCalculator.Consumption>)
                            Array.Empty<StatsTid.Backend.Api.Services.ConsumptionCalculator.Consumption>();

                    // ── S66 / TASK-6603 FIX-FORWARD — in-lock authoritative re-checks (ADR-032 D2/D3) ──
                    // The pre-tx guards (the D3 norm cap at :552 and the anchor profile-missing 422 at
                    // :829) ran BEFORE the advisory lock against a possibly-stale norm. A profile PUT
                    // (TASK-6604) that committed between those checks and THIS recompute lowered or
                    // removed fullDayHours — so the authoritative values just re-derived inside the lock
                    // are the source of truth and MUST be re-validated here (the racing-save loser gets a
                    // clean 422, never a 500 or an unvalued persist). On any violation we throw
                    // SkemaConsumptionValidationException → outer tx.RollbackAsync → the SAME 422 body the
                    // matching pre-tx guard returns. The pre-tx guards stay the cheap common fast path
                    // (unchanged semantics); this is the enforcement point.
                    if (request.Absences is { Length: > 0 })
                    {
                        // B1 — fail-closed per row: an entitlement-consuming row with a null
                        // authoritative fullDayHours (no dated profile covers the date under the in-lock
                        // read) must abort the save with the employment_profile_missing 422 family — never
                        // persist a null-valued (Feriedage=null) consuming row that contributes a ZERO
                        // guard delta (the old `?? 0m` fail-open hole). Non-entitlement rows are unaffected
                        // (they legitimately carry null Feriedage by design).
                        for (var i = 0; i < request.Absences.Length; i++)
                        {
                            var absence = request.Absences[i];
                            if (GetEntitlementType(absence.AbsenceType) is null)
                                continue; // non-entitlement row — null fullDayHours is fine.
                            if (authoritativeConsumption[i].FullDayHours is null)
                                throw new SkemaConsumptionValidationException(new
                                {
                                    error = "employment_profile_missing",
                                    absenceType = GetEntitlementType(absence.AbsenceType),
                                    date = absence.Date,
                                    message = $"Kan ikke validere Ferie/særlige feriedage for {absence.Date:dd-MM-yyyy}: ansættelsesprofil mangler."
                                });
                        }

                        // B2 — re-check the D3 all-types daily-hours cap against the AUTHORITATIVE
                        // fullDayHours (a racing PUT may have lowered the norm since the pre-tx check, which
                        // would persist a per-row dayEquivalent > 1.0). Per date, mirroring the pre-tx D3
                        // guard's branches: positive-norm ⇒ Σ all-types hours ≤ fullDayHours; zero-norm
                        // (weekend) ⇒ entitlement rows already rejected by B1's sibling pre-tx weekend guard
                        // path, but a racing norm→0 is re-caught here (entitlement rows ⇒ 422; non-entitlement
                        // rows keep the legacy flat-7.4 cap); null fullDayHours for entitlement rows is owned
                        // by B1 above, and a null on a purely non-entitlement date defers to the pre-tx
                        // anchor family (no in-lock cap to apply). fullDayHours is constant per date (cached
                        // in ComputeAsync), so the first row's value represents the date.
                        var authByDate = request.Absences
                            .Select((a, i) => (a.Date, a.AbsenceType, a.Hours, authoritativeConsumption[i].FullDayHours))
                            .GroupBy(r => r.Date);
                        foreach (var dayGroup in authByDate)
                        {
                            var date = dayGroup.Key;
                            var fullDayHours = dayGroup.First().FullDayHours;
                            if (fullDayHours is null)
                                continue; // entitlement rows handled by B1; non-entitlement defers to anchor family.

                            if (fullDayHours.Value > 0m)
                            {
                                // S73 / TASK-7301 (H1) — in-lock mirror of the pre-tx D3 cap:
                                // compare against the SAME RoundBasis-rounded basis (ADR-032 D1)
                                // the full-day rule uses, so the exact rounded full day is bookable.
                                var cappedHours = RoundBasis(fullDayHours)!.Value;
                                var totalHours = dayGroup.Sum(r => r.Hours);
                                if (totalHours > cappedHours)
                                    throw new SkemaConsumptionValidationException(new
                                    {
                                        error = "Total absence hours exceed norm day",
                                        date,
                                        totalHours,
                                        maxHours = cappedHours,
                                    });
                            }
                            else
                            {
                                // Zero-norm day (racing norm→0): entitlement rows cannot be booked.
                                var offendingRows = dayGroup
                                    .Where(r => GetEntitlementType(r.AbsenceType) is not null)
                                    .ToList();
                                if (offendingRows.Count > 0)
                                    throw new SkemaConsumptionValidationException(new
                                    {
                                        error = "Entitlement absence on a non-working day",
                                        date,
                                        absenceTypes = offendingRows.Select(r => r.AbsenceType).ToArray(),
                                        message = $"Ferie/særlige feriedage kan ikke registreres på en arbejdsfri dag ({date:dd-MM-yyyy}).",
                                    });

                                var nonEntitlementHours = dayGroup.Sum(r => r.Hours);
                                if (nonEntitlementHours > StandardDayHours)
                                    throw new SkemaConsumptionValidationException(new
                                    {
                                        error = "Total absence hours exceed norm day",
                                        date,
                                        totalHours = nonEntitlementHours,
                                        maxHours = StandardDayHours,
                                    });
                            }
                        }

                        // B3 (S73 / TASK-7301) — the R2 FULL-DAY-ONLY re-check. The IN-LOCK
                        // site is REQUIRED (the S70 B1 lesson: ANY enumeration-time predicate
                        // must be re-evaluated under the lock). Mirrors the pre-tx guard
                        // (same skip set, same 422 body) reading the AUTHORITATIVE in-lock
                        // fullDayHours re-derived above, so a racing profile-PUT that changed
                        // the day's basis between the pre-tx mirror and this lock is re-enforced
                        // here (the racing-save loser gets the same clean 422, never an
                        // exact-by-stale-basis persist). Null basis is owned by B1 (fail-closed);
                        // zero-norm by B2's weekend branch.
                        //
                        // S73 Step-7a cycle-2 B1 (TOCTOU) — the full-day predicate is read EXACTLY
                        // ONCE in-lock: `authoritativeFullDayOnly` was resolved above (line ~1350)
                        // by ResolveFullDayOnlyIndicesAsync(conn, tx) AND fed to the in-lock
                        // ComputeAsync that produced `authoritativeConsumption`. The guard REUSES
                        // that same index set rather than querying entitlement_configs a second
                        // time. entitlement_configs is GLOBAL (not under the per-employee D4
                        // advisory lock); under READ COMMITTED a concurrent GlobalAdmin config
                        // creation between two independent reads could otherwise make the divisor
                        // (raw) and the guard (fullDayOnly=true) disagree — re-introducing the
                        // 1.0004 divergence in that race. ONE snapshot ⇒ divisor and guard agree
                        // by construction: a row is full-day-divided IFF it is full-day-guarded.
                        {
                            for (var i = 0; i < request.Absences.Length; i++)
                            {
                                var absence = request.Absences[i];
                                if (!authoritativeFullDayOnly.Contains(i))
                                    continue; // non-entitlement OR not full-day-only — same snapshot as the divisor.

                                var basis = authoritativeConsumption[i].FullDayHours;
                                if (basis is null || basis.Value <= 0m)
                                    continue; // null → B1 (fail-closed); zero-norm → B2's weekend branch.

                                var requiredHours = RoundBasis(basis)!.Value;
                                if (absence.Hours != requiredHours)
                                    throw new SkemaConsumptionValidationException(new
                                    {
                                        error = "absence_full_day_only",
                                        absenceType = absence.AbsenceType,
                                        date = absence.Date,
                                        requiredHours,
                                        message = $"{AbsenceTypeLabels.LabelFor(absence.AbsenceType)} kan kun registreres som hel dag ({requiredHours} timer) den {absence.Date:dd-MM-yyyy}.",
                                    });
                            }
                        }
                    }

                    // Save time entries (outbox enqueue + projection INSERT inside outer tx).
                    //
                    // S145 / TASK-14507 (A0) — a Skema save REPLACES the cell. Each posted entry still
                    // appends its own TimeEntryRegistered (events stay append-only), now stamped
                    // Source = SKEMA; the projection insert then retires the cell's current Skema row
                    // (mark, then insert) so exactly one current row remains per (date, task). Cells
                    // are processed in posted order, so a cell posted twice in one save ends at the
                    // later value.
                    if (request.Entries is not null)
                    {
                        foreach (var entry in request.Entries)
                        {
                            var @event = new TimeEntryRegistered
                            {
                                EmployeeId = employeeId,
                                Date = entry.Date,
                                Hours = entry.Hours,
                                TaskId = entry.ProjectCode,
                                ActivityType = "NORMAL",
                                AgreementCode = agreementCode,
                                // S66 / TASK-6603 ASYMMETRY (DELIBERATE): TimeEntryRegistered.OkVersion
                                // stays live user.OkVersion — entry-date OK stamping is out of ADR-032
                                // D2 scope (D2 covers absence consumption only). Only
                                // AbsenceRegistered.OkVersion was moved to OkVersionResolver(entry-date).
                                OkVersion = user.OkVersion,
                                Source = HoursSource.Skema,
                                ActorId = actor.ActorId,
                                ActorRole = actor.ActorRole,
                                CorrelationId = actor.CorrelationId
                            };
                            // Per-event ordering: enqueue FIRST (allocates outbox_id), projection SECOND.
                            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                            await timeEntryProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                            savedCount++;
                        }
                    }

                    // ── TASK-5603 self-recorded work time ("Arbejdstid") ──
                    // Dedicated branch (NOT the project-entry / absence classifier).
                    // Each day emits a latest-wins WorkTimeRegistered event: enqueue
                    // FIRST (allocates outbox_id), then UpsertAsync SECOND consuming that
                    // id — same per-event ordering invariant as the time-entry branch
                    // above, all inside the outer atomic tx (ADR-018 D3/D13). Re-saving a
                    // day emits a NEW event; latest-wins is resolved by the projection's
                    // outbox_id guard so a stale replay cannot clobber a newer row.
                    if (request.WorkTime is not null)
                    {
                        foreach (var day in request.WorkTime)
                        {
                            var intervals = (day.Intervals ?? Array.Empty<SkemaWorkInterval>())
                                .Select(i => new WorkInterval { Start = i.Start, End = i.End })
                                .ToList();

                            var @event = new WorkTimeRegistered
                            {
                                EmployeeId = employeeId,
                                Date = day.Date,
                                Intervals = intervals,
                                ManualHours = day.ManualHours,
                                ActorId = actor.ActorId,
                                ActorRole = actor.ActorRole,
                                CorrelationId = actor.CorrelationId
                            };
                            // Per-event ordering: enqueue FIRST (allocates outbox_id), projection SECOND.
                            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                            await workTimeProjectionRepo.UpsertAsync(conn, tx, @event, outboxId, ct);
                            savedCount++;
                        }
                    }

                    // Save absences and atomically adjust entitlement balances.
                    if (request.Absences is not null)
                    {
                        // S145 / TASK-14507 (R23) — the AUTHORITATIVE read of what this save replaces:
                        // under the consumption lock (taken above, so no concurrent save or revaluation
                        // of this employee can interleave) and BEFORE the inserts below, which mark these
                        // very rows superseded. It drives the signed balance delta.
                        var replacedByEntitlementType = await ReadReplacedFeriedageByTypeAsync(conn, tx);
                        var savedByEntitlementType = new Dictionary<string, decimal>(StringComparer.Ordinal);

                        for (var i = 0; i < request.Absences.Length; i++)
                        {
                            var absence = request.Absences[i];
                            var hasEntitlement = AbsenceToEntitlementType.TryGetValue(
                                absence.AbsenceType, out var entitlementType) && entitlementType is not null;

                            // ── S66 / TASK-6603 — ADR-032 D2 authoritative per-row feriedage ──
                            // Entitlement-consuming rows carry the AUTHORITATIVE in-lock feriedage;
                            // non-entitlement absence types (null entitlement mapping, e.g.
                            // SICK_DAY / PARENTAL_LEAVE / LEAVE_*) carry null Feriedage (they consume
                            // no entitlement). The same authoritative value drives the guard delta
                            // below (single-valuation identity).
                            decimal? rowFeriedage = hasEntitlement
                                ? authoritativeConsumption[i].Feriedage
                                : null;

                            var @event = new AbsenceRegistered
                            {
                                EmployeeId = employeeId,
                                Date = absence.Date,
                                AbsenceType = absence.AbsenceType,
                                Hours = absence.Hours,
                                AgreementCode = agreementCode,
                                // S66 / TASK-6603 — ADR-032 D2 + TASK-1801 precedent: OK version is
                                // stamped from the ABSENCE ENTRY DATE (when the absence occurs), not
                                // the live user.OkVersion, so replays/retroactive saves resolve the
                                // version that was in force on that day deterministically.
                                OkVersion = OkVersionResolver.ResolveVersion(absence.Date),
                                Feriedage = rowFeriedage,
                                // S145 / TASK-14507 (A0) — a Skema save replaces the cell: the insert
                                // below retires the cell's current Skema row before adding this one.
                                Source = HoursSource.Skema,
                                ActorId = actor.ActorId,
                                ActorRole = actor.ActorRole,
                                CorrelationId = actor.CorrelationId
                            };
                            // Per-event ordering: enqueue FIRST (allocates outbox_id), projection SECOND.
                            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                            await absenceProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                            savedCount++;

                            if (hasEntitlement)
                            {
                                if (!savedByEntitlementType.ContainsKey(entitlementType!))
                                    savedByEntitlementType[entitlementType!] = 0m;
                                // Σ the AUTHORITATIVE per-row feriedage (replaces sum-hours-then-÷7.4).
                                // No re-division: the per-row valuation already divided by the day's
                                // real fullDayHours (ADR-032 D2 — single valuation, no NO re-division).
                                savedByEntitlementType[entitlementType!] += rowFeriedage ?? 0m;
                            }
                        }

                        // Atomically check quota and adjust balances (eliminates TOCTOU race) via the
                        // (conn, tx) overload from S26 TASK-2603(a) so the UPDATE participates in the
                        // outer tx. A breach (concurrent modification raced past the pre-validation
                        // HTTP check) throws to roll back the entire save (S27 TASK-2706 (c)+(d)).
                        foreach (var (entitlementType, totalFeriedage) in savedByEntitlementType)
                        {
                            if (!entitlementData.TryGetValue(entitlementType, out var data))
                                continue;

                            // S66 / TASK-6603 — the new side of the delta IS the Σ authoritative per-row
                            // feriedage (already day-equivalents from the in-lock valuation). NO
                            // re-division by StandardDayHours (ADR-032 D2 single valuation): this is
                            // the same value Σ'd into the emitted AbsenceRegistered.Feriedage above.
                            //
                            // S145 / TASK-14507 (R23) — the delta is SIGNED: new minus the stored
                            // feriedage of the current rows this save just replaced (read above, under
                            // the lock). Re-saving a cell unchanged moves `used` by 0; a reduction moves
                            // it down. A first booking replaces nothing, so it is the full new value,
                            // exactly as before.
                            var replacedDays = replacedByEntitlementType.GetValueOrDefault(entitlementType);
                            var deltaDays = totalFeriedage - replacedDays;

                            bool success;
                            decimal newUsed;
                            if (deltaDays > 0m)
                            {
                                // An INCREASE (or a first booking) is held to the cap exactly as before.
                                // S60 / TASK-6005 — split guard: GuardCap (carryover-EXCLUDED per-type
                                // bookable cap; the WHERE-clause re-adds carryover once) + SeedQuota
                                // (= annual entitlement, seeds total_quota on a first-INSERT only).
                                (success, newUsed) = await entitlementBalanceRepo.CheckAndAdjustAsync(
                                    conn, tx, employeeId, entitlementType, data.EntitlementYear,
                                    deltaDays, data.GuardCap, data.SeedQuota, ct);
                            }
                            else
                            {
                                // Zero or a REDUCTION: ungated, in this transaction. A reduction gives
                                // days back and is never refused, not even when an ADR-032 D4
                                // revaluation already left `used` above the cap (the guard would refuse
                                // `used + delta <= cap` there although the save lowers `used`).
                                newUsed = await entitlementBalanceRepo.AdjustUsedAsync(
                                    conn, tx, employeeId, entitlementType, data.EntitlementYear,
                                    deltaDays, data.SeedQuota, ct);
                                success = true;
                            }

                            if (!success)
                            {
                                // S27 TASK-2706 (c) quota-race fix: throw to roll back the entire save
                                // atomically (bundle-rollback per ADR-018 D3). Replaces pre-S27 silent
                                // `continue` that returned 200 OK with inconsistent state. `newUsed`
                                // here is the current used balance (the (conn, tx) overload routes the
                                // failure-path read through the same snapshot — see EntitlementBalanceRepository
                                // CheckAndAdjustInternalAsync). Caught at the outer try/catch below
                                // and mapped to a 422 response. The 422 "remaining" uses the same
                                // carryover-EXCLUDED GuardCap the guard enforced (the catch block
                                // re-adds nothing — see note there) so the surfaced number matches
                                // the per-type bookable cap, not the annual seed.
                                throw new SkemaQuotaBreachException(
                                    entitlementType,
                                    requestedDays: deltaDays,
                                    currentUsed: newUsed,
                                    effectiveQuota: data.GuardCap);
                            }

                            // Read the carryover via the (conn, tx) overload so it observes the same
                            // snapshot under the outer tx (ADR-018 D3 + S26 TASK-2603(a) refinement W3).
                            var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                                conn, tx, employeeId, entitlementType, data.EntitlementYear, ct);
                            var carryoverIn = balance?.CarryoverIn ?? 0m;
                            // Remaining for the audit event mirrors the enforced cap: the per-type
                            // GuardCap (carryover-EXCLUDED) + carryover − newUsed. For IMMEDIATE types
                            // GuardCap == annual effectiveQuota, so this is unchanged from pre-S60.
                            var newRemaining = data.GuardCap + carryoverIn - newUsed;

                            var balanceEvent = new EntitlementBalanceAdjusted
                            {
                                EmployeeId = employeeId,
                                EntitlementType = entitlementType,
                                EntitlementYear = data.EntitlementYear,
                                DeltaDays = deltaDays,
                                NewUsed = newUsed,
                                NewRemaining = Math.Round(newRemaining, 2),
                                Reason = "Absence registered via Skema save",
                                ActorId = actor.ActorId,
                                ActorRole = actor.ActorRole,
                                CorrelationId = actor.CorrelationId
                            };
                            // No projection write for EntitlementBalanceAdjusted: entitlement_balances is
                            // a write-back projection mutated by CheckAndAdjustAsync above (not event-
                            // derived); the event captures the balance transition for audit/replay only.
                            await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, balanceEvent, ct);
                        }
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (SkemaQuotaBreachException ex)
            {
                // 422 body shape mirrors the pre-validation 422 above (error / absenceType /
                // remaining / requested / message) so frontend useSkema.ts retry semantics
                // are unchanged.
                return Results.Json(new
                {
                    error = "Entitlement quota exceeded",
                    absenceType = ex.EntitlementType,
                    remaining = Math.Round(ex.EffectiveQuota - ex.CurrentUsed, 2),
                    requested = Math.Round(ex.RequestedDays, 2),
                    message = ex.Message
                }, statusCode: 422);
            }
            catch (SkemaConsumptionValidationException ex)
            {
                // S66 / TASK-6603 FIX-FORWARD — in-lock D2/D3 re-check failure (B1 profile-missing /
                // B2 norm cap) after a racing profile-PUT. The throw site already shaped the body to
                // match its pre-tx sibling guard byte-for-byte; surface it unchanged so the racing-save
                // loser gets a clean validation 422 (never a 500) and the frontend contract holds.
                return Results.Json(ex.Body, statusCode: 422);
            }

            // S120 / TASK-12000 — named record (BYTE-IDENTICAL wire JSON). Response-shaping
            // ONLY — the in-tx event/outbox/projection emits above are byte-untouched (P3
            // fence); the 422/400/409 error fan stays untyped (S120 Explicit exclusions).
            return Results.Ok(new SkemaSaveResponse(Saved: savedCount));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<SkemaSaveResponse>(StatusCodes.Status200OK); // S120 / TASK-12000

        // ── PUT /api/skema/{employeeId}/row-preferences — R4 full-replacement write ──
        // S72 / TASK-7201. The manager-modal's save: the body is the FULL replacement set of
        // visible project + absence rows. SELF-ONLY authorization (Step-5a B3, below).
        // Write-side validation against the CURRENT catalog (R4: additions validated;
        // org-hidden/inactive/ineligible rows are rejected 422 listing offenders). ONE
        // transaction: container upsert (initialized_at on first write) + DELETE-and-INSERT
        // selections with DENSE sort_order 0..n-1 in submitted order. Plain UN-EVENTED rows
        // (R4 — view preference, not domain state: NO outbox event, NO audit projection; the
        // ProjectRepository selection precedent). Month-independent: ADR-012 locks
        // registrations, not preferences (R5) — no approval-period check here, by design.
        app.MapPut("/api/skema/{employeeId}/row-preferences", async (
            string employeeId,
            RowPreferencesRequest request,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ProjectRepository projectRepo,
            SkemaRowPreferenceRepository rowPreferenceRepo,
            AbsenceTypeVisibilityRepository visibilityRepo,
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // ── S72 / Step-5a B3 (owner-adjudicated): this PUT is SELF-ONLY ──
            // Row preferences are PERSONAL VIEW STATE (which rows the employee sees on
            // their own Skema), so least privilege says ONLY the employee themself writes
            // them — a DELIBERATE deviation from the save endpoint's authorization family:
            // the covering-scope branch is removed for this WRITE only (the month GET's
            // read auth is untouched; leaders still review the full record per R12, and
            // preference filtering never applies to the approval surface anyway). This also
            // closes the S70-R9f1-shaped mixed-role hole: a JWT carrying an elevated
            // primary role in a DISJOINT org plus an Employee-level scope covering the
            // victim would have passed the old covering-scope branch
            // (ValidateEmployeeAccessAsync admits ANY covering scope without requiring the
            // admitting scope to be elevated) and written another employee's preferences.
            // Ordinal comparison; a null ActorId never equals a route id (fail-closed).
            if (!string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal))
                return Results.Json(new { error = "Access denied", reason = "Row preferences are self-service only" }, statusCode: 403);

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // Catalog anchor = server today (TimeProvider seam, never the wall clock
            // directly — the PAT-008 convention). Preferences are month-independent view
            // state (un-evented, no replay), so a today-anchored catalog validation is the
            // honest "current catalog" check; the month GET keeps its month-end display
            // anchor and re-intersects on every read, so a type that later leaves the
            // catalog is filtered there regardless of what was accepted here.
            //
            // S142 / TASK-14203, owner ruling OQ-6 — the anchor is nevertheless the COPENHAGEN
            // calendar day. This site is the honest exception in the sweep: nothing durable is
            // persisted from it (the argument above still holds), so it moves for CONSISTENCY, not
            // for correctness. The reason that is worth a line of code: leaving one "today" on the
            // retired UTC calendar with no stated reason is exactly how a later reader concludes the
            // rule has exceptions and writes the next one the old way. It is also not entirely
            // inert — `today` selects the employee's agreement code below, and agreement changes are
            // dated, so on the boundary day the two calendars can pick different catalogs to
            // validate against.
            var today = CopenhagenBusinessDate.Today(timeProvider);
            var prefAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(employeeId, today, ct)
                ?? user.AgreementCode;

            var submittedProjects = request.Projects ?? Array.Empty<RowPreferenceProjectItem>();
            var submittedAbsenceTypes = request.AbsenceTypes ?? Array.Empty<RowPreferenceAbsenceItem>();

            // ── Write-side validation (R4) against the CURRENT catalogs ──
            // Projects: the org's ACTIVE catalog (GetByOrgAsync filters is_active).
            // Absence types: the SAME filtered chain the month GET serves (one shared
            // implementation — ComputeAbsenceCatalogAsync), anchored at today.
            var orgProjects = await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct);
            var projectsById = orgProjects.ToDictionary(p => p.ProjectId);
            var absenceCatalog = await ComputeAbsenceCatalogAsync(
                employeeId, prefAgreementCode, user, today,
                visibilityRepo, eligibilityRepo, entitlementConfigRepo, ct);
            var catalogTypeSet = new HashSet<string>(
                absenceCatalog.Select(c => c.Type), StringComparer.Ordinal);

            var invalidProjectIds = submittedProjects
                .Select(p => p.ProjectId)
                .Where(id => !projectsById.ContainsKey(id))
                .Distinct()
                .ToArray();
            var invalidAbsenceTypes = submittedAbsenceTypes
                .Select(a => a.AbsenceType)
                .Where(t => !catalogTypeSet.Contains(t))
                .Distinct()
                .ToArray();
            // Duplicates are rejected too (defensive: a duplicate would otherwise surface as
            // a PK violation 500 from the replacement INSERT — never an acceptable failure
            // mode for a validation problem).
            var duplicateProjectIds = submittedProjects
                .GroupBy(p => p.ProjectId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            var duplicateAbsenceTypes = submittedAbsenceTypes
                .GroupBy(a => a.AbsenceType, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (invalidProjectIds.Length > 0 || invalidAbsenceTypes.Length > 0
                || duplicateProjectIds.Length > 0 || duplicateAbsenceTypes.Length > 0)
            {
                return Results.Json(new
                {
                    error = "row_preferences_invalid",
                    invalidProjectIds,
                    invalidAbsenceTypes,
                    duplicateProjectIds,
                    duplicateAbsenceTypes,
                    message = "Mindst én række er ikke i det aktuelle katalog eller er angivet flere gange."
                }, statusCode: 422);
            }

            // Dense 0..n-1 in submitted order: stable sort by the submitted sortOrder
            // (ties broken by array position — a well-formed dense submission is preserved
            // verbatim), then re-number densely.
            var orderedProjects = submittedProjects
                .Select((p, i) => (p.ProjectId, p.SortOrder, Index: i))
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Index)
                .Select((p, dense) => (p.ProjectId, SortOrder: dense))
                .ToList();
            var orderedAbsenceTypes = submittedAbsenceTypes
                .Select((a, i) => (a.AbsenceType, a.SortOrder, Index: i))
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Index)
                .Select((a, dense) => (a.AbsenceType, SortOrder: dense))
                .ToList();

            // ONE tx: container upsert + DELETE-and-INSERT both selection tables. Un-evented.
            await rowPreferenceRepo.ReplaceAsync(employeeId, orderedProjects, orderedAbsenceTypes, ct);

            // The new effective rowPreferences (same shape as the month GET's field). Every
            // submitted row was validated against the catalog above, so visible == the
            // submitted set in dense order — no re-read needed.
            // S73 / TASK-7301 (R3): the response keeps the month GET's absence-type DTO shape —
            // fullDayOnly included (today-anchored, the same catalog computation) — so the FE's
            // post-save replacement state never drops the flag (the S72-B1 drift class).
            var prefFullDayOnlyByType = absenceCatalog.ToDictionary(
                c => c.Type, c => c.FullDayOnly, StringComparer.Ordinal);
            // S120 / TASK-12000 — the SHARED container record (the month GET's rowPreferences
            // shape; sibling rule — one shape, two surfaces). BYTE-IDENTICAL wire JSON.
            return Results.Ok(new SkemaRowPreferencesResponse(
                Configured: true,
                Projects: orderedProjects
                    .Select(p =>
                    {
                        var project = projectsById[p.ProjectId];
                        return new ProjectResponse(
                            ProjectId: project.ProjectId,
                            ProjectCode: project.ProjectCode,
                            ProjectName: project.ProjectName,
                            SortOrder: p.SortOrder);
                    })
                    .ToList(),
                AbsenceTypes: orderedAbsenceTypes
                    .Select(a => new SkemaRowPreferenceAbsenceRow(
                        Type: a.AbsenceType,
                        Label: AbsenceTypeLabels.LabelFor(a.AbsenceType),
                        FullDayOnly: prefFullDayOnlyByType.TryGetValue(a.AbsenceType, out var fdo) && fdo,
                        SortOrder: a.SortOrder))
                    .ToList()));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<SkemaRowPreferencesResponse>(StatusCodes.Status200OK); // S120 / TASK-12000

        return app;
    }

    // ── S58 TASK-5802 work-time validation helpers ──

    // Parse an "HH:MM" / "HH:MM:SS" wall-clock string to seconds-since-midnight.
    // Mirrors the frontend calcIntervalHours parsing (split on ':', seconds optional).
    // Returns false for malformed or out-of-range values so they are simply skipped
    // (consistent with the frontend ignoring intervals it cannot measure).
    private static bool TryParseTimeToSeconds(string? value, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        var s = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out s)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59 || s < 0 || s > 59) return false;
        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    // Danish "Xt Ym" formatting for validation messages (e.g. 25.5 -> "25t 30m").
    private static string FormatHoursMinutes(decimal hours)
    {
        var totalMinutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        return m == 0 ? $"{h}t" : $"{h}t {m}m";
    }

    // ── S72 / TASK-7201 — the addable absence-type CATALOG (one shared chain) ──

    /// <summary>
    /// One addable absence type: the type code + its Danish display label.
    /// S73 / TASK-7301 (R3): <paramref name="FullDayOnly"/> carries the dated
    /// <c>entitlement_configs.full_day_only</c> flag resolved at the catalog anchor — served on
    /// every absence-type DTO surface (catalog AND visible rows) so the FE renders the
    /// "hele dage" notes and snaps entries to the served consumption basis.
    /// </summary>
    private sealed record AbsenceCatalogEntry(string Type, string Label, bool FullDayOnly);

    /// <summary>
    /// The ADDABLE absence-type catalog — the EXISTING filter chain extracted VERBATIM from
    /// the month GET's inline block (S72 / TASK-7201; SPRINT-72 R4 "reuse the existing chain,
    /// do not reimplement"): agreement (<see cref="GetAbsenceTypesForAgreement"/>) → org
    /// <c>absence_type_visibility</c> → per-employee eligibility. SELECTION-INDEPENDENT: the
    /// user's row preferences never feed this — removed rows stay re-addable. Consumed by the
    /// month GET (anchor = MONTH-END, the established S59 display anchor) and the
    /// row-preferences PUT validation (anchor = server today).
    ///
    /// <para>The pre-S72 semantics are preserved byte-for-byte (the eligibility notes below
    /// moved with the code):</para>
    /// <list type="bullet">
    ///   <item><b>S59 / TASK-5907 display filter:</b> a DISPLAY affordance only; the POST
    ///     gate per absence.Date is authoritative. The POST gate intentionally uses a
    ///     different anchor (per-row absence.Date); the two are NOT required to agree
    ///     mid-month (refinement line 19, SPRINT-59 TASK-5907).</item>
    ///   <item><b>(a) CHILD_SICK:</b> drop all child-sick variants (those mapping to
    ///     entitlement CHILD_SICK) when the employee is ineligible as-of the anchor. Absent
    ///     eligibility row ⇒ ineligible (opt-in default).</item>
    ///   <item><b>(b) SENIOR_DAY:</b> DOB-derived age gate — hide when under the resolved
    ///     SENIOR_DAY MinAge as-of the anchor, OR when BirthDate is missing (fail-closed).
    ///     S59 / Step-7a BLOCKER 2: the SENIOR_DAY config is resolved AS-OF the anchor (not
    ///     the live/open row) so min_age and the age computation share the same anchor
    ///     (determinism, P4 / ADR-020).</item>
    ///   <item><b>(c) SPECIAL_HOLIDAY (S147 / TASK-14701, QUAL-235):</b> the særlige-feriedage
    ///     row (<c>SPECIAL_HOLIDAY_ALLOWANCE</c>) is served only when a <c>SPECIAL_HOLIDAY</c>
    ///     entitlement config resolves for the agreement at the anchor (dated read, OK version
    ///     resolved from the anchor). Org visibility hides it like any other type.</item>
    /// </list>
    /// </summary>
    private static async Task<List<AbsenceCatalogEntry>> ComputeAbsenceCatalogAsync(
        string employeeId,
        string agreementCode,
        User user,
        DateOnly anchor,
        AbsenceTypeVisibilityRepository visibilityRepo,
        EmployeeEntitlementEligibilityRepository eligibilityRepo,
        EntitlementConfigRepository entitlementConfigRepo,
        CancellationToken ct)
    {
        // Org-level absence type visibility.
        var visibilityEntries = await visibilityRepo.GetByOrgAsync(user.PrimaryOrgId, ct);
        var hiddenTypes = new HashSet<string>(
            visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType),
            StringComparer.Ordinal);

        // (a) CHILD_SICK eligibility as-of the anchor.
        var childSickEligible = (await eligibilityRepo
            .GetEligibleAsOfAsync(employeeId, "CHILD_SICK", anchor, ct)).Eligible;

        // (b) SENIOR_DAY DOB-derived age gate, config resolved as-of the same anchor.
        var seniorConfig = await entitlementConfigRepo.GetByTypeAtAsync(
            "SENIOR_DAY", agreementCode, user.OkVersion, anchor, ct);
        var seniorMinAge = seniorConfig?.MinAge;
        var seniorVisible = seniorMinAge is null // no age gate configured ⇒ unrestricted
            || (user.BirthDate is { } dob && AgeAsOf(dob, anchor) >= seniorMinAge.Value);

        // (c) SPECIAL_HOLIDAY — S147 / TASK-14701 (QUAL-235): særlige feriedage are served only
        //     where the employee's agreement configures them. Same dated repository read as gate
        //     (b) (GetByTypeAtAsync at the catalogue anchor), with the OK version IN FORCE at the
        //     anchor (OkVersionResolver.ResolveVersion(anchor)) — the S73 H2 convention the served
        //     fullDayOnly flag below and the save path's consumption reads use. No dated row ⇒ not
        //     served (fail-closed). The resolved row also seeds the served fullDayOnly flag below:
        //     one read, so the gate and the flag can never come from two different rows.
        //     S147 / TASK-14708 (review follow-up A-1): this gate is a DISPLAY affordance, like
        //     gate (b)'s dated read; it does not decide whether a save is counted against a quota.
        //     The save's quota check keys on the live open config row (GetCurrentOpenAsync in the
        //     save path). With no live row the save records HasNoLiveConfig
        //     (QuotaPreCheckPlan.WithoutLiveConfig): the absence is still saved, no quota is
        //     checked and no balance moves.
        var specialHolidayConfig = await entitlementConfigRepo.GetByTypeAtAsync(
            "SPECIAL_HOLIDAY", agreementCode, OkVersionResolver.ResolveVersion(anchor), anchor, ct);
        var specialHolidayConfigured = specialHolidayConfig is not null;

        // Agreement → visibility → eligibility, in the established order.
        var agreementAbsenceTypes = GetAbsenceTypesForAgreement(agreementCode);
        var filteredTypes = agreementAbsenceTypes
            .Where(t => !hiddenTypes.Contains(t))
            .Where(t => childSickEligible
                || !string.Equals(GetEntitlementType(t), "CHILD_SICK", StringComparison.Ordinal))
            .Where(t => seniorVisible || !string.Equals(t, AbsenceTypes.SeniorDay, StringComparison.Ordinal))
            .Where(t => specialHolidayConfigured
                || !string.Equals(GetEntitlementType(t), "SPECIAL_HOLIDAY", StringComparison.Ordinal))
            .ToList();

        // ── S73 / TASK-7301 (R3) — the served fullDayOnly flag, per entitlement type ──
        // Resolved DATED at the SAME anchor as the rest of this catalog chain (month-end on
        // the GET, today on the row-preferences PUT) — a DISPLAY affordance like the S59
        // eligibility filter above; the POST save guard reads the flag per absence ROW date
        // and stays authoritative (the established display/gate anchor split). One dated read
        // per distinct entitlement type.
        //
        // S73 / TASK-7301 (H2) — the OK version is resolved FROM the anchor date
        // (OkVersionResolver.ResolveVersion(anchor)), matching the convention the month GET's
        // OTHER served dated fields use: dailyNorm + the consumptionBasis array resolve OK per
        // day via OkVersionResolver inside DailyNormCalculator, NOT user.OkVersion. SENIOR_DAY
        // flows through this same loop (it is NOT pre-seeded from the scope-guarded min_age
        // read above, whose user.OkVersion anchor is pre-existing S59 behavior and out of this
        // fix's scope) so every served fullDayOnly value shares this one OK-version convention.
        var fullDayOnlyByEntitlementType = new Dictionary<string, bool>(StringComparer.Ordinal);

        // S147 / TASK-14701 — SPECIAL_HOLIDAY IS pre-seeded (unlike SENIOR_DAY): gate (c) above
        // made exactly the read this loop would make (same type, agreement, anchor-resolved OK
        // version and anchor), so its row's flag is reused rather than read a second time.
        if (specialHolidayConfig is not null)
            fullDayOnlyByEntitlementType["SPECIAL_HOLIDAY"] = specialHolidayConfig.FullDayOnly;

        var catalog = new List<AbsenceCatalogEntry>(filteredTypes.Count);
        foreach (var t in filteredTypes)
        {
            var fullDayOnly = false;
            if (GetEntitlementType(t) is { } entitlementType)
            {
                if (!fullDayOnlyByEntitlementType.TryGetValue(entitlementType, out fullDayOnly))
                {
                    var datedConfig = await entitlementConfigRepo.GetByTypeAtAsync(
                        entitlementType, agreementCode,
                        OkVersionResolver.ResolveVersion(anchor), anchor, ct);
                    fullDayOnly = datedConfig?.FullDayOnly ?? false;
                    fullDayOnlyByEntitlementType[entitlementType] = fullDayOnly;
                }
            }

            catalog.Add(new AbsenceCatalogEntry(
                t, AbsenceTypeLabels.LabelFor(t), fullDayOnly));
        }

        return catalog;
    }

    // ── Request DTOs ──

    // S72 / TASK-7201 — PUT /api/skema/{employeeId}/row-preferences body: the FULL
    // replacement set. A null array is treated as the EMPTY set (full replacement
    // semantics); submitting both empty is the legal "configured, zero visible rows" state.
    private sealed class RowPreferencesRequest
    {
        public RowPreferenceProjectItem[]? Projects { get; init; }
        public RowPreferenceAbsenceItem[]? AbsenceTypes { get; init; }
    }

    private sealed class RowPreferenceProjectItem
    {
        public required Guid ProjectId { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class RowPreferenceAbsenceItem
    {
        public required string AbsenceType { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class SaveSkemaRequest
    {
        public required int Year { get; init; }
        public required int Month { get; init; }
        public SkemaEntry[]? Entries { get; init; }
        public SkemaAbsence[]? Absences { get; init; }

        // TASK-5603 — optional self-recorded work-time block ("Arbejdstid").
        // Routed through a DEDICATED handler branch (NOT the project-entry /
        // absence classifier). Each day emits a latest-wins WorkTimeRegistered
        // event; re-saving a day supersedes the prior row via the projection's
        // outbox_id guard.
        public SkemaWorkTimeDay[]? WorkTime { get; init; }
    }

    private sealed class SkemaEntry
    {
        public required DateOnly Date { get; init; }
        public required string ProjectCode { get; init; }
        public required decimal Hours { get; init; }
    }

    // TASK-5603 — one day's self-recorded work time: a list of wall-clock
    // intervals plus a manual daily-hours scalar. Mirrors the
    // WorkTimeRegistered event shape (EmployeeId is taken from the route, not
    // the body, so it cannot be spoofed).
    private sealed class SkemaWorkTimeDay
    {
        public required DateOnly Date { get; init; }
        public SkemaWorkInterval[]? Intervals { get; init; }
        public decimal ManualHours { get; init; }
    }

    private sealed class SkemaWorkInterval
    {
        public required string Start { get; init; }
        public required string End { get; init; }
    }

    private sealed class SkemaAbsence
    {
        public required DateOnly Date { get; init; }
        public required string AbsenceType { get; init; }
        public required decimal Hours { get; init; }
    }

    /// <summary>
    /// S145 / TASK-14507 (Step-5a cycle 2) — one entitlement type's quota pre-check as phase 1 of the
    /// save handler planned it: everything the per-type decision needs that does not depend on the
    /// balance, or the outcome phase 1 recorded instead of returning (phase 3 replays it in order).
    /// <see cref="HasNoLiveConfig"/> is the old loop's <c>continue</c> (no pre-check, no
    /// entitlementData); <see cref="Refusal"/> is the profile-missing 422, answered at this type's
    /// turn. Exactly one of the two, or neither for a fully planned type.
    /// </summary>
    private sealed class QuotaPreCheckPlan
    {
        public required string EntitlementType { get; init; }
        public decimal RequestedDays { get; init; }
        public DateOnly FirstAbsenceDate { get; init; }
        public int EntitlementYear { get; init; }
        public DateOnly EntitlementYearStart { get; init; }
        public EntitlementConfig? Config { get; init; }
        public decimal PartTimeFraction { get; init; }
        public decimal EffectiveQuota { get; init; }
        public bool IsMonthlyAccrual { get; init; }
        /// <summary>The carryover-EXCLUDED accruable cap of a MONTHLY_ACCRUAL type (0 otherwise);
        /// phase 3 adds the snapshot's carryover to form the rule-engine BookableLimit.</summary>
        public decimal AccruableCap { get; init; }
        public decimal GuardCap { get; init; }
        public bool HasNoLiveConfig { get; init; }
        public IResult? Refusal { get; init; }

        public static QuotaPreCheckPlan WithoutLiveConfig(string entitlementType) =>
            new() { EntitlementType = entitlementType, HasNoLiveConfig = true };

        public static QuotaPreCheckPlan Refused(string entitlementType, int entitlementYear, IResult refusal) =>
            new() { EntitlementType = entitlementType, EntitlementYear = entitlementYear, Refusal = refusal };
    }

    private sealed class EntitlementValidationResult
    {
        public bool Allowed { get; init; }
        public string Status { get; init; } = "";
        public decimal EffectiveQuota { get; init; }
        public decimal RemainingAfter { get; init; }
        public string? Message { get; init; }
    }
}
