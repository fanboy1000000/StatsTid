using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S127 / TASK-12701a — the cross-file pin behind the generator's coverage arithmetic.
///
/// <para>The submit-time coverage gate builds its expected-workday list from the
/// <c>danish_public_holidays</c> TABLE (<c>ApprovalEndpoints.cs:1404-1413</c>), while the generator
/// decides which days to register work on from <see cref="DanishHolidays"/>'s COMPUTED set. Those
/// two must agree, and nothing else in the build checks that they do.</para>
///
/// <para>The expected sets below are transcribed from the literal <c>init.sql:374-416</c> seed rows
/// — the authority — NOT from the calculator. If the calculator regresses, these go red; a test that
/// asked the calculator what it thought would go green forever.</para>
///
/// <para>The harmful drift direction is a day the CALCULATOR calls a holiday and the TABLE does not:
/// that weekday becomes expected-but-unregistered and the generated month fails coverage. The
/// converse is harmless (an extra registration never fails coverage), which is why set EQUALITY —
/// not containment — is asserted.</para>
/// </summary>
public sealed class DanishHolidaysTests
{
    // init.sql:374-386 — 2024 (Easter = March 31)
    private static readonly string[] Seeded2024 =
    {
        "2024-01-01", "2024-03-28", "2024-03-29", "2024-03-31", "2024-04-01", "2024-05-09",
        "2024-05-19", "2024-05-20", "2024-06-05", "2024-12-25", "2024-12-26",
    };

    // init.sql:389-401 — 2025 (Easter = April 20)
    private static readonly string[] Seeded2025 =
    {
        "2025-01-01", "2025-04-17", "2025-04-18", "2025-04-20", "2025-04-21", "2025-05-29",
        "2025-06-05", "2025-06-08", "2025-06-09", "2025-12-25", "2025-12-26",
    };

    // init.sql:404-416 — 2026 (Easter = April 5)
    private static readonly string[] Seeded2026 =
    {
        "2026-01-01", "2026-04-02", "2026-04-03", "2026-04-05", "2026-04-06", "2026-05-14",
        "2026-05-24", "2026-05-25", "2026-06-05", "2026-12-25", "2026-12-26",
    };

    public static TheoryData<int, string[]> SeededYears() => new()
    {
        { 2024, Seeded2024 },
        { 2025, Seeded2025 },
        { 2026, Seeded2026 },
    };

    [Theory]
    [MemberData(nameof(SeededYears))]
    public void ComputedHolidays_MatchTheInitSqlSeededRows_Exactly(int year, string[] seeded)
    {
        var expected = seeded.Select(DateOnly.Parse).OrderBy(d => d).ToList();
        var actual = DanishHolidays.For(year).OrderBy(d => d).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsExpectedWorkday_ExcludesWeekends_AndHolidays_AndAdmitsAPlainWeekday()
    {
        // A holiday that falls on a WEEKDAY — the case that actually matters. 2026-05-14 (Kristi
        // Himmelfartsdag) is a Thursday: the gate does not expect it, so the generator must not
        // register work on it.
        var kristiHimmelfart = new DateOnly(2026, 5, 14);
        Assert.Equal(DayOfWeek.Thursday, kristiHimmelfart.DayOfWeek);
        Assert.False(DanishHolidays.IsExpectedWorkday(kristiHimmelfart));

        Assert.False(DanishHolidays.IsExpectedWorkday(new DateOnly(2026, 5, 16))); // Saturday
        Assert.False(DanishHolidays.IsExpectedWorkday(new DateOnly(2026, 5, 17))); // Sunday
        Assert.True(DanishHolidays.IsExpectedWorkday(new DateOnly(2026, 5, 13)));  // plain Wednesday
    }

    [Fact]
    public void ComputedHolidays_ExtendBeyondTheSeededYears()
    {
        // 2027 has no seeded rows; the calculator must still answer (Easter 2027 = March 28), so a
        // reference date rolled forward does not silently lose the holiday filter.
        var y2027 = DanishHolidays.For(2027);
        Assert.Contains(new DateOnly(2027, 3, 26), y2027); // Langfredag
        Assert.Contains(new DateOnly(2027, 3, 28), y2027); // Paaskedag
        Assert.Contains(new DateOnly(2027, 5, 6), y2027);  // Kristi Himmelfartsdag (Easter + 39)
        Assert.Equal(11, y2027.Count);
    }
}
