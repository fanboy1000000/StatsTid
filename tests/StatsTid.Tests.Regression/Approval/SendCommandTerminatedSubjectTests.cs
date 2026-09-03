using System.Net;
using System.Text.Json;
using StatsTid.SharedKernel.Security;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S138 / TASK-13805 — the send command on a DEACTIVATED subject (the "leaver-send dead-end" that
/// S136 / TASK-13606 named and deferred).
///
/// <para><b>The problem, in plain terms.</b> When an employee leaves, their LAST month is exactly the
/// one payroll needs certified — but leaving flips <c>users.is_active</c> to FALSE, and until S138 the
/// send command read its subject through two ACTIVE-ONLY paths: the org-scope validator (in-scope HR
/// got 403 "Target employee not found") and the in-lock subject read (even a GlobalAdmin got 404
/// "Employee not found"). Nobody could send a departed employee's month. The fix switches BOTH reads
/// to the S70 terminated-INCLUSIVE pair the two registration writers adopted in S136 / TASK-13603,
/// and lets ADR-040 D3 decide WHO may act: the subject's deactivation raises the floor to LocalHR or
/// above, enforced per admitting scope; the self-exemption yields to it (SEC-046 stays closed).</para>
///
/// <para><b>What these pins prove, and what they deliberately leave to other suites.</b> Every case
/// here has a deactivated subject; the ACTIVE-subject behaviour of the same command is the standing
/// S127 R2/R4 matrix in <see cref="SendCommandAuthorizationTests"/>, which must stay green unchanged
/// (the swapped validator is behaviour-identical for an active target under the same floor). The
/// windowless byte-identity half is the standing characterization pin in
/// <c>Outbox.AllocationPredicateCharacterizationTests</c>. A 403 is never counted as evidence of
/// gating: every refused month here is VALID (covered + balanced), so a 403 is the floor and nothing
/// else; a separate case proves HR-for-a-leaver is still GATED (422 on an uncovered day).</para>
///
/// <para><b>RED-on-old.</b> Against the pre-S138 command every "sends 200" case below returns 403
/// (validator: "Target employee not found") for LocalHR/LocalAdmin and 404 (active-only subject read)
/// for GlobalAdmin; the terminated-self Employee case returned 404 and now returns the explicit D3
/// 403 — a DELIBERATE status change, the same one S136 recorded for the Skema save.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SendCommandTerminatedSubjectTests : SendCommandMatrixTestBase
{
    public SendCommandTerminatedSubjectTests(SendCommandMatrixFixture fx) : base(fx) { }

    /// <summary>The leaver's last day employed in the composed "final month" cases — Friday
    /// 2026-03-13, so weekdays 2..13 are expected and 16..31 are not (ADR-040 D1: the end date is
    /// inclusive; D6: the month is still approved AS A WHOLE).</summary>
    private static readonly DateOnly LastDayEmployed = new(2026, 3, 13);

    /// <summary>A deactivated leaver whose employment window is left NULL-unbounded (D2) and whose
    /// March is fully covered. The unbounded window ISOLATES the read fix: nothing but the
    /// active-only reads could refuse this month, so a 200 is the fix and a 403/404 is the dead-end.</summary>
    private async Task SeedDeactivatedLeaverWithCoveredMonthAsync(string employeeId)
    {
        await SeedEmployeeAsync(employeeId);
        await DeactivateEmployeeAsync(employeeId);
        await CoverMonthWithAbsencesAsync(employeeId, gap: null);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  The positive half — the HR tier CAN send a deactivated leaver's month.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The dead-end, closed, month-keyed adapter: every HR-tier role (LocalHR / LocalAdmin in scope,
    /// GlobalAdmin) sends a deactivated leaver's covered month → 200 <c>EMPLOYEE_APPROVED</c>. The
    /// row carries the on-behalf audit comment (P3 — the send was NOT a self-approval) and exactly
    /// ONE event on the approval stream (no spurious second emission on the new path).
    /// RED-on-old: LocalHR/LocalAdmin 403 "Target employee not found"; GlobalAdmin 404.
    /// </summary>
    [Theory]
    [InlineData(StatsTidRoles.LocalHR)]
    [InlineData(StatsTidRoles.LocalAdmin)]
    [InlineData(StatsTidRoles.GlobalAdmin)]
    public async Task HrTier_InScope_DeactivatedLeaver_MonthKeyed_Sends200(string actorRole)
    {
        var leaver = $"t13805_hrmk_sub_{actorRole}";
        var actor = $"t13805_hrmk_act_{actorRole}";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);

        using var client = ClientForRole(actor, actorRole, Org);
        var rsp = await PostSendAsync(client, leaver);

        await AssertOkEmployeeApprovedAsync(rsp);
        var periodId = await FindPeriodIdAsync(leaver, MarchStart, MarchEnd);
        Assert.NotNull(periodId);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId!.Value))!.Status);
        Assert.Equal($"Sent on behalf of {leaver}", await ReadFirstAuditCommentAsync(periodId.Value, "SUBMITTED"));
        Assert.Equal(1L, await CountOutboxTotalAsync(StreamId(leaver)));
    }

    /// <summary>
    /// The same closure through the BY-ID adapter (the <i>Mine perioder</i> re-send route) — proving
    /// the terminated-inclusive reads live in the shared command, not in one route. A seeded DRAFT
    /// whole-month row transitions to <c>EMPLOYEE_APPROVED</c>. RED-on-old: 403.
    /// </summary>
    [Fact]
    public async Task HrInScope_DeactivatedLeaver_ById_Sends200()
    {
        const string leaver = "t13805_hr_byid_sub";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);
        var periodId = await SeedApprovalRowAsync(leaver, "DRAFT", MarchStart, MarchEnd);

        using var client = ClientForRole("t13805_hr_byid_act", StatsTidRoles.LocalHR, Org);
        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertOkEmployeeApprovedAsync(rsp);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId))!.Status);
    }

    /// <summary>
    /// THE payroll case the fix exists for — D3 × D6 composed: a leaver whose employment ENDED on
    /// Friday 2026-03-13 and who was deactivated, with facts registered ONLY through the last day
    /// employed (as D3's write gate would have allowed). In-scope HR sends March → 200, and the row
    /// keeps the whole-month geometry (D6: a partially-employed month is approved AS A WHOLE, never
    /// trimmed). The window-aware coverage from S136 was unreachable for a leaver until this fix; this
    /// pin is where the two increments meet. RED-on-old: 403 "Target employee not found".
    /// </summary>
    [Fact]
    public async Task HrInScope_DeactivatedLeaver_FinalMonth_EmployedSpanCovered_SendsWholeMonth()
    {
        const string leaver = "t13805_final_month";
        await SeedEmployeeAsync(leaver);
        await SetEmploymentWindowAsync(leaver, start: null, end: LastDayEmployed);
        await DeactivateEmployeeAsync(leaver);
        await CoverMonthWithAbsencesAsync(leaver, gap: null, to: LastDayEmployed); // nothing after the last day

        using var client = ClientForRole("t13805_final_month_hr", StatsTidRoles.LocalHR, Org);
        var rsp = await PostSendAsync(client, leaver);

        await AssertOkEmployeeApprovedAsync(rsp);
        var periodId = await FindPeriodIdAsync(leaver, MarchStart, MarchEnd); // the FULL month's natural key
        Assert.NotNull(periodId);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId!.Value))!.Status);
    }

    /// <summary>
    /// R2 on the new path — "admitted then gated identically": HR-for-a-leaver is authorised (not
    /// 403) but STILL subject to the coverage gate. One employed weekday left bare → the ordinary
    /// coverage 422 naming EXACTLY that day, and no row is written. This is what keeps the 200s above
    /// from being read as "the terminated-inclusive path bypasses validation".
    /// </summary>
    [Fact]
    public async Task HrInScope_DeactivatedLeaver_UncoveredEmployedDay_IsGated422_NoRow()
    {
        const string leaver = "t13805_hr_gated";
        await SeedEmployeeAsync(leaver);
        await DeactivateEmployeeAsync(leaver);
        await CoverMonthWithAbsencesAsync(leaver, gap: GapDay); // GapDay (Thu 2026-03-05) left bare

        using var client = ClientForRole("t13805_hr_gated_hr", StatsTidRoles.LocalHR, Org);
        var rsp = await PostSendAsync(client, leaver);

        await AssertCoverage422Async(rsp); // gated, not 403 and not 200
        var body = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync()).RootElement;
        var missing = body.GetProperty("missingDays").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Equal(new[] { GapDay.ToString("yyyy-MM-dd") }, missing);
        Assert.False(await RowExistsAsync(leaver, MarchStart, MarchEnd));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  The floor — LocalHR or above, per ADMITTING SCOPE (R9b + R9f1), on a valid month.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A sub-HR leader IN scope → 403 on both adapters, no side effect. Two rules coincide here and
    /// both say 403: the S127 R4 send floor (a leader may not send for ANOTHER employee — already
    /// 403 for an active subject) and the S136 D3 floor (writes for a deactivated subject require
    /// LocalHR or above — the R9b/R9f1 gates the swapped validator carries). The month is valid, so
    /// the 403 is the floor, not the gate.
    /// </summary>
    [Fact]
    public async Task LeaderInScope_DeactivatedLeaver_Is403_NoRow_BothAdapters()
    {
        const string leaver = "t13805_leader_sub";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);
        using var leader = ClientForRole("t13805_leader_act", StatsTidRoles.LocalLeader, Org);

        var monthKeyed = await PostSendAsync(leader, leaver);
        await AssertStatusAsync(monthKeyed, HttpStatusCode.Forbidden);
        Assert.False(await RowExistsAsync(leaver, MarchStart, MarchEnd));

        var periodId = await SeedApprovalRowAsync(leaver, "DRAFT", MarchStart, MarchEnd);
        var byId = await PostEmployeeApproveAsync(leader, periodId);
        await AssertStatusAsync(byId, HttpStatusCode.Forbidden);
        Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status); // untouched

        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(leaver)));
    }

    /// <summary>
    /// Org scope is still respected on the terminated-inclusive path: LocalHR whose ORG_ONLY scope
    /// sits on a DIFFERENT org (STY01, exact-match scoping per S93) → 403, no row. Being HR is
    /// necessary but not sufficient — the admitting scope must cover the leaver's org.
    /// </summary>
    [Fact]
    public async Task HrOutOfScope_DeactivatedLeaver_Is403_NoRow()
    {
        const string leaver = "t13805_hr_oos_sub";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);

        using var client = ClientForRole("t13805_hr_oos_act", StatsTidRoles.LocalHR, OtherOrg);
        var rsp = await PostSendAsync(client, leaver);

        await AssertStatusAsync(rsp, HttpStatusCode.Forbidden);
        Assert.False(await RowExistsAsync(leaver, MarchStart, MarchEnd));
    }

    /// <summary>
    /// R9f1 on the send surface: a MIXED-role JWT holding LocalHR over a DISJOINT org (STY01) plus a
    /// LocalLeader scope that DOES cover the leaver's org (STY02) carries primary role LocalHR, yet
    /// must be refused — the scope that admits a terminated subject must ITSELF be LocalHR or above.
    /// Without the per-scope floor the Leader scope would carry the send.
    /// </summary>
    [Fact]
    public async Task MixedRoleJwt_HrDisjoint_LeaderCovering_DeactivatedLeaver_Is403_NoRow()
    {
        const string leaver = "t13805_mixed_sub";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);

        using var client = RoleClient("t13805_mixed_act", StatsTidRoles.LocalHR, OtherOrg,
            new RoleScope(StatsTidRoles.LocalHR, OtherOrg, "ORG_ONLY"),   // HR — but over STY01
            new RoleScope(StatsTidRoles.LocalLeader, Org, "ORG_ONLY"));   // covers STY02 — but as a Leader
        var rsp = await PostSendAsync(client, leaver);

        await AssertStatusAsync(rsp, HttpStatusCode.Forbidden);
        Assert.False(await RowExistsAsync(leaver, MarchStart, MarchEnd));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  SEC-046 held — the leaver's OWN still-valid token cannot send.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The self-exemption YIELDS to subject deactivation (ADR-040 D3; SEC-046). The leaver's own
    /// still-valid JWT — as an Employee (which bypasses the validator, so the in-lock D3 re-check is
    /// the load-bearing refusal) and as a LocalLeader (refused by the validator's R9b gate) — gets
    /// 403 on both adapters, writes nothing, and the body carries no date literal (D3 redaction).
    ///
    /// <para>DELIBERATE STATUS CHANGE for the Employee case: pre-S138 this was a 404 ("Employee not
    /// found") — the active-only subject read hiding the leaver by accident. The refusal is now the
    /// explicit D3 floor (a decision, auditable), the same change S136 recorded for the Skema save.
    /// The window is NULL-unbounded here, so the 403 proves the ROLE floor fires on deactivation
    /// alone — independent of the window gate.</para>
    /// </summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee)]
    [InlineData(StatsTidRoles.LocalLeader)]
    public async Task TerminatedSelf_LiveToken_Is403_NoRow_BothAdapters(string ownRole)
    {
        var leaver = $"t13805_self_{ownRole}";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);
        using var own = ClientForRole(leaver, ownRole, Org);

        var monthKeyed = await PostSendAsync(own, leaver);
        var raw = await monthKeyed.Content.ReadAsStringAsync();
        Assert.True(monthKeyed.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)monthKeyed.StatusCode}: {raw}");
        Assert.Contains("Access denied", raw);
        Assert.DoesNotContain("2026-", raw); // D3 redaction: no date, not even the month's
        Assert.False(await RowExistsAsync(leaver, MarchStart, MarchEnd));

        var periodId = await SeedApprovalRowAsync(leaver, "DRAFT", MarchStart, MarchEnd);
        var byId = await PostEmployeeApproveAsync(own, periodId);
        await AssertStatusAsync(byId, HttpStatusCode.Forbidden);
        Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status); // untouched

        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(leaver)));
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId));
    }

    /// <summary>
    /// Access outranks status on the self path too: a terminated Employee's own token against a month
    /// that is ALREADY <c>EMPLOYEE_APPROVED</c> gets the D3 403, not the 409 the source-state gate
    /// would give an authorised caller. The refused actor learns nothing about the month's state, and
    /// the row is untouched. (Pre-S138 this ordering was the other way round — 409 before the
    /// active-only 404 — because the subject was read only after the status gate.)
    /// </summary>
    [Fact]
    public async Task TerminatedSelf_AlreadyApprovedMonth_Is403_NotStatusLeak()
    {
        const string leaver = "t13805_self_locked";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(leaver);
        var periodId = await SeedApprovalRowAsync(leaver, "EMPLOYEE_APPROVED", MarchStart, MarchEnd);

        using var own = EmployeeClient(leaver);
        var rsp = await PostSendAsync(own, leaver);

        await AssertStatusAsync(rsp, HttpStatusCode.Forbidden);
        Assert.DoesNotContain("EMPLOYEE_APPROVED", await rsp.Content.ReadAsStringAsync());
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId))!.Status);
    }

    /// <summary>
    /// ACCEPTED BEHAVIOUR, pinned as a decision on record — the S136 Step-5a adjudication (a)
    /// applied to the send surface by the same D3 letter: a lifecycle-DEACTIVATED LocalHR actor's
    /// still-valid JWT CAN send their OWN covered month → 200. The floor reads "subject deactivated ⇒
    /// LocalHR or above", and this actor IS LocalHR; the validator admits self through its own HR
    /// scope (R9b + R9f1 both clear), and the in-lock D3 re-check passes for the same reason. The
    /// deactivated-self 403 above bites sub-HR tokens only. If this pin reddens, someone re-decided
    /// the adjudication — take it back to the owner, do not "fix" silently.
    /// </summary>
    [Fact]
    public async Task DeactivatedHrActor_OwnCoveredMonth_Sends200_AdjudicatedAccepted()
    {
        const string hrActor = "t13805_deact_hr_self";
        await SeedDeactivatedLeaverWithCoveredMonthAsync(hrActor);

        using var client = ClientForRole(hrActor, StatsTidRoles.LocalHR, Org);
        var rsp = await PostSendAsync(client, hrActor);

        await AssertOkEmployeeApprovedAsync(rsp);
        var periodId = await FindPeriodIdAsync(hrActor, MarchStart, MarchEnd);
        Assert.NotNull(periodId);
        Assert.Equal("Employee self-approval", await ReadFirstAuditCommentAsync(periodId!.Value, "SUBMITTED"));
    }
}
