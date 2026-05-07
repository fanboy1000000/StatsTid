namespace StatsTid.SharedKernel.Models;

public sealed class AgreementConfigEntity
{
    public required Guid ConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required AgreementConfigStatus Status { get; init; }

    // Norm settings
    public required decimal WeeklyNormHours { get; init; }
    public required int NormPeriodWeeks { get; init; }
    public required NormModel NormModel { get; init; }
    public required decimal AnnualNormHours { get; init; }

    // Flex settings
    public required decimal MaxFlexBalance { get; init; }
    public required decimal FlexCarryoverMax { get; init; }

    // Overtime settings
    public required bool HasOvertime { get; init; }
    public required bool HasMerarbejde { get; init; }
    public required decimal OvertimeThreshold50 { get; init; }
    public required decimal OvertimeThreshold100 { get; init; }

    // Supplement toggles
    public required bool EveningSupplementEnabled { get; init; }
    public required bool NightSupplementEnabled { get; init; }
    public required bool WeekendSupplementEnabled { get; init; }
    public required bool HolidaySupplementEnabled { get; init; }

    // Supplement time windows (hour of day)
    public required int EveningStart { get; init; }
    public required int EveningEnd { get; init; }
    public required int NightStart { get; init; }
    public required int NightEnd { get; init; }

    // Supplement rates
    public required decimal EveningRate { get; init; }
    public required decimal NightRate { get; init; }
    public required decimal WeekendSaturdayRate { get; init; }
    public required decimal WeekendSundayRate { get; init; }
    public required decimal HolidayRate { get; init; }

    // On-call duty (rådighedsvagt)
    public required bool OnCallDutyEnabled { get; init; }
    public required decimal OnCallDutyRate { get; init; }

    // Call-in work (tilkald)
    public required bool CallInWorkEnabled { get; init; }
    public required decimal CallInMinimumHours { get; init; }
    public required decimal CallInRate { get; init; }

    // Travel time (rejsetid)
    public required bool TravelTimeEnabled { get; init; }
    public required decimal WorkingTravelRate { get; init; }
    public required decimal NonWorkingTravelRate { get; init; }

    // Working time compliance (Sprint 16)
    public decimal MaxDailyHours { get; init; } = 13.0m;
    public decimal MinimumRestHours { get; init; } = 11.0m;
    public bool RestPeriodDerogationAllowed { get; init; }
    public int WeeklyMaxHoursReferencePeriod { get; init; } = 17;
    public bool VoluntaryUnsocialHoursAllowed { get; init; } = true;

    // Overtime governance & compensation (Sprint 17)
    public string DefaultCompensationModel { get; init; } = "UDBETALING";
    public bool EmployeeCompensationChoice { get; init; }
    public decimal MaxOvertimeHoursPerPeriod { get; init; }
    public bool OvertimeRequiresPreApproval { get; init; }

    // Metadata
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public Guid? ClonedFromId { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Row-version optimistic-concurrency token (ADR-018 D7 + ADR-019 pending). Mirrors the
    /// <see cref="LocalAgreementProfile.Version"/> pattern: BIGINT (long) row-version, first-
    /// insert is <c>1</c>; each in-place UPDATE bumps it by one. Combined with
    /// <c>If-Match: "&lt;version&gt;"</c> on PUT (RFC 7232 quoted), this provides optimistic-
    /// concurrency control on admin-config edits per ADR-019 (D2.2 propagation).
    ///
    /// Defaulted to <c>1</c> (rather than <c>required</c>) so existing constructor sites in
    /// seeders / endpoints continue to compile during the Phase 1 → Phase 2 transition.
    /// Phase 2 repository updates (TASK-2503/4/5) will read the column from the DB and
    /// set this property explicitly. The DB column is <c>BIGINT NOT NULL DEFAULT 1</c>
    /// (TASK-2501 migration <c>s25-d2-2-version</c>).
    /// </summary>
    public long Version { get; init; } = 1;

    /// <summary>
    /// Creates an AgreementRuleConfig from this entity. Pure mapping, no I/O.
    /// </summary>
    public AgreementRuleConfig ToRuleConfig() => new()
    {
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        WeeklyNormHours = WeeklyNormHours,
        NormPeriodWeeks = NormPeriodWeeks,
        MaxFlexBalance = MaxFlexBalance,
        FlexCarryoverMax = FlexCarryoverMax,
        HasOvertime = HasOvertime,
        HasMerarbejde = HasMerarbejde,
        OvertimeThreshold50 = OvertimeThreshold50,
        OvertimeThreshold100 = OvertimeThreshold100,
        EveningSupplementEnabled = EveningSupplementEnabled,
        NightSupplementEnabled = NightSupplementEnabled,
        WeekendSupplementEnabled = WeekendSupplementEnabled,
        HolidaySupplementEnabled = HolidaySupplementEnabled,
        EveningStart = EveningStart,
        EveningEnd = EveningEnd,
        NightStart = NightStart,
        NightEnd = NightEnd,
        EveningRate = EveningRate,
        NightRate = NightRate,
        WeekendSaturdayRate = WeekendSaturdayRate,
        WeekendSundayRate = WeekendSundayRate,
        HolidayRate = HolidayRate,
        OnCallDutyEnabled = OnCallDutyEnabled,
        OnCallDutyRate = OnCallDutyRate,
        CallInWorkEnabled = CallInWorkEnabled,
        CallInMinimumHours = CallInMinimumHours,
        CallInRate = CallInRate,
        TravelTimeEnabled = TravelTimeEnabled,
        WorkingTravelRate = WorkingTravelRate,
        NonWorkingTravelRate = NonWorkingTravelRate,
        NormModel = NormModel,
        AnnualNormHours = AnnualNormHours,
        MaxDailyHours = MaxDailyHours,
        MinimumRestHours = MinimumRestHours,
        RestPeriodDerogationAllowed = RestPeriodDerogationAllowed,
        WeeklyMaxHoursReferencePeriod = WeeklyMaxHoursReferencePeriod,
        VoluntaryUnsocialHoursAllowed = VoluntaryUnsocialHoursAllowed,
        DefaultCompensationModel = DefaultCompensationModel,
        EmployeeCompensationChoice = EmployeeCompensationChoice,
        MaxOvertimeHoursPerPeriod = MaxOvertimeHoursPerPeriod,
        OvertimeRequiresPreApproval = OvertimeRequiresPreApproval,
    };
}
