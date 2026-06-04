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

        return app;
    }
}
