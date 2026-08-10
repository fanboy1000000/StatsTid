using System.Net;
using StatsTid.SharedKernel.Security;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12711 — AC-11: the R2 + R4 authorization matrix on BOTH send adapters.
///
/// <para>R4: a leader may not send for ANOTHER employee (⇒ 403, an authorization decision, NOT a 422);
/// self, or LocalHR-and-above acting for another, is admitted then gated identically (R2 — no HR
/// override of the gate). The discrimination the AC demands: a 403 is NEVER counted as evidence of
/// gating. Every send-for-another target here carries a VALID (covered + balanced) month, so an
/// admitted actor gets 200 — the LocalLeader's 403 is therefore purely the R4 floor, not the month
/// failing; and a separate case proves HR-for-another is still subject to the gate (422 on a bad
/// month), which is what "admitted then gated identically" means.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SendCommandAuthorizationTests : SendCommandMatrixTestBase
{
    public SendCommandAuthorizationTests(SendCommandMatrixFixture fx) : base(fx) { }

    // ── Self-send: allowed and gated, all five roles, both adapters ──────────────────────────────

    /// <summary>Self-send is admitted for every role (the floor is inert when self==actor) and then
    /// gated — a valid month returns 200. Includes the LocalLeader self-send the AC calls out
    /// explicitly (a leader is also an employee sending their OWN month).</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee)]
    [InlineData(StatsTidRoles.LocalLeader)]
    [InlineData(StatsTidRoles.LocalHR)]
    [InlineData(StatsTidRoles.LocalAdmin)]
    [InlineData(StatsTidRoles.GlobalAdmin)]
    public async Task SelfSend_MonthKeyed_AllRoles_AdmittedAndGated(string role)
    {
        var emp = $"t711a_selfmk_{role}";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);

        using var client = ClientForRole(emp, role, Org);
        await AssertOkEmployeeApprovedAsync(await PostSendAsync(client, emp));
    }

    /// <summary>The same self matrix through the BY-ID adapter (a seeded DRAFT whole-month row).</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee)]
    [InlineData(StatsTidRoles.LocalLeader)]
    [InlineData(StatsTidRoles.LocalHR)]
    [InlineData(StatsTidRoles.LocalAdmin)]
    [InlineData(StatsTidRoles.GlobalAdmin)]
    public async Task SelfSend_ById_AllRoles_AdmittedAndGated(string role)
    {
        var emp = $"t711a_selfid_{role}";
        await SeedEmployeeAsync(emp);
        await CoverMonthWithAbsencesAsync(emp, gap: null);
        var periodId = await SeedApprovalRowAsync(emp, "DRAFT", MarchStart, MarchEnd);

        using var client = ClientForRole(emp, role, Org);
        await AssertOkEmployeeApprovedAsync(await PostEmployeeApproveAsync(client, periodId));
    }

    // ── Send-for-another: R4 floor (LocalLeader ⇒ 403), R2 (HR+ admitted then gated) ─────────────

    /// <summary>Send-for-another on the MONTH-KEYED adapter. Employee and LocalLeader ⇒ 403 (R4 floor —
    /// an AUTHORIZATION denial on a valid month, never a 422); LocalHR / LocalAdmin / GlobalAdmin ⇒
    /// admitted and gated, and because the month is valid, 200. A 403 leaves NO row behind.</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee, 403)]
    [InlineData(StatsTidRoles.LocalLeader, 403)]
    [InlineData(StatsTidRoles.LocalHR, 200)]
    [InlineData(StatsTidRoles.LocalAdmin, 200)]
    [InlineData(StatsTidRoles.GlobalAdmin, 200)]
    public async Task SendForAnother_MonthKeyed_MatrixR2R4(string actorRole, int expected)
    {
        var subject = $"t711a_famk_sub_{actorRole}";
        var actor = $"t711a_famk_act_{actorRole}";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null); // a VALID month, so 403 ≠ gate refusal

        using var client = ClientForRole(actor, actorRole, Org);
        var rsp = await PostSendAsync(client, subject);

        await AssertStatusAsync(rsp, (HttpStatusCode)expected);
        if (expected == 403)
            Assert.False(await RowExistsAsync(subject, MarchStart, MarchEnd)); // no side effect
        else
            await AssertOkEmployeeApprovedAsync(rsp);
    }

    /// <summary>The same matrix through the BY-ID adapter (the arm that was UNFLOORED before S127). A
    /// seeded DRAFT whole-month row; the 403 cases leave it DRAFT.</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee, 403)]
    [InlineData(StatsTidRoles.LocalLeader, 403)]
    [InlineData(StatsTidRoles.LocalHR, 200)]
    [InlineData(StatsTidRoles.LocalAdmin, 200)]
    [InlineData(StatsTidRoles.GlobalAdmin, 200)]
    public async Task SendForAnother_ById_MatrixR2R4(string actorRole, int expected)
    {
        var subject = $"t711a_faid_sub_{actorRole}";
        var actor = $"t711a_faid_act_{actorRole}";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null);
        var periodId = await SeedApprovalRowAsync(subject, "DRAFT", MarchStart, MarchEnd);

        using var client = ClientForRole(actor, actorRole, Org);
        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertStatusAsync(rsp, (HttpStatusCode)expected);
        if (expected == 403)
            Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status);
        else
            await AssertOkEmployeeApprovedAsync(rsp);
    }

    /// <summary>R2, the half a 200 cannot prove (MONTH-KEYED adapter): HR-for-another is admitted but
    /// STILL subject to the gate — an UNBALANCED month returns 422 <c>kind:"allocation"</c>, not 200 (no
    /// HR override) and not 403 (they are authorized). This is "admitted then gated identically".</summary>
    [Fact]
    public async Task SendForAnother_Hr_UnbalancedMonth_IsGated_Not403_MonthKeyed()
    {
        const string subject = "t711a_hrgate_sub";
        const string hr = "t711a_hrgate_hr";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null);
        await WorkedAsync(subject, GapDay, intervals: new[] { ("08:00", "15:24") }); // unbalanced

        using var client = ClientForRole(hr, StatsTidRoles.LocalHR, Org);
        var rsp = await PostSendAsync(client, subject);

        await AssertAllocation422Async(rsp); // gated, not 403 and not 200
        Assert.False(await RowExistsAsync(subject, MarchStart, MarchEnd));
    }

    /// <summary>S127 Step-7a F9 — R2 on the BY-ID adapter, which the month-keyed case above could not
    /// cover. HR-for-another over a seeded DRAFT whole-month row that is UNBALANCED is admitted (R2, not
    /// 403) then gated to 422 <c>kind:"allocation"</c> — pinning "admitted then gated" on the by-id path
    /// too. The 422 fires before any state change, so the row is left DRAFT (proving the 422 is the gate,
    /// not a 403 that never touched the row).</summary>
    [Fact]
    public async Task SendForAnother_Hr_UnbalancedMonth_IsGated_Not403_ById()
    {
        const string subject = "t711a_hrgate_byid_sub";
        const string hr = "t711a_hrgate_byid_hr";
        await SeedEmployeeAsync(subject);
        await CoverMonthWithAbsencesAsync(subject, gap: null);
        await WorkedAsync(subject, GapDay, intervals: new[] { ("08:00", "15:24") }); // unbalanced
        var periodId = await SeedApprovalRowAsync(subject, "DRAFT", MarchStart, MarchEnd);

        using var client = ClientForRole(hr, StatsTidRoles.LocalHR, Org);
        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertAllocation422Async(rsp); // admitted (R2), then gated — not 403 and not 200
        Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status); // untouched by the 422 (no side effect)
    }
}
