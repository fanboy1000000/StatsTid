using System.Net.Http.Json;
using System.Text.Json;
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

    // ── Absence type → entitlement type mapping (null = skip validation) ──
    private static readonly Dictionary<string, string?> AbsenceToEntitlementType = new(StringComparer.Ordinal)
    {
        ["VACATION"] = "VACATION",
        ["CARE_DAY"] = "CARE_DAY",
        ["CHILD_SICK_DAY"] = "CHILD_SICK",
        ["CHILD_SICK_DAY_2"] = "CHILD_SICK",
        ["CHILD_SICK_DAY_3"] = "CHILD_SICK",
        ["PARENTAL_LEAVE"] = null,
        ["SENIOR_DAY"] = "SENIOR_DAY",
        ["SPECIAL_HOLIDAY_ALLOWANCE"] = "SPECIAL_HOLIDAY",
        ["LEAVE_WITH_PAY"] = null,
        ["LEAVE_WITHOUT_PAY"] = null,
        ["SICK_DAY"] = null
    };

    // ── Standard work day hours (37h/week ÷ 5 days) ──
    private const decimal StandardDayHours = 7.4m;

    /// <summary>
    /// Resolve an absence type to its entitlement type (null = no entitlement gating),
    /// reusing the <see cref="AbsenceToEntitlementType"/> map. Unknown types ⇒ null.
    /// </summary>
    private static string? GetEntitlementType(string absenceType)
        => AbsenceToEntitlementType.TryGetValue(absenceType, out var et) ? et : null;

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

            // Fetch selected projects for the employee, falling back to all org
            // projects when no selections exist (backwards compatible for first-time users).
            var selectedProjects = await projectRepo.GetSelectedByEmployeeAsync(employeeId, user.PrimaryOrgId, ct);
            var projects = selectedProjects.Count > 0
                ? selectedProjects
                : await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct);

            // Fetch absence type visibility for this org
            var visibilityEntries = await visibilityRepo.GetByOrgAsync(user.PrimaryOrgId, ct);
            var hiddenTypes = new HashSet<string>(
                visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType),
                StringComparer.Ordinal);

            // ── S59 / TASK-5907 per-employee eligibility display filter ──
            // After the org-level absence_type_visibility filter, additionally hide
            // absence types the employee is personally ineligible for. This is a DISPLAY
            // affordance only (what can be added to this month); the POST gate per
            // absence.Date is authoritative. Anchor = MONTH-END (single, well-defined UI
            // anchor). The POST gate intentionally uses a different anchor (per-row
            // absence.Date) — see the note on the POST gate below; the two are NOT
            // required to agree mid-month (refinement line 19, SPRINT-59 TASK-5907).
            //
            // (a) CHILD_SICK: drop all child-sick variants (those mapping to entitlement
            //     CHILD_SICK) when the employee is ineligible as-of month-end. Absent
            //     eligibility row ⇒ ineligible (opt-in default).
            var childSickEligible = (await eligibilityRepo
                .GetEligibleAsOfAsync(employeeId, "CHILD_SICK", monthEnd, ct)).Eligible;

            // (b) SENIOR_DAY: DOB-derived age gate. Hide when the employee is under the
            //     resolved SENIOR_DAY MinAge as-of month-end, OR has no BirthDate
            //     (fail-closed). The age decision itself mirrors the rule-engine gate
            //     (TASK-5904) but here it is only a display affordance; the POST gate
            //     re-validates per row via the rule engine.
            // S59 / Step-7a BLOCKER 2 — resolve the SENIOR_DAY config AS-OF the display anchor
            // (month-end), not the live/open row, so min_age and the age computation share the
            // same anchor (determinism, P4 / ADR-020). Mirrors the dated quota read on POST.
            var seniorConfig = await entitlementConfigRepo.GetByTypeAtAsync(
                "SENIOR_DAY", agreementCode, user.OkVersion, monthEnd, ct);
            var seniorMinAge = seniorConfig?.MinAge;
            var seniorVisible = seniorMinAge is null // no age gate configured ⇒ unrestricted
                || (user.BirthDate is { } dob && AgeAsOf(dob, monthEnd) >= seniorMinAge.Value);

            // Build absence types list (filtered by agreement, org visibility, then
            // per-employee eligibility).
            var agreementAbsenceTypes = GetAbsenceTypesForAgreement(agreementCode);
            var absenceTypes = agreementAbsenceTypes
                .Where(t => !hiddenTypes.Contains(t))
                .Where(t => childSickEligible
                    || !string.Equals(GetEntitlementType(t), "CHILD_SICK", StringComparison.Ordinal))
                .Where(t => seniorVisible || !string.Equals(t, "SENIOR_DAY", StringComparison.Ordinal))
                .Select(t => new
                {
                    type = t,
                    label = AbsenceTypeLabels.TryGetValue(t, out var l) ? l : t
                })
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
                    hours = e.Hours
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
                managerDeadline
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
            IConfiguration configuration,
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

                var absenceHoursByDate = request.Absences
                    .GroupBy(a => a.Date)
                    .Where(g => g.Sum(a => a.Hours) > StandardDayHours);
                foreach (var overDay in absenceHoursByDate)
                {
                    return Results.Json(new
                    {
                        error = "Total absence hours exceed norm day",
                        date = overDay.Key,
                        totalHours = overDay.Sum(a => a.Hours),
                        maxHours = StandardDayHours,
                    }, statusCode: 422);
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
                    var seniorRuleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
                    var seniorHttpClient = httpClientFactory.CreateClient();
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
                                    $"{seniorRuleEngineUrl}/api/rules/validate-entitlement", seniorRequest, seniorJsonOptions, ct);
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

            if (request.Absences is not null && request.Absences.Length > 0)
            {
                var requestedByEntitlementType = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var absence in request.Absences)
                {
                    if (!AbsenceToEntitlementType.TryGetValue(absence.AbsenceType, out var entitlementType) || entitlementType is null)
                        continue;
                    if (!requestedByEntitlementType.ContainsKey(entitlementType))
                        requestedByEntitlementType[entitlementType] = 0m;
                    requestedByEntitlementType[entitlementType] += absence.Hours;
                }

                var ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
                var httpClient = httpClientFactory.CreateClient();
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                foreach (var (entitlementType, totalRequestedHours) in requestedByEntitlementType)
                {
                    var requestedDays = totalRequestedHours / StandardDayHours;

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
                                message = $"Kan ikke validere ferie/feriefridage for {firstAbsenceDate:dd-MM-yyyy}: ansættelsesprofil mangler."
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
                        $"{ruleEngineUrl}/api/rules/validate-entitlement", validationRequest, jsonOptions, ct);

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
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
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

                        foreach (var absence in request.Absences)
                        {
                            var @event = new AbsenceRegistered
                            {
                                EmployeeId = employeeId,
                                Date = absence.Date,
                                AbsenceType = absence.AbsenceType,
                                Hours = absence.Hours,
                                AgreementCode = agreementCode,
                                OkVersion = user.OkVersion,
                                ActorId = actor.ActorId,
                                ActorRole = actor.ActorRole,
                                CorrelationId = actor.CorrelationId
                            };
                            // Per-event ordering: enqueue FIRST (allocates outbox_id), projection SECOND.
                            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                            await absenceProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                            savedCount++;

                            if (AbsenceToEntitlementType.TryGetValue(absence.AbsenceType, out var entitlementType) && entitlementType is not null)
                            {
                                if (!savedByEntitlementType.ContainsKey(entitlementType))
                                    savedByEntitlementType[entitlementType] = 0m;
                                savedByEntitlementType[entitlementType] += absence.Hours;
                            }
                        }

                        // Atomically check quota and adjust balances (eliminates TOCTOU race) via the
                        // (conn, tx) overload from S26 TASK-2603(a) so the UPDATE participates in the
                        // outer tx. A breach (concurrent modification raced past the pre-validation
                        // HTTP check) throws to roll back the entire save (S27 TASK-2706 (c)+(d)).
                        foreach (var (entitlementType, totalHours) in savedByEntitlementType)
                        {
                            if (!entitlementData.TryGetValue(entitlementType, out var data))
                                continue;

                            var deltaDays = totalHours / StandardDayHours;
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

            return Results.Ok(new { saved = savedCount });
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

    // ── Request DTOs ──

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
