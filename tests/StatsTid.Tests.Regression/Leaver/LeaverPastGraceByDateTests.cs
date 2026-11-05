using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14725-T (Step 7a W-1; owner rulings 2026-11-05 "(a) Fix it now", then "(a) Extend now") — from
/// D+6 (the leaver's last day employed is before today minus 5) a leaver counts as switched off on EVERY route,
/// decided by the DATE, whether or not the leaver job's Step A has flipped <c>is_active</c> yet.
///
/// <para><b>Plain language.</b> Mette's manager may approve her final month for 5 days after her last day. From the
/// sixth day only HR may. The automatic switch-off runs on a 5-minute poll, so before this fix there was a window
/// (Copenhagen midnight to the next poll, or as long as the job failed) in which her manager could still approve
/// her month, read her Skema, and decide her overtime. These facts seed that window directly: the leaver is still
/// <c>is_active = TRUE</c> with an end date 6 days back, on a host whose leaver job is OFF
/// (<c>withoutLeaverJob: true</c>, TASK-14719's fixture), so Step A can never flip her mid-fact and every 403 below
/// is the DATE rule and nothing else.</para>
///
/// <para><b>Two layers, both pinned.</b> The approval EDGE and the unit-leader path never pass through the org-scope
/// validator, so the shared approver predicate (<c>DesignatedApproverAuthorizer.IsPastManagerGrace</c>, clause
/// <c>manager-grace-ended</c>) carries the date rule for them; every leader read and overtime write goes through
/// <c>OrgScopeValidator</c> (clause <c>past-grace-as-switched-off</c>). In sections (a) to (f) the manager is a LocalLeader whose
/// authority is the designated reporting-line edge (and, in the unit fact, a unit-leader row): the approve handler admits
/// an HR-tier actor through the org-scope arm before it asks the edge question, so an HR-tier manager would make those
/// facts pass or fail for the wrong reason (the S147 P-6 setup rule). Section (g) is the deliberate exception: it asks
/// what the audit LABEL says when an HR officer is also the manager, so its actors (<c>HrMgr</c>, <c>HrUnitLdr</c>) ARE
/// HR-tier, and its <c>LeaverM</c> seed (switched off by hand, no end date) has no date rule to act on at all.</para>
///
/// <para><b>Dates.</b> Host clock pinned to <see cref="F"/> (Wednesday 2026-06-10, Copenhagen day). D+6 means an end
/// date of F-6, D+5 an end date of F-5 (the last grace day). The month under approval is May 2026.</para>
///
/// <para><b>Docker</b> — CI-pending, never locally green. Kinds are named per fact; none can run locally, so each
/// "Red conditions" names the production behaviour that makes its assertion fail on master <c>c13024cd</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LeaverPastGraceByDateTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgA = "STY02";
    private const string HrGateReason = "Terminated-employee access requires LocalHR or above";

    /// <summary>The pinned "today" (Copenhagen business date): a Wednesday.</summary>
    private static readonly DateOnly F = new(2026, 6, 10);
    private static readonly DateOnly EndDay6 = F.AddDays(-6); // D+6: past the grace
    private static readonly DateOnly EndDay5 = F.AddDays(-5); // D+5: the last grace day

    private const string Leaver = "s147g_leaver";        // Employee; manager = Mgr via the designated edge
    private const string LeaverU = "s147g_leaver_u";     // Employee in unit U; NO edge; UnitLeader leads U
    private const string Mgr = "s147g_mgr";              // LocalLeader; PRIMARY manager of Leaver (edge only)
    private const string UnitLeader = "s147g_unitldr";   // LocalLeader; designated unit leader of U (no edge to LeaverU)
    private const string Hr = "s147g_hr";                // LocalHR over OrgA

    private static readonly Guid UnitU = Guid.Parse("51470000-0000-0000-0000-0000000000a1");

    // TASK-14725-T fix-up 2: the HR actors who ALSO hold a manager position, and a stand-in.
    private const string LeaverH = "s147g_leaver_h";     // Employee; PRIMARY edge manager = HrMgr
    private const string HrMgr = "s147g_hrmgr";          // LocalHR AND the designated (edge) manager of LeaverH
    private const string LeaverV = "s147g_leaver_v";     // Employee in unit V; NO edge; HrUnitLdr leads V
    private const string HrUnitLdr = "s147g_hrunitldr";  // LocalHR AND the designated unit leader of V
    private const string LeaverM = "s147g_leaver_m";     // Employee switched off by hand (no end date); edge manager = HrMgr
    private const string Vik = "s147g_vik";              // LocalLeader; active manager_vikar stand-in for Mgr
    private static readonly Guid UnitV = Guid.Parse("51470000-0000-0000-0000-0000000000a2");

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        // No leaver-flip job (TASK-14719's reason): otherwise Step A, which polls at host start and every 5
        // minutes, could switch the D+6 leaver off mid-fact and the facts would test the SWITCHED-OFF case
        // (which already passes on master) instead of the window this task closes.
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        // PAT-008 boot order: the FIXED host boots (and runs the seeders) BEFORE any fixture is seeded.
        _host = _factory.WithFixedToday(F);
        _ = _host.CreateClient();

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (a) approve: the manager is refused from D+6, HR is admitted
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F</b>, approve through the designated EDGE. The leaver is still ACTIVE with last day F-6 (D+6, Step A
    /// has not run). The manager's approve is 403 (reason: the LocalHR text, no date), the month stays
    /// EMPLOYEE_APPROVED; then HR's approve of the same month is 200 and the month becomes APPROVED.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal(Forbidden, mgr.StatusCode)</c>. On
    /// <c>c13024cd</c> the org-scope arm refuses the LocalLeader scope at its LocalHR floor (as it does today), and
    /// the designated-approver predicate, which had no date clause, admits the manager through the EDGE: the manager
    /// gets 200 and the month becomes APPROVED.
    /// <b>Mutation table (each ONE mutation alone turns this fact RED).</b> The approve handler asks the
    /// terminated-inclusive validator at a LocalHR floor first, and a LocalLeader scope never clears that floor (so the
    /// scope arm refuses a leader whatever the clause says); it then asks the edge predicate. <b>M-26</b>
    /// (<c>IsPastManagerGrace</c> returns false): the edge arm admits, so the manager gets 200 and
    /// <c>Assert.Equal(Forbidden, ...)</c> fails. <b>M-25</b> (the validator's past-grace rule off): the scope arm still
    /// refuses, but with the reason "Actor scope does not cover target organization" instead of the LocalHR text
    /// (the subject no longer counts as switched off, so the R9b gate is not reached), so the status stays 403 and
    /// <c>Assert.Contains(HrGateReason, ...)</c> fails. The HR half (<c>Assert.Equal(OK, hr.StatusCode)</c>) keeps the
    /// refusal from being "everyone is refused".</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_ManagerApproveEdge_Is403_HrApprove_Is200()
    {
        await SetEndDateAsync(Leaver, EndDay6);
        var period = await InsertPeriodAsync(Leaver, "EMPLOYEE_APPROVED");

        var mgr = await Client(Mgr).PostAsync($"/api/approval/{period}/approve", null);
        var mgrBody = await mgr.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
        Assert.Contains(HrGateReason, mgrBody);
        Assert.DoesNotContain(EndDay6.ToString("yyyy-MM-dd"), mgrBody); // ADR-040 D7: no date in a body
        Assert.Equal("EMPLOYEE_APPROVED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(Leaver), "the fixture must be the window: still is_active = TRUE");

        var hr = await Client(Hr, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hr.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
    }

    /// <summary>
    /// <b>Kind F</b>, approve through a designated UNIT LEADER row (no reporting-line edge to the leaver). Same state
    /// as above. The unit leader's approve is 403 and the month stays EMPLOYEE_APPROVED.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal(Forbidden, ...)</c>. On <c>c13024cd</c> the
    /// org-scope arm refuses the LocalLeader scope at its LocalHR floor, and the unit-leader leg of the predicate,
    /// which had no date clause, admits (200).
    /// <b>Mutation table.</b> <b>M-26</b> alone (<c>IsPastManagerGrace</c> returns false) turns it RED: the scope arm
    /// refuses a LocalLeader at the LocalHR floor regardless, then the unit-leader leg admits, so 200. <b>M-25</b> alone
    /// does NOT: it only changes the refusal's reason text, and this fact asserts the status and the unchanged month,
    /// not the reason, so it stays green under M-25 (the approve-edge fact above carries the reason assertion).</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_UnitLeaderApprove_Is403()
    {
        await SetEndDateAsync(LeaverU, EndDay6);
        var period = await InsertPeriodAsync(LeaverU, "EMPLOYEE_APPROVED");

        var rsp = await Client(UnitLeader).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(LeaverU));
    }

    /// <summary>
    /// <b>Kind B — boundary pin, holds today (the mirror of the two facts above).</b> One day earlier, last day
    /// F-5 (D+5, the LAST grace day): the manager approves through the edge (200, APPROVED) and the unit leader
    /// approves through the unit-leader row (200, APPROVED). Proves the D+6 refusals are about the DATE and not about
    /// the fixture (a broken seed would refuse here too).
    /// <para><b>Red conditions:</b> an off-by-one in the shared <c>IsPastGrace</c> (<c>end &lt;= today-5</c>), or in the
    /// approver predicate's use of it (<c>DesignatedApproverAuthorizer.IsPastManagerGrace</c>), makes one of these 403.
    /// A validator-only off-by-one does NOT: on approve the org-scope arm already refuses these LocalLeader actors at
    /// its LocalHR floor, so only the reason changes and the edge / unit-leader path still admits
    /// (<c>ApprovalEndpoints.cs:447-456</c>). The validator's own D+5 boundary is pinned by
    /// <c>DepartedSelfValidatorTests</c>. (Fix-up 4 c3, Codex fix-up 4 c2 WARNING.)</para>
    /// </summary>
    [Fact]
    public async Task Day5_LeaverStillActive_ManagerAndUnitLeaderApprove_Is200_BoundaryPin()
    {
        await SetEndDateAsync(Leaver, EndDay5);
        await SetEndDateAsync(LeaverU, EndDay5);
        var pEdge = await InsertPeriodAsync(Leaver, "EMPLOYEE_APPROVED");
        var pUnit = await InsertPeriodAsync(LeaverU, "EMPLOYEE_APPROVED");

        Assert.Equal(HttpStatusCode.OK, (await Client(Mgr).PostAsync($"/api/approval/{pEdge}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(UnitLeader).PostAsync($"/api/approval/{pUnit}/approve", null)).StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pEdge));
        Assert.Equal("APPROVED", await ReadStatusAsync(pUnit));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (b) reads: the leader's Skema month view
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F</b>, the leader-tier READ. The leaver's May is seeded as SENT (EMPLOYEE_APPROVED) before the first read.
    /// At D+6 with the leaver still active, the manager's Skema month view is 403 and the 403's reason is the date rule's
    /// ("Target employee not found", never "The month has not been submitted for approval"); HR's is 200 (HR is not
    /// month-gated, and the employed-month bound is inert for a still-active subject, <c>ApprovalEndpoints.cs:71-72</c>).
    /// At D+5 the manager's is 200 (the mirror, kind B).
    /// <para><b>Why the month is seeded as sent.</b> Since S124 the leader tier of this view is month-gated
    /// (<c>SkemaEndpoints.cs:535-553</c>, <c>ApprovalVisibility.IsSubmittedToManager</c>): a leader reading a month with no
    /// period, or a DRAFT one, is refused 403 "The month has not been submitted for approval"
    /// (<c>ApprovalReadTier.MonthNotSubmittedForbidden</c>, <c>ApprovalReadTier.cs:116-121</c>). Without the period every
    /// manager read here is refused by THAT gate, so the D+6 half passed for a reason that is not the date (on
    /// <c>c13024cd</c> and under either mutation too: vacuous) and the D+5 mirror, which needs a 200, failed in CI
    /// (run 42013444380). HR is not month-gated, which is why HR's 200 never noticed.</para>
    /// <para><b>Red conditions (Docker, argued):</b> the D+6 manager assertions
    /// (<c>Assert.Equal(Forbidden, ...)</c> and <c>Assert.Contains("Target employee not found", ...)</c>). Trace on HEAD:
    /// the tier classification does not admit the LocalLeader at HR tier, so the read is leader tier
    /// (<c>ApprovalReadTier.cs:105-107</c>; at D+6 the refusal is the primary-role gate for a subject past the grace while
    /// active, <c>OrgScopeValidator.cs:356-361</c>, at D+5 the per-scope LocalHR floor); the active-only validator answers "Target employee not found" for a subject
    /// past the grace by date (<c>OrgScopeValidator.cs:160-161</c>) at <c>SkemaEndpoints.cs:237</c>; the edge arm at
    /// <c>:246</c> asks <c>IsEffectiveApproverOrUnitLeaderAsync</c>, whose <c>manager-grace-ended</c> clause refuses
    /// (<c>DesignatedApproverAuthorizer.cs:401</c>); so <c>:249</c> answers 403 with that reason. The month gate is not
    /// reached (the 403 returns at :249, and the month is sent anyway).
    /// <b>On <c>c13024cd</c></b> (no date rule): the same classification, then the validator resolves the still-active
    /// leaver and the covering LocalLeader scope admits at <c>:237</c>, the month is sent, so the manager gets 200 and
    /// <c>Assert.Equal(Forbidden, ...)</c> fails. Both layers matter, and EITHER mutation alone turns the manager
    /// assertion RED. <b>M-25</b> alone (validator rule off): the validator admits at <c>:237</c> (covering scope, active
    /// subject), the edge arm is not asked, the month is sent: 200. <b>M-26</b> alone (predicate off,
    /// <c>IsPastManagerGrace</c> false): the validator still answers "Target employee not found" at <c>:237</c>, then the
    /// edge arm at <c>:246</c> admits Mgr (the edge Leaver to Mgr is live, as the approve facts prove), so no 403: 200.
    /// The reason assertions (<c>Contains "Target employee not found"</c>, <c>DoesNotContain "has not been
    /// submitted"</c>) make it impossible for a 403 from the month gate, or from anything but this clause, to satisfy the
    /// fact. The D+5 mirror needs only ONE admitting path on the last grace day, so an off-by-one in a single layer is
    /// masked here (a validator-only error falls through to the live edge at <c>:246</c>; a predicate-only error is
    /// bypassed by the covering scope at <c>:237</c>). It turns red only under an error both layers share, the common
    /// grace rule <c>EmploymentEndDateLifecycleWriter.IsPastGrace</c> (<c>OrgScopeValidator.cs:602</c>,
    /// <c>DesignatedApproverAuthorizer.cs:423</c>), or a broken seed. Each layer's own D+5 boundary is pinned elsewhere
    /// (the approve boundary fact above for the predicate; <c>DepartedSelfValidatorTests</c> and, at route level, the
    /// year-overview fact's D+5 mirror below for the validator). (Fix-up 4 c2, Codex WARNING; Fable fix-up 4 NOTE-1,
    /// NOTE-2, NOTE-4.)</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_ManagerSkemaMonthView_Is403_HrIs200_Day5ManagerIs200()
    {
        await SetEndDateAsync(Leaver, EndDay6);
        await InsertPeriodAsync(Leaver, "EMPLOYEE_APPROVED"); // sent: the S124 leader-tier month gate must not be what refuses

        var mgr = await Client(Mgr).GetAsync($"/api/skema/{Leaver}/month?year=2026&month=5");
        var mgrBody = await mgr.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
        Assert.Contains("Target employee not found", mgrBody);
        Assert.DoesNotContain("has not been submitted", mgrBody);

        var hr = await Client(Hr, StatsTidRoles.LocalHR).GetAsync($"/api/skema/{Leaver}/month?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, hr.StatusCode);

        await SetEndDateAsync(Leaver, EndDay5);
        var mirror = await Client(Mgr).GetAsync($"/api/skema/{Leaver}/month?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, mirror.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (c) overtime pre-approval approve: leader refused from D+6
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F</b>, an overtime WRITE through the active-only validator. A PENDING pre-approval for the leaver:
    /// at D+6 (still active) the leader's approve is 403 with reason "Target employee not found" (the answer a
    /// switched-off person gets) and the row stays PENDING.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal(Forbidden, ...)</c>. On <c>c13024cd</c> the
    /// validator resolves the still-active leaver and the leader's covering scope admits: 200, row APPROVED. This
    /// route never touches the edge predicate, so ONLY the validator layer refuses here. <b>M-25</b> alone
    /// (the active-only past-grace branch off) turns it RED (200, row APPROVED); <b>M-26</b> alone leaves it green.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_LeaderOvertimePreApprovalApprove_Is403_RowStaysPending()
    {
        await SetEndDateAsync(Leaver, EndDay6);
        var id = await InsertOvertimePreApprovalAsync(Leaver);

        var rsp = await Client(Mgr).PutAsync($"/api/overtime/pre-approval/{id}/approve", null);
        var body = await rsp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Contains("Target employee not found", body);
        Assert.Equal("PENDING", await ReadOvertimeStatusAsync(id));
    }

    /// <summary>
    /// <b>Kind B — boundary pin, holds today.</b> The same overtime approve at D+5 is 200 and the row becomes
    /// APPROVED: inside the grace the leader still decides.
    /// <para><b>Red conditions:</b> an off-by-one in the validator's grace test makes this 403.</para>
    /// </summary>
    [Fact]
    public async Task Day5_LeaverStillActive_LeaderOvertimePreApprovalApprove_Is200_BoundaryPin()
    {
        await SetEndDateAsync(Leaver, EndDay5);
        var id = await InsertOvertimePreApprovalAsync(Leaver);

        var rsp = await Client(Mgr).PutAsync($"/api/overtime/pre-approval/{id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadOvertimeStatusAsync(id));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (f) reject, and the year overview (TASK-14725-T fix-up 1, Fable N5)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F</b>, reject through the designated EDGE. Same state as the approve fact (leaver still ACTIVE, last day
    /// F-6, job off). The manager's reject is 403 with the LocalHR text, the month stays EMPLOYEE_APPROVED; then HR's
    /// reject of the same month is 200 and the month becomes REJECTED.
    /// <para><b>Red conditions (Docker, argued):</b> on <c>c13024cd</c> the manager's reject is 200 (the active leaver is
    /// an ordinary subject; the edge has no date clause), so <c>Assert.Equal(Forbidden, ...)</c> fails.
    /// <b>Mutation table (each ONE mutation alone turns it RED).</b> Reject runs the same two arms as approve
    /// (<c>ApprovalEndpoints.cs</c> ~:696-710): the terminated-inclusive validator at the LocalHR floor, which a
    /// LocalLeader scope never clears, then the edge predicate. <b>M-26</b> alone: the edge arm admits, 200.
    /// <b>M-25</b> alone: the scope arm's reason becomes "Actor scope does not cover target organization" (the subject no
    /// longer counts as switched off, so the R9b gate is not reached), so <c>Assert.Contains(HrGateReason, ...)</c>
    /// fails while the status stays 403.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_ManagerRejectEdge_Is403_HrReject_Is200()
    {
        await SetEndDateAsync(Leaver, EndDay6);
        var period = await InsertPeriodAsync(Leaver, "EMPLOYEE_APPROVED");

        var mgr = await Client(Mgr).PostAsJsonAsync($"/api/approval/{period}/reject", new { reason = "s147 past-grace pin" });
        var mgrBody = await mgr.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
        Assert.Contains(HrGateReason, mgrBody);
        Assert.DoesNotContain(EndDay6.ToString("yyyy-MM-dd"), mgrBody); // ADR-040 D7
        Assert.Equal("EMPLOYEE_APPROVED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(Leaver), "the fixture must be the window: still is_active = TRUE");

        var hr = await Client(Hr, StatsTidRoles.LocalHR).PostAsJsonAsync($"/api/approval/{period}/reject", new { reason = "s147 past-grace pin" });
        Assert.Equal(HttpStatusCode.OK, hr.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(period));
    }

    /// <summary>
    /// <b>Kind F</b>, the leader's YEAR OVERVIEW (<c>GET /api/balance/{id}/year-overview</c>, <c>BalanceEndpoints.cs</c>
    /// ~:710). At D+6 with the leaver still active, a leader whose scope covers her organisation gets 403 and HR gets
    /// 200; at D+5 the same leader gets 200 (the mirror, kind B). This route uses only the terminated-inclusive validator (no edge arm).
    /// <para><b>Red conditions (Docker, argued):</b> on <c>c13024cd</c> the validator treats the active leaver as an
    /// ordinary subject and the covering leader scope admits, so the leader gets 200 and
    /// <c>Assert.Equal(Forbidden, ...)</c> fails. <b>Mutation table.</b> <b>M-25</b> alone turns it RED (the leader is
    /// admitted: 200). <b>M-26</b> alone leaves it green, because this route never asks the edge predicate. The HR half
    /// keeps "everyone is refused" from passing. <b>Other gates (audited, fix-up 4):</b> this route has no month gate and
    /// no employed-month bound, and an ACTIVE leaver with a covering leader scope clears the validator, so the only thing
    /// that can answer 403 with the LocalHR text is the date rule (<c>OrgScopeValidator.cs</c>, the
    /// <c>pastGraceWhileActive</c> branch of the terminated-inclusive overload). The D+5 mirror (the same leader, last day
    /// F-5) is 200: the same request on the same seed, so a 200 there proves the D+6 403 is the date and not the fixture
    /// (HR's 200 at D+6 reaches the same body, so the body itself works for this seed).</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_LeaderYearOverview_Is403_HrIs200()
    {
        await SetEndDateAsync(Leaver, EndDay6);

        var leader = await Client(Mgr).GetAsync($"/api/balance/{Leaver}/year-overview?year=2026");
        var leaderBody = await leader.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, leader.StatusCode);
        Assert.Contains(HrGateReason, leaderBody);

        var hr = await Client(Hr, StatsTidRoles.LocalHR).GetAsync($"/api/balance/{Leaver}/year-overview?year=2026");
        Assert.Equal(HttpStatusCode.OK, hr.StatusCode);

        // The D+5 mirror (fix-up 4): the same leader on the same seed, one day earlier, inside the grace.
        await SetEndDateAsync(Leaver, EndDay5);
        var mirror = await Client(Mgr).GetAsync($"/api/balance/{Leaver}/year-overview?year=2026");
        Assert.Equal(HttpStatusCode.OK, mirror.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (g) TASK-14725 fix-up 2 (Step 7a cycle 2, Codex WARNING): the RECORDED approval_method follows the date
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F</b>, approve, the audit label. Plain language: from the sixth day the system refuses every manager
    /// path, so an HR officer who also happens to be the leaver's manager acts as HR, and the audit must say HR
    /// (<c>ORG_SCOPE_FALLBACK</c>), not "the manager approved". The leaver is still ACTIVE, last day F-6, job off.
    /// HrMgr (LocalHR AND the designated edge manager) approves: 200, month APPROVED, and
    /// <c>approval_periods.approval_method = ORG_SCOPE_FALLBACK</c>.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal("ORG_SCOPE_FALLBACK", method)</c>. On
    /// <c>076ac29e</c> <c>DeriveApprovalMethodAsync</c> tested the edge first (<c>actor == resolved approver</c>) and
    /// recorded <c>DESIGNATED_MANAGER</c>. On <c>d145f85a</c> the handler evaluates the authority predicate in-lock for
    /// every actor; at D+6 its <c>manager-grace-ended</c> clause refuses, so <c>holdsManagerPath</c> is false and step (0)
    /// returns the fallback. <b>Mutation (fix-up 3):</b> step (0) ignores <c>holdsManagerPath</c> (or the handler passes
    /// <c>true</c>): the edge step answers and the assertion fails with <c>DESIGNATED_MANAGER</c>. The 200 and status
    /// assertions keep it from passing on a refusal.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback()
    {
        await SetEndDateAsync(LeaverH, EndDay6);
        var period = await InsertPeriodAsync(LeaverH, "EMPLOYEE_APPROVED");

        var rsp = await Client(HrMgr, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(LeaverH), "the fixture must be the window: still is_active = TRUE");
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadMethodAsync(period));
    }

    /// <summary>
    /// <b>Kind F</b>, reject, the audit label, through a UNIT LEADER row. Same state (D+6, still active, job off).
    /// HrUnitLdr (LocalHR AND the designated unit leader of the leaver's unit; no edge to the leaver) rejects: 200,
    /// month REJECTED, <c>approval_method = ORG_SCOPE_FALLBACK</c>.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal("ORG_SCOPE_FALLBACK", method)</c>. On
    /// <c>076ac29e</c> the unit-leader step of the classification ran while the subject was still active (its
    /// same-organisation re-check reads active users), so the label was <c>UNIT_LEADER</c>. On <c>d145f85a</c> the
    /// predicate refuses at D+6 (grace clause), <c>holdsManagerPath</c> is false, step (0) returns the fallback.
    /// <b>Mutation (fix-up 3):</b> step (0) ignores <c>holdsManagerPath</c> (or reject passes <c>true</c>, ~:800): the
    /// unit-leader resolution still names HrUnitLdr (subject active), so the label is <c>UNIT_LEADER</c>.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_HrWhoIsUnitLeaderRejects_MethodIsOrgScopeFallback()
    {
        await SetEndDateAsync(LeaverV, EndDay6);
        var period = await InsertPeriodAsync(LeaverV, "EMPLOYEE_APPROVED");

        var rsp = await Client(HrUnitLdr, StatsTidRoles.LocalHR)
            .PostAsJsonAsync($"/api/approval/{period}/reject", new { reason = "s147 fix-up 2 pin" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(LeaverV));
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadMethodAsync(period));
    }

    /// <summary>
    /// <b>Kind B — boundary pin, holds today.</b> D+5, the LAST grace day: the same HR-who-is-the-manager approves and
    /// the method stays <c>DESIGNATED_MANAGER</c> (inside the grace the manager position is real authority and the
    /// audit says so). Proves the D+6 label is about the DATE, not a blanket "HR always records the fallback".
    /// <para><b>Red conditions:</b> an off-by-one in the predicate's grace clause (<c>end &lt;= today-5</c>), or
    /// <c>holdsManagerPath</c> forced false (step (0) always falling back), records <c>ORG_SCOPE_FALLBACK</c> here.</para>
    /// </summary>
    [Fact]
    public async Task Day5_LeaverStillActive_HrWhoIsDesignatedManagerApproves_MethodStaysDesignatedManager_BoundaryPin()
    {
        await SetEndDateAsync(LeaverH, EndDay5);
        var period = await InsertPeriodAsync(LeaverH, "EMPLOYEE_APPROVED");

        var rsp = await Client(HrMgr, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
        Assert.Equal("DESIGNATED_MANAGER", await ReadMethodAsync(period));
    }

    /// <summary>
    /// <b>Kind F</b>, AFTER the flip. The leaver is switched off (<c>is_active = FALSE</c>, last day F-6, as Step A
    /// leaves her). HrMgr, still her designated edge manager (the switch-off does not end the leaver's own reporting
    /// line), approves: 200 and <c>approval_method = ORG_SCOPE_FALLBACK</c>.
    /// <para><b>Red conditions (Docker, argued):</b> on <c>076ac29e</c> the edge test is
    /// <c>actor == resolved approver</c> and never looks at the subject, so the manager label survived the
    /// switch-off: <c>DESIGNATED_MANAGER</c>, and <c>Assert.Equal("ORG_SCOPE_FALLBACK", ...)</c> fails. On
    /// <c>d145f85a</c> the predicate refuses (grace clause and the inactive-subject re-check), so
    /// <c>holdsManagerPath</c> is false. <b>Mutation (fix-up 3):</b> step (0) ignores <c>holdsManagerPath</c>; the
    /// edge step (<c>actor == resolved approver</c>, which survives the switch-off) answers <c>DESIGNATED_MANAGER</c>.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverSwitchedOff_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback()
    {
        await SetEndDateAsync(LeaverH, EndDay6);
        await SetActiveAsync(LeaverH, false);
        var period = await InsertPeriodAsync(LeaverH, "EMPLOYEE_APPROVED");

        var rsp = await Client(HrMgr, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
        Assert.False(await IsActiveAsync(LeaverH), "the fixture must be the switched-off state");
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadMethodAsync(period));
    }

    /// <summary>
    /// <b>Kind F at D+6, kind B at D+5</b> (Fable Step 7a cycle 2, N-6): a <c>manager_vikar</c> STAND-IN for the leaver's
    /// manager. Vik (LocalLeader) is the active stand-in for Mgr, so the edge resolves to Vik (seeded through
    /// <c>ManagerVikarRepository.CreateAsync</c>, the precedent in <c>DesignatedApproverAuthorityTests.CreateVikarRawAsync</c>).
    /// At D+6 with the leaver still active her approve is 403 and the month stays EMPLOYEE_APPROVED; at D+5 the same
    /// month is 200, APPROVED, recorded <c>ACTING_MANAGER</c>.
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal(Forbidden, ...)</c> at D+6. On <c>c13024cd</c>
    /// (before TASK-14725) the org-scope arm refuses a LocalLeader at its LocalHR floor, then the edge predicate (no date
    /// clause) admits the resolved stand-in: 200. <b>Mutation:</b> <c>IsPastManagerGrace</c> returns false (M-26):
    /// the edge arm admits Vik at D+6, 200. The D+5 half is the boundary pin (holds today).
    /// <b>Checked against <c>d145f85a</c> (fix-up 3):</b> the D+6 half still targets M-26 and no other mutation of
    /// fix-up 3. Vik is a LocalLeader, so the org-scope arm never admits her; her admission is exactly
    /// <c>holdsManagerPath</c> (<c>if (!orgScopeAllowed &amp;&amp; !holdsManagerPath) 403</c>), whose date clause is
    /// <c>IsPastManagerGrace</c>. The label mutation ("step (0) ignores <c>holdsManagerPath</c>") cannot turn this
    /// half RED because a refused request never reaches the label. At D+5 the predicate holds and step (1) names
    /// <c>ACTING_MANAGER</c>.</para>
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_ManagerVikarStandIn_Is403_Day5Is200()
    {
        await using (var conn = _dbFactory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await new ManagerVikarRepository(_dbFactory).CreateAsync(conn, tx, new ManagerVikar
            {
                VikarId = Guid.NewGuid(),
                AbsentApproverId = Mgr,
                VikarUserId = Vik,
                UntilDate = new DateOnly(2099, 12, 31),
                Reason = "FERIE",
                OrganisationId = OrgA,
                Version = 1,
                CreatedBy = "TEST",
            });
            await tx.CommitAsync();
        }

        await SetEndDateAsync(Leaver, EndDay6);
        var period = await InsertPeriodAsync(Leaver, "EMPLOYEE_APPROVED");
        var refused = await Client(Vik).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadStatusAsync(period));
        Assert.True(await IsActiveAsync(Leaver), "the fixture must be the window: still is_active = TRUE");

        await SetEndDateAsync(Leaver, EndDay5);
        var admitted = await Client(Vik).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
        Assert.Equal("ACTING_MANAGER", await ReadMethodAsync(period));
    }

    /// <summary>
    /// <b>Kind F</b> (fix-up 3, Fable WARNING-1): a subject switched off BY HAND, with no end date. LeaverM is
    /// <c>is_active = FALSE</c>, <c>employment_end_date IS NULL</c>, <c>end_date_deactivated = FALSE</c> (what
    /// <c>PUT /api/admin/users/{userId}</c> leaves: it sets only <c>is_active</c>; there is no deactivation-reason
    /// column in the schema, the brief's name for it does not exist). Her designated edge manager HrMgr is LocalHR.
    /// HrMgr approves her submitted month: 200, APPROVED, <c>approval_method = ORG_SCOPE_FALLBACK</c>.
    /// <para><b>Gates checked:</b> the terminated-inclusive scope gate admits a LocalHR scope for an inactive target
    /// (the R9b floor is LocalHR); the employed-month bound passes because the window is both-NULL, which
    /// <c>EmploymentWindow.Overlaps</c> treats as unbounded. So the seed reaches approve.</para>
    /// <para><b>Red conditions (Docker, argued):</b> <c>Assert.Equal("ORG_SCOPE_FALLBACK", method)</c>. On
    /// <c>d997ee12</c> the only subject test was the manager-grace date, and a NULL end date is never past, so the
    /// edge step recorded <c>DESIGNATED_MANAGER</c>. On <c>d145f85a</c> the predicate's active-user re-check refuses, so
    /// <c>holdsManagerPath</c> is false. <b>Mutation:</b> step (0) ignores <c>holdsManagerPath</c> (or the date-only
    /// rule is restored): the edge step answers <c>DESIGNATED_MANAGER</c>.</para>
    /// </summary>
    [Fact]
    public async Task ManuallySwitchedOff_NoEndDate_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback()
    {
        await SetActiveAsync(LeaverM, false);
        var period = await InsertPeriodAsync(LeaverM, "EMPLOYEE_APPROVED");

        var rsp = await Client(HrMgr, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(period));
        Assert.False(await IsActiveAsync(LeaverM), "the fixture must be the manual switch-off");
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadMethodAsync(period));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed + helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await Exec(conn,
            "INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES (@u, @org, NULL, 'kontor', 'S147 Unit G')",
            ("u", UnitU), ("org", OrgA));
        await Exec(conn,
            "INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES (@u, @org, NULL, 'kontor', 'S147 Unit V')",
            ("u", UnitV), ("org", OrgA));

        await InsertUserAsync(conn, Leaver, "EMPLOYEE");
        await InsertUserAsync(conn, LeaverU, "EMPLOYEE", unit: UnitU);
        await InsertUserAsync(conn, Mgr, "LOCAL_LEADER");
        await InsertUserAsync(conn, UnitLeader, "LOCAL_LEADER", unit: UnitU); // a leader is a member of her own unit (D3)
        await InsertUserAsync(conn, Hr, "LOCAL_HR");
        await InsertUserAsync(conn, LeaverH, "EMPLOYEE");
        await InsertUserAsync(conn, HrMgr, "LOCAL_HR");
        await InsertUserAsync(conn, LeaverV, "EMPLOYEE", unit: UnitV);
        await InsertUserAsync(conn, HrUnitLdr, "LOCAL_HR", unit: UnitV); // a leader is a member of her own unit (D3)
        await InsertUserAsync(conn, Vik, "LOCAL_LEADER");
        await InsertUserAsync(conn, LeaverM, "EMPLOYEE");

        await Exec(conn, "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@u, @m)", ("u", UnitU), ("m", UnitLeader));
        await Exec(conn, "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@u, @m)", ("u", UnitV), ("m", HrUnitLdr));

        var rl = new ReportingLineRepository(_dbFactory);
        await rl.AssignAsync(null, Line(Leaver, Mgr));
        await rl.AssignAsync(null, Line(LeaverH, HrMgr));
        await rl.AssignAsync(null, Line(LeaverM, HrMgr));
    }

    private static async Task InsertUserAsync(NpgsqlConnection conn, string id, string roleId, Guid? unit = null)
    {
        await Exec(conn,
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES (@id, @id, '$2a$11$fake', @id, @email, @org, @unit, 'HK', 'OK24', TRUE)
            """,
            ("id", id), ("email", id + "@test.dk"), ("org", OrgA), ("unit", (object?)unit ?? DBNull.Value));
        await Exec(conn,
            "INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by, expires_at) VALUES (@id, @role, @org, 'ORG_ONLY', 'TEST', NULL)",
            ("id", id), ("role", roleId), ("org", OrgA));
    }

    private static ReportingLineModel Line(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = OrgA,
        Relationship = "PRIMARY",
        EffectiveFrom = F.AddYears(-1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private static async Task Exec(NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        // NpgsqlBatch (not NpgsqlCommand) so CA2100's warning budget does not grow: every caller passes a
        // literal, and the values travel as parameters.
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(sql);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        batch.BatchCommands.Add(cmd);
        await batch.ExecuteNonQueryAsync();
    }

    /// <summary>Sets the leaver's last day and leaves <c>is_active</c> TRUE: the real window between the end of the
    /// grace and Step A's poll.</summary>
    private async Task SetEndDateAsync(string userId, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE users SET employment_end_date = @d WHERE user_id = @id", ("d", end), ("id", userId));
    }

    private async Task SetActiveAsync(string userId, bool active)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE users SET is_active = @a WHERE user_id = @id", ("a", active), ("id", userId));
    }

    private async Task<string?> ReadMethodAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT approval_method FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        var r = await cmd.ExecuteScalarAsync();
        return r is DBNull or null ? null : (string)r;
    }

    private async Task<bool> IsActiveAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> InsertPeriodAsync(string employeeId, string status)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await Exec(conn,
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp, NOW(), @emp)
            """,
            ("id", id), ("emp", employeeId), ("org", OrgA),
            ("start", new DateOnly(2026, 5, 1)), ("end", new DateOnly(2026, 5, 31)), ("status", status));
        return id;
    }

    private async Task<string?> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<Guid> InsertOvertimePreApprovalAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await Exec(conn,
            """
            INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status, reason)
            VALUES (@id, @emp, @start, @end, 10, 'PENDING', 'S147 TASK-14725-T')
            """,
            ("id", id), ("emp", employeeId),
            ("start", new DateOnly(2026, 5, 1)), ("end", new DateOnly(2026, 5, 31)));
        return id;
    }

    private async Task<string?> ReadOvertimeStatusAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM overtime_pre_approvals WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private HttpClient Client(string userId, string role = StatsTidRoles.LocalLeader)
    {
        var client = _host.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: OrgA, scopes: new[] { new RoleScope(role, OrgA, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
