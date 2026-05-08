using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S25 / TASK-2508 cross-resource audit version-transition tests pinning ADR-019 D8 — one
/// test per S25-propagated resource verifying the canonical (version_before, version_after)
/// population convention across the CREATE / UPDATE / state-change lifecycle:
///
/// <list type="bullet">
///   <item>CREATE  → (NULL, NULL) — populated via v2 audit primitive
///     (S24 atomic-outbox-preserved overload). First-create has no prior version-transition
///     pair to record, and v2 audit signature has no version-pair parameters.</item>
///   <item>UPDATE  → (prior, new) — populated via v3 audit overload
///     (S25 / TASK-2503 / 2504 / 2505 versionBefore/versionAfter overload).</item>
///   <item>State-change (publish/archive/activate/deactivate) → (prior, new) — same v3
///     overload as UPDATE.</item>
///   <item>DELETE → (version, version) — v3 audit overload; records the version at point
///     of deletion since the row is gone (per D8 explicit clause). Verified in the
///     <see cref="WageTypeMapping_DeleteRecordsVersionAtPointOfDeletion"/> test.</item>
/// </list>
///
/// <para>
/// Replay determinism per ADR-016 D10 is preserved: replay reads the entity's <c>version</c>
/// directly from the row, NOT from these audit columns. Old audit rows (pre-S25 schema
/// migration) leave both columns NULL — non-blocking for any read or replay path.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditVersionTransitionTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AgreementConfigRepository _agreementConfigRepo = null!;
    private PositionOverrideRepository _positionOverrideRepo = null!;
    private WageTypeMappingRepository _wageTypeMappingRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _agreementConfigRepo = new AgreementConfigRepository(_harness.Factory);
        _positionOverrideRepo = new PositionOverrideRepository(_harness.Factory);
        _wageTypeMappingRepo = new WageTypeMappingRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task AgreementConfig_CreateThenUpdateThenArchive_AuditTransitions()
    {
        // CREATE  → (NULL, NULL); UPDATE → (1, 2); ARCHIVE → (2, 3) per ADR-019 D8.
        var initial = NewAgreementConfig();
        var configId = await _agreementConfigRepo.CreateAsync(initial);

        // CREATE audit (v2 primitive).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _agreementConfigRepo.AppendAuditAsync(
                conn, tx, configId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");
            await tx.CommitAsync();
        }

        // UPDATE — v3 mutate + v3 audit (1, 2).
        long updateVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var saveResult = await _agreementConfigRepo.UpdateDraftAsync(
                conn, tx, configId, expectedVersion: 1, NewAgreementConfig(weeklyNorm: 38m));
            await _agreementConfigRepo.AppendAuditAsync(
                conn, tx, configId, "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: 1, versionAfter: saveResult.Version);
            await tx.CommitAsync();
            updateVersion = saveResult.Version;
        }
        Assert.Equal(2L, updateVersion);

        // ARCHIVE — v3 mutate + v3 audit (2, 3).
        long archiveVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var saveResult = await _agreementConfigRepo.ArchiveAsync(
                conn, tx, configId, expectedVersion: 2, actorId: "tester");
            await _agreementConfigRepo.AppendAuditAsync(
                conn, tx, configId, "ARCHIVED", "{}", "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: 2, versionAfter: saveResult.Version);
            await tx.CommitAsync();
            archiveVersion = saveResult.Version;
        }
        Assert.Equal(3L, archiveVersion);

        // Verify the three audit rows in insertion order.
        var rows = await ReadAuditRowsAsync(
            "agreement_config_audit", "config_id", configId);
        Assert.Equal(3, rows.Count);
        AssertAuditRow(rows[0], action: "CREATED", before: null, after: null);
        AssertAuditRow(rows[1], action: "UPDATED", before: 1L, after: 2L);
        AssertAuditRow(rows[2], action: "ARCHIVED", before: 2L, after: 3L);
    }

    [Fact]
    public async Task PositionOverride_CreateThenUpdateThenDeactivate_AuditTransitions()
    {
        // CREATE → (NULL, NULL); UPDATE → (1, 2); DEACTIVATE → (2, 3) per ADR-019 D8.
        // PositionOverride uses Update + state-change (Activate/Deactivate) as its three
        // mutating endpoints — DEACTIVATE here exercises the state-change v3 audit path.
        var initial = NewPositionOverride();
        var overrideId = await _positionOverrideRepo.CreateAsync(initial);

        // CREATE audit (v2 primitive).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _positionOverrideRepo.AppendAuditAsync(
                conn, tx, overrideId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");
            await tx.CommitAsync();
        }

        // UPDATE — v3 mutate + v3 audit (1, 2).
        long updateVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var saveResult = await _positionOverrideRepo.UpdateAsync(
                conn, tx, overrideId, expectedVersion: 1, NewPositionOverride(maxFlex: 250m));
            await _positionOverrideRepo.AppendAuditAsync(
                conn, tx, overrideId, "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: 1, versionAfter: saveResult.Version);
            await tx.CommitAsync();
            updateVersion = saveResult.Version;
        }
        Assert.Equal(2L, updateVersion);

        // DEACTIVATE — v3 mutate + v3 audit (2, 3).
        long deactivateVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var saveResult = await _positionOverrideRepo.DeactivateAsync(
                conn, tx, overrideId, expectedVersion: 2);
            await _positionOverrideRepo.AppendAuditAsync(
                conn, tx, overrideId, "DEACTIVATED", "{}", null, "tester", "GLOBAL_ADMIN",
                versionBefore: 2, versionAfter: saveResult.Version);
            await tx.CommitAsync();
            deactivateVersion = saveResult.Version;
        }
        Assert.Equal(3L, deactivateVersion);

        // Verify the three audit rows in insertion order.
        var rows = await ReadAuditRowsAsync(
            "position_override_config_audit", "override_id", overrideId);
        Assert.Equal(3, rows.Count);
        AssertAuditRow(rows[0], action: "CREATED", before: null, after: null);
        AssertAuditRow(rows[1], action: "UPDATED", before: 1L, after: 2L);
        AssertAuditRow(rows[2], action: "DEACTIVATED", before: 2L, after: 3L);
    }

    [Fact]
    public async Task WageTypeMapping_DeleteRecordsVersionAtPointOfDeletion()
    {
        // ADR-019 D8 explicit clause: DELETE-event audit row records (version, version) —
        // the version at point of deletion — rather than NULL. Rationale: the row is gone
        // post-delete but the audit row is the only post-mortem trace; recording the
        // version at point of deletion supports replay determinism + audit completeness.
        // CREATE  → (NULL, NULL)
        // UPDATE  → (1, 2)
        // DELETE  → (2, 2)
        var seed = NewWageTypeMapping();
        await _wageTypeMappingRepo.CreateAsync(seed);

        // CREATE audit (v2 primitive).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _wageTypeMappingRepo.AppendAuditAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");
            await tx.CommitAsync();
        }

        // UPDATE — v3 mutate + v3 audit (1, 2).
        long updateVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var saveResult = await _wageTypeMappingRepo.UpdateAsync(
                conn, tx,
                NewWageTypeMapping(timeType: seed.TimeType, wageType: "SLS_8888"),
                expectedVersion: 1);
            await _wageTypeMappingRepo.AppendAuditAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: 1, versionAfter: saveResult.Version);
            await tx.CommitAsync();
            updateVersion = saveResult.Version;
        }
        Assert.Equal(2L, updateVersion);

        // DELETE — v3 mutate + v3 audit (2, 2). The version-at-point-of-deletion convention
        // is captured by the endpoint passing `versionBefore=2, versionAfter=2` (NOT a
        // post-delete version since there is no post-delete entity).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var deleted = await _wageTypeMappingRepo.DeleteAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                expectedVersion: 2);
            Assert.True(deleted);
            await _wageTypeMappingRepo.AppendAuditAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                "DELETED", "{}", null, "tester", "GLOBAL_ADMIN",
                versionBefore: 2, versionAfter: 2);
            await tx.CommitAsync();
        }

        // Verify the three audit rows in insertion order.
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM wage_type_mapping_audit
            WHERE time_type = @timeType AND ok_version = @okVersion
              AND agreement_code = @agreementCode AND position = @position
            ORDER BY audit_id ASC
            """, verifyConn);
        cmd.Parameters.AddWithValue("timeType", seed.TimeType);
        cmd.Parameters.AddWithValue("okVersion", seed.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", seed.AgreementCode);
        cmd.Parameters.AddWithValue("position", seed.Position);
        await using var reader = await cmd.ExecuteReaderAsync();

        // CREATE row.
        Assert.True(await reader.ReadAsync());
        Assert.Equal("CREATED", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));

        // UPDATE row.
        Assert.True(await reader.ReadAsync());
        Assert.Equal("UPDATED", reader.GetString(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));

        // DELETE row — the load-bearing assertion: version_before = version_after = 2 (NOT
        // NULL), recording the version at point of deletion per ADR-019 D8.
        Assert.True(await reader.ReadAsync());
        Assert.Equal("DELETED", reader.GetString(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));

        Assert.False(await reader.ReadAsync(), "Expected exactly three audit rows.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private record AuditRow(string Action, long? VersionBefore, long? VersionAfter);

    private async Task<List<AuditRow>> ReadAuditRowsAsync(
        string auditTable, string idColumn, Guid idValue)
    {
        var rows = new List<AuditRow>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Table + column names cannot be parameterised in Npgsql; this method is internal
        // to the test (no untrusted input) so direct interpolation is safe.
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT action, version_before, version_after
            FROM {auditTable}
            WHERE {idColumn} = @id
            ORDER BY audit_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("id", idValue);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var action = reader.GetString(0);
            long? before = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            long? after = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            rows.Add(new AuditRow(action, before, after));
        }
        return rows;
    }

    private static void AssertAuditRow(AuditRow row, string action, long? before, long? after)
    {
        Assert.Equal(action, row.Action);
        Assert.Equal(before, row.VersionBefore);
        Assert.Equal(after, row.VersionAfter);
    }

    // ── Test data builders ────────────────────────────────────────────────────

    private static AgreementConfigEntity NewAgreementConfig(decimal weeklyNorm = 37m) => new()
    {
        ConfigId = Guid.Empty,
        AgreementCode = "AVT_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8),
        OkVersion = "OK24",
        Status = AgreementConfigStatus.DRAFT,
        WeeklyNormHours = weeklyNorm,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37m,
        OvertimeThreshold100 = 40m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.5m,
        WeekendSaturdayRate = 1.5m,
        WeekendSundayRate = 2m,
        HolidayRate = 2m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 3m,
        CallInRate = 1m,
        TravelTimeEnabled = false,
        WorkingTravelRate = 1m,
        NonWorkingTravelRate = 0.5m,
        MaxDailyHours = 13m,
        MinimumRestHours = 11m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
        DefaultCompensationModel = "UDBETALING",
        EmployeeCompensationChoice = false,
        MaxOvertimeHoursPerPeriod = 0m,
        OvertimeRequiresPreApproval = false,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "audit-version-transition-test",
    };

    private static PositionOverrideConfigEntity NewPositionOverride(decimal? maxFlex = 200m) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = "AC",
        OkVersion = "OK24",
        PositionCode = "DEPARTMENT_HEAD",
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        FlexCarryoverMax = null,
        NormPeriodWeeks = 4,
        WeeklyNormHours = null,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "AVT_PO_" + Guid.NewGuid().ToString("N").Substring(0, 8),
    };

    private static WageTypeMapping NewWageTypeMapping(
        string? timeType = null, string wageType = "SLS_0110") => new()
    {
        TimeType = timeType ?? ("AVT_WTM_" + Guid.NewGuid().ToString("N").Substring(0, 6)),
        WageType = wageType,
        OkVersion = "OK24",
        AgreementCode = "HK",
        Position = "",
        Description = "audit-version-transition-test",
    };
}
