using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    // ═══ TASK-14515 (R15): lossless digest property facts, appended at re-spawn 1 ═══
    // Plain language: a replay proves a month by hashing the hour rows as read; the hash must be LOSSLESS
    // (fifth decimal, 100 ns) and over the rows BEFORE midnight normalization. Expected hashes are computed
    // here from the spec line forms (refinement A7 as amended by R-13), independently of the product.

    private static readonly Guid DIdA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid DIdB = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly DateOnly DigestDay = new(2026, 9, 15);

    /// <summary>
    /// Two entry sets identical except <c>Hours</c> 7.4 versus 7.40001 (FIFTH decimal) hash differently, and each
    /// equals the hash of its spec-written lines. Spec: refinement :674 and :383 (no rounding).
    /// Mutation (MU-33): write Hours to four decimals; both rows write 7.4, the hashes are equal, and the
    /// inequality and the exact-hash assertion for the second set fail.
    /// </summary>
    [Fact]
    public void HoursDifferingInFifthDecimal_HashDifferently()
    {
        var a = new[] { DEntry(DIdA, hours: 7.4m) };
        var b = new[] { DEntry(DIdA, hours: 7.40001m) };

        var hashA = HoursDigest.ComputeEntriesDigest(a);
        var hashB = HoursDigest.ComputeEntriesDigest(b);

        Assert.Equal(SpecEntriesDigest(a), hashA);
        Assert.Equal(SpecEntriesDigest(b), hashB);
        Assert.NotEqual(hashA, hashB);
    }

    /// <summary>
    /// Two entry sets identical except one clock time differs by 100 ns (one tick) hash differently, for the
    /// start and for the end time, each equal to its spec-written lines (<c>16:00:00.0000000</c> versus
    /// <c>16:00:00.0000001</c>). Spec: refinement :674 and :384.
    /// Mutation (MU-34): times written as <c>HH:mm:ss</c>; the tick vanishes, the hashes are equal, and the
    /// inequality and exact-hash assertions fail.
    /// </summary>
    [Fact]
    public void TimesDifferingBy100ns_HashDifferently()
    {
        var oneTick = TimeSpan.FromTicks(1);
        var baseStart = new TimeOnly(8, 0, 0);
        var baseEnd = new TimeOnly(16, 0, 0);

        var plain = new[] { DEntry(DIdA, start: baseStart, end: baseEnd) };
        var endPlusTick = new[] { DEntry(DIdA, start: baseStart, end: baseEnd.Add(oneTick)) };
        var startPlusTick = new[] { DEntry(DIdA, start: baseStart.Add(oneTick), end: baseEnd) };

        var hashPlain = HoursDigest.ComputeEntriesDigest(plain);
        var hashEnd = HoursDigest.ComputeEntriesDigest(endPlusTick);
        var hashStart = HoursDigest.ComputeEntriesDigest(startPlusTick);

        Assert.Equal(SpecEntriesDigest(plain), hashPlain);
        Assert.Equal(SpecEntriesDigest(endPlusTick), hashEnd);
        Assert.Equal(SpecEntriesDigest(startPlusTick), hashStart);
        Assert.NotEqual(hashPlain, hashEnd);
        Assert.NotEqual(hashPlain, hashStart);
        Assert.NotEqual(hashEnd, hashStart);
    }

    /// <summary>
    /// The digest hashes rows exactly AS READ: a midnight-crossing row (22:00 to 06:00, 8 h) and an ordinary row,
    /// listed out of order, must equal the hash of the two as-read lines sorted ordinally. Spec: R15 and
    /// refinement :379-380.
    /// Mutation (MU-35): digest over normalized entries; the crossing row becomes two lines, the exact hash fails.
    /// </summary>
    [Fact]
    public void HashesRowsAsRead_BeforeNormalization()
    {
        var crossing = DEntry(DIdB, start: new TimeOnly(22, 0, 0), end: new TimeOnly(6, 0, 0), hours: 8.0m);
        var ordinary = DEntry(DIdA, start: new TimeOnly(8, 0, 0), end: new TimeOnly(15, 24, 0), hours: 7.4m);

        var hash = HoursDigest.ComputeEntriesDigest(new[] { crossing, ordinary });

        var expected = Sha256Hex(string.Join("\n", new[]
        {
            "00000000-0000-0000-0000-00000000000a|2026-09-15|08:00:00.0000000|15:24:00.0000000|7.4||false",
            "a0000000-0000-0000-0000-000000000001|2026-09-15|22:00:00.0000000|06:00:00.0000000|8||false",
        }));
        Assert.Equal(expected, hash);
    }

    /// <summary>
    /// Guard (no MU; QUAL-197 check). The absences line does NOT include <c>Feriedage</c>: a row with a
    /// back-filled Feriedage and one with NULL hash identically, equal to the spec line. The AbsenceType is
    /// written as hex in the expected line since R-13 (the one expected-text change of re-spawn 1: the line
    /// FORM was amended by the ruling, the property pinned is unchanged).
    /// </summary>
    [Fact]
    public void AbsencesDigest_IgnoresFeriedage_QUAL197()
    {
        var live = new[] { DAbsence(DIdA, AbsenceTypes.LeaveWithPay, 7.4m, feriedage: null) };
        var rebuilt = new[] { DAbsence(DIdA, AbsenceTypes.LeaveWithPay, 7.4m, feriedage: 1.0m) };

        var hexType = Convert.ToHexString(Encoding.UTF8.GetBytes("LEAVE_WITH_PAY"));
        var expected = Sha256Hex("00000000-0000-0000-0000-00000000000a|2026-09-15|" + hexType + "|7.4");
        Assert.Equal(expected, HoursDigest.ComputeAbsencesDigest(live));
        Assert.Equal(expected, HoursDigest.ComputeAbsencesDigest(rebuilt));
    }

    private static string SpecEntriesDigest(IEnumerable<TimeEntryProjectionRow> rows) =>
        Sha256Hex(string.Join("\n", rows.Select(SpecEntryLine).OrderBy(l => l, StringComparer.Ordinal)));

    private static string SpecEntryLine(TimeEntryProjectionRow r) => string.Join("|",
        r.EventId.ToString("D"),
        r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        SpecTime(r.StartTime),
        SpecTime(r.EndTime),
        SpecDecimal(r.Hours),
        string.IsNullOrEmpty(r.ActivityType) ? "" : Convert.ToHexString(Encoding.UTF8.GetBytes(r.ActivityType)),
        r.VoluntaryUnsocialHours ? "true" : "false");

    private static string SpecTime(TimeOnly? t) =>
        t is null ? "" : t.Value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string SpecDecimal(decimal d) =>
        d.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static TimeEntryProjectionRow DEntry(
        Guid id, decimal hours = 7.4m, TimeOnly? start = null, TimeOnly? end = null) => new()
    {
        EventId = id,
        EmployeeId = "EMP-DIGEST",
        Date = DigestDay,
        Hours = hours,
        StartTime = start,
        EndTime = end,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };

    private static AbsenceProjectionRow DAbsence(Guid id, string type, decimal hours, decimal? feriedage) => new()
    {
        EventId = id,
        EmployeeId = "EMP-DIGEST",
        Date = DigestDay,
        AbsenceType = type,
        Hours = hours,
        Feriedage = feriedage,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
        OutboxId = 1,
    };
}
