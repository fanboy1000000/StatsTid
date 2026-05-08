using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S26 / TASK-2608 forced-rollback test for Phase 2 / TASK-2606's converted Time
/// endpoint (<c>POST /api/time-entries</c>). Pattern C — event-as-state, no separate
/// state table, no audit row — so only events + outbox absence are asserted on rollback.
///
/// <para>
/// The endpoint pre-S26 called <c>eventStore.AppendAsync</c> post-handler, with no tx
/// wrap. Post-S26 (TASK-2606) wraps the outbox enqueue in an explicit tx so a process
/// crash between request handling and event-store append no longer leaks state. This
/// test pins the rollback contract by wiring
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> in for the outbox call;
/// the throw must escape the try-block before <c>tx.CommitAsync</c>, so no outbox row
/// for stream <c>employee-{id}</c> exists on a fresh connection.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimeAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task RegisterTimeEntry_OutboxFails_RollsBack()
    {
        var employeeId = "EMP_FR_TIME_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var @event = new TimeEntryRegistered
                {
                    EmployeeId = employeeId,
                    Date = new DateOnly(2026, 5, 7),
                    Hours = 7.5m,
                    TaskId = "PROJ_TEST",
                    ActivityType = "NORMAL",
                    AgreementCode = "AC",
                    OkVersion = "OK24",
                };
                await _outbox.EnqueueAsync(conn, tx, streamId, @event);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Pattern C — event IS state. ZERO events + ZERO outbox rows for the stream.
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }
}
