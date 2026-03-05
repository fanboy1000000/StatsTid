namespace StatsTid.SharedKernel.Models;

public sealed class AbsenceTypeVisibility
{
    public required Guid Id { get; init; }
    public required string OrgId { get; init; }
    public required string AbsenceType { get; init; }
    public bool IsHidden { get; init; }
    public required string SetBy { get; init; }
}
