namespace StatsTid.Backend.Api.Contracts;

public sealed class RegisterAbsenceRequest
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }
    public decimal Hours { get; init; } = 7.4m;
    public required string AgreementCode { get; init; }
    public string OkVersion { get; init; } = "OK24";
}
