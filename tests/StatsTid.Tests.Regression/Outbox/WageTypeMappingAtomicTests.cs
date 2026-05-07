using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2405's 3 converted
/// wage-type-mapping endpoints (Pattern B — endpoint emits an audit row in the same tx).
/// Each test mirrors the converted endpoint's orchestration with
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> wired in for
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/>; the throw before commit
/// rolls the tx back, and the four post-action assertions pin no leakage.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/admin/wage-type-mappings</c> (<see cref="Create_OutboxFails_RollsBack"/>)</item>
///   <item><c>PUT /api/admin/wage-type-mappings</c> (<see cref="Update_OutboxFails_RollsBack"/>)</item>
///   <item><c>DELETE /api/admin/wage-type-mappings</c> (<see cref="Delete_OutboxFails_RollsBack"/>)</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingAtomicTests : IAsyncLifetime
{
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";
    private const string Position = "";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private WageTypeMappingRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new WageTypeMappingRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Create_OutboxFails_RollsBack()
    {
        var mapping = NewMapping("FR_WTM_C_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var streamId = $"wage-type-mapping-{AgreementCode}-{OkVersion}-{mapping.TimeType}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var success = await _repo.CreateAsync(conn, tx, mapping);
            Assert.True(success);
            await _repo.AppendAuditAsync(
                conn, tx,
                mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position,
                "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

            var @event = new WageTypeMappingCreated
            {
                TimeType = mapping.TimeType,
                WageType = mapping.WageType,
                OkVersion = mapping.OkVersion,
                AgreementCode = mapping.AgreementCode,
                Position = mapping.Position,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "wage_type_mappings",
            $"time_type = '{mapping.TimeType}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{mapping.TimeType}' AND action = 'CREATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task Update_OutboxFails_RollsBack()
    {
        // Arrange: a seeded mapping with known wage_type. UPDATE will mutate to a sentinel
        // WageType so the rollback assertion can target absence of the sentinel.
        var seed = NewMapping("FR_WTM_U_" + Guid.NewGuid().ToString("N").Substring(0, 8), wageType: "SLS_0110");
        await _repo.CreateAsync(seed);
        var streamId = $"wage-type-mapping-{AgreementCode}-{OkVersion}-{seed.TimeType}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var updated = NewMapping(seed.TimeType, wageType: "SLS_9999");
            var success = await _repo.UpdateAsync(conn, tx, updated);
            Assert.True(success);
            await _repo.AppendAuditAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN");

            var @event = new WageTypeMappingUpdated
            {
                TimeType = updated.TimeType,
                WageType = updated.WageType,
                OkVersion = updated.OkVersion,
                AgreementCode = updated.AgreementCode,
                Position = updated.Position,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // wage_type stays at SLS_0110 (rolled back); a row at SLS_9999 is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "wage_type_mappings",
            $"time_type = '{seed.TimeType}' AND wage_type = 'SLS_9999'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{seed.TimeType}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task Delete_OutboxFails_RollsBack()
    {
        // Arrange: a seeded mapping that DELETE will attempt to remove. The rollback must
        // leave the mapping intact.
        var seed = NewMapping("FR_WTM_D_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        await _repo.CreateAsync(seed);
        var streamId = $"wage-type-mapping-{AgreementCode}-{OkVersion}-{seed.TimeType}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var success = await _repo.DeleteAsync(
                conn, tx, seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position);
            Assert.True(success);
            await _repo.AppendAuditAsync(
                conn, tx,
                seed.TimeType, seed.OkVersion, seed.AgreementCode, seed.Position,
                "DELETED", "{}", null, "tester", "GLOBAL_ADMIN");

            var @event = new WageTypeMappingDeleted
            {
                TimeType = seed.TimeType,
                OkVersion = seed.OkVersion,
                AgreementCode = seed.AgreementCode,
                Position = seed.Position,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // The seed row must STILL exist post-rollback. We assert the absence of the
        // delete by counting; the load-bearing rollback witness is the audit row absence.
        await using (var verifyConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await verifyConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @t", verifyConn);
            cmd.Parameters.AddWithValue("t", seed.TimeType);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(1L, count);
        }
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{seed.TimeType}' AND action = 'DELETED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    // ── Test data builders ────────────────────────────────────────────────────────────

    private static WageTypeMapping NewMapping(string timeType, string wageType = "SLS_0110") => new()
    {
        TimeType = timeType,
        WageType = wageType,
        OkVersion = OkVersion,
        AgreementCode = AgreementCode,
        Position = Position,
        Description = "forced-rollback-test",
    };
}
