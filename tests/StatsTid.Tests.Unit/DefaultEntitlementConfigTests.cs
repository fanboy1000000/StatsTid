using StatsTid.SharedKernel.Config;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for DefaultEntitlementConfigs seed data (Sprint 15).
/// Verifies correct count, field values, uniqueness, and agreement-specific variations.
/// </summary>
public class DefaultEntitlementConfigTests
{
    [Fact]
    public void GetAll_Returns30Configs()
    {
        var configs = DefaultEntitlementConfigs.GetAll();
        Assert.Equal(30, configs.Count);
    }

    [Fact]
    public void GetAll_AllConfigsHaveNonEmptyRequiredFields()
    {
        var configs = DefaultEntitlementConfigs.GetAll();
        Assert.All(configs, c =>
        {
            Assert.NotEqual(Guid.Empty, c.ConfigId);
            Assert.False(string.IsNullOrWhiteSpace(c.EntitlementType));
            Assert.False(string.IsNullOrWhiteSpace(c.AgreementCode));
            Assert.False(string.IsNullOrWhiteSpace(c.OkVersion));
            Assert.False(string.IsNullOrWhiteSpace(c.AccrualModel));
        });
    }

    [Fact]
    public void GetAll_AllConfigIdsAreUnique()
    {
        var configs = DefaultEntitlementConfigs.GetAll();
        var ids = configs.Select(c => c.ConfigId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("AC", "OK26")]
    [InlineData("HK", "OK24")]
    [InlineData("HK", "OK26")]
    [InlineData("PROSA", "OK24")]
    [InlineData("PROSA", "OK26")]
    public void GetConfigsForAgreement_Returns5Types(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        Assert.Equal(5, configs.Count);

        var types = configs.Select(c => c.EntitlementType).OrderBy(t => t).ToList();
        Assert.Equal(
            new[] { "CARE_DAY", "CHILD_SICK", "SENIOR_DAY", "SPECIAL_HOLIDAY", "VACATION" },
            types);
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK26")]
    [InlineData("PROSA", "OK24")]
    public void VacationConfigs_Have25Quota_September_Carryover5_NoProRate(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var vacation = configs.Single(c => c.EntitlementType == "VACATION");

        Assert.Equal(25m, vacation.AnnualQuota);
        Assert.Equal(9, vacation.ResetMonth);
        Assert.Equal(5m, vacation.CarryoverMax);
        // S63 / ADR-031: VACATION day-count is FLAT (fraction-independent) per Ferieloven §5 stk.1
        // — a part-timer earns the SAME number of days as a full-timer; part-time pro-rates the
        // value/consumption (§6 stk.2), never the earned day-count. Flipped True→False.
        Assert.False(vacation.ProRateByPartTime);
        Assert.False(vacation.IsPerEpisode);
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK26")]
    public void SpecialHolidayConfigs_Have5Quota_CalendarAccrual_NoCarryover(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var special = configs.Single(c => c.EntitlementType == "SPECIAL_HOLIDAY");

        Assert.Equal(5m, special.AnnualQuota);
        // S80 / TASK-8001 (ADR-033 Slice 2, R1/R9 — D11 model correction): the DISCRIMINATING
        // before/after pin. SPECIAL_HOLIDAY accrual is now the CALENDAR year (reset_month 1, Cirkulære
        // 021-24 §12), NOT the mis-modeled Sep–Aug ferieår (reset_month 9). This assertion FAILS on the
        // pre-S80 seed. The §12 stk.2 taking window + 30-Apr-(Y+2) boundary are layered by
        // EntitlementPeriodResolver (see EntitlementPeriodResolverTests), not stored on the config.
        Assert.Equal(1, special.ResetMonth);
        Assert.Equal(0m, special.CarryoverMax);
        // S63 / ADR-031: SPECIAL_HOLIDAY day-count is FLAT (fraction-independent) per Ferieloven §5
        // — same rationale as VACATION; the earned day-count never scales by the part-time fraction.
        Assert.False(special.ProRateByPartTime);
        Assert.False(special.IsPerEpisode);
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK26")]
    public void CareDayConfigs_Have2Quota_January_NoProRate(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var careDay = configs.Single(c => c.EntitlementType == "CARE_DAY");

        Assert.Equal(2m, careDay.AnnualQuota);
        Assert.Equal(1, careDay.ResetMonth);
        Assert.Equal(0m, careDay.CarryoverMax);
        Assert.False(careDay.ProRateByPartTime);
        Assert.False(careDay.IsPerEpisode);
        // S73 / TASK-7301 (owner ruling D-A, SPRINT-73 R2): omsorgsdage are FULL-DAY-ONLY —
        // uniform by construction across every seed path (the factory mirrors the init.sql
        // seeds + the entitlement_configs_full_day_only_types CHECK).
        Assert.True(careDay.FullDayOnly);
    }

    [Fact]
    public void ChildSick_AC_Has1DayPerEpisode()
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement("AC", "OK24");
        var childSick = configs.Single(c => c.EntitlementType == "CHILD_SICK");

        Assert.Equal(1m, childSick.AnnualQuota);
        Assert.True(childSick.IsPerEpisode);
        Assert.False(childSick.ProRateByPartTime);
    }

    [Fact]
    public void ChildSick_HK_Has2DaysPerEpisode()
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement("HK", "OK24");
        var childSick = configs.Single(c => c.EntitlementType == "CHILD_SICK");

        Assert.Equal(2m, childSick.AnnualQuota);
        Assert.True(childSick.IsPerEpisode);
    }

    [Fact]
    public void ChildSick_PROSA_Has3DaysPerEpisode()
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement("PROSA", "OK26");
        var childSick = configs.Single(c => c.EntitlementType == "CHILD_SICK");

        Assert.Equal(3m, childSick.AnnualQuota);
        Assert.True(childSick.IsPerEpisode);
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK26")]
    [InlineData("PROSA", "OK24")]
    public void SeniorDayConfigs_Have2Quota_MinAge62(string agreement, string okVersion)
    {
        // S59 / TASK-5903 / ADR-029: reconciled to the S37-corrected DB seed (init.sql:1449
        // = 2 days / age 62). Previously this test pinned the stale paired-bug values
        // (quota 0 + min_age 60) that contradicted the seed and silently rejected every
        // senior-day registration regardless of age. Now asserts the live 2/62 contract.
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var seniorDay = configs.Single(c => c.EntitlementType == "SENIOR_DAY");

        Assert.Equal(2m, seniorDay.AnnualQuota);
        Assert.Equal(62, seniorDay.MinAge);
        Assert.Equal(1, seniorDay.ResetMonth);
        Assert.False(seniorDay.ProRateByPartTime);
        Assert.False(seniorDay.IsPerEpisode);
        // S73 / TASK-7301 (owner ruling D-A, SPRINT-73 R2): seniordage are FULL-DAY-ONLY —
        // uniform by construction across every seed path (the factory mirrors the init.sql
        // seeds + the entitlement_configs_full_day_only_types CHECK).
        Assert.True(seniorDay.FullDayOnly);
    }

    [Fact]
    public void GetAll_DeterministicGuids_AreStableAcrossCalls()
    {
        var first = DefaultEntitlementConfigs.GetAll();
        var second = DefaultEntitlementConfigs.GetAll();

        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].ConfigId, second[i].ConfigId);
        }
    }

    /// <summary>
    /// S60 / TASK-6003 / ADR-030 — activate monthly accrual. VACATION + SPECIAL_HOLIDAY now
    /// use MONTHLY_ACCRUAL (Ferieloven samtidighedsferie); the calendar-year types
    /// (CARE_DAY / CHILD_SICK / SENIOR_DAY) stay IMMEDIATE. Replaces the pre-S60 pin that
    /// asserted ALL models were IMMEDIATE (the dead-enum ADR-021 D6 state).
    /// </summary>
    [Fact]
    public void GetAll_AccrualModels_VacationAndSpecialHolidayAreMonthlyAccrual_RestImmediate()
    {
        var configs = DefaultEntitlementConfigs.GetAll();

        var monthlyTypes = new[] { "VACATION", "SPECIAL_HOLIDAY" };
        Assert.All(configs, c =>
        {
            var expected = monthlyTypes.Contains(c.EntitlementType) ? "MONTHLY_ACCRUAL" : "IMMEDIATE";
            Assert.Equal(expected, c.AccrualModel);
        });

        // Sanity: both monthly types are actually present across all agreements (6 each).
        Assert.Equal(6, configs.Count(c => c.EntitlementType == "VACATION" && c.AccrualModel == "MONTHLY_ACCRUAL"));
        Assert.Equal(6, configs.Count(c => c.EntitlementType == "SPECIAL_HOLIDAY" && c.AccrualModel == "MONTHLY_ACCRUAL"));
    }
}
