using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Config;

/// <summary>
/// Position-based overrides for AgreementRuleConfig.
/// Maps (AgreementCode, OkVersion, Position) to partial config overrides.
/// Pure static data, no I/O. Nullable fields mean "use base config value".
/// </summary>
public static class PositionOverrideConfigs
{
    /// <summary>
    /// A partial override record. Null fields = use base config value.
    /// Only fields that can legitimately vary by position are included.
    /// </summary>
    public sealed record PositionConfigOverride
    {
        public decimal? MaxFlexBalance { get; init; }
        public int? NormPeriodWeeks { get; init; }
        public decimal? WeeklyNormHours { get; init; }
    }

    private static readonly Dictionary<(string AgreementCode, string OkVersion, string Position), PositionConfigOverride> Overrides = new()
    {
        // AC DEPARTMENT_HEAD: higher flex cap, 4-week norm period
        [("AC", "OK24", "DEPARTMENT_HEAD")] = new PositionConfigOverride
        {
            MaxFlexBalance = 200.0m,
            NormPeriodWeeks = 4,
        },
        [("AC", "OK26", "DEPARTMENT_HEAD")] = new PositionConfigOverride
        {
            MaxFlexBalance = 200.0m,
            NormPeriodWeeks = 4,
        },

        // AC RESEARCHER: 4-week norm period (research schedules are irregular)
        [("AC", "OK24", "RESEARCHER")] = new PositionConfigOverride
        {
            NormPeriodWeeks = 4,
        },
        [("AC", "OK26", "RESEARCHER")] = new PositionConfigOverride
        {
            NormPeriodWeeks = 4,
        },
    };

    /// <summary>
    /// Tries to get a position override for the given agreement, version, and position.
    /// Returns null if no override exists.
    /// </summary>
    public static PositionConfigOverride? TryGetOverride(string agreementCode, string okVersion, string position)
    {
        return Overrides.TryGetValue((agreementCode, okVersion, position), out var positionOverride)
            ? positionOverride
            : null;
    }

    /// <summary>
    /// Applies a position override to a base AgreementRuleConfig: a new config that carries every
    /// member of the base, except that each of the override's three members
    /// (<see cref="PositionConfigOverride.WeeklyNormHours"/>, <see cref="PositionConfigOverride.MaxFlexBalance"/>,
    /// <see cref="PositionConfigOverride.NormPeriodWeeks"/>) replaces the base value when it is set; a
    /// null override member keeps the base value. Every override the database holds reaches pay and the
    /// rest checks through this merge (ADR-043 D12). <see cref="AgreementRuleConfig"/> is a class built
    /// by the explicit initializer below, so a member added to it must be added here too, or the merged
    /// config silently takes that member's default (QUAL-190).
    /// </summary>
    public static AgreementRuleConfig ApplyOverride(AgreementRuleConfig baseConfig, PositionConfigOverride positionOverride)
    {
        return new AgreementRuleConfig
        {
            AgreementCode = baseConfig.AgreementCode,
            OkVersion = baseConfig.OkVersion,
            WeeklyNormHours = positionOverride.WeeklyNormHours ?? baseConfig.WeeklyNormHours,
            HasOvertime = baseConfig.HasOvertime,
            HasMerarbejde = baseConfig.HasMerarbejde,
            MaxFlexBalance = positionOverride.MaxFlexBalance ?? baseConfig.MaxFlexBalance,
            EveningSupplementEnabled = baseConfig.EveningSupplementEnabled,
            NightSupplementEnabled = baseConfig.NightSupplementEnabled,
            WeekendSupplementEnabled = baseConfig.WeekendSupplementEnabled,
            HolidaySupplementEnabled = baseConfig.HolidaySupplementEnabled,
            EveningStart = baseConfig.EveningStart,
            EveningEnd = baseConfig.EveningEnd,
            NightStart = baseConfig.NightStart,
            NightEnd = baseConfig.NightEnd,
            EveningRate = baseConfig.EveningRate,
            NightRate = baseConfig.NightRate,
            WeekendSaturdayRate = baseConfig.WeekendSaturdayRate,
            WeekendSundayRate = baseConfig.WeekendSundayRate,
            HolidayRate = baseConfig.HolidayRate,
            OvertimeThreshold50 = baseConfig.OvertimeThreshold50,
            OvertimeThreshold100 = baseConfig.OvertimeThreshold100,
            OnCallDutyEnabled = baseConfig.OnCallDutyEnabled,
            OnCallDutyRate = baseConfig.OnCallDutyRate,
            CallInWorkEnabled = baseConfig.CallInWorkEnabled,
            CallInMinimumHours = baseConfig.CallInMinimumHours,
            CallInRate = baseConfig.CallInRate,
            TravelTimeEnabled = baseConfig.TravelTimeEnabled,
            WorkingTravelRate = baseConfig.WorkingTravelRate,
            NonWorkingTravelRate = baseConfig.NonWorkingTravelRate,
            NormPeriodWeeks = positionOverride.NormPeriodWeeks ?? baseConfig.NormPeriodWeeks,
            // No PositionConfigOverride varies the members below, so each carries the base value; a
            // member left out here would take its default instead (QUAL-190; S122 TASK-12200 for the
            // four overtime-governance members).
            NormModel = baseConfig.NormModel,
            AnnualNormHours = baseConfig.AnnualNormHours,
            MaxDailyHours = baseConfig.MaxDailyHours,
            MinimumRestHours = baseConfig.MinimumRestHours,
            RestPeriodDerogationAllowed = baseConfig.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = baseConfig.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = baseConfig.VoluntaryUnsocialHoursAllowed,
            DefaultCompensationModel = baseConfig.DefaultCompensationModel,
            EmployeeCompensationChoice = baseConfig.EmployeeCompensationChoice,
            MaxOvertimeHoursPerPeriod = baseConfig.MaxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = baseConfig.OvertimeRequiresPreApproval,
        };
    }
}
