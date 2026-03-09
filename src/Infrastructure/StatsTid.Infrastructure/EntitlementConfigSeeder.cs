using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Config;

namespace StatsTid.Infrastructure;

/// <summary>
/// Seeds the entitlement_configs table from DefaultEntitlementConfigs on first boot.
/// Idempotent: uses INSERT ON CONFLICT DO NOTHING.
/// </summary>
public static class EntitlementConfigSeeder
{
    public static async Task SeedAsync(DbConnectionFactory dbFactory, ILogger logger)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();

        // Check if any rows exist
        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM entitlement_configs", conn);
        var count = (long)(await countCmd.ExecuteScalarAsync())!;

        if (count > 0)
        {
            logger.LogDebug("Entitlement configs already seeded ({Count} configs) — skipping", count);
            return;
        }

        var configs = DefaultEntitlementConfigs.GetAll();
        logger.LogInformation("Seeding {Count} entitlement configs from DefaultEntitlementConfigs...", configs.Count);

        var seeded = 0;
        foreach (var config in configs)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO entitlement_configs (config_id, entitlement_type, agreement_code, ok_version, annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, created_at)
                VALUES (@configId, @entitlementType, @agreementCode, @okVersion, @annualQuota, @accrualModel, @resetMonth, @carryoverMax, @proRateByPartTime, @isPerEpisode, @minAge, @description, @createdAt)
                ON CONFLICT DO NOTHING", conn);

            cmd.Parameters.AddWithValue("configId", config.ConfigId);
            cmd.Parameters.AddWithValue("entitlementType", config.EntitlementType);
            cmd.Parameters.AddWithValue("agreementCode", config.AgreementCode);
            cmd.Parameters.AddWithValue("okVersion", config.OkVersion);
            cmd.Parameters.AddWithValue("annualQuota", config.AnnualQuota);
            cmd.Parameters.AddWithValue("accrualModel", config.AccrualModel);
            cmd.Parameters.AddWithValue("resetMonth", config.ResetMonth);
            cmd.Parameters.AddWithValue("carryoverMax", config.CarryoverMax);
            cmd.Parameters.AddWithValue("proRateByPartTime", config.ProRateByPartTime);
            cmd.Parameters.AddWithValue("isPerEpisode", config.IsPerEpisode);
            cmd.Parameters.AddWithValue("minAge", config.MinAge.HasValue ? config.MinAge.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("description", config.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("createdAt", config.CreatedAt);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows > 0) seeded++;
        }

        logger.LogInformation("Entitlement config seeding complete — {Seeded} configs inserted", seeded);
    }
}
