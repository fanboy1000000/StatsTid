using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2404's 4 converted
/// position-override endpoints (Pattern B — endpoint emits an audit row in the same tx).
/// Each test mirrors the converted endpoint's orchestration with
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> wired in for
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/>; the throw before commit
/// rolls the tx back, and the four post-action assertions pin no leakage.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/admin/position-overrides</c> (<see cref="Create_OutboxFails_RollsBack"/>)</item>
///   <item><c>PUT /api/admin/position-overrides/{overrideId}</c> (<see cref="Update_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/admin/position-overrides/{overrideId}/deactivate</c> (<see cref="Deactivate_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/admin/position-overrides/{overrideId}/activate</c> (<see cref="Activate_OutboxFails_RollsBack"/>)</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideAtomicTests : IAsyncLifetime
{
    private const string AgreementCode = "AC";
    private const string OkVersion = "OK24";
    private const string PositionCode = "DEPARTMENT_HEAD";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private PositionOverrideRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new PositionOverrideRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Create_OutboxFails_RollsBack()
    {
        var entity = NewOverride();
        var newIdPlaceholder = Guid.Empty;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var overrideId = await _repo.CreateAsync(conn, tx, entity);
            newIdPlaceholder = overrideId;
            await _repo.AppendAuditAsync(
                conn, tx, overrideId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

            var @event = new PositionOverrideCreated
            {
                OverrideId = overrideId,
                AgreementCode = entity.AgreementCode,
                OkVersion = entity.OkVersion,
                PositionCode = entity.PositionCode,
            };
            await _outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Filter on a sentinel attribute (description) to identify the attempted insert.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"description = '{entity.Description}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{newIdPlaceholder}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"position-override-{newIdPlaceholder}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"position-override-{newIdPlaceholder}");
    }

    [Fact]
    public async Task Update_OutboxFails_RollsBack()
    {
        // Arrange: a seeded ACTIVE override (max_flex=200).
        var seed = NewOverride(maxFlex: 200m);
        var overrideId = await _repo.CreateAsync(seed);

        // S25 / TASK-2504: v3 UpdateAsync(conn, tx, overrideId, expectedVersion, ...)
        // requires the row's current version. Read it back via the same path the HTTP
        // endpoint uses (GET → If-Match → PUT).
        var preEntity = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Update with sentinel value (max_flex=250).
            var updated = NewOverride(maxFlex: 250m);
            var saveResult = await _repo.UpdateAsync(conn, tx, overrideId, expectedVersion, updated);
            Assert.False(saveResult.IsCreated);
            Assert.Equal(expectedVersion + 1, saveResult.Version);
            await _repo.AppendAuditAsync(
                conn, tx, overrideId, "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: expectedVersion, versionAfter: saveResult.Version);

            var @event = new PositionOverrideUpdated
            {
                OverrideId = overrideId,
                AgreementCode = updated.AgreementCode,
                OkVersion = updated.OkVersion,
                PositionCode = updated.PositionCode,
            };
            await _outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // max_flex_balance stayed at 200 (rolled back); a row at 250 is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND max_flex_balance = 250");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
    }

    [Fact]
    public async Task Deactivate_OutboxFails_RollsBack()
    {
        var seed = NewOverride();
        var overrideId = await _repo.CreateAsync(seed);

        // S25 / TASK-2504: v3 DeactivateAsync(conn, tx, overrideId, expectedVersion, ...)
        // requires the row's current version.
        var preEntity = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var saveResult = await _repo.DeactivateAsync(conn, tx, overrideId, expectedVersion);
            Assert.False(saveResult.IsCreated);
            Assert.Equal("INACTIVE", saveResult.Status);
            await _repo.AppendAuditAsync(
                conn, tx, overrideId, "DEACTIVATED", "{}", null, "tester", "GLOBAL_ADMIN",
                versionBefore: expectedVersion, versionAfter: saveResult.Version);

            var @event = new PositionOverrideDeactivated
            {
                OverrideId = overrideId,
                AgreementCode = seed.AgreementCode,
                OkVersion = seed.OkVersion,
                PositionCode = seed.PositionCode,
            };
            await _outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Status stays ACTIVE (rolled back); a row matching INACTIVE is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND status = 'INACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'DEACTIVATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
    }

    [Fact]
    public async Task Activate_OutboxFails_RollsBack()
    {
        // Seed and then deactivate (no-tx) so the ACTIVATE happens against an INACTIVE row.
        var seed = NewOverride();
        var overrideId = await _repo.CreateAsync(seed);
        await _repo.DeactivateAsync(overrideId);

        // S25 / TASK-2504: v3 ActivateAsync(conn, tx, overrideId, expectedVersion, ...)
        // requires the row's current version.
        var preEntity = await _repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var saveResult = await _repo.ActivateAsync(conn, tx, overrideId, expectedVersion);
            Assert.False(saveResult.IsCreated);
            Assert.Equal("ACTIVE", saveResult.Status);
            await _repo.AppendAuditAsync(
                conn, tx, overrideId, "ACTIVATED", null, "{}", "tester", "GLOBAL_ADMIN",
                versionBefore: expectedVersion, versionAfter: saveResult.Version);

            var @event = new PositionOverrideActivated
            {
                OverrideId = overrideId,
                AgreementCode = seed.AgreementCode,
                OkVersion = seed.OkVersion,
                PositionCode = seed.PositionCode,
            };
            await _outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Status stays INACTIVE (rolled back); a row matching ACTIVE is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND status = 'ACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'ACTIVATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"position-override-{overrideId}");
    }

    // ── Test data builders ────────────────────────────────────────────────────────────

    private static PositionOverrideConfigEntity NewOverride(decimal? maxFlex = 200m) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        PositionCode = PositionCode,
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        FlexCarryoverMax = null,
        NormPeriodWeeks = 4,
        WeeklyNormHours = null,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        // Unique sentinel description per builder call so tests can target the attempted
        // insert by description column rather than override_id (override_id is generated
        // by repo so we don't have it before the call throws).
        Description = "FR_PO_" + Guid.NewGuid().ToString("N").Substring(0, 8),
    };
}
