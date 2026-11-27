using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// G-3 (TASK-14900, ADR-045) — the digest lines and the two digests frozen into a month's proof.
///
/// <para>
/// <b>Plain language.</b> A replay re-reads the hour rows a month was paid on and recomputes a fingerprint
/// (<c>HoursDigest</c>); if it differs from the one stored with the month, the replay is refused. The
/// fingerprint is a SHA-256 over text lines, so the number form (<c>7.40</c> becomes <c>7.4</c>), the date and
/// time forms, the hex of the typed text and the separators must never move. <c>G3-digest.golden</c> holds each
/// line and each digest (64 lowercase hex) for three entry rows and three absence rows, and the digest of the
/// empty list. The rows cover a null start and end, a time with 100-ns precision, hours <c>7.40</c>,
/// <c>8.0</c>, <c>0.25</c> and <c>-0.25</c>, typed text that is null, empty and Danish with escaped characters,
/// and the voluntary flag both ways.
/// </para>
///
/// <para>
/// <b>Kind: R.</b> Red conditions: any change to a line form or to the hash encoding fails the matching literal.
/// Proved by the mutation M-3 (the number form keeps trailing zeros) at the wave gate. The empty-list digests are
/// also checked against the SHA-256 of the empty string, which is a published constant.
/// </para>
/// </summary>
public sealed class G3HoursDigestGoldenTests
{
    private const string File = "G3-digest.golden";
    private const string Sha256OfEmptyText = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static TimeEntryProjectionRow Entry(int n, decimal hours, TimeOnly? start, TimeOnly? end, string? activity, bool voluntary) => new()
    {
        EventId = Guid.Parse($"{n:x8}-aaaa-4bbb-8ccc-{n:x12}"),
        EmployeeId = "EMP-0001",
        Date = new DateOnly(2026, 3, 2).AddDays(n),
        Hours = hours,
        StartTime = start,
        EndTime = end,
        ActivityType = activity,
        AgreementCode = "HK",
        OkVersion = "OK24",
        VoluntaryUnsocialHours = voluntary,
        OccurredAt = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = n,
    };

    private static AbsenceProjectionRow Absence(int n, decimal hours, string? type) => new()
    {
        EventId = Guid.Parse($"{n:x8}-dddd-4eee-8fff-{n:x12}"),
        EmployeeId = "EMP-0001",
        Date = new DateOnly(2026, 3, 2).AddDays(n),
        AbsenceType = type!,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK24",
        OccurredAt = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = n,
    };

    // Not in sorted order, so the digest's own sort is part of what is pinned.
    private static readonly TimeEntryProjectionRow[] Entries =
    {
        Entry(3, 0.25m, new TimeOnly(9, 0, 0), new TimeOnly(9, 15, 0), "", false),
        Entry(1, 7.40m, new TimeOnly(8, 30, 15).Add(TimeSpan.FromTicks(1234567)), new TimeOnly(16, 0, 0).Add(TimeSpan.FromTicks(1)), "Ærø Å/ø <&'\"+>", true),
        Entry(2, 8.0m, null, null, null, false),
    };

    private static readonly AbsenceProjectionRow[] Absences =
    {
        Absence(3, -0.25m, "Ærø Å/ø <&'\"+>"),
        Absence(1, 7.40m, null),
        Absence(2, 8.0m, ""),
    };

    private static IReadOnlyDictionary<string, string> Actual()
    {
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Entries.Length; i++)
            actual[$"entries.line.{i + 1}"] = HoursDigest.EntryLine(Entries[i]);
        actual["entries.digest"] = HoursDigest.ComputeEntriesDigest(Entries);
        actual["entries.empty.digest"] = HoursDigest.ComputeEntriesDigest(Array.Empty<TimeEntryProjectionRow>());
        for (var i = 0; i < Absences.Length; i++)
            actual[$"absences.line.{i + 1}"] = HoursDigest.AbsenceLine(Absences[i]);
        actual["absences.digest"] = HoursDigest.ComputeAbsencesDigest(Absences);
        actual["absences.empty.digest"] = HoursDigest.ComputeAbsencesDigest(Array.Empty<AbsenceProjectionRow>());
        return actual;
    }

    [Fact]
    public void Every_line_and_digest_of_the_fixed_rows_equals_its_literal()
    {
        GoldenFile.AssertMatches(File, Actual());
    }

    [Fact]
    public void The_digest_literals_are_64_lowercase_hex_and_the_empty_ones_are_the_hash_of_empty_text()
    {
        var golden = GoldenFile.Load(File);
        foreach (var key in new[] { "entries.digest", "absences.digest", "entries.empty.digest", "absences.empty.digest" })
            Assert.Matches("^[0-9a-f]{64}$", golden[key]);
        Assert.Equal(Sha256OfEmptyText, golden["entries.empty.digest"]);
        Assert.Equal(Sha256OfEmptyText, golden["absences.empty.digest"]);
    }
}
