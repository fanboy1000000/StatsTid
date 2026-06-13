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
            // S73 / TASK-7301 (R2): full_day_only carried explicitly — the
            // entitlement_configs_full_day_only_types CHECK is evaluated on the proposed tuple
            // BEFORE conflict arbitration, so a DEFAULT-FALSE CARE_DAY/SENIOR_DAY insert would
            // error even when ON CONFLICT would have skipped it.
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO entitlement_configs (config_id, entitlement_type, agreement_code, ok_version, annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, full_day_only, created_at)
                VALUES (@configId, @entitlementType, @agreementCode, @okVersion, @annualQuota, @accrualModel, @resetMonth, @carryoverMax, @proRateByPartTime, @isPerEpisode, @minAge, @description, @fullDayOnly, @createdAt)
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
            cmd.Parameters.AddWithValue("fullDayOnly", config.FullDayOnly);
            cmd.Parameters.AddWithValue("createdAt", config.CreatedAt);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows > 0) seeded++;
        }

        logger.LogInformation("Entitlement config seeding complete — {Seeded} configs inserted", seeded);
    }
}
