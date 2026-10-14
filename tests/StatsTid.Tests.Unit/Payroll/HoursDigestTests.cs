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

    // ── R-23: every field of a digest line is covered ───────────────────────────────

    /// <summary>
    /// S145 R-23 (guard, green today and after the fix-up): changing ONLY one field of a time-entry row changes the
    /// entries digest. The digest proves "these rows are unchanged"; a field left out of the line would let that
    /// field change unnoticed. One case per line field of refinement A7
    /// (<c>EventId|Date|StartTime|EndTime|Hours|ActivityType|VoluntaryUnsocialHours</c>).
    ///
    /// <para>
    /// <b>Mutation per case</b> (each makes exactly its case fail): drop that field from <c>EntryLine</c> in
    /// <see cref="HoursDigest"/>. The assertion tripped is the <c>NotEqual</c> of the two digests (they become
    /// equal when the field is not hashed). Note for <c>EventId</c>: with one row, dropping the id from the line
    /// does not change the sort either, so the case isolates the id's own presence in the line.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("EventId")]
    [InlineData("Date")]
    [InlineData("StartTime")]
    [InlineData("EndTime")]
    [InlineData("Hours")]
    [InlineData("ActivityType")]
    [InlineData("VoluntaryUnsocialHours")]
    public void EntriesDigest_ChangingOnlyOneField_ChangesTheDigest(string field)
    {
        // S145 R-23:
        var baseline = EntryWith(G1, Day, Start, End, Hours, "X", true);
        var changed = field switch
        {
            "EventId" => EntryWith(G2, Day, Start, End, Hours, "X", true),
            "Date" => EntryWith(G1, Day.AddDays(1), Start, End, Hours, "X", true),
            "StartTime" => EntryWith(G1, Day, new TimeOnly(8, 30, 0), End, Hours, "X", true),
            "EndTime" => EntryWith(G1, Day, Start, new TimeOnly(16, 0, 0), Hours, "X", true),
            "Hours" => EntryWith(G1, Day, Start, End, 7.5m, "X", true),
            "ActivityType" => EntryWith(G1, Day, Start, End, Hours, "Y", true),
            "VoluntaryUnsocialHours" => EntryWith(G1, Day, Start, End, Hours, "X", false),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.NotEqual(
            HoursDigest.ComputeEntriesDigest(new[] { baseline }),
            HoursDigest.ComputeEntriesDigest(new[] { changed }));
    }

    /// <summary>
    /// S145 R-23 (guard): the same for an absence row, one case per line field
    /// (<c>EventId|Date|AbsenceType|Hours</c>). Mutation per case: drop that field from <c>AbsenceLine</c>.
    /// </summary>
    [Theory]
    [InlineData("EventId")]
    [InlineData("Date")]
    [InlineData("AbsenceType")]
    [InlineData("Hours")]
    public void AbsencesDigest_ChangingOnlyOneField_ChangesTheDigest(string field)
    {
        // S145 R-23:
        var baseline = AbsenceWith(G1, Day, "X", Hours);
        var changed = field switch
        {
            "EventId" => AbsenceWith(G2, Day, "X", Hours),
            "Date" => AbsenceWith(G1, Day.AddDays(1), "X", Hours),
            "AbsenceType" => AbsenceWith(G1, Day, "Y", Hours),
            "Hours" => AbsenceWith(G1, Day, "X", 7.5m),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.NotEqual(
            HoursDigest.ComputeAbsencesDigest(new[] { baseline }),
            HoursDigest.ComputeAbsencesDigest(new[] { changed }));
    }

    private static TimeEntryProjectionRow EntryWith(
        Guid id, DateOnly date, TimeOnly start, TimeOnly end, decimal hours, string? activityType, bool voluntary) => new()
    {
        EventId = id,
        EmployeeId = Emp,
        Date = date,
        StartTime = start,
        EndTime = end,
        Hours = hours,
        ActivityType = activityType,
        VoluntaryUnsocialHours = voluntary,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };

    private static AbsenceProjectionRow AbsenceWith(Guid id, DateOnly date, string absenceType, decimal hours) => new()
    {
        EventId = id,
        EmployeeId = Emp,
        Date = date,
        AbsenceType = absenceType,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };
}
