namespace StatsTid.SharedKernel.Models;

public sealed class SupplementResult
{
    public required string SupplementType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
}

public static class SupplementTypes
{
    public const string Evening = "EVENING_SUPPLEMENT";
    public const string Night = "NIGHT_SUPPLEMENT";
    public const string Weekend = "WEEKEND_SUPPLEMENT";
    public const string Holiday = "HOLIDAY_SUPPLEMENT";
}
