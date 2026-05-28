using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S56 / TASK-5603 atomic-write + read-your-write proof for the
/// <c>work_time_projection</c> read-model and the Skema POST <c>/save</c>
/// work-time path. Mirrors the direct-orchestration shape of
/// <see cref="SkemaProjectionAtomicTests"/> (open outer tx, real
/// <see cref="PostgresEventStore"/> enqueue, in-tx projection upsert, commit)
/// — the focus is the persistence contract, not the HTTP surface.
///
/// <para>
/// Covers: (1) save → GET round-trip (intervals + manualHours survive); (2)
/// re-save same day emits a superseding <see cref="WorkTimeRegistered"/> event
/// and the latest-wins <c>outbox_id&lt;=</c> guard keeps the NEWEST row; (3) the
/// approval-survival regression — save work time, then run the approval
/// transition, then GET STILL returns the work time (the original bug was the
/// work-time row being clobbered/lost across approve).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WorkTimeProjectionAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private WorkTimeProjectionRepository _workTimeRepo = null!;
    private ApprovalPeriodRepository _approvalRepo = null!;
    private PostgresEventStore _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _workTimeRepo = new WorkTimeProjectionRepository(_harness.Factory);
        _approvalRepo = new ApprovalPeriodRepository(_harness.Factory);
        _outbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Save (enqueue WorkTimeRegistered in-tx then upsert projection) → GET via
    /// the date-range read. Intervals AND manual_hours survive the JSONB
    /// round-trip; read-your-write holds (the projection committed in the same
    /// tx as the event).
    /// </summary>
    [Fact]
    public async Task SaveWorkTime_RoundTripsThroughProjection()
    {
        var employeeId = "EMP_WT_RT_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var date = new DateOnly(2026, 3, 5);

        await SaveWorkTimeAsync(employeeId, streamId, date,
            new[] { ("08:00", "12:00"), ("12:30", "16:00") }, manualHours: 0.5m);

        var rows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, date, date);

        var row = Assert.Single(rows);
        Assert.Equal(date, row.Date);
        Assert.Equal(0.5m, row.ManualHours);
        Assert.Collection(row.Intervals,
            iv => { Assert.Equal("08:00", iv.Start); Assert.Equal("12:00", iv.End); },
            iv => { Assert.Equal("12:30", iv.Start); Assert.Equal("16:00", iv.End); });
    }

    /// <summary>
    /// Re-saving the same (employee, date) emits a NEW superseding
    /// WorkTimeRegistered event with a HIGHER outbox_id; the latest-wins upsert
    /// (<c>WHERE work_time_projection.outbox_id &lt;= EXCLUDED.outbox_id</c>)
    /// overwrites the prior row. GET returns exactly one row holding the NEWEST
    /// state.
    /// </summary>
    [Fact]
    public async Task ReSaveSameDay_LatestWins_KeepsNewest()
    {
        var employeeId = "EMP_WT_RESAVE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var date = new DateOnly(2026, 3, 6);

        await SaveWorkTimeAsync(employeeId, streamId, date,
            new[] { ("08:00", "12:00") }, manualHours: 0m);
        await SaveWorkTimeAsync(employeeId, streamId, date,
            new[] { ("09:00", "17:00") }, manualHours: 1.25m);

        var rows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, date, date);

        var row = Assert.Single(rows); // PK (employee_id, date) → one row
        Assert.Equal(1.25m, row.ManualHours);
        var iv = Assert.Single(row.Intervals);
        Assert.Equal("09:00", iv.Start);
        Assert.Equal("17:00", iv.End);

        // Two superseding events were enqueued to the outbox (history retained;
        // the save path enqueues each re-save as a fresh WorkTimeRegistered row).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'WorkTimeRegistered'",
            conn);
        cmd.Parameters.AddWithValue("s", streamId);
        Assert.Equal(2L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
    }

    /// <summary>
    /// Out-of-order replay safety: an upsert carrying a STALE (lower) outbox_id
    /// must NOT clobber a newer row. We upsert outbox_id=100, then attempt
    /// outbox_id=50 with different values; the guard blocks the overwrite (0 rows
    /// affected) and the newer row stands.
    /// </summary>
    [Fact]
    public async Task StaleOutboxId_DoesNotClobberNewerRow()
    {
        var employeeId = "EMP_WT_STALE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var date = new DateOnly(2026, 3, 9);

        var newer = new WorkTimeRegistered
        {
            EmployeeId = employeeId, Date = date,
            Intervals = new[] { new WorkInterval { Start = "09:00", End = "17:00" } },
            ManualHours = 2m,
        };
        var stale = new WorkTimeRegistered
        {
            EmployeeId = employeeId, Date = date,
            Intervals = new[] { new WorkInterval { Start = "00:00", End = "01:00" } },
            ManualHours = 99m,
        };

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _workTimeRepo.UpsertAsync(conn, tx, newer, outboxId: 100);
            await tx.CommitAsync();
        }
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _workTimeRepo.UpsertAsync(conn, tx, stale, outboxId: 50); // stale → blocked
            await tx.CommitAsync();
        }

        var rows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(employeeId, date, date);
        var row = Assert.Single(rows);
        Assert.Equal(2m, row.ManualHours); // newer row stands
        Assert.Equal("09:00", Assert.Single(row.Intervals).Start);
    }

    /// <summary>
    /// THE ORIGINAL BUG: work time saved before approval MUST survive the
    /// approval transition. Save work time, then run the EMPLOYEE_APPROVED state
    /// transition + audit in a separate tx (as the approve endpoint does), then
    /// GET — the work-time row is STILL present and unchanged. Approval touches
    /// approval_periods only; it must not clobber work_time_projection.
    /// </summary>
    [Fact]
    public async Task WorkTime_SurvivesApprovalTransition()
    {
        var employeeId = "EMP_WT_APPR_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var date = new DateOnly(2026, 3, 12);

        await SaveWorkTimeAsync(employeeId, streamId, date,
            new[] { ("08:00", "16:00") }, manualHours: 0m);

        // Run the approval write path (state transition + deadlines + audit) the
        // way the employee-approve endpoint does — a separate tx that does NOT
        // touch work_time_projection.
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = employeeId,
            OrgId = "STY02",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var periodId = await _approvalRepo.CreateAsync(conn, tx, period);
            await _approvalRepo.UpdateStatusAsync(conn, tx, periodId, "EMPLOYEE_APPROVED", "tester");
            await _approvalRepo.AppendAuditAsync(
                conn, tx, periodId, "SUBMITTED", "tester", "EMPLOYEE", "Employee self-approval");
            await tx.CommitAsync();
        }

        // GET work time AFTER approval — the row survives.
        var rows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(employeeId, date, date);
        var row = Assert.Single(rows);
        Assert.Equal("08:00", Assert.Single(row.Intervals).Start);
        Assert.Equal("16:00", Assert.Single(row.Intervals).End);
        Assert.Equal(0m, row.ManualHours);
    }

    /// <summary>
    /// Mirrors SkemaEndpoints.cs save path: enqueue the WorkTimeRegistered event
    /// (real outbox, capturing outbox_id) then upsert the projection in the SAME
    /// tx, then commit — so read-your-write holds.
    /// </summary>
    private async Task SaveWorkTimeAsync(
        string employeeId, string streamId, DateOnly date,
        (string Start, string End)[] intervals, decimal manualHours)
    {
        var @event = new WorkTimeRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Intervals = intervals
                .Select(t => new WorkInterval { Start = t.Start, End = t.End })
                .ToList(),
            ManualHours = manualHours,
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event);
        await _workTimeRepo.UpsertAsync(conn, tx, @event, outboxId);
        await tx.CommitAsync();
    }
}
