using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Security;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S147 / TASK-14704-T (SEC-053, AC-053-3) — the four-eyes rule holds under a RACE: a month that is reopened
/// and re-sent by the would-be approver AFTER the endpoint's early check read it must still never end up
/// APPROVED (or REJECTED) by its sender.
///
/// <para><b>Plain language.</b> The endpoint reads the month, sees "sent by the employee, you are a different
/// person, fine", and goes on to take its locks. In that window HR user B could reopen the month and re-send
/// it — now B is the sender — and then B's own approve, which already passed the check, would sign the month
/// B just sent. The in-transaction predicate on the write (the sender is not the actor, evaluated on the
/// LOCKED row) closes that window; these facts hold the window open on purpose and prove it.</para>
///
/// <para><b>How the window is forced, by observation and never by a sleep.</b> A side connection HOLDS the
/// STY02 <c>reporting-org-</c> advisory lock — the lock the approve / reject takes as its first in-transaction
/// statement, AFTER the early four-eyes check. B's request is started; the test polls <c>pg_locks</c> until a
/// backend is observed WAITING on that key (so the early check has provably already run and passed). While B is
/// parked, the net effect of "reopen, then re-send as B" is committed on another connection (a direct SQL
/// transaction that does not take the advisory: month to DRAFT with both stamps cleared, then back to
/// EMPLOYEE_APPROVED stamped with B). Only then is the lock released. The statement that follows must see B as
/// the sender on the committed row and refuse.</para>
///
/// Docker-gated (CI-verified only). Shares the send-command matrix fixture: the collection runs serially, so
/// holding the STY02 advisory lock briefly cannot disturb another fact.
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SenderSeparationRaceTests : SendCommandMatrixTestBase
{
    private const string FourEyesReason = "the sender of a month cannot approve or reject it (four-eyes)";

    public SenderSeparationRaceTests(SendCommandMatrixFixture fx) : base(fx) { }

    /// <summary>
    /// AC-053-3 (approve) — kind <b>F</b>. Month sent by the employee himself; HR user B starts an approve (the
    /// early check passes: B is not the sender), parks on the advisory lock, B "reopens and re-sends" under it
    /// (committed), the lock is released: the approve answers 403 with the four-eyes reason; the month stays
    /// EMPLOYEE_APPROVED stamped with B as sender; <c>approved_by</c> stays NULL; no APPROVED audit row.
    ///
    /// <para><b>Red conditions.</b> Today there is no sender predicate anywhere: after the release the
    /// conditional UPDATE matches the EMPLOYEE_APPROVED row and writes APPROVED with B as approver — B is also
    /// the sender — so the 403 assertion fails (200), and so would the status / approved_by / audit
    /// assertions.</para>
    /// </summary>
    [Fact]
    public async Task AC053_3_ReopenAndResendWhileApproveIsParked_NeverEndsApprovedBySender()
    {
        const string subject = "s147_race_a_subject";
        const string hrB = "s147_race_a_hr_b";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedEmployeeSentRowAsync(subject);

        var rsp = await RunRaceAsync(hrB, subject, periodId,
            client => client.PostAsync($"/api/approval/{periodId}/approve", content: null));

        await AssertFourEyesDeniedAsync(rsp);
        var row = (await ReadRowAsync(periodId))!;
        Assert.Equal("EMPLOYEE_APPROVED", row.Status);
        Assert.Equal(hrB, row.EmployeeApprovedBy);   // B is the sender the in-tx predicate saw
        Assert.Null(row.ApprovedBy);
        Assert.Null(row.ApprovedAt);
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId, "APPROVED"));
    }

    /// <summary>
    /// AC-053-3 (reject) — kind <b>F</b>. The same forced window for the other manager signature: the reject
    /// answers 403 (four-eyes), the month stays EMPLOYEE_APPROVED, no rejection reason, no REJECTED audit row.
    ///
    /// <para><b>Red conditions.</b> Today the reject writes REJECTED after the release: the 403 and the
    /// status assertions fail.</para>
    /// </summary>
    [Fact]
    public async Task AC053_3_ReopenAndResendWhileRejectIsParked_NeverEndsRejectedBySender()
    {
        const string subject = "s147_race_r_subject";
        const string hrB = "s147_race_r_hr_b";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedEmployeeSentRowAsync(subject);

        var rsp = await RunRaceAsync(hrB, subject, periodId,
            client => client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "race" }));

        await AssertFourEyesDeniedAsync(rsp);
        var row = (await ReadRowAsync(periodId))!;
        Assert.Equal("EMPLOYEE_APPROVED", row.Status);
        Assert.Equal(hrB, row.EmployeeApprovedBy);
        Assert.Null(row.RejectionReason);
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId, "REJECTED"));
    }

    // ── The forced interleave ─────────────────────────────────────────────────────────────────────

    /// <summary>Holds the tree lock, starts <paramref name="decision"/> as <paramref name="hrB"/>, waits (by
    /// observation) until it is parked on the lock, commits the reopen-and-re-send as B, releases the lock and
    /// returns the decision's response.</summary>
    private async Task<HttpResponseMessage> RunRaceAsync(
        string hrB, string subject, Guid periodId, Func<HttpClient, Task<HttpResponseMessage>> decision)
    {
        using var client = ClientForRole(hrB, StatsTidRoles.LocalHR);

        await using var holdConn = Fx.Db.Create();
        await holdConn.OpenAsync();
        var holdTx = await holdConn.BeginTransactionAsync();
        var released = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('reporting-org-' || @root))", holdConn, holdTx))
            {
                lockCmd.Parameters.AddWithValue("root", Org);
                await lockCmd.ExecuteScalarAsync();
            }

            // B's decision: the early four-eyes check reads the month (sender = the employee) and passes,
            // then the request takes its in-transaction locks and parks on ours.
            var decisionLeg = decision(client);

            Assert.True(await WaitForAdvisoryLockWaiterAsync(Org),
                "No backend was observed WAITING on the STY02 reporting-org advisory lock: the decision did not " +
                "reach its in-transaction lock, so the early check / lock window was never held open.");
            Assert.False(decisionLeg.IsCompleted, "the decision finished while the lock was held.");

            // WHILE parked: B reopens (DRAFT, both stamps cleared) and re-sends (EMPLOYEE_APPROVED, B stamped),
            // committed. A direct SQL transaction takes no advisory lock, so it commits freely.
            await using (var conn = new NpgsqlConnection(Fx.ConnectionString))
            {
                await conn.OpenAsync();
                await using var tx = await conn.BeginTransactionAsync();
                await ExecAsync(conn, tx,
                    """
                    UPDATE approval_periods
                       SET status = 'DRAFT', submitted_at = NULL, submitted_by = NULL,
                           employee_approved_at = NULL, employee_approved_by = NULL
                     WHERE period_id = @p AND status = 'EMPLOYEE_APPROVED'
                    """, periodId, null);
                await ExecAsync(conn, tx,
                    """
                    UPDATE approval_periods
                       SET status = 'EMPLOYEE_APPROVED', submitted_at = NOW(), submitted_by = @b,
                           employee_approved_at = NOW(), employee_approved_by = @b
                     WHERE period_id = @p AND status = 'DRAFT'
                    """, periodId, hrB);
                await tx.CommitAsync();
            }

            await holdTx.RollbackAsync();
            released = true;
            return await decisionLeg;
        }
        finally
        {
            if (!released)
                await holdTx.RollbackAsync();
        }
    }

