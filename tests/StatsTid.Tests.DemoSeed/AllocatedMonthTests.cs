using System.Globalization;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S127 / TASK-12701a — the generated activity months must satisfy BOTH submit-time gates.
///
/// <para>These tests re-derive the gates' arithmetic FROM THE GATES' OWN DEFINITIONS
/// (<c>ApprovalEndpoints.cs:1387-1506</c>), not from <see cref="AllocatedMonthBuilder"/>:</para>
/// <list type="bullet">
///   <item><b>Coverage</b> — expected workday := weekday AND not in <c>danish_public_holidays</c>;
///     covered := at least one time entry OR one absence on that date.</item>
///   <item><b>Allocation</b> — worked(day) := interval hours + manual hours from
///     <c>work_time_projection</c>; allocated(day) := summed hours of NORMAL entries with a non-null
///     TaskId; balanced := <c>|round(worked,2) − round(allocated,2)| &lt; 0.005</c>.</item>
/// </list>
///
/// <para><b>The worked side is computed from the CLOCK STRINGS</b>, exactly as the server does
/// (<c>SumIntervalHours</c>: summed second-deltas ÷ 3600), never from the manifest's own
/// <c>hours</c> field. Trusting that field would make the balance assertion compare the builder's
/// arithmetic against itself, and a mistyped interval ("08:00"–"15:25" declared as 7.4) would sail
/// through. The two are cross-checked separately.</para>
/// </summary>
public sealed class AllocatedMonthTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    /// <summary>The gate's tolerance, verbatim (<c>ApprovalEndpoints.cs:27</c>).</summary>
    private const decimal AllocationTolerance = 0.005m;

    private static DemoDataset Gen(string scale) => new DemoGenerator(scale, 42, Ref).Generate();

    private static DateOnly D(string iso) => DateOnly.Parse(iso, CultureInfo.InvariantCulture);

    /// <summary>The server's work-time summation (<c>ApprovalEndpoints.SumIntervalHours</c>):
    /// second-deltas of positive intervals, divided by 3600 in decimal.</summary>
    private static decimal IntervalHours(string start, string end)
    {
        static long Seconds(string hhmm)
        {
            var parts = hhmm.Split(':');
            return (long.Parse(parts[0], CultureInfo.InvariantCulture) * 3600)
                   + (long.Parse(parts[1], CultureInfo.InvariantCulture) * 60);
        }

        var diff = Seconds(end) - Seconds(start);
        return diff > 0 ? diff / 3600m : 0m;
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryActivityMonth_IsPerDayBalanced(string scale)
    {
        var ds = Gen(scale);
        Assert.NotEmpty(ds.Manifest.Activity);

        foreach (var a in ds.Manifest.Activity)
        {
            var worked = new Dictionary<DateOnly, decimal>();
            foreach (var w in a.WorkTime!)
                worked[D(w.Date)] = worked.GetValueOrDefault(D(w.Date)) + IntervalHours(w.Start, w.End);

            var allocated = new Dictionary<DateOnly, decimal>();
            foreach (var al in a.Allocations!)
                allocated[D(al.Date)] = allocated.GetValueOrDefault(D(al.Date)) + al.Hours;

            foreach (var day in worked.Keys.Union(allocated.Keys))
            {
                var w = Math.Round(worked.GetValueOrDefault(day), 2);
                var al = Math.Round(allocated.GetValueOrDefault(day), 2);
                Assert.True(Math.Abs(w - al) < AllocationTolerance,
                    $"{a.EmployeeId} {day:yyyy-MM-dd}: worked {w} vs allocated {al}");
            }
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryActivityMonth_BalancesExactly_NotMerelyWithinTolerance(string scale)
    {
        // A month that only just squeaks under 0.005 would be a latent failure the moment the
        // tolerance or the rounding convention moved. The construction claims EXACT equality; this
        // asserts the claim rather than the weaker thing the gate happens to accept.
        var ds = Gen(scale);
        foreach (var a in ds.Manifest.Activity)
        {
            var allocatedByDay = a.Allocations!
                .GroupBy(al => D(al.Date))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

            foreach (var w in a.WorkTime!)
            {
                var day = D(w.Date);
                Assert.True(allocatedByDay.TryGetValue(day, out var allocated),
                    $"{a.EmployeeId} {w.Date}: worked hours with NO allocation at all");
                Assert.Equal(IntervalHours(w.Start, w.End), allocated);
            }
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryActivityMonth_CoversEveryExpectedWorkday(string scale)
    {
        var ds = Gen(scale);
        foreach (var a in ds.Manifest.Activity)
        {
            var registered = a.WorkTime!.Select(w => D(w.Date))
                .Concat(a.Allocations!.Select(al => D(al.Date)))
                .Concat(a.Absences.Select(ab => D(ab.Date)))
                .ToHashSet();

            var daysInMonth = DateTime.DaysInMonth(a.Year, a.Month);
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(a.Year, a.Month, day);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (DanishHolidays.For(a.Year).Contains(date)) continue;

                Assert.True(registered.Contains(date),
                    $"{a.EmployeeId}: expected workday {date:yyyy-MM-dd} has no registration — coverage would 422");
            }
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void NoWorkTime_IsRegisteredOnAnAbsenceDay_OrAWeekend_OrAHoliday(string scale)
    {
        var ds = Gen(scale);
        foreach (var a in ds.Manifest.Activity)
        {
            var absenceDays = a.Absences.Select(ab => D(ab.Date)).ToHashSet();
            foreach (var w in a.WorkTime!)
            {
                var date = D(w.Date);
                Assert.DoesNotContain(date, absenceDays);
                Assert.False(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    $"{a.EmployeeId}: work registered on the weekend day {w.Date}");
                Assert.DoesNotContain(date, DanishHolidays.For(date.Year));
            }
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryAllocation_NamesAProjectInTheEmployeesOwnOrg(string scale)
    {
        // Projects are org-scoped. An allocation naming a sibling org's code would still satisfy the
        // gate (it only checks NORMAL + non-null TaskId) while showing the employee a row they can
        // neither see nor edit — a demo world that looks right and is not.
        var ds = Gen(scale);
        var codesByOrg = ds.Projects
            .GroupBy(p => p.OrgId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProjectCode).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        foreach (var a in ds.Manifest.Activity)
        {
            var codes = codesByOrg[a.OrgId];
            foreach (var al in a.Allocations!)
                Assert.Contains(al.ProjectCode, codes);
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryAllocation_CarriesPositiveHours_AtTwoDecimals(string scale)
    {
        var ds = Gen(scale);
        foreach (var a in ds.Manifest.Activity)
            foreach (var al in a.Allocations!)
            {
                Assert.True(al.Hours > 0m, $"{a.EmployeeId} {al.Date}: non-positive allocation {al.Hours}");
                Assert.Equal(Math.Round(al.Hours, 2), al.Hours);
            }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void DeclaredWorkDayHours_MatchTheClockInterval(string scale)
    {
        // The manifest's `hours` field is a convenience mirror of the interval. If it drifted, the
        // manifest would document a month the server does not produce.
        var ds = Gen(scale);
        foreach (var a in ds.Manifest.Activity)
            foreach (var w in a.WorkTime!)
                Assert.Equal(IntervalHours(w.Start, w.End), w.Hours);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void SplitDays_Exist_AndBookTwoDistinctProjects(string scale)
    {
        // The gate sums MULTIPLE entries per day. A generated world where every day had exactly one
        // entry would never exercise that sum, and the balance tests above would pass vacuously on
        // the one-row case.
        var ds = Gen(scale);
        var splitDays = ds.Manifest.Activity
            .SelectMany(a => a.Allocations!.GroupBy(al => (a.EmployeeId, al.Date)))
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.NotEmpty(splitDays);
        foreach (var g in splitDays)
            Assert.Equal(g.Count(), g.Select(x => x.ProjectCode).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Builder_FailsLoudly_OnAnEmptyProjectCatalogue()
    {
        // The RED case for the precondition: an org with no projects must abort generation, not
        // emit a month that can never be sent. That is the whole defect this task removes.
        var activity = new DemoActivity { EmployeeId = "x", OrgId = "NOPROJ", Year = 2026, Month = 5 };
        var ex = Assert.Throws<InvalidOperationException>(
            () => AllocatedMonthBuilder.Fill(activity, Array.Empty<string>()));
        Assert.Contains("NOPROJ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_IsPureDerivation_SameInputsGiveTheSameMonth()
    {
        // No RNG, no wall-clock: two independent fills of equivalent activities must agree exactly.
        // (The employee id is the only source of variation, via a stable FNV-1a salt — NOT
        // string.GetHashCode, which .NET randomizes per process.)
        var codes = new[] { "A", "B", "C", "D" };
        var first = new DemoActivity { EmployeeId = "demo_styx1_0007", OrgId = "STYX1", Year = 2026, Month = 5 };
        var second = new DemoActivity { EmployeeId = "demo_styx1_0007", OrgId = "STYX1", Year = 2026, Month = 5 };
        AllocatedMonthBuilder.Fill(first, codes);
        AllocatedMonthBuilder.Fill(second, codes);

        Assert.Equal(first.WorkTime!.Select(w => (w.Date, w.Start, w.End, w.Hours)),
                     second.WorkTime!.Select(w => (w.Date, w.Start, w.End, w.Hours)));
        Assert.Equal(first.Allocations!.Select(a => (a.Date, a.ProjectCode, a.Hours)),
                     second.Allocations!.Select(a => (a.Date, a.ProjectCode, a.Hours)));

        // Different employees get visibly different months (otherwise the salt is doing nothing).
        var other = new DemoActivity { EmployeeId = "demo_styx1_0008", OrgId = "STYX1", Year = 2026, Month = 5 };
        AllocatedMonthBuilder.Fill(other, codes);
        Assert.NotEqual(first.Allocations!.Select(a => (a.Date, a.ProjectCode, a.Hours)),
                        other.Allocations!.Select(a => (a.Date, a.ProjectCode, a.Hours)));
    }

    [Fact]
    public void Fill_LeavesAbsenceDaysEmpty_SoTheyStayImplicitlyBalanced()
    {
        // 2026-05-12 is a plain Tuesday; with an absence on it the day must carry NO work time and
        // NO allocation (worked==0, allocated==0 — the gate skips it), while the absence itself
        // still satisfies coverage.
        var activity = new DemoActivity
        {
            EmployeeId = "demo_styx1_0007",
            OrgId = "STYX1",
            Year = 2026,
            Month = 5,
            Absences = { new DemoAbsence { Date = "2026-05-12", AbsenceType = "VACATION", Hours = 7.4m } },
        };
        AllocatedMonthBuilder.Fill(activity, new[] { "A", "B", "C", "D" });

        Assert.DoesNotContain(activity.WorkTime!, w => w.Date == "2026-05-12");
        Assert.DoesNotContain(activity.Allocations!, a => a.Date == "2026-05-12");

        // …and the neighbouring workday IS filled, so the exclusion is targeted, not a blanket miss.
        Assert.Contains(activity.WorkTime!, w => w.Date == "2026-05-13");
    }

    [Fact]
    public void Fill_SkipsAWeekdayPublicHoliday()
    {
        // 2026-05-14 (Kristi Himmelfartsdag) is a Thursday. The gate does not expect it, so no work
        // is registered — but the surrounding weekdays are.
        var activity = new DemoActivity { EmployeeId = "demo_styx1_0007", OrgId = "STYX1", Year = 2026, Month = 5 };
        AllocatedMonthBuilder.Fill(activity, new[] { "A", "B", "C", "D" });

        Assert.DoesNotContain(activity.WorkTime!, w => w.Date == "2026-05-14");
        Assert.Contains(activity.WorkTime!, w => w.Date == "2026-05-13");
        Assert.Contains(activity.WorkTime!, w => w.Date == "2026-05-15");
    }
}
