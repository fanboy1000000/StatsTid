using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S145 / TASK-14513 (R-13) — the lossless hours digest (<see cref="HoursDigest"/>) cannot be fooled by typed
/// text that looks like the digest's own separators.
///
/// <para>
/// Plain language: a replay proves a month by hashing the hour rows it re-reads and comparing with the hash
/// recorded when the month was paid. The hash is built from one text line per row, with <c>|</c> between the
/// fields and a newline between the rows. <c>ActivityType</c> (and an absence's <c>AbsenceType</c>) is text a
/// person or an import can type. If that text may contain <c>|</c> and a newline, two DIFFERENT sets of rows
/// can produce the SAME lines, and so the same hash: a replay would then accept changed data as unchanged.
/// The ruling (R-13) makes typed text safe by writing it as hex in the line, which contains neither
/// separator.
/// </para>
///
/// <para>
/// <b>The collision, built from the spec's line form</b> (refinement A7: entries
/// <c>EventId|Date|StartTime|EndTime|Hours|ActivityType|VoluntaryUnsocialHours</c>, absences
/// <c>EventId|Date|AbsenceType|Hours</c>; lines sorted ordinally and joined with a newline). Two rows r1, r2
/// (ids g1 &lt; g2, same date, times and hours) differing only in their type text give the lines
/// <c>g1|d|s|e|h|X|true</c> and <c>g2|d|s|e|h|Y|true</c>. A single row r1' whose type text is
/// <c>"X|true\n" + g2 + "|d|s|e|h|Y"</c> gives the one line <c>g1|d|s|e|h|X|true\ng2|d|s|e|h|Y|true</c>:
/// byte for byte the same joined text, so the unencoded digests are equal although one row is not two.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> On the unencoded form (today, 9984173) <c>ComputeEntriesDigest([r1, r2])</c> equals
/// <c>ComputeEntriesDigest([r1'])</c> and the <c>NotEqual</c> assertion fails. Mutation: write the type text
/// unencoded again in <c>EntryLine</c> / <c>AbsenceLine</c> (drop the hex encoding). Green once the type text
/// is hex-encoded.
/// </para>
/// </summary>
public sealed class HoursDigestTests
{
    private const string Emp = "EMP-DIGEST";

    // Ordinal order on the D form: g1 < g2.
    private static readonly Guid G1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid G2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly DateOnly Day = new(2026, 10, 5);
    private static readonly TimeOnly Start = new(8, 0, 0);
    private static readonly TimeOnly End = new(15, 24, 0);
    private const decimal Hours = 7.4m;

    // The spec's text for the fixed fields of a line (refinement A7): date yyyy-MM-dd, times
    // HH:mm:ss.fffffff, hours as a value-normalised invariant decimal.
    private const string DateText = "2026-10-05";
    private const string StartText = "08:00:00.0000000";
    private const string EndText = "15:24:00.0000000";
    private const string HoursText = "7.4";

    private static TimeEntryProjectionRow Entry(Guid id, string? activityType) => new()
    {
        EventId = id,
        EmployeeId = Emp,
        Date = Day,
        StartTime = Start,
        EndTime = End,
        Hours = Hours,
        ActivityType = activityType,
        VoluntaryUnsocialHours = true,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };

    private static AbsenceProjectionRow Absence(Guid id, string absenceType) => new()
    {
        EventId = id,
        EmployeeId = Emp,
        Date = Day,
        AbsenceType = absenceType,
        Hours = Hours,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };

    /// <summary>
    /// S145 R-13: two rows (g1 "X", g2 "Y") and one row whose ActivityType smuggles the second row's whole line
    /// inside it are DIFFERENT data and must hash differently. The assertion is on the pair, so it fails exactly
    /// when the encoding lets two rows merge into one.
    /// </summary>
    [Fact]
    public void ActivityTypeContainingSeparatorAndNewline_CannotMergeTwoRows()
    {
        // S145 R-13:
        var twoRows = new[] { Entry(G1, "X"), Entry(G2, "Y") };

        var smuggled = "X|true\n" + G2.ToString("D")
            + $"|{DateText}|{StartText}|{EndText}|{HoursText}|Y";
        var oneRow = new[] { Entry(G1, smuggled) };

        Assert.NotEqual(
            HoursDigest.ComputeEntriesDigest(twoRows),
            HoursDigest.ComputeEntriesDigest(oneRow));
    }

    /// <summary>
    /// S145 R-13, the absence twin: <c>AbsenceType</c> is typed text too. One absence row whose type text carries
    /// the next row's line must not collide with the two rows it imitates. Red conditions as above (the unencoded
    /// absence line <c>g|d|type|hours</c> merges the same way).
    /// </summary>
    [Fact]
    public void AbsenceTypeContainingSeparatorAndNewline_CannotMergeTwoRows()
    {
        // S145 R-13:
        var twoRows = new[] { Absence(G1, "X"), Absence(G2, "Y") };

        var smuggled = $"X|{HoursText}\n" + G2.ToString("D") + $"|{DateText}|Y";
        var oneRow = new[] { Absence(G1, smuggled) };

        Assert.NotEqual(
            HoursDigest.ComputeAbsencesDigest(twoRows),
            HoursDigest.ComputeAbsencesDigest(oneRow));
    }
}
