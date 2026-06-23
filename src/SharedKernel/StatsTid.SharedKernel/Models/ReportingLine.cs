namespace StatsTid.SharedKernel.Models;

public sealed class ReportingLine
{
    public required Guid ReportingLineId { get; init; }
    public required string EmployeeId { get; init; }
    public required string ManagerId { get; init; }
    public required string OrganisationId { get; init; }
    public required string Relationship { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public required string Source { get; init; }
    public required long Version { get; init; }
    public DateOnly? ScheduledExpiry { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
