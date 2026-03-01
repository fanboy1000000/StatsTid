using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class AuditLogRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AuditLogRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO audit_log (actor_id, actor_role, action, resource, resource_id, correlation_id, http_method, http_path, http_status, result, details, ip_address)
            VALUES (@actorId, @actorRole, @action, @resource, @resourceId, @correlationId, @httpMethod, @httpPath, @httpStatus, @result, @details::jsonb, @ipAddress)
            """, conn);

        cmd.Parameters.AddWithValue("actorId", (object?)entry.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)entry.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("action", entry.Action);
        cmd.Parameters.AddWithValue("resource", entry.Resource);
        cmd.Parameters.AddWithValue("resourceId", (object?)entry.ResourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId", (object?)entry.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("httpMethod", (object?)entry.HttpMethod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("httpPath", (object?)entry.HttpPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("httpStatus", (object?)entry.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("result", entry.Result);
        cmd.Parameters.AddWithValue("details", (object?)entry.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ipAddress", (object?)entry.IpAddress ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryByActorAsync(
        string actorId, DateTime? from = null, DateTime? to = null, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var sql = "SELECT * FROM audit_log WHERE actor_id = @actorId";
        if (from.HasValue) sql += " AND timestamp >= @from";
        if (to.HasValue) sql += " AND timestamp <= @to";
        sql += " ORDER BY timestamp DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("actorId", actorId);
        if (from.HasValue) cmd.Parameters.AddWithValue("from", from.Value);
        if (to.HasValue) cmd.Parameters.AddWithValue("to", to.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEntriesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryByCorrelationAsync(
        Guid correlationId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM audit_log WHERE correlation_id = @correlationId ORDER BY timestamp ASC", conn);
        cmd.Parameters.AddWithValue("correlationId", correlationId);

        return await ReadEntriesAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<AuditLogEntry>> ReadEntriesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var entries = new List<AuditLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new AuditLogEntry
            {
                LogId = reader.GetInt64(reader.GetOrdinal("log_id")),
                Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                ActorId = reader.IsDBNull(reader.GetOrdinal("actor_id")) ? null : reader.GetString(reader.GetOrdinal("actor_id")),
                ActorRole = reader.IsDBNull(reader.GetOrdinal("actor_role")) ? null : reader.GetString(reader.GetOrdinal("actor_role")),
                Action = reader.GetString(reader.GetOrdinal("action")),
                Resource = reader.GetString(reader.GetOrdinal("resource")),
                ResourceId = reader.IsDBNull(reader.GetOrdinal("resource_id")) ? null : reader.GetString(reader.GetOrdinal("resource_id")),
                CorrelationId = reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetGuid(reader.GetOrdinal("correlation_id")),
                HttpMethod = reader.IsDBNull(reader.GetOrdinal("http_method")) ? null : reader.GetString(reader.GetOrdinal("http_method")),
                HttpPath = reader.IsDBNull(reader.GetOrdinal("http_path")) ? null : reader.GetString(reader.GetOrdinal("http_path")),
                HttpStatus = reader.IsDBNull(reader.GetOrdinal("http_status")) ? null : reader.GetInt32(reader.GetOrdinal("http_status")),
                Result = reader.GetString(reader.GetOrdinal("result")),
                Details = reader.IsDBNull(reader.GetOrdinal("details")) ? null : reader.GetString(reader.GetOrdinal("details")),
                IpAddress = reader.IsDBNull(reader.GetOrdinal("ip_address")) ? null : reader.GetString(reader.GetOrdinal("ip_address"))
            });
        }
        return entries;
    }
}
