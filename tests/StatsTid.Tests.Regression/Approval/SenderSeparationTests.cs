using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Security;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S147 / TASK-14704-T (SEC-053; owner ruling OQ-1 = (a), strict four-eyes, no exception) — whoever SENT a
/// month can neither approve nor reject it.
///
/// <para><b>Plain language.</b> A month is signed twice: once when it is sent (by the employee, or by HR on
/// the employee's behalf) and once when a manager approves it. SEC-009 already stops the employee from
/// approving their own month; it did not stop an HR user who sent the month on someone's behalf from then
/// approving it — one person signing both halves. These facts prove the endpoint now refuses that (403, never
/// the state-revealing 409), that a DIFFERENT person still can, that reopening (which signs nothing and clears
/// both stamps) stays open to the sender, and that the legacy-row shape (only <c>submitted_by</c> stamped) is
/// covered too.</para>
///
/// <para><b>The spec, not the code.</b> Expected values come from the task contract and refinement T4 /
/// AC-053-1 … -6: the sender is <c>employee_approved_by</c>, else the legacy <c>submitted_by</c>; the refusal is
/// 403 with the date-free reason below; no state change, no audit row. The race (AC-053-3) is in
/// <see cref="SenderSeparationRaceTests"/>; payroll's re-check (AC-053-4) is in
/// <c>PayrollSenderSeparationHostTests</c>.</para>
///
/// <para><b>AC-053-6 (SEC-009's matrix is unchanged).</b> No new fact here: every fact of
/// <see cref="SelfApprovalGuardTests"/> (13) runs UNCHANGED. Its seeded rows carry the employee as sender
/// (<c>employee_approved_by = employee</c>), so a self actor is BOTH employee and sender, and each fact still
/// asserts SEC-009's reason ("segregation of duties") — which pins that SEC-009's guard runs first and its
/// reason text is untouched.</para>
///
/// Docker-gated (CI-verified only). Shares the send-command matrix fixture (one container, serial).
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SenderSeparationTests : SendCommandMatrixTestBase
{
    /// <summary>The date-free reason the four-eyes 403 carries (task contract 2).</summary>
    private const string FourEyesReason = "the sender of a month cannot approve or reject it (four-eyes)";

    public SenderSeparationTests(SendCommandMatrixFixture fx) : base(fx) { }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-053-1 — the sender approving is refused; another HR user approves.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-053-1 — kind <b>F</b>, driven through the REAL send route. An HR user sends an employee's month on
    /// the employee's behalf (the stamp: <c>employee_approved_by = hrSender</c>). The SAME HR user then
    /// approves: 403 with the four-eyes reason, the month stays EMPLOYEE_APPROVED, nobody is recorded as
    /// approver, no APPROVED audit row. A DIFFERENT HR user then approves the same month: 200, APPROVED,
    /// recorded approver = that user.
    ///
    /// <para><b>Red conditions (Docker; cannot run locally).</b> Today <c>POST /approve</c> by the sender
    /// answers 200 (only SEC-009's approver==employee guard exists): the 403 assertion, the
    /// still-EMPLOYEE_APPROVED assertion and the null-approver assertion all fail on today's code.</para>
    /// </summary>
    [Fact]
    public async Task AC053_1_SenderApprove_Refused403_AnotherHrUser_Approves200()
    {
        const string subject = "s147_sep1_subject";
        const string hrSender = "s147_sep1_hr_sender";
        const string hrOther = "s147_sep1_hr_other";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null);

        using var senderClient = ClientForRole(hrSender, StatsTidRoles.LocalHR);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(senderClient, subject));
        var periodId = (await FindPeriodIdAsync(subject, MarchStart, MarchEnd))!.Value;
        Assert.Equal(hrSender, (await ReadRowAsync(periodId))!.EmployeeApprovedBy);

        // The sender approves: refused.
        await AssertFourEyesDeniedAsync(await PostApproveAsync(senderClient, periodId));
        var afterDenied = (await ReadRowAsync(periodId))!;
        Assert.Equal("EMPLOYEE_APPROVED", afterDenied.Status);
        Assert.Null(afterDenied.ApprovedBy);
        Assert.Null(afterDenied.ApprovedAt);
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId, "APPROVED"));

        // Another HR user approves the very same month: allowed, and recorded as the approver.
        using var otherClient = ClientForRole(hrOther, StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostApproveAsync(otherClient, periodId), HttpStatusCode.OK);
        var approved = (await ReadRowAsync(periodId))!;
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(hrOther, approved.ApprovedBy);
        Assert.Equal(1L, await CountApprovalAuditAsync(periodId, "APPROVED"));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-053-2 — reject, reopen, the legacy row, and the APPROVED month.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-053-2 (reject) — kind <b>F</b>. The sender of an EMPLOYEE_APPROVED month rejecting it: 403 with the
    /// four-eyes reason, status unchanged, no rejection reason stored, no REJECTED audit row. Another HR user
    /// rejecting the same month: 200, REJECTED.
    ///
    /// <para><b>Red conditions.</b> Today the sender's reject answers 200 and the month becomes REJECTED: the
    /// 403 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task AC053_2_SenderReject_Refused403_AnotherHrUser_Rejects200()
    {
        const string subject = "s147_sep2r_subject";
        const string hrSender = "s147_sep2r_hr_sender";
        const string hrOther = "s147_sep2r_hr_other";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedStampedRowAsync(subject, "EMPLOYEE_APPROVED",
            submittedBy: hrSender, employeeApprovedBy: hrSender);

        using var senderClient = ClientForRole(hrSender, StatsTidRoles.LocalHR);
        await AssertFourEyesDeniedAsync(await PostRejectAsync(senderClient, periodId, "not mine to reject"));
        var afterDenied = (await ReadRowAsync(periodId))!;
        Assert.Equal("EMPLOYEE_APPROVED", afterDenied.Status);
        Assert.Null(afterDenied.RejectionReason);
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId, "REJECTED"));

        using var otherClient = ClientForRole(hrOther, StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostRejectAsync(otherClient, periodId, "second signer rejects"), HttpStatusCode.OK);
        var rejected = (await ReadRowAsync(periodId))!;
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal("second signer rejects", rejected.RejectionReason);
    }

    /// <summary>
    /// AC-053-2 (reopen) — kind <b>R</b> (regression pin: reopen is NOT a signature and stays open to the
    /// sender; owner ruling OQ-1 = (a), R-6). The sender reopening their own EMPLOYEE_APPROVED month: 200,
    /// DRAFT, and BOTH sender stamps are cleared (so the next sender starts clean). This holds today; it
    /// guards against the four-eyes guard being added to the wrong handler.
    /// </summary>
    [Fact]
    public async Task AC053_2_SenderReopen_OfEmployeeApprovedMonth_Allowed200_ClearsBothStamps()
    {
        const string subject = "s147_sep2o_subject";
        const string hrSender = "s147_sep2o_hr_sender";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedStampedRowAsync(subject, "EMPLOYEE_APPROVED",
            submittedBy: hrSender, employeeApprovedBy: hrSender);

        using var senderClient = ClientForRole(hrSender, StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostReopenAsync(senderClient, periodId, "sender undoes the send"), HttpStatusCode.OK);

        var row = (await ReadRowAsync(periodId))!;
        Assert.Equal("DRAFT", row.Status);
        Assert.Null(row.SubmittedBy);
        Assert.Null(row.EmployeeApprovedBy);
    }

    /// <summary>
    /// AC-053-2 (legacy row) — kind <b>F</b>. A legacy SUBMITTED row stamped only with <c>submitted_by</c>
    /// (<c>employee_approved_by</c> NULL): the sender is <c>submitted_by</c>. That user approving: 403,
    /// four-eyes reason, row unchanged. A different HR user approving the same row: 200 (so the refusal was
    /// about the sender, not a blanket denial).
    ///
    /// <para><b>Red conditions.</b> Today the legacy sender's approve answers 200: the 403 assertion
    /// fails.</para>
    /// </summary>
    [Fact]
    public async Task AC053_2_LegacyRow_OnlySubmittedBy_SenderApprove_Refused403_OtherHr200()
    {
        const string subject = "s147_sep2l_subject";
        const string hrSender = "s147_sep2l_hr_sender";
        const string hrOther = "s147_sep2l_hr_other";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedStampedRowAsync(subject, "SUBMITTED",
            submittedBy: hrSender, employeeApprovedBy: null);

        using var senderClient = ClientForRole(hrSender, StatsTidRoles.LocalHR);
        await AssertFourEyesDeniedAsync(await PostApproveAsync(senderClient, periodId));
        var afterDenied = (await ReadRowAsync(periodId))!;
        Assert.Equal("SUBMITTED", afterDenied.Status);
        Assert.Null(afterDenied.ApprovedBy);

        using var otherClient = ClientForRole(hrOther, StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostApproveAsync(otherClient, periodId), HttpStatusCode.OK);
        Assert.Equal(hrOther, (await ReadRowAsync(periodId))!.ApprovedBy);
    }

    /// <summary>
    /// AC-053-2 (APPROVED month) — kind <b>F</b>. The guard sits BEFORE the status check, like SEC-009's: the
    /// sender asking to approve OR reject a month that is already APPROVED gets 403 (four-eyes), never the
    /// 409 that would disclose the month's state to someone who may not decide it.
    ///
    /// <para><b>Red conditions.</b> Today both calls answer 409 ("Cannot approve/reject period with status
    /// APPROVED"): the 403 assertions fail.</para>
    /// </summary>
    [Fact]
    public async Task AC053_2_SenderOnApprovedMonth_Approve_And_Reject_Return403_Not409()
    {
        const string subject = "s147_sep2a_subject";
        const string hrSender = "s147_sep2a_hr_sender";
        const string hrApprover = "s147_sep2a_hr_approver";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedStampedRowAsync(subject, "APPROVED",
            submittedBy: hrSender, employeeApprovedBy: hrSender, approvedBy: hrApprover);

        using var senderClient = ClientForRole(hrSender, StatsTidRoles.LocalHR);

        var approveRsp = await PostApproveAsync(senderClient, periodId);
        Assert.NotEqual(HttpStatusCode.Conflict, approveRsp.StatusCode);
        await AssertFourEyesDeniedAsync(approveRsp);

        var rejectRsp = await PostRejectAsync(senderClient, periodId, "late");
        Assert.NotEqual(HttpStatusCode.Conflict, rejectRsp.StatusCode);
        await AssertFourEyesDeniedAsync(rejectRsp);

        var row = (await ReadRowAsync(periodId))!;
        Assert.Equal("APPROVED", row.Status);
        Assert.Equal(hrApprover, row.ApprovedBy);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Boundaries of the sender definition.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sender definition — kind <b>F</b> (the <c>employee_approved_by</c> half) with a boundary half that
    /// holds today. When BOTH stamps exist and DIFFER, the sender is <c>employee_approved_by</c>
    /// (<c>COALESCE(employee_approved_by, submitted_by)</c>), not <c>submitted_by</c>. Month A:
    /// <c>submitted_by = hrA</c>, <c>employee_approved_by = hrB</c>. hrB (the sender) approving: 403.
    /// hrA (not the sender by the contract) approving: 200.
    ///
    /// <para><b>Red conditions.</b> The hrB 403 assertion fails today (200). The hrA 200 assertion holds
    /// today and must keep holding — a guard that compared against <c>submitted_by</c> first would break
    /// it.</para>
    /// </summary>
    [Fact]
    public async Task SenderIsEmployeeApprovedBy_NotSubmittedBy_WhenBothStampsDiffer()
    {
        const string subjectB = "s147_sepc_subject_b";
        const string subjectA = "s147_sepc_subject_a";
        const string hrA = "s147_sepc_hr_a";
        const string hrB = "s147_sepc_hr_b";
        await SeedEmployeeAsync(subjectB);
        await SeedEmployeeAsync(subjectA);

        var periodForB = await SeedStampedRowAsync(subjectB, "EMPLOYEE_APPROVED",
            submittedBy: hrA, employeeApprovedBy: hrB);
        using var clientB = ClientForRole(hrB, StatsTidRoles.LocalHR);
        await AssertFourEyesDeniedAsync(await PostApproveAsync(clientB, periodForB));
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodForB))!.Status);

        var periodForA = await SeedStampedRowAsync(subjectA, "EMPLOYEE_APPROVED",
            submittedBy: hrA, employeeApprovedBy: hrB);
        using var clientA = ClientForRole(hrA, StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostApproveAsync(clientA, periodForA), HttpStatusCode.OK);
        Assert.Equal(hrA, (await ReadRowAsync(periodForA))!.ApprovedBy);
    }

    /// <summary>
    /// Ordinal comparison — kind <b>B</b> (boundary pin — holds today). The sender id is compared exactly
    /// (Ordinal): an actor whose id differs from the sender's only by case is a different person to the
    /// guard and may approve. Pins the contract's "Ordinal" so nobody loosens it to a case-insensitive match
    /// (which would be a different, unspecified rule).
    /// </summary>
    [Fact]
    public async Task SenderComparison_IsOrdinal_ActorDifferingOnlyByCase_IsNotTheSender()
    {
        const string subject = "s147_sepo_subject";
        await SeedEmployeeAsync(subject);
        var periodId = await SeedStampedRowAsync(subject, "EMPLOYEE_APPROVED",
            submittedBy: "s147_sepo_hr", employeeApprovedBy: "s147_sepo_hr");

        using var client = ClientForRole("S147_SEPO_HR", StatsTidRoles.LocalHR);
        await AssertStatusAsync(await PostApproveAsync(client, periodId), HttpStatusCode.OK);
        Assert.Equal("S147_SEPO_HR", (await ReadRowAsync(periodId))!.ApprovedBy);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Seeding and assertions.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Seeds a March-2026 row for <paramref name="employeeId"/> carrying EXACTLY the sender stamps
    /// the test names (the send route writes <c>submitted_by</c> and <c>employee_approved_by</c>; a legacy
    /// row has only the former). Natural key (employee, March) — one row per employee per test.</summary>
    private async Task<Guid> SeedStampedRowAsync(
        string employeeId, string status, string? submittedBy, string? employeeApprovedBy, string? approvedBy = null)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by, approved_at, approved_by)
            VALUES
                (@e, @org, @s, @en, 'MONTHLY', @st, 'HK', 'OK24',
                 NOW(), @sb, CASE WHEN @eab IS NULL THEN NULL ELSE NOW() END, @eab,
                 CASE WHEN @ab IS NULL THEN NULL ELSE NOW() END, @ab)
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", Org);
        cmd.Parameters.AddWithValue("s", MarchStart);
        cmd.Parameters.AddWithValue("en", MarchEnd);
        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("sb", (object?)submittedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eab", (object?)employeeApprovedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ab", (object?)approvedBy ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<HttpResponseMessage> PostRejectAsync(HttpClient client, Guid periodId, string reason)
        => client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason });

    /// <summary>The four-eyes denial: 403 AND the exact date-free reason (which tells the four-eyes guard
    /// from SEC-009's "segregation of duties" 403 and from any authorization 403).</summary>
    private static async Task AssertFourEyesDeniedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        var reason = JsonDocument.Parse(raw).RootElement.GetProperty("reason").GetString();
        Assert.Equal(FourEyesReason, reason);
    }
}
