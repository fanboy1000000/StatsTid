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

    // Compensation-specific types (Sprint 17)
    public const string Overtime50Payout = "OVERTIME_50_PAYOUT";
    public const string Overtime50Afspadsering = "OVERTIME_50_AFSPADSERING";
    public const string Overtime100Payout = "OVERTIME_100_PAYOUT";
    public const string Overtime100Afspadsering = "OVERTIME_100_AFSPADSERING";
    public const string MerarbejdePayout = "MERARBEJDE_PAYOUT";
    public const string MerarbejdeAfspadsering = "MERARBEJDE_AFSPADSERING";
}
