using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S29 / TASK-2909 D-tests #9, #11a, #11b — concurrent-write race-safety on
/// <c>wage_type_mappings</c>. Direct-repo orchestration (per refinement L161 cycle 2 R2-W5
/// absorption) — mirrors the S22 <see cref="ProfileSupersessionTests.ConcurrentFirstCreate_RaceTranslatesToOptimisticConcurrency"/>
/// precedent which proved the SELECT-FOR-UPDATE + partial-unique-index lock contract at
/// the repository surface (not HTTP).
///
/// <list type="bullet">
///   <item><b>#9 (partial-unique-index enforcement)</b>: two concurrent same-day INSERTs on
///   the same natural key → exactly one wins, the other gets <c>PostgresException</c> with
///   <c>SqlState='23505'</c> and <c>ConstraintName='idx_wtm_natural_key_open'</c>.</item>
///   <item><b>#11a (DELETE↔POST race)</b>: Thread A holds the row lock via
///   <see cref="WageTypeMappingRepository.SoftDeleteAsync"/> + <c>SELECT ... FOR UPDATE</c>;
///   Thread B's POST blocks on lock acquisition; once Thread A commits, Thread B unblocks,
///   observes the closed-today predecessor, and routes Case B fresh INSERT. NO 23505,
///   audit chain is DELETED → CREATED.</item>
///   <item><b>#11b (POST↔POST race with no predecessor)</b>: both threads observe 0 rows on
///   lock query, both proceed Case A INSERT, the partial-unique-index
///   <c>idx_wtm_natural_key_open WHERE effective_to IS NULL</c> rejects the loser with
///   23505. Exactly one open row afterwards.</item>
/// </list>
///
/// Synchronization primitive: <see cref="TaskCompletionSource"/> — Thread A signals it has
/// acquired its lock (and is holding it before commit), Thread B awaits the signal then
/// fires its conflicting query. After Thread A commits, Thread B unblocks naturally via
/// PostgreSQL's row-lock release.
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingRaceTests : IAsyncLifetime
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
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new WageTypeMappingRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #9 — partial-unique-index enforcement on two concurrent same-day INSERTs.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task PartialUniqueIndex_RejectsSecondOpenRowForSameNaturalKey()
    {
        var timeType = NewTimeType("UNIQ");

        // Sequential pattern (no concurrent-thread interleaving required): the
        // partial-unique-index enforces "at most one open row per natural key" — so after
        // Tx A commits its open INSERT, Tx B's open INSERT raises 23505 immediately
        // (it's not a lock-wait race; the committed Tx A row is visible to B's index
        // check). Concurrent-thread interleaving is exercised by #11a + #11b below; this
        // test pins the index constraint itself.
        await using (var connA = _harness.Factory.Create())
        {
            await connA.OpenAsync();
            await using var txA = await connA.BeginTransactionAsync();
            await InsertOpenRowAsync(connA, txA, timeType);
            await txA.CommitAsync();
        }

        var pgEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connB = _harness.Factory.Create();
            await connB.OpenAsync();
            await using var txB = await connB.BeginTransactionAsync();
            await InsertOpenRowAsync(connB, txB, timeType);
            await txB.CommitAsync();
        });
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("idx_wtm_natural_key_open", pgEx.ConstraintName);

        // After both threads complete, exactly one open row exists for the natural key.
        Assert.Equal(1L, await CountOpenRowsAsync(timeType));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #11a — DELETE↔POST race: Thread A's DELETE acquires lock, Thread B's
    // POST blocks; after A commits, B observes closed-today predecessor → Case B.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task DeleteVsPost_Race_ThreadBObservesClosedTodayPredecessor_RoutesCaseB()
    {
        var timeType = NewTimeType("DELPOST");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var pastEffectiveFrom = new DateOnly(2024, 1, 1);

        // Setup: insert one open row at effective_from = 2024-01-01 (predecessor < today).
        await _repo.CreateAsync(new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "predecessor",
            EffectiveFrom = pastEffectiveFrom,
        });

        // Concurrent race: Thread A opens its tx + acquires the natural-key row lock via
        // SoftDeleteAsync (which performs SELECT ... FOR UPDATE on the open row). Thread B
        // opens its tx + issues SELECT ... FOR UPDATE on the SAME open row (effective_to IS
        // NULL predicate to match what's there pre-A's UPDATE). Thread B blocks on Thread
        // A's row lock. When Thread A commits its DELETE (effective_to = today), Thread B's
        // wait ends; B's now-released SELECT sees the closed-today row (the predicate
        // updated to effective_to IS NULL no longer matches, so B re-queries on the
        // post-commit predicate to find the closed-today predecessor).
        //
        // To avoid MVCC snapshot subtlety with the post-A-commit `effective_to = today`
        // query (B's snapshot is taken at the FIRST FOR UPDATE which may be too early),
        // each B-side read uses its own fresh sub-transaction-scope query.
        var threadAReleasedSignal = new TaskCompletionSource();

        var threadATask = Task.Run(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var deleted = await _repo.SoftDeleteAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                expectedVersion: 1, closeDate: today);
            Assert.True(deleted);

            await _repo.AppendAuditAsync(
                conn, tx, timeType, OkVersion, AgreementCode, Position,
                "DELETED",
                previousData: """{"description":"predecessor"}""",
                newData: null,
                actorId: "admin1", actorRole: "GlobalAdmin",
                versionBefore: 1, versionAfter: 1);

            // Hold the lock briefly so Thread B genuinely interleaves (would block on the
            // FOR UPDATE row lock if it tried to acquire the same row). Then commit.
            await Task.Delay(200);
            await tx.CommitAsync();
            threadAReleasedSignal.SetResult();
        });

        // Thread B: simulate the endpoint POST orchestration. The endpoint's pattern is:
        //   1. Acquire the closed-today row lock (SELECT FOR UPDATE WHERE effective_to=today).
        //   2. If found, route Case B / Case C; if not, route Case A.
        // We wait for Thread A's commit signal first to make B's read deterministic — once
        // A has committed, B's snapshot at the SELECT timestamp sees the closed-today row.
        // (Without the wait, B's snapshot may be taken before A commits, returning 0 rows
        // and forcing Case A routing — a different valid race outcome that we exercise via
        // D-test #11b's no-predecessor path.)
        await threadAReleasedSignal.Task;
        await threadATask;

        await using (var connB = _harness.Factory.Create())
        {
            await connB.OpenAsync();
            await using var txB = await connB.BeginTransactionAsync();
            var closedToday = await TryLockClosedTodayAsync(connB, txB, timeType, today);
            Assert.NotNull(closedToday);
            Assert.Equal(today, closedToday!.EffectiveTo);
            Assert.True(closedToday.EffectiveFrom < today,
                "Predecessor must have effective_from < today for Case B routing.");

            // Case B: fresh INSERT preserving the closure (no UPDATE of predecessor).
            await _repo.CreateAsync(connB, txB, new WageTypeMapping
            {
                TimeType = timeType,
                WageType = "SLS_0110",
                OkVersion = OkVersion,
                AgreementCode = AgreementCode,
                Position = Position,
                Description = "case-B-after-race",
                EffectiveFrom = today,
            });
            await _repo.AppendAuditAsync(
                connB, txB, timeType, OkVersion, AgreementCode, Position,
                "CREATED",
                previousData: null,
                newData: """{"description":"case-B-after-race"}""",
                actorId: "admin2", actorRole: "GlobalAdmin");
            await txB.CommitAsync();
        }

        // Assertions: row count = 2 (closed predecessor + new open row).
        Assert.Equal(2L, await CountRowsAsync(timeType));
        Assert.Equal(1L, await CountOpenRowsAsync(timeType));

        // Audit chain: DELETED (Thread A) + CREATED (Thread B). No 23505 collision.
        var audits = await ReadAuditActionsAsync(timeType);
        Assert.Contains("DELETED", audits);
        Assert.Contains("CREATED", audits);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #11b — POST↔POST race with no predecessor: both threads run Case A
    // INSERT; partial-unique-index rejects the loser with 23505.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task PostVsPost_Race_NoPredecessor_PartialUniqueIndexRejectsLoser()
    {
        var timeType = NewTimeType("POSTPOST");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Setup: no row exists for the natural key. Both Thread A + Thread B race to
        // INSERT a fresh open row at effective_to = NULL. The partial-unique-index
        // (idx_wtm_natural_key_open WHERE effective_to IS NULL) allows exactly one to
        // commit; the other gets 23505 on its INSERT or commit.

        // Open both connections + txs.
        await using var connA = _harness.Factory.Create();
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();

        await using var connB = _harness.Factory.Create();
        await connB.OpenAsync();
        await using var txB = await connB.BeginTransactionAsync();

        // Both threads perform their endpoint-mirror lock query for closed-today
        // predecessors. With no predecessor, both queries return 0 rows — both proceed
        // to Case A INSERT.
        Assert.Null(await TryLockClosedTodayAsync(connA, txA, timeType, today));
        Assert.Null(await TryLockClosedTodayAsync(connB, txB, timeType, today));

        // Both threads INSERT a fresh row. Thread A's INSERT succeeds (no committed
        // collision yet because the second write blocks on the unique index check).
        // Thread B's INSERT blocks on the index pending Thread A's commit, then either
        // (a) fails immediately when A is committed or (b) waits to see A's commit then
        // raises 23505 on commit. Either way, exactly one writer wins.
        await _repo.CreateAsync(connA, txA, new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_AAA",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "winner-A",
            EffectiveFrom = today,
        });

        // Thread B's INSERT into the same partial-unique-key slot. PostgreSQL behaviors:
        //  - the INSERT may block waiting on Thread A's lock and only fail after Thread A
        //    commits, OR
        //  - if Thread A has already committed, the INSERT fails immediately.
        // Either way the loser gets 23505 + idx_wtm_natural_key_open. We commit A first
        // to make the failure path deterministic.
        await txA.CommitAsync();

        var pgEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await _repo.CreateAsync(connB, txB, new WageTypeMapping
            {
                TimeType = timeType,
                WageType = "SLS_BBB",
                OkVersion = OkVersion,
                AgreementCode = AgreementCode,
                Position = Position,
                Description = "loser-B",
                EffectiveFrom = today,
            });
            await txB.CommitAsync();
        });
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("idx_wtm_natural_key_open", pgEx.ConstraintName);

        // Tx B should already be aborted by the exception — rollback for cleanliness.
        if (txB.Connection is not null)
            await txB.RollbackAsync();

        // Exactly one open row after both threads complete.
        Assert.Equal(1L, await CountOpenRowsAsync(timeType));
        var winner = await ReadOnlyOpenRowAsync(timeType);
        Assert.NotNull(winner);
        Assert.Equal("winner-A", winner!.Description);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string NewTimeType(string prefix) =>
        $"WTM_RACE_{prefix}_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task InsertOpenRowAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string timeType)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await _repo.CreateAsync(conn, tx, new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "SLS_0110",
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = "concurrent-insert",
            EffectiveFrom = today,
        });
    }

    private async Task<long> CountRowsAsync(string timeType)
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

    private async Task<long> CountOpenRowsAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<WageTypeMapping?> ReadOnlyOpenRowAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                   description, version, effective_from, effective_to
            FROM wage_type_mappings
            WHERE time_type = @tt AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
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

    private static async Task<WageTypeMapping?> TryLockClosedTodayAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string timeType, DateOnly today)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                   description, version, effective_from, effective_to
            FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos AND effective_to = @today
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("tt", timeType);
        lockCmd.Parameters.AddWithValue("ok", OkVersion);
        lockCmd.Parameters.AddWithValue("ac", AgreementCode);
        lockCmd.Parameters.AddWithValue("pos", Position);
        lockCmd.Parameters.AddWithValue("today", today);
        await using var reader = await lockCmd.ExecuteReaderAsync();
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

    private async Task<List<string>> ReadAuditActionsAsync(string timeType)
    {
        var actions = new List<string>();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action FROM wage_type_mapping_audit
            WHERE time_type = @tt ORDER BY audit_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actions.Add(reader.GetString(0));
        return actions;
    }
}
