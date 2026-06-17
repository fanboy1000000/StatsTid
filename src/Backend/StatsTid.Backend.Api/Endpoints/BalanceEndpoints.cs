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
        ["SPECIAL_HOLIDAY"] = "Særlige feriedage",
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
            VacationSettlementRepository settlementRepo,
            DatedEntitlementConfigResolverFactory datedConfigResolverFactory,
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
            // S81 / TASK-8102 (R2) — Step 1 is anchored on the OPERATION DATE (monthEnd, the same
            // as-of the per-type year derivation + earned-to-date computation use), on BOTH
            // dimensions: OkVersionResolver.ResolveVersion(monthEnd) + the agreement dated at
            // monthEnd — NOT the live user.OkVersion + the month-start agreement. ResetMonth is
            // frozen WITHIN a natural key by the TASK-3007 admin-scope 422 guard (ADR-021 D5), so
            // reading the live-open row of the operation-date key is safe; for the unconstrained
            // IMMEDIATE calendar types whose reset_month CAN diverge across keys after an
            // OK/agreement change, the operation-date key is the correct-by-construction source of
            // the consumed entitlement year (SPRINT-81 §Design decision — re-derivation).
            //
            // EffectiveTo IS NULL filter is load-bearing: GetByAgreementAsync returns ALL rows
            // (open + closed predecessors). Post-supersession the bulk read contains 2 rows per
            // natural key — without this filter the loop double-emits each entitlement and
            // vacationDaysEntitlement gets overwritten with whichever row is visited last.
            var opDateOkVersion = OkVersionResolver.ResolveVersion(monthEnd);
            var opDateAgreement = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, monthEnd, ct) ?? user.AgreementCode;
            var liveConfigs = (await entitlementConfigRepo.GetByAgreementAsync(
                opDateAgreement, opDateOkVersion, ct))
                .Where(c => c.EffectiveTo is null);

            // S81 / TASK-8102 (R1) — the shared GRACEFUL dated-config resolver (year-overview path).
            // The Step-2 quota read below resolves the year-start-dated OK + year-start-dated
            // agreement through it. todayAgreementCode operand = opDateAgreement (the agreement the
            // live rows were read under): when a year-start agreement equals it, the resolver returns
            // the passed live row; when it differs, the historical agreement's own row is used.
            var datedConfigResolver = datedConfigResolverFactory.Create(
                employeeId, opDateOkVersion, user.AgreementCode, opDateAgreement);

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
                // S80 / TASK-8001 (R2/R10) — the entitlement (accrual) year + its accrual-window start
                // now come from the SHARED EntitlementPeriodResolver, anchored at the requested month-end
                // (the same as-of the earned-to-date computation below uses). VACATION + every other type
                // are BEHAVIOR-IDENTICAL to the old inline keying (reset_month==1 → calendar year; else
                // "Month ≥ reset_month"). SPECIAL_HOLIDAY uses the two-calendar-year taking-window mapping
                // (1 May Y+1 .. 30 Apr Y+2) and a 1-Jan accrual anchor (R1), neither expressible by raw
                // reset_month.
                var period = EntitlementPeriodResolver.Resolve(live.EntitlementType, live.ResetMonth, monthEnd);
                var entitlementYear = period.EntitlementYear;

                // Step 2 (S81 / TASK-8102 R1): dated read at the entitlement-year-START key — the
                // year-start-dated OK version AND the year-start-dated agreement — via the shared
                // resolver. This is the config row that was IN EFFECT when the current entitlement
                // year started; its annual_quota / carryover_max define this year's quota for display.
                // Per-type because each type may resolve to a DIFFERENT year-start (different
                // reset_month / accrual geometry). The resolver encapsulates the ADR-023 D3 graceful
                // fallback (never 500): a dated miss for a year-start OK that came into existence
                // mid-year, or a year-start agreement with no dated row, falls through to the live
                // row so the entitlement still appears in the summary with the current quota values.
                var entitlementYearStart = period.AccrualStart;
                var ec = await datedConfigResolver.ResolveDatedConfigAsync(
                    live.EntitlementType, entitlementYearStart,
                    OkVersionResolver.ResolveVersion(entitlementYearStart), live, ct);

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

                // ── S68 / TASK-6807 (ADR-033 D6 clarification) — settled-year reader special-case ──
                // A SETTLED / PENDING_REVIEW entitlement-year reads the RECORDED disposition off the
                // ACTIVE (non-REVERSED) vacation_settlements row, NOT a live recompute (determinism;
                // ADR-033 D3/D6). The per-bucket day-counts (transfer_days §21 / payout_days §24 /
                // forfeit_days §34) on the row are pure functions of the immutable settle-time
                // snapshot, so reading them IS reading the snapshot disposition. `used` is NEVER
                // mutated by settlement (ADR-032 D2); only the displayed `remaining` changes. The
                // lookup keys on (employee, type, entitlementYear) — for any type with no settlement
                // (every non-VACATION type pre-slice-2, and any unsettled VACATION year) GetActiveAsync
                // returns null ⇒ the unsettled path below, byte-identical to prior behavior.
                var settlement = await settlementRepo.GetActiveAsync(
                    employeeId, ec.EntitlementType, entitlementYear, ct);

                decimal displayedRemaining;
                object? settlementInfo;
                if (settlement is null)
                {
                    // Unsettled — unchanged.
                    displayedRemaining = Math.Round(remaining, 2);
                    settlementInfo = null;
                }
                else if (string.Equals(settlement.SettlementState, "SETTLED", StringComparison.Ordinal))
                {
                    // SETTLED — the year is fully disposed: remaining reads 0, the disposition is the
                    // recorded §21/§24/§34 partition (the FULL partition; a SUPERSET of the D9 expiring
                    // figure, which is the §34 forfeit bucket alone — ADR-033 D6 clarification).
                    //
                    // S71 / TASK-7103 (SPRINT-71 R5) — the termination CLAIM disposition renders
                    // DISTINCTLY: a WAIVED (slice-3b waive-in-full of the §7-shaped over-taken claim)
                    // TERMINATION row carries reviewDisposition = "WAIVED" +
                    // claimDispositionDays = the waived quantity, with forfeitDays CLEARED to 0 by
                    // the resolve verb — the claim never reads as §34 forfeiture and never as a
                    // positive pending remainder (remaining stays 0; the SETTLED zero-bucket
                    // rendering for unwaived rows is unchanged: both new fields are null there).
                    displayedRemaining = 0m;
                    settlementInfo = new
                    {
                        state = settlement.SettlementState,
                        transferDays = settlement.TransferDays,   // §21 — to next-year carryover_in
                        payoutDays = settlement.PayoutDays,       // §24 — auto-payout (day-count line, S69)
                        forfeitDays = settlement.ForfeitDays,     // §34 — == the D9 expiring bucket
                        forfeitPending = false,
                        reviewDisposition = settlement.ReviewDisposition,           // FORFEIT / WAIVED / null
                        claimDispositionDays = settlement.ClaimDispositionDays      // non-null iff MODREGNING/WAIVED
                    };
                }
                else
                {
                    // PENDING_REVIEW — the auto-resolved §21/§24 buckets are disposed, but the §34
                    // forfeit_days remainder is UNRESOLVED (a human must adjudicate §34-vs-§22, ADR-033
                    // D10). It is shown as STILL PENDING (flagged), NOT counted as 0 (Codex W): the
                    // displayed remaining is exactly that unresolved §34 remainder. (S71: the two
                    // claim fields are surfaced for shape-uniformity with the SETTLED branch — a
                    // PENDING_REVIEW row can carry reviewDisposition only as the DEFER marker, and
                    // claimDispositionDays is always null here by the 7100 pairing CHECK.)
                    displayedRemaining = Math.Round(settlement.ForfeitDays, 2);
                    settlementInfo = new
                    {
                        state = settlement.SettlementState,
                        transferDays = settlement.TransferDays,
                        payoutDays = settlement.PayoutDays,
                        forfeitDays = settlement.ForfeitDays,
                        forfeitPending = true,
                        reviewDisposition = settlement.ReviewDisposition,           // DEFER / null
                        claimDispositionDays = settlement.ClaimDispositionDays      // always null (CHECK)
                    };
                }

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
                    remaining = displayedRemaining,
                    entitlementYear,
                    // null for unsettled years (every current consumer sees the same shape + a new
                    // optional field); the recorded disposition for a SETTLED/PENDING_REVIEW year.
                    settlement = settlementInfo
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
            DatedEntitlementConfigResolverFactory datedConfigResolverFactory,
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

            // The as-of for the entitlement-year keying — the requested month's end (matches /summary).
            // This is the OPERATION DATE that anchors the Step-1 reset_month read (S81 / TASK-8102 R2).
            var seriesAsOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            // S81 / TASK-8102 (R2) — Step 1 is anchored on the OPERATION DATE (seriesAsOf = monthEnd),
            // on BOTH dimensions: OkVersionResolver.ResolveVersion(seriesAsOf) + the agreement dated at
            // seriesAsOf — NOT the live user.OkVersion + the month-start agreement. ResetMonth is frozen
            // WITHIN a natural key (ADR-021 D5), so reading the live-open row of the operation-date key
            // is safe (same re-derivation rationale as /summary). EffectiveTo IS NULL filter is
            // load-bearing (post-supersession the bulk read holds 2 rows per natural key; same rationale
            // as /summary).
            var opDateOkVersion = OkVersionResolver.ResolveVersion(seriesAsOf);
            var opDateAgreement = await userAgreementCodeRepo.GetByUserIdAtAsync(
                employeeId, seriesAsOf, ct) ?? user.AgreementCode;
            var liveConfigs = (await entitlementConfigRepo.GetByAgreementAsync(
                opDateAgreement, opDateOkVersion, ct))
                .Where(c => c.EffectiveTo is null);

            // S81 / TASK-8102 (R1) — the shared GRACEFUL dated-config resolver (year-overview path).
            // todayAgreementCode operand = opDateAgreement (the agreement the live rows were read under).
            var datedConfigResolver = datedConfigResolverFactory.Create(
                employeeId, opDateOkVersion, user.AgreementCode, opDateAgreement);

            var series = new List<object>();

            foreach (var live in liveConfigs)
            {
                // S80 / TASK-8001 (R2/R10) — derive the entitlement (accrual) year + accrual-window
                // start via the shared EntitlementPeriodResolver (same as /summary), anchored at the
                // requested month-end. VACATION + every reset_month type are BEHAVIOR-IDENTICAL
                // (reset_month==1 → calendar year; else "Month ≥ reset_month"). SPECIAL_HOLIDAY uses
                // the calendar accrual (1 Jan) + two-calendar-year taking-window mapping: a query in
                // the taking window resolves to the prior accrual year, and the 12-month curve below
                // therefore renders that accrual year's Jan–Dec earning progression. (Consequence: for
                // SPECIAL_HOLIDAY the isSelected month — in the TAKING window — may not coincide with
                // any curve point, which lies in the ACCRUAL window; that is correct — the curve shows
                // when the days were EARNED, the query is when you LOOK.)
                var period = EntitlementPeriodResolver.Resolve(live.EntitlementType, live.ResetMonth, seriesAsOf);
                var entitlementYear = period.EntitlementYear;
                var ferieaarStart = period.AccrualStart;

                // Step 2 (S81 / TASK-8102 R1): dated config at the entitlement-year-START key — the
                // year-start-dated OK version AND the year-start-dated agreement — via the shared
                // resolver, defines this year's annual_quota. The resolver's ADR-023 D3 graceful chain
                // falls back to the live row if no row was effective at year-start (e.g. this OK version
                // came into existence mid-year) — same fallback as /summary.
                var ec = await datedConfigResolver.ResolveDatedConfigAsync(
                    live.EntitlementType, ferieaarStart,
                    OkVersionResolver.ResolveVersion(ferieaarStart), live, ct);

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
            VacationSettlementRepository settlementRepo,
            DatedEntitlementConfigResolverFactory datedConfigResolverFactory,
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
                // S70 / TASK-7003 (SPRINT-70 R9c allowlist) — terminated-INCLUSIVE validator:
                // HR reviewing a deactivated leaver's PENDING_REVIEW settlement needs this
                // read-only overview (the S68 B2 fix). For an ACTIVE target the new validator is
                // behavior-identical to ValidateEmployeeAccessAsync's non-Employee path (in-scope
                // leader access preserved); a TERMINATED target additionally requires HROrAbove.
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            if (year < 2000 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year" });

            // S70 / TASK-7003 (R9c; re-hardened per Step-5a cycle-3, Codex cycle-2 B2) —
            // read-first-REVALIDATE body read, extracted to ReadYearOverviewTargetAsync (below)
            // so the regression suite can pin the exact production selection deterministically.
            //
            //   SELF (employeeId == actor.ActorId): unconditional terminated-INCLUSIVE read —
            //   a terminated employee's OWN still-valid JWT still renders their own
            //   year-overview (explicitly pinned, owner-accepted R9e consequence; read-only,
            //   bounded by the JWT lifetime — login is is_active-filtered).
            //
            //   NON-SELF: the is_active-filtered GetByIdAsync runs FIRST. Only when it returns
            //   null (target terminated — or nonexistent) is
            //   ValidateEmployeeAccessIncludingTerminatedAsync RE-RUN against the now-CURRENT
            //   target state; only an actor clearing the R9f1 per-scope HROrAbove floor at
            //   that moment gets the terminated-inclusive read.
            //
            // WHY not key the inclusive read on actor privilege (the previous R9f2 shape,
            // `ActorRole >= LocalHR`)? The primary ActorRole is NOT org-bound (Codex cycle-2
            // B2): a mixed-role JWT — LocalHR in a DISJOINT org plus a LocalLeader scope
            // COVERING the target — carries primary role LocalHR, so after a
            // validate-while-active → read-after-deactivation flip the role check admitted the
            // terminated-inclusive read even though the actor's only covering scope is below
            // the HR floor: the same TOCTOU leak, mixed-role variant. Re-running the validator
            // (whose R9f1 floor binds privilege to the ADMITTING scope) closes it. The
            // validator gate above STAYS (fail-fast; its 403 reasons are matrix-pinned) — the
            // re-validation is ADDITIVE, on the null-active-read path only, so an HR-floor
            // actor reading a terminated target costs two validator calls (accepted) while an
            // ACTIVE target costs exactly one filtered read, byte-identical to before (pinned
            // by YearOverviewTests.Auth_LeaderInScope_Returns200).
            //
            // A DENIED re-validation falls through to the SAME 404 the filtered-null path
            // returns (NOT 403): a below-floor actor must not get an oracle distinguishing
            // "terminated but you lack the HR floor" from "does not exist" — and the
            // genuinely-nonexistent target (re-validation denies "Target employee not found")
            // lands on the identical shape.
            var user = await ReadYearOverviewTargetAsync(userRepo, scopeValidator, actor, employeeId, ct);
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
            // S81 / TASK-8101 (R3) — the three former local functions (ResolveAgreementAtAsync /
            // ResolveFallbackLiveAsync / ResolveDatedConfigAsync) + their two shared per-request
            // caches were hoisted byte-for-byte into the shared DatedEntitlementConfigResolver
            // (Infrastructure; it does repository I/O). One per-request instance is minted here so
            // the matrix loop (≤3 distinct ferieår starts × ≤4 categories) + the configless
            // probe-anchor bootstrap below SHARE the same agreement-by-date + live-by-(type,
            // agreement) caches — bounding repo reads exactly as before. Behaviour-IDENTICAL:
            //   - agreement code dated at each ferieår start (the SAME-code byte-identity for a
            //     single-agreement employee),
            //   - the year-start-anchored OK version operand passed per call,
            //   - the ADR-023 D3 graceful fallback chain (dated → today's-agreement liveConfig →
            //     per-agreement live → liveConfig), never 500.
            var datedConfigResolver = datedConfigResolverFactory.Create(
                employeeId, user.OkVersion, user.AgreementCode, todayAgreementCode);

            // ── Categories: saldo[12] + afholdt[12] + expiring + boundaryMonth ──
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
                        var anchorAgreement = await datedConfigResolver.ResolveAgreementAtAsync(anchor, ct);
                        if (string.Equals(anchorAgreement, todayAgreementCode, StringComparison.Ordinal))
                            continue;
                        var altLive = await datedConfigResolver.ResolveFallbackLiveAsync(type, anchorAgreement, ct);
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
                        expiring = 0m,
                        boundaryMonth = 12
                    });
                    continue;
                }
                var resetMonth = liveConfig.ResetMonth;

                // S81 / TASK-8102 (R2) — the operation-date reset_month memo. The other three
                // Convention-A consumers (Skema quota validation, Balance /summary + /series) derive
                // the CONSUMED entitlement year from the config governing the OPERATION DATE's key
                // (op-date OK + op-date agreement), so the year-start derivation is correct even when
                // reset_month DIVERGES across the OK/agreement natural keys (reachable for the
                // unconstrained IMMEDIATE types CARE_DAY/SENIOR_DAY — only VACATION is schema-pinned to
                // reset_month 9; SPECIAL_HOLIDAY is reset_month-independent via the resolver). The
                // YearOverview matrix must key each month the SAME way or it split-brains against
                // balance/validation/summary/series (the exact thing S81 closes). So for EACH month
                // we read the open config at THAT month's operation-date key and take ITS reset_month;
                // null (no op-date-key config) falls back to the today-key liveConfig.ResetMonth —
                // byte-identical to the prior single-resetMonth behaviour when no op-date config
                // exists. Memoised per (opOk, opAgreement) so a 12-month loop issues at most a handful
                // of extra reads per type; the op-date agreement reuses datedConfigResolver's shared
                // agreement-by-date cache. VACATION (schema-pinned 9) and the common no-divergence
                // employee (op-date key reset_month == today-key reset_month) yield BYTE-IDENTICAL
                // periods; SPECIAL_HOLIDAY's Resolve IGNORES reset_month entirely (S80 1-Jan geometry),
                // so it is unaffected regardless of the value fed in.
                var resetMonthByOpKey = new Dictionary<(string Ok, string Agreement), int>();
                async Task<int> ResolveOpDateResetMonthAsync(DateOnly opDate)
                {
                    var opOk = OkVersionResolver.ResolveVersion(opDate);
                    var opAgreement = await datedConfigResolver.ResolveAgreementAtAsync(opDate, ct);
                    var memoKey = (opOk, opAgreement);
                    if (resetMonthByOpKey.TryGetValue(memoKey, out var memoed))
                        return memoed;
                    var opConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                        type, opAgreement, opOk, ct);
                    var resolved = opConfig?.ResetMonth ?? resetMonth;
                    resetMonthByOpKey[memoKey] = resolved;
                    return resolved;
                }

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

                    // S80 / TASK-8001 (R2/R10) — the entitlement (accrual) year + accrual-window start
                    // governing month m, via the SHARED EntitlementPeriodResolver (anchored at this
                    // month's end). VACATION + every other type are BEHAVIOR-IDENTICAL to the old
                    // ResolveEntitlementYear + (entYear, resetMonth, 1) ferieaarStart. SPECIAL_HOLIDAY
                    // uses the two-calendar-year taking-window mapping + a 1-Jan accrual anchor, so a
                    // calendar-year matrix shows the accrual-year balance being taken in its taking
                    // window (the transition at 1 May matches the R2 booking keying).
                    //
                    // S81 / TASK-8102 (R2) — the reset_month operand is re-derived from the
                    // operation-date key (op-date OK + op-date agreement) governing THIS month's end,
                    // matching the other three R2-corrected consumers; falls back to the today-key
                    // liveConfig.ResetMonth when no op-date-key config exists (byte-identical to before).
                    var resetMonthForMonth = await ResolveOpDateResetMonthAsync(monthEnd);
                    var period = EntitlementPeriodResolver.Resolve(type, resetMonthForMonth, monthEnd);
                    var entYear = period.EntitlementYear;
                    var ferieaarStart = period.AccrualStart;

                    // Config dated at the entitlement-year START (ADR-021 D2, same as /summary):
                    // OK resolved at the year-start, AND the agreement code resolved at the
                    // year-start (Step-7a fix — historical ferieår must not be valued with today's
                    // agreement), then the dated config read with the graceful fallback chain.
                    var entOkVersion = OkVersionResolver.ResolveVersion(ferieaarStart);
                    var ec = await datedConfigResolver.ResolveDatedConfigAsync(type, ferieaarStart, entOkVersion, liveConfig, ct);

                    var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                        employeeId, type, entYear, ct);
                    var carryoverIn = balance?.CarryoverIn ?? 0m;

                    var earned = string.Equals(ec.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
                        ? AccrualMath.EarnedToDate(
                            ec.AnnualQuota, 1.0m, ferieaarStart, user.EmploymentStartDate, monthEnd)
                        : ec.AnnualQuota; // IMMEDIATE: full quota earned up-front.

                    // S80 / TASK-8001 (BLOCKER 2 fix) — the afholdt subtracted must be the bookings of
                    // THIS accrual year (entYear), summed over the window in which entYear's days are
                    // TAKEN. For VACATION/calendar types accrual == taking, so the window is
                    // [AccrualStart, monthEnd] (unchanged, byte-identical to pre-S80). For
                    // SPECIAL_HOLIDAY the accrual year Y is taken 1 May (Y+1) – 30 Apr (Y+2) — a window
                    // that does NOT overlap [AccrualStart=1 Jan Y, monthEnd]; a date-range subtraction
                    // anchored at AccrualStart would (a) miss this accrual year's real bookings and
                    // (b) wrongly subtract a DIFFERENT accrual year's bookings that happen to fall in
                    // [1 Jan Y, monthEnd] (e.g. a Mar-Y booking belongs to accrual Y−2, not Y). So sum
                    // over [TakingStart, min(monthEnd, Boundary)] — the taking window, clamped to the
                    // viewed month so the running cumulative tracks the calendar matrix.
                    DateOnly afholdtFrom;
                    DateOnly afholdtTo;
                    if (EntitlementPeriodResolver.IsSpecialHoliday(type))
                    {
                        afholdtFrom = period.TakingStart;
                        afholdtTo = monthEnd < period.Boundary ? monthEnd : period.Boundary;
                    }
                    else
                    {
                        afholdtFrom = ferieaarStart;
                        afholdtTo = monthEnd;
                    }
                    var cumulativeAfholdt = AfholdtWithin(type, afholdtFrom, afholdtTo);
                    saldo[m - 1] = Math.Round(earned + carryoverIn - cumulativeAfholdt, 2);
                }

                // expiring: period-end DISPOSITION projection, COMPUTED at the type's model boundary,
                // EMITTED at boundaryMonth=12 (ADR-030 D9 as amended — S66 post-close). The projected
                // VALUE is the COMPLEMENT of the old "transferable": what remains BEYOND the carryover
                // cap at the boundary (what will leave the balance), not what fits under it. The
                // per-category LABEL ("Til udløb" vs "Til udbetaling") is FE-side, keyed off type.
                //   VACATION identity: max(0, raw − cap) ≡ max(0, 20 − used) = the Feriefonden-lost
                //     figure (cap 5, quota 25 ⇒ raw 25 − used; 25 − used − 5 = 20 − used).
                //   SPECIAL_HOLIDAY expiring = godtgørelse-bound days (the untaken særlige feriedage
                //     convert to the 2½% cash godtgørelse — Cirkulære 021-24 §15 stk.2 + §17): money, not loss.
                // This is STILL a projection, not settlement (D7 deferred).
                // ResetMonth-9 → 31 Aug of the selected year (closes ferieår year-1, spanning
                // Sep year-1 .. Aug year). Calendar types → 31 Dec of the selected year (ferieår
                // year). carryoverIn/used/planned are the CLOSED-boundary-ferieår balances (the
                // SAME ferieår as earnedAtBoundary — NOT the live current-ferieår row; the
                // ferieRemaining tile uses the live balances, a different quantity). carryoverMax
                // is year-start dated.
                //
                // S80 / TASK-8001 (R3/R10) — the CLOSED accrual year whose disposition boundary falls
                // in the selected calendar `year`, derived via the SHARED EntitlementPeriodResolver so
                // SPECIAL_HOLIDAY's calendar-accrual / 30-Apr-(Y+2) boundary is expressed (the old
                // reset_month-9 branch would have invented a non-existent 31-Aug boundary). VACATION +
                // calendar types stay BYTE-IDENTICAL: reset_month 1 → closedEntYear = year (boundary
                // 31 Dec year); reset_month 9 → closedEntYear = year−1 (boundary 31 Aug year), the
                // §21 deadline-year geometry. SPECIAL_HOLIDAY → closedEntYear = year−2 (boundary
                // 30 Apr year). The settlement CLOSE itself (the godtgørelse) is TASK-8002; this stays
                // a display-only disposition PROJECTION (D7 deferred).
                // S81 / TASK-8103 (Step-7a cycle-2 BLOCKER fix) — the closed-ferieår disposition keys
                // off the OPERATION-DATE reset_month (R2-consistent with the per-month saldo + the
                // balance), NOT the today/live-key resetMonth. Jan-1 of the selected year is within the
                // ferieår that closes during `year` for ANY reset_month (1..12), so it resolves the
                // closed ferieår's governing key without circularity. Common case (no divergence):
                // closedResetMonth == resetMonth ⇒ byte-identical. SPECIAL_HOLIDAY is reset_month-
                // independent (the IsSpecialHoliday branch below).
                var closedResetMonth = await ResolveOpDateResetMonthAsync(new DateOnly(year, 1, 1));
                int closedEntYear;
                if (EntitlementPeriodResolver.IsSpecialHoliday(type))
                    closedEntYear = year - 2;                          // boundary 30 Apr (Y+2) falls in `year`.
                else if (closedResetMonth == 1)
                    closedEntYear = year;                              // boundary 31 Dec E (E == year).
                else
                    closedEntYear = year - 1;                          // boundary 31 Dec E+1 (E == year−1).
                var closedPeriod = EntitlementPeriodResolver.ResolveForYear(type, closedResetMonth, closedEntYear);
                DateOnly closedFerieaarStart = closedPeriod.AccrualStart;
                // boundaryDate is the EarnedToDate asOf below (a non-stored, display-only projection).
                // S80 / TASK-8001 (BLOCKER 1 fix) — this MUST be the ACCRUAL-window END
                // (closedPeriod.AccrualEnd: 31 Aug E+1 for VACATION, 31 Dec E for calendar types,
                // 31 Dec Y for SPECIAL_HOLIDAY), NOT the resolver's Boundary (the §21 31-Dec DEADLINE
                // / 30-Apr-E+2 settlement boundary, which falls LATER). AccrualMath.EarnedToDate
                // clamps elapsed months FROM the accrual start (= the hire date for a mid-ferieår
                // hire), capped at 12 — it does NOT cap the asOf at the accrual-window end. So feeding
                // the later Boundary OVER-COUNTS a mid-ferieår hire (e.g. a VACATION hire on 1 Jan
                // accrues 8 months by 31 Aug, but a 31-Dec asOf would read the full 12). For a
                // full-ferieår employee both dates yield 12 months (byte-identical to pre-S80); the
                // mid-hire case is where they diverge. IMMEDIATE types ignore boundaryDate
                // (earnedAtBoundary = AnnualQuota).
                DateOnly boundaryDate = closedPeriod.AccrualEnd;
                var closedOkVersion = OkVersionResolver.ResolveVersion(closedFerieaarStart);
                // Step-7a fix: agreement code resolved at the CLOSED ferieår start (same dated
                // anchoring + graceful fallback as the monthly saldo read above).
                var closedConfig = await datedConfigResolver.ResolveDatedConfigAsync(
                    type, closedFerieaarStart, closedOkVersion, liveConfig, ct);
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
                // Disposition projection: the amount BEYOND the carryover cap at the boundary
                // (what expires as days / converts to godtgørelse), i.e. the complement of the
                // old capped-transferable. raw ≤ cap ⇒ 0 (nothing leaves the balance).
                var expiring = Math.Round(
                    Math.Max(0m, transferableRaw - closedConfig.CarryoverMax), 2);

                // ── S68 / TASK-6807 (ADR-033 D6 clarification) — settled-CLOSED-ferieår special-case ──
                // When the CLOSED entitlement-year (closedEntYear — the one the D9 boundary projects)
                // has an ACTIVE settlement, the D9 `expiring` figure is the RECORDED §34 forfeit_days
                // off the row, NOT the live recompute (determinism; post-settlement readers never
                // recompute — ADR-033 D3/D6). By construction these COINCIDE: SettlementService.Partition
                // computes forfeit_days as round(max(0, earned + carryoverIn − used − planned − carryover_max), 2)
                // with MidpointRounding.ToEven — byte-identical to the `expiring` formula above — so this
                // swap is value-preserving on a healthy row while pinning the deterministic source.
                // `expiring` (== recorded forfeit_days) stays ONLY the over-cap §34-candidate bucket; the
                // settlement `disposition` exposes the FULL §21+§24+§34 partition (a SUPERSET of expiring).
                // The per-month `saldo` array is left UNTOUCHED — settlement does not retroactively zero
                // the ferieår's monthly history (ADR-033 D6 clarification). Lookup keys on
                // (employee, type, closedEntYear); null ⇒ unchanged (every non-VACATION type, any
                // unsettled VACATION year).
                var closedSettlement = await settlementRepo.GetActiveAsync(
                    employeeId, type, closedEntYear, ct);

                decimal displayedExpiring = expiring;
                object? disposition = null;
                if (closedSettlement is not null)
                {
                    var pending = !string.Equals(
                        closedSettlement.SettlementState, "SETTLED", StringComparison.Ordinal);
                    // Pin the D9 figure to the recorded §34 bucket (deterministic source).
                    displayedExpiring = Math.Round(closedSettlement.ForfeitDays, 2);
                    disposition = new
                    {
                        state = closedSettlement.SettlementState,
                        transferDays = closedSettlement.TransferDays,   // §21
                        payoutDays = closedSettlement.PayoutDays,       // §24
                        forfeitDays = closedSettlement.ForfeitDays,     // §34 — == displayedExpiring
                        // PENDING_REVIEW: the §34 remainder is still unresolved (flagged, NOT 0; Codex W).
                        forfeitPending = pending,
                        // S71 / TASK-7103 (SPRINT-71 R5) — the termination claim disposition renders
                        // DISTINCTLY (same shape as the /summary settled-year reader): a WAIVED row
                        // shows "WAIVED" + the waived quantity with forfeit_days already cleared to 0
                        // by the resolve verb, so `expiring` reads 0 — never §34 forfeiture. Null for
                        // every unwaived row.
                        reviewDisposition = closedSettlement.ReviewDisposition,
                        claimDispositionDays = closedSettlement.ClaimDispositionDays
                    };
                }

                categories.Add(new
                {
                    type,
                    label = DanishLabels.TryGetValue(type, out var lbl) ? lbl : type,
                    saldo,
                    afholdt,
                    expiring = displayedExpiring,
                    boundaryMonth = 12,
                    // null for unsettled closed-ferieår; the recorded FULL disposition for a
                    // SETTLED/PENDING_REVIEW closed year (a SUPERSET of `expiring`).
                    settlement = disposition
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
            // S80 / TASK-8001 (R10) — SENIOR_DAY year-start via the shared resolver (BEHAVIOR-IDENTICAL:
            // a calendar reset_month-1 type, so the accrual start is 1 Jan of the entitlement year).
            int? seniorMinAge = seniorLiveConfig is not null
                ? await ResolveSeniorMinAgeAsync(seniorLiveConfig)
                : null;

            async Task<int?> ResolveSeniorMinAgeAsync(EntitlementConfig live)
            {
                var seniorStart = EntitlementPeriodResolver
                    .Resolve("SENIOR_DAY", live.ResetMonth, today).AccrualStart;
                return (await entitlementConfigRepo.GetByTypeAtAsync(
                        "SENIOR_DAY", todayAgreementCode,
                        OkVersionResolver.ResolveVersion(seniorStart), seniorStart, ct))?.MinAge
                    ?? live.MinAge;
            }
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
                // S80 / TASK-8001 (R2/R10) — the entitlement (accrual) year + accrual start for `type`
                // as-of today, via the shared resolver. The four tiles cover VACATION/CARE_DAY/
                // SENIOR_DAY/CHILD_SICK only (NOT SPECIAL_HOLIDAY), all BEHAVIOR-IDENTICAL to the old
                // ResolveEntitlementYear keying; routing through the resolver keeps the geometry in one
                // place (and would key SPECIAL_HOLIDAY correctly if a tile is ever added).
                var period = EntitlementPeriodResolver.Resolve(type, live.ResetMonth, today);
                var entYear = period.EntitlementYear;
                var ferieaarStart = period.AccrualStart;
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

                // ── S68 / TASK-6807 (ADR-033 D6 clarification) — settled CURRENT-ferieår tile ──
                // If the CURRENT entitlement-year (entYear — the ferieår containing today, the one this
                // tile reports) has an ACTIVE settlement, the tile reads the RECORDED disposition, not
                // the live earned-based remaining (determinism; post-settlement no recompute). SETTLED
                // ⇒ 0; PENDING_REVIEW ⇒ the unresolved §34 forfeit_days remainder (flagged-pending, NOT
                // 0; Codex W). Unsettled (null) ⇒ the live remaining, unchanged. Keys on
                // (employee, type, entYear) — non-VACATION + unsettled VACATION ⇒ null ⇒ unchanged.
                var settlement = await settlementRepo.GetActiveAsync(employeeId, type, entYear, ct);
                if (settlement is not null)
                {
                    return string.Equals(settlement.SettlementState, "SETTLED", StringComparison.Ordinal)
                        ? 0m
                        : Math.Round(settlement.ForfeitDays, 2);
                }
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

    /// <summary>
    /// The year-overview body-read selection (S70 / TASK-7003, Step-5a cycle-3 shape — Codex
    /// cycle-2 B2). SELF → unconditional terminated-INCLUSIVE read (the pinned, owner-accepted
    /// R9e consequence: a terminated employee's own still-valid JWT renders their own
    /// year-overview). NON-SELF → the is_active-filtered <c>GetByIdAsync</c> FIRST; on null,
    /// RE-RUN <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>
    /// against the now-CURRENT target state (its R9f1 per-scope HROrAbove floor binds privilege
    /// to the ADMITTING scope — primary-role keying was spoofable by a mixed-role JWT carrying
    /// LocalHR in a disjoint org plus a covering LocalLeader scope) and only on Allow perform
    /// the terminated-inclusive read.
    ///
    /// Null ⇒ the caller's 404. A DENIED re-validation deliberately returns the SAME null the
    /// filtered-read miss returns: no terminated-vs-nonexistent oracle for below-floor actors
    /// (the genuinely-nonexistent target also denies, with "Target employee not found").
    ///
    /// PUBLIC (not private like this file's other helpers) so the regression suite can pin the
    /// EXACT production read path deterministically: HTTP requests cannot interleave a
    /// deactivation between the handler's auth gate and this read, and the sealed
    /// repository/validator types preclude interception doubles — see
    /// <c>TerminatedEmployeeAccessTests.YearOverview_BodyRead_*</c>.
    /// </summary>
    public static async Task<User?> ReadYearOverviewTargetAsync(
        UserRepository userRepo,
        OrgScopeValidator scopeValidator,
        ActorContext actor,
        string employeeId,
        CancellationToken ct)
    {
        if (employeeId == actor.ActorId)
            return await userRepo.GetByIdIncludingTerminatedAsync(employeeId, ct);

        // Non-self: active-only read first — the overwhelmingly common case, and the read that
        // makes a validate-while-active → read-after-deactivation race fail CLOSED.
        var activeUser = await userRepo.GetByIdAsync(employeeId, ct);
        if (activeUser is not null)
            return activeUser;

        // Target not active (terminated or nonexistent): re-validate against CURRENT state —
        // the R9f1 per-scope HROrAbove floor decides, not the un-org-bound primary role.
        var (allowed, _) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
            actor, employeeId, ct);
        if (!allowed)
            return null; // fail closed: same null/404 as a filtered-read miss (no oracle)

        return await userRepo.GetByIdIncludingTerminatedAsync(employeeId, ct);
    }

    // ── S65 / TASK-6502 helpers (BalanceEndpoints-local) ──

    // S80 / TASK-8001 (R10) — the BalanceEndpoints-local ResolveEntitlementYear helper was removed;
    // the entitlement (accrual) year + accrual start now come from the shared
    // StatsTid.SharedKernel.Calendar.EntitlementPeriodResolver (the SPECIAL_HOLIDAY third-geometry hoist).

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
