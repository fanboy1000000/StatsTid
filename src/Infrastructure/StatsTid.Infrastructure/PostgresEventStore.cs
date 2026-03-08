using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;

namespace StatsTid.Infrastructure;

public sealed class PostgresEventStore : IEventStore
{
    private readonly DbConnectionFactory _connectionFactory;

    public PostgresEventStore(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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
}
