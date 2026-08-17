namespace StatsTid.SharedKernel.Models;

public sealed class AgreementRuleConfig
{
    /// <summary>
    /// The valid norm-period lengths in weeks (1, 2, 4, 8, or 12). Single domain source of truth,
    /// relocated here (SEC-033) from <c>RuleEngine.Api.Rules.NormCheckRule</c> so BOTH the Rule
    /// Engine (which falls back to a 1-week norm for any value outside this set) and the Backend
    /// admin-config write surfaces (which reject out-of-set values) can share it WITHOUT a
    /// Backend→RuleEngine project reference — ARCHITECTURE.md hard rule #2 forbids that edge; both
    /// projects already reference SharedKernel.
    /// </summary>
    public static readonly IReadOnlySet<int> ValidNormPeriodWeeks = new HashSet<int> { 1, 2, 4, 8, 12 };

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

    // Call-in work (tilkald)
    public bool CallInWorkEnabled { get; init; }
    public decimal CallInMinimumHours { get; init; } = 3.0m;
    public decimal CallInRate { get; init; } = 1.0m;

    // Travel time (rejsetid)
    public bool TravelTimeEnabled { get; init; }
    public decimal WorkingTravelRate { get; init; } = 1.0m;
    public decimal NonWorkingTravelRate { get; init; } = 0.5m;

    // Norm period length in weeks (valid values: see ValidNormPeriodWeeks above)
    public int NormPeriodWeeks { get; init; } = 1;

    // Norm model (default WEEKLY_HOURS for backward compatibility)
    public NormModel NormModel { get; init; } = NormModel.WEEKLY_HOURS;

    // Annual norm hours for ANNUAL_ACTIVITY model (1924h standard = 37h x 52w)
    public decimal AnnualNormHours { get; init; } = 1924m;

    // Working time compliance (Sprint 16)
    public decimal MaxDailyHours { get; init; } = 13.0m;
    public decimal MinimumRestHours { get; init; } = 11.0m;
    public bool RestPeriodDerogationAllowed { get; init; }
    public int WeeklyMaxHoursReferencePeriod { get; init; } = 17;
    public bool VoluntaryUnsocialHoursAllowed { get; init; } = true;

    // Overtime governance & compensation (Sprint 17)
    public string DefaultCompensationModel { get; init; } = "AFSPADSERING";
    public bool EmployeeCompensationChoice { get; init; }
    public decimal MaxOvertimeHoursPerPeriod { get; init; }
    public bool OvertimeRequiresPreApproval { get; init; }
}
