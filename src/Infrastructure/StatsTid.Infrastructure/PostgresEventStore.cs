using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;

namespace StatsTid.Infrastructure;

/// <summary>
/// Single concrete event-store implementation. Implements both interface contracts
/// per ADR-018 D3 (cycle-6 split): <see cref="IEventStore"/> for the read +
/// publisher-side append surface, and <see cref="IOutboxEnqueue"/> for the
/// state-change-site in-tx enqueue surface. DI registers the concrete once and
/// exposes it under both contracts.
/// </summary>
public sealed class PostgresEventStore : IEventStore, IOutboxEnqueue
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly OutboxServiceContext? _outboxServiceContext;

    /// <summary>
    /// Read-only / publisher-side constructor. <see cref="EnqueueAsync"/> is
    /// not callable through this overload — services that enqueue must register
    /// with the <see cref="OutboxServiceContext"/> overload below.
    /// </summary>
    public PostgresEventStore(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _outboxServiceContext = null;
    }

    /// <summary>
    /// State-change-site constructor. The injected
    /// <see cref="OutboxServiceContext"/> stamps each enqueued row's
    /// <c>service_id</c> column so the per-service <see cref="OutboxPublisher"/>
    /// can scope its polling query (ADR-018 D2 + D6).
    /// </summary>
    public PostgresEventStore(DbConnectionFactory connectionFactory, OutboxServiceContext outboxServiceContext)
    {
        _connectionFactory = connectionFactory;
        _outboxServiceContext = outboxServiceContext;
    }

    public async Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Ensure stream exists and get next version
        await using var ensureCmd = new NpgsqlCommand(
            """
            INSERT INTO event_streams (stream_id) VALUES (@streamId)
            ON CONFLICT (stream_id) DO NOTHING
            """, conn, tx);
        ensureCmd.Parameters.AddWithValue("streamId", streamId);
        await ensureCmd.ExecuteNonQueryAsync(ct);

        // Get current max version for this stream
        await using var versionCmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(stream_version), 0) FROM events WHERE stream_id = @streamId",
            conn, tx);
        versionCmd.Parameters.AddWithValue("streamId", streamId);
        var currentVersion = Convert.ToInt32(await versionCmd.ExecuteScalarAsync(ct));

        var nextVersion = currentVersion + 1;
        var data = EventSerializer.Serialize(@event);

        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at, actor_id, actor_role, correlation_id)
            VALUES (@eventId, @streamId, @version, @eventType, @data::jsonb, @occurredAt, @actorId, @actorRole, @correlationId)
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("eventId", @event.EventId);
        insertCmd.Parameters.AddWithValue("streamId", streamId);
        insertCmd.Parameters.AddWithValue("version", nextVersion);
        insertCmd.Parameters.AddWithValue("eventType", @event.EventType);
        insertCmd.Parameters.AddWithValue("data", NpgsqlDbType.Text, data);
        insertCmd.Parameters.AddWithValue("occurredAt", @event.OccurredAt);
        insertCmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);

        await insertCmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<IDomainEvent>> ReadStreamAsync(string streamId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_type, data FROM events
            WHERE stream_id = @streamId
            ORDER BY stream_version ASC
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);

        var events = new List<IDomainEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var eventType = reader.GetString(0);
            var data = reader.GetString(1);
            events.Add(EventSerializer.Deserialize(eventType, data));
        }

        return events;
    }

    public async Task<IReadOnlyList<IDomainEvent>> ReadAllAsync(int fromPosition = 0, int maxCount = 1000, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_type, data FROM events
            ORDER BY global_position ASC
            OFFSET @offset LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("offset", fromPosition);
        cmd.Parameters.AddWithValue("limit", maxCount);

        var events = new List<IDomainEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var eventType = reader.GetString(0);
            var data = reader.GetString(1);
            events.Add(EventSerializer.Deserialize(eventType, data));
        }

        return events;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ADR-018 D1 + D3: writes a single row to <c>outbox_events</c> using the
    /// caller's <paramref name="conn"/>+<paramref name="tx"/>. Visibility follows
    /// the caller's tx commit/rollback. The <c>service_id</c> column is stamped
    /// from the injected <see cref="OutboxServiceContext"/> so the per-service
    /// <see cref="OutboxPublisher"/> can scope its polling query (ADR-018 D2).
    /// <para>
    /// Audit-context columns (<c>correlation_id</c>, <c>actor_id</c>, <c>actor_role</c>)
    /// mirror the canonical event's audit fields at enqueue time so the
    /// publisher can write the canonical <c>events</c> row without
    /// deserializing the JSONB payload (ADR-018 D1 cycle-2 design choice).
    /// </para>
    /// </remarks>
    public async Task EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string streamId,
        IDomainEvent @event,
        CancellationToken ct = default)
    {
        if (_outboxServiceContext is null)
        {
            throw new InvalidOperationException(
                "PostgresEventStore.EnqueueAsync requires an OutboxServiceContext. " +
                "Register the concrete with the (DbConnectionFactory, OutboxServiceContext) " +
                "constructor in DI per ADR-018 D3 dual-binding pattern.");
        }

        var payload = EventSerializer.Serialize(@event);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (
                service_id, stream_id, event_id, event_type, event_payload,
                correlation_id, actor_id, actor_role
            )
            VALUES (
                @serviceId, @streamId, @eventId, @eventType, @payload::jsonb,
                @correlationId, @actorId, @actorRole
            )
            """, conn, tx);
        cmd.Parameters.AddWithValue("serviceId", _outboxServiceContext.ServiceId);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventId", @event.EventId);
        cmd.Parameters.AddWithValue("eventType", @event.EventType);
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
        // outbox_events.correlation_id is TEXT (vs events.correlation_id UUID).
        // Stringify the Guid so the column type matches; the publisher parses
        // back to Guid when binding to the canonical events.correlation_id UUID
        // column (see OutboxPublisher.InsertEventAsync).
        cmd.Parameters.AddWithValue("correlationId",
            @event.CorrelationId.HasValue ? (object)@event.CorrelationId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
