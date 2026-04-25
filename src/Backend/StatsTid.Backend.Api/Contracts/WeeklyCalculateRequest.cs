namespace StatsTid.Backend.Api.Contracts;

public sealed class WeeklyCalculateRequest
{
    public required string EmployeeId { get; init; }
    public required string AgreementCode { get; init; }

    /// <summary>
    /// DEPRECATED: Server resolves OkVersion from <see cref="WeekStartDate"/> via entry-date
    /// resolution (ADR-003). This field is retained for backward compatibility but its value
    /// is IGNORED — the server-resolved value is used unconditionally.
    /// </summary>
    [Obsolete("Server-resolved from week start date; kept for backward compatibility")]
    public string OkVersion { get; init; } = "OK24";
    public required DateOnly WeekStartDate { get; init; }
    public decimal WeeklyNormHours { get; init; } = 37.0m;
    public decimal PartTimeFraction { get; init; } = 1.0m;
    public decimal PreviousFlexBalance { get; init; } = 0m;
}
