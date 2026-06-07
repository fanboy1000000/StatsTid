using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class BalanceEndpoints
{
    private static readonly Dictionary<string, string> DanishLabels = new()
    {
        ["VACATION"] = "Ferie",
        ["SPECIAL_HOLIDAY"] = "Feriefridage",
        ["CARE_DAY"] = "Omsorgsdage",
        ["CHILD_SICK"] = "Barns sygedag",
        ["SENIOR_DAY"] = "Seniordage"
    };

    /// <summary>The "MONTHLY_ACCRUAL" accrual_model string (ADR-030).</summary>
    private const string MonthlyAccrualModel = "MONTHLY_ACCRUAL";

    // S61 / TASK-6101 — the Backend-local EarnedToDate/MonthIndex mirror was removed. The pure
    // earned-to-date math is now the single shared copy in StatsTid.SharedKernel.Calendar.AccrualMath
    // (delegated to via the `using` above). PAT-005 is unaffected: AccrualMath is a dependency-free
    // SharedKernel leaf both the Backend and the Rule Engine already reference, NOT the RuleEngine
    // assembly (the validate-entitlement boundary stays HTTP-only).

    public static WebApplication MapBalanceEndpoints(this WebApplication app)
    {
        // ── GET /api/balance/{employeeId}/summary — Employee balance summary for a given month ──

        app.MapGet("/api/balance/{employeeId}/summary", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            AgreementConfigRepository configRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
            OvertimeBalanceRepository overtimeBalanceRepo,
            TimeEntryProjectionRepository timeEntryProjectionRepo,
            AbsenceProjectionRepository absenceProjectionRepo,
            IEventStore eventStore,
            OrgScopeValidator scopeValidator,
            IEmploymentProfileResolver profileResolver,
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

            // Validate month/year
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year or month" });

            // Get employee profile
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // Calculate working days (Mon-Fri) in the month
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            // S34 / TASK-3410 — ADR-023 D2 binding cutover for agreement_code on past-period
            // queries. Source the month-effective agreement_code from the dated repository
            // instead of the live `user.AgreementCode` cache. If admin changed agreement_code
            // today, last month's summary must recompute against the agreement code that was
            // in effect at the start of last month — not today's.
            //
            // ADR-023 D3 (Balance = graceful-fallback consumer): if the dated lookup returns
            // null (defensive — shouldn't happen post-backfill but possible if user was
            // created after the period being summarized + before backfill), fall through to
            // the live `user.AgreementCode` cache. Balance is informational (not load-bearing
            // for PCS replay); graceful degradation is correct per the ADR-023 D3 split
            // (PCS fails-closed; HTTP consumers gracefully fall back).
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, monthStart, ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            // ADR-023 D3: resolver-first, then existing fallback chain. Balance is a
            // pure-HTTP non-rule-engine consumer — null resolver result falls through
            // gracefully to AgreementConfig → CentralAgreementConfigs → 37.0m floor.
            // Documented determinism gap on agreement_code: when admin soft-deletes a
            // profile, this endpoint returns sensible default data (graceful), unlike
            // the PCS-routed callers that fail-closed (ADR-023 D3 split).
            //
            // S60 / TASK-6005 — the resolver can throw EmployeeProfileNotFoundException when an
            // employee_profiles row exists but the dated user_agreement_codes row is missing
            // (data-integrity fail-loud, EmploymentProfileResolver). The Balance summary MUST
            // stay graceful (ADR-023 D3): a profile-less / inconsistent employee's summary still
            // renders rather than 500ing. Tolerate both null AND the exception ⇒ no dated profile.
            EmploymentProfile? datedProfile;
            try
            {
                datedProfile = await profileResolver.GetByEmployeeIdAtAsync(
                    employeeId, monthEnd, ct);
            }
            catch (EmployeeProfileNotFoundException)
            {
                datedProfile = null;
            }

            // S60 / TASK-6005 — dated part-time fraction at the requested MONTH-END (the Balance
            // seam's as-of anchor, matching the earned-to-date computation below). Graceful
            // fallback to 1.0m when there is no dated profile (or the resolver fail-loud above),
            // so a profile-less employee's summary renders without a 500. Replaces the prior
            // hard-coded 1.0m.
            var partTimeFraction = datedProfile?.PartTimeFraction ?? 1.0m;

            // Get agreement config — try DB first (ACTIVE), fall back to central static config
            var dbConfig = await configRepo.GetActiveAsync(agreementCode, user.OkVersion, ct);
            var weeklyNormHours = dbConfig?.WeeklyNormHours
                ?? CentralAgreementConfigs.TryGetConfig(agreementCode, user.OkVersion)?.WeeklyNormHours
                ?? 37.0m;
            var hasMerarbejde = dbConfig?.HasMerarbejde
                ?? CentralAgreementConfigs.TryGetConfig(agreementCode, user.OkVersion)?.HasMerarbejde
                ?? false;

            var weekdays = 0;
            for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    weekdays++;
            }

            var normHoursExpected = (weekdays / 5.0m) * weeklyNormHours;

            // Time entries: read from projection (date-range scoped to the month) — S27 TASK-2708.
            // Read-your-write is satisfied because the atomic POST handler (TASK-2707) commits
            // the projection row in the same transaction as the TimeEntryRegistered event.
            var timeEntries = await timeEntryProjectionRepo.GetByEmployeeAndDateRangeAsync(
                employeeId, monthStart, monthEnd, ct);
            var normHoursActual = timeEntries.Sum(e => e.Hours);

            // Absences: vacation days used over the entire calendar year — S27 TASK-2708.
            // The date filter spans the whole year (not just the month), so use the full-stream
            // read + client-side year filter rather than a date-range query.
            var allAbsences = await absenceProjectionRepo.GetByEmployeeAsync(employeeId, ct);
            var vacationDaysUsed = allAbsences
                .Where(e => e.AbsenceType == "VACATION" && e.Date.Year == year)
                .Select(e => e.Date)
                .Distinct()
                .Count();

            // Flex balance still comes from the event stream — flex projection is out of scope
            // for S27 (Phase 4c.6 Assumption #4). The employee event stream is retained ONLY for
            // this FlexBalanceUpdated read.
            var streamId = $"employee-{employeeId}";
            var allEvents = await eventStore.ReadStreamAsync(streamId, ct);
            var latestFlex = allEvents.OfType<FlexBalanceUpdated>().LastOrDefault();
            var flexBalance = latestFlex?.NewBalance ?? 0m;
            var flexDelta = latestFlex?.Delta ?? 0m;

            // Overtime: max(0, actual - expected)
            var overtimeHours = Math.Max(0m, normHoursActual - normHoursExpected);

            // ── Overtime balance from DB ──
            var overtimeBalance = await overtimeBalanceRepo.GetByEmployeeAndYearAsync(employeeId, year, ct);

            // ── Entitlements: load configs and balances ──
            // S30 TASK-3008 two-step pattern (ADR-021 D2 + ADR-016 D5b "fifth pattern").
            // Step 1: read the LIVE (open) rows for this agreement/OK pair to discover the
            // natural keys + their ResetMonth values. ResetMonth is frozen per natural key by
            // the TASK-3007 admin-scope 422 guard (ADR-021 Q1 sub-fork (i)), so each live
            // row's ResetMonth is safe to use for year-start derivation across the full history.
            //
            // EffectiveTo IS NULL filter is load-bearing: GetByAgreementAsync returns ALL rows
            // (open + closed predecessors). Post-supersession the bulk read contains 2 rows per
            // natural key — without this filter the loop double-emits each entitlement and
            // vacationDaysEntitlement gets overwritten with whichever row is visited last.
            var liveConfigs = (await entitlementConfigRepo.GetByAgreementAsync(
                agreementCode, user.OkVersion, ct))
                .Where(c => c.EffectiveTo is null);

            // S60 / TASK-6005 — partTimeFraction is sourced from the dated employment profile at
            // month-end (resolved above, graceful ?? 1.0m). It is retained ONLY for the generic
            // ProRateByPartTime totalQuota ternary below (config-driven — admin CRUD may set the
            // flag on some types; ADR-023 D3 graceful ?? 1.0m polarity). As of S63 / ADR-031 every
            // SEEDED entitlement type is ProRateByPartTime=false, so the fraction no longer scales
            // any seeded quota; the earned-to-date figure is fraction-INDEPENDENT (flat day-count,
            // Ferieloven §5) — see the earned computation below.

            var entitlements = new List<object>();
            decimal? vacationEntitlementFromConfig = null;

            foreach (var live in liveConfigs)
            {
                // Calculate entitlement year based on the live ResetMonth (immutable per natural key).
                int entitlementYear;
                if (live.ResetMonth == 1)
                {
                    entitlementYear = year;
                }
                else
                {
                    entitlementYear = month >= live.ResetMonth ? year : year - 1;
                }

                // Step 2: dated read at the entitlement-year-start. This is the config row that
                // was IN EFFECT when the current entitlement year started — its annual_quota /
                // carryover_max define this year's quota for display. Per-type dated reads
                // (not GetByAgreementAtAsync) because each type may resolve to a DIFFERENT
                // asOfDate (different reset_month → different year-start).
                // Fallback: if no row was effective at year-start (e.g. this OK version came
                // into existence mid-year), fall back to the live row so the entitlement still
                // appears in the summary with the current quota values.
                var entitlementYearStart = new DateOnly(entitlementYear, live.ResetMonth, 1);
                var ec = await entitlementConfigRepo.GetByTypeAtAsync(
                    live.EntitlementType, agreementCode, user.OkVersion, entitlementYearStart, ct)
                    ?? live;

                // Look up balance for this employee + type + year (read from entitlement_balances
                // — the balance projection is owned by Skema/Time POST handlers and is unchanged
                // by Phase 4d-2 versioning of entitlement_configs).
                var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                    employeeId, ec.EntitlementType, entitlementYear, ct);

                var totalQuota = ec.ProRateByPartTime
                    ? ec.AnnualQuota * partTimeFraction
                    : ec.AnnualQuota;

                var used = balance?.Used ?? 0m;
                var planned = balance?.Planned ?? 0m;
                var carryoverIn = balance?.CarryoverIn ?? 0m;

                // ── S60 / TASK-6005 · S63 / TASK-6302 — earned-to-date for MONTHLY_ACCRUAL types ──
                // VACATION + SPECIAL_HOLIDAY accrue monthly (ADR-030): the AVAILABLE ("rest") figure
                // reflects what is EARNED-to-date (optjent), not the full annual quota the moment the
                // ferieår starts. S63 / ADR-031 cutover: the earned day-count is FLAT —
                // annualQuota × monthsElapsed / 12 — and INDEPENDENT of the part-time fraction
                // (Ferieloven §5 stk.1: a part-timer earns the same NUMBER of vacation days as a
                // full-timer; part-time affects consumption §6 stk.2 — S64 — and the monetary value
                // only, neither computed here). ADR-031 supersedes ADR-030 D8's piecewise per-month
                // fraction weighting. The 1.0m literal below is the DELIBERATE identity fraction —
                // the day-count never scales by fraction. Mid-ferieår-hire pro-ration by
                // months-elapsed is PRESERVED by threading user.EmploymentStartDate (ADR-030 D6;
                // null ⇒ full-ferieår, never fail-closed). asOf = the requested MONTH-END (the same
                // anchor as the Skema seam, so the two seams agree for the same as-of date).
                // ferieaarStart = the entitlement-year start (reset_month). IMMEDIATE types keep
                // their full quota as "earned".
                var isMonthlyAccrual = string.Equals(
                    ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal);

                decimal earned;
                if (isMonthlyAccrual)
                {
                    earned = AccrualMath.EarnedToDate(
                        ec.AnnualQuota, 1.0m, entitlementYearStart,
                        user.EmploymentStartDate, monthEnd);
                }
                else
                {
                    earned = totalQuota; // IMMEDIATE: full quota is "earned" up-front (unchanged).
                }

                // remaining ("rest") nets the EARNED amount + carryover − used − planned. For
                // IMMEDIATE types earned == totalQuota, so remaining is byte-for-byte unchanged.
                var remaining = earned + carryoverIn - used - planned;

                DanishLabels.TryGetValue(ec.EntitlementType, out var label);

                entitlements.Add(new
                {
                    type = ec.EntitlementType,
                    label = label ?? ec.EntitlementType,
                    // totalQuota stays the ANNUAL entitlement (its invariant display meaning, P3) so
                    // the card can show "X af Y" (earned-of-annual). The accrual change surfaces via
                    // the new `earned` field + the now-earned-based `remaining`.
                    totalQuota,
                    earned = Math.Round(earned, 2),
                    used,
                    planned,
                    carryoverIn,
                    remaining = Math.Round(remaining, 2),
                    entitlementYear
                });

                // Derive vacationDaysEntitlement from config instead of hardcoded 25
                if (ec.EntitlementType == "VACATION")
                    vacationEntitlementFromConfig = totalQuota;
            }

            // S60 / TASK-6005 — legacy top-level fields disposition (refinement AC):
            // `vacationDaysEntitlement` stays the ANNUAL entitlement (back-compat — it has always
            // meant the full annual quota, and the FE/Oversigt consume it as such). The
            // earned/available change is surfaced ONLY through the entitlements[] array's new
            // `earned` field + its now-earned-based `remaining`, so no existing top-level
            // consumer drifts. `vacationDaysUsed` (computed above from absences) is likewise
            // unchanged. Carryover semantics are otherwise untouched (ADR-030).
            var vacationDaysEntitlement = vacationEntitlementFromConfig ?? 25m;

            return Results.Ok(new
            {
                employeeId,
                year,
                month,
                flexBalance,
                flexDelta,
                vacationDaysUsed,
                vacationDaysEntitlement,
                normHoursExpected,
                normHoursActual,
                overtimeHours,
                agreementCode,
                hasMerarbejde,
                entitlements,
                overtimeBalance = overtimeBalance is not null ? new
                {
                    accumulated = overtimeBalance.Accumulated,
                    paidOut = overtimeBalance.PaidOut,
                    afspadseringUsed = overtimeBalance.AfspadseringUsed,
                    remaining = overtimeBalance.Remaining,
                    compensationModel = overtimeBalance.CompensationModel
                } : null
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── GET /api/balance/{employeeId}/series — per-month earned-to-date accrual curve ──
        //
        // S61 / TASK-6102 (ADR-030 compute-on-read) · S63 / TASK-6302 (ADR-031 flat day-count).
        // Returns the per-month optjent (earned-to-date) accrual curve for the MONTHLY_ACCRUAL
        // entitlements ONLY (VACATION + SPECIAL_HOLIDAY) across the relevant ferieår, so the FE
        // can plot the accrual ramp and highlight "now". Read-only, deterministic, emits NO events.
        //
        // Invariants pinned by Step-0b review + the S61 Step-7a fix (see CLAUDE.md priorities
        // #2/#4 — determinism; #4 — version/curve correctness):
        //   • MONTHLY_ACCRUAL-only: a type is included iff its resolved config
        //     AccrualModel == "MONTHLY_ACCRUAL". No IMMEDIATE series.
        //   • Server-derives the ferieår per type from the live config's ResetMonth, EXACTLY as
        //     /summary does — no client-supplied ferieår is trusted.
        //   • Full-ferieår curve: 12 points, month-END as-of for each, constructed from the
        //     derived ferieår months via DateOnly.AddMonths — NEVER DateTime.Today/Now (no
        //     wall-clock; pure/deterministic so the curve replays byte-stably).
        //   • FLAT, fraction-INDEPENDENT (S63 / ADR-031): each point is AccrualMath.EarnedToDate
        //     with the identity 1.0m fraction, so the curve is 12 equal monthly steps of
        //     annualQuota / 12 (≈2,08 d/md for VACATION) from the accrual start. A part-timer's
        //     curve is IDENTICAL to a full-timer's — the S62 part-time "bend" is gone (ADR-031
        //     supersedes ADR-030 D8; Ferieloven §5 day-count never scales by fraction). The curve
        //     is monotonic non-decreasing trivially (equal non-negative steps).
        //   • Mid-ferieår hires still start the curve at 0 until the accrual start (employmentStart
        //     threaded through AccrualMath, ADR-030 D6) and only ever rise — still monotonic.
        //   • Reconciliation: the point whose (year, month) == the requested (year, month) is
        //     byte-identical to /summary's `earned` for the same key. Both call
        //     EarnedToDate with the same (annualQuota, 1.0m, ferieår-start, employmentStart,
        //     asOf = that month-end) and apply the same Math.Round(.,2).

        app.MapGet("/api/balance/{employeeId}/series", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // ── Auth — both branches copied from /summary ──
            // Employee can only access own data (employee-self equality).
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only access own data" }, statusCode: 403);

            // Non-employee actors go through OrgScope (SECURITY.md / OrgScopeValidator).
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // Validate month/year (identical shape to /summary).
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year or month" });

            // Get employee profile.
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // Dated agreement_code at the requested month-start, with the same ADR-023 D3
            // graceful fallback to the live cache as /summary.
            var pastEffectiveAgreementCode = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, new DateOnly(year, month, 1), ct);
            var agreementCode = pastEffectiveAgreementCode ?? user.AgreementCode;

            // Live (open) entitlement configs for this agreement/OK pair — EffectiveTo IS NULL
            // filter is load-bearing (post-supersession the bulk read holds 2 rows per natural
            // key; same rationale as /summary). ResetMonth is frozen per natural key, so each
            // live row's ResetMonth safely drives year-start derivation.
            var liveConfigs = (await entitlementConfigRepo.GetByAgreementAsync(
                agreementCode, user.OkVersion, ct))
                .Where(c => c.EffectiveTo is null);

            var series = new List<object>();

            foreach (var live in liveConfigs)
            {
                // Derive the entitlement year EXACTLY as /summary does (from the requested
                // (year, month) relative to the immutable ResetMonth). VACATION/SPECIAL_HOLIDAY
                // reset in September, so e.g. a request for 2025-10 resolves to ferieår 2025.
                int entitlementYear;
                if (live.ResetMonth == 1)
                {
                    entitlementYear = year;
                }
                else
                {
                    entitlementYear = month >= live.ResetMonth ? year : year - 1;
                }

                var ferieaarStart = new DateOnly(entitlementYear, live.ResetMonth, 1);

                // Dated config effective at the ferieår start defines this year's annual_quota.
                // Fall back to the live row if no row was effective at year-start (e.g. this OK
                // version came into existence mid-year) — same fallback as /summary.
                var ec = await entitlementConfigRepo.GetByTypeAtAsync(
                    live.EntitlementType, agreementCode, user.OkVersion, ferieaarStart, ct)
                    ?? live;

                // MONTHLY_ACCRUAL-only: skip IMMEDIATE types entirely — no series for them.
                if (!string.Equals(ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal))
                    continue;

                // ── Full-ferieår curve: one point per month, month 0..11 from ferieaarStart ──
                // The as-of for each point is THAT month's month-END, derived purely from the
                // ferieår via DateOnly.AddMonths — NEVER wall-clock. earned is the FLAT,
                // fraction-INDEPENDENT day-count (S63 / ADR-031): EarnedToDate with the identity
                // 1.0m fraction yields 12 equal monthly steps of annualQuota / 12 (≈2,08 d/md for
                // VACATION). Each step is a non-negative increment, so the curve is monotonic
                // non-decreasing trivially; a part-timer's curve == a full-timer's (the S62
                // part-time bend is gone). annualQuota comes from the dated config effective at
                // ferieaarStart (constant across the curve). employmentStart is threaded through
                // AccrualMath, so a mid-ferieår hire's curve starts at 0 until the accrual start
                // (ADR-030 D6) and only ever rises.
                var points = new List<object>();
                for (var i = 0; i < 12; i++)
                {
                    var monthFirst = ferieaarStart.AddMonths(i);
                    var pointMonthEnd = new DateOnly(
                        monthFirst.Year, monthFirst.Month,
                        DateTime.DaysInMonth(monthFirst.Year, monthFirst.Month));

                    var earned = AccrualMath.EarnedToDate(
                        ec.AnnualQuota, 1.0m, ferieaarStart,
                        user.EmploymentStartDate, pointMonthEnd);

                    // The point matching the requested (year, month) is "now" — its earned value
                    // is byte-identical to /summary's earned for the same key (both call
                    // EarnedToDate with the same args + Math.Round(.,2); see header).
                    var isSelected = monthFirst.Year == year && monthFirst.Month == month;

                    points.Add(new
                    {
                        monthEnd = pointMonthEnd.ToString("yyyy-MM-dd"),
                        earned = Math.Round(earned, 2),
                        isSelected
                    });
                }

                DanishLabels.TryGetValue(ec.EntitlementType, out var label);

                series.Add(new
                {
                    type = ec.EntitlementType,
                    label = label ?? ec.EntitlementType,
                    annualQuota = ec.AnnualQuota,
                    entitlementYear,
                    ferieaarStart = ferieaarStart.ToString("yyyy-MM-dd"),
                    points
                });
            }

            return Results.Ok(new
            {
                employeeId,
                year,
                month,
                series
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // ── GET /api/balance/{employeeId}/year-overview?year=YYYY — S65 / TASK-6502 ──
        //
        // Read-only year-at-a-glance for the Direction E Årsoversigt: 6 current-balance tiles +
        // a months × categories matrix. Pure read; no events; no schema change. Every quantity
        // derives from existing primitives (ADR-028 per-day norm via DailyNormCalculator,
        // ADR-030/031 flat earned-to-date via AccrualMath, hours-based day-equivalents via the
        // shared StandardDayHours divisor). Deterministic: a pure function of (employeeId, year,
        // today, projections) — two identical requests byte-equal (the server `today` is the
        // ONLY non-projection input and it comes from the injected TimeProvider seam, NOT the
        // wall clock directly).
        MapYearOverview(app);

        return app;
    }

    // VACATION/SPECIAL_HOLIDAY reset in September; CARE_DAY/SENIOR_DAY are calendar-year. The
    // matrix renders these four categories (in this order) plus the Arbejdstid group (FE-side).
    private static readonly string[] YearOverviewCategoryTypes =
        { "VACATION", "SPECIAL_HOLIDAY", "CARE_DAY", "SENIOR_DAY" };

    private static void MapYearOverview(WebApplication app)
    {
        app.MapGet("/api/balance/{employeeId}/year-overview", async (
            string employeeId,
            int year,
            UserRepository userRepo,
            UserAgreementCodeRepository userAgreementCodeRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            AbsenceProjectionRepository absenceProjectionRepo,
            WorkTimeProjectionRepository workTimeProjectionRepo,
            IEventStore eventStore,
            IEmploymentProfileResolver profileResolver,
            ConfigResolutionService configResolver,
            StatsTid.Backend.Api.Services.DailyNormCalculator dailyNormCalculator,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // ── Auth — mirror /summary exactly (Step-0b Codex W3) ──
            // The gate runs the SAME OrgScopeValidator check on the SAME route employeeId every
            // downstream read below uses (no second id source): employee-self 403 for a foreign
            // id; out-of-scope leader / local-admin 403 via the negative org-scope branch.
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied", reason = "Employee can only access own data" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            if (year < 2000 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year" });

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // Server date — sole past/current/future + "Nu" authority. Derived ONCE per request
            // from the injected TimeProvider (no DateTime.Now/Today/UtcNow anywhere in this
            // handler). Tests override the provider in the WebApplicationFactory host.
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            // ── Header ──
            // agreementCode dated at today (user_agreement_codes, ADR-023 D3 graceful fallback to
            // the live cache); okVersion = OkVersionResolver at today (DISPLAY context only — the
            // matrix resolves OK per day / per entitlement-year-start below).
            var todayAgreementCode =
                await userAgreementCodeRepo.GetByUserIdAtAsync(employeeId, today, ct)
                ?? user.AgreementCode;
            var headerOkVersion = OkVersionResolver.ResolveVersion(today);

            // weeklyNormHours = merged-config WeeklyNorm × current PartTimeFraction; null if no
            // dated profile/config (graceful per ADR-023 D3 — a profile-less employee renders).
            decimal? weeklyNormHours = null;
            EmploymentProfile? todayProfile;
            try
            {
                todayProfile = await profileResolver.GetByEmployeeIdAtAsync(employeeId, today, ct);
            }
            catch (EmployeeProfileNotFoundException)
            {
                todayProfile = null;
            }
            if (todayProfile is not null)
            {
                var orgId = todayProfile.OrgId ?? user.PrimaryOrgId;
                var mergedConfig = await configResolver.ResolveAsync(
                    orgId, todayProfile.AgreementCode, OkVersionResolver.ResolveVersion(today),
                    todayProfile.Position, ct);
                weeklyNormHours = Math.Round(
                    mergedConfig.WeeklyNormHours * todayProfile.PartTimeFraction, 2);
            }

            // ── All absences for the employee (one read), mapped to entitlement type +
            // day-equivalents. The mapping is MANDATORY via the shared map (Step-0b Codex W1):
            // projection rows carry the ABSENCE type (e.g. SPECIAL_HOLIDAY_ALLOWANCE), not the
            // entitlement type. We need the full stream because the entitlement-year straddle
            // reaches into September of the prior calendar year. ──
            //
            // S66 / TASK-6605 (ADR-032 D2): day-equivalents are the AUTHORITATIVE recorded
            // per-absence feriedage computed at booking — NO re-derivation here. The booking path
            // (D1) values each row once and persists it to absences_projection.feriedage; this
            // read sums those recorded values so afholdt/saldo never recompute (replay-deterministic,
            // no drift, and post-revaluation (D4) reads reflect the replaced values automatically).
            // Null-coalesce to ROUND(Hours/7.4, 4, AwayFromZero) for pre-S66 / not-yet-backfilled
            // rows — byte-identical to the init.sql + ProjectionBackfillService backfill convention
            // in force when those rows were written (ADR-032 D2). Full-time 7.4/7.4=1.0 == old Hours/7.4.
            var allAbsences = await absenceProjectionRepo.GetByEmployeeAsync(employeeId, ct);
            var mappedAbsences = allAbsences
                .Select(a => (
                    EntitlementType: StatsTid.Backend.Api.Services.EntitlementMapping.GetEntitlementType(a.AbsenceType),
                    a.Date,
                    DayEquivalents: a.Feriedage ?? Math.Round(
                        a.Hours / StatsTid.Backend.Api.Services.EntitlementMapping.StandardDayHours,
                        4, MidpointRounding.AwayFromZero)))
                .Where(a => a.EntitlementType is not null)
                .Select(a => (EntitlementType: a.EntitlementType!, a.Date, a.DayEquivalents))
                .ToList();

            // Local helper: day-equivalents consumed for an entitlement type within [from, to]
            // (inclusive), summed from the mapped projection rows.
            decimal AfholdtWithin(string entitlementType, DateOnly from, DateOnly to)
                => mappedAbsences
                    .Where(a => a.EntitlementType == entitlementType && a.Date >= from && a.Date <= to)
                    .Sum(a => a.DayEquivalents);

            // ── Months: workedHours + normHours + diff ──
            // workedHours = Σ work_time_projection (intervals + manual) in the calendar month
            // (ADR-028). normHours = Σ DailyNormCalculator per-day norms; null if ANY norm-bearing
            // day resolves null. diff = worked − norm for months ≤ today's month; null for future.
            var months = new List<object>(12);
            for (var m = 1; m <= 12; m++)
            {
                var daysInMonth = DateTime.DaysInMonth(year, m);
                var monthStart = new DateOnly(year, m, 1);
                var monthEnd = new DateOnly(year, m, daysInMonth);

                var workTimeRows = await workTimeProjectionRepo.GetByEmployeeAndDateRangeAsync(
                    employeeId, monthStart, monthEnd, ct);
                var workedHours = Math.Round(
                    workTimeRows.Sum(w => SumIntervalHours(w.Intervals) + w.ManualHours), 2);

                var normEntries = await dailyNormCalculator.ComputeRangeAsync(
                    employeeId, monthStart, monthEnd, user.PrimaryOrgId, ct);
                // null if ANY norm-bearing day (weekday) resolves null — ANNUAL_ACTIVITY or no
                // dated profile (e.g. months before employment_start_date). Weekends contribute 0.
                decimal? normHours = normEntries.Any(n => n.Hours is null)
                    ? null
                    : Math.Round(normEntries.Sum(n => n.Hours ?? 0m), 2);

                // diff only for months at/earlier than today's month of the selected year; future
                // months (and future years) → null (no fabricated performance). normHours null ⇒
                // diff null mechanically.
                bool isPastOrCurrent = year < today.Year || (year == today.Year && m <= today.Month);
                decimal? diff = (isPastOrCurrent && normHours is not null)
                    ? Math.Round(workedHours - normHours.Value, 2)
                    : null;

                months.Add(new
                {
                    month = m,
                    workedHours,
                    normHours,
                    diff
                });
            }

            // ── S65 Step-7a fix: per-ferieår dated agreement-code anchoring ──
            // The OK version is already anchored at each entitlement-year start below, but the
            // AGREEMENT CODE operand of the dated entitlement-config reads must ALSO be the one in
            // effect at that ferieår start — NOT today's. When an employee changes agreement (e.g.
            // AC→HK) the earlier AC ferieår must be valued with AC quotas/carryover rules, not HK's.
            // Mirrors the existing header dated read (user_agreement_codes, ADR-023 D3 graceful
            // fallback to the live cache). For a single-agreement employee every date resolves to
            // the SAME code today resolves to, so this path is byte-identical to the prior
            // todayAgreementCode reads (ALL current tests). Cached per request: a 12-month loop over
            // ≤3 distinct ferieår starts must not issue 12 repo calls per category.
            var agreementByDate = new Dictionary<DateOnly, string>();
            async Task<string> ResolveAgreementAtAsync(DateOnly asOf)
            {
                if (agreementByDate.TryGetValue(asOf, out var cached))
                    return cached;
                var resolved = await userAgreementCodeRepo.GetByUserIdAtAsync(employeeId, asOf, ct)
                    ?? user.AgreementCode;
                agreementByDate[asOf] = resolved;
                return resolved;
            }

            // Fallback live (open) config for a (type, agreement) pair OTHER than today's. Only
            // consulted when the dated read misses AND the per-ferieår agreement differs from
            // today's — falling back to today's-agreement liveConfig in that case would re-introduce
            // the cross-agreement bug. Cached per (type, agreement) to bound reads. Null result is
            // cached too (so a missing per-agreement live row is not re-queried per month).
            var liveByTypeAgreement = new Dictionary<(string Type, string Agreement), EntitlementConfig?>();
            async Task<EntitlementConfig?> ResolveFallbackLiveAsync(string type, string agreement)
            {
                var key = (type, agreement);
                if (liveByTypeAgreement.TryGetValue(key, out var cached))
                    return cached;
                var resolved = await entitlementConfigRepo.GetCurrentOpenAsync(
                    type, agreement, user.OkVersion, ct);
                liveByTypeAgreement[key] = resolved;
                return resolved;
            }

            // Dated entitlement-config read anchored at the per-ferieår agreement code. OK version
            // stays the year-start-anchored value passed in (already correct). Graceful fallback
            // chain (ADR-023 D3, never 500): dated row → if the per-ferieår agreement == today's,
            // the already-fetched live row for this type (liveConfig) → otherwise the live open row
            // of the per-ferieår agreement → and only if THAT is null, liveConfig.
            async Task<EntitlementConfig> ResolveDatedConfigAsync(
                string type, DateOnly ferieaarStart, string okVersion, EntitlementConfig liveConfig)
            {
                var agreement = await ResolveAgreementAtAsync(ferieaarStart);
                var dated = await entitlementConfigRepo.GetByTypeAtAsync(
                    type, agreement, okVersion, ferieaarStart, ct);
                if (dated is not null)
                    return dated;
                if (string.Equals(agreement, todayAgreementCode, StringComparison.Ordinal))
                    return liveConfig;
                return await ResolveFallbackLiveAsync(type, agreement) ?? liveConfig;
            }

            // ── Categories: saldo[12] + afholdt[12] + transferable + boundaryMonth ──
            var categories = new List<object>(YearOverviewCategoryTypes.Length);
            foreach (var type in YearOverviewCategoryTypes)
            {
                // Resolve the live (open) config to discover ResetMonth (immutable per natural key,
                // ADR-021 Q1) — the year-start anchors all dated entitlement-config reads below.
                var liveConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                    type, todayAgreementCode, user.OkVersion, ct);

                // S65 Step-7a cycle-2 fix C2-1 — historical-agreement resolution BEFORE the graceful
                // empty branch. When TODAY's agreement has no config for this type, the prior code
                // took the empty branch for EVERY selected year — even years lived under a PRIOR
                // agreement that HAS configs (e.g. AC→unconfigured: the AC history vanished). Before
                // giving up, attempt the SELECTED YEAR's own agreement (the per-ferieår dated reads
                // inside the loop already resolve each ferieår's agreement, so months under the
                // configured historical agreement value correctly). Months that fall under the
                // configless TODAY agreement fall through ResolveDatedConfigAsync's chain to this
                // altLive terminal — ACCEPTED best-effort graceful behavior (better than empty;
                // never 500). Only activates when liveConfig is null AND the year-start agreement
                // DIFFERS from today's, so an employee whose today-agreement HAS configs is
                // byte-identical; the profile-less no-config PIN (same code at every date ⇒
                // yearStartAgreement == todayAgreementCode) still hits the empty branch unchanged.
                if (liveConfig is null)
                {
                    // S65 Step-7a cycle-3 fix C3-1 — probe a candidate anchor SET, not just Jan-1.
                    // The cycle-2 C2-1 fix probed ONLY new DateOnly(year, 1, 1). That missed the case
                    // where today's (and Jan-1's) agreement is configless, yet the September-reset
                    // ferieår covering Jan–Aug of the selected year BEGAN (Sep 1 of year−1) under a
                    // CONFIGURED agreement (e.g. an Oct-2025 switch to a configless code, viewing 2026):
                    // the single Jan-1 probe resolved the configless code → empty branch → Jan–Aug
                    // saldo/afholdt wrongly null/zero. The candidate set covers EVERY ferieår start that
                    // can intersect the selected calendar year under the system's two reset geometries
                    // (the only ResetMonth values seeded are 1 and 9): the calendar ferieår (year-01-01),
                    // the Sep ferieår whose Jan–Aug tail falls in the selected year (year−1-09-01), and
                    // the Sep ferieår whose Sep–Dec head falls in the selected year (year-09-01). All
                    // reads go through the EXISTING cached helpers, so this adds ≤2 extra cached lookups
                    // (Jan-1 is shared with the per-ferieår loop; the two Sep anchors are the only new
                    // keys). RESIDUAL (documented, accepted): this bootstraps resetMonth + the
                    // ResolveDatedConfigAsync fallback terminal from ANY configured intersecting ferieår;
                    // an exotic history with MULTIPLE distinct configured agreements across the candidates
                    // uses the FIRST hit (best-effort graceful, never 500).
                    var probeAnchors = new[]
                    {
                        new DateOnly(year, 1, 1),
                        new DateOnly(year - 1, 9, 1),
                        new DateOnly(year, 9, 1)
                    };
                    foreach (var anchor in probeAnchors)
                    {
                        var anchorAgreement = await ResolveAgreementAtAsync(anchor);
                        if (string.Equals(anchorAgreement, todayAgreementCode, StringComparison.Ordinal))
                            continue;
                        var altLive = await ResolveFallbackLiveAsync(type, anchorAgreement);
                        if (altLive is not null)
                        {
                            liveConfig = altLive; // resetMonth discovery + ResolveDatedConfigAsync fallback terminal
                            break;
                        }
                    }
                }

                if (liveConfig is null)
                {
                    // No config for this type under the employee's agreement/OK (and none under the
                    // selected year's agreement either) — graceful empty row (nulls/zeros, never a
                    // 500; ADR-023 D3).
                    categories.Add(new
                    {
                        type,
                        label = DanishLabels.TryGetValue(type, out var lbl0) ? lbl0 : type,
                        saldo = new decimal?[12],
                        afholdt = new decimal[12],
                        transferable = 0m,
                        boundaryMonth = 12
                    });
                    continue;
                }
                var resetMonth = liveConfig.ResetMonth;

                // saldo[m]: end-of-month remaining within the entitlement year CONTAINING month m.
                // saldo = EarnedToDate(quota, 1.0, ferieaarStart, employmentStart, monthEnd)
                //         + carryoverIn − cumulative afholdt(this type) within the ferieår up to
                //         monthEnd. Sep shows the reset sawtooth for ResetMonth-9 types.
                var saldo = new decimal?[12];
                var afholdt = new decimal[12];
                for (var m = 1; m <= 12; m++)
                {
                    var daysInMonth = DateTime.DaysInMonth(year, m);
                    var monthStart = new DateOnly(year, m, 1);
                    var monthEnd = new DateOnly(year, m, daysInMonth);

                    // afholdt[m] = day-equivalents mapped to this type within calendar month m.
                    afholdt[m - 1] = Math.Round(AfholdtWithin(type, monthStart, monthEnd), 2);

                    // Entitlement year + its start for the ferieår containing month m.
                    var entYear = ResolveEntitlementYear(monthStart, resetMonth);
                    var ferieaarStart = new DateOnly(entYear, resetMonth, 1);

                    // Config dated at the entitlement-year START (ADR-021 D2, same as /summary):
                    // OK resolved at the year-start, AND the agreement code resolved at the
                    // year-start (Step-7a fix — historical ferieår must not be valued with today's
                    // agreement), then the dated config read with the graceful fallback chain.
                    var entOkVersion = OkVersionResolver.ResolveVersion(ferieaarStart);
                    var ec = await ResolveDatedConfigAsync(type, ferieaarStart, entOkVersion, liveConfig);

                    var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                        employeeId, type, entYear, ct);
                    var carryoverIn = balance?.CarryoverIn ?? 0m;

                    var earned = string.Equals(ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
                        ? AccrualMath.EarnedToDate(
                            ec.AnnualQuota, 1.0m, ferieaarStart, user.EmploymentStartDate, monthEnd)
                        : ec.AnnualQuota; // IMMEDIATE: full quota earned up-front.

                    var cumulativeAfholdt = AfholdtWithin(type, ferieaarStart, monthEnd);
                    saldo[m - 1] = Math.Round(earned + carryoverIn - cumulativeAfholdt, 2);
                }

                // transferable: COMPUTED at the type's model boundary, EMITTED at boundaryMonth=12.
                // ResetMonth-9 → 31 Aug of the selected year (closes ferieår year-1, spanning
                // Sep year-1 .. Aug year). Calendar types → 31 Dec of the selected year (ferieår
                // year). carryoverIn/used/planned are the CLOSED-boundary-ferieår balances (the
                // SAME ferieår as earnedAtBoundary — NOT the live current-ferieår row; the
                // ferieRemaining tile uses the live balances, a different quantity). carryoverMax
                // is year-start dated.
                DateOnly closedFerieaarStart;
                DateOnly boundaryDate;
                if (resetMonth == 1)
                {
                    closedFerieaarStart = new DateOnly(year, 1, 1);
                    boundaryDate = new DateOnly(year, 12, 31);
                }
                else
                {
                    closedFerieaarStart = new DateOnly(year - 1, resetMonth, 1);
                    boundaryDate = closedFerieaarStart.AddYears(1).AddDays(-1); // 31 Aug of selected year
                }
                var closedEntYear = closedFerieaarStart.Year;
                var closedOkVersion = OkVersionResolver.ResolveVersion(closedFerieaarStart);
                // Step-7a fix: agreement code resolved at the CLOSED ferieår start (same dated
                // anchoring + graceful fallback as the monthly saldo read above).
                var closedConfig = await ResolveDatedConfigAsync(
                    type, closedFerieaarStart, closedOkVersion, liveConfig);
                var closedBalance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                    employeeId, type, closedEntYear, ct);
                var closedCarryoverIn = closedBalance?.CarryoverIn ?? 0m;
                var closedUsed = closedBalance?.Used ?? 0m;
                var closedPlanned = closedBalance?.Planned ?? 0m;

                var earnedAtBoundary =
                    string.Equals(closedConfig.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
                        ? AccrualMath.EarnedToDate(
                            closedConfig.AnnualQuota, 1.0m, closedFerieaarStart,
                            user.EmploymentStartDate, boundaryDate)
                        : closedConfig.AnnualQuota;

                var transferableRaw = earnedAtBoundary + closedCarryoverIn - closedUsed - closedPlanned;
                var transferable = Math.Round(
                    Math.Min(Math.Max(0m, transferableRaw), closedConfig.CarryoverMax), 2);

                categories.Add(new
                {
                    type,
                    label = DanishLabels.TryGetValue(type, out var lbl) ? lbl : type,
                    saldo,
                    afholdt,
                    transferable,
                    boundaryMonth = 12
                });
            }

            // ── Tiles (the designed 6 — NO 7th tile for Feriefridage; matrix-only) ──
            // Live "current" balances anchored at TODAY (the entitlement year containing today).
            // flexBalance = latest FlexBalanceUpdated (same read as /summary).
            var streamId = $"employee-{employeeId}";
            var allEvents = await eventStore.ReadStreamAsync(streamId, ct);
            var flexBalance = allEvents.OfType<FlexBalanceUpdated>().LastOrDefault()?.NewBalance ?? 0m;

            // Eligibility (display affordances). childSick = S59 opt-in eligibility as of today;
            // senior = birth_date + config min_age as of today.
            var childSickEligible = (await eligibilityRepo
                .GetEligibleAsOfAsync(employeeId, "CHILD_SICK", today, ct)).Eligible;

            var seniorLiveConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                "SENIOR_DAY", todayAgreementCode, user.OkVersion, ct);
            int? seniorMinAge = seniorLiveConfig is not null
                ? (await entitlementConfigRepo.GetByTypeAtAsync(
                        "SENIOR_DAY", todayAgreementCode,
                        OkVersionResolver.ResolveVersion(new DateOnly(
                            ResolveEntitlementYear(today, seniorLiveConfig.ResetMonth),
                            seniorLiveConfig.ResetMonth, 1)),
                        new DateOnly(
                            ResolveEntitlementYear(today, seniorLiveConfig.ResetMonth),
                            seniorLiveConfig.ResetMonth, 1), ct))?.MinAge ?? seniorLiveConfig.MinAge
                : null;
            // Eligible when no age gate is configured, OR the employee meets it as of today.
            var seniorDayEligible = seniorMinAge is null
                || (user.BirthDate is { } dob && AgeAsOf(dob, today) >= seniorMinAge.Value);

            // Live "remaining" for the four entitlement tiles. Local helper computes
            // earned(asOf today) + carryoverIn − used − planned for the current ferieår of a type.
            async Task<decimal?> CurrentRemainingAsync(string type)
            {
                var live = await entitlementConfigRepo.GetCurrentOpenAsync(
                    type, todayAgreementCode, user.OkVersion, ct);
                if (live is null) return null;
                var entYear = ResolveEntitlementYear(today, live.ResetMonth);
                var ferieaarStart = new DateOnly(entYear, live.ResetMonth, 1);
                var ec = await entitlementConfigRepo.GetByTypeAtAsync(
                    type, todayAgreementCode, OkVersionResolver.ResolveVersion(ferieaarStart),
                    ferieaarStart, ct) ?? live;
                var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                    employeeId, type, entYear, ct);
                var carryoverIn = balance?.CarryoverIn ?? 0m;
                var used = balance?.Used ?? 0m;
                var planned = balance?.Planned ?? 0m;
                var earned = string.Equals(ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
                    ? AccrualMath.EarnedToDate(
                        ec.AnnualQuota, 1.0m, ferieaarStart, user.EmploymentStartDate, today)
                    : ec.AnnualQuota;
                return Math.Round(earned + carryoverIn - used - planned, 2);
            }

            var ferieRemaining = await CurrentRemainingAsync("VACATION");
            var careDayRemaining = await CurrentRemainingAsync("CARE_DAY");
            var seniorDayRemaining = seniorDayEligible ? await CurrentRemainingAsync("SENIOR_DAY") : null;
            var childSickRemaining = childSickEligible ? await CurrentRemainingAsync("CHILD_SICK") : null;

            // sickDaysYtd: distinct SICK_DAY dates in the current calendar year (not quota-gated →
            // distinct-date is the right primitive). Uses the raw absence_type (SICK_DAY maps to a
            // null entitlement type, so it is intentionally NOT in mappedAbsences).
            var sickDaysYtd = allAbsences
                .Where(a => string.Equals(a.AbsenceType, "SICK_DAY", StringComparison.Ordinal)
                            && a.Date.Year == today.Year)
                .Select(a => a.Date)
                .Distinct()
                .Count();

            return Results.Ok(new
            {
                employeeId,
                year,
                today = today.ToString("yyyy-MM-dd"),
                header = new
                {
                    employeeName = user.DisplayName,
                    agreementCode = todayAgreementCode,
                    okVersion = headerOkVersion,
                    weeklyNormHours
                },
                tiles = new
                {
                    flexBalance,
                    ferieRemaining,
                    careDayRemaining,
                    seniorDayRemaining,
                    sickDaysYtd,
                    childSickRemaining,
                    childSickEligible,
                    seniorDayEligible
                },
                months,
                categories
            });
        }).RequireAuthorization("EmployeeOrAbove");
    }

    // ── S65 / TASK-6502 helpers (BalanceEndpoints-local) ──

    /// <summary>
    /// Resolve the entitlement year for a date given a reset month: month ≥ resetMonth ⇒
    /// date.Year, else date.Year − 1 (mirrors the SkemaEndpoints two-step pattern).
    /// </summary>
    private static int ResolveEntitlementYear(DateOnly date, int resetMonth)
        => date.Month >= resetMonth ? date.Year : date.Year - 1;

    /// <summary>
    /// Pure integer-age computation as-of a date (mirrors the SkemaEndpoints senior-gate
    /// helper). Completed years lived as-of <paramref name="asOf"/> (this year's birthday must
    /// have occurred on/before <paramref name="asOf"/>). Deterministic; no wall clock.
    /// </summary>
    private static int AgeAsOf(DateOnly birthDate, DateOnly asOf)
    {
        var age = asOf.Year - birthDate.Year;
        if (asOf < birthDate.AddYears(age))
            age--;
        return age;
    }

    /// <summary>
    /// Sum the hours represented by a day's work intervals. Mirrors the frontend
    /// <c>calcIntervalHours</c> + the Skema work-time validation: only positive-duration
    /// intervals count; total seconds rounded to 2 decimals. "HH:MM"/"HH:MM:SS" wall-clock.
    /// </summary>
    private static decimal SumIntervalHours(IReadOnlyList<WorkInterval> intervals)
    {
        var totalSeconds = 0;
        foreach (var iv in intervals)
        {
            if (TryParseTimeToSeconds(iv.Start, out var startSec)
                && TryParseTimeToSeconds(iv.End, out var endSec)
                && endSec > startSec)
            {
                totalSeconds += endSec - startSec;
            }
        }
        return Math.Round(totalSeconds / 3600m, 2);
    }

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
}
