using System.Globalization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Formats PayrollExportLines into SLS-compatible pipe-delimited text format.
/// Pure formatting function — no I/O, no state.
///
/// Format (TASK-2010, ADR-016 D10 manifest linkage):
/// - Header: H|{ExportId}|{Timestamp}|{RecordCount}|{ManifestId}
/// - Data:   D|{EmployeeId}|{WageType}|{Hours:F2}|{Amount:F2}|{PeriodStart:yyyyMMdd}|{PeriodEnd:yyyyMMdd}|{OkVersion}|{ManifestId}
/// - Trailer: T|{RecordCount}|{TotalHours:F2}|{TotalAmount:F2}|{Checksum}
///
/// <para>
/// ManifestId column semantics — the new manifest column always renders, as either a
/// guid or empty:
/// - Header carries the dominant export-level manifest id (caller-supplied — typically the
///   first or only manifest for the batch). When all lines pre-date manifest plumbing
///   (legacy callers passing <see cref="System.Guid.Empty"/>) the trailing field renders as
///   an empty string (i.e. "...|3|") — the column is present, the value is empty.
/// - Per-line ManifestId is the line's own <see cref="PayrollExportLine.ManifestId"/>;
///   <see cref="System.Guid.Empty"/> renders as empty string ("D|...|OK24|").
/// </para>
/// </summary>
public static class SlsExportFormatter
{
    public static string Format(
        IReadOnlyList<PayrollExportLine> lines,
        string exportId,
        DateTime exportTimestamp,
        Guid manifestId = default)
    {
        var sb = new System.Text.StringBuilder();

        var ic = CultureInfo.InvariantCulture;

        // Header record. ManifestId renders empty when Guid.Empty (pre-S20 legacy callers).
        var headerManifest = manifestId == Guid.Empty ? string.Empty : manifestId.ToString();
        sb.AppendLine(string.Format(ic, "H|{0}|{1:yyyy-MM-dd HH:mm:ss}|{2}|{3}",
            exportId, exportTimestamp, lines.Count, headerManifest));

        // Data records
        foreach (var line in lines)
        {
            var lineManifest = line.ManifestId == Guid.Empty ? string.Empty : line.ManifestId.ToString();
            sb.AppendLine(string.Format(ic, "D|{0}|{1}|{2:F2}|{3:F2}|{4:yyyyMMdd}|{5:yyyyMMdd}|{6}|{7}",
                line.EmployeeId, line.WageType, line.Hours, line.Amount, line.PeriodStart, line.PeriodEnd,
                line.OkVersion, lineManifest));
        }

        // Trailer record with checksum (unchanged).
        var totalHours = lines.Sum(l => l.Hours);
        var totalAmount = lines.Sum(l => l.Amount);
        var checksum = CalculateChecksum(lines);
        sb.Append(string.Format(ic, "T|{0}|{1:F2}|{2:F2}|{3}", lines.Count, totalHours, totalAmount, checksum));

        return sb.ToString();
    }

    /// <summary>
    /// Formats CorrectionExportLines into SLS-compatible correction export format.
    /// Pure formatting function — no I/O, no state.
    ///
    /// Format:
    /// - Header:  HC|{ExportId}|{Timestamp}|{RecordCount}
    /// - Data:    C|{EmployeeId}|{WageType}|{OrigHours:F2}|{CorrHours:F2}|{DiffHours:F2}|{DiffAmount:F2}|{PeriodStart:yyyyMMdd}|{PeriodEnd:yyyyMMdd}|{OkVersion}
    /// - Trailer: TC|{RecordCount}|{TotalDiffHours:F2}|{TotalDiffAmount:F2}|{Checksum}
    /// </summary>
    public static string FormatCorrections(
        IReadOnlyList<CorrectionExportLine> lines,
        string exportId,
        DateTime exportTimestamp)
    {
        var sb = new System.Text.StringBuilder();
        var ic = CultureInfo.InvariantCulture;

        // Header correction record
        sb.AppendLine(string.Format(ic, "HC|{0}|{1:yyyy-MM-dd HH:mm:ss}|{2}", exportId, exportTimestamp, lines.Count));

        // Correction data records
        foreach (var line in lines)
        {
            sb.AppendLine(string.Format(ic, "C|{0}|{1}|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6:yyyyMMdd}|{7:yyyyMMdd}|{8}",
                line.EmployeeId, line.WageType, line.OriginalHours, line.CorrectedHours,
                line.DifferenceHours, line.DifferenceAmount,
                line.PeriodStart, line.PeriodEnd, line.OkVersion));
        }

        // Trailer correction record with checksum
        var totalDiffHours = lines.Sum(l => l.DifferenceHours);
        var totalDiffAmount = lines.Sum(l => l.DifferenceAmount);
        var checksum = CalculateCorrectionChecksum(lines);
        sb.Append(string.Format(ic, "TC|{0}|{1:F2}|{2:F2}|{3}", lines.Count, totalDiffHours, totalDiffAmount, checksum));

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

    /// <summary>
    /// Correction checksum: sum of absolute differences multiplied by 100, truncated to long.
    /// Uses absolute values to ensure checksum is always positive regardless of correction direction.
    /// Deterministic and reproducible.
    /// </summary>
    private static long CalculateCorrectionChecksum(IReadOnlyList<CorrectionExportLine> lines)
    {
        decimal sum = 0;
        foreach (var line in lines)
        {
            sum += Math.Abs(line.DifferenceHours) * 100 + Math.Abs(line.DifferenceAmount) * 100;
        }
        return (long)sum;
    }
}
