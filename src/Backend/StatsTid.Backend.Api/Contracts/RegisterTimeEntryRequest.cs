namespace StatsTid.Backend.Api.Contracts;

public sealed class RegisterTimeEntryRequest
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Hours { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public string? TaskId { get; init; }
    public string? ActivityType { get; init; }
    public required string AgreementCode { get; init; }

    /// <summary>
    /// DEPRECATED: Server resolves OkVersion from <see cref="Date"/> via entry-date resolution
    /// (ADR-003). This field is retained for backward compatibility with existing callers but
    /// its value is IGNORED — the server-resolved value is used unconditionally. A mismatch
    /// between the supplied and resolved value is logged as a warning but does not reject the
    /// request.
    /// </summary>
    [Obsolete("Server-resolved from entry date; kept for backward compatibility")]
    public string OkVersion { get; init; } = "OK24";
}
