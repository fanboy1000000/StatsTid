using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S30 / TASK-3010 D-tests — ADR-020 D2 3-case routing + ADR-021 D3 seed idempotency on
/// <see cref="EntitlementConfigRepository"/>. Mirrors S29's
/// <see cref="WageTypeMappingSupersessionTests"/> + <see cref="WageTypeMappingIdempotencyTests"/>
/// shape verbatim (direct-repo orchestration against the full <c>init.sql</c> schema).
///
/// <list type="bullet">
///   <item><b>Case A</b> — no predecessor → fresh INSERT at version 1.</item>
///   <item><b>Case B</b> — predecessor with <c>effective_from &lt; today</c> → predecessor
///   closed at <c>effective_to=today</c>, new open row inserted at version 1.</item>
///   <item><b>Case C</b> — predecessor with <c>effective_from = today</c> → UPDATE-in-place,
///   version bumped (no new row).</item>
///   <item><b>Seed-idempotency #1..#4</b> — fresh bootstrap × 1; fresh bootstrap × 2;
///   post-admin-edit re-seed; post-admin-soft-delete re-seed. ADR-020 D3 pattern using
///   <c>ON CONFLICT (entitlement_type, agreement_code, ok_version, effective_from) DO
///   NOTHING</c> against the <c>idx_ec_natural_key_history</c> conflict target.</item>
/// </list>
///
/// <para>
/// <b>Direct-orchestration</b>: repo-surface contracts (per the S29 precedent at
/// <see cref="WageTypeMappingSupersessionTests"/>). HTTP-level coverage of the admin
/// CRUD wire shape lives in <see cref="EntitlementConfigEndpointTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementConfigSupersessionTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private EntitlementConfigRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full init.sql brings up the post-S30 entitlement_configs shape (effective_from
        // + effective_to + partial-unique-index + history-unique-index) + 30 seed rows
        // at effective_from='0001-01-01'. Idempotency tests use the same init.sql as the
        // "second run" payload.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _repo = new EntitlementConfigRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #1 — Case A: no predecessor → fresh INSERT.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SupersedeAndCreate_CaseA_NoPredecessor_FreshInsert()
    {
        // Pick a natural key that doesn't exist in the seed: a synthetic ok_version.
        const string EntitlementType = "VACATION";
        const string AgreementCode = "AC";
        var fakeOk = "OK_CASEA_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        SaveEntitlementConfigResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Acquire lock returns null (no row exists for this natural key).
            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, fakeOk);
            Assert.Null(predecessor);

            var newConfig = NewConfig(EntitlementType, AgreementCode, fakeOk, today, annualQuota: 12m);
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor: null, expectedCurrentVersion: null);
            await tx.CommitAsync();
        }

        Assert.True(result.IsCreated);
        Assert.Equal(1L, result.Version);
        Assert.Null(result.SupersededConfigId);
        Assert.Equal(today, result.Config.EffectiveFrom);
        Assert.Null(result.Config.EffectiveTo);
        Assert.Equal(12m, result.Config.AnnualQuota);

        // Row count: 1 for this natural key (no history).
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(EntitlementType, AgreementCode, fakeOk));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #2 — Case B: cross-day predecessor → close predecessor + INSERT new.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SupersedeAndCreate_CaseB_CrossDayPredecessor_ClosesAndInserts()
    {
        // Pick a seeded natural key (predecessor effective_from='0001-01-01' < today).
        // CARE_DAY/PROSA/OK24: annual_quota=2, reset_month=1.
        const string EntitlementType = "CARE_DAY";
        const string AgreementCode = "PROSA";
        const string OkVersion = "OK24";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var predecessorBefore = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(predecessorBefore);
        Assert.Equal(new DateOnly(1, 1, 1), predecessorBefore!.EffectiveFrom);
        Assert.Null(predecessorBefore.EffectiveTo);

        SaveEntitlementConfigResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, OkVersion);
            Assert.NotNull(predecessor);

            // Build new config matching predecessor's frozen fields (resetMonth + accrualModel
            // are immutable per natural key in the admin endpoint surface; here we mirror the
            // contract at the repo level by simply re-using the predecessor's values).
            var newConfig = NewConfigMatchingPredecessor(predecessor!, today, annualQuota: 3m);
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor, expectedCurrentVersion: predecessor!.Version);
            await tx.CommitAsync();
        }

        // Case B: predecessor closed, new row INSERTed, IsCreated=false, version=1 on new row,
        // SupersededConfigId points at predecessor.
        Assert.False(result.IsCreated);
        Assert.Equal(1L, result.Version);
        Assert.NotNull(result.SupersededConfigId);
        Assert.Equal(predecessorBefore.ConfigId, result.SupersededConfigId);
        Assert.Equal(today, result.Config.EffectiveFrom);
        Assert.Null(result.Config.EffectiveTo);
        Assert.Equal(3m, result.Config.AnnualQuota);
        // S73 / TASK-7301 (R2 version-survival, repo level): the CARE_DAY successor row carries
        // the predecessor's full_day_only = TRUE through the Case-B INSERT.
        Assert.True(result.Config.FullDayOnly);

        // Row count for this natural key: 2 (closed predecessor + new open row).
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion));

        // Predecessor closed at effective_to=today; original quota preserved.
        var predecessorRow = await ReadRowByConfigIdAsync(predecessorBefore.ConfigId);
        Assert.NotNull(predecessorRow);
        Assert.Equal(today, predecessorRow!.EffectiveTo);
        Assert.Equal(2m, predecessorRow.AnnualQuota);

        // GetByTypeAtAsync resolves correctly:
        //   yesterday → predecessor wins (quota=2)
        //   today     → new row wins (quota=3)
        var yesterday = today.AddDays(-1);
        var atYesterday = await _repo.GetByTypeAtAsync(EntitlementType, AgreementCode, OkVersion, yesterday);
        Assert.NotNull(atYesterday);
        Assert.Equal(predecessorBefore.ConfigId, atYesterday!.ConfigId);
        Assert.Equal(2m, atYesterday.AnnualQuota);

        var atToday = await _repo.GetByTypeAtAsync(EntitlementType, AgreementCode, OkVersion, today);
        Assert.NotNull(atToday);
        Assert.Equal(result.Config.ConfigId, atToday!.ConfigId);
        Assert.Equal(3m, atToday.AnnualQuota);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #3 — Case C: same-day predecessor → UPDATE-in-place, version bump.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SupersedeAndCreate_CaseC_SameDayPredecessor_UpdatesInPlace()
    {
        // Seed a fresh row at effective_from=today, version=1 (via Case A path),
        // then issue a second SupersedeAndCreate at the same date — Case C fires.
        const string EntitlementType = "VACATION";
        const string AgreementCode = "AC";
        var fakeOk = "OK_CASEC_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Step 1: Case A insert at effective_from=today.
        Guid initialConfigId;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var initial = NewConfig(EntitlementType, AgreementCode, fakeOk, today, annualQuota: 25m);
            var r1 = await _repo.SupersedeAndCreateAsync(
                conn, tx, initial, predecessor: null, expectedCurrentVersion: null);
            Assert.True(r1.IsCreated);
            Assert.Equal(1L, r1.Version);
            initialConfigId = r1.Config.ConfigId;
            await tx.CommitAsync();
        }

        // Step 2: same-day edit — Case C.
        SaveEntitlementConfigResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, fakeOk);
            Assert.NotNull(predecessor);
            Assert.Equal(today, predecessor!.EffectiveFrom);
            Assert.Equal(1L, predecessor.Version);

            var newConfig = NewConfigMatchingPredecessor(predecessor!, today, annualQuota: 26m);
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor, expectedCurrentVersion: 1L);
            await tx.CommitAsync();
        }

        // Case C: IsCreated=false, version bumped to 2, SupersededConfigId=null,
        // SAME config_id as the initial row (UPDATE-in-place, not a new row).
        Assert.False(result.IsCreated);
        Assert.Equal(2L, result.Version);
        Assert.Null(result.SupersededConfigId);
        Assert.Equal(initialConfigId, result.Config.ConfigId);
        Assert.Equal(today, result.Config.EffectiveFrom);
        Assert.Null(result.Config.EffectiveTo);
        Assert.Equal(26m, result.Config.AnnualQuota);

        // Row count: 1 (still just the one row, with version=2 + updated quota).
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(EntitlementType, AgreementCode, fakeOk));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #4 — Seed idempotency #1: re-running init.sql produces 50 rows exactly.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SeedIdempotency_ReApplyInitSql_ProducesExactlyFiftyRowsAcrossReRuns()
    {
        // S37/TASK-3701 (3eea4f5): AC_RESEARCH + AC_TEACHING variants added (+20 rows).
        // After the first ApplyFullSchemaAsync in InitializeAsync, count must be 50
        // (5 entitlement_types × 5 agreement_codes × 2 ok_versions).
        var rowsAfterFirst = await CountAllRowsAsync();
        Assert.Equal(50L, rowsAfterFirst);

        var configIdsAfterFirst = await ReadAllConfigIdsAsync();
        Assert.Equal(50, configIdsAfterFirst.Count);

        // Re-apply init.sql against the same container. ON CONFLICT
        // (entitlement_type, agreement_code, ok_version, effective_from) DO NOTHING
        // short-circuits the seed INSERTs; no new rows; no row count change.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        var rowsAfterSecond = await CountAllRowsAsync();
        Assert.Equal(50L, rowsAfterSecond);

        var configIdsAfterSecond = await ReadAllConfigIdsAsync();
        Assert.Equal(configIdsAfterFirst.OrderBy(g => g).ToArray(),
                     configIdsAfterSecond.OrderBy(g => g).ToArray());

        // Belt-and-braces: no duplicate (natural_key, effective_from) tuple.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM (
                SELECT entitlement_type, agreement_code, ok_version, effective_from, COUNT(*) c
                FROM entitlement_configs
                GROUP BY entitlement_type, agreement_code, ok_version, effective_from
                HAVING COUNT(*) > 1
            ) duplicates
            """, conn);
        var dupCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(0L, dupCount);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #5 — Seed idempotency #2: post-admin-edit re-seed does NOT resurrect predecessor.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SeedIdempotency_AfterCrossDayEdit_ReApplyDoesNotResurrectPredecessor()
    {
        // Pick a seed: SPECIAL_HOLIDAY / HK / OK24, annual_quota=5, effective_from='0001-01-01'.
        const string EntitlementType = "SPECIAL_HOLIDAY";
        const string AgreementCode = "HK";
        const string OkVersion = "OK24";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var before = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Single(before);
        Assert.Equal(new DateOnly(1, 1, 1), before[0].EffectiveFrom);
        Assert.Null(before[0].EffectiveTo);
        Assert.Equal(5m, before[0].AnnualQuota);

        // Cross-day admin edit (Case B): predecessor becomes ('0001-01-01', today),
        // new row at (today, NULL) with annual_quota=7.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, OkVersion);
            Assert.NotNull(predecessor);

            var newConfig = NewConfigMatchingPredecessor(predecessor!, today, annualQuota: 7m);
            await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor, expectedCurrentVersion: predecessor!.Version);
            await tx.CommitAsync();
        }

        var afterEdit = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Equal(2, afterEdit.Count);
        Assert.Single(afterEdit.Where(r => r.EffectiveFrom == new DateOnly(1, 1, 1) && r.EffectiveTo == today));
        Assert.Single(afterEdit.Where(r => r.EffectiveFrom == today && r.EffectiveTo is null));

        // Re-apply init.sql. The seed INSERT at effective_from='0001-01-01' should hit
        // ON CONFLICT against the closed predecessor row (same conflict target) and
        // DO NOTHING. The closed predecessor's data (annual_quota=5) is preserved
        // unchanged; the live row at effective_from=today (annual_quota=7) is untouched
        // (no seed targets today's date).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        var afterReseed = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Equal(2, afterReseed.Count);

        var predecessor2 = afterReseed.Single(r => r.EffectiveFrom == new DateOnly(1, 1, 1));
        Assert.Equal(today, predecessor2.EffectiveTo);
        Assert.Equal(5m, predecessor2.AnnualQuota); // original preserved

        var live = afterReseed.Single(r => r.EffectiveFrom == today);
        Assert.Null(live.EffectiveTo);
        Assert.Equal(7m, live.AnnualQuota); // post-edit value preserved
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #6 — Seed idempotency #3: post-admin-soft-delete re-seed.
    //
    // Critical question per the test spec: does the seed re-resurrect a soft-deleted
    // row? Under the ADR-021 D3 conflict target on (natural_key, effective_from), the
    // soft-deleted row's effective_from is '0001-01-01' — same as the seed's
    // effective_from — so ON CONFLICT DO NOTHING and the row stays deleted. If this
    // test FAILS, the seed's idempotency strategy doesn't honor admin-soft-delete and
    // we have a real Q3-class bug that needs ADR-021 documented limitation OR an
    // Orchestrator-direct init.sql fix.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SeedIdempotency_AfterSoftDelete_ReApplyDoesNotResurrectSoftDeletedRow()
    {
        // Pick a seed: CHILD_SICK / AC / OK26, annual_quota=1, effective_from='0001-01-01'.
        const string EntitlementType = "CHILD_SICK";
        const string AgreementCode = "AC";
        const string OkVersion = "OK26";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var before = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Single(before);
        Assert.Null(before[0].EffectiveTo);

        // Admin soft-delete: closes the live row at effective_to=today; effective_from
        // stays at '0001-01-01'; no new row inserted.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, OkVersion);
            Assert.NotNull(predecessor);
            await _repo.SoftDeleteAsync(conn, tx, predecessor!, today);
            await tx.CommitAsync();
        }

        var afterDelete = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Single(afterDelete);
        Assert.Equal(new DateOnly(1, 1, 1), afterDelete[0].EffectiveFrom);
        Assert.Equal(today, afterDelete[0].EffectiveTo);

        // Live (open) row for the natural key: there isn't one anymore.
        Assert.Null(await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion));

        // Re-apply init.sql. The seed INSERT at effective_from='0001-01-01' hits ON CONFLICT
        // against the soft-deleted row (same conflict target on (natural_key, effective_from))
        // and DO NOTHING. No new row inserted; the soft-deleted row stays closed; no live row.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        var afterReseed = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Single(afterReseed);
        Assert.Equal(today, afterReseed[0].EffectiveTo); // STILL deleted

        // No live row resurrected.
        Assert.Null(await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #7 — Seed idempotency #4: post-admin-edit, the new live row at
    // effective_from=today survives a second init.sql re-run (no seed targets today).
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SeedIdempotency_AfterCrossDayEdit_LiveRowAtTodaySurvivesReSeed()
    {
        // Pick a seed: SENIOR_DAY / PROSA / OK24, annual_quota=0, effective_from='0001-01-01'.
        const string EntitlementType = "SENIOR_DAY";
        const string AgreementCode = "PROSA";
        const string OkVersion = "OK24";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Cross-day edit.
        Guid newConfigId;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var predecessor = await _repo.AcquireLockAsync(conn, tx, EntitlementType, AgreementCode, OkVersion);
            Assert.NotNull(predecessor);

            var newConfig = NewConfigMatchingPredecessor(predecessor!, today, annualQuota: 3m);
            var r = await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor, expectedCurrentVersion: predecessor!.Version);
            newConfigId = r.Config.ConfigId;
            await tx.CommitAsync();
        }

        var live = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(live);
        Assert.Equal(3m, live!.AnnualQuota);
        Assert.Equal(today, live.EffectiveFrom);

        // Re-apply init.sql.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        // Live row at effective_from=today is untouched (the seed inserts at
        // effective_from='0001-01-01' so no overlap; the closed predecessor is also
        // preserved per ON CONFLICT).
        var liveAfter = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(liveAfter);
        Assert.Equal(newConfigId, liveAfter!.ConfigId);
        Assert.Equal(3m, liveAfter.AnnualQuota);
        Assert.Equal(today, liveAfter.EffectiveFrom);

        // Row count for this natural key: still 2.
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fresh <see cref="EntitlementConfig"/> for use with SupersedeAndCreateAsync
    /// on the no-predecessor Case A path. Defaults reset_month=9, accrual_model='IMMEDIATE'.
    /// </summary>
    private static EntitlementConfig NewConfig(
        string entitlementType, string agreementCode, string okVersion, DateOnly effectiveFrom,
        decimal annualQuota) => new()
    {
        ConfigId = Guid.NewGuid(),
        EntitlementType = entitlementType,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        AnnualQuota = annualQuota,
        AccrualModel = "IMMEDIATE",
        ResetMonth = 9,
        CarryoverMax = 0m,
        ProRateByPartTime = true,
        IsPerEpisode = false,
        MinAge = null,
        Description = null,
        EffectiveFrom = effectiveFrom,
    };

    /// <summary>
    /// Builds a fresh <see cref="EntitlementConfig"/> whose natural-key + immutable fields
    /// match an existing predecessor row (resetMonth + accrualModel + minAge are agreement-
    /// defining and frozen at the admin endpoint surface per ADR-021 Q1 sub-fork (i); we
    /// mirror that contract at the repo level by reusing the predecessor's values). Only
    /// the mutable fields (annualQuota + effectiveFrom) vary.
    /// </summary>
    private static EntitlementConfig NewConfigMatchingPredecessor(
        EntitlementConfig predecessor, DateOnly effectiveFrom, decimal annualQuota) => new()
    {
        ConfigId = Guid.NewGuid(),
        EntitlementType = predecessor.EntitlementType,
        AgreementCode = predecessor.AgreementCode,
        OkVersion = predecessor.OkVersion,
        AnnualQuota = annualQuota,
        AccrualModel = predecessor.AccrualModel,
        ResetMonth = predecessor.ResetMonth,
        CarryoverMax = predecessor.CarryoverMax,
        ProRateByPartTime = predecessor.ProRateByPartTime,
        IsPerEpisode = predecessor.IsPerEpisode,
        MinAge = predecessor.MinAge,
        Description = predecessor.Description,
        // S73 / TASK-7301 (SPRINT-73 R2/R7 — legitimate behavior change, refinement
        // REFINEMENT-s73-ui-testing-fix-bundle): the full-day-only flag threads the full config
        // surface; a successor must carry the predecessor's flag or the new DB CHECK
        // entitlement_configs_full_day_only_types rejects the CARE_DAY/SENIOR_DAY INSERT —
        // which is ALSO the repo-level version-survival contract this helper now mirrors.
        FullDayOnly = predecessor.FullDayOnly,
        EffectiveFrom = effectiveFrom,
    };

    private async Task<long> CountRowsForNaturalKeyAsync(
        string entitlementType, string agreementCode, string okVersion)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
            """, conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountAllRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM entitlement_configs", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<Guid>> ReadAllConfigIdsAsync()
    {
        var ids = new List<Guid>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT config_id FROM entitlement_configs ORDER BY config_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<List<EntitlementConfig>> ReadAllForNaturalKeyAsync(
        string entitlementType, string agreementCode, string okVersion)
    {
        var rows = new List<EntitlementConfig>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
            ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(ReadConfig(reader));
        return rows;
    }

    private async Task<EntitlementConfig?> ReadRowByConfigIdAsync(Guid configId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs WHERE config_id = @configId", conn);
        cmd.Parameters.AddWithValue("configId", configId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadConfig(reader) : null;
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
        MinAge = reader.IsDBNull(reader.GetOrdinal("min_age"))
            ? null
            : reader.GetInt32(reader.GetOrdinal("min_age")),
        Description = reader.IsDBNull(reader.GetOrdinal("description"))
            ? null
            : reader.GetString(reader.GetOrdinal("description")),
        // S73 / TASK-7301 (R2/R7 — legitimate behavior change, refinement
        // REFINEMENT-s73-ui-testing-fix-bundle): the test-local reader mirrors the repo's.
        FullDayOnly = reader.GetBoolean(reader.GetOrdinal("full_day_only")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
    };
}
