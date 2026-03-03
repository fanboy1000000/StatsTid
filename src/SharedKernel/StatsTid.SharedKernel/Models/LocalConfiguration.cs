namespace StatsTid.SharedKernel.Models;

public sealed class LocalConfiguration
{
    public required Guid ConfigId { get; init; }
    public required string OrgId { get; init; }
    public required string ConfigArea { get; init; }  // WORKING_TIME, FLEX_RULES, ORG_STRUCTURE, LOCAL_AGREEMENT, OPERATIONAL
    public required string ConfigKey { get; init; }
    public required string ConfigValue { get; init; }  // JSON string
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public int Version { get; init; } = 1;
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string CreatedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
