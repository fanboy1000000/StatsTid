using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2402's 5 converted approval
/// endpoints (Pattern B — endpoint emits an audit row in the same tx). Each test mirrors
/// the converted endpoint's orchestration verbatim with
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> wired in for
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/>; the throw before
/// <see cref="NpgsqlTransaction.CommitAsync(CancellationToken)"/> forces PostgreSQL to
/// roll back the tx and the four post-action assertions pin that the state mutation,
/// audit row, canonical event row, and outbox row are all absent on a fresh connection.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/approval/submit</c> (<see cref="Submit_OutboxFails_RollsBackEntireTransaction"/>)</item>
///   <item><c>POST /api/approval/{periodId}/approve</c> (<see cref="ManagerApprove_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/approval/{periodId}/reject</c> (<see cref="Reject_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/approval/{periodId}/employee-approve</c> (<see cref="EmployeeApprove_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/approval/{periodId}/reopen</c> (<see cref="Reopen_OutboxFails_RollsBack"/>)</item>
/// </list>
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
    public async Task Submit_OutboxFails_RollsBackEntireTransaction()
    {
        // Arrange: no pre-seeded period — Submit creates + transitions in one tx.
        var period = NewPeriod();
        var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";

        // Act: invoke the converted endpoint's orchestration; expect the throw to escape
        // the using-blocks before tx.CommitAsync runs, which forces tx rollback.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var periodId = await _repo.CreateAsync(conn, tx, period);
            await _repo.UpdateStatusAsync(conn, tx, periodId, "SUBMITTED", "tester");
            await _repo.AppendAuditAsync(conn, tx, periodId, "SUBMITTED", "tester", "EMPLOYEE", null);

            var @event = new PeriodSubmitted
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                PeriodType = period.PeriodType,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Assert: no state, no audit, no event, no outbox row.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "approval_periods",
            $"employee_id = '{period.EmployeeId}' AND period_start = '{period.PeriodStart:yyyy-MM-dd}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "approval_audit",
            $"actor_id = 'tester' AND action = 'SUBMITTED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

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
    public async Task EmployeeApprove_OutboxFails_RollsBack()
    {
        // Arrange: a DRAFT period (the only state the employee-approve handler accepts).
        var period = NewPeriod();
        var periodId = await _repo.CreateAsync(period);
        var streamId = $"approval-{period.EmployeeId}-{period.PeriodStart:yyyy-MM-dd}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await _repo.UpdateStatusAsync(conn, tx, periodId, "EMPLOYEE_APPROVED", period.EmployeeId);
            // Endpoint also stamps deadlines in-tx — mirror that.
            var lastDayOfMonth = new DateOnly(period.PeriodEnd.Year, period.PeriodEnd.Month,
                DateTime.DaysInMonth(period.PeriodEnd.Year, period.PeriodEnd.Month));
            await _repo.UpdateDeadlinesAsync(conn, tx, periodId, lastDayOfMonth.AddDays(2), lastDayOfMonth.AddDays(5));
            await _repo.AppendAuditAsync(
                conn, tx, periodId, "SUBMITTED", period.EmployeeId, "EMPLOYEE", "Employee self-approval");

            var @event = new PeriodEmployeeApproved
            {
                PeriodId = periodId,
                EmployeeId = period.EmployeeId,
                OrgId = period.OrgId,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
            };
            await _outbox.EnqueueAsync(conn, tx, streamId, @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "approval_periods",
            $"period_id = '{periodId}' AND status = 'EMPLOYEE_APPROVED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "approval_audit",
            $"period_id = '{periodId}' AND comment = 'Employee self-approval'");
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
