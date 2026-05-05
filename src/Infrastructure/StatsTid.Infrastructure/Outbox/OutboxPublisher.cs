using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure.Outbox;

/// <summary>
/// Per-service polling publisher that drains its own <c>service_id</c> partition
/// of <c>outbox_events</c> into the canonical <c>events</c> table with
/// at-least-once + per-stream FIFO semantics (ADR-018 D2 + D4 + D5).
///
/// <para>
/// The publisher's transaction uses <see cref="IsolationLevel.ReadCommitted"/>
/// rather than <see cref="IsolationLevel.RepeatableRead"/> — under RepeatableRead
/// the snapshot is fixed at <c>BEGIN</c>, so even after waiting on
/// <c>SELECT ... FOR UPDATE</c> the post-lock-wait <c>MAX(stream_version)</c>
/// read still observes the pre-commit snapshot and picks the same next version
/// as the prior publisher, reproducing the exact bug ADR-018 was designed to
/// eliminate. ReadCommitted gives each statement a fresh snapshot of latest
/// committed data, which is what the publisher's correctness requires.
/// </para>
///
/// <para>
/// Per ADR-018 D6, Orchestrator MAY NOT register this hosted service.
/// </para>
/// </summary>
public sealed class OutboxPublisher : BackgroundService
{
    /// <summary>Maximum rows fetched per poll iteration.</summary>
    private const int BatchSize = 100;

    /// <summary>Quiet-poll backoff delay (no rows fetched).</summary>
    private const int QuietPollIntervalMs = 1000;

    /// <summary>Active-poll delay (rows were fetched and processed).</summary>
    private const int ActivePollIntervalMs = 250;

    /// <summary>Backoff after an unhandled exception in the loop body.</summary>
    private const int LoopErrorBackoffMs = 2000;

    /// <summary>Concurrent stream parallelism — per-stream FIFO is preserved
    /// within each group (ADR-018 D5).</summary>
    private const int MaxStreamParallelism = 4;

