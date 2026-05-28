using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S56 / TASK-5604 — allocation-reconciliation HARD gate at employee-approve.
/// The production gate (ApprovalEndpoints.cs employee-approve) compares, per day:
///
/// <list type="bullet">
///   <item><b>worked</b> = Σ interval hours + manual_hours from work_time_projection.</item>
///   <item><b>allocated</b> = Σ hours of time_entries WHERE ActivityType='NORMAL'
///   AND TaskId IS NOT NULL (absences excluded; null-TaskId NORMAL excluded — parity
///   with the grid's "Ikke fordelt" predicate).</item>
/// </list>
///
/// Both rounded to 2 decimals; days with <c>|worked-allocated| &lt; 0.005</c> are
/// balanced; otherwise the day is unbalanced with direction = worked &gt; allocated
/// ? "under" : "over". Any unbalanced day → 422 <c>{kind:"allocation", unbalancedDays}</c>.
///
/// <para>
/// These tests seed REAL work_time_projection + time_entries_projection rows (via
/// the production repos + real outbox) and run the gate algorithm against the
/// rows read back from those projections — pinning the gate contract against the
/// actual read-model shape. The gate math here is a verbatim mirror of the inline
/// endpoint logic (SumIntervalHours + manual; NORMAL/non-null-TaskId allowlist;
/// round 2dp; tolerance 0.005m; both directions) and serves as its executable spec.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AllocationGateTests : IAsyncLifetime
{
    // Single shared tolerance — mirrors ApprovalEndpoints.AllocationTolerance.
    private const decimal AllocationTolerance = 0.005m;

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private TimeEntryProjectionRepository _timeRepo = null!;
    private WorkTimeProjectionRepository _workTimeRepo = null!;
    private PostgresEventStore _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _timeRepo = new TimeEntryProjectionRepository(_harness.Factory);
        _workTimeRepo = new WorkTimeProjectionRepository(_harness.Factory);
        _outbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Balanced_Approves()
    {
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveWorkTimeAsync(emp, date, new[] { ("08:00", "16:24") }, 0m); // 8.4h
        await SaveTimeEntryAsync(emp, date, 8.4m, "NORMAL", "PROJ-1");

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        Assert.Empty(unbalanced);
    }

    [Fact]
    public async Task UnderAllocated_Blocks_DirectionUnder()
    {
        // worked 7.4 > allocated 3.0 → "under" (under-allocated).
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveWorkTimeAsync(emp, date, new[] { ("08:00", "15:24") }, 0m); // 7.4h
        await SaveTimeEntryAsync(emp, date, 3.0m, "NORMAL", "PROJ-1");

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        var day = Assert.Single(unbalanced);
        Assert.Equal(7.4m, day.Worked);
        Assert.Equal(3.0m, day.Allocated);
        Assert.Equal("under", day.Direction);
    }

    [Fact]
    public async Task OverAllocated_Blocks_DirectionOver()
    {
        // Project hours exist but NO work time recorded → worked=0 < allocated 7.4 → "over".
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveTimeEntryAsync(emp, date, 7.4m, "NORMAL", "PROJ-1");

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        var day = Assert.Single(unbalanced);
        Assert.Equal(0m, day.Worked);   // build-order guard: no work_time row → worked=0
        Assert.Equal(7.4m, day.Allocated);
        Assert.Equal("over", day.Direction);
    }

    [Fact]
    public async Task Tolerance_7point40_vs_7point4_Passes()
    {
        // 7.40 and 7.4 are equal after 2dp rounding → within tolerance → balanced.
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveWorkTimeAsync(emp, date, Array.Empty<(string, string)>(), 7.40m);
        await SaveTimeEntryAsync(emp, date, 7.4m, "NORMAL", "PROJ-1");

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        Assert.Empty(unbalanced);
    }

    [Fact]
    public async Task AbsenceExcluded_OnlyWorkedMustBeAllocated()
    {
        // 3t worked + 4.4t sick. Absences are NOT in time_entries_projection, so
        // allocated only needs to cover the 3t worked. Allocate 3.0 → balanced.
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveWorkTimeAsync(emp, date, new[] { ("08:00", "11:00") }, 0m); // 3.0h worked
        await SaveTimeEntryAsync(emp, date, 3.0m, "NORMAL", "PROJ-1");
        // (the 4.4t SICK absence would live in absences_projection — not seeded here;
        //  the gate never reads it, which is exactly the contract under test.)

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        Assert.Empty(unbalanced);
    }

    [Fact]
    public async Task WeekendWorked_MustBeAllocated()
    {
        // Saturday work is still gated — worked 4.0, allocated 0 → "under".
        var emp = NewEmp();
        var saturday = new DateOnly(2026, 3, 7); // 2026-03-07 is a Saturday
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        await SaveWorkTimeAsync(emp, saturday, new[] { ("10:00", "14:00") }, 0m); // 4.0h

        var unbalanced = await ComputeUnbalancedAsync(emp, saturday, saturday);
        var day = Assert.Single(unbalanced);
        Assert.Equal(4.0m, day.Worked);
        Assert.Equal(0m, day.Allocated);
        Assert.Equal("under", day.Direction);
    }

    [Fact]
    public async Task NullTaskId_NormalEntry_ExcludedFromAllocated()
    {
        // A NORMAL time entry with NULL TaskId is NOT counted as allocated (grid
        // parity). worked 7.4, allocated 0 (the null-TaskId entry is ignored) → "under".
        var emp = NewEmp();
        var date = new DateOnly(2026, 3, 5);
        await SaveWorkTimeAsync(emp, date, new[] { ("08:00", "15:24") }, 0m); // 7.4h
        await SaveTimeEntryAsync(emp, date, 7.4m, "NORMAL", taskId: null);

        var unbalanced = await ComputeUnbalancedAsync(emp, date, date);
        var day = Assert.Single(unbalanced);
        Assert.Equal(7.4m, day.Worked);
        Assert.Equal(0m, day.Allocated); // null-TaskId NORMAL excluded
        Assert.Equal("under", day.Direction);
    }

    // ── Gate algorithm: verbatim mirror of the inline employee-approve gate ──

    private sealed record UnbalancedDay(DateOnly Date, decimal Worked, decimal Allocated, string Direction);

    private async Task<List<UnbalancedDay>> ComputeUnbalancedAsync(
        string employeeId, DateOnly start, DateOnly end)
    {
        var workTimeRows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(employeeId, start, end);
        var timeEntries = await _timeRepo.GetByEmployeeAndDateRangeAsync(employeeId, start, end);

        var workedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var row in workTimeRows)
        {
            var worked = SumIntervalHours(row.Intervals) + row.ManualHours;
            workedByDay[row.Date] = workedByDay.TryGetValue(row.Date, out var existing)
                ? existing + worked : worked;
        }

        var allocatedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var entry in timeEntries)
        {
            if (entry.ActivityType != "NORMAL" || entry.TaskId is null)
                continue;
            allocatedByDay[entry.Date] = allocatedByDay.TryGetValue(entry.Date, out var existing)
                ? existing + entry.Hours : entry.Hours;
        }

        var result = new List<UnbalancedDay>();
        foreach (var day in workedByDay.Keys.Union(allocatedByDay.Keys).OrderBy(d => d))
        {
            var worked = Math.Round(workedByDay.GetValueOrDefault(day), 2);
            var allocated = Math.Round(allocatedByDay.GetValueOrDefault(day), 2);
            if (Math.Abs(worked - allocated) < AllocationTolerance)
                continue;
            result.Add(new UnbalancedDay(day, worked, allocated,
                worked > allocated ? "under" : "over"));
        }
        return result;
    }

    private static decimal SumIntervalHours(IReadOnlyList<WorkInterval> intervals)
    {
        long totalSec = 0;
        foreach (var iv in intervals)
        {
            if (string.IsNullOrEmpty(iv.Start) || string.IsNullOrEmpty(iv.End))
                continue;
            var diff = ParseToSeconds(iv.End) - ParseToSeconds(iv.Start);
            if (diff > 0) totalSec += diff;
        }
        return totalSec / 3600m;
    }

    private static long ParseToSeconds(string hhmmss)
    {
        var parts = hhmmss.Split(':');
        long h = parts.Length > 0 ? long.Parse(parts[0]) : 0;
        long m = parts.Length > 1 ? long.Parse(parts[1]) : 0;
        long s = parts.Length > 2 ? long.Parse(parts[2]) : 0;
        return h * 3600 + m * 60 + s;
    }

    // ── Seed helpers (real outbox + projection upsert/insert in-tx) ──

    private static string NewEmp() => "EMP_ALLOC_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task SaveWorkTimeAsync(
        string employeeId, DateOnly date, (string Start, string End)[] intervals, decimal manualHours)
    {
        var streamId = $"employee-{employeeId}";
        var @event = new WorkTimeRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Intervals = intervals.Select(t => new WorkInterval { Start = t.Start, End = t.End }).ToList(),
            ManualHours = manualHours,
        };
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event);
        await _workTimeRepo.UpsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    private async Task SaveTimeEntryAsync(
        string employeeId, DateOnly date, decimal hours, string activityType, string? taskId)
    {
        var streamId = $"employee-{employeeId}";
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Hours = hours,
            TaskId = taskId,
            ActivityType = activityType,
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event);
        await _timeRepo.InsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }
}
