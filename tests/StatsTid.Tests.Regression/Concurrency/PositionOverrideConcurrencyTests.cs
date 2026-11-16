using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S25 / TASK-2508 concurrency regression tests for the three v3 mutating endpoints on
/// <c>position_override_configs</c> (per ADR-019 D2/D6/D7/D8). Verifies the row-version
/// optimistic-concurrency contract at the repository surface, the admin-strict If-Match
/// parser at the helper surface, and ADR-019 D6's 23505-vs-412 distinction (partial-unique-
/// index race surfaces as <see cref="PostgresException"/> with SQL state 23505 → endpoint
/// maps to 409, distinct from row-version concurrency → 412).
///
/// <para>
/// Test slots (4 total):
///   <list type="bullet">
///     <item>3 stale-If-Match → <see cref="OptimisticConcurrencyException"/> tests
///       (PUT update / activate / deactivate)</item>
///     <item>1 end-to-end ETag-cycle test (CREATE → version=1, UPDATE → version=2 read-back)</item>
///   </list>
/// S133 / TASK-13306 (QUAL-111): the three missing-If-Match "428" slots that used to live here were
/// verification-theater clones — they called <c>EtagHeaderHelper.TryParseIfMatch</c> directly on a
/// hand-built header-less request and so pinned the shared helper, NEVER the endpoints they were named
/// for (a removed endpoint 428 guard left every clone GREEN, PAT-014). They were deleted; genuine
/// per-endpoint 428 proofs that drive the REAL routes over HTTP now live in
/// <c>Hosting.PositionOverridePreconditionHttpTests</c>.
///
/// The 23505-vs-412 distinction test lives in
/// <see cref="ActivateConflict_Distinction_23505_NotOptimisticConcurrency"/> and is COUNTED
/// within the 8 stale-If-Match slots (it stresses the activate path's concurrent-sibling
/// race rather than a row-version mismatch — different exception class, different mapping).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideConcurrencyTests : IAsyncLifetime
{
    private const string AgreementCode = "AC";
    private const string OkVersion = "OK24";
    private const string PositionCode = "DEPARTMENT_HEAD";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private PositionOverrideRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new PositionOverrideRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ─── Stale-If-Match (412 contract) — repo surface ─────────────────────────

    [Fact]
    public async Task Update_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed an ACTIVE position override; bump it once via v3 to take version 1 → 2;
        // attempt a SECOND v3 update with the STALE expectedVersion=1.
        var initial = NewOverride(maxFlex: 200m);
        var overrideId = await PositionOverrideTestWriter.CreateAsync(_repo, _harness.Factory, initial);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var result = await PositionOverrideTestWriter.UpdateAsync(_repo, 
                conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 250m));
            await tx.CommitAsync();
            Assert.Equal(2L, result.Version);
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await PositionOverrideTestWriter.UpdateAsync(_repo, 
                conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 300m));
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Deactivate_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed ACTIVE; bump version to 2 via update; attempt deactivate with STALE
        // expectedVersion=1.
        var seed = NewOverride();
        var overrideId = await PositionOverrideTestWriter.CreateAsync(_repo, _harness.Factory, seed);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await PositionOverrideTestWriter.UpdateAsync(_repo, conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 250m));
            await tx.CommitAsync();
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await PositionOverrideTestWriter.DeactivateAsync(_repo, conn, tx, overrideId, expectedVersion: 1);
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task ActivateConflict_Distinction_23505_NotOptimisticConcurrency()
    {
        // ADR-019 D6 — the partial-unique-index `WHERE status='ACTIVE'` enforces "at most
        // one ACTIVE per (agreement_code, ok_version, position_code)". Activating a second
        // override for the same triple while the first is still ACTIVE must fire a Postgres
        // 23505 (unique violation) rather than an OptimisticConcurrencyException — these are
        // distinct race classes that the endpoint handler (PositionOverrideEndpoints.cs:428)
        // catches in a specific order: PostgresException SqlState=23505 → 409, then
        // OptimisticConcurrencyException → 412.
        //
        // This test pins the exception-class distinction at the repo surface. Both rows
        // share the same (agreement, ok, position) triple; the FIRST is created ACTIVE; the
        // SECOND is then deactivated to INACTIVE; activating the second under v3 would clash
        // with the first's still-active partial-unique-index entry → 23505.
        var sharedPosition = "POS_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        await SeedPositionAsync(sharedPosition);

        // First override — created ACTIVE.
        var first = NewOverride(positionCode: sharedPosition);
        await PositionOverrideTestWriter.CreateAsync(_repo, _harness.Factory, first);

        // Second override on the same triple — deactivate immediately (CreateAsync writes
        // ACTIVE so we'd hit 23505 on the create itself; deactivate it via the self-managed
        // path so the second row exists in INACTIVE state with version=1).
        // Self-managed CreateAsync writes ACTIVE — to land a SECOND row with the same triple,
        // we have to bypass the partial-unique-index. The simplest path: deactivate the FIRST
        // first, create the SECOND ACTIVE, then re-activate the FIRST. But the simpler path
        // for THIS test is: insert a parallel row directly (bypassing the repo) at INACTIVE
        // status, then attempt v3 ActivateAsync on it. The 23505 fires when v3 tries to
        // transition to ACTIVE because the first row is still ACTIVE.
        Guid secondId;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            secondId = Guid.NewGuid();
            await using var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO position_override_configs (
                    override_id, agreement_code, ok_version, position_code, status,
                    max_flex_balance, created_by, description, created_at, updated_at,
                    applies_from -- mandatory since S148 C-2 (no DEFAULT)
                ) VALUES (
                    @id, @ac, @ok, @pos, 'INACTIVE',
                    @mfb, 'tester', @desc, NOW(), NOW(),
                    '0001-01-01'
                )
                """, conn);
            insertCmd.Parameters.AddWithValue("id", secondId);
            insertCmd.Parameters.AddWithValue("ac", AgreementCode);
            insertCmd.Parameters.AddWithValue("ok", OkVersion);
            insertCmd.Parameters.AddWithValue("pos", sharedPosition);
            insertCmd.Parameters.AddWithValue("mfb", 100m);
            insertCmd.Parameters.AddWithValue("desc", "second-inactive-override");
            await insertCmd.ExecuteNonQueryAsync();

            // S148 NR-P8: every override head has its matching version row (state INACTIVE, the
            // helper's date, the same values), as the repository's writers leave it.
            await using var versionCmd = new NpgsqlCommand(
                """
                INSERT INTO position_override_config_versions (
                    override_id, applies_from, state, max_flex_balance, norm_period_weeks,
                    weekly_norm_hours, recorded_by
                ) VALUES (@id, @from, 'INACTIVE', @mfb, NULL, NULL, 'tester')
                """, conn);
            versionCmd.Parameters.AddWithValue("id", secondId);
            versionCmd.Parameters.AddWithValue("from", PositionOverrideTestWriter.Anchor);
            versionCmd.Parameters.AddWithValue("mfb", 100m);
            await versionCmd.ExecuteNonQueryAsync();
        }

        // Read the second row's version (should be 1 from DB DEFAULT).
        var second = await _repo.GetByIdAsync(secondId);
        Assert.NotNull(second);
        Assert.Equal("INACTIVE", second!.Status);
        Assert.Equal(1L, second.Version);

        // Attempt v3 activate on the second — must fire 23505 because the first override
        // is still ACTIVE for the same triple. Critically: the exception type is
        // PostgresException with SqlState=23505, NOT OptimisticConcurrencyException.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await PositionOverrideTestWriter.ActivateAsync(_repo, conn, tx, secondId, expectedVersion: 1);
            await tx.CommitAsync();
        });
        Assert.Equal("23505", ex.SqlState);
    }

    // ─── End-to-end ETag-cycle ─────────────────────────────────────────────────

    [Fact]
    public async Task EtagCycle_CreateThenUpdate_VersionMonotonicallyIncreases()
    {
        // Wire shape: CREATE (version=1) → GET (version=1) → UPDATE with If-Match: "1"
        // (version=2) → GET (version=2). This is the contract the frontend banner-with-retry
        // hook depends on.
        var initial = NewOverride(maxFlex: 200m);
        var overrideId = await PositionOverrideTestWriter.CreateAsync(_repo, _harness.Factory, initial);

        var afterCreate = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(afterCreate);
        Assert.Equal(1L, afterCreate!.Version);

        SavePositionOverrideResult updateResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            updateResult = await PositionOverrideTestWriter.UpdateAsync(_repo, 
                conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 300m));
            await tx.CommitAsync();
        }
        Assert.Equal(2L, updateResult.Version);
        Assert.False(updateResult.IsCreated);
        Assert.Equal("ACTIVE", updateResult.Status);

        var afterUpdate = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(afterUpdate);
        Assert.Equal(2L, afterUpdate!.Version);
        Assert.Equal(300m, afterUpdate.MaxFlexBalance);
    }

    // ─── AC-C2r: the empty key, three writers (S148 5a c1 ruling 1.1) ──────────

    /// <summary>
    /// A key with no head has no row to lock, so the key's advisory lock is what serialises its
    /// writers. A creates and deactivates in one open transaction (dated 12 and 19 January 2026,
    /// both Mondays); B's create dated 5 January must wait for A, then see A's committed versions
    /// and be refused as before the predecessor (19 January). The poll waits until B holds ANY
    /// ungranted lock, so the wait is the same in both worlds and the verdict lands at the outcome.
    ///
    /// <para>Red conditions (M-43, delete the advisory statement in
    /// <c>PositionOverrideRepository.LockKeyAsync</c>): B passes the date rules on the empty key,
    /// its INSERT waits on A's uncommitted ACTIVE index entry (a <c>transactionid</c> wait, still
    /// ungranted, so the poll returns), A commits, B's INSERT then proceeds and commits a version
    /// dated 5 January. B does not throw, so <c>ThrowsAsync</c> fails; and the key would hold three
    /// version rows with an ACTIVE head. The only way this fact can time out is a mis-wired test.</para>
    /// </summary>
    [Fact]
    public async Task EmptyKey_ThreeWriters_LateCreateRefusedBeforePredecessor()
    {
        var position = "POS_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        await SeedPositionAsync(position);

        var monday12 = new DateOnly(2026, 1, 12);
        var monday19 = new DateOnly(2026, 1, 19);
        var monday5 = new DateOnly(2026, 1, 5);

        await using var connA = _harness.Factory.Create();
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();
        var aEntity = new PositionOverrideConfigEntity
        {
            OverrideId = Guid.Empty, AgreementCode = AgreementCode, OkVersion = OkVersion,
            PositionCode = position, Status = "ACTIVE", MaxFlexBalance = 200m, NormPeriodWeeks = 4,
            WeeklyNormHours = 37m, CreatedBy = "tester", CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow, Description = "C2R_A",
        };
        var aId = await _repo.CreateAsync(connA, txA, aEntity, monday12);
        await _repo.DeactivateAsync(connA, txA, aId, 1, monday19, PositionOverrideTestWriter.Actor);
        // A is NOT committed: B must queue behind it.

        await using var connB = _harness.Factory.Create();
        await connB.OpenAsync();
        var pidB = connB.ProcessID;
        var bTask = Task.Run(async () =>
        {
            await using var txB = await connB.BeginTransactionAsync();
            var bEntity = new PositionOverrideConfigEntity
            {
                OverrideId = Guid.Empty, AgreementCode = AgreementCode, OkVersion = OkVersion,
                PositionCode = position, Status = "ACTIVE", MaxFlexBalance = 100m, NormPeriodWeeks = 4,
                WeeklyNormHours = null, CreatedBy = "tester", CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow, Description = "C2R_B",
            };
            await _repo.CreateAsync(connB, txB, bEntity, monday5);
            await txB.CommitAsync();
        });

        // Bounded wait (no fixed sleep): until B holds any ungranted lock. Precedent for the
        // pg_locks wait: SendConcurrencyTestBase.WaitForWaitersAsync (Approval/SendConcurrencyTests.cs:249).
        string? waitedOn = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (waitedOn is null)
        {
            if (bTask.IsCompleted)
                throw new Xunit.Sdk.XunitException(
                    "B finished before A committed: B never blocked, the test setup is wrong.");
            await using var probe = new NpgsqlConnection(_harness.ConnectionString);
            await probe.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT locktype FROM pg_locks WHERE pid = @pid AND NOT granted LIMIT 1", probe);
            cmd.Parameters.AddWithValue("pid", pidB);
            waitedOn = (string?)await cmd.ExecuteScalarAsync();
            if (waitedOn is null)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(30))
                {
                    await txA.RollbackAsync(); // release B so nothing is left hanging
                    throw new TimeoutException("B never blocked: the test setup is wrong.");
                }
                await Task.Delay(20);
            }
        }
        Console.WriteLine($"AC-C2r: B waited on an ungranted '{waitedOn}' lock."); // informational, not asserted

        await txA.CommitAsync();

        var ex = await Assert.ThrowsAsync<AppliesFromRefusedException>(() => bTask);
        var err = Assert.Single(ex.Errors);
        Assert.Equal("AppliesFrom", err.Field);
        Assert.Equal("BEFORE_PREDECESSOR", err.Code);
        Assert.Equal(new[] { monday19 }, err.NearestValid);

        await using var check = new NpgsqlConnection(_harness.ConnectionString);
        await check.OpenAsync();
        await using var versions = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM position_override_config_versions v
            JOIN position_override_configs c ON c.override_id = v.override_id
            WHERE c.agreement_code = @ac AND c.ok_version = @ok AND c.position_code = @pos
            """, check);
        versions.Parameters.AddWithValue("ac", AgreementCode);
        versions.Parameters.AddWithValue("ok", OkVersion);
        versions.Parameters.AddWithValue("pos", position);
        Assert.Equal(2L, (long)(await versions.ExecuteScalarAsync())!);

        await using var heads = new NpgsqlCommand(
            "SELECT COUNT(*) FROM position_override_configs WHERE agreement_code=@ac AND ok_version=@ok AND position_code=@pos AND status='ACTIVE'",
            check);
        heads.Parameters.AddWithValue("ac", AgreementCode);
        heads.Parameters.AddWithValue("ok", OkVersion);
        heads.Parameters.AddWithValue("pos", position);
        Assert.Equal(0L, (long)(await heads.ExecuteScalarAsync())!);
    }

    // ── Test data builders ────────────────────────────────────────────────────

    private static PositionOverrideConfigEntity NewOverride(
        decimal? maxFlex = 200m, string positionCode = PositionCode) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        PositionCode = positionCode,
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        NormPeriodWeeks = 4,
        WeeklyNormHours = null,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "CON_PO_" + Guid.NewGuid().ToString("N").Substring(0, 8),
    };

    /// <summary>
    /// Seeds a unique <c>position_code</c> row in the <c>positions</c> FK parent so the
    /// 23505-vs-412 distinction test can use a different triple than the default
    /// <c>DEPARTMENT_HEAD</c> seed (avoids cross-test contamination on a shared container).
    /// </summary>
    private async Task SeedPositionAsync(string positionCode)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO positions (position_code, display_label, agreement_code)
            VALUES (@code, @code || ' Test', 'AC')
            ON CONFLICT (position_code) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("code", positionCode);
        await cmd.ExecuteNonQueryAsync();
    }
}
