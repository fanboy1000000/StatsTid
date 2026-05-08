using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S26 / TASK-2608 atomic-emission tests for Phase 2 / TASK-2607's converted overtime
/// approve / reject endpoints (PUT /api/overtime/pre-approval/{id}/approve and .../reject).
/// Pattern C — these endpoints do NOT emit a separate audit row; the new event types
/// <see cref="OvertimePreApprovalApproved"/> + <see cref="OvertimePreApprovalRejected"/>
/// (TASK-2602) carry actor metadata via <c>DomainEventBase</c> and are the audit-of-record
/// for the state change. Pre-S26 the status flipped silently with NO event emitted at all
/// (silent state-change bug); post-S26 the status change + new-event emission ride a
/// single tx so a process crash between the UPDATE and event emission cannot leave the
/// row in APPROVED / REJECTED state without its corresponding event.
///
/// <para>
/// Two tests:
/// <list type="bullet">
///   <item><description><see cref="Approve_EmitsApprovedEvent_AndUpdatesStatus"/> — happy
///   path with the REAL outbox INSERT shape (raw SQL, mirroring the
///   <see cref="Concurrency.AgreementConfigConcurrencyTests"/> precedent at
///   <c>InsertOutboxEventAsync</c>). Verifies the row's status is APPROVED post-commit
///   AND a single OvertimePreApprovalApproved outbox row exists on the canonical stream.</description></item>
///   <item><description><see cref="Reject_OutboxFails_RollsBackStatusChange"/> — rollback
///   path with <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/>. Verifies the
///   row's status remains PENDING (rolled back) and ZERO outbox rows exist on the stream.</description></item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OvertimeApproveRejectAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private OvertimePreApprovalRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _throwingOutbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new OvertimePreApprovalRepository(_harness.Factory);
        _throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Happy path: approve a PENDING pre-approval. Status flips to APPROVED in the same
    /// tx that emits the new <see cref="OvertimePreApprovalApproved"/> event to the
    /// outbox. Asserts the post-commit state: status='APPROVED' AND one outbox row of
    /// type 'OvertimePreApprovalApproved' on stream <c>overtime-preapproval-{id}</c>
    /// with PreApprovalId/EmployeeId/ApprovedBy correctly set in event_payload.
    /// </summary>
    [Fact]
    public async Task Approve_EmitsApprovedEvent_AndUpdatesStatus()
    {
        // Seed a PENDING pre-approval.
        var employeeId = "EMP_FR_OT_AP_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var approval = new OvertimePreApproval
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            MaxHours = 20m,
            Status = "PENDING",
            Reason = "Atomic-emission happy-path test",
        };
        await _repo.CreateAsync(approval);
        var streamId = $"overtime-preapproval-{approval.Id}";

        // Mirror the endpoint's L266 orchestration: status UPDATE + outbox enqueue in tx.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await _repo.UpdateStatusAsync(conn, tx, approval.Id, "APPROVED", "manager", "Approved");
                // Real outbox INSERT (raw SQL — mirrors AgreementConfigConcurrencyTests'
                // InsertOutboxEventAsync precedent). Stamps event_type so we can verify it.
                await using (var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO outbox_events (service_id, stream_id, event_id, event_type, event_payload, actor_id, actor_role)
                    VALUES ('backend-api', @stream, @eventId, 'OvertimePreApprovalApproved', @payload::jsonb, 'manager', 'LEADER')
                    """, conn, tx))
                {
                    cmd.Parameters.AddWithValue("stream", streamId);
                    cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
                    cmd.Parameters.AddWithValue("payload",
                        $"{{\"PreApprovalId\":\"{approval.Id}\",\"EmployeeId\":\"{employeeId}\",\"ApprovedBy\":\"manager\"}}");
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Status post-commit is APPROVED.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT status FROM overtime_pre_approvals WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", approval.Id);
            var status = (string?)await cmd.ExecuteScalarAsync();
            Assert.Equal("APPROVED", status);
        }

        // Outbox holds exactly one row for the stream, of type OvertimePreApprovalApproved.
        var outboxRows = await ReadOutboxRowsAsync(streamId);
        Assert.Single(outboxRows);
        Assert.Equal("OvertimePreApprovalApproved", outboxRows[0].EventType);
        Assert.Contains(approval.Id.ToString(), outboxRows[0].Payload);
        Assert.Contains(employeeId, outboxRows[0].Payload);
        Assert.Contains("\"ApprovedBy\":\"manager\"", outboxRows[0].Payload);
    }

    /// <summary>
    /// Rollback path: reject a PENDING pre-approval, but the outbox enqueue throws. The
    /// surrounding tx must roll back: row stays PENDING, outbox stream is empty. Pre-S26
    /// silent-state-change bug would have left the row in REJECTED with no event emitted;
    /// post-S26 forced-rollback contract pins the all-or-nothing semantics.
    /// </summary>
    [Fact]
    public async Task Reject_OutboxFails_RollsBackStatusChange()
    {
        var employeeId = "EMP_FR_OT_RJ_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var approval = new OvertimePreApproval
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            MaxHours = 20m,
            Status = "PENDING",
            Reason = "Atomic-emission forced-rollback test",
        };
        await _repo.CreateAsync(approval);
        var streamId = $"overtime-preapproval-{approval.Id}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await _repo.UpdateStatusAsync(conn, tx, approval.Id, "REJECTED", "manager", "Rejected");
                var @event = new OvertimePreApprovalRejected
                {
                    PreApprovalId = approval.Id,
                    EmployeeId = employeeId,
                    RejectedBy = "manager",
                    Reason = "Rejected",
                };
                await _throwingOutbox.EnqueueAsync(conn, tx, streamId, @event);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Status remains PENDING (rolled back). The state-witness for the rollback is
        // a row matching status='REJECTED' for our test id — that row must NOT exist.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "overtime_pre_approvals",
            $"id = '{approval.Id}' AND status = 'REJECTED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    /// <summary>
    /// Reads outbox_events rows for a given stream — used by the happy-path test to
    /// verify the event_type, event_id, and event_payload of the emitted row.
    /// </summary>
    private async Task<List<(string EventType, string Payload)>> ReadOutboxRowsAsync(string streamId)
    {
        var rows = new List<(string, string)>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_type, event_payload::text FROM outbox_events WHERE stream_id = @stream ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("stream", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }
}
