using System.Reflection;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Config;

/// <summary>
/// S148 TASK-14826-T, AC-C1p (QUAL-190) - the position-override merge carries every config member.
///
/// <para>
/// <b>Plain language.</b> A department head's override changes three rules (weekly norm, flex cap,
/// norm period). The merge that applies it used to rebuild the config by hand and silently dropped
/// seven other rules back to their defaults, including the minimum rest. This fact walks every
/// member of the config by reflection, so a member added later and forgotten in the merge fails it.
/// </para>
/// </summary>
public class PositionOverrideMergeCompletenessTests
{
    private static readonly string[] OverriddenMembers =
    [
        nameof(AgreementRuleConfig.WeeklyNormHours),
        nameof(AgreementRuleConfig.MaxFlexBalance),
        nameof(AgreementRuleConfig.NormPeriodWeeks),
    ];

    private static PropertyInfo[] Members() =>
        typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>A value that is not the member's default (and not the other value for the same type).</summary>
    private static object NonDefault(PropertyInfo property, object? current)
    {
        var type = property.PropertyType;
        if (type == typeof(string)) return "NON_DEFAULT_" + property.Name;
        if (type == typeof(bool)) return !(bool)current!;
        if (type == typeof(int)) return (int)current! + 7;
        if (type == typeof(decimal)) return (decimal)current! + 3.5m;
        if (type == typeof(NormModel))
            return Enum.GetValues<NormModel>().First(v => !v.Equals(current));
        throw new InvalidOperationException($"No non-default value for {property.Name} ({type.Name}).");
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; no mutation). Red conditions: on the code before this sprint's merge fix
    /// the seven members <c>ApplyOverride</c> dropped (NormModel, AnnualNormHours, MaxDailyHours,
    /// MinimumRestHours, RestPeriodDerogationAllowed, WeeklyMaxHoursReferencePeriod,
    /// VoluntaryUnsocialHoursAllowed) come back as their defaults, so the "empty override returns the
    /// base on every property" assertion fails once per dropped member and the "full override changes
    /// exactly its three members" assertion fails on the same seven.
    ///
    /// Expected values from the spec (ADR-043 D12, QUAL-190): an empty override changes nothing; a full
    /// override replaces exactly WeeklyNormHours, MaxFlexBalance and NormPeriodWeeks. The base is
    /// asserted to differ from a defaults-only config on every member the merge could drop, so the
    /// fact cannot pass vacuously.
    /// </summary>
    [Fact]
    public void ApplyOverride_CarriesEveryConfigMember()
    {
        var defaults = new AgreementRuleConfig
        {
            AgreementCode = "X",
            OkVersion = "OK26",
            WeeklyNormHours = 0m,
            HasOvertime = false,
            HasMerarbejde = false,
            MaxFlexBalance = 0m,
            EveningSupplementEnabled = false,
            NightSupplementEnabled = false,
            WeekendSupplementEnabled = false,
            HolidaySupplementEnabled = false,
        };

        var baseConfig = (AgreementRuleConfig)Activator.CreateInstance(typeof(AgreementRuleConfig), nonPublic: true)!;
        foreach (var property in Members())
        {
            var setter = property.SetMethod
                ?? throw new InvalidOperationException($"{property.Name} cannot be set.");
            setter.Invoke(baseConfig, [NonDefault(property, property.GetValue(defaults))]);
        }

        // The base differs from the defaults on every member (not vacuous).
        foreach (var property in Members())
        {
            Assert.False(
                Equals(property.GetValue(defaults), property.GetValue(baseConfig)),
                $"The base config must be non-default on {property.Name}.");
        }

        // An empty override returns the base on every property.
        var emptied = PositionOverrideConfigs.ApplyOverride(baseConfig, new PositionOverrideConfigs.PositionConfigOverride());
        var emptyDiffers = Members()
            .Where(p => !Equals(p.GetValue(baseConfig), p.GetValue(emptied)))
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(emptyDiffers);

        // A full override changes exactly its three members.
        var full = PositionOverrideConfigs.ApplyOverride(baseConfig, new PositionOverrideConfigs.PositionConfigOverride
        {
            WeeklyNormHours = baseConfig.WeeklyNormHours + 1m,
            MaxFlexBalance = baseConfig.MaxFlexBalance + 1m,
            NormPeriodWeeks = baseConfig.NormPeriodWeeks + 1,
        });
        var changed = Members()
            .Where(p => !Equals(p.GetValue(baseConfig), p.GetValue(full)))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(OverriddenMembers.OrderBy(n => n, StringComparer.Ordinal).ToList(), changed);
    }
}
