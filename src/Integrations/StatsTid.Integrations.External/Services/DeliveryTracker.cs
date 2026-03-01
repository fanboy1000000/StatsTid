using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.External.Services;

public sealed class DeliveryTracker
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<DeliveryTracker> _logger;

    public DeliveryTracker(DbConnectionFactory connectionFactory, ILogger<DeliveryTracker> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task MarkDeliveredAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE outbox_messages
            SET status = 'delivered', delivered_at = NOW(), attempt_count = attempt_count + 1
            WHERE message_id = @messageId
            """, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Message {MessageId} marked as delivered", messageId);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE outbox_messages
            SET status = 'failed', last_attempt_at = NOW(), attempt_count = attempt_count + 1, error_message = @error
            WHERE message_id = @messageId
            """, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("error", error);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogWarning("Message {MessageId} marked as failed: {Error}", messageId, error);
    }

    public async Task MarkDeadLetterAsync(Guid messageId, string reason, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE outbox_messages
            SET status = 'dead_letter', last_attempt_at = NOW(), error_message = @error
            WHERE message_id = @messageId
            """, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("error", reason);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogError("Message {MessageId} moved to dead letter: {Reason}", messageId, reason);
    }
}
