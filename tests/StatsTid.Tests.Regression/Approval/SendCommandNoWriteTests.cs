using System.Net;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12711 — the send command's WRITE discipline: what a rejected send must NOT write
/// (AC-4), that neither failure mode strands any of the four source states (AC-5), the exact
/// event/audit/timestamp shape of a successful send from both adapters incl. reopen→re-send (AC-8),
/// and the accepted legacy manager-approve bypass (AC-18, ruling R6).
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SendCommandNoWriteTests : SendCommandMatrixTestBase
{
    public SendCommandNoWriteTests(SendCommandMatrixFixture fx) : base(fx) { }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-4 — rejection writes nothing, PER ARM (the two arms fail differently, so they are two
    //  criteria: "columns unchanged" is vacuous on the create arm, whose own INSERT rolled back).
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-4 (transition arm). A pre-existing row that fails the allocation gate is left byte-for-byte
    /// unchanged — every column: status, both timestamp pairs, the three dimensions AND the deadlines —
    /// and no <c>approval_audit</c> or <c>outbox_events</c> or <c>audit_projection</c> row is written.
    /// The gate fires at step (5e), strictly before the state change at (6), so the whole snapshot must
    /// survive.
    /// </summary>
    [Fact]
    public async Task Rejected_TransitionArm_LeavesEveryColumnUnchanged_NoAuditNoOutbox()
    {
        const string emp = "t711n_ac4_transition";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") }); // 7.4 worked, 0 allocated → 422
        var periodId = await SeedApprovalRowAsync(emp, "SUBMITTED", MarchStart, MarchEnd);

        var before = await ReadRowAsync(periodId);

        using var client = EmployeeClient(emp);
        await AssertAllocation422Async(await PostSendAsync(client, emp));

        var after = await ReadRowAsync(periodId);
        Assert.Equal(before, after); // record equality over the whole column snapshot
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId));
        Assert.Equal(0L, await CountOutboxAsync(StreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(0L, await CountAuditProjectionByPeriodAsync(periodId));
    }

    /// <summary>
    /// AC-4 (create arm). A fresh employee whose month fails the gate leaves NO row at all — the CREATE
    /// arm's own INSERT rolled back — and writes no audit / outbox / projection row. Stating this as
    /// "columns unchanged" would be vacuous: there is no row to inspect.
    /// </summary>
    [Fact]
    public async Task Rejected_CreateArm_LeavesNoRow_NoAuditNoOutbox()
    {
        const string emp = "t711n_ac4_create";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") }); // 422 allocation

        using var client = EmployeeClient(emp);
        await AssertAllocation422Async(await PostSendAsync(client, emp));

        Assert.False(await RowExistsAsync(emp, MarchStart, MarchEnd));
        Assert.Equal(0L, await CountOutboxAsync(StreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(0L, await CountAuditProjectionByEmployeeAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-5 — neither failure mode strands, from all four source states.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-5. For each source state (no row / DRAFT / REJECTED / SUBMITTED) and each failure mode
    /// (uncovered workday / unbalanced day): the failing send leaves the source state unchanged, AND a
    /// corrected retry then succeeds. Written as "did not strand" without observing the corrected retry
    /// this would pass vacuously.
    /// </summary>
    [Theory]
    [InlineData("NONE", "coverage")]
    [InlineData("NONE", "allocation")]
    [InlineData("DRAFT", "coverage")]
    [InlineData("DRAFT", "allocation")]
    [InlineData("REJECTED", "coverage")]
    [InlineData("REJECTED", "allocation")]
    [InlineData("SUBMITTED", "coverage")]
    [InlineData("SUBMITTED", "allocation")]
    public async Task NeitherFailureMode_Strands_AnySource_AndCorrectedRetrySucceeds(
        string sourceState, string failureMode)
    {
        var emp = $"t711n_ac5_{sourceState.ToLowerInvariant()}_{failureMode}";
        await SeedEmployeeAsync(emp);

        Guid? seededId = sourceState == "NONE"
            ? null
            : await SeedApprovalRowAsync(emp, sourceState, MarchStart, MarchEnd);

        // Build the FAILING month.
        if (failureMode == "coverage")
        {
            // Every weekday covered EXCEPT the gap day → coverage 422 (gap missing).
            await CoverMonthWithAbsencesAsync(emp, gap: GapDay);
        }
        else
        {
            // Every weekday covered, but the gap day has worked hours and no allocation → allocation 422.
            await CoverMonthWithAbsencesAsync(emp, gap: null);
            await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") });
        }

        using var client = EmployeeClient(emp);

        // (1) the failing send.
        var failed = await PostSendAsync(client, emp);
        if (failureMode == "coverage")
            await AssertCoverage422Async(failed);
        else
            await AssertAllocation422Async(failed);

        // (2) the source state is unchanged.
        if (sourceState == "NONE")
            Assert.False(await RowExistsAsync(emp, MarchStart, MarchEnd));
        else
            Assert.Equal(sourceState, (await ReadRowAsync(seededId!.Value))!.Status);

        // (3) correct the month and retry → success.
        if (failureMode == "coverage")
            await InsertAbsenceAsync(emp, GapDay, "VACATION", 7.4m);       // close the coverage gap
        else
            await AllocatedAsync(emp, GapDay, 7.4m, "NORMAL", "DRIFT-01"); // balance the gap day

        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));
        var final = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(final!.Value))!.Status);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-8 — one event, one audit row (literal "SUBMITTED"), one audit_projection, both timestamp
    //  pairs non-NULL — from BOTH adapters incl. reopen→re-send.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>AC-8 via the MONTH-KEYED adapter (create arm). Exactly one outbox
    /// <c>PeriodEmployeeApproved</c>, one <c>approval_audit</c> row with the literal action
    /// <c>"SUBMITTED"</c> (the <c>init.sql:903</c> CHECK has no EMPLOYEE_APPROVED member), one
    /// <c>audit_projection</c> row, and all four stamps (submitted + employee-approved pairs)
    /// non-NULL. The comment is the self path.</summary>
    [Fact]
    public async Task Send_MonthKeyed_EmitsExactlyOneEventOneAuditOneProjection_BothStampPairs()
    {
        const string emp = "t711n_ac8_month";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = EmployeeClient(emp);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));

        var periodId = (await FindPeriodIdAsync(emp, MarchStart, MarchEnd))!.Value;
        Assert.Equal(1L, await CountOutboxAsync(StreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(1L, await CountOutboxTotalAsync(StreamId(emp))); // F6 — ONE event of ANY type on the stream
        Assert.Equal(1L, await CountApprovalAuditAsync(periodId, "SUBMITTED"));
        Assert.Equal(1L, await CountApprovalAuditAsync(periodId)); // ONLY the one SUBMITTED row
        Assert.Equal(1L, await CountAuditProjectionByPeriodAsync(periodId));
        Assert.Equal(1L, await CountAuditProjectionTotalByPeriodAsync(periodId)); // F6 — ONE projection of ANY type
        Assert.Equal("Employee self-approval", await ReadFirstAuditCommentAsync(periodId, "SUBMITTED"));

        var row = await ReadRowAsync(periodId);
        Assert.NotNull(row!.SubmittedAt);
        Assert.Equal(emp, row.SubmittedBy);
        Assert.NotNull(row.EmployeeApprovedAt);
        Assert.Equal(emp, row.EmployeeApprovedBy);
    }

    /// <summary>AC-8 via the BY-ID adapter (transition arm, no prior send). A directly-seeded DRAFT
    /// whole-month row sent via <c>employee-approve</c> yields exactly one of each and both stamp pairs
    /// non-NULL.</summary>
    [Fact]
    public async Task Send_ById_EmitsExactlyOneEventOneAuditOneProjection_BothStampPairs()
    {
        const string emp = "t711n_ac8_byid";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        var periodId = await SeedApprovalRowAsync(emp, "DRAFT", MarchStart, MarchEnd);

        using var client = EmployeeClient(emp);
        await AssertOkEmployeeApprovedAsync(await PostEmployeeApproveAsync(client, periodId));

        Assert.Equal(1L, await CountOutboxAsync(StreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(1L, await CountOutboxTotalAsync(StreamId(emp))); // F6 — ONE event of ANY type on the stream
        Assert.Equal(1L, await CountApprovalAuditAsync(periodId, "SUBMITTED"));
        Assert.Equal(1L, await CountAuditProjectionByPeriodAsync(periodId));
        Assert.Equal(1L, await CountAuditProjectionTotalByPeriodAsync(periodId)); // F6 — ONE projection of ANY type

        var row = await ReadRowAsync(periodId);
        Assert.NotNull(row!.SubmittedAt);
        Assert.Equal(emp, row.SubmittedBy);
        Assert.NotNull(row.EmployeeApprovedAt);
        Assert.Equal(emp, row.EmployeeApprovedBy);
    }

    /// <summary>AC-8 the reopen→re-send arm (both adapters over one period). send (month-keyed) NULLs
    /// nothing; reopen (employee arm) NULLs the whole decision record incl. both stamp pairs; the by-id
    /// re-send RE-STAMPS them. Asserts the four stamps are non-NULL AFTER the re-send, and that the two
    /// send acts produced two SUBMITTED audit rows + two events + two projection rows (an honest
    /// cumulative count — the "exactly one" is per send act, pinned by the two tests above).</summary>
    [Fact]
    public async Task ReopenThenResend_ReStampsBothPairs_TwoSendActs()
    {
        const string emp = "t711n_ac8_reopen";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = EmployeeClient(emp);

        // send #1 (month-keyed) → EMPLOYEE_APPROVED, stamps set.
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));
        var periodId = (await FindPeriodIdAsync(emp, MarchStart, MarchEnd))!.Value;

        // reopen (employee arm: own EMPLOYEE_APPROVED → DRAFT) → NULLs both stamp pairs.
        await AssertStatusAsync(await PostReopenAsync(client, periodId, "AC-8 reopen"), HttpStatusCode.OK);
        var reopened = await ReadRowAsync(periodId);
        Assert.Null(reopened!.SubmittedAt);
        Assert.Null(reopened.EmployeeApprovedAt);

        // send #2 (BY-ID) → EMPLOYEE_APPROVED, re-stamps.
        await AssertOkEmployeeApprovedAsync(await PostEmployeeApproveAsync(client, periodId));

        var row = await ReadRowAsync(periodId);
        Assert.Equal("EMPLOYEE_APPROVED", row!.Status);
        Assert.NotNull(row.SubmittedAt);
        Assert.Equal(emp, row.SubmittedBy);
        Assert.NotNull(row.EmployeeApprovedAt);
        Assert.Equal(emp, row.EmployeeApprovedBy);

        // Two send acts, honestly counted.
        Assert.Equal(2L, await CountApprovalAuditAsync(periodId, "SUBMITTED"));
        Assert.Equal(2L, await CountOutboxAsync(StreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(2L, await CountAuditProjectionByPeriodAsync(periodId));
    }

    /// <summary>AC-8 (P3) the on-behalf audit comment. When an HR actor sends for ANOTHER employee (a
    /// sanctioned R4 path), the audit comment is <c>"Sent on behalf of {employeeId}"</c>, not the false
    /// "Employee self-approval". Fails if the comment is written unconditionally.</summary>
    [Fact]
    public async Task Send_OnBehalfByHr_WritesOnBehalfComment()
    {
        const string subject = "t711n_ac8_subject";
        const string hr = "t711n_ac8_hr";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null);

        using var hrClient = ClientForRole(hr, StatsTid.SharedKernel.Security.StatsTidRoles.LocalHR, Org);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(hrClient, subject));

        var periodId = (await FindPeriodIdAsync(subject, MarchStart, MarchEnd))!.Value;
        Assert.Equal($"Sent on behalf of {subject}", await ReadFirstAuditCommentAsync(periodId, "SUBMITTED"));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-18 — the accepted legacy bypass (ruling R6).
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-18 (ruling R6 — recorded as INTENDED, not a defect). A directly-seeded legacy <c>SUBMITTED</c>
    /// row whose month FAILS the allocation gate (proven first: a /send of that month 422s and leaves the
    /// row SUBMITTED) remains manager-approvable — <c>POST /approve</c> transitions it straight to
    /// APPROVED with no coverage/allocation re-validation. Retiring <c>/submit</c> stops NEW such rows;
    /// it does not force-migrate the existing ones. Fails if the manager-approve path grew a gate.
    /// </summary>
    [Fact]
    public async Task LegacySubmittedRow_FailingAllocation_RemainsManagerApprovable_R6()
    {
        const string emp = "t711n_ac18_legacy";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") }); // month is unbalanced
        var periodId = await SeedApprovalRowAsync(emp, "SUBMITTED", MarchStart, MarchEnd);

        // The month genuinely fails the send gate (so the bypass is real, not vacuous), and the failing
        // send leaves the SUBMITTED row untouched.
        using var self = EmployeeClient(emp);
        await AssertAllocation422Async(await PostSendAsync(self, emp));
        Assert.Equal("SUBMITTED", (await ReadRowAsync(periodId))!.Status);

        // R6: a manager (GlobalAdmin here, admitted by GLOBAL org-scope) approves it anyway.
        using var admin = GlobalAdminClient("t711n_ac18_admin");
        await AssertStatusAsync(await PostApproveAsync(admin, periodId), HttpStatusCode.OK);
        Assert.Equal("APPROVED", (await ReadRowAsync(periodId))!.Status);
    }
}
