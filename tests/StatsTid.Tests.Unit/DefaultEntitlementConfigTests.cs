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
    public void VacationConfigs_Have25Quota_September_Carryover5_ProRate(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var vacation = configs.Single(c => c.EntitlementType == "VACATION");

        Assert.Equal(25m, vacation.AnnualQuota);
        Assert.Equal(9, vacation.ResetMonth);
        Assert.Equal(5m, vacation.CarryoverMax);
        Assert.True(vacation.ProRateByPartTime);
        Assert.False(vacation.IsPerEpisode);
    }

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK26")]
    public void SpecialHolidayConfigs_Have5Quota_September_NoCarryover(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var special = configs.Single(c => c.EntitlementType == "SPECIAL_HOLIDAY");

        Assert.Equal(5m, special.AnnualQuota);
        Assert.Equal(9, special.ResetMonth);
        Assert.Equal(0m, special.CarryoverMax);
        Assert.True(special.ProRateByPartTime);
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
    public void SeniorDayConfigs_Have0Quota_MinAge60(string agreement, string okVersion)
    {
        var configs = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, okVersion);
        var seniorDay = configs.Single(c => c.EntitlementType == "SENIOR_DAY");

        Assert.Equal(0m, seniorDay.AnnualQuota);
        Assert.Equal(60, seniorDay.MinAge);
        Assert.Equal(1, seniorDay.ResetMonth);
        Assert.False(seniorDay.ProRateByPartTime);
        Assert.False(seniorDay.IsPerEpisode);
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

    [Fact]
    public void GetAll_AllAccrualModelsAreImmediate()
    {
        var configs = DefaultEntitlementConfigs.GetAll();
        Assert.All(configs, c => Assert.Equal("IMMEDIATE", c.AccrualModel));
    }
}
