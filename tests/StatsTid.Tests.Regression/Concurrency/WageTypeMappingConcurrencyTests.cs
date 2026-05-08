using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S25 / TASK-2508 concurrency regression tests for the two v3 mutating endpoints on
/// <c>wage_type_mappings</c> (per ADR-019 D2/D7/D8). Verifies the row-version optimistic-
/// concurrency contract at the repository surface and the admin-strict If-Match parser at
/// the helper surface.
///
/// <para>
/// Test slots (5 total):
///   <list type="bullet">
///     <item>2 stale-If-Match → <see cref="OptimisticConcurrencyException"/> tests
///       (PUT update / DELETE)</item>
///     <item>2 missing-If-Match → <see cref="EtagHeaderHelper.TryParseIfMatch"/> false-return tests
///       (PUT update / DELETE)</item>
///     <item>1 end-to-end ETag-cycle test (CREATE → version=1, UPDATE → version=2 read-back)</item>
///   </list>
/// Audit version-transition coverage lives in <see cref="AuditVersionTransitionTests"/>
/// (per ADR-019 D8 — cross-resource invariant; DELETE records (version, version) per D8).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingConcurrencyTests : IAsyncLifetime
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

    // ─── Stale-If-Match (412 contract) — repo surface ─────────────────────────

    [Fact]
    public async Task Update_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a mapping; bump it once via v3 to take version 1 → 2; attempt a SECOND v3
        // update with the STALE expectedVersion=1.
        var seed = NewMapping(timeType: "WK_U_" + Guid.NewGuid().ToString("N").Substring(0, 6),
            wageType: "SLS_0110");
        await _repo.CreateAsync(seed);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var result = await _repo.UpdateAsync(
                conn, tx, NewMapping(seed.TimeType, wageType: "SLS_2222"), expectedVersion: 1);
            await tx.CommitAsync();
            Assert.Equal(2L, result.Version);
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateAsync(
                conn, tx, NewMapping(seed.TimeType, wageType: "SLS_3333"), expectedVersion: 1);
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Delete_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a mapping; bump it once via v3 update to version 1 → 2; attempt DELETE with
        // STALE expectedVersion=1.
        var seed = NewMapping(timeType: "WK_D_" + Guid.NewGuid().ToString("N").Substring(0, 6),
            wageType: "SLS_0110");
        await _repo.CreateAsync(seed);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateAsync(
                conn, tx, NewMapping(seed.TimeType, wageType: "SLS_4444"), expectedVersion: 1);
            await tx.CommitAsync();
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.DeleteAsync(
                conn, tx, seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                expectedVersion: 1);
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    // ─── Missing-If-Match (428 contract) — helper surface ─────────────────────

    [Fact]
    public void Update_MissingIfMatch_HelperRejects()
    {
        // Mirrors PUT /api/admin/wage-type-mappings missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_MissingIfMatch_HelperRejects()
    {
        // Mirrors DELETE /api/admin/wage-type-mappings missing-precondition path.
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
        // (version=2) → GET (version=2).
        var seed = NewMapping(timeType: "WK_E_" + Guid.NewGuid().ToString("N").Substring(0, 6),
            wageType: "SLS_0110");
        await _repo.CreateAsync(seed);

        var afterCreate = await _repo.GetByKeyAsync(
            seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position);
        Assert.NotNull(afterCreate);
        Assert.Equal(1L, afterCreate!.Version);

        SaveWageTypeMappingResult updateResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            updateResult = await _repo.UpdateAsync(
                conn, tx, NewMapping(seed.TimeType, wageType: "SLS_5555"), expectedVersion: 1);
            await tx.CommitAsync();
        }
        Assert.Equal(2L, updateResult.Version);
        Assert.False(updateResult.IsCreated);
        Assert.Equal("SLS_5555", updateResult.Mapping.WageType);

        var afterUpdate = await _repo.GetByKeyAsync(
            seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position);
        Assert.NotNull(afterUpdate);
        Assert.Equal(2L, afterUpdate!.Version);
        Assert.Equal("SLS_5555", afterUpdate.WageType);
    }

    // ── Test data builders ────────────────────────────────────────────────────

    private static HttpRequest NewRequestWithoutIfMatch()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        return ctx.Request;
    }

    private static WageTypeMapping NewMapping(string timeType, string wageType = "SLS_0110") => new()
    {
        TimeType = timeType,
        WageType = wageType,
        OkVersion = OkVersion,
        AgreementCode = AgreementCode,
        Position = Position,
        Description = "concurrency-test",
    };
}