#pragma warning disable CA2100 // each sql is a hardcoded literal at its call site; values flow through parameters.
    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Guid periodId, string? b)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("p", periodId);
        if (b is not null)
            cmd.Parameters.AddWithValue("b", b);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }
#pragma warning restore CA2100

    /// <summary>A month the EMPLOYEE sent himself: EMPLOYEE_APPROVED, both stamps = the employee.</summary>
    private async Task<Guid> SeedEmployeeSentRowAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by)
            VALUES
                (@e, @org, @s, @en, 'MONTHLY', 'EMPLOYEE_APPROVED', 'HK', 'OK24', NOW(), @e, NOW(), @e)
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", Org);
        cmd.Parameters.AddWithValue("s", MarchStart);
        cmd.Parameters.AddWithValue("en", MarchEnd);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Polls <c>pg_locks</c> until a backend is WAITING (granted = false) on the
    /// <c>reporting-org-{root}</c> advisory key (bounded; false on timeout). A condition wait, not a sleep
    /// that decides the outcome.</summary>
    private async Task<bool> WaitForAdvisoryLockWaiterAsync(string root, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = Fx.Db.Create();
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(DISTINCT pl.pid)
                FROM pg_locks pl
                WHERE pl.locktype = 'advisory'
                  AND pl.granted = FALSE
                  AND pl.pid <> pg_backend_pid()
                  AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                      = hashtext('reporting-org-' || @root)::bigint
                """, conn))
            {
                cmd.Parameters.AddWithValue("root", root);
                if ((long)(await cmd.ExecuteScalarAsync())! >= 1)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    private static async Task AssertFourEyesDeniedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(FourEyesReason, JsonDocument.Parse(raw).RootElement.GetProperty("reason").GetString());
    }
}
