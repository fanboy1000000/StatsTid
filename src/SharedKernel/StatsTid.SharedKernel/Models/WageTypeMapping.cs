namespace StatsTid.SharedKernel.Models;

public sealed class WageTypeMapping
{
    public required string TimeType { get; init; }
    public required string WageType { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public string? Description { get; init; }
}
