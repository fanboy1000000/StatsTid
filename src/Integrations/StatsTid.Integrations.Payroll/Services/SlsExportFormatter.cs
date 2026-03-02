using System.Globalization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Formats PayrollExportLines into SLS-compatible pipe-delimited text format.
/// Pure formatting function — no I/O, no state.
///
/// Format:
/// - Header: H|{ExportId}|{Timestamp}|{RecordCount}
/// - Data:   D|{EmployeeId}|{WageType}|{Hours:F2}|{Amount:F2}|{PeriodStart:yyyyMMdd}|{PeriodEnd:yyyyMMdd}|{OkVersion}
/// - Trailer: T|{RecordCount}|{TotalHours:F2}|{TotalAmount:F2}|{Checksum}
/// </summary>
public static class SlsExportFormatter
{
    public static string Format(
        IReadOnlyList<PayrollExportLine> lines,
        string exportId,
        DateTime exportTimestamp)
    {
        var sb = new System.Text.StringBuilder();

        var ic = CultureInfo.InvariantCulture;

        // Header record
        sb.AppendLine(string.Format(ic, "H|{0}|{1:yyyy-MM-dd HH:mm:ss}|{2}", exportId, exportTimestamp, lines.Count));

        // Data records
        foreach (var line in lines)
        {
            sb.AppendLine(string.Format(ic, "D|{0}|{1}|{2:F2}|{3:F2}|{4:yyyyMMdd}|{5:yyyyMMdd}|{6}",
                line.EmployeeId, line.WageType, line.Hours, line.Amount, line.PeriodStart, line.PeriodEnd, line.OkVersion));
        }

        // Trailer record with checksum
        var totalHours = lines.Sum(l => l.Hours);
        var totalAmount = lines.Sum(l => l.Amount);
        var checksum = CalculateChecksum(lines);
        sb.Append(string.Format(ic, "T|{0}|{1:F2}|{2:F2}|{3}", lines.Count, totalHours, totalAmount, checksum));

        return sb.ToString();
    }

    /// <summary>
    /// Simple checksum: sum of all hours and amounts multiplied, truncated to integer.
    /// Deterministic and reproducible.
    /// </summary>
    private static long CalculateChecksum(IReadOnlyList<PayrollExportLine> lines)
    {
        decimal sum = 0;
        foreach (var line in lines)
        {
            sum += line.Hours * 100 + line.Amount * 100;
        }
        return (long)sum;
    }
}
