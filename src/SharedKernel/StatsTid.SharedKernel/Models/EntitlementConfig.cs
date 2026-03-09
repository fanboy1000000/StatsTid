namespace StatsTid.SharedKernel.Models;

public enum EntitlementType
{
    VACATION,
    SPECIAL_HOLIDAY,
    CARE_DAY,
    CHILD_SICK,
    SENIOR_DAY
}

public enum AccrualModel
{
    IMMEDIATE,
    MONTHLY_ACCRUAL
}

public sealed class EntitlementConfig
{
    public required Guid ConfigId { get; init; }
    public required string EntitlementType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required decimal AnnualQuota { get; init; }
    public required string AccrualModel { get; init; }
    public required int ResetMonth { get; init; }
    public required decimal CarryoverMax { get; init; }
    public required bool ProRateByPartTime { get; init; }
    public required bool IsPerEpisode { get; init; }
    public int? MinAge { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
