using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class WageTypeMappingRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public WageTypeMappingRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WageTypeMapping>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM wage_type_mappings ORDER BY agreement_code, ok_version, time_type, position", conn);
        return await ReadMappingsAsync(cmd, ct);
    }

    public async Task<WageTypeMapping?> GetByKeyAsync(
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMapping(reader) : null;
    }

    public async Task<bool> CreateAsync(WageTypeMapping mapping, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteCreateAsync(conn, null, mapping, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="CreateAsync(WageTypeMapping, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across audit + outbox writes
    /// (ADR-018 D3 transactional-outbox contract). The caller commits or rolls back.
    /// </summary>
    public async Task<bool> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping mapping, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, mapping, ct);

    private static async Task<bool> ExecuteCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        WageTypeMapping mapping, CancellationToken ct)
    {
        var sql =
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
            VALUES (@timeType, @wageType, @okVersion, @agreementCode, @position, @description)
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        cmd.Parameters.AddWithValue("wageType", mapping.WageType);
        cmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        cmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        cmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(WageTypeMapping mapping, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteUpdateAsync(conn, null, mapping, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateAsync(WageTypeMapping, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping mapping, CancellationToken ct = default)
        => await ExecuteUpdateAsync(conn, tx, mapping, ct);

    private static async Task<bool> ExecuteUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        WageTypeMapping mapping, CancellationToken ct)
    {
        var sql =
            """
            UPDATE wage_type_mappings SET
                wage_type = @wageType,
                description = @description
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("wageType", mapping.WageType);
        cmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        cmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        cmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteDeleteAsync(conn, null, timeType, okVersion, agreementCode, position, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="DeleteAsync(string, string, string, string, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> DeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
        => await ExecuteDeleteAsync(conn, tx, timeType, okVersion, agreementCode, position, ct);

    private static async Task<bool> ExecuteDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct)
    {
        var sql =
            """
            DELETE FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<WageTypeMapping>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
            ORDER BY time_type, position
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadMappingsAsync(cmd, ct);
    }

    public async Task AppendAuditAsync(
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mapping_audit (time_type, ok_version, agreement_code, position, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@timeType, @okVersion, @agreementCode, @position, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn);
        AddAuditParameters(cmd, timeType, okVersion, agreementCode, position, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="AppendAuditAsync(string, string, string, string, string, string?, string?, string, string, CancellationToken)"/>.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mapping_audit (time_type, ok_version, agreement_code, position, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@timeType, @okVersion, @agreementCode, @position, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn, tx);
        AddAuditParameters(cmd, timeType, okVersion, agreementCode, position, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddAuditParameters(
        NpgsqlCommand cmd,
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole)
    {
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
    }

    private static async Task<IReadOnlyList<WageTypeMapping>> ReadMappingsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var mappings = new List<WageTypeMapping>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            mappings.Add(ReadMapping(reader));
        return mappings;
    }

    private static WageTypeMapping ReadMapping(NpgsqlDataReader reader) => new()
    {
        TimeType = reader.GetString(reader.GetOrdinal("time_type")),
        WageType = reader.GetString(reader.GetOrdinal("wage_type")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        Position = reader.GetString(reader.GetOrdinal("position")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
    };
}
