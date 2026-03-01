namespace StatsTid.Backend.Api.Contracts;

public sealed class WeeklyCalculateRequest
{
    public required string EmployeeId { get; init; }
    public required string AgreementCode { get; init; }
    public string OkVersion { get; init; } = "OK24";
    public required DateOnly WeekStartDate { get; init; }
    public decimal WeeklyNormHours { get; init; } = 37.0m;
    public decimal PartTimeFraction { get; init; } = 1.0m;
    public decimal PreviousFlexBalance { get; init; } = 0m;
}
