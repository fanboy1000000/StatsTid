using System.Net;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12711 — the send command's OUTCOME behaviour, exercised through the two production route
/// adapters (<c>POST /api/approval/send</c> and <c>POST /api/approval/{periodId}/employee-approve</c>).
///
/// <para>Covers AC-3 (allocation-gate falsifiability), AC-6 (the vacuous absence month), AC-9
/// (<c>SUBMITTED</c> retired on production routes), AC-10 (the whole-month guard) and AC-12
/// (server-resolved dimensions per arm). Every case reads its verdict off the HTTP response and the
/// <c>approval_periods</c> row; none re-implements the rule under test.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SendCommandBehaviourTests : SendCommandMatrixTestBase
{
    public SendCommandBehaviourTests(SendCommandMatrixFixture fx) : base(fx) { }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-3 — the allocation gate is independently falsifiable.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-3 (the falsified case). A month that PASSES coverage but has one day with worked hours and no
    /// allocation must 422 with <c>kind:"allocation"</c>.
    ///
    /// <para><b>Why the fixture is built the way it is.</b> Coverage reads <c>time_entries_projection</c>
    /// + <c>absences_projection</c>; the allocation gate's WORKED side reads <c>work_time_projection</c>
    /// and its ALLOCATED side reads the time-entry list — two DIFFERENT tables. So the ONE day the gate
    /// may fault (2026-03-05) is covered by an ABSENCE (satisfying coverage without touching either side
    /// of the gate) and carries a <c>work_time_projection</c> row of 7.4h with NO NORMAL+task_id entry
    /// (worked 7.4, allocated 0). Every other weekday is absence-only: covered, and outside the gate's
    /// Union. The allocation gate is therefore the ONLY thing that can 422 this month.</para>
    ///
    /// <para><b>What makes it FAIL (and the AC-3 falsification runs it):</b> delete/short-circuit the
    /// allocation arm of <c>ExecuteSendAsync</c> and this returns 200 — the month passes coverage on its
    /// own. Demonstrated red-then-green by the orchestrator per the sprint's scratch-edit discipline.</para>
    /// </summary>
    [Fact]
    public async Task WorkedWithNoAllocation_OnACoveredDay_Refused_Allocation()
    {
        const string emp = "t711b_ac3_refuse";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);            // every weekday covered by absence
        await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") }); // 7.4h worked, 0 allocated

        using var client = EmployeeClient(emp);
        var rsp = await PostSendAsync(client, emp);

        var body = await AssertAllocation422Async(rsp);
        var day = Assert.Single(body.GetProperty("unbalancedDays").EnumerateArray().ToList());
        Assert.Equal(GapDay.ToString("yyyy-MM-dd"), day.GetProperty("date").GetString());
        Assert.Equal(7.40m, day.GetProperty("worked").GetDecimal());
        Assert.Equal(0.00m, day.GetProperty("allocated").GetDecimal());
        Assert.Equal("under", day.GetProperty("direction").GetString());
        Assert.False(await RowExistsAsync(emp, MarchStart, MarchEnd)); // create arm rolled back
    }

    /// <summary>
    /// AC-3 (the positive control that keeps the 422 above non-vacuous). The SAME month with the matching
    /// allocation added on the faulting day sends 200 — proving the 422 is the imbalance, not "any
    /// <c>work_time_projection</c> row refuses".
    /// </summary>
    [Fact]
    public async Task WorkedWithMatchingAllocation_OnACoveredDay_Sent()
    {
        const string emp = "t711b_ac3_balanced";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        await WorkedAsync(emp, GapDay, intervals: new[] { ("08:00", "15:24") }); // 7.4h worked
        await AllocatedAsync(emp, GapDay, 7.4m, "NORMAL", "DRIFT-01");            // 7.4h allocated

        using var client = EmployeeClient(emp);
        var rsp = await PostSendAsync(client, emp);

        await AssertOkEmployeeApprovedAsync(rsp);
    }

    /// <summary>
    /// AC-3's reachability contrast, asserted directly: a NORMAL entry with a NULL <c>task_id</c> and no
    /// <c>work_time_projection</c> row lands in NEITHER map — it satisfies coverage but contributes 0 to
    /// both worked and allocated — so the day never enters the Union and the send is 200. This is the
    /// exact trap the refinement calls out (rev 5's false "null-TaskId isolates the gate" fixture): a
    /// green here would flip to a 422 "over" if the code ever counted null-task entries as allocated.
    /// </summary>
    [Fact]
    public async Task NullTaskEntryAlone_LandsInNeitherMap_Sent()
    {
        const string emp = "t711b_ac3_nulltask";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: GapDay);      // GapDay NOT absence-covered
        await AllocatedAsync(emp, GapDay, 7.4m, "NORMAL", taskId: null); // covers GapDay, 0 to both sides

        using var client = EmployeeClient(emp);
        var rsp = await PostSendAsync(client, emp);

        await AssertOkEmployeeApprovedAsync(rsp);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-6 — the vacuous case.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-6. A month whose every expected workday is absence-covered succeeds for an employee with no
    /// project allocations at all — and the assertion that keeps it honest is that ZERO
    /// <c>work_time_projection</c> rows exist on those days, so the 200 is the vacuous-balance path
    /// (worked==0 ∧ allocated==0 on every day, all skipped) and not the accident of work that happened
    /// to balance. Written as "an empty month succeeds" this would instead FAIL coverage.
    /// </summary>
    [Fact]
    public async Task VacuousAbsenceMonth_NoWork_Succeeds_ZeroWorkTimeRows()
    {
        const string emp = "t711b_ac6_vacuous";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp)); // the anti-vacuous guard: no work at all

        using var client = EmployeeClient(emp);
        var rsp = await PostSendAsync(client, emp);

        await AssertOkEmployeeApprovedAsync(rsp);
        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp)); // still none after the send
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-9 — SUBMITTED retired on production routes.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-9. A production send writes <c>EMPLOYEE_APPROVED</c>, never <c>SUBMITTED</c>, and the created
    /// row carries non-NULL deadlines. Fails if the create arm ever wrote <c>SUBMITTED</c> to the status
    /// column, or left either deadline NULL.
    /// </summary>
    [Fact]
    public async Task Send_WritesEmployeeApproved_NotSubmitted_WithDeadlines()
    {
        const string emp = "t711b_ac9_status";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = EmployeeClient(emp);
        var rsp = await PostSendAsync(client, emp);
        await AssertOkEmployeeApprovedAsync(rsp);

        var periodId = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        var row = await ReadRowAsync(periodId!.Value);
        Assert.NotNull(row);
        Assert.Equal("EMPLOYEE_APPROVED", row!.Status);
        Assert.NotEqual("SUBMITTED", row.Status);
        Assert.NotNull(row.EmployeeDeadline);
        Assert.NotNull(row.ManagerDeadline);
        // +2 / +5 off the month's last day (the send act sets them).
        Assert.Equal(MarchEnd.AddDays(2), row.EmployeeDeadline);
        Assert.Equal(MarchEnd.AddDays(5), row.ManagerDeadline);
    }

    /// <summary>
    /// AC-9. Sending an already <c>EMPLOYEE_APPROVED</c> period is a 409 (the source-state gate rejects
    /// it — <c>EMPLOYEE_APPROVED</c> is not an allowed source), with no downgrade: the row stays
    /// <c>EMPLOYEE_APPROVED</c>. Fails if a re-send walked the status backwards or 200'd.
    /// </summary>
    [Fact]
    public async Task Send_AlreadyEmployeeApproved_Conflicts_NoDowngrade()
    {
        const string emp = "t711b_ac9_dupe";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = EmployeeClient(emp);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));

        var second = await PostSendAsync(client, emp);
        await AssertStatusAsync(second, HttpStatusCode.Conflict);

        var periodId = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId!.Value))!.Status);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-10 — the whole-month guard, both paths.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-10(b). The by-id adapter rejects a partial-range row with 409 <c>kind:"not-whole-month"</c>,
    /// and leaves it untouched. (The month-keyed adapter cannot produce a partial range at all — it
    /// derives [first..last] from (year, month) — so there is nothing to test on that path.)
    /// </summary>
    [Fact]
    public async Task ById_PartialRangeRow_Rejected_NotWholeMonth()
    {
        const string emp = "t711b_ac10_partial";
        await SeedEmployeeAsync(emp);
        var partialStart = new DateOnly(2026, 3, 2);
        var partialEnd = new DateOnly(2026, 3, 6);
        var periodId = await SeedApprovalRowAsync(emp, "DRAFT", partialStart, partialEnd);

        using var client = EmployeeClient(emp);
        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertStatusAsync(rsp, HttpStatusCode.Conflict);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.Contains("not-whole-month", raw);
        Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status); // untouched
    }

    /// <summary>
    /// AC-10. A <c>WEEKLY</c> row that happens to span an exact calendar month is ACCEPTED by the by-id
    /// adapter — the guard is a BOUNDARY check, not a <c>period_type</c> check. This documents the
    /// absence of a type check (adding one would make legacy WEEKLY rows permanently unsendable). Fails
    /// if the guard ever started rejecting on <c>period_type</c>.
    /// </summary>
    [Fact]
    public async Task ById_WeeklyRowSpanningWholeMonth_Accepted()
    {
        const string emp = "t711b_ac10_weekly";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        var periodId = await SeedApprovalRowAsync(emp, "DRAFT", MarchStart, MarchEnd, periodType: "WEEKLY");

        using var client = EmployeeClient(emp);
        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertOkEmployeeApprovedAsync(rsp);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId))!.Status);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-12 — server-resolved dimensions, per arm (P4).
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-12 (create arm). A March 2026 month resolves <c>ok_version = OK24</c> — the resolver keys on
    /// the MONTH being sent (2026-03-01, inside the OK24 window 2024-04-01..2026-03-31), NOT on today
    /// (the test clock sits past the 2026-04-01 boundary, where "today" would resolve OK26). org_id is
    /// the employee's current primary org (STY02) and agreement_code the DATED code at the month (HK).
    /// Fails if any dimension were read from "now" or from the caller.
    ///
    /// <para>S127 Step-7a F8 — the LIVE cache <c>users.agreement_code</c> is deliberately set to a
    /// DIFFERENT code (<c>AC</c>) than the dated <c>user_agreement_codes</c> value at the month (<c>HK</c>).
    /// Production resolves <c>GetByUserIdAtAsync(monthStart) ?? user.AgreementCode</c>, so the correct
    /// value is HK; if a regression read the live cache instead, the row would carry AC and this fails.</para>
    /// </summary>
    [Fact]
    public async Task CreateArm_RecordsResolvedDimensions()
    {
        const string emp = "t711b_ac12_create";
        await SeedEmployeeAsync(emp, Org, "HK");            // dated agreement-at-month = HK
        await SetLiveAgreementCodeAsync(emp, "AC");         // F8 — live cache DIFFERS (AC), so HK proves the dated read
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = EmployeeClient(emp);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));

        var periodId = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        var row = await ReadRowAsync(periodId!.Value);
        Assert.Equal(Org, row!.OrgId);            // STY02
        Assert.Equal("HK", row.AgreementCode);    // the DATED code, not the live AC cache
        Assert.Equal("OK24", row.OkVersion);      // month-resolved, not today
    }

    /// <summary>
    /// AC-12 (transition arm). A pre-existing SUBMITTED row seeded with DELIBERATELY WRONG dimensions
    /// (org <c>STY01</c>, agreement <c>WRONG</c>, ok <c>OK21</c>) is CORRECTED on re-send to the
    /// server-resolved values (STY02 / HK / OK24). This is the half that had no write path before S127 —
    /// the retired <c>/submit</c> INSERTed caller-supplied dimensions and the status switch never touched
    /// them, so a re-send preserved a wrong value. Fails if the follow-up UPDATE does not carry the three
    /// dimensions onto the transition arm.
    ///
    /// <para>S127 Step-7a F8 — the live cache <c>users.agreement_code</c> is set to <c>AC</c> while the
    /// dated <c>user_agreement_codes</c> value at the month stays <c>HK</c>, so the corrected-to-HK
    /// assertion fails if a regression sourced agreement_code from the live cache rather than the dated
    /// lookup (the stored "WRONG" already excludes the preserve-the-stale-value bug).</para>
    /// </summary>
    [Fact]
    public async Task TransitionArm_CorrectsWrongStoredDimensions()
    {
        const string emp = "t711b_ac12_correct";
        await SeedEmployeeAsync(emp, Org, "HK");            // dated agreement-at-month = HK
        await SetLiveAgreementCodeAsync(emp, "AC");         // F8 — live cache DIFFERS (AC)
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        var periodId = await SeedApprovalRowAsync(
            emp, "SUBMITTED", MarchStart, MarchEnd,
            orgId: OtherOrg, agreementCode: "WRONG", okVersion: "OK21");

        using var client = EmployeeClient(emp);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));

        var row = await ReadRowAsync(periodId);
        Assert.Equal("EMPLOYEE_APPROVED", row!.Status);
        Assert.Equal(Org, row.OrgId);          // corrected STY01 → STY02
        Assert.Equal("HK", row.AgreementCode); // corrected WRONG → HK (the DATED code, not the live AC cache)
        Assert.Equal("OK24", row.OkVersion);   // corrected OK21 → OK24
    }
}
