using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S25 / TASK-2508 concurrency regression tests for the three v3 mutating endpoints on
/// <c>agreement_configs</c> (per ADR-019 D2/D6/D7/D8). Verifies the row-version optimistic
/// concurrency contract at the repository surface and the admin-strict If-Match parser
/// at the helper surface. Direct-orchestration shape mirroring <see cref="Config.ProfileAuditTests"/>
/// + <see cref="Outbox.AgreementConfigAtomicTests"/> precedent (no <c>WebApplicationFactory&lt;Program&gt;</c>
/// — HTTP-surface harness deferred to Phase 4d per S24 carry-forward).
///
/// <para>
/// Test slots (7 total):
///   <list type="bullet">
///     <item>3 stale-If-Match → <see cref="OptimisticConcurrencyException"/> tests
///       (PUT update DRAFT / publish / archive)</item>
///     <item>3 missing-If-Match → <see cref="EtagHeaderHelper.TryParseIfMatch"/> false-return tests
///       (PUT update DRAFT / publish / archive)</item>
///     <item>1 end-to-end ETag-cycle test (CREATE → version=1, UPDATE → version=2 read-back)</item>
///   </list>
/// Audit version-transition coverage lives in <see cref="AuditVersionTransitionTests"/>
/// (per ADR-019 D8 — cross-resource invariant).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigConcurrencyTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AgreementConfigRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        await ConcurrencyTestSchema.ApplyAsync(_harness.ConnectionString);
        _repo = new AgreementConfigRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ─── Stale-If-Match (412 contract) — repo surface ─────────────────────────

    [Fact]
    public async Task UpdateDraft_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once via v3 to take version 1 → 2; then
        // attempt a SECOND v3 update with the STALE expectedVersion=1. Repo must throw
        // OptimisticConcurrencyException carrying (Expected=1, Actual=2). Endpoint maps to 412.
        var initial = NewConfig(weeklyNorm: 37m);
        var configId = await _repo.CreateAsync(initial);

        // First update — succeeds, version 1 → 2.
        var updateA = NewConfig(weeklyNorm: 38m);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var result = await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, updateA);
            await tx.CommitAsync();
            Assert.Equal(2L, result.Version);
        }

        // Second update with STALE expectedVersion=1 — must throw.
        var updateB = NewConfig(weeklyNorm: 39m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, updateB);
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Publish_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once (e.g. via v3 update) to take version 1 → 2;
        // then attempt to publish with STALE expectedVersion=1. Repo must throw.
        var draft = NewConfig();
        var configId = await _repo.CreateAsync(draft, "DRAFT");

        // Bump version to 2 via an UpdateDraftAsync call.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }

        // Stale publish with expectedVersion=1 — must throw.
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.PublishAsync(conn, tx, configId, expectedVersion: 1, actorId: "tester");
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Archive_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once via v3 update to take version 1 → 2;
        // attempt archive with STALE expectedVersion=1. Repo must throw.
        var draft = NewConfig();
        var configId = await _repo.CreateAsync(draft, "DRAFT");

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.ArchiveAsync(conn, tx, configId, expectedVersion: 1, actorId: "tester");
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    // ─── Missing-If-Match (428 contract) — helper surface ─────────────────────

    [Fact]
    public void UpdateDraft_MissingIfMatch_HelperRejects()
    {
        // The 428 path is hit at the endpoint, not the repo. Verify the helper surface
        // rejects requests with no If-Match header in admin-strict mode (mirrors the
        // PUT /api/agreement-configs/{configId} endpoint's first-line precondition check).
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(
            request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/agreement-configs/{configId}/publish missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/agreement-configs/{configId}/archive missing-precondition path.
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
        var initial = NewConfig(weeklyNorm: 37m);
        var configId = await _repo.CreateAsync(initial);

        var afterCreate = await _repo.GetByIdAsync(configId);
        Assert.NotNull(afterCreate);
        Assert.Equal(1L, afterCreate!.Version);

        SaveAgreementConfigResult updateResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            updateResult = await _repo.UpdateDraftAsync(
                conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }
        Assert.Equal(2L, updateResult.Version);
        Assert.False(updateResult.IsCreated);
        Assert.Null(updateResult.ArchivedId);

        var afterUpdate = await _repo.GetByIdAsync(configId);
        Assert.NotNull(afterUpdate);
        Assert.Equal(2L, afterUpdate!.Version);
        Assert.Equal(38m, afterUpdate.WeeklyNormHours);
    }

    // ── Test data builders ────────────────────────────────────────────────────

    /// <summary>
    /// Construct a synthetic <see cref="HttpRequest"/> with no If-Match / If-None-Match
    /// headers — drives <see cref="EtagHeaderHelper.TryParseIfMatch"/>'s missing-precondition
    /// branch.
    /// </summary>
    private static HttpRequest NewRequestWithoutIfMatch()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        return ctx.Request;
    }

    private static AgreementConfigEntity NewConfig(decimal weeklyNorm = 37m) => new()
    {
        ConfigId = Guid.Empty,
        // Unique agreement_code per call so concurrent test fixtures don't collide.
        AgreementCode = "CON_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8),
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
        Description = "concurrency-test",
    };
}
