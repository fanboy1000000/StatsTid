using System.Globalization;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S127 / TASK-12701a — fills a generated activity month with self-recorded WORK TIME and matching
/// PROJECT ALLOCATIONS, so the month satisfies BOTH submit-time gates.
///
/// <para><b>The two gates it targets</b> (<c>ApprovalEndpoints.cs:1387-1506</c>):</para>
/// <list type="number">
///   <item><b>Coverage</b> — every expected workday (weekday minus a
///     <c>danish_public_holidays</c> row) must carry at least one time entry OR one absence.
///     Satisfied by registering a work day on every expected workday the generator did not already
///     spend on an absence.</item>
///   <item><b>Allocation</b> — for EVERY day, <c>work_time_projection</c> hours (interval hours +
///     manual hours) must equal the summed <c>NORMAL</c> non-null-<c>TaskId</c> time-entry hours,
///     within 0.005 after 2-decimal rounding. Satisfied by construction: the day's allocation rows
///     are derived FROM the day's work-time total and are exact 2-decimal splits of it, so the
///     difference is exactly 0.00 — never a value that leans on the tolerance.</item>
/// </list>
///
/// <para><b>Absence days are left empty on purpose.</b> They carry no work time and no allocation,
/// so worked==0 and allocated==0 and the gate skips them as implicitly balanced — while the absence
/// itself still satisfies coverage. Registering work on a vacation day would balance too, and would
/// be a lie.</para>
///
/// <para><b>Determinism.</b> Pure derivation from (employeeId, year, month, org project codes) —
/// NO <c>Random</c> at all, not even a derived stream, and no wall-clock. This is deliberate and
/// load-bearing: the generator's single seeded <see cref="Random"/> is consumed in a fixed order and
/// every existing golden pins that order, so a new draw anywhere would shift people, edges, vikars
/// and messy cases. The per-employee variation below comes from a stable FNV-1a hash of the employee
/// id, not from the RNG. <see cref="string.GetHashCode()"/> is deliberately NOT used — .NET
/// randomizes it per process.</para>
/// </summary>
internal static class AllocatedMonthBuilder
{
    /// <summary>The day shapes, cycled per employee-day. Every end-minus-start is an exact
    /// 2-decimal hour count, so <c>SumIntervalHours</c> (<c>totalSec / 3600m</c>) returns it
    /// without rounding drift: 8h00 = 8.0, 7h24 = 7.4, 6h30 = 6.5.</summary>
    private static readonly (string Start, string End, decimal Hours)[] DayShapes =
    {
        ("08:00", "16:00", 8.0m),
        ("08:00", "15:24", 7.4m),
        ("08:30", "15:54", 7.4m),
        ("09:00", "15:30", 6.5m),
    };

    /// <summary>The share of a split day booked to the FIRST project. Chosen so every shape splits
    /// into two exact 2-decimal values (8.0 → 4.80/3.20 · 7.4 → 4.44/2.96 · 6.5 → 3.90/2.60).</summary>
    private const decimal SplitShare = 0.6m;

    /// <summary>
    /// Fills <paramref name="activity"/>'s <see cref="DemoActivity.WorkTime"/> and
    /// <see cref="DemoActivity.Allocations"/>. Idempotent-by-overwrite: both lists are replaced.
    /// </summary>
    /// <param name="activity">The activity whose month is being completed. Its
    /// <see cref="DemoActivity.Absences"/> are read to find the days that are already spoken for.</param>
    /// <param name="projectCodes">The project codes of the employee's OWN org, in display order.
    /// Must be non-empty — an empty catalogue is exactly the defect this task exists to remove, so
    /// it fails generation loudly rather than emitting an unallocatable month.</param>
    internal static void Fill(DemoActivity activity, IReadOnlyList<string> projectCodes)
    {
        if (projectCodes.Count == 0)
            throw new InvalidOperationException(
                $"[S127 allocation] employee {activity.EmployeeId}: org {activity.OrgId} has NO projects, " +
                "so its months can never satisfy the submit-time allocation gate. Generation FAILED (never the load).");

        var workTime = new List<DemoWorkDay>();
        var allocations = new List<DemoAllocation>();

        var absenceDays = activity.Absences
            .Select(a => DateOnly.Parse(a.Date, CultureInfo.InvariantCulture))
            .ToHashSet();

        var salt = StableSalt(activity.EmployeeId);
        var daysInMonth = DateTime.DaysInMonth(activity.Year, activity.Month);

        for (var dayOfMonth = 1; dayOfMonth <= daysInMonth; dayOfMonth++)
        {
            var date = new DateOnly(activity.Year, activity.Month, dayOfMonth);

            // Weekends and public holidays are not expected workdays — the gate does not ask for
            // them, and registering work there would be noise in the demo world.
            if (!DanishHolidays.IsExpectedWorkday(date))
                continue;

            // Already covered by an absence: leave the day EMPTY (worked==0, allocated==0).
            if (absenceDays.Contains(date))
                continue;

            var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rotor = salt + dayOfMonth;
            var shape = DayShapes[rotor % DayShapes.Length];

            workTime.Add(new DemoWorkDay
            {
                Date = iso,
                Start = shape.Start,
                End = shape.End,
                Hours = shape.Hours,
            });

            // Every third day is split across two projects, so the generated world exercises the
            // gate's PER-DAY SUM over multiple entries — not just the one-entry-equals-one-day case.
            // A single-project org (never produced by ProjectCatalog, but cheap to honour) always
            // books whole days, so a split can never name the same code twice.
            var split = projectCodes.Count >= 2 && rotor % 3 == 0;
            if (split)
            {
                var first = Math.Round(shape.Hours * SplitShare, 2, MidpointRounding.AwayFromZero);
                var second = shape.Hours - first;
                allocations.Add(new DemoAllocation
                {
                    Date = iso,
                    ProjectCode = projectCodes[rotor % projectCodes.Count],
                    Hours = first,
                });
                allocations.Add(new DemoAllocation
                {
                    Date = iso,
                    ProjectCode = projectCodes[(rotor + 1) % projectCodes.Count],
                    Hours = second,
                });
            }
            else
            {
                allocations.Add(new DemoAllocation
                {
                    Date = iso,
                    ProjectCode = projectCodes[rotor % projectCodes.Count],
                    Hours = shape.Hours,
                });
            }
        }

        activity.WorkTime = workTime;
        activity.Allocations = allocations;
    }

    /// <summary>A stable, platform-independent non-negative hash of the employee id. FNV-1a over the
    /// ordinal chars — NOT <see cref="string.GetHashCode()"/>, which .NET randomizes per process and
    /// which would therefore make the generated manifest non-reproducible.</summary>
    private static int StableSalt(string employeeId)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var c in employeeId)
            {
                hash ^= c;
                hash *= prime;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
