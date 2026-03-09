using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class EntitlementConfigRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public EntitlementConfigRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EntitlementConfig>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs WHERE agreement_code = @agreementCode AND ok_version = @okVersion ORDER BY entitlement_type",
            conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadConfigsAsync(cmd, ct);
    }

    public async Task<EntitlementConfig?> GetByTypeAsync(
        string entitlementType, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs WHERE entitlement_type = @entitlementType AND agreement_code = @agreementCode AND ok_version = @okVersion",
            conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    public async Task<IReadOnlyList<EntitlementConfig>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs ORDER BY agreement_code, ok_version, entitlement_type",
            conn);
        return await ReadConfigsAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<EntitlementConfig>> ReadConfigsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var configs = new List<EntitlementConfig>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            configs.Add(ReadConfig(reader));
        return configs;
    }

    private static EntitlementConfig ReadConfig(NpgsqlDataReader reader) => new()
    {
        ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
        AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
        ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
        CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
        ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
        IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
        MinAge = reader.IsDBNull(reader.GetOrdinal("min_age")) ? null : reader.GetInt32(reader.GetOrdinal("min_age")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
