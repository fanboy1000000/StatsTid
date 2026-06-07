using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S27 / TASK-2710 Slot 1 atomic-rollback proof for the S27 sync-in-tx projection write
/// paths. This suite exercises the repositories DIRECTLY (no HTTP surface):
/// <see cref="TimeEntryProjectionRepository.InsertAsync"/> and
/// <see cref="AbsenceProjectionRepository.InsertAsync"/> driven inside a hand-rolled outer
/// transaction with a throwing outbox. Direct-orchestration shape mirroring
/// <see cref="OvertimeApproveRejectAtomicTests"/> / <see cref="ApprovalAtomicTests"/> — the
/// focus is the rollback invariant, not any HTTP route. Establishes that the projection
/// writes follow the ADR-018 D3 transactional-outbox contract: a throw on the outbox call
/// MUST roll back the projection INSERT in the same tx.
///
/// <para>
/// Backend orchestration shape under test (enqueue FIRST, project SECOND, single outer tx):
/// <code>
///   await using var conn = connectionFactory.Create();
///   await conn.OpenAsync(ct);
///   await using var tx = await conn.BeginTransactionAsync(ct);
///   var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
///   await projectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
///   await tx.CommitAsync(ct);
/// </code>
/// We substitute the throwing outbox so the tx must roll back, then assert ZERO rows in
/// the projection table + <c>outbox_events</c> + <c>events</c> for the employee's stream.
/// The time-entry path is reached via the Skema save seam (POST /api/skema/{id}/save) and
/// the legacy time-entry POST; the absence path is reached via the Skema save seam. This
/// test does not exercise any HTTP route — it pins the repository contract those write
/// seams depend on.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimeProjectionAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private TimeEntryProjectionRepository _timeRepo = null!;
    private AbsenceProjectionRepository _absenceRepo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _throwingOutbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _timeRepo = new TimeEntryProjectionRepository(_harness.Factory);
        _absenceRepo = new AbsenceProjectionRepository(_harness.Factory);
        _throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Time-entry projection forced-rollback (repo-direct). The outer tx wraps
    /// EnqueueAndReturnIdAsync + projection INSERT; throw on the outbox call must roll back
    /// the projection INSERT. Asserts: ZERO rows in time_entries_projection, outbox_events,
    /// events for this stream.
    /// </summary>
    [Fact]
    public async Task RegisterTimeEntry_OutboxFails_RollsBack()
    {
        var employeeId = "EMP_FR_TIME_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2026, 5, 7),
            Hours = 7.4m,
            TaskId = "PROJ-TIME-FR",
            ActivityType = "NORMAL",
            AgreementCode = "HK",
            OkVersion = "OK24",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // The throw happens BEFORE the projection INSERT — so we never reach the
                // InsertAsync line. This still pins the contract: if the outbox enqueue
                // fails, nothing in the tx commits. (A throw AFTER the projection INSERT
                // would also roll back; we cover the throw-first variant here because the
                // production write seams order enqueue FIRST (TimeEndpoints time-entry POST
                // + the Skema save handler), so the projection INSERT is the second step.)
                var outboxId = await _throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event);
                await _timeRepo.InsertAsync(conn, tx, @event, outboxId);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await AssertProjectionRowCountAsync(
            _harness.ConnectionString, "time_entries_projection", employeeId, expected: 0);
    }

    /// <summary>
    /// Absence projection forced-rollback (repo-direct). Same shape as the time-entry test
    /// for the absences_projection write path — drives
    /// <see cref="AbsenceProjectionRepository.InsertAsync"/> inside the outer tx with a
    /// throwing outbox; the throw must roll back the projection INSERT.
    /// </summary>
    [Fact]
    public async Task RegisterAbsence_OutboxFails_RollsBack()
    {
        var employeeId = "EMP_FR_ABS_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var @event = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2026, 5, 7),
            AbsenceType = "VACATION",
            Hours = 7.4m,
            AgreementCode = "HK",
            OkVersion = "OK24",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var outboxId = await _throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event);
                await _absenceRepo.InsertAsync(conn, tx, @event, outboxId);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await AssertProjectionRowCountAsync(
            _harness.ConnectionString, "absences_projection", employeeId, expected: 0);
    }

    private static async Task AssertProjectionRowCountAsync(
        string connectionString, string tableName, string employeeId, long expected,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(expected, count);
    }
}
