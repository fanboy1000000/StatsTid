using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Config;

/// <summary>
/// Pure static function: returns agreement-specific rule configuration.
/// No I/O, no DB — all config is in-memory and version-aware.
/// </summary>
public static class AgreementConfigProvider
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
        },
    };

    public static AgreementRuleConfig GetConfig(string agreementCode, string okVersion)
    {
        if (Configs.TryGetValue((agreementCode, okVersion), out var config))
            return config;

        throw new InvalidOperationException(
            $"No agreement configuration found for {agreementCode}/{okVersion}");
    }

    public static bool HasConfig(string agreementCode, string okVersion)
    {
        return Configs.ContainsKey((agreementCode, okVersion));
    }

    public static IReadOnlyList<string> GetSupportedAgreements(string okVersion)
    {
        return Configs.Keys
            .Where(k => k.OkVersion == okVersion)
            .Select(k => k.AgreementCode)
            .ToList();
    }
}
