using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2407's 2 converted timer
/// endpoints. Pattern C — these endpoints do NOT emit an audit row in the same tx, so
/// only the state mutation, canonical event, and outbox-row assertions apply.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/timer/check-in</c> — <see cref="TimerEndpoints"/> L66, stream
///         <c>timer-{employeeId}</c> (<see cref="CheckIn_OutboxFails_RollsBack"/>).</item>
///   <item><c>POST /api/timer/check-out</c> — <see cref="TimerEndpoints"/> L123, stream
///         <c>timer-{employeeId}</c> (<see cref="CheckOut_OutboxFails_RollsBack"/>).</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimerAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private TimerSessionRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new TimerSessionRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task CheckIn_OutboxFails_RollsBack()
    {
        var employeeId = "EMP_FR_TIMER_IN_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"timer-{employeeId}";
        var session = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = employeeId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            CheckInAt = DateTime.UtcNow,
            IsActive = true,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.CheckInAsync(conn, tx, session);

            var @event = new TimerCheckedIn
            {
                EmployeeId = employeeId,
                Date = session.Date,
                CheckInAt = session.CheckInAt,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Pattern C — no audit assertion. State / event / outbox absence still required.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "timer_sessions",
            $"session_id = '{session.SessionId}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task CheckOut_OutboxFails_RollsBack()
    {
        var employeeId = "EMP_FR_TIMER_OUT_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"timer-{employeeId}";
        var session = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = employeeId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            CheckInAt = DateTime.UtcNow,
            IsActive = true,
        };
        await _repo.CheckInAsync(session);

        var checkOutAt = DateTime.UtcNow.AddHours(8);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.CheckOutAsync(conn, tx, session.SessionId, checkOutAt);

            var @event = new TimerCheckedOut
            {
                EmployeeId = employeeId,
                Date = session.Date,
                CheckOutAt = checkOutAt,
                ClockedHours = 8m,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // is_active stays TRUE (rolled back); a row matching is_active=FALSE is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "timer_sessions",
            $"session_id = '{session.SessionId}' AND is_active = FALSE");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }
}
