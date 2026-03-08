namespace StatsTid.SharedKernel.Models;

public sealed class EmploymentProfile
{
    public required string EmployeeId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required decimal WeeklyNormHours { get; init; }
    public required string EmploymentCategory { get; init; }
    public bool IsPartTime { get; init; }
    public decimal PartTimeFraction { get; init; } = 1.0m;

    /// <summary>
    /// Position code for position-based rule overrides (e.g. "DEPARTMENT_HEAD", "RESEARCHER").
    /// Null means no position override — base agreement config applies.
    /// </summary>
    public string? Position { get; init; }
}
