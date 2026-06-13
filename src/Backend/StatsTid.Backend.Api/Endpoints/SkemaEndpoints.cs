using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class SkemaEndpoints
{
    // ── Danish absence type labels ──
    private static readonly Dictionary<string, string> AbsenceTypeLabels = new(StringComparer.Ordinal)
    {
        ["SICK_DAY"] = "Sygedag",
        ["VACATION"] = "Ferie",
        ["CARE_DAY"] = "Omsorgsdag",
        ["CHILD_SICK_DAY"] = "Barns 1. sygedag",
        ["CHILD_SICK_DAY_2"] = "Barns 2. sygedag",
        ["CHILD_SICK_DAY_3"] = "Barns 3. sygedag",
        ["PARENTAL_LEAVE"] = "Barsel",
        ["SENIOR_DAY"] = "Seniordag",
        ["LEAVE_WITH_PAY"] = "Tjenestefri m. l\u00f8n",
        ["LEAVE_WITHOUT_PAY"] = "Tjenestefri u. l\u00f8n"
    };

    // ── Base absence types common to all agreements ──
    private static readonly string[] BaseAbsenceTypes =
    [
        "SICK_DAY", "VACATION", "CARE_DAY", "CHILD_SICK_DAY",
        "PARENTAL_LEAVE", "SENIOR_DAY", "LEAVE_WITH_PAY", "LEAVE_WITHOUT_PAY"
    ];

    /// <summary>
    /// Returns the absence types available for a given agreement code.
    /// Child sick days vary: AC=1, HK=2, PROSA=3.
    /// </summary>
    private static HashSet<string> GetAbsenceTypesForAgreement(string agreementCode)
    {
        var types = new HashSet<string>(BaseAbsenceTypes, StringComparer.Ordinal);

        // HK gets 2 child sick days, PROSA gets 3
        if (agreementCode is "HK" or "PROSA")
            types.Add("CHILD_SICK_DAY_2");

        if (agreementCode is "PROSA")
            types.Add("CHILD_SICK_DAY_3");

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

    /// <summary>
    /// Resolve the entitlement year for a given date based on the reset month.
    /// If resetMonth is 9 (ferieår) and date is September+, year = date.Year; else year = date.Year - 1.
    /// </summary>
    private static int ResolveEntitlementYear(DateOnly date, int resetMonth)
    {
        return date.Month >= resetMonth ? date.Year : date.Year - 1;
    }

    /// <summary>
    /// S30 / TASK-3008 — derive the entitlement-year START DATE for a given relevant date
    /// and reset month. Used as the <c>asOfDate</c> for dated reads against
    /// <see cref="EntitlementConfigRepository.GetByTypeAtAsync(string, string, string, DateOnly, CancellationToken)"/>
    /// (two-step pattern: live <c>ResetMonth</c> read first, then dated re-read at year-start).
    /// resetMonth is 1-12. The entitlement year starts on (resetMonth, 1) of either the relevantDate's
    /// calendar year (when month &gt;= resetMonth) or the prior calendar year (when month &lt; resetMonth).
    /// E.g. AC VACATION resetMonth=9, relevantDate=2026-05-14 → year-start = 2025-09-01.
    /// </summary>
    private static DateOnly ResolveEntitlementYearStart(DateOnly relevantDate, int resetMonth)
    {
        var year = relevantDate.Month >= resetMonth
            ? relevantDate.Year
            : relevantDate.Year - 1;
        return new DateOnly(year, resetMonth, 1);
    }

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
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Employee can only access own data
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only access own data" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
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
                .Select(c => new { type = c.Type, label = c.Label, fullDayOnly = c.FullDayOnly })
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
                .Select(e => new
                {
                    date = e.Date,
                    projectCode = e.TaskId,
                    hours = e.Hours
                })
                .ToList();

            var absencesRows = await absenceProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var absences = absencesRows
                .Select(e => new
                {
                    date = e.Date,
                    absenceType = e.AbsenceType,
                    hours = e.Hours,
                    // S72 / TASK-7201 (R10) — the ADR-032 recorded per-absence feriedage,
                    // served verbatim from absences_projection (nullable passthrough: ADR-032
                    // persists null on zero-norm days / non-entitlement rows; the FE skips
                    // null-valued rows when summing — SPRINT-72 R10 / Reviewer N4).
                    feriedage = e.Feriedage
                })
                .ToList();

            // ── TASK-5603 self-recorded work time ("Arbejdstid") ──
            // Latest-wins per-day rows from work_time_projection (read-your-write
            // satisfied: the atomic POST upserts in the same tx as the
            // WorkTimeRegistered event). Intervals are already deserialized by the repo.
            var workTimeRows = await workTimeProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var workTime = workTimeRows
                .Select(w => new
                {
                    date = w.Date,
                    intervals = w.Intervals.Select(i => new { start = i.Start, end = i.End }).ToList(),
                    manualHours = w.ManualHours
                })
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
                .Select(n => (object)new { date = n.Date, hours = n.Hours })
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

            var rowPreferences = new
            {
                configured = preferencesConfigured,
                projects = visibleProjects
                    .Select((p, i) => new
                    {
                        projectId = p.ProjectId,
                        projectCode = p.ProjectCode,
                        projectName = p.ProjectName,
                        sortOrder = i
                    })
                    .ToList(),
                absenceTypes = visibleAbsenceTypes
                    .Select((t, i) => new
                    {
                        type = t,
                        label = AbsenceTypeLabels.TryGetValue(t, out var l) ? l : t,
                        fullDayOnly = fullDayOnlyByType.TryGetValue(t, out var fdo) && fdo,
                        sortOrder = i
                    })
                    .ToList()
            };

            var catalogs = new
            {
                // Addable projects in the org read's order; sortOrder here is the ORG-level
                // sort (mirrors the existing `projects` field's meaning for this value).
                projects = catalogProjects
                    .Select(p => new
                    {
                        projectId = p.ProjectId,
                        projectCode = p.ProjectCode,
                        projectName = p.ProjectName,
                        sortOrder = p.SortOrder
                    })
                    .ToList(),
                // The SAME filtered chain (and order) the existing `absenceTypes` field
                // serves — one computation, two projections. S73 / TASK-7301 (R3): the
                // catalog surface carries fullDayOnly.
                absenceTypes = absenceCatalog
                    .Select(c => new { type = c.Type, label = c.Label, fullDayOnly = c.FullDayOnly })
                    .ToList()
            };

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
                .Select(w => new
                {
                    date = w.Date,
                    intervals = w.Intervals.Select(i => new { start = i.Start, end = i.End }).ToList(),
                    manualHours = w.ManualHours
                })
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
            var consumptionBasis = new List<object>(daysInMonth);
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
                consumptionBasis.Add(new { date = basisDay, hours = RoundBasis(basis) });
            }

            // Get approval period for this month
            var period = await approvalRepo.GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd, ct);
            object? approval = period is not null
                ? new
                {
                    periodId = period.PeriodId,
                    status = period.Status,
                    employeeDeadline = period.EmployeeDeadline,
                    managerDeadline = period.ManagerDeadline,
                    employeeApprovedAt = period.EmployeeApprovedAt,
                    rejectionReason = period.RejectionReason
                }
                : null;

            // Compute deadlines
            var employeeDeadline = monthEnd.AddDays(2);
            var managerDeadline = monthEnd.AddDays(5);

            return Results.Ok(new
            {
                year,
                month,
                daysInMonth,
                projects = projects.Select(p => new
                {
                    projectId = p.ProjectId,
                    projectCode = p.ProjectCode,
                    projectName = p.ProjectName,
                    sortOrder = p.SortOrder
                }),
                absenceTypes,
                entries,
                absences,
                workTime,
                dailyNorm,
                approval,
                employeeDeadline,
                managerDeadline,
                // ── S72 / TASK-7201 additive fields (ALL existing fields above are
                // byte-unchanged; the redesigned FE consumes these four) ──
                rowPreferences,
                catalogs,
                boundaryWorkTime,
                fullDayNormAtMonthEnd,
                // ── S73 / TASK-7301 additive field (SPRINT-73 R3) ──
                consumptionBasis
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── POST /api/skema/{employeeId}/save — Batch save entries and absences ──

        app.MapPost("/api/skema/{employeeId}/save", async (
            string employeeId,
            SaveSkemaRequest request,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            ApprovalPeriodRepository approvalRepo,
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
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Get employee profile for agreement info
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

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

            var period = await approvalRepo.GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd, ct);
            if (period is not null && period.Status is "EMPLOYEE_APPROVED" or "APPROVED")
                return Results.Conflict(new { error = $"Cannot save entries for a period with status {period.Status}" });

            // ── Work-time date-range validation (S56 / Step 7a BLOCKER fix) ──
            // The approval-lock check above is scoped to the REQUESTED month. Reject any
            // work-time day outside [monthStart, monthEnd] up front (before the tx) so that
            // lock governs every written date. Without this, a save targeting an unlocked
            // month could smuggle in a date belonging to an already-approved/locked month and
            // overwrite its work_time_projection row, bypassing the period lock.
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
                                message = $"{AbsenceTypeLabels.GetValueOrDefault(absence.AbsenceType, absence.AbsenceType)} kan kun registreres som hel dag ({requiredHours} timer) den {absence.Date:dd-MM-yyyy}.",
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
                                    message = $"Medarbejderen er ikke berettiget til {AbsenceTypeLabels.GetValueOrDefault(absence.AbsenceType, absence.AbsenceType)} på {absence.Date:dd-MM-yyyy}."
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

                foreach (var (entitlementType, totalRequestedDays) in requestedByEntitlementType)
                {
                    var requestedDays = totalRequestedDays;

                    // ── S30 TASK-3008 two-step pattern (ADR-021 D2 + ADR-016 D5b "fifth pattern") ──
                    // Step 1: read the LIVE (open) row to derive ResetMonth. ResetMonth is frozen
                    // per natural key by the TASK-3007 admin-scope 422 guard (per ADR-021 Q1 sub-fork
                    // (i)), so the live value is safe to use for entitlement-year-start derivation
                    // across the entire history of this natural key.
                    var liveConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                        entitlementType, agreementCode, user.OkVersion, ct);
                    if (liveConfig is null)
                        continue;

                    var firstAbsenceDate = request.Absences
                        .Where(a => AbsenceToEntitlementType.TryGetValue(a.AbsenceType, out var et) && et == entitlementType)
                        .Select(a => a.Date)
                        .Min();
                    var entitlementYear = ResolveEntitlementYear(firstAbsenceDate, liveConfig.ResetMonth);
                    var entitlementYearStart = ResolveEntitlementYearStart(firstAbsenceDate, liveConfig.ResetMonth);

                    // Step 2: dated read at the entitlement-year-start. This is the config row that
                    // was IN EFFECT on the day the current entitlement year started — what quota
                    // validation must use (annual_quota / carryover_max as they stood at year-start).
                    // Fallback: if no row was effective at year-start (e.g. this OK version came into
                    // existence mid-year), fall back to the live config so we don't silently skip a
                    // real quota check.
                    var config = await entitlementConfigRepo.GetByTypeAtAsync(
                        entitlementType, agreementCode, user.OkVersion, entitlementYearStart, ct)
                        ?? liveConfig;

                    var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                        employeeId, entitlementType, entitlementYear, ct);

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
                            return Results.Json(new
                            {
                                error = "employment_profile_missing",
                                absenceType = entitlementType,
                                date = firstAbsenceDate,
                                message = $"Kan ikke validere Ferie/særlige feriedage for {firstAbsenceDate:dd-MM-yyyy}: ansættelsesprofil mangler."
                            }, statusCode: 422);
                    }
                    var partTimeFraction = datedProfile?.PartTimeFraction ?? 1.0m;

                    var carryoverIn = balance?.CarryoverIn ?? 0m;

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

                    decimal? bookableLimit;
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
                        decimal accruableCap;
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
                            accruableCap = AccrualMath.EarnedToDate(
                                config.AnnualQuota, 1.0m, entitlementYearStart,
                                user.EmploymentStartDate, firstAbsenceDate);
                        }

                        guardCap = accruableCap;                       // carryover-EXCLUDED
                        bookableLimit = accruableCap + carryoverIn;    // carryover-INCLUSIVE business cap
                    }
                    else
                    {
                        // IMMEDIATE types: cap unchanged (annual effectiveQuota). No BookableLimit
                        // override — the rule keys off (effectiveQuota + carryover) as before.
                        guardCap = effectiveQuota;
                        bookableLimit = null;
                    }

                    // Call Rule Engine via HTTP (PAT-005 compliance). Accrual inputs are carried so
                    // the rule applies the per-type cap; the warning-threshold + per-episode branches
                    // stay on the ANNUAL quota (no spurious early-ferieår warnings).
                    var validationRequest = new
                    {
                        annualQuota = config.AnnualQuota,
                        used = balance?.Used ?? 0m,
                        planned = balance?.Planned ?? 0m,
                        carryoverIn,
                        requestedDays,
                        partTimeFraction,
                        proRateByPartTime = config.ProRateByPartTime,
                        isPerEpisode = config.IsPerEpisode,
                        perEpisodeLimit = (decimal?)null,
                        accrualModel = config.AccrualModel,
                        ferieaarStart = (DateOnly?)entitlementYearStart,
                        employmentStart = user.EmploymentStartDate,
                        asOfDate = (DateOnly?)firstAbsenceDate,
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

                    // GuardCap is carryover-EXCLUDED (CheckAndAdjustAsync re-adds carryover once);
                    // SeedQuota = annual effectiveQuota (seeds total_quota on a first-INSERT only).
                    entitlementData[entitlementType] = (requestedDays, entitlementYear, guardCap, effectiveQuota);
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
            // benign: the in-lock guard re-enforces against the fresh values.
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    // ── S66 / TASK-6603 — ADR-032 D2/D4 two-phase consumption cutover ──
                    // PHASE 2 (in-lock authoritative). FIRST acquire the per-employee advisory
                    // lock — before ANY read/write in this tx, so it precedes any FOR UPDATE and is
                    // held to commit (D4). This serializes against a racing profile-PUT revaluation
                    // (TASK-6604, which reuses this same helper).
                    await StatsTid.Backend.Api.Services.EmployeeConsumptionLock.AcquireAsync(
                        conn, tx, employeeId, ct);

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
                                        message = $"{AbsenceTypeLabels.GetValueOrDefault(absence.AbsenceType, absence.AbsenceType)} kan kun registreres som hel dag ({requiredHours} timer) den {absence.Date:dd-MM-yyyy}.",
                                    });
                            }
                        }
                    }

                    // Save time entries (outbox enqueue + projection INSERT inside outer tx).
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

                            // S66 / TASK-6603 — the guard delta IS the Σ authoritative per-row
                            // feriedage (already day-equivalents from the in-lock valuation). NO
                            // re-division by StandardDayHours (ADR-032 D2 single valuation): this is
                            // the same value Σ'd into the emitted AbsenceRegistered.Feriedage above.
                            var deltaDays = totalFeriedage;
                            // S60 / TASK-6005 — split guard: GuardCap (carryover-EXCLUDED per-type
                            // bookable cap; the WHERE-clause re-adds carryover once) + SeedQuota
                            // (= annual entitlement, seeds total_quota on a first-INSERT only).
                            var (success, newUsed) = await entitlementBalanceRepo.CheckAndAdjustAsync(
                                conn, tx, employeeId, entitlementType, data.EntitlementYear,
                                deltaDays, data.GuardCap, data.SeedQuota, ct);

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

            return Results.Ok(new { saved = savedCount });
        }).RequireAuthorization("EmployeeOrAbove");

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
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
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
            return Results.Ok(new
            {
                configured = true,
                projects = orderedProjects
                    .Select(p =>
                    {
                        var project = projectsById[p.ProjectId];
                        return new
                        {
                            projectId = project.ProjectId,
                            projectCode = project.ProjectCode,
                            projectName = project.ProjectName,
                            sortOrder = p.SortOrder
                        };
                    })
                    .ToList(),
                absenceTypes = orderedAbsenceTypes
                    .Select(a => new
                    {
                        type = a.AbsenceType,
                        label = AbsenceTypeLabels.TryGetValue(a.AbsenceType, out var l) ? l : a.AbsenceType,
                        fullDayOnly = prefFullDayOnlyByType.TryGetValue(a.AbsenceType, out var fdo) && fdo,
                        sortOrder = a.SortOrder
                    })
                    .ToList()
            });
        }).RequireAuthorization("EmployeeOrAbove");

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

        // Agreement → visibility → eligibility, in the established order.
        var agreementAbsenceTypes = GetAbsenceTypesForAgreement(agreementCode);
        var filteredTypes = agreementAbsenceTypes
            .Where(t => !hiddenTypes.Contains(t))
            .Where(t => childSickEligible
                || !string.Equals(GetEntitlementType(t), "CHILD_SICK", StringComparison.Ordinal))
            .Where(t => seniorVisible || !string.Equals(t, "SENIOR_DAY", StringComparison.Ordinal))
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
                t, AbsenceTypeLabels.TryGetValue(t, out var l) ? l : t, fullDayOnly));
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

    private sealed class EntitlementValidationResult
    {
        public bool Allowed { get; init; }
        public string Status { get; init; } = "";
        public decimal EffectiveQuota { get; init; }
        public decimal RemainingAfter { get; init; }
        public string? Message { get; init; }
    }
}
