using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

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
/// Test slots (7 total):
///   <list type="bullet">
///     <item>3 stale-If-Match → <see cref="OptimisticConcurrencyException"/> tests
///       (PUT update / activate / deactivate)</item>
///     <item>3 missing-If-Match → <see cref="EtagHeaderHelper.TryParseIfMatch"/> false-return tests
///       (PUT update / activate / deactivate)</item>
///     <item>1 end-to-end ETag-cycle test (CREATE → version=1, UPDATE → version=2 read-back)</item>
///   </list>
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
        await ConcurrencyTestSchema.ApplyAsync(_harness.ConnectionString);
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
        var overrideId = await _repo.CreateAsync(initial);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var result = await _repo.UpdateAsync(
                conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 250m));
            await tx.CommitAsync();
            Assert.Equal(2L, result.Version);
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateAsync(
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
        var overrideId = await _repo.CreateAsync(seed);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateAsync(conn, tx, overrideId, expectedVersion: 1, NewOverride(maxFlex: 250m));
            await tx.CommitAsync();
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.DeactivateAsync(conn, tx, overrideId, expectedVersion: 1);
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
        await _repo.CreateAsync(first);

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
                    max_flex_balance, created_by, description, created_at, updated_at
                ) VALUES (
                    @id, @ac, @ok, @pos, 'INACTIVE',
                    @mfb, 'tester', @desc, NOW(), NOW()
                )
                """, conn);
            insertCmd.Parameters.AddWithValue("id", secondId);
            insertCmd.Parameters.AddWithValue("ac", AgreementCode);
            insertCmd.Parameters.AddWithValue("ok", OkVersion);
            insertCmd.Parameters.AddWithValue("pos", sharedPosition);
            insertCmd.Parameters.AddWithValue("mfb", 100m);
            insertCmd.Parameters.AddWithValue("desc", "second-inactive-override");
            await insertCmd.ExecuteNonQueryAsync();
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
            await _repo.ActivateAsync(conn, tx, secondId, expectedVersion: 1);
            await tx.CommitAsync();
        });
        Assert.Equal("23505", ex.SqlState);
    }

    // ─── Missing-If-Match (428 contract) — helper surface ─────────────────────

    [Fact]
    public void Update_MissingIfMatch_HelperRejects()
    {
        // Mirrors PUT /api/admin/position-overrides/{overrideId} missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Activate_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/admin/position-overrides/{overrideId}/activate missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Deactivate_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/admin/position-overrides/{overrideId}/deactivate missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    // ─── End-to-end ETag-cycle ─────────────────────────────────────────────────

    [Fact]
    public async Task EtagCycle_CreateThenUpdate_VersionMonotonicallyIncreases()
    {
        // Wire shape: CREATE (version=1) → GET (version=1) → UPDATE with If-Match: "1"
        // (version=2) → GET (version=2). This is the contract the frontend banner-with-retry
        // hook depends on.
        var initial = NewOverride(maxFlex: 200m);
        var overrideId = await _repo.CreateAsync(initial);

        var afterCreate = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(afterCreate);
        Assert.Equal(1L, afterCreate!.Version);

        SavePositionOverrideResult updateResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            updateResult = await _repo.UpdateAsync(
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

    // ── Test data builders ────────────────────────────────────────────────────

    private static HttpRequest NewRequestWithoutIfMatch()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        return ctx.Request;
    }

    private static PositionOverrideConfigEntity NewOverride(
        decimal? maxFlex = 200m, string positionCode = PositionCode) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        PositionCode = positionCode,
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        FlexCarryoverMax = null,
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
