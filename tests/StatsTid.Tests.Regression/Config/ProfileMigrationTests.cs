using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixtures #1–#5 — exercises <see cref="LocalAgreementProfileMigrator"/>'s big-bang
/// rewrite of the legacy <c>local_configurations</c> patch-bag into typed
/// <c>local_agreement_profiles</c> rows (ADR-017 D4 + S21 Migration Plan deliverable #3).
///
/// <para>
/// Each test seeds a specific legacy-row shape, runs <see cref="LocalAgreementProfileMigrator.RebuildAsync"/>,
/// then asserts the post-migration state of <c>local_agreement_profiles</c> +
/// <c>local_configuration_audit</c>. Validates the four classification paths:
/// </para>
/// <list type="bullet">
///   <item>Multi-row collision on overridable key (#1)</item>
///   <item>Informational key drop (#2)</item>
///   <item>Unknown / typo key drop (#3)</item>
///   <item>Expired-but-active filter (#4)</item>
///   <item>Happy-path one-row-per-overridable-key (#5)</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileMigrationTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task MultiRowPerKeyCollision_KeepsMostRecentEffectiveFrom()
    {
        // Seed: two rows for (STY02, HK, OK24, MaxFlexBalance), both is_active=TRUE,
        // effective_to NULL, distinct effective_from. Newer row should win; older row
        // should produce a DROPPED_DUPLICATE_AT_MIGRATION audit entry.
        var olderId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "MaxFlexBalance", "80",
            new DateOnly(2024, 1, 1), effectiveTo: null);
        var newerId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "MaxFlexBalance", "100",
            new DateOnly(2025, 6, 1), effectiveTo: null);

        var migrator = new LocalAgreementProfileMigrator(
            _harness.Factory, NullLogger<LocalAgreementProfileMigrator>.Instance);
        var result = await migrator.RebuildAsync();

        Assert.Equal(1, result.ProfilesCreated);
        Assert.Equal(1, result.RowsMigrated);
        Assert.Equal(1, result.RowsDroppedDuplicates);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.NotNull(profile);
        Assert.Equal(100m, profile!.Value.MaxFlexBalance);
        // effective_from = MIN of source effective_froms (winner here is newer; the loser
        // contributes its own effective_from to the MIN consideration only if it is older).
        // Since only the winner was absorbed (loser was dropped), effective_from comes from
        // the winner row (2025-06-01). The migrator's own logic uses the winner's
        // effective_from rather than the per-key MIN — since each overridable key
        // contributes one winner, "earliest among winners" reduces to "winner's".
        Assert.Equal(new DateOnly(2025, 6, 1), profile.Value.EffectiveFrom);

        // The 80-value loser must surface as DROPPED_DUPLICATE_AT_MIGRATION.
        var dropAction = await GetAuditActionForConfigAsync(olderId);
        Assert.Equal("DROPPED_DUPLICATE_AT_MIGRATION", dropAction);
        // The winner produces no DROPPED_* audit entry (it was absorbed).
        Assert.Null(await GetAuditActionForConfigAsync(newerId));
    }

    [Fact]
    public async Task InformationalKey_PlanningStartDay_DroppedWithAudit()
    {
        // Seed: PlanningStartDay is in LegacyInformationalKeys → DROPPED_INFORMATIONAL.
        var configId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "PlanningStartDay", "\"MONDAY\"",
            new DateOnly(2024, 1, 1), effectiveTo: null);

        var migrator = new LocalAgreementProfileMigrator(
            _harness.Factory, NullLogger<LocalAgreementProfileMigrator>.Instance);
        var result = await migrator.RebuildAsync();

        Assert.Equal(0, result.ProfilesCreated);
        Assert.Equal(1, result.RowsDroppedInformational);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.Null(profile);

        var auditAction = await GetAuditActionForConfigAsync(configId);
        Assert.Equal("DROPPED_INFORMATIONAL", auditAction);
    }

    [Fact]
    public async Task TypoKey_MaxOvetimeHoursPerPeriod_DroppedWithAudit()
    {
        // Seed: MaxOvetimeHoursPerPeriod (typo for MaxOvertime…) → DROPPED_UNKNOWN_KEY.
        var configId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "MaxOvetimeHoursPerPeriod", "150",
            new DateOnly(2024, 1, 1), effectiveTo: null);

        var migrator = new LocalAgreementProfileMigrator(
            _harness.Factory, NullLogger<LocalAgreementProfileMigrator>.Instance);
        var result = await migrator.RebuildAsync();

        Assert.Equal(0, result.ProfilesCreated);
        Assert.Equal(1, result.RowsDroppedUnknown);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.Null(profile);

        var auditAction = await GetAuditActionForConfigAsync(configId);
        Assert.Equal("DROPPED_UNKNOWN_KEY", auditAction);
    }

    [Fact]
    public async Task ExpiredButActiveRow_IgnoredEntirely()
    {
        // Seed: row with effective_to in the past but is_active=TRUE → filtered out by the
        // discovery query (effective_to >= CURRENT_DATE). Row stays in local_configurations
        // but contributes nothing to the migration; no profile, no audit.
        var configId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "MaxFlexBalance", "60",
            new DateOnly(2024, 1, 1), effectiveTo: new DateOnly(2024, 12, 31));

        var migrator = new LocalAgreementProfileMigrator(
            _harness.Factory, NullLogger<LocalAgreementProfileMigrator>.Instance);
        var result = await migrator.RebuildAsync();

        Assert.Equal(0, result.ProfilesCreated);
        Assert.Equal(0, result.RowsMigrated);
        Assert.Equal(0, result.RowsDroppedDuplicates);
        Assert.Equal(0, result.RowsDroppedInformational);
        Assert.Equal(0, result.RowsDroppedUnknown);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.Null(profile);

        // No audit emission — the row was filtered out before any classification step.
        Assert.Null(await GetAuditActionForConfigAsync(configId));

        // Legacy row is still present — preserved for audit-history reads.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM local_configurations WHERE config_id = @id", conn);
        cmd.Parameters.AddWithValue("id", configId);
        Assert.Equal(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task OneRowPerOverridableKey_HappyPath()
    {
        // Seed: one row per overridable key (5 total) on (STY02, HK, OK24) with distinct
        // effective_from dates → one profile row with all 5 columns populated; zero drops.
        var d1 = new DateOnly(2024, 1, 1);
        var d2 = new DateOnly(2024, 4, 1);
        var d3 = new DateOnly(2024, 7, 1);
        var d4 = new DateOnly(2024, 10, 1);
        var d5 = new DateOnly(2025, 1, 1);

        await InsertLegacyConfigAsync("STY02", "HK", "OK24", "WeeklyNormHours", "36", d1);
        await InsertLegacyConfigAsync("STY02", "HK", "OK24", "MaxFlexBalance", "100", d2);
        await InsertLegacyConfigAsync("STY02", "HK", "OK24", "FlexCarryoverMax", "10", d3);
        await InsertLegacyConfigAsync("STY02", "HK", "OK24", "MaxOvertimeHoursPerPeriod", "50", d4);
        await InsertLegacyConfigAsync("STY02", "HK", "OK24", "OvertimeRequiresPreApproval", "true", d5);

        var migrator = new LocalAgreementProfileMigrator(
            _harness.Factory, NullLogger<LocalAgreementProfileMigrator>.Instance);
        var result = await migrator.RebuildAsync();

        Assert.Equal(1, result.ProfilesCreated);
        Assert.Equal(5, result.RowsMigrated);
        Assert.Equal(0, result.RowsDroppedDuplicates);
        Assert.Equal(0, result.RowsDroppedInformational);
        Assert.Equal(0, result.RowsDroppedUnknown);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.NotNull(profile);
        Assert.Equal(36m, profile!.Value.WeeklyNormHours);
        Assert.Equal(100m, profile.Value.MaxFlexBalance);
        Assert.Equal(10m, profile.Value.FlexCarryoverMax);
        Assert.Equal(50m, profile.Value.MaxOvertimeHoursPerPeriod);
        Assert.True(profile.Value.OvertimeRequiresPreApproval);

        // effective_from = MIN(picked rows' effective_from) — all 5 are winners, so MIN = d1.
        Assert.Equal(d1, profile.Value.EffectiveFrom);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> InsertLegacyConfigAsync(
        string orgId, string agreementCode, string okVersion,
        string configKey, string rawValue,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var configId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_configurations (
                config_id, org_id, config_area, config_key, config_value,
                effective_from, effective_to, agreement_code, ok_version,
                is_active, created_by)
            VALUES (
                @id, @org, 'WORKING_TIME', @key, @val::jsonb,
                @from, @to, @ac, @ok,
                TRUE, 'test')
            """, conn);
        cmd.Parameters.AddWithValue("id", configId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("key", configKey);
        cmd.Parameters.AddWithValue("val", rawValue);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await cmd.ExecuteNonQueryAsync();
        return configId;
    }

    private async Task<ProfileSnapshot?> GetSingleProfileAsync(
        string orgId, string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, weekly_norm_hours, max_flex_balance, flex_carryover_max,
                   max_overtime_hours_per_period, overtime_requires_pre_approval
            FROM local_agreement_profiles
            WHERE org_id = @org AND agreement_code = @ac AND ok_version = @ok
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ProfileSnapshot(
            EffectiveFrom: DateOnly.FromDateTime(reader.GetDateTime(0)),
            WeeklyNormHours: reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            MaxFlexBalance: reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            FlexCarryoverMax: reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            MaxOvertimeHoursPerPeriod: reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            OvertimeRequiresPreApproval: reader.IsDBNull(5) ? null : reader.GetBoolean(5));
    }

    private async Task<string?> GetAuditActionForConfigAsync(Guid configId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action FROM local_configuration_audit WHERE config_id = @id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", configId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private readonly record struct ProfileSnapshot(
        DateOnly EffectiveFrom,
        decimal? WeeklyNormHours,
        decimal? MaxFlexBalance,
        decimal? FlexCarryoverMax,
        decimal? MaxOvertimeHoursPerPeriod,
        bool? OvertimeRequiresPreApproval);
}
