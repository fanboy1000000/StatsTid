using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
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

    /// <summary>
    /// S60 / TASK-6005 — Backend-local mirror of the rule engine's pure
    /// <c>AccrualCalculator.EarnedToDate</c> (ADR-030). The authoritative math lives in the
    /// Rule Engine, but PAT-005 forbids the Backend from referencing the RuleEngine assembly
    /// directly (the boundary is HTTP); the Balance summary is a pure read with no
    /// validate-entitlement call, so the earned-to-date figure is computed locally from the
    /// SAME formula. MUST stay byte-identical to <c>AccrualCalculator.EarnedToDate</c>: exact
    /// fractional days, accrual begins at <c>max(ferieaarStart, employmentStart)</c>, months
    /// elapsed = inclusive whole month-boundaries crossed, clamped to [0, 12].
    /// </summary>
    private static decimal EarnedToDate(
        decimal annualQuota,
        decimal partTimeFraction,
        DateOnly ferieaarStart,
        DateOnly? employmentStart,
        DateOnly asOf)
    {
        var accrualStart = ferieaarStart;
        if (employmentStart.HasValue && employmentStart.Value > accrualStart)
            accrualStart = employmentStart.Value;

        var monthsElapsed = MonthIndex(asOf) - MonthIndex(accrualStart) + 1;
        if (monthsElapsed <= 0)
            return 0m;
        if (monthsElapsed > 12)
            monthsElapsed = 12;

        return annualQuota * partTimeFraction * monthsElapsed / 12m;
    }

    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month;

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

            // S60 / TASK-6005 — partTimeFraction is now sourced from the dated employment profile
            // at month-end (resolved above, graceful ?? 1.0m), replacing the prior hard-coded 1.0.

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

                // ── S60 / TASK-6005 — earned-to-date for MONTHLY_ACCRUAL types ──
                // VACATION + SPECIAL_HOLIDAY now accrue monthly (ADR-030): the AVAILABLE ("rest")
                // figure must reflect what is EARNED-to-date (optjent), not the full annual quota
                // the moment the ferieår starts. asOf = the requested MONTH-END (the Balance seam
                // anchor — the same anchor the Skema validation uses at firstAbsenceDate so the two
                // seams agree for the same as-of date). ferieaarStart = the entitlement-year start
                // (reset_month). employmentStart = HR-managed User.EmploymentStartDate (null ⇒
                // full-ferieår; never fail-closed, ADR-030). partTimeFraction = the dated month-end
                // fraction resolved above. IMMEDIATE types keep their full quota as "earned".
                var isMonthlyAccrual = string.Equals(
                    ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal);

                decimal earned;
                if (isMonthlyAccrual)
                {
                    earned = EarnedToDate(
                        ec.AnnualQuota, partTimeFraction, entitlementYearStart,
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

        return app;
    }
}