    private readonly DbConnectionFactory _connectionFactory;
    private readonly OutboxServiceContext _serviceContext;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        DbConnectionFactory connectionFactory,
        OutboxServiceContext serviceContext,
        ILogger<OutboxPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _serviceContext = serviceContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxPublisher started for service {ServiceId} (batch={BatchSize}, parallelism={Parallelism})",
            _serviceContext.ServiceId, BatchSize, MaxStreamParallelism);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await ReadBatchAsync(stoppingToken).ConfigureAwait(false);

                if (batch.Count == 0)
                {
                    await Task.Delay(QuietPollIntervalMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Per-stream FIFO: group by stream_id, then publish each group
                // sequentially in outbox_id order (ADR-018 D5). Cross-stream
                // groups can publish concurrently up to MaxStreamParallelism.
                var byStream = batch.GroupBy(r => r.StreamId);
                await Parallel.ForEachAsync(
                    byStream,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxStreamParallelism,
                        CancellationToken = stoppingToken
                    },
                    async (group, innerCt) =>
                    {
                        foreach (var row in group.OrderBy(r => r.OutboxId))
                        {
                            try
                            {
                                await PublishAsync(row, innerCt).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (innerCt.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (PublisherCorrelationException ex)
                            {
                                // Per ADR-018 D4: surface loudly. The bookkeeping
                                // (attempts/last_error) is already recorded by
                                // PublishAsync's catch block before throw. Manual
                                // reconcile is required; break out of the stream
                                // group so later rows in this stream don't overtake
                                // the stuck row (per-stream FIFO, ADR-018 D5).
                                // Other stream groups continue independently.
                                _logger.LogError(ex,
                                    "OutboxPublisher correlation mismatch on outbox {OutboxId} stream {StreamId} event_id {EventId}; manual reconcile required; halting this stream group to preserve FIFO",
                                    row.OutboxId, row.StreamId, row.EventId);
                                break;
                            }
                            catch (Exception ex)
                            {
                                // Per-stream FIFO (ADR-018 D5): on transient failure
                                // BREAK out of the stream group rather than continue.
                                // If we continued, later rows on this stream could
                                // be published at higher stream_versions while the
                                // failed row's retry would land at an even-higher
                                // version, permanently reordering the stream.
                                // Cross-stream parallelism is preserved — other
                                // groups in the Parallel.ForEachAsync continue.
                                _logger.LogWarning(ex,
                                    "OutboxPublisher failed to publish outbox {OutboxId} stream {StreamId}; will retry; halting this stream group to preserve FIFO",
                                    row.OutboxId, row.StreamId);
                                break;
                            }
                        }
                    }).ConfigureAwait(false);

                await Task.Delay(ActivePollIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OutboxPublisher loop error for service {ServiceId}; backing off",
                    _serviceContext.ServiceId);
                try
                {
                    await Task.Delay(LoopErrorBackoffMs, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation(
            "OutboxPublisher stopped for service {ServiceId}",
            _serviceContext.ServiceId);
    }

    /// <summary>
    /// Reads up to <see cref="BatchSize"/> unpublished outbox rows for THIS
    /// service partition. Ordered by <c>outbox_id ASC</c> (= enqueue order;
    /// BIGSERIAL).
    /// </summary>
    private async Task<List<OutboxRow>> ReadBatchAsync(CancellationToken ct)
    {
        var rows = new List<OutboxRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT outbox_id, stream_id, event_id, event_type, event_payload,
                   correlation_id, actor_id, actor_role, created_at
            FROM outbox_events
            WHERE service_id = @serviceId
              AND published_at IS NULL
            ORDER BY outbox_id ASC
            LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("serviceId", _serviceContext.ServiceId);
        cmd.Parameters.AddWithValue("limit", BatchSize);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OutboxRow(
                OutboxId: reader.GetInt64(0),
                StreamId: reader.GetString(1),
                EventId: reader.GetGuid(2),
                EventType: reader.GetString(3),
                EventPayload: reader.GetString(4),
                CorrelationId: reader.IsDBNull(5) ? null : reader.GetString(5),
                ActorId: reader.IsDBNull(6) ? null : reader.GetString(6),
                ActorRole: reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt: reader.GetDateTime(8)));
        }

        return rows;
    }

    /// <summary>
    /// Publishes one outbox row into the canonical <c>events</c> table per
    /// the ADR-018 D4 protocol. ReadCommitted tx; FOR UPDATE serialization;
    /// event_id correlation on 23505 retry.
    /// </summary>
    private async Task PublishAsync(OutboxRow row, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // ADR-018 D4: ReadCommitted (NOT RepeatableRead). Each statement gets
        // a fresh snapshot of latest committed data, so the post-FOR-UPDATE
        // MAX(stream_version) read correctly observes the prior publisher's
        // just-committed row.
        await using var tx = await conn
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        try
        {
            // Step 2: ensure stream row + acquire stream lock. Mirrors the
            // existing PostgresEventStore.AppendAsync ON CONFLICT pattern;
            // the FOR UPDATE serializes concurrent publishers on this stream.
            await EnsureStreamRowAsync(conn, tx, row.StreamId, ct).ConfigureAwait(false);
            await AcquireStreamLockAsync(conn, tx, row.StreamId, ct).ConfigureAwait(false);

            // Step 3: compute next version. Fresh snapshot under ReadCommitted.
            var version = await ComputeNextStreamVersionAsync(conn, tx, row.StreamId, ct)
                .ConfigureAwait(false);

            // Step 4: insert canonical event using the outbox row's event_id
            // (NOT a fresh GUID — at-least-once correlation key per D4).
            // ADR-018 D4 design choice: bind audit-context (correlation_id,
            // actor_id, actor_role) directly from the outbox row and use the
            // row's created_at as the occurred_at proxy, avoiding a payload
            // deserialize on the publisher path.
            try
            {
                await InsertEventAsync(conn, tx, row, version, ct).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // 23505 on the events INSERT can be either:
                //   (a) Crash-during-publish recovery: this same outbox row
                //       was published successfully on a prior attempt before
                //       step 5 (UPDATE outbox_events) ran. The events row
                //       already exists with this exact event_id. Idempotent
                //       retry: read the existing version and continue to the
                //       MarkPublished step.
                //   (b) Different-writer collision: a different outbox row's
                //       event won the (stream_id, stream_version) slot. This
                //       is unreachable under D6 single-writer-per-stream + D4
                //       FOR UPDATE serialization, but the defensive lookup
                //       proves it.
                var existingVersion = await TryGetExistingEventVersionAsync(
                    conn, tx, row.EventId, ct).ConfigureAwait(false);

                if (existingVersion is null)
                {
                    throw new PublisherCorrelationException(
                        $"23505 on stream {row.StreamId} for outbox {row.OutboxId} but " +
                        $"event_id {row.EventId} not found in events table; another writer " +
                        $"won the version slot. Manual reconcile required.",
                        ex);
                }

                version = existingVersion.Value;
            }

            // Step 5: mark outbox row published.
            await MarkPublishedAsync(conn, tx, row.OutboxId, version, ct).ConfigureAwait(false);

            // Step 6: commit the publisher tx.
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PublisherCorrelationException)
        {
            // Roll back the publisher tx; bookkeeping (attempts/last_error) on
            // a separate auto-commit conn so the rollback above doesn't undo it.
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            await IncrementAttemptsAsync(row.OutboxId, "PublisherCorrelationException", ct)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            await IncrementAttemptsAsync(row.OutboxId, ex.GetType().Name + ": " + ex.Message, ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureStreamRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO event_streams (stream_id) VALUES (@streamId)
            ON CONFLICT (stream_id) DO NOTHING
            """, conn, tx);
        cmd.Parameters.AddWithValue("streamId", streamId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task AcquireStreamLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, CancellationToken ct)
    {
        // SELECT 1 FROM event_streams WHERE stream_id = @id FOR UPDATE.
        // Locks the parent stream row for the duration of the publisher tx,
        // serializing concurrent publishers on the same stream (ADR-018 D4).
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM event_streams WHERE stream_id = @streamId FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("streamId", streamId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> ComputeNextStreamVersionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(stream_version), 0) + 1 FROM events WHERE stream_id = @streamId",
            conn, tx);
        cmd.Parameters.AddWithValue("streamId", streamId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, OutboxRow row, int version, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO events (
                event_id, stream_id, stream_version, event_type, data, occurred_at,
                actor_id, actor_role, correlation_id
            )
            VALUES (
                @eventId, @streamId, @version, @eventType, @data::jsonb, @occurredAt,
                @actorId, @actorRole, @correlationId
            )
            """, conn, tx);
        cmd.Parameters.AddWithValue("eventId", row.EventId);
        cmd.Parameters.AddWithValue("streamId", row.StreamId);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("eventType", row.EventType);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Text, row.EventPayload);
        // occurred_at proxy: outbox row created_at (in caller's enqueue tx).
        cmd.Parameters.AddWithValue("occurredAt", row.CreatedAt);
        cmd.Parameters.AddWithValue("actorId", (object?)row.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)row.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId",
            row.CorrelationId is null
                ? DBNull.Value
                : Guid.TryParse(row.CorrelationId, out var corr) ? (object)corr : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int?> TryGetExistingEventVersionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid eventId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT stream_version FROM events WHERE event_id = @eventId",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", eventId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result is DBNull) return null;
        return Convert.ToInt32(result);
    }

    private static async Task MarkPublishedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long outboxId, int version, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE outbox_events
            SET published_at = NOW(), stream_version = @version
            WHERE outbox_id = @outboxId
            """, conn, tx);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task IncrementAttemptsAsync(long outboxId, string lastError, CancellationToken ct)
    {
        // Bookkeeping happens on a SEPARATE connection so it is NOT rolled back
        // by the publisher tx's rollback. Best-effort: if this itself fails,
        // we swallow rather than re-throw and lose the original error context.
        try
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE outbox_events
                SET attempts = attempts + 1,
                    last_error = @lastError,
                    last_attempt_at = NOW()
                WHERE outbox_id = @outboxId
                """, conn);
            cmd.Parameters.AddWithValue("outboxId", outboxId);
            cmd.Parameters.AddWithValue("lastError", lastError ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OutboxPublisher failed to record attempt-bookkeeping for outbox {OutboxId}",
                outboxId);
        }
    }

    private async Task SafeRollbackAsync(NpgsqlTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OutboxPublisher rollback failed (likely already rolled back)");
        }
    }

    /// <summary>
    /// In-memory shape of one outbox row read by the publisher.
    /// </summary>
    private sealed record OutboxRow(
        long OutboxId,
        string StreamId,
        Guid EventId,
        string EventType,
        string EventPayload,
        string? CorrelationId,
        string? ActorId,
        string? ActorRole,
        DateTime CreatedAt);
}
