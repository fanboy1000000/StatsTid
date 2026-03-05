namespace StatsTid.SharedKernel.Models;

public sealed class Project
{
    public required Guid ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectCode { get; init; }
    public required string ProjectName { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
