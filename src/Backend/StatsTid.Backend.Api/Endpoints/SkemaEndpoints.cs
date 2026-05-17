using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
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
            TimerSessionRepository timerRepo,
            ApprovalPeriodRepository approvalRepo,
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

            // Fetch projects for the employee's org
            var projects = await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct);

            // Fetch absence type visibility for this org
            var visibilityEntries = await visibilityRepo.GetByOrgAsync(user.PrimaryOrgId, ct);
            var hiddenTypes = new HashSet<string>(
                visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType),
                StringComparer.Ordinal);

            // Build absence types list (filtered by agreement and org visibility)
            var agreementAbsenceTypes = GetAbsenceTypesForAgreement(agreementCode);
            var absenceTypes = agreementAbsenceTypes
                .Where(t => !hiddenTypes.Contains(t))
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

            // Get active timer session
            var activeTimer = await timerRepo.GetActiveByEmployeeAsync(employeeId, ct);
            object? timerSession = activeTimer is not null
                ? new
                {
                    sessionId = activeTimer.SessionId,
                    employeeId = activeTimer.EmployeeId,
                    date = activeTimer.Date,
                    checkInAt = activeTimer.CheckInAt,
                    checkOutAt = activeTimer.CheckOutAt,
                    isActive = activeTimer.IsActive
                }
                : null;

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
                timerSession,
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

            // ── Pre-compute entitlement data for validation and post-save adjustment ──
            // Aggregate requested hours per entitlement type
            var entitlementData = new Dictionary<string, (decimal RequestedDays, int EntitlementYear, decimal EffectiveQuota)>(StringComparer.Ordinal);

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

                const decimal partTimeFraction = 1.0m;
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

                    var effectiveQuota = config.ProRateByPartTime
                        ? config.AnnualQuota * partTimeFraction
                        : config.AnnualQuota;

                    // Call Rule Engine via HTTP (PAT-005 compliance)
                    var validationRequest = new
                    {
                        annualQuota = config.AnnualQuota,
                        used = balance?.Used ?? 0m,
                        planned = balance?.Planned ?? 0m,
                        carryoverIn = balance?.CarryoverIn ?? 0m,
                        requestedDays,
                        partTimeFraction,
                        proRateByPartTime = config.ProRateByPartTime,
                        isPerEpisode = config.IsPerEpisode,
                        perEpisodeLimit = (decimal?)null
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

                    entitlementData[entitlementType] = (requestedDays, entitlementYear, effectiveQuota);
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
                            var (success, newUsed) = await entitlementBalanceRepo.CheckAndAdjustAsync(
                                conn, tx, employeeId, entitlementType, data.EntitlementYear,
                                deltaDays, data.EffectiveQuota, ct);

                            if (!success)
                            {
                                // S27 TASK-2706 (c) quota-race fix: throw to roll back the entire save
                                // atomically (bundle-rollback per ADR-018 D3). Replaces pre-S27 silent
                                // `continue` that returned 200 OK with inconsistent state. `newUsed`
                                // here is the current used balance (the (conn, tx) overload routes the
                                // failure-path read through the same snapshot — see EntitlementBalanceRepository
                                // CheckAndAdjustInternalAsync). Caught at the outer try/catch below
                                // and mapped to a 422 response.
                                throw new SkemaQuotaBreachException(
                                    entitlementType,
                                    requestedDays: deltaDays,
                                    currentUsed: newUsed,
                                    effectiveQuota: data.EffectiveQuota);
                            }

                            // Read the carryover via the (conn, tx) overload so it observes the same
                            // snapshot under the outer tx (ADR-018 D3 + S26 TASK-2603(a) refinement W3).
                            var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                                conn, tx, employeeId, entitlementType, data.EntitlementYear, ct);
                            var carryoverIn = balance?.CarryoverIn ?? 0m;
                            var newRemaining = data.EffectiveQuota + carryoverIn - newUsed;

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

    // ── Request DTOs ──

    private sealed class SaveSkemaRequest
    {
        public required int Year { get; init; }
        public required int Month { get; init; }
        public SkemaEntry[]? Entries { get; init; }
        public SkemaAbsence[]? Absences { get; init; }
    }

    private sealed class SkemaEntry
    {
        public required DateOnly Date { get; init; }
        public required string ProjectCode { get; init; }
        public required decimal Hours { get; init; }
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
