using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Config;

/// <summary>
/// Single source of truth for default entitlement configurations.
/// Provides seed data for 5 entitlement types × 3 agreements × 2 OK versions = 30 configs.
/// Pure static data, no I/O.
/// </summary>
public static class DefaultEntitlementConfigs
{
    private static readonly string[] AgreementCodes = ["AC", "HK", "PROSA"];
    private static readonly string[] OkVersions = ["OK24", "OK26"];

    /// <summary>
    /// Returns all 30 default entitlement configs.
    /// GUIDs are deterministic based on (entitlementType, agreementCode, okVersion).
    /// </summary>
    public static IReadOnlyList<EntitlementConfig> GetAll()
    {
        var configs = new List<EntitlementConfig>();

        foreach (var agreement in AgreementCodes)
        {
            foreach (var okVersion in OkVersions)
            {
                configs.AddRange(GetConfigsForAgreement(agreement, okVersion));
            }
        }

        return configs;
    }

    /// <summary>
    /// Returns entitlement configs for a specific agreement and OK version (5 entitlement types).
    /// </summary>
    public static IReadOnlyList<EntitlementConfig> GetConfigsForAgreement(string agreementCode, string okVersion)
    {
        return
        [
            CreateVacation(agreementCode, okVersion),
            CreateSpecialHoliday(agreementCode, okVersion),
            CreateCareDay(agreementCode, okVersion),
            CreateChildSick(agreementCode, okVersion),
            CreateSeniorDay(agreementCode, okVersion),
        ];
    }

    /// <summary>
    /// Generates a deterministic GUID from the entitlement type, agreement code, and OK version.
    /// Uses a namespace-based approach for reproducible seeding.
    /// </summary>
    private static Guid DeterministicGuid(string entitlementType, string agreementCode, string okVersion)
    {
        var input = $"entitlement:{entitlementType}:{agreementCode}:{okVersion}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        // Set version 4 and variant bits for RFC 4122 compliance
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static EntitlementConfig CreateVacation(string agreementCode, string okVersion) => new()
    {
        ConfigId = DeterministicGuid("VACATION", agreementCode, okVersion),
        EntitlementType = "VACATION",
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = 25m,
        AccrualModel = "IMMEDIATE",
        ResetMonth = 9,  // Ferieår: September
        CarryoverMax = 5m,
        ProRateByPartTime = true,
        IsPerEpisode = false,
        Description = "Ferie (25 dage pr. ferieår)"
    };

    private static EntitlementConfig CreateSpecialHoliday(string agreementCode, string okVersion) => new()
    {
        ConfigId = DeterministicGuid("SPECIAL_HOLIDAY", agreementCode, okVersion),
        EntitlementType = "SPECIAL_HOLIDAY",
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = 5m,
        AccrualModel = "IMMEDIATE",
        ResetMonth = 9,  // Ferieår: September
        CarryoverMax = 0m,
        ProRateByPartTime = true,
        IsPerEpisode = false,
        Description = "Særlige feriedage (5 dage pr. ferieår)"
    };

    private static EntitlementConfig CreateCareDay(string agreementCode, string okVersion) => new()
    {
        ConfigId = DeterministicGuid("CARE_DAY", agreementCode, okVersion),
        EntitlementType = "CARE_DAY",
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = 2m,
        AccrualModel = "IMMEDIATE",
        ResetMonth = 1,  // Calendar year
        CarryoverMax = 0m,
        ProRateByPartTime = false,
        IsPerEpisode = false,
        Description = "Omsorgsdage (2 dage pr. kalenderår)"
    };

    private static EntitlementConfig CreateChildSick(string agreementCode, string okVersion)
    {
        // CHILD_SICK quota varies by agreement
        var quota = agreementCode switch
        {
            "AC" => 1m,
            "HK" => 2m,
            "PROSA" => 3m,
            _ => 1m
        };

        return new EntitlementConfig
        {
            ConfigId = DeterministicGuid("CHILD_SICK", agreementCode, okVersion),
            EntitlementType = "CHILD_SICK",
            AgreementCode = agreementCode,
            OkVersion = okVersion,
            AnnualQuota = quota,
            AccrualModel = "IMMEDIATE",
            ResetMonth = 1,  // Calendar year
            CarryoverMax = 0m,
            ProRateByPartTime = false,
            IsPerEpisode = true,
            Description = $"Barns sygedag ({quota} dag(e) pr. episode)"
        };
    }

    private static EntitlementConfig CreateSeniorDay(string agreementCode, string okVersion) => new()
    {
        ConfigId = DeterministicGuid("SENIOR_DAY", agreementCode, okVersion),
        EntitlementType = "SENIOR_DAY",
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = 0m,  // Age-dependent, resolved at runtime
        AccrualModel = "IMMEDIATE",
        ResetMonth = 1,  // Calendar year
        CarryoverMax = 0m,
        ProRateByPartTime = false,
        IsPerEpisode = false,
        MinAge = 60,
        Description = "Seniordage (aldersbetinget, fra 60 år)"
    };
}
