using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S136 / TASK-13607 — employment-window reconciliation (ADR-040, owner ruling OQ-2a). S136 makes
/// employment windows ENFORCED: registrations dated outside an employee's window 422 at the API,
/// and a DB CHECK refuses <c>employment_end_date &lt; employment_start_date</c>. The generator
/// therefore clamps its draws so the seed can never collide with the enforcement:
/// (D2) every ACTIVE user's start is at or before the activity month's first day — activity is
/// generated for active users only, in the previous calendar month — and (D3) a leaver's end is
/// never before their start (the end stays in the past as designed; the START is what moves).
///
/// <para>Full-scale generation here is conclusive, not probabilistic: the RNG is seeded and the
/// draws are fixed-order, so ONE full-scale run IS the full-scale seed. Non-vacuity verified by
/// probe against the pre-clamp generator (RED): at (full, 42, 2026-06-15) it produced 11 active
/// users starting after the activity month start (1 of them with generated activity —
/// demo_styx1_1473) and 4 inverted leaver windows; smoke produced neither cohort, which is why
/// the clamp leaves the golden-pinned legacy smoke bytes untouched.</para>
/// </summary>
public sealed class EmploymentWindowClampTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private static DemoDataset Gen(string scale, int y, int m, int d)
        => new DemoGenerator(scale, 42, new DateOnly(y, m, d)).Generate();

    /// <summary>First day of the activity month the generator targets (the previous calendar
    /// month relative to the reference date) — the anchor the D2 clamp must respect.</summary>
    private static DateOnly ActivityMonthStart(DateOnly referenceDate)
    {
        var m = referenceDate.AddMonths(-1);
        return new DateOnly(m.Year, m.Month, 1);
    }

    // D2 — across both scales and three activity-month shapes (the same reference dates the
    // Leder-persona month-independence test exercises): NO active user may start after the
    // activity month's first day, so no generated registration can pre-date its employment.
    [Theory]
    [InlineData("full", 2026, 6, 15)]
    [InlineData("full", 2026, 8, 1)]
    [InlineData("full", 2026, 2, 1)]
    [InlineData("smoke", 2026, 6, 15)]
    public void ActiveUsers_StartOnOrBeforeActivityMonthStart(string scale, int y, int m, int d)
    {
        var ds = Gen(scale, y, m, d);
        var monthStart = ActivityMonthStart(new DateOnly(y, m, d));

        var violators = ds.Users
            .Where(u => u.IsActive && DateOnly.Parse(u.EmploymentStartDate) > monthStart)
            .Select(u => $"{u.UserId} starts {u.EmploymentStartDate}")
            .ToList();
        Assert.True(violators.Count == 0,
            $"{violators.Count} active user(s) start after the activity month start {monthStart:yyyy-MM-dd}: "
            + string.Join("; ", violators.Take(10)));
    }

    // D2, at the enforcement surface itself: every manifest activity entry belongs to an employee
    // whose employment covers the whole activity month (start ≤ its first day).
    [Theory]
    [InlineData("full", 2026, 6, 15)]
    [InlineData("full", 2026, 8, 1)]
    [InlineData("full", 2026, 2, 1)]
    public void ActivityEntries_NeverPreDateTheEmployeesEmployment(string scale, int y, int m, int d)
    {
        var ds = Gen(scale, y, m, d);
        var startById = ds.Users.ToDictionary(u => u.UserId, u => DateOnly.Parse(u.EmploymentStartDate));

        var violators = ds.Manifest.Activity
            .Where(a => startById[a.EmployeeId] > new DateOnly(a.Year, a.Month, 1))
            .Select(a => $"{a.EmployeeId} ({a.Year}-{a.Month:D2} vs start {startById[a.EmployeeId]:yyyy-MM-dd})")
            .ToList();
        Assert.True(violators.Count == 0,
            $"{violators.Count} activity month(s) pre-date their employee's start: "
            + string.Join("; ", violators.Take(10)));
    }

    // D3 — the DB CHECK's exact predicate: no user, at any scale or reference date, may carry
    // end < start (leavers are the only end-dated cohort, but the assertion sweeps everyone).
    [Theory]
    [InlineData("full", 2026, 6, 15)]
    [InlineData("full", 2026, 8, 1)]
    [InlineData("full", 2026, 2, 1)]
    [InlineData("smoke", 2026, 6, 15)]
    public void NoUser_HasEndDateBeforeStartDate(string scale, int y, int m, int d)
    {
        var ds = Gen(scale, y, m, d);
        var violators = ds.Users
            .Where(u => u.EmploymentEndDate is not null
                        && DateOnly.Parse(u.EmploymentEndDate) < DateOnly.Parse(u.EmploymentStartDate))
            .Select(u => $"{u.UserId} ({u.EmploymentStartDate} → {u.EmploymentEndDate})")
            .ToList();
        Assert.True(violators.Count == 0,
            $"{violators.Count} user(s) have end < start: " + string.Join("; ", violators.Take(10)));
    }

    // Determinism guard: the clamps are post-draw (they consume no extra RNG draw), so the same
    // seed must still yield the same population — same user count, same ids, same windows, same
    // tree shape. Deliberately NOT over-pinned: byte-level identity is GeneratorDeterminismTests'
    // job; this pins that the clamp did not move the draw structure.
    [Fact]
    public void SameSeed_FullScale_PopulationAndWindowsAreStable()
    {
        var a = Gen("full", Ref.Year, Ref.Month, Ref.Day);
        var b = Gen("full", Ref.Year, Ref.Month, Ref.Day);

        Assert.Equal(5, a.Manifest.Trees.Count);
        Assert.InRange(a.Users.Count, 3200, 3500); // the established full-scale headcount band
        Assert.Equal(a.Users.Count, b.Users.Count);
        Assert.Equal(
            a.Users.Select(u => (u.UserId, u.EmploymentStartDate, u.EmploymentEndDate, u.IsActive)),
            b.Users.Select(u => (u.UserId, u.EmploymentStartDate, u.EmploymentEndDate, u.IsActive)));
    }
}
