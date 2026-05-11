using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S29 / TASK-2909 D-tests #1, #2, #3, #4, #5 — same-day + cross-day supersession routing on
/// <see cref="WageTypeMappingRepository"/> (ADR-020 D2 + S22 precedent at
/// <c>LocalAgreementProfileRepository</c>). Verifies the repository-level supersession
/// contract end-to-end against a Postgres testcontainer with the full S29 effective-dating
/// schema (mapping_id surrogate PK + effective_from/effective_to range + partial-unique-
/// index on open rows + audit table widened with SUPERSEDED action).
///
/// <list type="bullet">
///   <item>#1: same-day UPDATE-in-place preserves natural key + bumps version + UPDATED audit.</item>
///   <item>#2: cross-day supersession (predecessor closed, new row inserted, SUPERSEDED
///   audit + WageTypeMappingSuperseded outbox event, single tx).</item>
///   <item>#3: <see cref="WageTypeMappingRepository.GetByKeyAtAsync"/> across closed range,
///   open range, before-earliest, and exact-boundary (end-exclusive predicate).</item>
///   <item>#4: D2 Case B (DELETE-then-CREATE-same-day, predecessor <c>effective_from &lt; today</c>):
///   predecessor stays closed at <c>(original_day, today)</c>; new row at <c>(today, NULL)</c>;
///   audit chain DELETED → CREATED.</item>
///   <item>#5: D2 Case C (CREATE-DELETE-CREATE-same-day, predecessor <c>effective_from == today</c>):
///   zero-width predecessor reopened via UPDATE-and-reopen; final state is single open row at
///   <c>(today, NULL)</c> with bumped version; UPDATED audit (not CREATED).</item>
/// </list>
///
/// Direct-repo orchestration — these are repo-surface contracts (per refinement L161 for #11a/#11b
/// and matching the S22 <see cref="ProfileSupersessionTests"/> location precedent). HTTP-level
/// validation lives in the endpoint tests for the same-day-only-edit validator (#12).
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingSupersessionTests : IAsyncLifetime
{
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";
    private const string Position = "";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private WageTypeMappingRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        // ForcedRollbackHarness schema includes wage_type_mapping_audit with version_before /
        // version_after columns + SUPERSEDED action — the canonical post-S29 fixture DDL.
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new WageTypeMappingRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #1 — same-day in-place UPDATE preserves natural key + bumps version.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SameDayInPlaceUpdate_PreservesNaturalKey_BumpsVersion_EmitsUpdatedAudit()
    {
        var timeType = NewTimeType("SAMEDAY");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Seed one row at effective_from = today, version = 1 (CreateAsync path).
        var seed = new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "original",
            EffectiveFrom = today,
        };
        await _repo.CreateAsync(seed);

        // Call SupersedeAndCreateAsync with newMapping.EffectiveFrom = today (== predecessor's),
        // different description — exercises the same-day in-place UPDATE branch.
        SaveWageTypeMappingResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var newMapping = new WageTypeMapping
            {
                TimeType = timeType,
                WageType = "SLS_0110",
                OkVersion = OkVersion,
                AgreementCode = AgreementCode,
                Position = Position,
                Description = "updated-same-day",
                EffectiveFrom = today,
            };
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, newMapping, expectedCurrentVersion: 1);

            // Endpoint-style emit UPDATED audit row to verify the audit pairing the
            // refinement L288 AC requires (#1: "Audit row inserted with action='UPDATED'").
            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "UPDATED",
                previousData: """{"description":"original"}""",
                newData: """{"description":"updated-same-day"}""",
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: 1, versionAfter: result.Version);

            await tx.CommitAsync();
        }

        Assert.False(result.IsCreated);
        Assert.Equal(2L, result.Version);
        Assert.Equal("updated-same-day", result.Mapping.Description);
        Assert.Equal(today, result.Mapping.EffectiveFrom);
        Assert.Null(result.Mapping.EffectiveTo);

        // Row count unchanged at 1 for the natural key.
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(timeType));

        // Audit row: action = UPDATED, version pair (1 -> 2).
        var (audAction, audBefore, audAfter) = await ReadLatestAuditAsync(timeType);
        Assert.Equal("UPDATED", audAction);
        Assert.Equal(1L, audBefore);
        Assert.Equal(2L, audAfter);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #2 — cross-day supersession.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task CrossDaySupersession_ClosesPredecessor_InsertsNewRow_EmitsSupersededAudit_SingleTx()
    {
        var timeType = NewTimeType("CROSSDAY");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Seed one row at effective_from = '2020-01-01' (mirrors init.sql backfill epoch).
        var predecessorEffectiveFrom = new DateOnly(2020, 1, 1);
        var seed = new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "original",
            EffectiveFrom = predecessorEffectiveFrom,
        };
        await _repo.CreateAsync(seed);

        // Cross-day supersession: SupersedeAndCreateAsync with newMapping.EffectiveFrom = today.
        SaveWageTypeMappingResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var newMapping = new WageTypeMapping
            {
                TimeType = timeType,
                WageType = "SLS_0110",
                OkVersion = OkVersion,
                AgreementCode = AgreementCode,
                Position = Position,
                Description = "new",
                EffectiveFrom = today,
            };
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, newMapping, expectedCurrentVersion: 1);

            // SUPERSEDED audit + outbox event are the endpoint's responsibility (per the
            // ConfigEndpoints / WageTypeMappingEndpoints contract). Mirror the endpoint
            // orchestration in-tx — single-tx invariant must hold (audit + outbox + state
            // change all commit together per ADR-018 D3).
            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "SUPERSEDED",
                previousData: """{"description":"original"}""",
                newData: """{"description":"new"}""",
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: 1, versionAfter: result.Version);

            // Outbox event (WageTypeMappingSuperseded) — written in the same tx.
            await EnqueueOutboxAsync(conn, tx, timeType, "WageTypeMappingSuperseded");

            await tx.CommitAsync();
        }

        Assert.False(result.IsCreated);
        Assert.Equal(1L, result.Version);  // new row starts at v=1
        Assert.Equal(today, result.Mapping.EffectiveFrom);
        Assert.Null(result.Mapping.EffectiveTo);

        // Row count: 2 (closed predecessor + new open row).
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(timeType));

        // Predecessor row: effective_to = today, original description preserved.
        var predecessor = await ReadRowAsync(timeType, predecessorEffectiveFrom);
        Assert.NotNull(predecessor);
        Assert.Equal(today, predecessor!.EffectiveTo);
        Assert.Equal("original", predecessor.Description);
        Assert.Equal(1L, predecessor.Version);

        // New row: effective_from = today, effective_to = NULL, version = 1.
        var newRow = await ReadRowAsync(timeType, today);
        Assert.NotNull(newRow);
        Assert.Null(newRow!.EffectiveTo);
        Assert.Equal("new", newRow.Description);
        Assert.Equal(1L, newRow.Version);

        // Single audit row with SUPERSEDED + version pair (1 -> 1).
        var auditRows = await ReadAllAuditAsync(timeType);
        var supersededAuditCount = auditRows.Count(r => r.Action == "SUPERSEDED");
        Assert.Equal(1, supersededAuditCount);
        var sup = auditRows.Single(r => r.Action == "SUPERSEDED");
        Assert.Equal(1L, sup.VersionBefore);
        Assert.Equal(1L, sup.VersionAfter);

        // Single outbox row for stream wage-type-mapping-{AC}-{OK}-{TT}.
        var streamId = $"wage-type-mapping-{AgreementCode}-{OkVersion}-{timeType}";
        Assert.Equal(1L, await CountOutboxRowsAsync(streamId, "WageTypeMappingSuperseded"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #3 — GetByKeyAtAsync(asOfDate) across 3 cases + end-exclusive boundary.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task GetByKeyAtAsync_HistoryAndOpenRows_ResolvesCorrectlyAcrossDates()
    {
        var timeType = NewTimeType("DATED");

        // Seed one closed history row at [2024-01-01, 2024-06-01).
        // Then one open row at [2024-06-01, NULL).
        await InsertRawAsync(
            timeType, "SLS_0110", description: "closed-row",
            effectiveFrom: new DateOnly(2024, 1, 1),
            effectiveTo: new DateOnly(2024, 6, 1),
            version: 1);
        await InsertRawAsync(
            timeType, "SLS_0220", description: "open-row",
            effectiveFrom: new DateOnly(2024, 6, 1),
            effectiveTo: null,
            version: 1);

        // (a) asOfDate before earliest history row → NULL.
        var beforeEarliest = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position,
            asOfDate: new DateOnly(2023, 12, 1));
        Assert.Null(beforeEarliest);

        // (b) asOfDate within the closed range → returns the closed row.
        var withinClosed = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position,
            asOfDate: new DateOnly(2024, 3, 15));
        Assert.NotNull(withinClosed);
        Assert.Equal("closed-row", withinClosed!.Description);
        Assert.Equal("SLS_0110", withinClosed.WageType);

        // (c) asOfDate within the open range → returns the open row.
        var withinOpen = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position,
            asOfDate: new DateOnly(2024, 9, 1));
        Assert.NotNull(withinOpen);
        Assert.Equal("open-row", withinOpen!.Description);
        Assert.Equal("SLS_0220", withinOpen.WageType);
        Assert.Null(withinOpen.EffectiveTo);

        // (d) Boundary: asOfDate == exact effective_from of the open row.
        // Per the end-exclusive predicate (effective_from <= asOfDate AND
        // (effective_to IS NULL OR effective_to > asOfDate)), the open row wins:
        //   - closed row: effective_to = 2024-06-01, asOfDate = 2024-06-01 → 6/1 > 6/1 is FALSE
        //                 → closed row excluded.
        //   - open row:   effective_from = 2024-06-01, asOfDate = 2024-06-01 → 6/1 <= 6/1 TRUE,
        //                 effective_to IS NULL TRUE → open row included.
        var atBoundary = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position,
            asOfDate: new DateOnly(2024, 6, 1));
        Assert.NotNull(atBoundary);
        Assert.Equal("open-row", atBoundary!.Description);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #4 — D2 Case B: DELETE-then-CREATE-same-day with predecessor < today.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Case_B_DeleteThenCreateSameDay_PredecessorBeforeToday_ResultsInTwoRows()
    {
        var timeType = NewTimeType("CASEB");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var pastEffectiveFrom = new DateOnly(2024, 1, 1);

        // Setup: insert one open row at effective_from = 2024-01-01 (a typical seed row).
        var seed = new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "original-seed",
            EffectiveFrom = pastEffectiveFrom,
        };
        await _repo.CreateAsync(seed);

        // Step 1: SoftDeleteAsync(today) — predecessor becomes (2024-01-01, today).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var deleted = await _repo.SoftDeleteAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                expectedVersion: 1, closeDate: today);
            Assert.True(deleted);

            // The DELETE's own audit row (DELETED).
            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "DELETED",
                previousData: """{"description":"original-seed"}""",
                newData: null,
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: 1, versionAfter: 1);
            await tx.CommitAsync();
        }

        // Step 2: a fresh CREATE for the same natural key — repro the endpoint's Case B
        // path: there's no open row, but there's a closed-today predecessor with
        // effective_from < today, so Case B is fresh INSERT preserving the closure.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Mirror the endpoint's lock query for closed-on-today predecessors.
            WageTypeMapping? closedToday = null;
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                       description, version, effective_from, effective_to
                FROM wage_type_mappings
                WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
                  AND position = @pos AND effective_to = @today
                FOR UPDATE
                """, conn, tx))
            {
                lockCmd.Parameters.AddWithValue("tt", timeType);
                lockCmd.Parameters.AddWithValue("ok", OkVersion);
                lockCmd.Parameters.AddWithValue("ac", AgreementCode);
                lockCmd.Parameters.AddWithValue("pos", Position);
                lockCmd.Parameters.AddWithValue("today", today);
                await using var reader = await lockCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync(), "Predecessor closed-today not found");
                closedToday = new WageTypeMapping
                {
                    MappingId = reader.GetGuid(0),
                    TimeType = reader.GetString(1),
                    WageType = reader.GetString(2),
                    OkVersion = reader.GetString(3),
                    AgreementCode = reader.GetString(4),
                    Position = reader.GetString(5),
                    Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Version = reader.GetInt64(7),
                    EffectiveFrom = reader.GetFieldValue<DateOnly>(8),
                    EffectiveTo = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                };
            }

            // Case B routing: predecessor.effective_from < today → fresh INSERT.
            Assert.True(closedToday!.EffectiveFrom < today);

            await _repo.CreateAsync(conn, tx, new WageTypeMapping
            {
                TimeType = timeType,
                WageType = "SLS_0110",
                OkVersion = OkVersion,
                AgreementCode = AgreementCode,
                Position = Position,
                Description = "recreated-case-B",
                EffectiveFrom = today,
            });

            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "CREATED",
                previousData: null,
                newData: """{"description":"recreated-case-B"}""",
                actorId: "admin1", actorRole: "GlobalAdmin");
            await tx.CommitAsync();
        }

        // Assert: row count = 2. Predecessor unchanged from step 1 (effective_to = today).
        // New row: effective_from = today, effective_to = NULL.
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(timeType));
        var predecessor = await ReadRowAsync(timeType, pastEffectiveFrom);
        Assert.NotNull(predecessor);
        Assert.Equal(today, predecessor!.EffectiveTo);

        var newRow = await ReadRowAsync(timeType, today);
        Assert.NotNull(newRow);
        Assert.Null(newRow!.EffectiveTo);
        Assert.Equal("recreated-case-B", newRow.Description);
        Assert.Equal(1L, newRow.Version);

        // Audit chain: DELETED (step 1) then CREATED (step 2) — no SUPERSEDED.
        var allAudits = await ReadAllAuditAsync(timeType);
        Assert.Contains(allAudits, a => a.Action == "DELETED");
        Assert.Contains(allAudits, a => a.Action == "CREATED");
        Assert.DoesNotContain(allAudits, a => a.Action == "SUPERSEDED");

        // GetByKeyAtAsync gap behavior: yesterday returns the closed row;
        // today returns the new row (end-exclusive predicate at the closed row's effective_to).
        var yesterday = today.AddDays(-1);
        var resolveYesterday = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: yesterday);
        Assert.NotNull(resolveYesterday);
        Assert.Equal("original-seed", resolveYesterday!.Description);

        var resolveToday = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: today);
        Assert.NotNull(resolveToday);
        Assert.Equal("recreated-case-B", resolveToday!.Description);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #5 — D2 Case C: CREATE-DELETE-CREATE-same-day with predecessor = today.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Case_C_CreateDeleteCreateSameDay_PredecessorEqualToday_UpdatesAndReopens()
    {
        var timeType = NewTimeType("CASEC");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Step 0: insert one open row at effective_from = today.
        await _repo.CreateAsync(new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "first-create-today",
            EffectiveFrom = today,
        });

        // Step 1: SoftDeleteAsync(today) — zero-width close (effective_from = effective_to = today).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var deleted = await _repo.SoftDeleteAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                expectedVersion: 1, closeDate: today);
            Assert.True(deleted);

            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "DELETED",
                previousData: """{"description":"first-create-today"}""",
                newData: null,
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: 1, versionAfter: 1);
            await tx.CommitAsync();
        }

        // Verify the zero-width predecessor state (effective_from = effective_to = today).
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(timeType));
        var afterDelete = await ReadRowAsync(timeType, today);
        Assert.NotNull(afterDelete);
        Assert.Equal(today, afterDelete!.EffectiveFrom);
        Assert.Equal(today, afterDelete.EffectiveTo);

        // Step 2: re-CREATE same natural key, same day — Case C: UPDATE-and-reopen.
        long persistedVersion;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Lock the closed-today predecessor (mirror endpoint).
            Guid mappingId;
            long preVersion;
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT mapping_id, version, effective_from
                FROM wage_type_mappings
                WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
                  AND position = @pos AND effective_to = @today
                FOR UPDATE
                """, conn, tx))
            {
                lockCmd.Parameters.AddWithValue("tt", timeType);
                lockCmd.Parameters.AddWithValue("ok", OkVersion);
                lockCmd.Parameters.AddWithValue("ac", AgreementCode);
                lockCmd.Parameters.AddWithValue("pos", Position);
                lockCmd.Parameters.AddWithValue("today", today);
                await using var reader = await lockCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync(), "Zero-width predecessor not found");
                mappingId = reader.GetGuid(0);
                preVersion = reader.GetInt64(1);
                var lockedFrom = reader.GetFieldValue<DateOnly>(2);
                Assert.Equal(today, lockedFrom); // Case C: predecessor.effective_from == today
            }

            // Case C reopen: UPDATE effective_to = NULL + version + 1 + new field values.
            await using (var reopenCmd = new NpgsqlCommand(
                """
                UPDATE wage_type_mappings SET
                    wage_type    = @wt,
                    description  = @desc,
                    effective_to = NULL,
                    version      = version + 1
                WHERE mapping_id = @id
                RETURNING version
                """, conn, tx))
            {
                reopenCmd.Parameters.AddWithValue("wt", "SLS_0110");
                reopenCmd.Parameters.AddWithValue("desc", "recreated-case-C");
                reopenCmd.Parameters.AddWithValue("id", mappingId);
                persistedVersion = (long)(await reopenCmd.ExecuteScalarAsync())!;
            }

            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "UPDATED",
                previousData: """{"description":"first-create-today"}""",
                newData: """{"description":"recreated-case-C"}""",
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: preVersion, versionAfter: persistedVersion);
            await tx.CommitAsync();
        }

        // Assert: row count = 1 (UPDATE-and-reopen, NOT a fresh INSERT).
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(timeType));

        // Row: effective_from = today, effective_to = NULL, version bumped to 2.
        var final = await ReadRowAsync(timeType, today);
        Assert.NotNull(final);
        Assert.Equal(today, final!.EffectiveFrom);
        Assert.Null(final.EffectiveTo);
        Assert.Equal(2L, final.Version);
        Assert.Equal("recreated-case-C", final.Description);
        Assert.Equal(2L, persistedVersion);

        // Audit chain: DELETED (step 1) → UPDATED (step 2). NO separate CREATED for step 2.
        var allAudits = await ReadAllAuditAsync(timeType);
        Assert.Contains(allAudits, a => a.Action == "DELETED");
        Assert.Contains(allAudits, a => a.Action == "UPDATED");
        Assert.DoesNotContain(allAudits, a => a.Action == "CREATED");
    }

    // ─── Test helpers ────────────────────────────────────────────────────────

    private static string NewTimeType(string prefix) =>
        $"WTM_S29_{prefix}_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<long> CountRowsForNaturalKeyAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<WageTypeMapping?> ReadRowAsync(string timeType, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                   description, version, effective_from, effective_to
            FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos AND effective_from = @ef
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        cmd.Parameters.AddWithValue("ef", effectiveFrom);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new WageTypeMapping
        {
            MappingId = reader.GetGuid(0),
            TimeType = reader.GetString(1),
            WageType = reader.GetString(2),
            OkVersion = reader.GetString(3),
            AgreementCode = reader.GetString(4),
            Position = reader.GetString(5),
            Description = reader.IsDBNull(6) ? null : reader.GetString(6),
            Version = reader.GetInt64(7),
            EffectiveFrom = reader.GetFieldValue<DateOnly>(8),
            EffectiveTo = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
        };
    }

    private async Task InsertRawAsync(
        string timeType, string wageType, string description,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long version)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (
                mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                description, effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @tt, @wt, @ok, @ac, @pos,
                @desc, @ef, @et, @v)
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        cmd.Parameters.AddWithValue("desc", description);
        cmd.Parameters.AddWithValue("ef", effectiveFrom);
        cmd.Parameters.AddWithValue("et", (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string Action, long? VersionBefore, long? VersionAfter)> ReadLatestAuditAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM wage_type_mapping_audit
            WHERE time_type = @tt
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "No audit row found");
        var action = reader.GetString(0);
        long? before = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        long? after = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        return (action, before, after);
    }

    private async Task<List<(string Action, long? VersionBefore, long? VersionAfter)>> ReadAllAuditAsync(string timeType)
    {
        var rows = new List<(string Action, long? VersionBefore, long? VersionAfter)>();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM wage_type_mapping_audit
            WHERE time_type = @tt
            ORDER BY audit_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var action = reader.GetString(0);
            long? before = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            long? after = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            rows.Add((action, before, after));
        }
        return rows;
    }

    private async Task<long> CountOutboxRowsAsync(string streamId, string eventType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @sid AND event_type = @et
            """, conn);
        cmd.Parameters.AddWithValue("sid", streamId);
        cmd.Parameters.AddWithValue("et", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task EnqueueOutboxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string eventType)
    {
        var streamId = $"wage-type-mapping-{AgreementCode}-{OkVersion}-{timeType}";
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (
                service_id, stream_id, event_id, event_type, event_payload)
            VALUES (
                'backend-api', @sid, @eid, @et, @payload::jsonb)
            """, conn, tx);
        cmd.Parameters.AddWithValue("sid", streamId);
        cmd.Parameters.AddWithValue("eid", Guid.NewGuid());
        cmd.Parameters.AddWithValue("et", eventType);
        cmd.Parameters.AddWithValue("payload", "{}");
        await cmd.ExecuteNonQueryAsync();
    }
}
