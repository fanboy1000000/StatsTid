namespace StatsTid.SharedKernel.Models;

public sealed class RoleAssignment
{
    public required Guid AssignmentId { get; init; }
    public required string UserId { get; init; }
    public required string RoleId { get; init; }
    public string? OrgId { get; init; }  // NULL = global scope
    public required string ScopeType { get; init; }  // GLOBAL, ORG_ONLY, ORG_AND_DESCENDANTS
    public required string AssignedBy { get; init; }
    public DateTime AssignedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; init; }
    public bool IsActive { get; init; } = true;
}
