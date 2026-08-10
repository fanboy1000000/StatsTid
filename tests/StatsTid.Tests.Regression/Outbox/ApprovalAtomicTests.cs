using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2402's converted approval
/// endpoints (Pattern B — endpoint emits an audit row in the same tx). Each test mirrors
/// the converted endpoint's orchestration verbatim with
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> wired in for
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/>; the throw before
/// <see cref="NpgsqlTransaction.CommitAsync(CancellationToken)"/> forces PostgreSQL to
/// roll back the tx and the four post-action assertions pin that the state mutation,
/// audit row, canonical event row, and outbox row are all absent on a fresh connection.
///
/// <para>
/// Endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/approval/{periodId}/approve</c> (<see cref="ManagerApprove_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/approval/{periodId}/reject</c> (<see cref="Reject_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/approval/{periodId}/reopen</c> (<see cref="Reopen_OutboxFails_RollsBack"/>)</item>
/// </list>
/// </para>
///
/// <para>
/// S127 / TASK-12708 (AC-17): the former <c>Submit_OutboxFails_RollsBackEntireTransaction</c> (the
/// retired <c>POST /api/approval/submit</c>) and <c>EmployeeApprove_OutboxFails_RollsBack</c> (a stale
/// inline mirror of the by-id path) were REMOVED. Both mirrored the endpoint orchestration inline and
/// stayed green after <c>/submit</c> was retired and the send path was rewritten into the one shared
/// <c>ExecuteSendAsync</c> command. Their replacement drives the REAL send routes (both adapters)
/// through a throwing-outbox host in <see cref="Approval.SendAtomicityTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ApprovalAtomicTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ApprovalPeriodRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new ApprovalPeriodRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ManagerApprove_OutboxFails_RollsBack()
    {
        // Arrange: seed a SUBMITTED period — pre-existing state via the no-tx repo path.
        var period = NewPeriod();
        var periodId = await _repo.CreateAsync(period);
        await _repo.UpdateStatusAsync(periodId, "SUBMITTED", "submitter");
        var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.UpdateStatusAsync(conn, tx, periodId, "APPROVED", "manager");
            await _repo.AppendAuditAsync(conn, tx, periodId, "APPROVED", "manager", "LEADER", null);

            var @event = new PeriodApproved
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                ApprovedBy = "manager",
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Assert: status is still SUBMITTED (rollback reverted), no APPROVED audit row,
        // no event row, no outbox row.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "approval_periods",
            $"period_id = '{periodId}' AND status = 'APPROVED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "approval_audit",
            $"period_id = '{periodId}' AND action = 'APPROVED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task Reject_OutboxFails_RollsBack()
    {
        var period = NewPeriod();
        var periodId = await _repo.CreateAsync(period);
        await _repo.UpdateStatusAsync(periodId, "SUBMITTED", "submitter");
        var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.UpdateStatusAsync(conn, tx, periodId, "REJECTED", "manager", "Insufficient detail");
            await _repo.AppendAuditAsync(conn, tx, periodId, "REJECTED", "manager", "LEADER", "Insufficient detail");

            var @event = new PeriodRejected
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                RejectedBy = "manager",
                RejectionReason = "Insufficient detail",
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "approval_periods",
            $"period_id = '{periodId}' AND status = 'REJECTED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "approval_audit",
            $"period_id = '{periodId}' AND action = 'REJECTED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    [Fact]
    public async Task Reopen_OutboxFails_RollsBack()
    {
        // Arrange: an EMPLOYEE_APPROVED period (the only state the reopen handler accepts).
        var period = NewPeriod();
        var periodId = await _repo.CreateAsync(period);
        await _repo.UpdateStatusAsync(periodId, "EMPLOYEE_APPROVED", period.EmployeeId);
        var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.UpdateStatusAsync(conn, tx, periodId, "DRAFT", "manager");
            await _repo.AppendAuditAsync(conn, tx, periodId, "REOPENED", "manager", "LEADER", "Re-evaluate hours");

            var @event = new PeriodReopened
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                Reason = "Re-evaluate hours",
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Status remains EMPLOYEE_APPROVED post-rollback (the seed value), so a row matching
        // status='DRAFT' for this period_id is the absence-witness for the rollback.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "approval_periods",
            $"period_id = '{periodId}' AND status = 'DRAFT'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "approval_audit",
            $"period_id = '{periodId}' AND action = 'REOPENED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    // ── Test data builders ────────────────────────────────────────────────────────────

    private static ApprovalPeriod NewPeriod() => new()
    {
        PeriodId = Guid.NewGuid(),
        EmployeeId = "EMP_FR_AP_" + Guid.NewGuid().ToString("N").Substring(0, 8),
        OrgId = OrgId,
        PeriodStart = new DateOnly(2026, 5, 1),
        PeriodEnd = new DateOnly(2026, 5, 31),
        PeriodType = "MONTHLY",
        Status = "DRAFT",
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
    };
}
