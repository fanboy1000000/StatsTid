using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S146 / TASK-14609 (R-24) — <see cref="PayrollOutboxDrain"/> does what its name says. Docker: CI-pending
/// (it cannot run on the owner's machine; it flips to CI-verified at CI-W1).
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollOutboxDrainTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// <b>Kind N (Docker, CI-pending).</b> One event enqueued through the harness's enqueue-capable store
    /// (the Payroll partition) is, after <see cref="PayrollOutboxDrain.DrainAsync"/>, in <c>events</c> with
    /// the same <c>event_id</c>, and its <c>outbox_events</c> row is published.
    ///
    /// <para><b>Plain language.</b> The drain helper stands in for the publisher the test hosts do not run.
    /// If it silently did nothing, every later fact that reads the manifest back from <c>events</c> would
    /// pass or fail for the wrong reason. This fact proves the helper really moves a row across, and that
    /// the id that travels is the one the event was enqueued with.</para>
    ///
    /// <para><b>Red conditions.</b> Names a type this sprint creates, so RED is a mutation, not a revert:
    /// make <c>DrainAsync</c> return before <c>publisher.StartAsync</c> (keep the signature). Nothing then
    /// publishes the row; the helper's poll sees one unpublished row and throws <see cref="TimeoutException"/>
    /// after its limit, so the fact fails (and with a polling short-circuit that skips the wait, the
    /// <c>events</c> assertion below trips on a missing row). A helper that drained the wrong partition
    /// (service id other than <c>"payroll"</c>) fails the same way. Not run locally.</para>
    /// </summary>
    [Fact]
    public async Task PayrollOutboxDrain_EnqueuedPayrollRow_DrainedToEvents_SameEventId()
    {
        var streamId = "payroll-drain-stream-" + Guid.NewGuid().ToString("N");
        var evt = new LocalAgreementProfileChanged
        {
            ProfileId = Guid.NewGuid(),
            OrgId = "STY02",
            AgreementCode = "HK",
            OkVersion = "OK24",
            EffectiveFrom = new DateOnly(2026, 5, 4),
            ActorId = "admin1",
            ActorRole = "LocalAdmin",
        };

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await _harness.EventStore.EnqueueAsync(conn, tx, streamId, evt);
            await tx.CommitAsync();
        }

        // Non-vacuity: the row is on the outbox and not yet in events before the drain.
        Assert.Equal(1L, await ScalarAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id AND service_id = 'payroll' AND published_at IS NULL", evt.EventId));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM events WHERE event_id = @id", evt.EventId));

        await PayrollOutboxDrain.DrainAsync(_harness.Factory, TimeSpan.FromSeconds(15));

        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM events WHERE event_id = @id", evt.EventId));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id AND published_at IS NOT NULL", evt.EventId));
    }

    private async Task<long> ScalarAsync(string sql, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): every caller passes a compile-time constant; the id is a parameter.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("id", eventId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
