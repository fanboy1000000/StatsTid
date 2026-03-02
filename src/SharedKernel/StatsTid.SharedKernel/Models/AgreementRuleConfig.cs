namespace StatsTid.SharedKernel.Models;

public sealed class AgreementRuleConfig
{
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required decimal WeeklyNormHours { get; init; }
    public required bool HasOvertime { get; init; }
    public required bool HasMerarbejde { get; init; }
    public required decimal MaxFlexBalance { get; init; }
    public required decimal FlexCarryoverMax { get; init; }

    // Supplement toggles
    public required bool EveningSupplementEnabled { get; init; }
    public required bool NightSupplementEnabled { get; init; }
    public required bool WeekendSupplementEnabled { get; init; }
    public required bool HolidaySupplementEnabled { get; init; }

    // Supplement time windows (hour of day)
    public int EveningStart { get; init; } = 17;
    public int EveningEnd { get; init; } = 23;
    public int NightStart { get; init; } = 23;
    public int NightEnd { get; init; } = 6;

    // Supplement rates
    public decimal EveningRate { get; init; } = 1.25m;
    public decimal NightRate { get; init; } = 1.50m;
    public decimal WeekendSaturdayRate { get; init; } = 1.50m;
    public decimal WeekendSundayRate { get; init; } = 2.0m;
    public decimal HolidayRate { get; init; } = 2.0m;

    // Overtime thresholds (weekly hours)
    public decimal OvertimeThreshold50 { get; init; } = 37.0m;
    public decimal OvertimeThreshold100 { get; init; } = 40.0m;

    // On-call duty (rådighedsvagt)
    public bool OnCallDutyEnabled { get; init; }
    public decimal OnCallDutyRate { get; init; } = 0.33m;
}
