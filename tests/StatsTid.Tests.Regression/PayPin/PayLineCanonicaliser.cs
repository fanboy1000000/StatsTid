using System.Globalization;
using System.Text;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>One payroll export record's balance triple for an employee-month (null when the record carries none).</summary>
internal sealed record PayRecord(string EmployeeId, int Year, int Month, decimal? Opening, decimal? Closing, string? Provenance);

/// <summary>
/// S148 / TASK-14800 (PIN-1, R-4) — turns what payroll produced for a demo world into ONE canonical text, the form the
/// golden files hold and the form two runs are compared in.
///
/// <para>
/// <b>Format</b> (LF line endings, a final LF; "canonical" means the same bytes on any machine):
/// <code>
/// &lt;header lines, as given&gt;
/// record|&lt;employee&gt;|&lt;yyyy-MM&gt;|opening=&lt;n&gt;|closing=&lt;n&gt;|provenance=&lt;s&gt;
/// line|&lt;employee&gt;|&lt;start&gt;|&lt;end&gt;|&lt;wageType&gt;|&lt;sourceRuleId&gt;|&lt;sourceTimeType&gt;|&lt;hours&gt;|&lt;amount&gt;|&lt;okVersion&gt;
/// </code>
/// Records come first, sorted by (employee, year, month); lines follow, sorted by (EmployeeId, PeriodStart, WageType,
/// SourceRuleId, SourceTimeType, Hours, Amount, OkVersion). Strings sort by ordinal comparison, with a null as the empty
/// string; numbers sort by value. A null string prints as <c>~</c> and a null balance as <c>~</c>. Dates print as ISO
/// <c>yyyy-MM-dd</c>. Numbers print with the invariant culture and no trailing zeros, so 7.40 and 7.4 are the same figure
/// and a later change that moves only the scale moves no byte. The manifest id, which is new on every calculation, is
/// left out; every other <see cref="PayrollExportLine"/> field is in.
/// </para>
/// </summary>
internal static class PayLineCanonicaliser
{
    private const string Null = "~";

    internal static string Canonicalise(
        IEnumerable<string> header, IEnumerable<PayrollExportLine> lines, IEnumerable<PayRecord> records)
    {
        var sb = new StringBuilder();
        foreach (var h in header)
            sb.Append(h).Append('\n');

        foreach (var r in records
                     .OrderBy(r => r.EmployeeId, StringComparer.Ordinal)
                     .ThenBy(r => r.Year)
                     .ThenBy(r => r.Month))
        {
            sb.Append("record|").Append(r.EmployeeId)
              .Append('|').Append(r.Year.ToString("D4", CultureInfo.InvariantCulture))
              .Append('-').Append(r.Month.ToString("D2", CultureInfo.InvariantCulture))
              .Append("|opening=").Append(Number(r.Opening))
              .Append("|closing=").Append(Number(r.Closing))
              .Append("|provenance=").Append(r.Provenance ?? Null)
              .Append('\n');
        }

        foreach (var l in lines
                     .OrderBy(l => l.EmployeeId, StringComparer.Ordinal)
                     .ThenBy(l => l.PeriodStart)
                     .ThenBy(l => l.WageType, StringComparer.Ordinal)
                     .ThenBy(l => l.SourceRuleId ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(l => l.SourceTimeType ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(l => l.Hours)
                     .ThenBy(l => l.Amount)
                     .ThenBy(l => l.OkVersion, StringComparer.Ordinal))
        {
            sb.Append("line|").Append(l.EmployeeId)
              .Append('|').Append(Date(l.PeriodStart))
              .Append('|').Append(Date(l.PeriodEnd))
              .Append('|').Append(l.WageType)
              .Append('|').Append(l.SourceRuleId ?? Null)
              .Append('|').Append(l.SourceTimeType ?? Null)
              .Append('|').Append(Number(l.Hours))
              .Append('|').Append(Number(l.Amount))
              .Append('|').Append(l.OkVersion)
              .Append('\n');
        }

        return sb.ToString();
    }

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal? d) =>
        d is null ? Null : Normalise(d.Value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Dividing by 1.000…0 (28 places) drops every trailing zero of the scale and changes no value.</summary>
    private static decimal Normalise(decimal d) => d / 1.0000000000000000000000000000m;
}
