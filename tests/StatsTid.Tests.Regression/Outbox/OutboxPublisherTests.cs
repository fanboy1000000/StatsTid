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
    public async Task MaxAttemptsCap_QuarantinedRowIsNotPolled_OthersStillPublish()
    {
        // S23 / TASK-2301: ReadBatchAsync filters `attempts < MaxAttempts` (= 10).
        // A row that's already hit the cap stays unpublished forever; rows on
        // the same partition that haven't crossed the cap continue to publish.
        //
        // We seed Row A via raw INSERT with attempts = 10 already (simulating a
        // poison row that has burned all its retries) and Row B via the normal
        // enqueue path (attempts = 0). After running the publisher: Row B must
        // be published; Row A must remain unpublished.
        var streamA = "test-stream-cap-poison";
        var streamB = "test-stream-cap-fresh";
        var poisonEventId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var insertPoison = new NpgsqlCommand(
                """
                INSERT INTO outbox_events (
                    service_id, stream_id, event_id, event_type, event_payload,
                    correlation_id, actor_id, actor_role, attempts, last_error)
                VALUES (
                    @serviceId, @streamId, @eventId, 'TestPoison', '{}'::jsonb,
                    NULL, NULL, NULL, 10, 'simulated burn')
                """, conn);
            insertPoison.Parameters.AddWithValue("serviceId", ServiceId);
            insertPoison.Parameters.AddWithValue("streamId", streamA);
            insertPoison.Parameters.AddWithValue("eventId", poisonEventId);
            await insertPoison.ExecuteNonQueryAsync();
        }

        var fresh = NewProfileEvent("STY02", "HK", "OK24");
        await EnqueueAndCommitAsync(streamB, fresh);

        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamB, expectedCount: 1, timeoutMs: 10_000);
            // Give the publisher a generous extra window — if the cap predicate
            // were missing, the poison row would be re-attempted in this time.
            await Task.Delay(1_500);
            await publisher.StopAsync(CancellationToken.None);
        }

        // Fresh row published normally.
        var freshVersion = await ReadCanonicalEventVersionAsync(fresh.EventId);
        Assert.NotNull(freshVersion);

        // Poison row remains unpublished AND its attempts count is unchanged
        // (proving the publisher never picked it up — not even to bump attempts).
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();
        await using var poisonCmd = new NpgsqlCommand(
            """
            SELECT published_at, attempts FROM outbox_events WHERE event_id = @id
            """, verifyConn);
        poisonCmd.Parameters.AddWithValue("id", poisonEventId);
        await using var reader = await poisonCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));        // published_at IS NULL
        Assert.Equal(10, reader.GetInt32(1));   // attempts unchanged at 10
    }

    [Fact]
    public async Task ConcurrentEnqueueOnSameStream_PreservesOutboxIdFifo()
    {
        // S23 / TASK-2305 D12 NOTE-4 #1: two endpoints commit transactions
        // concurrently against the same stream_id. The publisher must publish
        // them in outbox_id ASC order (= the SERIAL commit order), preserving
        // the per-stream FIFO contract (ADR-018 D5) even though enqueue tx's
        // ran in parallel.
        var streamId = "test-stream-concurrent-fifo";

        // Two parallel enqueue tasks. Each opens its own connection + tx,
        // enqueues, then commits. PostgreSQL assigns outbox_id (BIGSERIAL) at
        // commit time in commit order; the publisher draining ORDER BY
        // outbox_id ASC must surface stream_versions matching that order.
        var evtA = NewProfileEvent("STY02", "HK", "OK24");
        var evtB = NewProfileEvent("STY02", "HK", "OK24");
        var taskA = EnqueueAndCommitAsync(streamId, evtA);
        var taskB = EnqueueAndCommitAsync(streamId, evtB);
        await Task.WhenAll(taskA, taskB);

        // Read back the outbox_id assignments in commit order — needed because
        // enqueue order != commit order under parallel tasks.
        var (firstEventId, secondEventId) = await ReadConcurrentEnqueueOrderAsync(streamId, evtA.EventId, evtB.EventId);

        // Run publisher and let it drain.
        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForOutboxDrainedAsync(streamId, expectedCount: 2, timeoutMs: 10_000);
            await publisher.StopAsync(CancellationToken.None);
        }

        // Assert: stream_version 1 went to whichever event committed first; 2 to second.
        Assert.Equal(1, await ReadCanonicalEventVersionAsync(firstEventId));
        Assert.Equal(2, await ReadCanonicalEventVersionAsync(secondEventId));
    }

    [Fact]
    public async Task SustainedLoad_FiftyRowsAcrossFourStreams_AllPublishedFifo()
    {
        // S23 / TASK-2305 D12 NOTE-4 #2: drive sustained load (50 rows) across
        // 4 streams to exercise MaxStreamParallelism = 4 saturation. Per-stream
        // FIFO must hold within each stream (versions = enqueue index per stream).
        var streamIds = new[]
        {
            "test-stream-load-A",
            "test-stream-load-B",
            "test-stream-load-C",
            "test-stream-load-D",
        };

        // Seed: 13 rows on A, 13 on B, 12 on C, 12 on D = 50 total. Sequential
        // enqueue per stream so commit order on each stream is deterministic;
        // BIGSERIAL outbox_id is monotone but interleaved across streams.
        var perStream = new Dictionary<string, List<LocalAgreementProfileChanged>>();
        foreach (var s in streamIds) perStream[s] = new List<LocalAgreementProfileChanged>();

        // Round-robin enqueue to interleave outbox_ids across streams realistically.
        for (int i = 0; i < 50; i++)
        {
            var streamId = streamIds[i % streamIds.Length];
            var evt = NewProfileEvent("STY02", "HK", "OK24");
            perStream[streamId].Add(evt);
            await EnqueueAndCommitAsync(streamId, evt);
        }

        using (var publisher = new OutboxPublisher(_harness.Factory, _context, NullLogger<OutboxPublisher>.Instance))
        {
            await publisher.StartAsync(CancellationToken.None);
            foreach (var streamId in streamIds)
            {
                await WaitForOutboxDrainedAsync(streamId, expectedCount: perStream[streamId].Count, timeoutMs: 30_000);
            }
            await publisher.StopAsync(CancellationToken.None);
        }

        // Each stream's own version sequence is consecutive 1..N in enqueue order,
        // independent of cross-stream interleaving.
        foreach (var (streamId, events) in perStream)
        {
            for (int v = 0; v < events.Count; v++)
            {
                var version = await ReadCanonicalEventVersionAsync(events[v].EventId);
                Assert.NotNull(version);
                Assert.Equal(v + 1, version);
            }
        }
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

    private async Task<(Guid First, Guid Second)> ReadConcurrentEnqueueOrderAsync(
        string streamId, Guid candidateA, Guid candidateB)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id FROM outbox_events
            WHERE stream_id = @streamId
              AND event_id IN (@a, @b)
            ORDER BY outbox_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("a", candidateA);
        cmd.Parameters.AddWithValue("b", candidateB);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Concurrent enqueue produced no rows.");
        var first = reader.GetGuid(0);
        Assert.True(await reader.ReadAsync(), "Concurrent enqueue produced only one row.");
        var second = reader.GetGuid(0);
        return (first, second);
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
