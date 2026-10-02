using System.Globalization;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit.Calendar;

/// <summary>
/// S145 / TASK-14506 — pins for <c>StatsTid.SharedKernel.Calendar.WeeklyContext</c>, the pure
/// calendar helper behind "which weeks does this month settle?" (ruling Q3 = F1) and "can this
/// month be calculated at all?" (ruling Q5 = (c), refinement A5).
///
/// <para>
/// <b>Plain language.</b> Overtime is judged one Monday–Sunday week at a time, and a week is paid
/// by the month that contains its <b>Sunday</b> (a leaver's week is paid when their employment ends,
/// on their last day). So September 2026 settles the weeks ending Sundays 6, 13, 20 and 27 September
/// — the first of them opens on Monday 31 August, a "context day" of August — but NOT the week of
/// 28 September – 4 October, which October settles. A week that contains an agreement change on a
/// day other than its Monday cannot be judged (HK pays tiered overtime, AC pays merarbejde 1:1, and
/// the sources are silent on a week with both), so the month is refused. A change on the Monday is
/// fine: the whole week then runs under one agreement.
/// </para>
///
/// <para>
/// <b>The matrix below is the contract of <c>MidWeekAgreementChange</c></b> (TASK-14505, R43): a
/// change counts only on a non-Monday, only inside a week the month settles, and context days
/// count. The predicate is date-only and pure; filtering rows whose previous day was not employed
/// ("callers filter hire-dated rows", R43) is the caller's job and is pinned in TASK-14514.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate): MU-16 makes the predicate ignore
/// context days (only changes inside the month itself count) and trips the matrix row
/// "Tuesday 29 September is a context day of October"; MU-17 drops the Monday exemption and trips
/// every "Monday" row. The helper facts below fail under any change to the week arithmetic those
/// two rely on.
/// </para>
/// </summary>
public sealed class WeeklyContextTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<DateOnly> Dates(string commaSeparated) =>
        commaSeparated.Length == 0
            ? Array.Empty<DateOnly>()
            : commaSeparated.Split(',').Select(D).ToList();

    [Theory]
    // --- September 2026 (settles the weeks ending 6, 13, 20, 27 Sep; the opening week starts Mon 31 Aug) ---
    [InlineData("Monday inside a settled week is exempt", "2026-09-01", "2026-09-30", null, "2026-09-14", false)]
    [InlineData("Wednesday inside a settled week", "2026-09-01", "2026-09-30", null, "2026-09-16", true)]
    [InlineData("weekday 1st (Tuesday 1 September) is interior to the opening week", "2026-09-01", "2026-09-30", null, "2026-09-01", true)]
    [InlineData("Monday 31 August is a context day, but a Monday", "2026-09-01", "2026-09-30", null, "2026-08-31", false)]
    [InlineData("Wednesday 30 September is in the week October settles", "2026-09-01", "2026-09-30", null, "2026-09-30", false)]
    [InlineData("Monday 28 September is in the week October settles", "2026-09-01", "2026-09-30", null, "2026-09-28", false)]
    [InlineData("Wednesday 26 August is before the opening week", "2026-09-01", "2026-09-30", null, "2026-08-26", false)]
    [InlineData("Wednesday 7 October is after the month", "2026-09-01", "2026-09-30", null, "2026-10-07", false)]
    [InlineData("any one non-Monday change among several is enough", "2026-09-01", "2026-09-30", null, "2026-09-14,2026-09-16", true)]
    [InlineData("no change at all", "2026-09-01", "2026-09-30", null, "", false)]
    // --- October 2026 (settles the weeks ending 4, 11, 18, 25 Oct; the opening week starts Mon 28 Sep) ---
    [InlineData("Tuesday 29 September is a context day of October", "2026-10-01", "2026-10-31", null, "2026-09-29", true)]
    [InlineData("Monday 28 September is a context day of October, but a Monday", "2026-10-01", "2026-10-31", null, "2026-09-28", false)]
    [InlineData("Wednesday 7 October inside a settled week", "2026-10-01", "2026-10-31", null, "2026-10-07", true)]
    [InlineData("Friday 30 October is in the week November settles", "2026-10-01", "2026-10-31", null, "2026-10-30", false)]
    // --- June 2026 starts on a Monday ---
    [InlineData("Monday 1 June 2026 pays: a change on the Monday is exempt", "2026-06-01", "2026-06-30", null, "2026-06-01", false)]
    // --- the leaver cutoff moves the settlement day into the month ---
    [InlineData("leaver (last day Tue 29 Sep): the week of 28 Sep settles in September, so Tuesday 29 Sep counts", "2026-09-01", "2026-09-30", "2026-09-29", "2026-09-29", true)]
    [InlineData("same date, no cutoff: that week belongs to October, so it does not count", "2026-09-01", "2026-09-30", null, "2026-09-29", false)]
    public void WeeklyContext_MidWeekAgreementChange_Matrix(
        string why, string periodStart, string periodEnd, string? cutoff, string changeDates, bool expected)
    {
        var settled = WeeklyContext.SettledWeeks(D(periodStart), D(periodEnd), cutoff is null ? null : D(cutoff));

        var actual = WeeklyContext.MidWeekAgreementChange(settled, Dates(changeDates));

        Assert.True(expected == actual, $"{why}: expected {expected}, got {actual}.");
    }

    [Fact]
    public void WeeklyContext_WeekStartAndEnd_AreMondayAndSunday()
    {
        // Every day of Monday 14 – Sunday 20 September 2026 belongs to that one week. The Sunday is
        // the classic trap (DayOfWeek.Sunday is 0, so a naive "back to Monday" lands a week late).
        for (var i = 0; i < 7; i++)
        {
            var day = D("2026-09-14").AddDays(i);
            Assert.Equal(D("2026-09-14"), WeeklyContext.WeekStart(day));
            Assert.Equal(D("2026-09-20"), WeeklyContext.WeekEnd(day));
        }

        // The next day starts the next week.
        Assert.Equal(D("2026-09-21"), WeeklyContext.WeekStart(D("2026-09-21")));
        Assert.Equal(D("2026-09-27"), WeeklyContext.WeekEnd(D("2026-09-21")));
    }

    [Fact]
    public void WeeklyContext_SettlementDay_IsTheSunday_OrTheCutoffWhenItFallsInsideTheWeek()
    {
        var weekStart = D("2026-09-28"); // Monday; its Sunday is 4 October

        // No cutoff: the Sunday (which here lies in the NEXT month).
        Assert.Equal(D("2026-10-04"), WeeklyContext.SettlementDay(weekStart, null));

        // A cutoff inside the week (a leaver's last day, or a legacy call's last day) wins.
        Assert.Equal(D("2026-09-29"), WeeklyContext.SettlementDay(weekStart, D("2026-09-29")));
        Assert.Equal(D("2026-09-28"), WeeklyContext.SettlementDay(weekStart, D("2026-09-28")));

        // A cutoff ON the Sunday is the Sunday; a cutoff after the week does not shorten it.
        Assert.Equal(D("2026-10-04"), WeeklyContext.SettlementDay(weekStart, D("2026-10-04")));
        Assert.Equal(D("2026-10-04"), WeeklyContext.SettlementDay(weekStart, D("2026-10-20")));
    }

    [Fact]
    public void WeeklyContext_SettledWeeks_AreTheWeeksWhoseSettlementDayLiesInThePeriod()
    {
        // September 2026, a stayer: the weeks ending Sundays 6, 13, 20, 27 September. The week that
        // opens on Monday 31 August IS settled (its Sunday is 6 September); the week that opens on
        // Monday 28 September is NOT (its Sunday is 4 October).
        var september = WeeklyContext.SettledWeeks(D("2026-09-01"), D("2026-09-30"), null);
        Assert.Equal(
            new[] { "2026-08-31", "2026-09-07", "2026-09-14", "2026-09-21" },
            september.Select(w => w.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());
        Assert.Equal(
            new[] { "2026-09-06", "2026-09-13", "2026-09-20", "2026-09-27" },
            september.Select(w => w.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());

        // October 2026, a stayer: the closing week of September is October's opening week.
        var october = WeeklyContext.SettledWeeks(D("2026-10-01"), D("2026-10-31"), null);
        Assert.Equal(
            new[] { "2026-09-28", "2026-10-05", "2026-10-12", "2026-10-19" },
            october.Select(w => w.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());

        // June 2026 starts on a Monday: no context day, four weeks.
        var june = WeeklyContext.SettledWeeks(D("2026-06-01"), D("2026-06-30"), null);
        Assert.Equal(
            new[] { "2026-06-01", "2026-06-08", "2026-06-15", "2026-06-22" },
            june.Select(w => w.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());

        // A leaver whose last day is Tuesday 29 September settles the week of 28 September in
        // September (the cutoff is that week's settlement day): five weeks, the fifth opening 28 Sep.
        var leaver = WeeklyContext.SettledWeeks(D("2026-09-01"), D("2026-09-30"), D("2026-09-29"));
        Assert.Equal(
            new[] { "2026-08-31", "2026-09-07", "2026-09-14", "2026-09-21", "2026-09-28" },
            leaver.Select(w => w.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());
    }
}
