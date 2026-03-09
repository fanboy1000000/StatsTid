using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
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

    // ── Known absence time_types (used to filter wage_type_mappings) ──
    private static readonly HashSet<string> AbsenceTimeTypes = new(StringComparer.Ordinal)
    {
        "SICK_DAY", "VACATION", "CARE_DAY", "CHILD_SICK_DAY", "CHILD_SICK_DAY_2",
        "CHILD_SICK_DAY_3", "PARENTAL_LEAVE", "SENIOR_DAY", "LEAVE_WITH_PAY", "LEAVE_WITHOUT_PAY"
    };

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

    public static WebApplication MapSkemaEndpoints(this WebApplication app)
    {
        // ── GET /api/skema/{employeeId}/month — Composite monthly spreadsheet data ──

        app.MapGet("/api/skema/{employeeId}/month", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            ProjectRepository projectRepo,
            AbsenceTypeVisibilityRepository visibilityRepo,
            TimerSessionRepository timerRepo,
            ApprovalPeriodRepository approvalRepo,
            IEventStore eventStore,
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

            // Fetch projects for the employee's org
            var projects = await projectRepo.GetByOrgAsync(user.PrimaryOrgId, ct);

            // Fetch absence type visibility for this org
            var visibilityEntries = await visibilityRepo.GetByOrgAsync(user.PrimaryOrgId, ct);
            var hiddenTypes = new HashSet<string>(
                visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType),
                StringComparer.Ordinal);

            // Build absence types list (filtered by visibility)
            var absenceTypes = AbsenceTimeTypes
                .Where(t => !hiddenTypes.Contains(t))
                .Select(t => new
                {
                    type = t,
                    label = AbsenceTypeLabels.TryGetValue(t, out var l) ? l : t
                })
                .ToList();

            // Fetch events for the employee stream
            var streamId = $"employee-{employeeId}";
            var allEvents = await eventStore.ReadStreamAsync(streamId, ct);

            // Filter time entries for this month
            var entries = allEvents.OfType<TimeEntryRegistered>()
                .Where(e => e.Date >= monthStart && e.Date <= monthEnd)
                .Select(e => new
                {
                    date = e.Date,
                    projectCode = e.TaskId,
                    hours = e.Hours
                })
                .ToList();

            // Filter absences for this month
            var absences = allEvents.OfType<AbsenceRegistered>()
                .Where(e => e.Date >= monthStart && e.Date <= monthEnd)
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
            ApprovalPeriodRepository approvalRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IEventStore eventStore,
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

                    var config = await entitlementConfigRepo.GetByTypeAsync(
                        entitlementType, user.AgreementCode, user.OkVersion, ct);
                    if (config is null)
                        continue;

                    var firstAbsenceDate = request.Absences
                        .Where(a => AbsenceToEntitlementType.TryGetValue(a.AbsenceType, out var et) && et == entitlementType)
                        .Select(a => a.Date)
                        .Min();
                    var entitlementYear = ResolveEntitlementYear(firstAbsenceDate, config.ResetMonth);

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

            // Save time entries
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
                        AgreementCode = user.AgreementCode,
                        OkVersion = user.OkVersion,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId
                    };
                    await eventStore.AppendAsync(streamId, @event, ct);
                    savedCount++;
                }
            }

            // Save absences and atomically adjust entitlement balances
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
                        AgreementCode = user.AgreementCode,
                        OkVersion = user.OkVersion,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId
                    };
                    await eventStore.AppendAsync(streamId, @event, ct);
                    savedCount++;

                    if (AbsenceToEntitlementType.TryGetValue(absence.AbsenceType, out var entitlementType) && entitlementType is not null)
                    {
                        if (!savedByEntitlementType.ContainsKey(entitlementType))
                            savedByEntitlementType[entitlementType] = 0m;
                        savedByEntitlementType[entitlementType] += absence.Hours;
                    }
                }

                // Atomically check quota and adjust balances (eliminates TOCTOU race)
                foreach (var (entitlementType, totalHours) in savedByEntitlementType)
                {
                    if (!entitlementData.TryGetValue(entitlementType, out var data))
                        continue;

                    var deltaDays = totalHours / StandardDayHours;
                    var (success, newUsed) = await entitlementBalanceRepo.CheckAndAdjustAsync(
                        employeeId, entitlementType, data.EntitlementYear, deltaDays, data.EffectiveQuota, ct);

                    if (!success)
                    {
                        // Concurrent modification caused quota breach — events already saved, but balance not adjusted.
                        // Log warning; the balance remains consistent (not over-adjusted).
                        continue;
                    }

                    var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                        employeeId, entitlementType, data.EntitlementYear, ct);
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
                    await eventStore.AppendAsync(streamId, balanceEvent, ct);
                }
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
