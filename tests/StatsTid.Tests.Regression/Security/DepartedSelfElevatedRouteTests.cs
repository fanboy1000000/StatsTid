using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// One Postgres container (the real <c>docker/postgres/init.sql</c>) and one booted Backend host,
/// shared by <see cref="DepartedSelfElevatedRouteTests"/>. Isolation is by case-unique ids: every
/// fact seeds its own people (a unique prefix), so facts never read each other's rows.
/// </summary>
public sealed class DepartedSelfHostFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        _ = Factory.CreateClient(); // boots the host and its seeders
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// S147 / TASK-14703-T (AC-230-5's elevated bullets with F2 and F3; AC-230-18, F4, P-1, P-10, P-13;
/// R-30, R-31) — "a departed person cannot change their own record, from any door", proven route by
/// route over a SEEDED subject: still <c>is_active = TRUE</c> with an end date in the past (the real
/// grace state), plus a deactivated variant on each terminated-inclusive route.
///
/// <para><b>Plain language.</b> When someone leaves, their own access ends the day after their last
/// working day. A leader, an HR user or an administrator reaches their OWN record through admin
/// routes, so each of these facts uses the leaver's own still-valid token on a route that would
/// otherwise let them rewrite themselves, and expects a refusal (403 carrying the date-free
/// <see cref="DepartedSubject.DenyReason"/>) with NOTHING written. Each refusal is paired with the same
/// token acting on ANOTHER person in scope, which must still work: a manager keeps reaching a leaver
/// during the grace, and HR keeps acting on leavers.</para>
///
/// <para><b>Seeding.</b> The Backend host reads the real clock (Copenhagen day). Every departed subject
/// carries a FIXED past end date (<see cref="Gone"/>, a Wednesday in March 2026), which is departed for
/// any real "today" after it, so no fact depends on the calendar day CI happens to run on. The two facts
/// that need a controlled "today" (the last-day boundary; the termination-payout request, whose own rule
/// compares the end date with today) use a derived host pinned with <c>WithFixedToday</c> (PAT-008).
/// Absences are not seeded here, so the weekday rule (S138) is not engaged.</para>
///
/// <para><b>Docker</b> — CI-pending (never locally green) until CI-W2 shows each fact green. Every fact
/// says its kind. <b>F</b> = an assertion that fails on today's code (the production behaviour that
/// fails today is named). <b>B</b> = boundary pin, holds today; it keeps a guard from over-reaching.
/// The wave-gate mutations that turn the facts RED: M-18 deletes the inline guard at the named route;
/// M-19 widens a guard from the subject key to every id the route names (turns the holder facts RED).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DepartedSelfElevatedRouteTests : IClassFixture<DepartedSelfHostFixture>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgA = "STY01";   // settlement / overtime / agreement / users / role facts
    private const string OrgB = "STY02";   // reporting-line / unit facts

    /// <summary>A fixed past last day (Wednesday 2026-03-04): the subject is departed for any real today after it.</summary>
    private static readonly DateOnly Gone = new(2026, 3, 4);

    private readonly DepartedSelfHostFixture _fx;
    public DepartedSelfElevatedRouteTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  LocalLeader (AC-230-5): overtime pre-approval create / approve / reject, compensate
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind F. A departed LocalLeader (active, past her last day) creates a pre-approval naming herself:
    /// 403 with the clause's reason and no row; the same token naming ANOTHER employee in scope: 201.
    /// Red conditions: <c>Assert 403</c> on the self call — today <c>OrgScopeValidator</c> admits the
    /// covered leader's own id (the active-only read succeeds) and the route answers 201 and inserts a row.
    /// </summary>
    [Fact]
    public async Task LocalLeader_PreApprovalCreate_NamingHerself_403_OtherEmployee_201()
    {
        var mette = await SeedAsync("ds_pa_c", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_pa_c_o", OrgA);
        using var client = ClientFor(Leader(mette, OrgA));

        var self = await client.PostAsJsonAsync("/api/overtime/pre-approval", PreApprovalBody(mette));
        await AssertDeniedAsync(self);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM overtime_pre_approvals WHERE employee_id=@e", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await client.PostAsJsonAsync("/api/overtime/pre-approval", PreApprovalBody(other));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM overtime_pre_approvals WHERE employee_id=@e", ("e", other)));
    }

    /// <summary>
    /// Kind F. Approve of her OWN pending pre-approval: 403, the row stays PENDING; approve of another
    /// employee's pending row: 200 APPROVED. Red conditions: <c>Assert 403</c> — today the validator
    /// admits the covered leader's own id and the route approves the row (200).
    /// </summary>
    [Fact]
    public async Task LocalLeader_PreApprovalApprove_OwnPending_403_OtherEmployee_200()
    {
        var mette = await SeedAsync("ds_pa_a", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_pa_a_o", OrgA);
        var own = await SeedPendingPreApprovalAsync(mette);
        var theirs = await SeedPendingPreApprovalAsync(other);
        using var client = ClientFor(Leader(mette, OrgA));

        var self = await client.PutAsJsonAsync($"/api/overtime/pre-approval/{own}/approve", new { reason = "s147" });
        await AssertDeniedAsync(self);
        Assert.Equal("PENDING", await PreApprovalStatusAsync(own));
        await AssertNoWriteTrailAsync(mette);

        var ok = await client.PutAsJsonAsync($"/api/overtime/pre-approval/{theirs}/approve", new { reason = "s147" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("APPROVED", await PreApprovalStatusAsync(theirs));
    }

    /// <summary>Kind F. As above for reject. Red condition: <c>Assert 403</c> (today the row is rejected, 200).</summary>
    [Fact]
    public async Task LocalLeader_PreApprovalReject_OwnPending_403_OtherEmployee_200()
    {
        var mette = await SeedAsync("ds_pa_r", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_pa_r_o", OrgA);
        var own = await SeedPendingPreApprovalAsync(mette);
        var theirs = await SeedPendingPreApprovalAsync(other);
        using var client = ClientFor(Leader(mette, OrgA));

        var self = await client.PutAsJsonAsync($"/api/overtime/pre-approval/{own}/reject", new { reason = "s147" });
        await AssertDeniedAsync(self);
        Assert.Equal("PENDING", await PreApprovalStatusAsync(own));
        await AssertNoWriteTrailAsync(mette);

        var ok = await client.PutAsJsonAsync($"/api/overtime/pre-approval/{theirs}/reject", new { reason = "s147" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("REJECTED", await PreApprovalStatusAsync(theirs));
    }

    /// <summary>
    /// Kind F. Compensate naming herself: 403, <c>paid_out</c> unchanged; for another employee: 200 and
    /// <c>paid_out</c> moved. Red condition: <c>Assert 403</c> (today the balance is adjusted, 200).
    /// </summary>
    [Fact]
    public async Task LocalLeader_Compensate_ForHerself_403_OtherEmployee_200()
    {
        var mette = await SeedAsync("ds_oc", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_oc_o", OrgA);
        await SeedOvertimeBalanceAsync(mette);
        await SeedOvertimeBalanceAsync(other);
        using var client = ClientFor(Leader(mette, OrgA));

        var self = await client.PostAsJsonAsync($"/api/overtime/{mette}/compensate",
            new { periodYear = 2026, hours = 1m, compensationType = "PAYOUT" });
        await AssertDeniedAsync(self);
        Assert.Equal(0m, await PaidOutAsync(mette));
        await AssertNoWriteTrailAsync(mette);

        var ok = await client.PostAsJsonAsync($"/api/overtime/{other}/compensate",
            new { periodYear = 2026, hours = 1m, compensationType = "PAYOUT" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(1m, await PaidOutAsync(other));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  LocalHR (AC-230-5): settlement resolve, reconcile-payout, termination-payout request,
    //  agreement-code PUT (terminated-inclusive: a deactivated variant each); users PUT, unit PUT
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind F (Theory: still-active self, and deactivated self). A departed LocalHR resolves a PENDING_REVIEW
    /// settlement for HERSELF: 403, the settlement stays PENDING_REVIEW. The same token for ANOTHER leaver in
    /// scope: 200 SETTLED. Red condition: <c>Assert 403</c> — today the terminated-inclusive validator
    /// admits an HR self (the S136 adjudicated residual), active or deactivated.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalHr_SettlementResolve_ForHerself_403_OtherEmployee_200(bool deactivated)
    {
        var mette = await SeedAsync("ds_sr", OrgA, active: !deactivated, end: Gone);
        var other = await SeedAsync("ds_sr_o", OrgA, active: false, end: Gone);
        await SeedVacationSettlementAsync(mette, state: "PENDING_REVIEW", payout: 0m, forfeit: 20m);
        await SeedVacationSettlementAsync(other, state: "PENDING_REVIEW", payout: 0m, forfeit: 20m);
        using var client = ClientFor(Hr(mette, OrgA));

        var self = await ResolveAsync(client, mette);
        await AssertDeniedAsync(self);
        Assert.Equal("PENDING_REVIEW", await SettlementStateAsync(mette));
        await AssertNoWriteTrailAsync(mette);

        var ok = await ResolveAsync(client, other);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("SETTLED", await SettlementStateAsync(other));
    }

    /// <summary>
    /// Kind F (Theory as above). Reconcile-payout for herself: 403, the payout marker stays unset; for
    /// another leaver: 200. Red condition: <c>Assert 403</c> (today admitted).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalHr_ReconcilePayout_ForHerself_403_OtherEmployee_200(bool deactivated)
    {
        var mette = await SeedAsync("ds_rp", OrgA, active: !deactivated, end: Gone);
        var other = await SeedAsync("ds_rp_o", OrgA, active: false, end: Gone);
        await SeedVacationSettlementAsync(mette, state: "SETTLED", payout: 5m, forfeit: 0m);
        await SeedVacationSettlementAsync(other, state: "SETTLED", payout: 5m, forfeit: 0m);
        using var client = ClientFor(Hr(mette, OrgA));

        var self = await ReconcileAsync(client, mette);
        await AssertDeniedAsync(self);
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id=@e AND payout_reconciled_by IS NOT NULL", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await ReconcileAsync(client, other);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// Kind F (Theory as above). A departed LocalHR files the §26 termination-payout request for HERSELF:
    /// 403, no request row; for another leaver: 201. Runs on a host pinned to 2026-03-05 (the route's own
    /// "end date has passed" rule reads the same clock; the end date is 2026-02-28, ferieår 2025).
    /// Red condition: <c>Assert 403</c> (today the HR self is admitted and an OPEN request row is written).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalHr_TerminationPayoutRequest_ForHerself_403_OtherEmployee_201(bool deactivated)
    {
        var endDate = new DateOnly(2026, 2, 28);
        var mette = await SeedAsync("ds_tp", OrgA, active: !deactivated, end: endDate);
        var other = await SeedAsync("ds_tp_o", OrgA, active: false, end: endDate);
        await SeedTerminationSettlementAsync(mette, endDate);
        await SeedTerminationSettlementAsync(other, endDate);
        using var host = _fx.Factory.WithFixedToday(new DateOnly(2026, 3, 5));
        using var client = ClientFor(Hr(mette, OrgA), host);

        var self = await TerminationRequestAsync(client, mette);
        await AssertDeniedAsync(self);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM termination_payout_requests WHERE employee_id=@e", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await TerminationRequestAsync(client, other);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM termination_payout_requests WHERE employee_id=@e", ("e", other)));
    }

    /// <summary>
    /// Kind F (Theory as above). The agreement-code PUT for herself: 403, no new agreement row, the live
    /// code unchanged; for another employee: 200. Red condition: <c>Assert 403</c> (today the
    /// terminated-inclusive validator admits the HR self and the new code is written).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalHr_AgreementCodePut_ForHerself_403_OtherEmployee_200(bool deactivated)
    {
        var mette = await SeedAsync("ds_ac", OrgA, active: !deactivated, end: Gone);
        var other = await SeedAsync("ds_ac_o", OrgA);
        using var client = ClientFor(Hr(mette, OrgA));

        var self = await PutAgreementCodeAsync(client, mette);
        await AssertDeniedAsync(self);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM user_agreement_codes WHERE user_id=@e", ("e", mette)));
        Assert.Equal("AC", await ScalarStringAsync("SELECT agreement_code FROM users WHERE user_id=@e", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await PutAgreementCodeAsync(client, other);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// Kind F. <c>PUT /api/admin/users/{userId}</c> for herself (still active): 403, the display name
    /// unchanged; for another user in scope: 200. Red condition: <c>Assert 403</c> — this route validates the
    /// organisation, not the employee, so today nothing asks whether the actor IS the subject (200).
    /// </summary>
    [Fact]
    public async Task LocalHr_UsersPut_ForHerself_403_OtherEmployee_200()
    {
        var mette = await SeedAsync("ds_up", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_up_o", OrgA);
        using var client = ClientFor(Hr(mette, OrgA));

        var self = await PutUserAsync(client, mette, "Renamed Self");
        await AssertDeniedAsync(self);
        Assert.Equal($"Seeded {mette}", await ScalarStringAsync("SELECT display_name FROM users WHERE user_id=@e", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await PutUserAsync(client, other, "Renamed Other");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Renamed Other", await ScalarStringAsync("SELECT display_name FROM users WHERE user_id=@e", ("e", other)));
    }

    /// <summary>
    /// Kind B — boundary pin, holds today (G, the active-only read). A DEACTIVATED self on the users PUT
    /// reuses the route's active-only subject read, so it answers 404 "User not found", as today; the guard
    /// is never reached and adds no new answer.
    /// </summary>
    [Fact]
    public async Task UsersPut_DeactivatedSelf_NotFound_AsToday()
    {
        var mette = await SeedAsync("ds_up_d", OrgA, active: false, end: Gone);
        using var client = ClientFor(Hr(mette, OrgA));

        var rsp = await PutUserAsync(client, mette, "Renamed Self");
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    /// <summary>
    /// Kind F. <c>PUT /api/admin/users/{userId}/unit</c> for herself: 403, her unit unchanged; for another
    /// user: 200. Red condition: <c>Assert 403</c> (today the HR self may move herself into a unit).
    /// </summary>
    [Fact]
    public async Task LocalHr_UnitPut_ForHerself_403_OtherEmployee_200()
    {
        var mette = await SeedAsync("ds_un", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_un_o", OrgA);
        var unit = await SeedUnitAsync(OrgA);
        using var client = ClientFor(Hr(mette, OrgA));

        var self = await PutUserUnitAsync(client, mette, unit);
        await AssertDeniedAsync(self);
        Assert.Null(await UnitOfAsync(mette));
        await AssertNoWriteTrailAsync(mette);

        var ok = await PutUserUnitAsync(client, other, unit);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(unit, await UnitOfAsync(other));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  LocalAdmin (F3, F2): role grant, role revoke
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind F (F3). A departed LocalAdmin grants a role to HERSELF: 403, no <c>role_assignments</c> row; to
    /// another user in scope: 201. (Role grant needs <c>LocalAdminOrAbove</c>, so a LocalHR token would be
    /// refused by the policy for the wrong reason: this fact uses a LocalAdmin.) Red condition:
    /// <c>Assert 403</c> — today the route checks the organisation and the grantor's level, never whether the
    /// grantee is the departed actor (201).
    /// </summary>
    [Fact]
    public async Task LocalAdmin_RoleGrant_ToHerself_403_OtherEmployee_201()
    {
        var mette = await SeedAsync("ds_rg", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_rg_o", OrgA);
        using var client = ClientFor(Admin(mette, OrgA));

        var self = await GrantAsync(client, mette);
        await AssertDeniedAsync(self);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM role_assignments WHERE user_id=@e", ("e", mette)));
        await AssertNoWriteTrailAsync(mette);

        var ok = await GrantAsync(client, other);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM role_assignments WHERE user_id=@e", ("e", other)));
    }

    /// <summary>
    /// Kind F (F2, G3). A departed LocalAdmin revokes HER OWN assignment: 403, the assignment still active
    /// (the comparison is on the assignment's USER, which the request carries only as an assignment id); the
    /// revoke of SOMEONE ELSE's assignment: 200 (the not-self pass-through). Red condition: <c>Assert 403</c> —
    /// today the route never compares the actor with the assignment's user.
    /// </summary>
    [Fact]
    public async Task LocalAdmin_RoleRevoke_OwnAssignment_403_StillActive_OtherAssignment_200()
    {
        var mette = await SeedAsync("ds_rv", OrgA, active: true, end: Gone);
        var other = await SeedAsync("ds_rv_o", OrgA);
        var own = await SeedRoleAssignmentAsync(mette, "LOCAL_LEADER", OrgA);
        var theirs = await SeedRoleAssignmentAsync(other, "LOCAL_LEADER", OrgA);
        using var client = ClientFor(Admin(mette, OrgA));

        var self = await client.PostAsJsonAsync("/api/admin/roles/revoke", new { assignmentId = own, reason = "s147" });
        await AssertDeniedAsync(self);
        Assert.True(await AssignmentActiveAsync(own), "a refused revoke must leave the assignment active");
        await AssertNoWriteTrailAsync(mette);

        var ok = await client.PostAsJsonAsync("/api/admin/roles/revoke", new { assignmentId = theirs, reason = "s147" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.False(await AssignmentActiveAsync(theirs));
    }

    /// <summary>
    /// Kind F for the next-day half, kind B for the last-day half (the boundary pair on ONE representative
    /// inline route, role grant). On a host pinned to Monday 2026-03-16: a self whose last day IS today is
    /// still employed, so the grant to herself is allowed (201 — boundary pin, holds today; mutation M-11
    /// turns it RED); a self whose last day was YESTERDAY is departed, so the grant is refused (403).
    /// Red condition (second half): <c>Assert 403</c> — today the route admits the grant.
    /// </summary>
    [Fact]
    public async Task RoleGrant_LastDayItself_Allowed_NextDay_Denied()
    {
        var clock = new DateOnly(2026, 3, 16);
        var lastDay = await SeedAsync("ds_bd_a", OrgA, active: true, end: clock);
        var dayAfter = await SeedAsync("ds_bd_b", OrgA, active: true, end: clock.AddDays(-1));
        using var host = _fx.Factory.WithFixedToday(clock);

        using (var client = ClientFor(Admin(lastDay, OrgA), host))
            Assert.Equal(HttpStatusCode.Created, (await GrantAsync(client, lastDay)).StatusCode);

        using (var client = ClientFor(Admin(dayAfter, OrgA), host))
        {
            await AssertDeniedAsync(await GrantAsync(client, dayAfter));
            Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM role_assignments WHERE user_id=@e", ("e", dayAfter)));
        }
    }

    /// <summary>
    /// Kind B — boundary pin, holds today (the active-only read). A DEACTIVATED self on role grant reuses the
    /// route's active-only target read: 404 "Target user not found", as today.
    /// </summary>
    [Fact]
    public async Task RoleGrant_DeactivatedSelf_NotFound_AsToday()
    {
        var mette = await SeedAsync("ds_rg_d", OrgA, active: false, end: Gone);
        using var client = ClientFor(Admin(mette, OrgA));

        var rsp = await GrantAsync(client, mette);
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-230-18 (F4, P-1, P-10, P-13) — the inline routes. Sixteen facts: 10 F, 6 B
    // ═════════════════════════════════════════════════════════════════════════════════════

    // ── Import ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kind F. A departed GlobalAdmin who is also an employee imports a reporting-line row naming HERSELF as
    /// the employee: 403 for the WHOLE import, no <c>reporting_lines</c> row for her and none for the other
    /// rows. Red conditions: <c>Assert 403</c> and the zero-row asserts — today only the tree's organisation is
    /// checked, so the import commits (200) and writes her own line.
    /// </summary>
    [Fact]
    public async Task Import_RowNamingDepartedSelf_403_NothingWritten()
    {
        var self = await SeedAsync("ds_im", OrgB, active: true, end: Gone);
        var mgr = await SeedAsync("ds_im_m", OrgB);
        var other = await SeedAsync("ds_im_e", OrgB);
        using var client = ClientFor(GlobalAdmin(self));

        var rsp = await client.PostAsJsonAsync("/api/admin/reporting-lines/import", new
        {
            organisationId = OrgB,
            rows = new[]
            {
                new { employeeId = other, managerId = mgr, effectiveFrom = "2026-01-01" },
                new { employeeId = self, managerId = mgr, effectiveFrom = "2026-01-01" },
            },
        });

        await AssertDeniedAsync(rsp);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM reporting_lines WHERE employee_id=ANY(@ids)", ("ids", new[] { self, other })));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind B — boundary pin, holds today (the holder / not-self case). The same departed GlobalAdmin imports
    /// rows that do NOT name her as the employee (she appears, at most, as a manager = a holder, classified
    /// out by P-1): the import proceeds as today, 200, and the other employee's line exists.
    /// </summary>
    [Fact]
    public async Task Import_WithoutSelfRow_AsToday()
    {
        var self = await SeedAsync("ds_im2", OrgB, active: true, end: Gone);
        var other = await SeedAsync("ds_im2_e", OrgB);
        using var client = ClientFor(GlobalAdmin(self));

        var rsp = await client.PostAsJsonAsync("/api/admin/reporting-lines/import", new
        {
            organisationId = OrgB,
            rows = new[] { new { employeeId = other, managerId = self, effectiveFrom = "2026-01-01" } },
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM reporting_lines WHERE employee_id=@e AND manager_id=@m", ("e", other), ("m", self)));
    }

    /// <summary>
    /// Kind B (G4) — boundary pin, holds today. A GlobalAdmin token with an EMPTY actor id imports an ordinary
    /// row: it is not treated as anybody's self, so the import proceeds (200). (The empty id can never equal a
    /// row's employee id, which the structural pass requires to be non-empty; this fact records that an
    /// id-less token never reaches the actor read, which would dereference a null id.)
    /// </summary>
    [Fact]
    public async Task Import_ActorWithEmptyId_NotTreatedAsSelf_AsToday()
    {
        var mgr = await SeedAsync("ds_im3_m", OrgB);
        var emp = await SeedAsync("ds_im3_e", OrgB);
        using var client = ClientFor(GlobalAdmin(""));

        var rsp = await client.PostAsJsonAsync("/api/admin/reporting-lines/import", new
        {
            organisationId = OrgB,
            rows = new[] { new { employeeId = emp, managerId = mgr, effectiveFrom = "2026-01-01" } },
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ── Unit delete ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kind F. The departed HR actor is a DIRECT MEMBER of the unit and deletes it: 403 BEFORE the delete (the
    /// pre-check fires before the first write), the unit not deleted, her membership unchanged, no
    /// <c>UnitDeleted</c> / <c>UserUnitChanged</c> event. Red conditions: <c>Assert 403</c> and the
    /// still-a-member assert — today the delete commits and re-homes her (204).
    /// </summary>
    [Fact]
    public async Task UnitDelete_DepartedSelfIsDirectMember_403_NothingWritten()
    {
        var self = await SeedAsync("ds_ud", OrgB, active: true, end: Gone);
        var unit = await SeedUnitAsync(OrgB);
        await SetUnitAsync(self, unit);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await DeleteUnitAsync(client, unit);

        await AssertDeniedAsync(rsp);
        Assert.False(await UnitDeletedAsync(unit));
        Assert.Equal(unit, await UnitOfAsync(self));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind F (G5). The departed actor is a LEADER of the unit (a member by the member invariant) and deletes
    /// it: 403, the unit stays, and her <c>unit_leaders</c> row survives. Red conditions: <c>Assert 403</c>
    /// and the surviving leader row — today the delete clears the leadership row too.
    /// </summary>
    [Fact]
    public async Task UnitDelete_DepartedSelfIsLeaderOfTheUnit_403_LeaderRowSurvives()
    {
        var self = await SeedAsync("ds_ud_l", OrgB, active: true, end: Gone);
        var unit = await SeedUnitAsync(OrgB);
        await SetUnitAsync(self, unit);
        await SeedUnitLeaderAsync(unit, self);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await DeleteUnitAsync(client, unit);

        await AssertDeniedAsync(rsp);
        Assert.False(await UnitDeletedAsync(unit));
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM unit_leaders WHERE unit_id=@u AND user_id=@e", ("u", unit), ("e", self)));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind B — boundary pin, holds today. ANOTHER (active) HR actor deletes a unit that the departed person
    /// is a member of (the grace; HR keeps acting on leavers): 204, the unit deleted, the departed member
    /// re-homed.
    /// </summary>
    [Fact]
    public async Task UnitDelete_OtherActor_AsToday()
    {
        var departed = await SeedAsync("ds_ud_o", OrgB, active: true, end: Gone);
        var actor = await SeedAsync("ds_ud_o_a", OrgB);
        var unit = await SeedUnitAsync(OrgB);
        await SetUnitAsync(departed, unit);
        using var client = ClientFor(Hr(actor, OrgB));

        var rsp = await DeleteUnitAsync(client, unit);

        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);
        Assert.True(await UnitDeletedAsync(unit));
        Assert.Null(await UnitOfAsync(departed));
    }

    // ── Remove (the derived guard) ───────────────────────────────────────────────────────

    /// <summary>
    /// Kind F. HR removes manager M while the departed HR actor is a PRIMARY report of M and the replacement
    /// map would re-point her own line: 403 and the whole transaction rolled back — her line is still M's,
    /// M is still active. Red conditions: <c>Assert 403</c>, her line unchanged, M active — today the removal
    /// commits and rewrites her own reporting line to the replacement (200).
    /// </summary>
    [Fact]
    public async Task Remove_DepartedActorIsDirectReport_403_RolledBack()
    {
        var self = await SeedAsync("ds_rm_p", OrgB, active: true, end: Gone);
        var manager = await SeedAsync("ds_rm_p_m", OrgB);
        var replacement = await SeedAsync("ds_rm_p_r", OrgB);
        await SeedLineAsync(self, manager, "PRIMARY");
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await RemoveAsync(client, manager, new Dictionary<string, string> { [self] = replacement });

        await AssertDeniedAsync(rsp);
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id=@e AND manager_id=@m AND relationship='PRIMARY' AND effective_to IS NULL",
            ("e", self), ("m", manager)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM reporting_lines WHERE employee_id=@e AND manager_id=@r", ("e", self), ("r", replacement)));
        Assert.True(await IsActiveAsync(manager), "the removal must roll back: the manager stays active");
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind F (renamed by P-15: the departed actor is an ACTING <i>report</i> of the removed manager; "holder"
    /// means the authority side elsewhere). HR removes M while the departed HR actor has an ACTING edge to M:
    /// 403, rolled back, her ACTING edge stays open. Red conditions: <c>Assert 403</c> and the open ACTING
    /// edge — today the removal closes every incoming ACTING edge.
    /// </summary>
    [Fact]
    public async Task Remove_DepartedActorIsActingReport_403_RolledBack()
    {
        var self = await SeedAsync("ds_rm_a", OrgB, active: true, end: Gone);
        var manager = await SeedAsync("ds_rm_a_m", OrgB);
        var primaryMgr = await SeedAsync("ds_rm_a_p", OrgB);
        await SeedLineAsync(self, primaryMgr, "PRIMARY");
        await SeedLineAsync(self, manager, "ACTING");
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await RemoveAsync(client, manager, new Dictionary<string, string>());

        await AssertDeniedAsync(rsp);
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id=@e AND manager_id=@m AND relationship='ACTING' AND effective_to IS NULL",
            ("e", self), ("m", manager)));
        Assert.True(await IsActiveAsync(manager));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind B (P-1: a replacement is a HOLDER, classified out) — boundary pin, holds today. The departed HR
    /// actor is NAMED as the replacement for a report of the removed manager; she is not an incoming report
    /// herself, so the removal proceeds: 200, the report's line now points at her, the manager is
    /// deactivated. Catches a guard that wrongly refuses on any id the route names.
    /// </summary>
    [Fact]
    public async Task Remove_DepartedActorNamedAsReplacement_AsToday()
    {
        var self = await SeedAsync("ds_rm_h", OrgB, active: true, end: Gone);
        var manager = await SeedAsync("ds_rm_h_m", OrgB);
        var report = await SeedAsync("ds_rm_h_e", OrgB);
        await SeedLineAsync(report, manager, "PRIMARY");
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await RemoveAsync(client, manager, new Dictionary<string, string> { [report] = self });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id=@e AND manager_id=@m AND relationship='PRIMARY' AND effective_to IS NULL",
            ("e", report), ("m", self)));
        Assert.False(await IsActiveAsync(manager));
    }

    /// <summary>
    /// Kind B (G6) — boundary pin, holds today. An active HR actor removes a leaver who is still active in the
    /// grace (end date passed, no reports): the clause is the identity arm only, so this is allowed: 200, the
    /// leaver deactivated.
    /// </summary>
    [Fact]
    public async Task Remove_ActorRemovesGraceLeaver_AsToday()
    {
        var actor = await SeedAsync("ds_rm_g_a", OrgB);
        var leaver = await SeedAsync("ds_rm_g", OrgB, active: true, end: Gone);
        using var client = ClientFor(Hr(actor, OrgB));

        var rsp = await RemoveAsync(client, leaver, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.False(await IsActiveAsync(leaver));
    }

    // ── Delegate (self-service stand-in) ─────────────────────────────────────────────────

    /// <summary>
    /// Kind F (Theory: still-active self; deactivated self = G7). A departed LocalLeader who manages a report
    /// creates a stand-in for HERSELF through <c>POST /api/reporting-lines/delegate</c>: 403, no
    /// <c>manager_vikar</c> row (the resolver would honour that row ahead of escalation). Red condition:
    /// <c>Assert 403</c> and zero rows — today the route creates the row (200).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delegate_DepartedActor_403_NoVikarRow(bool deactivated)
    {
        var self = await SeedAsync("ds_dl", OrgB, active: !deactivated, end: Gone);
        var report = await SeedAsync("ds_dl_e", OrgB);
        var standIn = await SeedStandInAsync("ds_dl_v", OrgB);
        await SeedLineAsync(report, self, "PRIMARY");
        using var client = ClientFor(Leader(self, OrgB));

        var rsp = await client.PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = standIn, effectiveTo = FutureDate() });

        await AssertDeniedAsync(rsp);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id=@a", ("a", self)));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind F (Theory as above; seed the delegation BEFORE departure). The departed manager clears her own
    /// delegation: 403, the <c>manager_vikar</c> row stays open. Red condition: <c>Assert 403</c> and the
    /// open row — today the route closes it (200).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelegateClear_DepartedActor_403_RowStillOpen(bool deactivated)
    {
        var self = await SeedAsync("ds_dc", OrgB, active: !deactivated, end: Gone);
        var standIn = await SeedStandInAsync("ds_dc_v", OrgB);
        await SeedVikarRowAsync(self, standIn, OrgB);
        using var client = ClientFor(Leader(self, OrgB));

        var rsp = await DeleteAsync(client, "/api/reporting-lines/delegate");

        await AssertDeniedAsync(rsp);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id=@a AND effective_to IS NULL", ("a", self)));
        await AssertNoWriteTrailAsync(self);
    }

    // ── Admin stand-in ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kind F. A departed HR actor who is ALSO a manager sets a stand-in for HERSELF through the admin route
    /// (<c>managerId</c> = her own id): 403, no <c>manager_vikar</c> row. Red condition: <c>Assert 403</c> and
    /// zero rows — today HR may set a stand-in for any manager in scope, herself included (200).
    /// </summary>
    [Fact]
    public async Task AdminVikar_DepartedActorAsManager_403()
    {
        var self = await SeedAsync("ds_av_m", OrgB, active: true, end: Gone);
        var report = await SeedAsync("ds_av_m_e", OrgB);
        var standIn = await SeedStandInAsync("ds_av_m_v", OrgB);
        await SeedLineAsync(report, self, "PRIMARY");
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await client.PostAsJsonAsync($"/api/admin/reporting-lines/{self}/vikar",
            new { vikarUserId = standIn, effectiveTo = FutureDate(), reason = "FERIE" });

        await AssertDeniedAsync(rsp);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id=@a", ("a", self)));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind B (P-13) — boundary pin, holds today. <b>The departed HR actor herself</b> calls
    /// <c>POST /api/admin/reporting-lines/{otherManagerId}/vikar</c> for a DIFFERENT manager, naming HERSELF as
    /// the stand-in (<c>VikarUserId</c> = actor, a HOLDER, classified out): as today, 200 and the row exists.
    /// This is the fact that catches a guard that wrongly refuses a departed caller who appears only as the
    /// stand-in (mutation M-19).
    /// </summary>
    [Fact]
    public async Task AdminVikar_DepartedActorAsStandIn_AsToday()
    {
        var self = await SeedAsync("ds_av_s", OrgB, active: true, end: Gone);
        var manager = await SeedAsync("ds_av_s_m", OrgB);
        var report = await SeedAsync("ds_av_s_e", OrgB);
        await SeedLineAsync(report, manager, "PRIMARY");
        await SeedRoleAssignmentAsync(self, "LOCAL_HR", OrgB);   // she must hold a qualifying role to stand in
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await client.PostAsJsonAsync($"/api/admin/reporting-lines/{manager}/vikar",
            new { vikarUserId = self, effectiveTo = FutureDate(), reason = "FERIE" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id=@m AND vikar_user_id=@v AND effective_to IS NULL",
            ("m", manager), ("v", self)));
    }

    /// <summary>
    /// Kind F. The departed HR actor clears the stand-in on HER OWN manager record
    /// (<c>DELETE .../{managerId}/vikar</c>, <c>managerId</c> = herself): 403, the row stays open. Red
    /// condition: <c>Assert 403</c> and the open row — today the route closes it.
    /// </summary>
    [Fact]
    public async Task AdminVikarClear_DepartedActorAsManager_403()
    {
        var self = await SeedAsync("ds_avc", OrgB, active: true, end: Gone);
        var standIn = await SeedStandInAsync("ds_avc_v", OrgB);
        await SeedVikarRowAsync(self, standIn, OrgB);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await DeleteAsync(client, $"/api/admin/reporting-lines/{self}/vikar");

        await AssertDeniedAsync(rsp);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id=@a AND effective_to IS NULL", ("a", self)));
        await AssertNoWriteTrailAsync(self);
    }

    // ── Unit leaders ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kind F. The departed HR actor (a member of the unit) designates HERSELF its leader: 403, no
    /// <c>unit_leaders</c> row. Red condition: <c>Assert 403</c> and zero rows — today the member invariant
    /// holds and the designation commits (200).
    /// </summary>
    [Fact]
    public async Task UnitLeaderAdd_DepartedSelf_403()
    {
        var self = await SeedAsync("ds_la", OrgB, active: true, end: Gone);
        var unit = await SeedUnitAsync(OrgB);
        await SetUnitAsync(self, unit);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await client.PostAsJsonAsync($"/api/admin/units/{unit}/leaders", new { userId = self });

        await AssertDeniedAsync(rsp);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM unit_leaders WHERE unit_id=@u AND user_id=@e", ("u", unit), ("e", self)));
        await AssertNoWriteTrailAsync(self);
    }

    /// <summary>
    /// Kind F. The departed HR actor removes HERSELF as a leader of her unit: 403, the row stays. Red
    /// condition: <c>Assert 403</c> and the surviving row — today the removal commits (204).
    /// </summary>
    [Fact]
    public async Task UnitLeaderRemove_DepartedSelf_403()
    {
        var self = await SeedAsync("ds_lr", OrgB, active: true, end: Gone);
        var unit = await SeedUnitAsync(OrgB);
        await SetUnitAsync(self, unit);
        await SeedUnitLeaderAsync(unit, self);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await DeleteAsync(client, $"/api/admin/units/{unit}/leaders/{self}");

        await AssertDeniedAsync(rsp);
        Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM unit_leaders WHERE unit_id=@u AND user_id=@e", ("u", unit), ("e", self)));
        await AssertNoWriteTrailAsync(self);
    }

    // ── The consequence clause (P-10): two B facts ───────────────────────────────────────

    /// <summary>
    /// Kind B — boundary pin — holds today; P-10 consequence clause. HR (the departed actor) removes HER OWN
    /// stand-in X through <c>/remove</c>. The removal proceeds as today, and as a side effect the actor's own
    /// <c>manager_vikar</c> row (<c>absent_approver_id</c> = actor, <c>vikar_user_id</c> = X) is closed: the
    /// row is dead either way (a deactivated stand-in is skipped), so no guard is added. Its
    /// <c>ManagerVikarEnded</c> event names the DEPARTED ACTOR as <c>ActorId</c>.
    /// <para>Seeding: the departed actor is NOT an incoming report of X, or the derived guard would fire first
    /// and the fact would fail for the wrong reason.</para>
    /// </summary>
    [Fact]
    public async Task Remove_DepartedActorsStandInRemoved_OwnVikarRowClosed_AsToday()
    {
        var self = await SeedAsync("ds_cc_r", OrgB, active: true, end: Gone);
        var standIn = await SeedStandInAsync("ds_cc_r_x", OrgB);
        var vikarId = await SeedVikarRowAsync(self, standIn, OrgB);
        using var client = ClientFor(Hr(self, OrgB));

        var rsp = await RemoveAsync(client, standIn, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE vikar_id=@v AND effective_to IS NULL", ("v", vikarId)));
        Assert.Equal(self, await EndedEventActorAsync($"reporting-line-{self}"));
    }

    /// <summary>
    /// Kind B — boundary pin — holds today; P-10 consequence clause. The departed HR actor transfers HER OWN
    /// stand-in X to another organisation through <c>PUT /api/admin/users/{X}</c>: the transfer proceeds, the
    /// actor's own <c>manager_vikar</c> row is closed (a transferred stand-in is outside the organisation the
    /// row is bound to, ADR-038 D12), and the <c>ManagerVikarEnded</c> event names the departed actor as
    /// <c>ActorId</c>.
    /// <para>Seeding: X manages no active reports (else the route answers 422), and the actor's scopes cover
    /// BOTH organisations.</para>
    /// </summary>
    [Fact]
    public async Task UsersPut_DepartedActorsStandInTransferred_OwnVikarRowClosed_AsToday()
    {
        var self = await SeedAsync("ds_cc_t", OrgB, active: true, end: Gone);
        var standIn = await SeedStandInAsync("ds_cc_t_x", OrgB);
        var vikarId = await SeedVikarRowAsync(self, standIn, OrgB);
        using var client = ClientFor(HrTwoOrgs(self, OrgB, OrgA));

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{standIn}")
        {
            Content = JsonContent.Create(new { primaryOrgId = OrgA }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);

        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");
        Assert.Equal(OrgA, await ScalarStringAsync("SELECT primary_org_id FROM users WHERE user_id=@e", ("e", standIn)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM manager_vikar WHERE vikar_id=@v AND effective_to IS NULL", ("v", vikarId)));
        Assert.Equal(self, await EndedEventActorAsync($"manager-vikar-{vikarId}"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers: assertions
    // ═════════════════════════════════════════════════════════════════════════════════════

    private static async Task AssertDeniedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(DepartedSubject.DenyReason, doc.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>"Nothing was written": no outbox event and no audit-projection row carries the departed
    /// actor as its actor (every fact uses a case-unique actor who performs only the refused call before this
    /// check, so a non-zero count is the refused call's trail).</summary>
    private async Task AssertNoWriteTrailAsync(string actorId)
    {
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE actor_id=@a", ("a", actorId)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE actor_id=@a", ("a", actorId)));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers: tokens and clients
    // ═════════════════════════════════════════════════════════════════════════════════════

    private static string Mint(string actorId, string role, string? orgId, params RoleScope[] scopes)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(actorId, actorId, role, "AC", orgId, scopes);
    }

    private static string Leader(string id, string org) =>
        Mint(id, StatsTidRoles.LocalLeader, org, new RoleScope(StatsTidRoles.LocalLeader, org, "ORG_ONLY"));

    private static string Hr(string id, string org) =>
        Mint(id, StatsTidRoles.LocalHR, org, new RoleScope(StatsTidRoles.LocalHR, org, "ORG_ONLY"));

    private static string HrTwoOrgs(string id, string org, string org2) =>
        Mint(id, StatsTidRoles.LocalHR, org,
            new RoleScope(StatsTidRoles.LocalHR, org, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalHR, org2, "ORG_ONLY"));

    private static string Admin(string id, string org) =>
        Mint(id, StatsTidRoles.LocalAdmin, org, new RoleScope(StatsTidRoles.LocalAdmin, org, "ORG_ONLY"));

    private static string GlobalAdmin(string id) =>
        Mint(id, StatsTidRoles.GlobalAdmin, null, new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL"));

    private HttpClient ClientFor(string bearer, WebApplicationFactory<Program>? host = null)
    {
        var client = host is null ? _fx.Factory.CreateClient() : host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static string FutureDate() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd");

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers: requests
    // ═════════════════════════════════════════════════════════════════════════════════════

    private static object PreApprovalBody(string employeeId) => new
    {
        employeeId,
        periodStart = new DateOnly(2026, 3, 2),
        periodEnd = new DateOnly(2026, 3, 8),
        maxHours = 10m,
        reason = "s147",
    };

    private static async Task<HttpResponseMessage> SendWithIfMatchAsync(
        HttpClient client, HttpMethod method, string url, object? body, string ifMatch = "\"1\"")
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient client, string employeeId) =>
        SendWithIfMatchAsync(client, HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/VACATION/2025/resolve", new { disposition = "FORFEIT" });

    private static Task<HttpResponseMessage> ReconcileAsync(HttpClient client, string employeeId) =>
        SendWithIfMatchAsync(client, HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/VACATION/2025/reconcile-payout", new { });

    private static Task<HttpResponseMessage> TerminationRequestAsync(HttpClient client, string employeeId) =>
        SendWithIfMatchAsync(client, HttpMethod.Post,
            $"/api/admin/employees/{employeeId}/termination-payout-request",
            new
            {
                entitlementYear = 2025,
                expectedSettlementSequence = 1,
                requestDate = new DateOnly(2026, 3, 1),
                evidenceNote = "s147",
            });

    private static Task<HttpResponseMessage> PutAgreementCodeAsync(HttpClient client, string userId) =>
        SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/admin/users/{userId}/agreement-code",
            new { agreementCode = "HK", effectiveFrom = "2025-05-11" });

    private static Task<HttpResponseMessage> PutUserAsync(HttpClient client, string userId, string displayName) =>
        SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/admin/users/{userId}",
            new { displayName, effectiveFrom = "2026-03-04" });

    private static Task<HttpResponseMessage> PutUserUnitAsync(HttpClient client, string userId, Guid unitId) =>
        SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/admin/users/{userId}/unit", new { unitId });

    private static Task<HttpResponseMessage> GrantAsync(HttpClient client, string userId) =>
        client.PostAsJsonAsync("/api/admin/roles/grant",
            new { userId, roleId = "LOCAL_LEADER", orgId = OrgA, scopeType = "ORG_ONLY" });

    private static Task<HttpResponseMessage> DeleteUnitAsync(HttpClient client, Guid unitId) =>
        SendWithIfMatchAsync(client, HttpMethod.Delete, $"/api/admin/units/{unitId}", null);

    private static Task<HttpResponseMessage> RemoveAsync(
        HttpClient client, string employeeId, Dictionary<string, string> replacements) =>
        client.PostAsJsonAsync($"/api/admin/reporting-lines/{employeeId}/remove", new { replacements });

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string url) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers: seeding
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A case-unique person: users + profile + agreement rows (RegressionSeed), then the lifecycle
    /// state set directly. <paramref name="end"/> null = open-ended employment.</summary>
    private async Task<string> SeedAsync(string prefix, string org, bool active = true, DateOnly? end = null)
    {
        var id = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(Cs, id, org, "AC", "OK24");
        await ExecAsync(
            "UPDATE users SET is_active=@a, employment_end_date=@e, end_date_deactivated=@d, updated_at=NOW() WHERE user_id=@id",
            ("a", active), ("e", end), ("d", !active && end is not null), ("id", id));
        return id;
    }

    /// <summary>A person who can stand in (LocalLeader in the organisation), for the stand-in routes.</summary>
    private async Task<string> SeedStandInAsync(string prefix, string org)
    {
        var id = await SeedAsync(prefix, org);
        await SeedRoleAssignmentAsync(id, "LOCAL_LEADER", org);
        return id;
    }

    private async Task<Guid> SeedRoleAssignmentAsync(string userId, string roleId, string org)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO role_assignments (assignment_id, user_id, role_id, org_id, scope_type, assigned_by) VALUES (@i,@u,@r,@o,'ORG_ONLY','TEST')",
            ("i", id), ("u", userId), ("r", roleId), ("o", org));
        return id;
    }

    private async Task<Guid> SeedUnitAsync(string org)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES (@i,@o,NULL,'direktion',@n)",
            ("i", id), ("o", org), ("n", "S147 unit " + id.ToString("N")));
        return id;
    }

    private Task SetUnitAsync(string userId, Guid unitId) =>
        ExecAsync("UPDATE users SET unit_id=@u WHERE user_id=@id", ("u", unitId), ("id", userId));

    private Task SeedUnitLeaderAsync(Guid unitId, string userId) =>
        ExecAsync("INSERT INTO unit_leaders (unit_id, user_id) VALUES (@u,@e)", ("u", unitId), ("e", userId));

    private Task SeedLineAsync(string employeeId, string managerId, string relationship) =>
        ExecAsync(
            "INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by) VALUES (@e,@m,@o,@r,'2024-01-01','MANUAL','seed')",
            ("e", employeeId), ("m", managerId), ("o", OrgB), ("r", relationship));

    private async Task<Guid> SeedVikarRowAsync(string absentApproverId, string vikarUserId, string org)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO manager_vikar (vikar_id, absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES (@i,@a,@v,@u,'ANDET',@o,'seed')",
            ("i", id), ("a", absentApproverId), ("v", vikarUserId),
            ("u", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)), ("o", org));
        return id;
    }

    private async Task<Guid> SeedPendingPreApprovalAsync(string employeeId)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status) VALUES (@i,@e,'2026-03-02','2026-03-08',10,'PENDING')",
            ("i", id), ("e", employeeId));
        return id;
    }

    private Task SeedOvertimeBalanceAsync(string employeeId) =>
        ExecAsync(
            "INSERT INTO overtime_balances (employee_id, agreement_code, period_year, accumulated) VALUES (@e,'AC',2026,10)",
            ("e", employeeId));

    private Task SeedVacationSettlementAsync(string employeeId, string state, decimal payout, decimal forfeit)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m, used = 0m, planned = 0m, carryoverIn = 0m, annualQuota = 25m, carryoverMax = 5m,
            resetMonth = 9, okVersion = "OK24", transferAgreementDays = 0m, isFeriehindret = false,
        });
        return ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger, snapshot,
                 transfer_days, payout_days, forfeit_days, review_disposition, version)
            VALUES (@e,'VACATION',2025,1,@s,'YEAR_END',@snap::jsonb,0,@p,@f,NULL,1)
            """,
            ("e", employeeId), ("s", state), ("snap", snapshot), ("p", payout), ("f", forfeit));
    }

    private Task SeedTerminationSettlementAsync(string employeeId, DateOnly endDate)
    {
        var snapshot = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["recordedAbsences"] = Array.Empty<object>(),
            ["earned"] = 25m, ["used"] = 0m, ["planned"] = 0m, ["carryoverIn"] = 0m, ["annualQuota"] = 25m,
            ["carryoverMax"] = 5m, ["resetMonth"] = 9, ["okVersion"] = "OK24", ["transferAgreementDays"] = 0m,
            ["isFeriehindret"] = false,
            ["settlementBoundaryDate"] = endDate.ToString("yyyy-MM-dd"),
            ["crystallizedDays"] = 12.5m,
            ["terminationDate"] = endDate.ToString("yyyy-MM-dd"),
            ["crystallizationBasis"] = "S26_WHOLE_MONTH",
        });
        return ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger, snapshot,
                 transfer_days, payout_days, forfeit_days, version)
            VALUES (@e,'VACATION',2025,1,'SETTLED','TERMINATION',@snap::jsonb,0,0,0,1)
            """,
            ("e", employeeId), ("snap", snapshot));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers: SQL
    // ═════════════════════════════════════════════════════════════════════════════════════

    // CA2100 suppression: every caller passes a string LITERAL (test seed or read); values go through parameters.
#pragma warning disable CA2100
    private async Task<int> ExecAsync(string sql, params (string Name, object? Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }
#pragma warning restore CA2100

    private async Task<long> CountAsync(string sql, params (string Name, object? Value)[] ps) =>
        Convert.ToInt64(await ScalarAsync(sql, ps));

    private async Task<string?> ScalarStringAsync(string sql, params (string Name, object? Value)[] ps) =>
        (await ScalarAsync(sql, ps)) as string;

    private async Task<string?> PreApprovalStatusAsync(Guid id) =>
        await ScalarStringAsync("SELECT status FROM overtime_pre_approvals WHERE id=@i", ("i", id));

    private async Task<decimal> PaidOutAsync(string employeeId) =>
        Convert.ToDecimal(await ScalarAsync(
            "SELECT paid_out FROM overtime_balances WHERE employee_id=@e AND period_year=2026", ("e", employeeId)));

    private async Task<string?> SettlementStateAsync(string employeeId) =>
        await ScalarStringAsync("SELECT settlement_state FROM vacation_settlements WHERE employee_id=@e", ("e", employeeId));

    private async Task<Guid?> UnitOfAsync(string userId) =>
        await ScalarAsync("SELECT unit_id FROM users WHERE user_id=@e", ("e", userId)) as Guid?;

    private async Task<bool> UnitDeletedAsync(Guid unitId) =>
        await ScalarAsync("SELECT deleted_at FROM units WHERE unit_id=@u", ("u", unitId)) is not null;

    private async Task<bool> IsActiveAsync(string userId) =>
        (bool)(await ScalarAsync("SELECT is_active FROM users WHERE user_id=@e", ("e", userId)))!;

    private async Task<bool> AssignmentActiveAsync(Guid assignmentId) =>
        (bool)(await ScalarAsync("SELECT is_active FROM role_assignments WHERE assignment_id=@a", ("a", assignmentId)))!;

    /// <summary>The <c>actorId</c> of the latest <c>ManagerVikarEnded</c> on the stream.</summary>
    private async Task<string?> EndedEventActorAsync(string streamId)
    {
        var payload = await ScalarStringAsync(
            "SELECT event_payload::text FROM outbox_events WHERE stream_id=@s AND event_type='ManagerVikarEnded' ORDER BY outbox_id DESC LIMIT 1",
            ("s", streamId));
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        return doc.RootElement.GetProperty("actorId").GetString();
    }
}
