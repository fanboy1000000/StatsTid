using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// D12 fixture #1–#5 — <see cref="OutboxPublisher"/> end-to-end behavior on a real
/// Postgres testcontainer. Drives the publisher via <see cref="OutboxPublisher.StartAsync"/>
/// + a poll-loop on the <c>outbox_events.published_at</c> column and verifies the
/// at-least-once + per-stream FIFO contract from ADR-018 D2 / D4 / D5.
///
/// <para>
/// Each test seeds outbox rows directly (or via <see cref="IOutboxEnqueue.EnqueueAsync"/>),
/// runs the publisher, waits for <c>published_at</c> to land, then asserts the canonical
/// <c>events</c> table reflects the expected stream_versions. The Rule Engine is NOT
/// running — these tests exercise the publisher's transactional / ordering contract,
/// not domain rule evaluation.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OutboxPublisherTests : IAsyncLifetime
{
    private const string ServiceId = "backend-api";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private OutboxServiceContext _context = null!;
    private PostgresEventStore _enqueueStore = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        _context = new OutboxServiceContext(ServiceId);
        _enqueueStore = new PostgresEventStore(_harness.Factory, _context);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task HappyPath_EnqueueAndPublish()
    {
        // Seed: enqueue one event inside a tx + commit.
        var streamId = "test-stream-happy";
        var evt = NewProfileEvent("STY02", "HK", "OK24");
        await EnqueueAndCommitAsync(streamId, evt);

        // Run publisher long enough to drain the row, then assert events + outbox state.
        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamId, expectedCount: 1, timeoutMs: 10_000);
            await publisher.StopAsync(CancellationToken.None);
        }

        // events row exists with stream_version = 1.
        var canonicalVersion = await ReadCanonicalEventVersionAsync(evt.EventId);
        Assert.NotNull(canonicalVersion);
        Assert.Equal(1, canonicalVersion);

        // outbox row marked published; stream_version is non-null.
        var (publishedAt, streamVersion) = await ReadOutboxStateAsync(evt.EventId);
        Assert.NotNull(publishedAt);
        Assert.Equal(1, streamVersion);
    }

    [Fact]
    public async Task PublisherRestart_ResumesFromOldestUnpublished()
    {
        // Seed 5 rows on the same stream, all unpublished.
        var streamId = "test-stream-restart";
        var events = new List<LocalAgreementProfileChanged>();
        for (int i = 0; i < 5; i++)
        {
            var e = NewProfileEvent("STY02", "HK", "OK24");
            events.Add(e);
            await EnqueueAndCommitAsync(streamId, e);
        }

        // Start publisher #1 briefly. The publisher's batch-size is 100 so all 5 rows
        // should be processed in the first batch — but we only give it a short window
        // and stop, simulating a mid-batch crash if any remain. Even if all 5 publish
        // quickly, the stronger contract is "after stop+restart, all 5 are still
        // published exactly once", which we then verify.
        using (var publisher1 = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher1.StartAsync(CancellationToken.None);
            // Brief delay to let the loop body run at least once.
            await Task.Delay(500);
            await publisher1.StopAsync(CancellationToken.None);
        }

        // Restart publisher #2. It picks up any unpublished rows starting from oldest.
        using (var publisher2 = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher2.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamId, expectedCount: 5, timeoutMs: 10_000);
            await publisher2.StopAsync(CancellationToken.None);
        }

        // All 5 events arrived in canonical events table; stream_versions are 1..5 in
        // outbox_id order (per-stream FIFO preserved by the publisher).
        for (int i = 0; i < 5; i++)
        {
            var v = await ReadCanonicalEventVersionAsync(events[i].EventId);
            Assert.NotNull(v);
            Assert.Equal(i + 1, v);
        }
    }

    [Fact]
    public async Task PerStreamFifo_OrderedPublishing()
    {
        // Enqueue A, B, C on the same stream. Publisher must publish them in
        // (stream_id, outbox_id ASC) order with consecutive stream_versions.
        var streamId = "test-stream-fifo";
        var a = NewProfileEvent("STY02", "HK", "OK24");
        var b = NewProfileEvent("STY02", "HK", "OK24");
        var c = NewProfileEvent("STY02", "HK", "OK24");
        await EnqueueAndCommitAsync(streamId, a);
        await EnqueueAndCommitAsync(streamId, b);
        await EnqueueAndCommitAsync(streamId, c);

        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamId, expectedCount: 3, timeoutMs: 10_000);
            await publisher.StopAsync(CancellationToken.None);
        }

        // Versions assigned by enqueue order = outbox_id order.
        var va = await ReadCanonicalEventVersionAsync(a.EventId);
        var vb = await ReadCanonicalEventVersionAsync(b.EventId);
        var vc = await ReadCanonicalEventVersionAsync(c.EventId);
        Assert.Equal(1, va);
        Assert.Equal(2, vb);
        Assert.Equal(3, vc);
    }

    [Fact]
    public async Task CrossStreamConcurrency_NoInterference()
    {
        // Interleaved enqueue on streams X and Y. Each stream gets its own version
        // sequence (1..3 each). Publisher's per-stream FIFO is preserved within each
        // group; cross-stream groups can publish concurrently up to the parallelism
        // setting. The functional contract under test: BOTH streams' rows publish, and
        // each stream's own version sequence is consecutive 1..3.
        var streamX = "test-stream-X";
        var streamY = "test-stream-Y";
        var x1 = NewProfileEvent("STY02", "HK", "OK24");
        var y1 = NewProfileEvent("STY03", "HK", "OK24");
        var x2 = NewProfileEvent("STY02", "HK", "OK24");
        var y2 = NewProfileEvent("STY03", "HK", "OK24");
        var x3 = NewProfileEvent("STY02", "HK", "OK24");
        var y3 = NewProfileEvent("STY03", "HK", "OK24");

        await EnqueueAndCommitAsync(streamX, x1);
        await EnqueueAndCommitAsync(streamY, y1);
        await EnqueueAndCommitAsync(streamX, x2);
        await EnqueueAndCommitAsync(streamY, y2);
        await EnqueueAndCommitAsync(streamX, x3);
        await EnqueueAndCommitAsync(streamY, y3);

        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamX, expectedCount: 3, timeoutMs: 10_000);
            await WaitForOutboxDrainedAsync(streamY, expectedCount: 3, timeoutMs: 10_000);
            await publisher.StopAsync(CancellationToken.None);
        }

        // Each stream's own version sequence is consecutive 1..3 in enqueue order.
        Assert.Equal(1, await ReadCanonicalEventVersionAsync(x1.EventId));
        Assert.Equal(2, await ReadCanonicalEventVersionAsync(x2.EventId));
        Assert.Equal(3, await ReadCanonicalEventVersionAsync(x3.EventId));
        Assert.Equal(1, await ReadCanonicalEventVersionAsync(y1.EventId));
        Assert.Equal(2, await ReadCanonicalEventVersionAsync(y2.EventId));
        Assert.Equal(3, await ReadCanonicalEventVersionAsync(y3.EventId));
    }

    [Fact]
    public async Task RolledBackStateChange_DoesNotPublish()
    {
        // Open a tx, EnqueueAsync, ROLLBACK. Outbox row visibility follows tx
        // commit/rollback (ADR-018 D3 transactional outbox contract): the publisher
        // must never see a row whose enqueue tx rolled back.
        var streamId = "test-stream-rollback";
        var evt = NewProfileEvent("STY02", "HK", "OK24");

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await _enqueueStore.EnqueueAsync(conn, tx, streamId, evt);
            await tx.RollbackAsync();
        }

        // Run publisher and give it a generous window — it must NOT publish anything
        // because the row never committed.
        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await Task.Delay(2_000);
            await publisher.StopAsync(CancellationToken.None);
        }

        // Outbox row count for this event_id = 0; events table has no row for this id.
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();
        await using var outboxCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id", verifyConn);
        outboxCmd.Parameters.AddWithValue("id", evt.EventId);
        Assert.Equal(0L, Convert.ToInt64(await outboxCmd.ExecuteScalarAsync()));

        await using var eventCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE event_id = @id", verifyConn);
        eventCmd.Parameters.AddWithValue("id", evt.EventId);
        Assert.Equal(0L, Convert.ToInt64(await eventCmd.ExecuteScalarAsync()));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static LocalAgreementProfileChanged NewProfileEvent(string orgId, string agreementCode, string okVersion) => new()
    {
        ProfileId = Guid.NewGuid(),
        OrgId = orgId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EffectiveFrom = new DateOnly(2026, 5, 4),
        ActorId = "admin1",
        ActorRole = "LocalAdmin",
    };

    private async Task EnqueueAndCommitAsync(string streamId, IDomainEvent evt)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await _enqueueStore.EnqueueAsync(conn, tx, streamId, evt);
        await tx.CommitAsync();
    }

    private async Task WaitForOutboxDrainedAsync(string streamId, int expectedCount, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM outbox_events
                WHERE stream_id = @streamId AND published_at IS NOT NULL
                """, conn);
            cmd.Parameters.AddWithValue("streamId", streamId);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count >= expectedCount) return;
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"OutboxPublisher did not publish {expectedCount} rows on stream {streamId} within {timeoutMs}ms.");
    }

    private async Task<int?> ReadCanonicalEventVersionAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT stream_version FROM events WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull) return null;
        return Convert.ToInt32(result);
    }

    private async Task<(DateTime? PublishedAt, int? StreamVersion)> ReadOutboxStateAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT published_at, stream_version FROM outbox_events WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, null);
        var published = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
        var version = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return (published, version);
    }
}
