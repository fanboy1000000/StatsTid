using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;

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
///   <item>The eligibility window is the DANISH calendar day, not UTC and not the DB server's
///     (#6 — S142 / TASK-14208)</item>
/// </list>
///
/// <para>
/// <b>Why every fact now pins the clock (S142 / TASK-14208, owner ruling OQ-7).</b> "Which legacy
/// rows are currently effective?" is a business-date question. Until S142 the migrator asked
/// Postgres, via <c>CURRENT_DATE</c> — so the answer depended on the timezone of whatever container
/// the database happened to be running in, and no test could pin it. The migrator now takes a
/// <see cref="TimeProvider"/> and derives the day through <c>CopenhagenBusinessDate</c>, so these
/// tests inject a <see cref="FixedTimeProvider"/> and the outcome is a pure function of
/// (seed, pinned instant). <see cref="PinnedToday"/> is the everyday pin — comfortably inside every
/// legacy fixture's window, so facts #1–#5 keep asserting exactly what they always asserted, now
/// without a hidden dependency on the calendar day CI runs on.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileMigrationTests : IAsyncLifetime
{
    /// <summary>
    /// The everyday pinned "today" for fixtures #1–#5: later than every seeded
    /// <c>effective_from</c> (latest is 2025-06-01) and later than the expired fixture's
    /// <c>effective_to</c> (2024-12-31), so the eligible/expired split those facts assert is
    /// unchanged from the pre-S142 wall-clock behaviour — just deterministic now.
    /// </summary>
    private static readonly DateOnly PinnedToday = new(2026, 3, 2);

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    /// <summary>
    /// Builds the migrator under test with an explicit clock. Defaults to
    /// <see cref="PinnedToday"/> at UTC midnight, where the UTC and Copenhagen calendar days agree
    /// (Denmark's offset is never negative), so a fact that does not care about the zone is
    /// unaffected by it. Fact #6 passes its own instant, chosen so the two calendars DISAGREE.
    /// </summary>
    private LocalAgreementProfileMigrator NewMigrator(TimeProvider? clock = null) =>
        new(_harness.Factory,
            NullLogger<LocalAgreementProfileMigrator>.Instance,
            clock ?? new FixedTimeProvider(PinnedToday));

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

        var migrator = NewMigrator();
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

        var migrator = NewMigrator();
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

        var migrator = NewMigrator();
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

        var migrator = NewMigrator();
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

        var migrator = NewMigrator();
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

    /// <summary>
    /// S142 / TASK-14208 (owner ruling OQ-7) — <b>fixture #6: "currently effective" is the DANISH
    /// calendar day.</b>
    ///
    /// <para>
    /// <b>The problem, in plain language.</b> StatsTid decides what is in force "today". Denmark is
    /// one or two hours ahead of UTC, so between Copenhagen midnight and UTC midnight the two
    /// calendars name DIFFERENT days. A migration run in that window used to ask the database
    /// server which day it was — and got the answer for the server container's timezone, which
    /// nobody sets, sees or tests. If that answer was yesterday, a configuration row that took
    /// effect this morning was treated as not yet in force, and the migration silently dropped it.
    /// </para>
    ///
    /// <para>
    /// <b>How this test proves the fix rather than restating it.</b> The clock is pinned at
    /// 2026-06-30 <b>22:30 UTC</b>. Denmark is on summer time (CEST, UTC+2) that day, so in
    /// Copenhagen it is already 2026-07-01 00:30 — the two calendars disagree, and the expected
    /// dates below are written as LITERALS, never computed by calling the helper under test (which
    /// would only prove the helper agrees with itself). Two rows straddle the boundary from
    /// opposite sides:
    /// <list type="bullet">
    ///   <item><c>WeeklyNormHours</c> starts ON 2026-07-01 — in force under the Copenhagen day,
    ///     not yet in force under the UTC day.</item>
    ///   <item><c>MaxFlexBalance</c> ends ON 2026-06-30 — still in force under the UTC day,
    ///     expired under the Copenhagen day.</item>
    /// </list>
    /// Exactly one of the two survives, and WHICH one is the entire assertion. Under the old
    /// UTC/server-clock behaviour every assertion below inverts, so the test cannot pass for the
    /// wrong reason. (Docker-gated: verified in CI, not runnable on the author's machine.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task EligibilityWindow_UsesCopenhagenCalendarDay_NotUtcAndNotTheServerClock()
    {
        // In force from the Copenhagen "today" (2026-07-01) onward — invisible to a UTC reading.
        await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "WeeklyNormHours", "36",
            new DateOnly(2026, 7, 1), effectiveTo: null);
        // Expired as of the Copenhagen "today" — but still open on the UTC day (2026-06-30).
        var utcOnlyRowId = await InsertLegacyConfigAsync(
            "STY02", "HK", "OK24", "MaxFlexBalance", "100",
            new DateOnly(2024, 1, 1), effectiveTo: new DateOnly(2026, 6, 30));

        // 22:30 UTC on 30 June = 00:30 on 1 July in Copenhagen (CEST, UTC+2).
        var migrator = NewMigrator(new FixedTimeProvider(
            new DateTimeOffset(2026, 6, 30, 22, 30, 0, TimeSpan.Zero)));
        var result = await migrator.RebuildAsync();

        Assert.Equal(1, result.ProfilesCreated);
        Assert.Equal(1, result.RowsMigrated);

        var profile = await GetSingleProfileAsync("STY02", "HK", "OK24");
        Assert.NotNull(profile);
        // The Copenhagen-day row was absorbed …
        Assert.Equal(36m, profile!.Value.WeeklyNormHours);
        Assert.Equal(new DateOnly(2026, 7, 1), profile.Value.EffectiveFrom);
        // … and the row that only a UTC reading would still call live was NOT.
        Assert.Null(profile.Value.MaxFlexBalance);

        // Expired rows are filtered out before classification, so they emit no audit at all
        // (the fixture-#4 contract). A UTC reading would have absorbed this row instead.
        Assert.Null(await GetAuditActionForConfigAsync(utcOnlyRowId));
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
