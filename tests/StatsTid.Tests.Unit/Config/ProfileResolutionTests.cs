using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Config;

/// <summary>
/// D11 fixtures #8–#10 — pure unit tests for the per-column profile overlay applied by
/// <c>ConfigResolutionService.ApplyProfileOverlay</c> (ADR-017 D3 step 3).
///
/// <para>
/// The overlay method is private (it is an internal step of <c>ResolveAsync</c>'s 3-stage
/// chain), so these tests invoke it via reflection. This is justified because the fixture
/// table specifies "construct LocalAgreementProfile + AgreementConfig in-memory, call
/// resolution"; the overlay IS the resolution-time profile-application semantic. Going
/// through <c>ResolveAsync</c> would require a live DB (sealed repos with real
/// <see cref="DbConnectionFactory"/>); reflection-on-private gives us the same contract
/// in-memory.
/// </para>
///
/// <para>
/// The constructor of <see cref="ConfigResolutionService"/> is invoked with non-null
/// repository instances pointing at a fake connection — neither the constructor nor
/// <c>ApplyProfileOverlay</c> issue any DB calls, so the fake connection never opens.
/// </para>
/// </summary>
public sealed class ProfileResolutionTests
{
    private static readonly AgreementRuleConfig CentralConfig = new()
    {
        AgreementCode = "HK",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        HasOvertime = true,
        HasMerarbejde = false,
        MaxFlexBalance = 80m,
        FlexCarryoverMax = 20m,
        EveningSupplementEnabled = true,
        NightSupplementEnabled = true,
        WeekendSupplementEnabled = true,
        HolidaySupplementEnabled = true,
        MaxOvertimeHoursPerPeriod = 0m,
        OvertimeRequiresPreApproval = false,
    };

    [Fact]
    public void AllNullColumns_InheritCentralForEveryField()
    {
        // Profile with every overridable column NULL — overlay must be a no-op
        // (output equals central for every overridable AgreementRuleConfig field).
        var profile = NewProfile(
            weeklyNormHours: null,
            maxFlexBalance: null,
            flexCarryoverMax: null,
            maxOvertimeHoursPerPeriod: null,
            overtimeRequiresPreApproval: null);

        var resolved = InvokeApplyProfileOverlay(CentralConfig, profile);

        Assert.Equal(CentralConfig.WeeklyNormHours, resolved.WeeklyNormHours);
        Assert.Equal(CentralConfig.MaxFlexBalance, resolved.MaxFlexBalance);
        Assert.Equal(CentralConfig.FlexCarryoverMax, resolved.FlexCarryoverMax);
        Assert.Equal(CentralConfig.MaxOvertimeHoursPerPeriod, resolved.MaxOvertimeHoursPerPeriod);
        Assert.Equal(CentralConfig.OvertimeRequiresPreApproval, resolved.OvertimeRequiresPreApproval);

        // Non-overridable fields must always inherit central byte-for-byte.
        Assert.Equal(CentralConfig.HasOvertime, resolved.HasOvertime);
        Assert.Equal(CentralConfig.HasMerarbejde, resolved.HasMerarbejde);
        Assert.Equal(CentralConfig.EveningSupplementEnabled, resolved.EveningSupplementEnabled);
    }

    [Fact]
    public void OneNonNullColumn_OverridesOnlyThatField()
    {
        // Single overridable field set; the other four columns NULL → only MaxFlexBalance
        // should differ from central in the resolved config.
        var profile = NewProfile(
            weeklyNormHours: null,
            maxFlexBalance: 100m,
            flexCarryoverMax: null,
            maxOvertimeHoursPerPeriod: null,
            overtimeRequiresPreApproval: null);

        var resolved = InvokeApplyProfileOverlay(CentralConfig, profile);

        Assert.Equal(100m, resolved.MaxFlexBalance);
        // The other four overridable fields must come from central.
        Assert.Equal(CentralConfig.WeeklyNormHours, resolved.WeeklyNormHours);
        Assert.Equal(CentralConfig.FlexCarryoverMax, resolved.FlexCarryoverMax);
        Assert.Equal(CentralConfig.MaxOvertimeHoursPerPeriod, resolved.MaxOvertimeHoursPerPeriod);
        Assert.Equal(CentralConfig.OvertimeRequiresPreApproval, resolved.OvertimeRequiresPreApproval);
    }

    [Fact]
    public void NullAfterNonNull_RevertsToCentral()
    {
        // Sanity: a first profile with MaxFlexBalance=100 produces a resolution at 100;
        // a successor profile that nulls the column reverts the resolution to central
        // (the schema-level "NULL means inherit" contract). Per ADR-017, each profile
        // is resolved independently — no carry-over from predecessors.
        var firstProfile = NewProfile(
            weeklyNormHours: null,
            maxFlexBalance: 100m,
            flexCarryoverMax: null,
            maxOvertimeHoursPerPeriod: null,
            overtimeRequiresPreApproval: null);
        var firstResolved = InvokeApplyProfileOverlay(CentralConfig, firstProfile);
        Assert.Equal(100m, firstResolved.MaxFlexBalance);

        // Successor profile (a new row after supersession) with MaxFlexBalance=NULL.
        var secondProfile = NewProfile(
            weeklyNormHours: null,
            maxFlexBalance: null,
            flexCarryoverMax: null,
            maxOvertimeHoursPerPeriod: null,
            overtimeRequiresPreApproval: null);
        var secondResolved = InvokeApplyProfileOverlay(CentralConfig, secondProfile);

        Assert.Equal(CentralConfig.MaxFlexBalance, secondResolved.MaxFlexBalance);
    }

    private static LocalAgreementProfile NewProfile(
        decimal? weeklyNormHours,
        decimal? maxFlexBalance,
        decimal? flexCarryoverMax,
        decimal? maxOvertimeHoursPerPeriod,
        bool? overtimeRequiresPreApproval) => new()
    {
        ProfileId = Guid.NewGuid(),
        OrgId = "STY02",
        AgreementCode = "HK",
        OkVersion = "OK24",
        EffectiveFrom = new DateOnly(2026, 5, 4), // Monday — ignore alignment policy here
        WeeklyNormHours = weeklyNormHours,
        MaxFlexBalance = maxFlexBalance,
        FlexCarryoverMax = flexCarryoverMax,
        MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
        OvertimeRequiresPreApproval = overtimeRequiresPreApproval,
        CreatedBy = "test",
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Constructs a <see cref="ConfigResolutionService"/> with a non-null
    /// <see cref="DbConnectionFactory"/> + sealed repositories pointing at an unreachable
    /// connection string, then invokes the private <c>ApplyProfileOverlay</c> via reflection.
    /// Neither the constructor nor the overlay method opens a DB connection, so the
    /// unreachable connection never matters at runtime.
    /// </summary>
    private static AgreementRuleConfig InvokeApplyProfileOverlay(
        AgreementRuleConfig central, LocalAgreementProfile profile)
    {
        const string fakeConnString = "Host=localhost;Port=1;Database=na;Username=na;Password=na";
        var factory = new DbConnectionFactory(fakeConnString);
        var service = new ConfigResolutionService(
            new AgreementConfigRepository(factory),
            new LocalConfigurationRepository(factory),
            new PositionOverrideRepository(factory),
            new LocalAgreementProfileRepository(factory),
            NullLogger<ConfigResolutionService>.Instance);

        var method = typeof(ConfigResolutionService)
            .GetMethod("ApplyProfileOverlay", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var result = method!.Invoke(service, new object[] { central, profile, profile.OrgId });
        Assert.NotNull(result);
        return (AgreementRuleConfig)result!;
    }
}
