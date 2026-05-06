using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S23 / TASK-2304 — same-day no-op short-circuit.
///
/// <para>
/// When an admin saves a profile with no overridable-field change on the same
/// <c>effective_from</c>, the repository returns
/// <see cref="SaveProfileResult.IsNoOp"/> = true: version unchanged, no
/// UPDATE issued, no audit row, no outbox row. The endpoint mirrors that by
/// skipping audit + outbox emission.
/// </para>
///
/// <para>
/// Codex Step 0b BLOCKER fix (2026-05-06): the no-op detection happens
/// INSIDE the repository AFTER <c>AcquireLockAsync</c> + <c>ValidatePrecondition</c>,
/// NOT at the endpoint pre-tx. A stale caller cannot hide a version mismatch
/// behind an apparent no-op — their <c>If-Match</c> is rejected by the
/// precondition check first. <see cref="StaleIfMatchOnNoOpPath_Returns412_NotNoOp"/>
/// is the load-bearing test for this contract.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileNoOpShortCircuitTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private LocalAgreementProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString, OrgId);
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        _repo = new LocalAgreementProfileRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task NoOp_WhenAllFieldsMatch_VersionUnchanged()
    {
        var monday = new DateOnly(2026, 5, 4);
        var seedResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m, maxFlexBalance: 50m, flexCarryoverMax: 10m));
        Assert.False(seedResult.IsNoOp);
        Assert.Equal(1L, seedResult.Version);

        // Re-save with all fields identical — must be detected as a no-op.
        var candidate = NewProfile(monday, weeklyNormHours: 37m, maxFlexBalance: 50m, flexCarryoverMax: 10m);
        var noOpResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: seedResult.Version, candidate);

        Assert.True(noOpResult.IsNoOp);
        Assert.Equal(seedResult.ProfileId, noOpResult.ProfileId);
        Assert.Equal(seedResult.Version, noOpResult.Version);  // unchanged
    }

    [Fact]
    public async Task NoOp_DoesNotUpdateRow()
    {
        var monday = new DateOnly(2026, 5, 4);
        var seedResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));

        // Capture the predecessor's row identity (created_at is the most sensitive
        // — UpdateInPlaceAsync would not change it, but we want to be 100% sure
        // no UPDATE fired at all by snapshotting then re-reading).
        var beforeNoOp = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(beforeNoOp);

        var candidate = NewProfile(monday, weeklyNormHours: 37m);
        await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: seedResult.Version, candidate);

        var afterNoOp = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(afterNoOp);
        Assert.Equal(beforeNoOp!.Version, afterNoOp!.Version);
        Assert.Equal(beforeNoOp.CreatedAt, afterNoOp.CreatedAt);
        Assert.Equal(beforeNoOp.WeeklyNormHours, afterNoOp.WeeklyNormHours);
    }

    [Fact]
    public async Task NoOp_AnyFieldDifferent_PerformsUpdate()
    {
        var monday = new DateOnly(2026, 5, 4);
        var seedResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));

        // Different WeeklyNormHours -> not a no-op, version bumps.
        var candidate = NewProfile(monday, weeklyNormHours: 36m);
        var updateResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: seedResult.Version, candidate);

        Assert.False(updateResult.IsNoOp);
        Assert.Equal(seedResult.ProfileId, updateResult.ProfileId);  // same row
        Assert.Equal(seedResult.Version + 1, updateResult.Version);  // bumped
    }

    [Fact]
    public async Task NoOp_NullableFieldFlippedToNull_PerformsUpdate()
    {
        var monday = new DateOnly(2026, 5, 4);
        var seedResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m, maxFlexBalance: 50m));

        // Setting MaxFlexBalance from 50 to null is a real change, not a no-op.
        var candidate = NewProfile(monday, weeklyNormHours: 37m, maxFlexBalance: null);
        var updateResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: seedResult.Version, candidate);

        Assert.False(updateResult.IsNoOp);
        Assert.Equal(seedResult.Version + 1, updateResult.Version);
    }

    [Fact]
    public async Task StaleIfMatchOnNoOpPath_Returns412_NotNoOp()
    {
        // Codex Step 0b BLOCKER fix verification: a caller with a stale If-Match
        // who happens to send the SAME field values as the current row must STILL
        // hit OptimisticConcurrencyException, not get a silent 200/no-op.
        var monday = new DateOnly(2026, 5, 4);

        // Admin A1 seeds at v1 then bumps to v2 with a real change.
        var a1Seed = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));
        var a1Update = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: a1Seed.Version,
            NewProfile(monday, weeklyNormHours: 36m));
        Assert.Equal(2L, a1Update.Version);

        // Admin A2 had read at v1; sends a save with the SAME fields as the
        // current row (37m → 37m would be a no-op IF the version matched).
        // The repo MUST throw OptimisticConcurrencyException on the v1 If-Match,
        // not silently short-circuit, because the row has advanced.
        var a2Stale = NewProfile(monday, weeklyNormHours: 36m);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => _repo.SupersedeAndCreateAsync(expectedCurrentVersion: a1Seed.Version, a2Stale));

        // Confirm: still v2, profile_id unchanged.
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(a1Update.ProfileId, current!.ProfileId);
        Assert.Equal(2L, current.Version);
    }

    [Fact]
    public async Task NoOp_NoOutboxRowEmitted_ByEndToEndIfMatchPath()
    {
        // Repository-only test: the in-tx overload is what the endpoint uses.
        // Verify directly that wrapping a no-op call in the endpoint's tx pattern
        // produces zero outbox rows (the endpoint guards EnqueueAsync on IsNoOp).
        // Here we simulate the endpoint's tx but only do the repo call.
        var monday = new DateOnly(2026, 5, 4);
        var seedResult = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));

        var outboxRowsBefore = await CountOutboxRowsAsync();

        // Same-day, same fields → no-op.
        var candidate = NewProfile(monday, weeklyNormHours: 37m);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var result = await _repo.SupersedeAndCreateAsync(
                conn, tx, expectedCurrentVersion: seedResult.Version, candidate);
            Assert.True(result.IsNoOp);
            // The endpoint would skip outbox.EnqueueAsync here. Repo doesn't
            // touch outbox itself, so this asserts the repo's own contract:
            // even with no caller-skip, no outbox rows appear from the repo path.
            await tx.CommitAsync();
        }

        var outboxRowsAfter = await CountOutboxRowsAsync();
        Assert.Equal(outboxRowsBefore, outboxRowsAfter);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static LocalAgreementProfile NewProfile(
        DateOnly effectiveFrom,
        decimal? weeklyNormHours = null,
        decimal? maxFlexBalance = null,
        decimal? flexCarryoverMax = null,
        decimal? maxOvertimeHoursPerPeriod = null,
        bool? overtimeRequiresPreApproval = null) => new()
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = effectiveFrom,
            WeeklyNormHours = weeklyNormHours,
            MaxFlexBalance = maxFlexBalance,
            FlexCarryoverMax = flexCarryoverMax,
            MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = overtimeRequiresPreApproval,
            CreatedBy = "admin",
            CreatedAt = DateTime.UtcNow,
            Version = 1,
        };

    private async Task<long> CountOutboxRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM outbox_events", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
