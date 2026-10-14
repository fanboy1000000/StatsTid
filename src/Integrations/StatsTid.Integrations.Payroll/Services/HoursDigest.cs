using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14513 (ruling R15, refinement A7) — the lossless digest of the hour rows a calculation
/// consumed, recorded in <c>PlanInputs.EntriesDigest</c> / <c>PlanInputs.AbsencesDigest</c> and recomputed
/// by a replay over the rows it re-reads by recorded event id.
///
/// <para>
/// <b>Why it exists (plain language).</b> A payslip must be provable later. A replay re-reads the exact
/// rows a calculation recorded (by their event ids) and recomputes this digest: equal digests prove the
/// rows still say what they said when the month was paid; a different digest refuses the replay by name
/// (<c>replay-entries-digest-mismatch</c> / <c>replay-absences-digest-mismatch</c>) instead of quietly
/// paying from changed data.
/// </para>
///
/// <para>
/// <b>Lossless, and over the rows AS READ (R15).</b> The digest covers the rows exactly as the hours read
/// returned them, BEFORE the midnight-crossing normalizer (ADR-039 D6) splits a night shift: the normalizer
/// divides on ticks and keeps the exact remainder, so normalized hours can carry more than four decimals,
/// and a digest over them would depend on the normalizer's code as well as on the data (that code is
/// covered by the evaluation-model marker instead). Nothing is rounded: two row sets that differ in the
/// fifth decimal of an hours value, or by 100 ns in a clock time, are different data and hash differently.
/// </para>
///
/// <para>
/// <b>The line forms (refinement A7, :381-388, as amended by adjudicator ruling R-13).</b> One line per row:
/// <list type="bullet">
/// <item>entries: <c>EventId|Date|StartTime|EndTime|Hours|hex(ActivityType)|VoluntaryUnsocialHours</c>;</item>
/// <item>absences: <c>EventId|Date|hex(AbsenceType)|Hours</c>.</item>
/// </list>
/// The event id in its <c>D</c> form (lowercase, hyphenated); the date <c>yyyy-MM-dd</c>; times
/// <c>HH:mm:ss.fffffff</c> (full <see cref="TimeOnly"/> precision) or empty; <c>Hours</c> an invariant
/// decimal, value-normalised (no rounding, no trailing zeros: <c>7.40</c> → <c>7.4</c>, <c>8.0</c> →
/// <c>8</c>) — the same form as the Rule Engine's config fingerprint; booleans <c>true</c>/<c>false</c>;
/// the typed text (<c>ActivityType</c>, <c>AbsenceType</c>) as <c>hex(…)</c>, the uppercase hex of its UTF-8
/// bytes (<see cref="Convert.ToHexString(byte[])"/>), with null and empty both as an empty field; any other
/// null as empty. The lines are sorted ordinally, joined with <c>\n</c>, encoded UTF-8 and hashed with
/// SHA-256; the result is lowercase hex (64 characters). Sorting makes the digest independent of the order
/// the rows were read in.
/// </para>
///
/// <para>
/// <b>Why typed text is hex-encoded (R-13, plain language).</b> Every other field has a fixed shape that can
/// never contain <c>|</c> or a newline. The activity and absence types are text a person or an import can type,
/// so unencoded they could: one row whose type text reads <c>"X|true\n"</c> followed by a second row's line
/// would produce exactly the joined text of two rows, and the two different row sets would share a digest. A
/// replay would then accept changed data as unchanged. Hex digits contain neither separator, so the line
/// boundaries are unambiguous by construction, and nothing is lost (it decodes back to the exact bytes).
/// </para>
///
/// <para>
/// <b>What is deliberately NOT in a line.</b>
/// <list type="bullet">
/// <item>Any column that changes after a row is written. <c>superseded_by_outbox_id</c> is stamped on an
/// older Skema row when the employee re-saves the cell; if it were digested, a re-save would make an
/// untouched bound row fail its replay (TASK-14513 Do 1; adjudicator pointer at TASK-14508's Step 5a).</item>
/// <item>The absence row's <c>Feriedage</c> (the ADR-032 vacation-day revaluation). No payroll rule reads it,
/// and a from-scratch backfill stamps it where the live save leaves it NULL (QUAL-197), so digesting it
/// would refuse a replay over a rebuilt projection although no paid figure differs.</item>
/// <item>Fields no rule reads (task id, agreement code, OK version, occurred-at, actor, outbox id). The
/// forms cover exactly the fields the rules read (<c>SupplementRule</c>, <c>OnCallDutyRule</c>,
/// <c>CallInWorkRule</c>, <c>TravelTimeRule</c>, the absence rule); a rule that starts reading another
/// field adds it here and bumps the evaluation-model marker.</item>
/// </list>
/// </para>
///
/// <para>Pure: the result depends on the rows alone (no clock, no culture, no I/O).</para>
/// </summary>
public static class HoursDigest
{
    /// <summary>
    /// The lowercase-hex SHA-256 digest of the time-entry rows exactly as read (see the class remarks for
    /// the line form). An empty list hashes the empty string.
    /// </summary>
    public static string ComputeEntriesDigest(IReadOnlyList<TimeEntryProjectionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Digest(rows.Select(EntryLine));
    }

    /// <summary>
    /// The lowercase-hex SHA-256 digest of the absence rows exactly as read (see the class remarks for the
    /// line form; <c>Feriedage</c> is not part of it). An empty list hashes the empty string.
    /// </summary>
    public static string ComputeAbsencesDigest(IReadOnlyList<AbsenceProjectionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Digest(rows.Select(AbsenceLine));
    }

    /// <summary>One entry line: <c>EventId|Date|StartTime|EndTime|Hours|hex(ActivityType)|VoluntaryUnsocialHours</c>.</summary>
    internal static string EntryLine(TimeEntryProjectionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join('|',
            row.EventId.ToString("D", CultureInfo.InvariantCulture),
            Date(row.Date),
            Time(row.StartTime),
            Time(row.EndTime),
            Number(row.Hours),
            TypedText(row.ActivityType),
            row.VoluntaryUnsocialHours ? "true" : "false");
    }

    /// <summary>One absence line: <c>EventId|Date|hex(AbsenceType)|Hours</c>.</summary>
    internal static string AbsenceLine(AbsenceProjectionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join('|',
            row.EventId.ToString("D", CultureInfo.InvariantCulture),
            Date(row.Date),
            TypedText(row.AbsenceType),
            Number(row.Hours));
    }

    private static string Digest(IEnumerable<string> lines)
    {
        var canonical = string.Join('\n', lines.OrderBy(line => line, StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Date(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Time(TimeOnly? time) =>
        time is { } t ? t.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// S145 R-13 — a typed-text field (<c>ActivityType</c>, <c>AbsenceType</c>) as the hex of its UTF-8 bytes
    /// (<c>"NORMAL"</c> → <c>4E4F524D414C</c>); null and empty both give an empty field. Hex digits contain
    /// neither the field separator <c>|</c> nor the line separator <c>\n</c>, so no typed value can imitate a
    /// field or row boundary, and the encoding loses nothing (every distinct string has distinct bytes).
    /// </summary>
    private static string TypedText(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// The value-normalised invariant form: trailing zeros removed without changing the value
    /// (<c>7.40</c> → <c>7.4</c>, <c>8.0</c> → <c>8</c>), never rounded. The same normalisation as the Rule
    /// Engine's <c>ConfigFingerprint</c> (refinement A7: "the same form as the fingerprint").
    /// </summary>
    private static string Number(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
