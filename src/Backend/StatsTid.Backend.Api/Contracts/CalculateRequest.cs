namespace StatsTid.Backend.Api.Contracts;

public sealed class CalculateRequest
{
    public required string EmployeeId { get; init; }
    public required string AgreementCode { get; init; }
    public string OkVersion { get; init; } = "OK24";
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public decimal WeeklyNormHours { get; init; } = 37.0m;
    public decimal PartTimeFraction { get; init; } = 1.0m;
}
