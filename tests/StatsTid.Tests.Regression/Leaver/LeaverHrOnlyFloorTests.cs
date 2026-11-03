using System.Net;
using StatsTid.SharedKernel.Security;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-6; owner ruling OQ-2; refinement R-15, R-25) — the floors of "HR only from day 6", and the month bound
/// that sits AFTER them.
///
/// <para><b>Plain language.</b> From day 6 a switched-off leaver's final month can be handled by an HR-tier person and by nobody else.
/// These facts prove three things on the three decision handlers (approve, reject, reopen's leader arm) and on the Skema month view.
/// (1) The people who must stay out do: a plain leader in scope, the leaver's own manager, a manager who has himself left or been
/// switched off, a token whose only scope over the leaver is a leader scope, and an ordinary employee. (2) Staying out is the SAME answer
/// whatever month is asked about: if a refused person got "not found" for one month and "outside the employment period" for another, that
/// difference would tell them when the leaver was employed. (3) An HR person who IS admitted gets a refusal that carries no date when the
/// month holds no employed day, and the same bytes for a month before the start and a month after the end.</para>
///
/// <para><b>Where state comes from.</b> The leaver is seeded SWITCHED OFF directly (<c>is_active = FALSE</c>) on a Backend host that
/// has no leaver job, clock pinned to D+6 (Thursday 19 March 2026); the one grace-state fact pins D+3. The refusal body, asserted byte
/// for byte: 422 <c>{error:"month_outside_employment_period", kind:"employment-window", message:"Måneden ligger uden for
/// ansættelsesperioden."}</c> (the Orchestrator's ruling on TASK-14713's deviation 1), and not one digit in it.</para>
///
/// <para><b>Mutations (Docker-only, argued).</b> M-floor: the gate admits any caller (or reads the active-only validator) answers 200/
/// the wrong reason and trips the status / reason assertions. M-order: the bound placed BEFORE the gate gives a gate-refused actor a 422
/// on a non-employed month, so the differential fact's equal-body assertion fails. M-bound: dropping the bound answers 200 for January
/// and April, so the 422 assertions fail. M-boundary: an exclusive end date or start date answers 422 for the one-day months of the
/// boundary fact.</para>
///
/// <para>Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverHrOnlyFloorTests : LeaverHrOnlyTestBase
{
    public LeaverHrOnlyFloorTests(LeaverHrOnlyFixture fx) : base(fx) { }

    private async Task<(string Token, string? Reason)> ActorForAsync(string kind, string leaver) => kind switch
    {
        // A LocalLeader holding a leader scope over the leaver's organisation: no edge, no HR.
        "leader" => (RoleToken(NewId("fl_act"), StatsTidRoles.LocalLeader), BelowHrReason),
        // The leaver's own designated manager (the edge): the person who held the month through day 5.
        "manager" => (RoleToken(await SeedManagerAsync(Cs, "fl_mgr", leaver), StatsTidRoles.LocalLeader), BelowHrReason),
        // The same edge holder, manually switched off: the edge path's activity test refuses him on top of everything else.
        "manager_off" => (RoleToken(await SeedManagerAsync(Cs, "fl_mgroff", leaver, active: false), StatsTidRoles.LocalLeader), BelowHrReason),
        // The same edge holder, still switched on but past his own last day (the grace state), refused by the same test.
        "manager_gone" => (RoleToken(await SeedManagerAsync(Cs, "fl_mgrgone", leaver, endDate: Day(-3)), StatsTidRoles.LocalLeader), BelowHrReason),
        // Primary role LocalHR, but the HR scope is over a DISJOINT organisation and the covering scope is a leader scope (R9f1).
        "mixed" => (MixedRoleToken(NewId("fl_mix")), ScopeNotCoveringReason),
        // An ordinary employee: refused by the route's policy (approve, reject) or by the employee arm (reopen); no reason is pinned.
        _ => (RoleToken(NewId("fl_emp"), StatsTidRoles.Employee), null),
    };

    /// <summary>
    /// AC-230-6, "from D+6 refused". Kind <b>B</b> on the status and the untouched state (boundary pin, holds today: every caller is
    /// refused today) and kind <b>F</b> on the reason text of the leader legs (today "Target employee not found"). For approve, reject
    /// and reopen's leader arm, a switched-off leaver's EMPLOYEE_APPROVED March is refused 403 to: a plain leader in scope, the leaver's
    /// designated manager, that manager switched off, that manager past his last day, a mixed-role token whose only covering scope is a
    /// leader scope, and an Employee token. The month stays EMPLOYEE_APPROVED, nobody is recorded as approver, and no audit row or event is
    /// written. The leader legs answer "Terminated-employee access requires LocalHR or above"; the mixed-role leg answers "Actor scope
    /// does not cover target organization" (its primary role passes the HR gate, its covering scope is below the floor).
    ///
    /// <para><b>Red conditions.</b> The reason assertions fail on today's code ("Target employee not found"). The 403 and the
    /// unchanged-state assertions are the boundary: mutation M-floor (a widening that admits a leader scope or the edge) answers 200 and
    /// moves the month.</para>
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reopen")]
    public async Task FromDay6_NonHrActors_AreRefused403_MonthUnchanged_Handler(string action)
    {
        foreach (var kind in new[] { "leader", "manager", "manager_off", "manager_gone", "mixed", "employee" })
        {
            var leaver = await SeedLeaverAsync(Cs, $"fl_{action}_{kind}");
            var periodId = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED");
            var (token, expectedReason) = await ActorForAsync(kind, leaver);

            using var client = ClientAt(Day(6), token);
            var (_, reason) = await AssertForbiddenAsync(
                await ActAsync(action, client, leaver, periodId, 3), $"{action} by {kind}");

            if (expectedReason is not null)
                Assert.Equal(expectedReason, reason);
            var row = await ReadPeriodAsync(Cs, periodId);
            Assert.True(row.Status == "EMPLOYEE_APPROVED", $"{action} by {kind} must leave the month EMPLOYEE_APPROVED, got {row.Status}");
            Assert.Null(row.ApprovedBy);
            Assert.Equal(0L, await CountApprovalAuditAsync(Cs, periodId));
            Assert.Equal(0L, await CountApprovalEventsAsync(Cs, leaver, 3));
        }
    }

    /// <summary>
    /// AC-230-6, "an actor the gate refuses gets the same 403 for every month" — a differential fact per handler (approve, reject,
    /// reopen, the Skema view). Kind <b>B</b> (boundary pin, holds today: every month is refused today).
    ///
    /// <para>The leaver was employed 2 February to 13 March 2026 and is switched off. January (before the start), March (employed) and
    /// April (after the end) each carry an EMPLOYEE_APPROVED row. A plain leader in scope and a mixed-role token each ask about all three
    /// months, on every handler, and each gets 403, and the three raw bodies are IDENTICAL: the gate answers before the month bound,
    /// so nobody the gate refuses can learn, from the difference between two answers, which months the leaver was employed.</para>
    ///
    /// <para><b>Red conditions (argued).</b> Mutation M-order: the bound placed before the gate answers the plain leader 422 for January
    /// and April and 403 for March, so the 403 assertion and the equal-bodies assertion both fail. Holds today because every month is
    /// refused at the active-only gate.</para>
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reopen")]
    [InlineData("skema")]
    public async Task Differential_GateRefusedActor_GetsTheSame403_ForEmployedAndNonEmployedMonths_Handler(string handler)
    {
        var leaver = await SeedLeaverAsync(Cs, $"fl_diff_{handler}", start: new DateOnly(2026, 2, 2), end: LastDay);
        var periods = new Dictionary<int, Guid>
        {
            [1] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 1),
            [3] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 3),
            [4] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 4),
        };

        foreach (var kind in new[] { "leader", "mixed" })
        {
            var (token, _) = await ActorForAsync(kind, leaver);
            using var client = ClientAt(Day(6), token);
            var raws = new List<string>();
            foreach (var month in new[] { 1, 3, 4 })
            {
                var (raw, _) = await AssertForbiddenAsync(
                    await ActAsync(handler, client, leaver, periods[month], month), $"{handler} by {kind}, month {month}");
                Assert.DoesNotContain("month_outside_employment_period", raw);
                raws.Add(raw);
            }
            Assert.Equal(raws[0], raws[1]);
            Assert.Equal(raws[1], raws[2]);
        }
    }

    /// <summary>
    /// AC-230-6, "an admitted HR actor gets the date-free refusal for a month with no employed day". Kind <b>F</b> (today HR is refused
    /// 403 "Target employee not found" for a switched-off subject). Per handler (approve, reject, reopen, the Skema view): an in-scope
    /// LocalHR asking about January (before the leaver's start) and April (after her end) gets the 422 EXACTLY as ruled, with no
    /// date in it, and the SAME bytes for both months; the month's state is untouched (no status change, no audit row, no event). The
    /// same HR asking about March (employed) is admitted and the handler does its job (200; APPROVED / REJECTED / DRAFT / the view),
    /// so the 422 is about the month, not a blanket.
    ///
    /// <para><b>Red conditions.</b> Today every call answers 403 ("Target employee not found"): <c>AssertDateFreeRefusalAsync</c> (status
    /// 422) fails for January and the March 200 fails. Mutation M-bound (argued, Docker-only): dropping the bound answers 200 for
    /// January and April.</para>
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reopen")]
    [InlineData("skema")]
    public async Task AdmittedHr_MonthWithNoEmployedDay_GetsTheDateFree422_SameBytesBeforeAndAfter_March200_Handler(string handler)
    {
        var leaver = await SeedLeaverAsync(Cs, $"fl_hr_{handler}", start: new DateOnly(2026, 2, 2), end: LastDay);
        await CoverWeekdaysAsync(Cs, leaver, new DateOnly(2026, 3, 2), LastDay);
        var periods = new Dictionary<int, Guid>
        {
            [1] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 1),
            [3] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 3),
            [4] = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 4),
        };
        var hr = NewId("fl_hr");
        using var client = HrAt(Day(6), hr);

        var january = await AssertDateFreeRefusalAsync(await ActAsync(handler, client, leaver, periods[1], 1));
        var april = await AssertDateFreeRefusalAsync(await ActAsync(handler, client, leaver, periods[4], 4));
        Assert.Equal(january, april); // before-start and after-end are indistinguishable (ADR-040 D3)

        foreach (var month in new[] { 1, 4 })
        {
            var row = await ReadPeriodAsync(Cs, periods[month]);
            Assert.Equal("EMPLOYEE_APPROVED", row.Status);
            Assert.Null(row.ApprovedBy);
            Assert.Equal(0L, await CountApprovalAuditAsync(Cs, periods[month]));
        }
        Assert.Equal(0L, await CountApprovalEventsAsync(Cs, leaver, 1));
        Assert.Equal(0L, await CountApprovalEventsAsync(Cs, leaver, 4));

        await AssertStatusAsync(await ActAsync(handler, client, leaver, periods[3], 3), HttpStatusCode.OK, $"{handler} of the employed March");
        Assert.Equal(StatusAfter(handler), (await ReadPeriodAsync(Cs, periods[3])).Status);
    }

    /// <summary>
    /// AC-230-6, the inclusive ends of the employment window (ADR-040 D1, D2). Kind <b>F</b>. A leaver employed 31 March and 1 April 2026
    /// only (start date the last day of March, end date the first of April), switched off: HR's approve of March (one employed day, the
    /// start date) is 200, HR's approve of April (one employed day, the end date) is 200, and February (before the start) and May (after
    /// the end) are the date-free 422. A bound that treated either end as exclusive would refuse March or April.
    ///
    /// <para><b>Red conditions.</b> Today all four answer 403. Mutation M-boundary (argued, Docker-only): an exclusive end or start in the
    /// window read answers 422 for April or March.</para>
    /// </summary>
    [Fact]
    public async Task AdmittedHr_OneEmployedDayMonths_AreAdmitted_BothEndsInclusive()
    {
        var leaver = await SeedLeaverAsync(Cs, "fl_edge", start: new DateOnly(2026, 3, 31), end: new DateOnly(2026, 4, 1));
        var feb = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 2);
        var mar = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 3);
        var apr = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 4);
        var may = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED", month: 5);
        using var client = HrAt(Day(6), NewId("fl_edge_hr"));

        await AssertDateFreeRefusalAsync(await ApproveAsync(client, feb));
        await AssertDateFreeRefusalAsync(await ApproveAsync(client, may));
        await AssertStatusAsync(await ApproveAsync(client, mar), HttpStatusCode.OK, "approve of March (its one employed day is the start date)");
        await AssertStatusAsync(await ApproveAsync(client, apr), HttpStatusCode.OK, "approve of April (its one employed day is the end date)");
        Assert.Equal("APPROVED", (await ReadPeriodAsync(Cs, mar)).Status);
        Assert.Equal("APPROVED", (await ReadPeriodAsync(Cs, apr)).Status);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadPeriodAsync(Cs, feb)).Status);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadPeriodAsync(Cs, may)).Status);
    }

    /// <summary>
    /// AC-230-6, "an actor the edge path's activity test refuses (manually deactivated, or past their end date) is refused on that path"
    /// (AC-230-15, TASK-14712). Kind <b>R</b> — a regression pin of the activity test on the surface this task widened: it holds today.
    /// During the grace (D+3) the leaver is still ACTIVE, so the designated-manager edge is live. A manager who has himself been switched
    /// off, or is past his own last day, is refused 403 and the month does not move; a manager in employment with the same edge, on a
    /// separate leaver, approves 200 (the control that proves the refusal is the manager's own state, not a broken fixture).
    ///
    /// <para><b>Red conditions (argued).</b> Mutation: the edge path's activity test dropped (a departed manager treated as acting) answers
    /// 200 for the departed managers and fails the 403 assertion; the control's 200 guards the other direction.</para>
    /// </summary>
    [Theory]
    [InlineData("switched_off")]
    [InlineData("past_last_day")]
    public async Task InGrace_DepartedManager_IsRefusedOnTheEdgePath_InEmploymentManagerApproves200(string managerState)
    {
        var day = Day(3);
        var leaver = await SeedLeaverAsync(Cs, $"fl_grace_{managerState}", deactivated: false);
        var departed = await SeedManagerAsync(
            Cs, "fl_gone", leaver, active: managerState != "switched_off", endDate: managerState == "past_last_day" ? Day(-3) : null);
        var periodId = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED");

        using (var gone = LeaderAt(day, departed))
            await AssertForbiddenAsync(await ApproveAsync(gone, periodId), $"approve by a manager who is {managerState}");
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadPeriodAsync(Cs, periodId)).Status);

        var control = await SeedLeaverAsync(Cs, $"fl_grace_ctl_{managerState}", deactivated: false);
        var controlManager = await SeedManagerAsync(Cs, "fl_ok", control);
        var controlPeriod = await SeedPeriodAsync(Cs, control, "EMPLOYEE_APPROVED");
        using (var ok = LeaderAt(day, controlManager))
            await AssertStatusAsync(await ApproveAsync(ok, controlPeriod), HttpStatusCode.OK, "the in-employment manager's approve in the grace");
        Assert.Equal("APPROVED", (await ReadPeriodAsync(Cs, controlPeriod)).Status);
    }
}
