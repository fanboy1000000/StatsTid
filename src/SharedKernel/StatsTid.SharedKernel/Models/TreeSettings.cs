namespace StatsTid.SharedKernel.Models;

public sealed class TreeSettings
{
    public required string TreeRootOrgId { get; init; }
    public required string EnforcementMode { get; init; }
    public required long Version { get; init; }
    public required string UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
}
