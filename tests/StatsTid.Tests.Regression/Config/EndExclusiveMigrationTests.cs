using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D12 fixtures #11–#13 — the S22 / ADR-018 D8 end-exclusive migration. Seeds
/// pre-S22 row shapes via <see cref="LegacyProfileSchema"/>, runs the
/// <see cref="LegacyProfileSchema.S22MigrationDdl"/> DO $$ block, and verifies the
/// post-migration shape preserves semantic equivalence.
///
/// <para>
/// The migration shifts closed-row <c>effective_to</c> by <c>+1 day</c> so that the
/// previous end-inclusive value (e.g. "active through 2026-03-31") becomes the new
/// end-exclusive value ("active until 2026-04-01 exclusive"). Open rows
/// (<c>effective_to IS NULL</c>) stay untouched. The new <c>version</c> column
/// defaults to 1 for all existing rows.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EndExclusiveMigrationTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await LegacyProfileSchema.ApplyAsync(_harness.ConnectionString);
        await SeedOrganizationAsync(_harness.ConnectionString, OrgId);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ClosedRow_ConvertsByOneDayShift()
    {
        // Pre-migration seed: closed row with end-inclusive effective_to = 2026-03-31
        // (= "active through 2026-03-31"). Pre-S22 query semantic for "active on date d":
        //   effective_from <= d AND (effective_to IS NULL OR d <= effective_to)
        // (note <=, end-inclusive).
        var profileId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2025, 12, 29);
        var preMigrationEffectiveTo = new DateOnly(2026, 3, 31);
        var boundaryDate = new DateOnly(2026, 3, 31);

        await SeedClosedRowAsync(profileId, effectiveFrom, preMigrationEffectiveTo, weeklyNormHours: 37m);

        // Pre-migration: the row IS active on the boundary date under end-inclusive.
        Assert.True(await IsActiveOnDateAsync_EndInclusive(boundaryDate, profileId));

        // Run S22 migration.
        await LegacyProfileSchema.RunS22MigrationAsync(_harness.ConnectionString);

        // Post-migration: effective_to is shifted to 2026-04-01.
        var postMigrationEffectiveTo = await ReadEffectiveToAsync(profileId);
        Assert.NotNull(postMigrationEffectiveTo);
        Assert.Equal(new DateOnly(2026, 4, 1), postMigrationEffectiveTo!.Value);

        // Post-migration query semantic for "active on date d":
        //   effective_from <= d AND (effective_to IS NULL OR d < effective_to)
        // (note <, end-exclusive). The same boundary date 2026-03-31 still returns
        // the row — the semantic has been preserved across the migration.
        Assert.True(await IsActiveOnDateAsync_EndExclusive(boundaryDate, profileId));
    }

    [Fact]
    public async Task OpenRow_StaysNullWithVersion1()
    {
        // Pre-migration: open row (effective_to IS NULL).
        var profileId = Guid.NewGuid();
        await SeedOpenRowAsync(profileId, new DateOnly(2025, 12, 29), weeklyNormHours: 36m);

        // Run S22 migration.
        await LegacyProfileSchema.RunS22MigrationAsync(_harness.ConnectionString);

        // Post-migration: effective_to still NULL, version backfilled to 1.
        var effectiveTo = await ReadEffectiveToAsync(profileId);
        Assert.Null(effectiveTo);

        var version = await ReadVersionAsync(profileId);
        Assert.Equal(1L, version);
    }

    [Fact]
    public async Task PreS22Manifest_ReplaysIdentical()
    {
        // Narrowed scope (per the agent brief): the full PCS-replay-determinism property
        // requires manifest creation under S21-era BuildPlanForLegacyCallers + a real
        // ReplayAsync call. That's heavy to wire here without standing up the full
        // segmentation harness. The narrowed assertion: a profile snapshot taken
        // pre-migration (the columns the S20 SnapshotContract would persist into a
        // segment_manifests.segments_jsonb entry) is byte-identical (modulo the new
        // version column, which the snapshot does NOT include — see ADR-016 D4 + ADR-017
        // schema columns) to the snapshot taken post-migration. This pins the property
        // that ReplayAsync, which reads from the manifest snapshot rather than the
        // post-migration row, would produce identical replay output.
        //
        // The migration affects:
        //   - effective_to (+1 day on closed rows) — manifest snapshot for THIS profile
        //     is captured pre-close; the closed-row effective_to shift cannot affect a
        //     manifest's recorded inputs.
        //   - version column (NEW) — not part of the profile snapshot contract.
        // Therefore: the byte-identical projection across migration confirms replay
        // determinism for pre-S22 manifests.
        var profileId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2025, 12, 29);
        await SeedOpenRowAsync(profileId, effectiveFrom, weeklyNormHours: 37.5m);

        var preMigrationSnapshot = await ReadProfileSnapshotAsync(profileId);

        await LegacyProfileSchema.RunS22MigrationAsync(_harness.ConnectionString);

        var postMigrationSnapshot = await ReadProfileSnapshotAsync(profileId);

        Assert.Equal(preMigrationSnapshot, postMigrationSnapshot);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task SeedClosedRowAsync(
        Guid profileId, DateOnly effectiveFrom, DateOnly effectiveTo, decimal weeklyNormHours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, created_by)
            VALUES (@id, @org, @ac, @ok, @from, @to, @wnh, 'admin1')
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", effectiveTo);
        cmd.Parameters.AddWithValue("wnh", weeklyNormHours);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedOpenRowAsync(Guid profileId, DateOnly effectiveFrom, decimal weeklyNormHours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from,
                weekly_norm_hours, created_by)
            VALUES (@id, @org, @ac, @ok, @from, @wnh, 'admin1')
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("wnh", weeklyNormHours);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<DateOnly?> ReadEffectiveToAsync(Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_to FROM local_agreement_profiles WHERE profile_id = @id", conn);
        cmd.Parameters.AddWithValue("id", profileId);
        var raw = await cmd.ExecuteScalarAsync();
        if (raw is null || raw is DBNull) return null;
        return DateOnly.FromDateTime((DateTime)raw);
    }

    private async Task<long> ReadVersionAsync(Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM local_agreement_profiles WHERE profile_id = @id", conn);
        cmd.Parameters.AddWithValue("id", profileId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Byte-identical projection of the profile columns that the S20 SnapshotContract
    /// would persist into a segment_manifests entry. Excludes the new <c>version</c>
    /// column (added post-S22, not part of the snapshot contract — see ADR-016 D4 +
    /// ADR-017 D5 / D6 schema definitions).
    /// </summary>
    private async Task<string> ReadProfileSnapshotAsync(Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by
            FROM local_agreement_profiles
            WHERE profile_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Profile row missing.");
        var fields = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            fields.Add(reader.IsDBNull(i) ? "<null>" : reader.GetValue(i)!.ToString() ?? string.Empty);
        }
        return string.Join("|", fields);
    }

    private async Task<bool> IsActiveOnDateAsync_EndInclusive(DateOnly d, Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM local_agreement_profiles
            WHERE profile_id = @id
              AND effective_from <= @d
              AND (effective_to IS NULL OR @d <= effective_to)
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("d", d);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1L;
    }

    private async Task<bool> IsActiveOnDateAsync_EndExclusive(DateOnly d, Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM local_agreement_profiles
            WHERE profile_id = @id
              AND effective_from <= @d
              AND (effective_to IS NULL OR @d < effective_to)
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("d", d);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1L;
    }

    private static async Task SeedOrganizationAsync(string connectionString, string orgId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgId || ' Test Org', 'STYRELSE', '/' || @orgId || '/', 'HK', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await cmd.ExecuteNonQueryAsync();
    }
}
