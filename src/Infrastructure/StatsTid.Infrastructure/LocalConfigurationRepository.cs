using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class LocalConfigurationRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public LocalConfigurationRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<LocalConfiguration?> GetByIdAsync(Guid configId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM local_configurations WHERE config_id = @configId", conn);
        cmd.Parameters.AddWithValue("configId", configId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    public async Task<IReadOnlyList<LocalConfiguration>> GetByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM local_configurations WHERE org_id = @orgId AND is_active = TRUE ORDER BY config_area, config_key", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadConfigsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<LocalConfiguration>> GetActiveByOrgAsync(
        string orgId, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM local_configurations
            WHERE org_id = @orgId AND agreement_code = @agreementCode AND ok_version = @okVersion
              AND is_active = TRUE
              AND effective_from <= CURRENT_DATE
              AND (effective_to IS NULL OR effective_to >= CURRENT_DATE)
            ORDER BY config_area, config_key
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadConfigsAsync(cmd, ct);
    }

    public async Task<Guid> CreateAsync(LocalConfiguration config, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var configId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_configurations (config_id, org_id, config_area, config_key, config_value, effective_from, effective_to, version, agreement_code, ok_version, created_by, approved_by, approved_at, is_active)
            VALUES (@configId, @orgId, @configArea, @configKey, @configValue::jsonb, @effectiveFrom, @effectiveTo, @version, @agreementCode, @okVersion, @createdBy, @approvedBy, @approvedAt, @isActive)
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("orgId", config.OrgId);
        cmd.Parameters.AddWithValue("configArea", config.ConfigArea);
        cmd.Parameters.AddWithValue("configKey", config.ConfigKey);
        cmd.Parameters.AddWithValue("configValue", config.ConfigValue);
        cmd.Parameters.AddWithValue("effectiveFrom", config.EffectiveFrom);
        cmd.Parameters.AddWithValue("effectiveTo", (object?)config.EffectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("version", config.Version);
        cmd.Parameters.AddWithValue("agreementCode", config.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", config.OkVersion);
        cmd.Parameters.AddWithValue("createdBy", config.CreatedBy);
        cmd.Parameters.AddWithValue("approvedBy", (object?)config.ApprovedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("approvedAt", (object?)config.ApprovedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isActive", config.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
        return configId;
    }

    public async Task DeactivateAsync(Guid configId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE local_configurations SET is_active = FALSE WHERE config_id = @configId", conn);
        cmd.Parameters.AddWithValue("configId", configId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendAuditAsync(
        Guid configId, string action, string? previousValue, string? newValue,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_configuration_audit (config_id, action, previous_value, new_value, actor_id, actor_role)
            VALUES (@configId, @action, @previousValue::jsonb, @newValue::jsonb, @actorId, @actorRole)
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousValue", (object?)previousValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newValue", (object?)newValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<LocalConfiguration>> ReadConfigsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var configs = new List<LocalConfiguration>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            configs.Add(ReadConfig(reader));
        return configs;
    }

    private static LocalConfiguration ReadConfig(NpgsqlDataReader reader) => new()
    {
        ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        ConfigArea = reader.GetString(reader.GetOrdinal("config_area")),
        ConfigKey = reader.GetString(reader.GetOrdinal("config_key")),
        ConfigValue = reader.GetString(reader.GetOrdinal("config_value")),
        EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_from"))),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_to"))),
        Version = reader.GetInt32(reader.GetOrdinal("version")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approved_by")) ? null : reader.GetString(reader.GetOrdinal("approved_by")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
