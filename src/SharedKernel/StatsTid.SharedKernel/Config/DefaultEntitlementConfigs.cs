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
        AccrualModel = "MONTHLY_ACCRUAL",
        ResetMonth = 9,  // Ferieår: September
        CarryoverMax = 5m,
        // ADR-031: day-count is flat per Ferieloven §5 — part-time pro-rates consumption (§6 stk.2, S64) + value only, never the day-count
        ProRateByPartTime = false,
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
        AccrualModel = "MONTHLY_ACCRUAL",
        // S80 / TASK-8001 (ADR-033 Slice 2, R1 — D11 model correction): særlige feriedage accrue on
        // the CALENDAR year (1 Jan–31 Dec, ~0,42 d/md), NOT the statutory Sep–Aug ferieår (Cirkulære
        // 021-24 §12). The mis-modeled ResetMonth=9 settled ~4 months early and mis-keyed bookings.
        // reset_month=1 makes the stored geometry honest (calendar accrual), and the shared
        // EntitlementPeriodResolver layers the §12 stk.2 taking window (1 May Y+1–30 Apr Y+2) +
        // 30-Apr-(Y+2) settlement boundary on top — neither expressible by raw reset_month. The
        // VACATION-only reset_month CHECK (init.sql) does NOT constrain SPECIAL_HOLIDAY.
        ResetMonth = 1,  // Calendar-year accrual (1 Jan); taking window + boundary via EntitlementPeriodResolver.
        CarryoverMax = 0m,
        // ADR-031: day-count is flat per Ferieloven §5 — part-time pro-rates consumption (§6 stk.2, S64) + value only, never the day-count
        ProRateByPartTime = false,
        IsPerEpisode = false,
        Description = "Særlige feriedage (5 dage pr. kalenderår)"
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
        // S73 / TASK-7301 (owner ruling D-A 2026-06-13): omsorgsdage are FULL-DAY-ONLY ("hele
        // dage") — uniform by construction across every seed path (the DB CHECK
        // entitlement_configs_full_day_only_types rejects a FALSE insert).
        FullDayOnly = true,
        Description = "Omsorgsdage (2 dage pr. kalenderår)"
    };

    /// <summary>
    /// The CHILD_SICK row: the whole sick days a child's illness gives, counted PER ILLNESS
    /// (<c>IsPerEpisode</c>; the AnnualQuota column holds the per-illness count, there is no yearly
    /// cap). OK24: 2 days for every agreement (CIR1H 9333 § 18 stk. 6; 029-24 § 14; 012-25 § 14).
    /// OK26, from 1 April 2026: 3 days (021-26 § 14, 020-26 § 14; for AC agreed in the AC
    /// settlement item 4, its circular pending, so the description says provisional). These counts
    /// decide which day codes the Skema save and catalogue offer (<see cref="ChildSickCodesOffered"/>),
    /// so they must agree with the init.sql seeds (ADR-014; QUAL-236).
    /// </summary>
    private static EntitlementConfig CreateChildSick(string agreementCode, string okVersion)
    {
        var days = okVersion == "OK26" ? 3 : 2;
        var provisional = okVersion == "OK26" && agreementCode == "AC" ? " (foreløbig)" : "";

        return new EntitlementConfig
        {
            ConfigId = DeterministicGuid("CHILD_SICK", agreementCode, okVersion),
            EntitlementType = "CHILD_SICK",
            AgreementCode = agreementCode,
            OkVersion = okVersion,
            AnnualQuota = days,
            AccrualModel = "IMMEDIATE",
            ResetMonth = 1,  // Calendar year
            CarryoverMax = 0m,
            ProRateByPartTime = false,
            IsPerEpisode = true,
            Description = $"Barns sygedag – {days} dage pr. sygdomsforløb{provisional}"
        };
    }

    // ── The child's sick days: which codes a date offers (QUAL-236) ──

    /// <summary>
    /// Every child's-sick code: the 1st, 2nd and 3rd sick day and the call-home day. One set, so
    /// the HR opt-in (ADR-029 D2) finds them all without going through the entitlement mapping,
    /// which sends none of them to a yearly quota.
    /// </summary>
    public static readonly IReadOnlySet<string> ChildSickCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        AbsenceTypes.ChildSick1, AbsenceTypes.ChildSick2, AbsenceTypes.ChildSick3, AbsenceTypes.ChildSickCallHome,
    };

    // shortcut(pending): the call-home day's availability per OK version is code, not entitlement config; upgrade when the config can carry per-illness extras
    // The call-home day per OK version: OK24 has none; OK26 grants it (021-26 and 020-26 § 14; AC's
    // circular pending). An OK version this table does not name gets none (fail-closed, as a
    // missing CHILD_SICK row offers nothing).
    private static readonly IReadOnlyDictionary<string, bool> ChildSickCallHomeDayByOkVersion =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["OK24"] = false,
            ["OK26"] = true,
        };

    /// <summary>
    /// Whether <paramref name="okVersion"/> grants the call-home day (false for a version the
    /// table does not name).
    /// </summary>
    public static bool ChildSickCallHomeDay(string okVersion) =>
        ChildSickCallHomeDayByOkVersion.TryGetValue(okVersion, out var granted) && granted;

    /// <summary>
    /// The whole sick days per illness a dated CHILD_SICK row gives: the whole part of its
    /// <c>AnnualQuota</c>, capped at 3 (the codes stop at day 3) and never below 0.
    /// </summary>
    public static int ChildSickDaysPerIllness(EntitlementConfig childSickConfig) =>
        (int)Math.Clamp(Math.Floor(childSickConfig.AnnualQuota), 0m, 3m);

    /// <summary>
    /// The child's-sick codes a date offers, from the CHILD_SICK row dated at that date (read
    /// under the date's OK version): the call-home day when <see cref="ChildSickCallHomeDay"/>
    /// grants it for the row's OK version, then day 1 up to min(whole quota, 3). No row, or a row
    /// giving no whole day, offers nothing, the call-home day included (it belongs to the same
    /// entitlement). In display order: the call-home day happens first.
    /// </summary>
    public static IReadOnlyList<string> ChildSickCodesOffered(EntitlementConfig? childSickConfig)
    {
        if (childSickConfig is null)
            return [];
        var days = ChildSickDaysPerIllness(childSickConfig);
        if (days < 1)
            return [];

        var codes = new List<string>(4);
        if (ChildSickCallHomeDay(childSickConfig.OkVersion))
            codes.Add(AbsenceTypes.ChildSickCallHome);
        codes.Add(AbsenceTypes.ChildSick1);
        if (days >= 2)
            codes.Add(AbsenceTypes.ChildSick2);
        if (days >= 3)
            codes.Add(AbsenceTypes.ChildSick3);
        return codes;
    }

    private static EntitlementConfig CreateSeniorDay(string agreementCode, string okVersion) => new()
    {
        ConfigId = DeterministicGuid("SENIOR_DAY", agreementCode, okVersion),
        EntitlementType = "SENIOR_DAY",
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = 2m,  // S37 TASK-3703: 2 days/year, age-gated by MinAge (was paired-bug 0 + 60)
        AccrualModel = "IMMEDIATE",
        ResetMonth = 1,  // Calendar year
        CarryoverMax = 0m,
        ProRateByPartTime = false,
        IsPerEpisode = false,
        MinAge = 62,
        // S73 / TASK-7301 (owner ruling D-A 2026-06-13): seniordage are FULL-DAY-ONLY ("hele
        // dage") — uniform by construction across every seed path (the DB CHECK
        // entitlement_configs_full_day_only_types rejects a FALSE insert).
        FullDayOnly = true,
        Description = "Seniordage (aldersbetinget, fra 62 år)"
    };
}
