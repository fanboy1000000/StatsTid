namespace StatsTid.SharedKernel.Models;

public sealed class OvertimeResult
{
    public required string OvertimeType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
}

public static class OvertimeTypes
{
    public const string Merarbejde = "MERARBEJDE";
    public const string Overtime50 = "OVERTIME_50";
    public const string Overtime100 = "OVERTIME_100";
}
