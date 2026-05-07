using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2406's 2 converted overtime
/// endpoints. Pattern C — these endpoints do NOT emit an audit row in the same tx, so
/// only the state mutation, canonical event, and outbox-row assertions apply.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/overtime/pre-approval</c> — <see cref="OvertimeEndpoints"/> L164
///         (<see cref="CreatePreApproval_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/overtime/{employeeId}/compensate</c> — <see cref="OvertimeEndpoints"/> L328
///         (<see cref="AdjustBalance_OutboxFails_RollsBack"/>)</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OvertimeAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private OvertimePreApprovalRepository _preApprovalRepo = null!;
    private OvertimeBalanceRepository _balanceRepo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _preApprovalRepo = new OvertimePreApprovalRepository(_harness.Factory);
        _balanceRepo = new OvertimeBalanceRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task CreatePreApproval_OutboxFails_RollsBack()
    {
        var approval = new OvertimePreApproval
        {
            Id = Guid.NewGuid(),
            EmployeeId = "EMP_FR_OT_PA_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            MaxHours = 20m,
            Status = "PENDING",
            Reason = "Forced rollback test",
        };
        var streamId = $"overtime-preapproval-{approval.Id}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _preApprovalRepo.CreateAsync(conn, tx, approval);

            var @event = new OvertimePreApprovalCreated
            {
                EmployeeId = approval.EmployeeId,
                PeriodStart = approval.PeriodStart,
                PeriodEnd = approval.PeriodEnd,
                MaxHours = approval.MaxHours,
                Status = "PENDING",
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Pattern C — no audit assertion. State / event / outbox absence still required.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "overtime_pre_approvals",
            $"id = '{approval.Id}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task AdjustBalance_OutboxFails_RollsBack()
    {
        // Arrange: a seeded balance row with paid_out=0; the AdjustPaidOut call will try
        // to bump it by +5 in the test tx and the outbox throw must roll that back.
        const string employeeId = "EMP_FR_OT_BAL";
        const int year = 2026;
        await _balanceRepo.UpsertAsync(new OvertimeBalance
        {
            BalanceId = Guid.NewGuid(),
            EmployeeId = employeeId,
            AgreementCode = "HK",
            PeriodYear = year,
            Accumulated = 50m,
            PaidOut = 0m,
            AfspadseringUsed = 0m,
            CompensationModel = "UDBETALING",
        });
        var streamId = $"overtime-balance-{employeeId}-{year}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var (success, _) = await _balanceRepo.AdjustPaidOutAsync(conn, tx, employeeId, year, 5m);
            Assert.True(success);

            var @event = new OvertimeCompensationApplied
            {
                EmployeeId = employeeId,
                PeriodYear = year,
                Hours = 5m,
                ConvertedHours = 5m,
                CompensationType = "PAYOUT",
                OvertimeType = "OVERTIME",
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Pattern C — no audit assertion. paid_out must still be 0 (rolled back); a row
        // matching paid_out > 0 is the absence-witness for the rollback.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "overtime_balances",
            $"employee_id = '{employeeId}' AND period_year = {year} AND paid_out > 0");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }
}
