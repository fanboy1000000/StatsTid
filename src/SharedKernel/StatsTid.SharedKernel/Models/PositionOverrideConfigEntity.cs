namespace StatsTid.SharedKernel.Models;

public sealed class PositionOverrideConfigEntity
{
    public required Guid OverrideId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string PositionCode { get; init; }
    public required string Status { get; init; } // ACTIVE or INACTIVE
    public decimal? MaxFlexBalance { get; init; }
    public decimal? FlexCarryoverMax { get; init; }
    public int? NormPeriodWeeks { get; init; }
    public decimal? WeeklyNormHours { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    public string? Description { get; init; }

    /// <summary>
    /// Converts to the existing PositionConfigOverride record used by PositionOverrideConfigs.ApplyOverride()
    /// </summary>
    public StatsTid.SharedKernel.Config.PositionOverrideConfigs.PositionConfigOverride ToPositionConfigOverride()
    {
        return new StatsTid.SharedKernel.Config.PositionOverrideConfigs.PositionConfigOverride
        {
            MaxFlexBalance = MaxFlexBalance,
            FlexCarryoverMax = FlexCarryoverMax,
            NormPeriodWeeks = NormPeriodWeeks,
            WeeklyNormHours = WeeklyNormHours,
        };
    }
}
