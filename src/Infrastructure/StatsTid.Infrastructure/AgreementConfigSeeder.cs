using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Seeds the agreement_configs table from CentralAgreementConfigs on first boot.
/// Idempotent: does nothing if any configs already exist.
/// After seeding, the database is the single source of truth (ADR-014).
/// </summary>
public static class AgreementConfigSeeder
{
    private static readonly (string Code, string Version)[] AllConfigs =
    [
        ("AC", "OK24"), ("AC", "OK26"),
        ("HK", "OK24"), ("HK", "OK26"),
        ("PROSA", "OK24"), ("PROSA", "OK26"),
        ("AC_RESEARCH", "OK24"), ("AC_RESEARCH", "OK26"),
        ("AC_TEACHING", "OK24"), ("AC_TEACHING", "OK26"),
    ];

    public static async Task SeedAsync(
        AgreementConfigRepository repository,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existing = await repository.GetAllAsync(ct);
        if (existing.Count > 0)
        {
            logger.LogDebug("Agreement configs already seeded ({Count} configs) — skipping", existing.Count);
            return;
        }

        logger.LogInformation("Seeding {Count} agreement configs from CentralAgreementConfigs...", AllConfigs.Length);

        foreach (var (code, version) in AllConfigs)
        {
            var config = CentralAgreementConfigs.TryGetConfig(code, version);
            if (config is null)
            {
                logger.LogWarning("No static config found for {Code}/{Version} — skipping seed", code, version);
                continue;
            }

            var entity = new AgreementConfigEntity
            {
                ConfigId = Guid.NewGuid(),
                AgreementCode = config.AgreementCode,
                OkVersion = config.OkVersion,
                Status = AgreementConfigStatus.ACTIVE,
                WeeklyNormHours = config.WeeklyNormHours,
                NormPeriodWeeks = config.NormPeriodWeeks,
                NormModel = config.NormModel,
                AnnualNormHours = config.AnnualNormHours,
                MaxFlexBalance = config.MaxFlexBalance,
                FlexCarryoverMax = config.FlexCarryoverMax,
                HasOvertime = config.HasOvertime,
                HasMerarbejde = config.HasMerarbejde,
                OvertimeThreshold50 = config.OvertimeThreshold50,
                OvertimeThreshold100 = config.OvertimeThreshold100,
                EveningSupplementEnabled = config.EveningSupplementEnabled,
                NightSupplementEnabled = config.NightSupplementEnabled,
                WeekendSupplementEnabled = config.WeekendSupplementEnabled,
                HolidaySupplementEnabled = config.HolidaySupplementEnabled,
                EveningStart = config.EveningStart,
                EveningEnd = config.EveningEnd,
                NightStart = config.NightStart,
                NightEnd = config.NightEnd,
                EveningRate = config.EveningRate,
                NightRate = config.NightRate,
                WeekendSaturdayRate = config.WeekendSaturdayRate,
                WeekendSundayRate = config.WeekendSundayRate,
                HolidayRate = config.HolidayRate,
                OnCallDutyEnabled = config.OnCallDutyEnabled,
                OnCallDutyRate = config.OnCallDutyRate,
                CallInWorkEnabled = config.CallInWorkEnabled,
                CallInMinimumHours = config.CallInMinimumHours,
                CallInRate = config.CallInRate,
                TravelTimeEnabled = config.TravelTimeEnabled,
                WorkingTravelRate = config.WorkingTravelRate,
                NonWorkingTravelRate = config.NonWorkingTravelRate,
                MaxDailyHours = config.MaxDailyHours,
                MinimumRestHours = config.MinimumRestHours,
                RestPeriodDerogationAllowed = config.RestPeriodDerogationAllowed,
                WeeklyMaxHoursReferencePeriod = config.WeeklyMaxHoursReferencePeriod,
                VoluntaryUnsocialHoursAllowed = config.VoluntaryUnsocialHoursAllowed,
                CreatedBy = "SYSTEM_SEED",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow,
                Description = $"{config.AgreementCode} {config.OkVersion} — seeded from static config",
            };

            await repository.CreateAsync(entity, "ACTIVE", ct);
            logger.LogInformation("Seeded {Code}/{Version} as ACTIVE", code, version);
        }

        logger.LogInformation("Agreement config seeding complete");
    }
}
