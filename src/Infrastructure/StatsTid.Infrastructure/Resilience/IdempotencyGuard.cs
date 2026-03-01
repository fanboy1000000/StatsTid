using Npgsql;

namespace StatsTid.Infrastructure.Resilience;

/// <summary>
/// Checks outbox for already-delivered messages to prevent duplicate processing.
/// </summary>
public sealed class IdempotencyGuard
{
    private readonly DbConnectionFactory _connectionFactory;

    public IdempotencyGuard(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> HasBeenDeliveredAsync(Guid idempotencyToken, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @token AND status = 'delivered'",
            conn);
        cmd.Parameters.AddWithValue("token", idempotencyToken);

        var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return count > 0;
    }
}
