using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
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

    public static WebApplication MapBalanceEndpoints(this WebApplication app)
    {
        // ── GET /api/balance/{employeeId}/summary — Employee balance summary for a given month ──

        app.MapGet("/api/balance/{employeeId}/summary", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            AgreementConfigRepository configRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
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

            // Validate month/year
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
                return Results.BadRequest(new { error = "Invalid year or month" });

            // Get employee profile
            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            // Get agreement config — try DB first (ACTIVE), fall back to central static config
            var dbConfig = await configRepo.GetActiveAsync(user.AgreementCode, user.OkVersion, ct);
            var weeklyNormHours = dbConfig?.WeeklyNormHours
                ?? CentralAgreementConfigs.TryGetConfig(user.AgreementCode, user.OkVersion)?.WeeklyNormHours
                ?? 37.0m;
            var hasMerarbejde = dbConfig?.HasMerarbejde
                ?? CentralAgreementConfigs.TryGetConfig(user.AgreementCode, user.OkVersion)?.HasMerarbejde
                ?? false;

            // Calculate working days (Mon-Fri) in the month
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            var weekdays = 0;
            for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    weekdays++;
            }

            var normHoursExpected = (weekdays / 5.0m) * weeklyNormHours;

            // Read employee event stream
            var streamId = $"employee-{employeeId}";
            var allEvents = await eventStore.ReadStreamAsync(streamId, ct);

            // Sum time entry hours for the given month
            var normHoursActual = allEvents.OfType<TimeEntryRegistered>()
                .Where(e => e.Date >= monthStart && e.Date <= monthEnd)
                .Sum(e => e.Hours);

            // Count distinct vacation days used in the calendar year
            var vacationDaysUsed = allEvents.OfType<AbsenceRegistered>()
                .Where(e => e.AbsenceType == "VACATION" && e.Date.Year == year)
                .Select(e => e.Date)
                .Distinct()
                .Count();

            // Flex balance from the latest FlexBalanceUpdated event
            var latestFlex = allEvents.OfType<FlexBalanceUpdated>().LastOrDefault();
            var flexBalance = latestFlex?.NewBalance ?? 0m;
            var flexDelta = latestFlex?.Delta ?? 0m;

            // Overtime: max(0, actual - expected)
            var overtimeHours = Math.Max(0m, normHoursActual - normHoursExpected);

            // ── Entitlements: load configs and balances ──
            var entitlementConfigs = await entitlementConfigRepo.GetByAgreementAsync(
                user.AgreementCode, user.OkVersion, ct);

            // Part-time fraction not available on User model — default to 1.0
            const decimal partTimeFraction = 1.0m;

            var entitlements = new List<object>();
            decimal? vacationEntitlementFromConfig = null;

            foreach (var ec in entitlementConfigs)
            {
                // Calculate entitlement year based on resetMonth
                int entitlementYear;
                if (ec.ResetMonth == 1)
                {
                    entitlementYear = year;
                }
                else
                {
                    entitlementYear = month >= ec.ResetMonth ? year : year - 1;
                }

                // Look up balance for this employee + type + year
                var balance = await entitlementBalanceRepo.GetByEmployeeAndTypeAsync(
                    employeeId, ec.EntitlementType, entitlementYear, ct);

                var totalQuota = ec.ProRateByPartTime
                    ? ec.AnnualQuota * partTimeFraction
                    : ec.AnnualQuota;

                var used = balance?.Used ?? 0m;
                var planned = balance?.Planned ?? 0m;
                var carryoverIn = balance?.CarryoverIn ?? 0m;
                var remaining = totalQuota + carryoverIn - used - planned;

                DanishLabels.TryGetValue(ec.EntitlementType, out var label);

                entitlements.Add(new
                {
                    type = ec.EntitlementType,
                    label = label ?? ec.EntitlementType,
                    totalQuota,
                    used,
                    planned,
                    carryoverIn,
                    remaining,
                    entitlementYear
                });

                // Derive vacationDaysEntitlement from config instead of hardcoded 25
                if (ec.EntitlementType == "VACATION")
                    vacationEntitlementFromConfig = totalQuota;
            }

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
                agreementCode = user.AgreementCode,
                hasMerarbejde,
                entitlements
            });
        }).RequireAuthorization("EmployeeOrAbove");

        return app;
    }
}
