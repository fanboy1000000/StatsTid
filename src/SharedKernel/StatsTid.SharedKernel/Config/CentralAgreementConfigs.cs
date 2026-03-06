using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Config;

/// <summary>
/// Single source of truth for central agreement rule configurations.
/// Both the Rule Engine (AgreementConfigProvider) and Infrastructure (ConfigResolutionService)
/// delegate to this class. No duplication — update configs here only.
/// Pure static data, no I/O.
/// </summary>
public static class CentralAgreementConfigs
{
    private static readonly Dictionary<(string AgreementCode, string OkVersion), AgreementRuleConfig> Configs = new()
    {
        // AC OK24
        [("AC", "OK24")] = new AgreementRuleConfig
        {
            AgreementCode = "AC",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = false,
            HasMerarbejde = true,
            MaxFlexBalance = 150.0m,
            FlexCarryoverMax = 150.0m,
            EveningSupplementEnabled = false,
            NightSupplementEnabled = false,
            WeekendSupplementEnabled = false,
            HolidaySupplementEnabled = false,
            OnCallDutyEnabled = false,
            CallInWorkEnabled = false,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
        // HK OK24
        [("HK", "OK24")] = new AgreementRuleConfig
        {
            AgreementCode = "HK",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = true,
            HasMerarbejde = false,
            MaxFlexBalance = 100.0m,
            FlexCarryoverMax = 100.0m,
            EveningSupplementEnabled = true,
            NightSupplementEnabled = true,
            WeekendSupplementEnabled = true,
            HolidaySupplementEnabled = true,
            EveningStart = 17,
            EveningEnd = 23,
            NightStart = 23,
            NightEnd = 6,
            OnCallDutyEnabled = true,
            OnCallDutyRate = 0.33m,
            CallInWorkEnabled = true,
            CallInMinimumHours = 3.0m,
            CallInRate = 1.0m,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
        // PROSA OK24
        [("PROSA", "OK24")] = new AgreementRuleConfig
        {
            AgreementCode = "PROSA",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = true,
            HasMerarbejde = false,
            MaxFlexBalance = 120.0m,
            FlexCarryoverMax = 120.0m,
            EveningSupplementEnabled = true,
            NightSupplementEnabled = true,
            WeekendSupplementEnabled = true,
            HolidaySupplementEnabled = true,
            EveningStart = 17,
            EveningEnd = 23,
            NightStart = 23,
            NightEnd = 6,
            OnCallDutyEnabled = true,
            OnCallDutyRate = 0.33m,
            CallInWorkEnabled = true,
            CallInMinimumHours = 3.0m,
            CallInRate = 1.0m,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
        // AC OK26 (placeholder — identical to OK24 for now)
        [("AC", "OK26")] = new AgreementRuleConfig
        {
            AgreementCode = "AC",
            OkVersion = "OK26",
            WeeklyNormHours = 37.0m,
            HasOvertime = false,
            HasMerarbejde = true,
            MaxFlexBalance = 150.0m,
            FlexCarryoverMax = 150.0m,
            EveningSupplementEnabled = false,
            NightSupplementEnabled = false,
            WeekendSupplementEnabled = false,
            HolidaySupplementEnabled = false,
            OnCallDutyEnabled = false,
            CallInWorkEnabled = false,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
        // HK OK26 (placeholder)
        [("HK", "OK26")] = new AgreementRuleConfig
        {
            AgreementCode = "HK",
            OkVersion = "OK26",
            WeeklyNormHours = 37.0m,
            HasOvertime = true,
            HasMerarbejde = false,
            MaxFlexBalance = 100.0m,
            FlexCarryoverMax = 100.0m,
            EveningSupplementEnabled = true,
            NightSupplementEnabled = true,
            WeekendSupplementEnabled = true,
            HolidaySupplementEnabled = true,
            EveningStart = 17,
            EveningEnd = 23,
            NightStart = 23,
            NightEnd = 6,
            OnCallDutyEnabled = true,
            OnCallDutyRate = 0.33m,
            CallInWorkEnabled = true,
            CallInMinimumHours = 3.0m,
            CallInRate = 1.0m,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
        // PROSA OK26 (placeholder)
        [("PROSA", "OK26")] = new AgreementRuleConfig
        {
            AgreementCode = "PROSA",
            OkVersion = "OK26",
            WeeklyNormHours = 37.0m,
            HasOvertime = true,
            HasMerarbejde = false,
            MaxFlexBalance = 120.0m,
            FlexCarryoverMax = 120.0m,
            EveningSupplementEnabled = true,
            NightSupplementEnabled = true,
            WeekendSupplementEnabled = true,
            HolidaySupplementEnabled = true,
            EveningStart = 17,
            EveningEnd = 23,
            NightStart = 23,
            NightEnd = 6,
            OnCallDutyEnabled = true,
            OnCallDutyRate = 0.33m,
            CallInWorkEnabled = true,
            CallInMinimumHours = 3.0m,
            CallInRate = 1.0m,
            TravelTimeEnabled = true,
            WorkingTravelRate = 1.0m,
            NonWorkingTravelRate = 0.5m,
            NormPeriodWeeks = 1,
        },
    };

    /// <summary>
    /// Gets the config for a given agreement code and OK version.
    /// Throws if not found.
    /// </summary>
    public static AgreementRuleConfig GetConfig(string agreementCode, string okVersion)
    {
        if (Configs.TryGetValue((agreementCode, okVersion), out var config))
            return config;

        throw new InvalidOperationException(
            $"No agreement configuration found for {agreementCode}/{okVersion}");
    }

    /// <summary>
    /// Tries to get the config for a given agreement code and OK version.
    /// Returns null if not found.
    /// </summary>
    public static AgreementRuleConfig? TryGetConfig(string agreementCode, string okVersion)
    {
        return Configs.TryGetValue((agreementCode, okVersion), out var config) ? config : null;
    }

    /// <summary>
    /// Returns whether a config exists for the given agreement code and OK version.
    /// </summary>
    public static bool HasConfig(string agreementCode, string okVersion)
    {
        return Configs.ContainsKey((agreementCode, okVersion));
    }

    /// <summary>
    /// Returns the list of supported agreement codes for a given OK version.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedAgreements(string okVersion)
    {
        return Configs.Keys
            .Where(k => k.OkVersion == okVersion)
            .Select(k => k.AgreementCode)
            .ToList();
    }
}
