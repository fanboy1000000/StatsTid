using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Contracts;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S148 / TASK-14845-T (AC-C13o (1)-(9), AC-C13p (1)-(13); SEC-062; the SEC-062 second-trace adjudication, ruling 6;
/// M-46 and M-47) - the organisation-drift guard on the 22 write sites W1-W22: a request caught in a transfer race
/// answers 409 and writes nothing.
///
/// <para>
/// <b>In plain language.</b> Before a write handler changes something about a person, it asks "may this administrator
/// act on this person?" from a read taken before it locks anything. If a colleague transfers the person to another
/// institution in that moment, the write used to land on someone the administrator was never checked for. Each of the 22
/// actions now compares, under a lock, the organisation the check approved with the organisation the lock found, and
/// answers 409 "refresh and retry" with nothing written. Each fact here forces exactly that interleaving, and the test,
/// not the scheduler, decides the order: it holds a lock the request needs, waits until PostgreSQL shows the request
/// waiting on it, moves the person to STY02, commits, and only then lets the request go on.
/// </para>
///
/// <para>
/// <b>Choreography A, the users-row wait (tiers 2 and 3: W4-W10, W16-W22), as <c>TransferRaceTests</c>.</b> (1) A side
/// connection takes the person's <c>users</c> row <c>FOR UPDATE</c> in an open transaction. (2) The request is sent
/// without being awaited. (3) The test polls <c>pg_stat_activity</c> until another backend waits on a lock in a statement
/// that locks a <c>users</c> row (<c>FROM users ... FOR UPDATE</c>); it gives up when the request completes. When the
/// request completes before a waiter is seen, the response is asserted first (409 expected) and the fact fails on it: on
/// the code without the guard a tier-3 request never blocks, so that is the red; under the mutation the request blocks
/// on the held row, so the red there is the same assertion after the wait. (4) The side connection sets
/// <c>primary_org_id = 'STY02'</c> and <c>version = version + 1</c> and commits. (5) The request is awaited.
/// </para>
///
/// <para>
/// <b>Choreography B, tier 1 (W1, W2, W3, W11-W15): no row wait.</b> (1) A side connection takes
/// <c>LOCK TABLE organizations IN ACCESS EXCLUSIVE MODE</c> in an open transaction. (2) The request is sent without being
/// awaited; it reads the person (still STY01) and blocks on the scope check's organisation read. (3) The test polls
/// <c>pg_stat_activity</c> until a backend waits on a lock in a statement that STARTS <c>SELECT * FROM organizations WHERE
/// org_id</c> and names no <c>users</c> table (the validator's user read has no join, <c>UserRepository.cs:31</c>,
/// <c>:328</c>; its next statement is <c>OrganizationRepository.GetByIdAsync</c>): a request blocked anywhere else fails
/// the fact, it cannot pass it. (4) The move runs ON THE CONNECTION THAT HOLDS THE TABLE LOCK (a second connection would
/// wait forever on the foreign-key check), then commits. (5) The request is awaited: the check admits on STY01 (the user
/// was read before the move) and the reporting-tree acquire, which runs after the commit, finds STY02. Every tier-1 site's
/// first read of <c>organizations</c> is the scope check's own (the brief adjudication, R11), so the choreography is
/// deterministic at all eight; the drift-retry wrapper's own 409 uses a different sentence (<c>TreeRootDriftRetry.cs</c>),
/// so the sentence asserted as a literal cannot be satisfied by it.
/// </para>
///
/// <para>
/// <b>Assertions (every fact).</b> 409; the body holds exactly one member, <c>error</c>, with the sentence asserted as a
/// literal and never as the production constant (a later edit that adds the person's new organisation to the refusal would
/// pass every other assertion, and an HR user scoped only to STY01 would learn where the person moved);
/// <c>users.primary_org_id</c> "STY02" at version v+1 (the side transaction's move and nothing more); the person's write
/// trail unchanged (<see cref="SelfChangeRefusalTests.WriteTrailAsync"/> plus <see cref="MentionsAsync"/>: every event, audit
/// projection and worklist row that names the person); and the site's own data unchanged. The request-level audit log is
/// not counted; whether it records a 409 is <c>(unproven)</c>.
/// </para>
///
/// <para>
/// <b>Fixture.</b> The clock fixed at Tuesday 10 November 2026 (<see cref="Clock"/>); one container and one Backend host for
/// the class (<see cref="DepartedSelfHostFixture"/>, no leaver-flip job), each fact seeding its own person in STY01 at
/// version v. The actor is a LocalHR scoped to STY01 only (<c>hr_s148_sty01_only</c>), holding no reporting edge or unit
/// leadership over the person; W13 and W14 (floor LocalAdmin) use a LocalAdmin scoped to STY01 only. Where a fact needs a
/// manager or a reporting line it uses the seeded users <c>mgr03</c> (STY01's root) and <c>ladm01</c> (STY02's root), so no
/// fact adds a second root to a tree another fact's census reads. On W4, W16 and W20 (R2's self exemption) and on W9 and
/// W10 (the self refusal answers 403 before the transaction) the actor is not the subject.
/// <b>Serial-collections premise:</b> the <c>ACCESS EXCLUSIVE</c> lock on <c>organizations</c> blocks every request that
/// reads the table, so choreography B is safe only because the Regression suite runs one test collection at a time
/// (<c>xunit.runner.json:3</c>, <c>parallelizeTestCollections</c> false) and each fact leaves no request in flight.
/// Docker-gated: <b>CI-pending</b>.
/// </para>
///
/// <para>
/// <b>M-46 and M-47 (compound mutations, run at CI-M4a).</b> M-46: "the organisation compare deleted at the nine sites
/// (W1-W4, W16-W20); the validator siblings, the locked reads and the tree acquires kept". Frozen list: the nine facts of
/// AC-C13o ((1)-(9): <c>Approve_</c>, <c>Reject_</c>, <c>ReopenLeaderArm_</c>, <c>Send_</c>, <c>OvertimePreApprovalCreate_</c>,
/// <c>OvertimePreApprovalApprove_</c>, <c>OvertimePreApprovalReject_</c>, <c>OvertimeCompensate_</c>, <c>SkemaSave_</c>), each
/// tripping its <c>Assert.True(status == Conflict)</c>. M-47: the same for W5-W15, W21 and W22, with W14's new tree lock
/// kept; frozen list: the thirteen facts of AC-C13p ((1)-(13): <c>TransferAgreementPost_</c> through <c>UserUnitPut_</c>).
/// With the compare gone each request writes (the success status each docstring names) on a person who now lives in STY02;
/// the locked reads and acquires are kept, so every request still reaches its wait and the failure is on the status, not on
/// the wait. The two sets are disjoint from each other and from M-44's and M-45's sites.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OrganisationDriftGuardRoutesTests : IClassFixture<DepartedSelfHostFixture>, IAsyncLifetime
{
    /// <summary>The refusal's sentence, as a literal (not the production constant).</summary>
    private const string DriftError =
        "The user's organisation changed while this request was being processed; refresh and retry.";

    private const string HrActor = "hr_s148_sty01_only";
    private const string AdminActor = "ladm_s148_sty01_only";

    /// <summary>STY01's seeded root (init.sql: <c>emp001</c> reports to it); the manager of every STY01 line a fact seeds.</summary>
    private const string Sty01Root = "mgr03";

    /// <summary>STY02's seeded root: "a manager already in STY02".</summary>
    private const string Sty02Root = "ladm01";

    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);
    private static readonly DateOnly Mon9Nov = new(2026, 11, 9);

    // A monotonic outbox_id for hand-written absence rows (a NOT NULL BIGINT); a range of its own.
    private static long _absenceSeq = 148_450_000;

    private readonly DepartedSelfHostFixture _fx;
    private WebApplicationFactory<Program> _host = null!;

    public OrganisationDriftGuardRoutesTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    public Task InitializeAsync()
    {
        _host = _fx.Factory.WithFixedToday(Clock);
        _ = _host.CreateClient(); // boot before any fact seeds (PAT-008)
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host?.Dispose();
        return Task.CompletedTask;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C13o - the nine sites in files S148 already changes (M-46)
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (1), W1; choreography B). Approve a SUBMITTED October 2026 period (the actor admitted only by
    /// the organisation-scope arm: HR scoped to STY01, no edge), with the person moved while the scope check's organisation
    /// read waits: 409 and the exact sentence; the period is still SUBMITTED. Fixture: the period insert of
    /// <c>S94FlatApprovalTests.InsertPeriodWithRangeAsync</c>, October 2026, <c>OK26</c>.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c> (the scope check's organisation read).</para>
    /// <para><b>Red conditions (on <c>446ec114</c>/<c>ba0a054e</c>, without the guard).</b> The tree acquire derives STY02, the
    /// request compares nothing and approves: <c>Assert.True(status == Conflict)</c> fails (200). <b>M-46</b> (the compare
    /// deleted, the acquire kept) trips the same assertion; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task Approve_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var period = await InsertPeriodAsync(person, "SUBMITTED", Oct1, Oct31);

        var raced = await RaceOrganisationsTableAsync(person, HrClient(),
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/approval/{period}/approve"));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("SUBMITTED", await PeriodStatusAsync(period));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (2), W2; choreography B). Reject the same SUBMITTED period with a reason: 409 and the exact
    /// sentence; still SUBMITTED.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c>.</para>
    /// <para><b>Red conditions.</b> The request rejects the period of a person now in STY02 and answers 200:
    /// <c>Assert.True(status == Conflict)</c> fails. <b>M-46</b> trips it; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task Reject_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var period = await InsertPeriodAsync(person, "SUBMITTED", Oct1, Oct31);

        var raced = await RaceOrganisationsTableAsync(person, HrClient(), () =>
            Json(HttpMethod.Post, $"/api/approval/{period}/reject", new { reason = "s148 drift race" }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("SUBMITTED", await PeriodStatusAsync(period));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (3), W3; choreography B). Reopen's leader arm on an APPROVED, unexported October 2026 period
    /// (the actor admitted by the organisation-scope arm: HR scoped to STY01): 409 and the exact sentence; still APPROVED.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c> (the leader arm's validator; the employee
    /// arm, which takes no tree lock and no guard, is not driven).</para>
    /// <para><b>Red conditions.</b> The request reopens the period (APPROVED to DRAFT) and answers 200:
    /// <c>Assert.True(status == Conflict)</c> fails. <b>M-46</b> trips it; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task ReopenLeaderArm_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var period = await InsertPeriodAsync(person, "APPROVED", Oct1, Oct31);

        var raced = await RaceOrganisationsTableAsync(person, HrClient(), () =>
            Json(HttpMethod.Post, $"/api/approval/{period}/reopen", new { reason = "s148 drift race" }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("APPROVED", await PeriodStatusAsync(period));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (4), W4; choreography A). The send command on another's behalf: <c>POST /api/approval/send</c>
    /// for the person's October 2026 by the HR actor (not the subject), the month covered with absences (the coverage fixture
    /// of <c>SendCommandMatrixTestBase.CoverMonthWithAbsencesAsync</c>, moved to October 2026), with the person moved while
    /// the request waits on the locked subject read: 409 and the exact sentence; no period row created or moved.
    /// <para><b>Waits in</b> a <c>users ... FOR UPDATE</c> read (the in-lock subject read, locking when the actor is not the
    /// subject).</para>
    /// <para><b>Red conditions.</b> The subject read is a plain read today, so the request never blocks and sends the month:
    /// the early-completion path asserts the 409 first and fails on 200. <b>M-46</b> (the compare deleted, the locked read
    /// kept) lets the request proceed after the wait and answer 200: <c>Assert.True(status == Conflict)</c> fails; success
    /// status under the mutation: 200 (a 422 would mean the fixture's month is not covered: reported).</para>
    /// </summary>
    [Fact]
    public async Task Send_OnAnothersBehalf_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        await CoverOctoberWithAbsencesAsync(person);

        var raced = await RaceUsersRowAsync(person, HrClient(), _ =>
            Json(HttpMethod.Post, "/api/approval/send", new { employeeId = person, year = 2026, month = 10 }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM approval_periods WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (5), W16; choreography A). The overtime pre-approval create on another's behalf, for the week of
    /// Monday 9 November 2026 (to Sunday 15), 10 hours: 409 and the exact sentence; no pre-approval row.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the new locked read, taken when the actor is not the subject).</para>
    /// <para><b>Red conditions.</b> No locked read exists today, so the request never blocks and creates the row (201):
    /// the early-completion path fails on the 409. <b>M-46</b> trips the status after the wait; success status under the
    /// mutation: 201.</para>
    /// </summary>
    [Fact]
    public async Task OvertimePreApprovalCreate_OnAnothersBehalf_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => Json(HttpMethod.Post, "/api/overtime/pre-approval", new
        {
            employeeId = person,
            periodStart = Mon9Nov.ToString("yyyy-MM-dd"),
            periodEnd = Mon9Nov.AddDays(6).ToString("yyyy-MM-dd"),
            maxHours = 10m,
            reason = "s148 drift race",
        }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM overtime_pre_approvals WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (6), W17; choreography A). Approve the person's PENDING overtime pre-approval (inserted by SQL):
    /// 409 and the exact sentence; still PENDING.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (first statement of the transaction).</para>
    /// <para><b>Red conditions.</b> The request locks no users row today, completes and approves (200): the early-completion
    /// path fails on the 409. <b>M-46</b> trips the status after the wait; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task OvertimePreApprovalApprove_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var id = await InsertPendingPreApprovalAsync(person);

        var raced = await RaceUsersRowAsync(person, HrClient(), _ =>
            Json(HttpMethod.Put, $"/api/overtime/pre-approval/{id}/approve", new { reason = "s148 drift race" }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("PENDING", await PreApprovalStatusAsync(id));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (7), W18; choreography A). Reject the same kind of PENDING pre-approval: 409 and the exact
    /// sentence; still PENDING.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c>.</para>
    /// <para><b>Red conditions.</b> As part (6): the request completes and rejects (200). <b>M-46</b> trips it; success status
    /// under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task OvertimePreApprovalReject_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var id = await InsertPendingPreApprovalAsync(person);

        var raced = await RaceUsersRowAsync(person, HrClient(), _ =>
            Json(HttpMethod.Put, $"/api/overtime/pre-approval/{id}/reject", new { reason = "s148 drift race" }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("PENDING", await PreApprovalStatusAsync(id));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (8), W19; choreography A). Compensate 1 hour, PAYOUT, for 2026 from a balance row seeded by SQL
    /// (accumulated 6, nothing paid out; no production write feeds the balance today, <c>OvertimeEndpoints.cs:563-565</c>):
    /// 409 and the exact sentence; the balance row unchanged.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (first statement of the transaction, before the guarded updates).</para>
    /// <para><b>Red conditions.</b> The request locks no users row today and pays the hour out (200): the early-completion
    /// path fails on the 409. <b>M-46</b> trips the status after the wait; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task OvertimeCompensate_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO overtime_balances (employee_id, agreement_code, period_year, accumulated, paid_out, afspadsering_used)
            VALUES (@e, 'AC', 2026, 6, 0, 0)
            """, ("e", person));

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => Json(HttpMethod.Post, $"/api/overtime/{person}/compensate",
            new { periodYear = 2026, hours = 1m, compensationType = "PAYOUT" }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("6|0|0", await ScalarStringAsync(Cs,
            "SELECT accumulated::text || '|' || paid_out::text || '|' || afspadsering_used::text FROM overtime_balances WHERE employee_id = @e AND period_year = 2026",
            ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13o (9), W20; choreography A). The Skema save on another's behalf: one work-time registration on
    /// Monday 9 November 2026 (08:00-16:00) in November 2026, by the HR actor (not the subject): 409 and the exact sentence; no
    /// registration stored for the day.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the in-lock subject read, locking when the actor is not the
    /// subject; the <c>employee-</c> advisory before it is not held).</para>
    /// <para><b>Red conditions.</b> The in-lock subject read is a plain read today, so the request never blocks and stores the
    /// registration (200): the early-completion path fails on the 409. <b>M-46</b> trips the status after the wait; success
    /// status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task SkemaSave_OnAnothersBehalf_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => Json(HttpMethod.Post, $"/api/skema/{person}/save", new
        {
            year = 2026,
            month = 11,
            workTime = new[]
            {
                new { date = Mon9Nov.ToString("yyyy-MM-dd"), intervals = new[] { new { start = "08:00", end = "16:00" } } },
            },
        }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id = @e AND date = DATE '2026-11-09'", ("e", person)));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C13p - the thirteen sites the owner added (M-47)
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (1), W5; choreography A). <c>POST /api/vacation-transfer-agreements/{person}</c>, a section 21
    /// agreement for ferieaar 2026 (the ferieaar starting 1 September 2026), 3 days, dated Tuesday 10 November 2026: 409 and the
    /// exact sentence; no agreement row. Fixture: the body of <c>VacationSettlementEndpointTests.PostTransferAgreementAsync</c>
    /// at this sprint's clock.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the locked read directly after the <c>employee-</c> advisory).</para>
    /// <para><b>Red conditions.</b> No users row is locked today, so the request never blocks and records the agreement (201):
    /// the early-completion path fails on the 409. <b>M-47</b> trips the status after the wait; success status under the
    /// mutation: 201.</para>
    /// </summary>
    [Fact]
    public async Task TransferAgreementPost_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => Json(HttpMethod.Post, $"/api/vacation-transfer-agreements/{person}", new
        {
            entitlementYear = 2026,
            entitlementType = "VACATION",
            transferDays = 3m,
            agreementDate = "2026-11-10",
        }));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM vacation_transfer_agreements WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (2), W6; choreography A). Resolve a PENDING_REVIEW settlement (FORFEIT, <c>If-Match "1"</c>, its
    /// real version): 409 and the exact sentence; the settlement unchanged at its version. Fixture: the row of
    /// <c>VacationSettlementEndpointTests.SeedSettlementRowAsync</c> (payout 5, forfeit 20).
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (terminated-inclusive locked read directly after the lock).</para>
    /// <para><b>Red conditions.</b> The request locks no users row today and settles the row (200): the early-completion path
    /// fails on the 409. <b>M-47</b> trips the status after the wait; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task SettlementResolve_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await InsertSettlementAsync(person, "PENDING_REVIEW", payout: 5m, forfeit: 20m);

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => IfMatchJson(HttpMethod.Post,
            $"/api/vacation-settlements/{person}/VACATION/2025/resolve", new { disposition = "FORFEIT", forfeitDays = (decimal?)null }, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("PENDING_REVIEW|1", await SettlementStateAsync(person));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (3), W7; choreography A). Reconcile a SETTLED row's section 24 payout (payout 5, <c>If-Match "1"</c>):
    /// 409 and the exact sentence; the row unchanged, not reconciled.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (directly after the inline <c>employee-</c> lock).</para>
    /// <para><b>Red conditions.</b> The request locks no users row today and reconciles (200): the early-completion path fails
    /// on the 409. <b>M-47</b> trips the status after the wait; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task ReconcilePayout_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await InsertSettlementAsync(person, "SETTLED", payout: 5m, forfeit: 0m);

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => IfMatchJson(HttpMethod.Post,
            $"/api/vacation-settlements/{person}/VACATION/2025/reconcile-payout", new { }, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("SETTLED|1", await SettlementStateAsync(person));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @e AND payout_reconciled_at IS NOT NULL", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (4), W8; choreography A). A section 26 termination payout request against a SETTLED TERMINATION row
    /// (a deactivated leaver ending 28 February 2026, ferieaar 2025, produced by the real settlement pass as
    /// <c>TerminationPayoutRequestEndpointTests.SeedSettledTerminationAsync</c> does; sequence 1, <c>If-Match "1"</c>, request date
    /// 1 March 2026): 409 and the exact sentence; no request row.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the existing in-lock read made the terminated-inclusive locked overload).</para>
    /// <para><b>Red conditions.</b> The in-lock read is a plain read today, so the request never blocks and records the request
    /// (201): the early-completion path fails on the 409. <b>M-47</b> trips the status after the wait; success status under the
    /// mutation: 201. <b>(unproven)</b> that the TERMINATION settle drive yields a settled row at this fact's clock; a settle that
    /// does not fails the fixture's own assertion, not a pin.</para>
    /// </summary>
    [Fact]
    public async Task TerminationPayoutRequest_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await S120ContractAssert.ExecAsync(Cs,
            """
            UPDATE users SET employment_end_date = DATE '2026-02-28', is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", person));
        var service = _host.Services.GetRequiredService<VacationSettlementService>();
        await using (var settleConn = _host.Services.GetRequiredService<DbConnectionFactory>().Create())
        {
            await settleConn.OpenAsync();
            await using var tx = await settleConn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            var outcome = await service.SettleAsync(person, "VACATION", 2025, "TERMINATION", settleConn, tx, leaverGoLiveFloor: null);
            await tx.CommitAsync();
            Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
        }

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => IfMatchJson(HttpMethod.Post,
            $"/api/admin/employees/{person}/termination-payout-request",
            new { entitlementYear = 2025, expectedSettlementSequence = 1, requestDate = "2026-03-01", evidenceNote = (string?)null }, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM termination_payout_requests WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (5), W9; choreography A). The eligibility PUT: CHILD_SICK eligible, <c>If-None-Match: *</c>: 409 and
    /// the exact sentence; no eligibility row and no eligibility audit row.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the locked read as the transaction's first statement).</para>
    /// <para><b>Red conditions.</b> No users row is locked today, so the request never blocks and creates the row (200): the
    /// early-completion path fails on the 409. <b>M-47</b> trips the status after the wait; success status under the mutation:
    /// 200.</para>
    /// </summary>
    [Fact]
    public async Task EligibilityPut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), _ =>
        {
            var req = Json(HttpMethod.Put, $"/api/admin/employees/{person}/entitlement-eligibility/CHILD_SICK", new { eligible = true });
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
            return req;
        });

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM employee_entitlement_eligibility WHERE employee_id = @e", ("e", person)));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM employee_entitlement_eligibility_audit WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (6), W10; choreography A). The birth-date PUT, a new birth date (12 March 1980), <c>If-Match "v+1"</c>
    /// (the version the side transaction produces): 409 and the exact sentence; the birth date unchanged.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the handler's own locked read).</para>
    /// <para><b>Red conditions.</b> After the wait the version check passes and the request writes the birth date (200): the
    /// status assertion fails. <b>M-47</b> trips it; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task BirthDatePut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), next => IfMatchJson(HttpMethod.Put,
            $"/api/admin/employees/{person}/birth-date", new { birthDate = "1980-03-12" }, next));

        await AssertDriftRefusedAsync(raced);
        Assert.True(await ScalarBoolAsync("SELECT (birth_date IS NULL)::text FROM users WHERE user_id = @u", person));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (7), W11; choreography B). Reporting-line assign: the person's first assignment to a manager already
    /// in STY02 (<c>ladm01</c>, <c>If-None-Match: *</c>, effective Tuesday 10 November 2026). Without the guard the assignment
    /// succeeds once both are in STY02 (<c>ValidateSameOrganisationAsync</c>, <c>ReportingLineRepository.cs:540-566</c>): 409 and
    /// the exact sentence; no new line.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c> (the HR actor's scope check).</para>
    /// <para><b>Red conditions.</b> The tree acquire finds STY02, the same-organisation validation passes and the line is created
    /// (201): <c>Assert.True(status == Conflict)</c> fails. <b>M-47</b> trips it; success status under the mutation: 201.</para>
    /// </summary>
    [Fact]
    public async Task ReportingLineAssign_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceOrganisationsTableAsync(person, HrClient(), () =>
        {
            var req = Json(HttpMethod.Post, "/api/admin/reporting-lines",
                new { employeeId = person, managerId = Sty02Root, effectiveFrom = "2026-11-10" });
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
            return req;
        });

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (8), W12; choreography B). Delete the person's PRIMARY line (to <c>mgr03</c>, STY01's root, version 1,
    /// inserted by SQL): 409 and the exact sentence; the line still active at its version. The manager is the tree's existing root,
    /// so removing the line never makes a second root and the success route (204, <c>ReportingLineEndpoints.cs:464</c>) is open
    /// to the mutation; the second-root check (<c>:361-404</c>, its 409 at <c>:399-403</c>) cannot fire.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c>.</para>
    /// <para><b>Red conditions.</b> The request closes the line and answers 204: <c>Assert.True(status == Conflict)</c> fails.
    /// <b>M-47</b> trips it; success status under the mutation: 204.</para>
    /// </summary>
    [Fact]
    public async Task ReportingLineDelete_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await InsertLineAsync(person, "PRIMARY");

        var raced = await RaceOrganisationsTableAsync(person, HrClient(), () =>
            IfMatchJson(HttpMethod.Delete, $"/api/admin/reporting-lines/{person}", null, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("1|1", await ActiveLineAsync(person, "PRIMARY"));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (9), W13; choreography B). The acting POST by a LocalAdmin scoped to STY01 only (floor LocalAdmin,
    /// <c>ReportingLineEndpoints.cs:616</c>): a first acting manager already in STY02 (<c>ladm01</c>, <c>If-None-Match: *</c>): 409
    /// and the exact sentence; no acting line.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c>.</para>
    /// <para><b>Red conditions.</b> The acquire finds STY02, the manager is in the same organisation and the acting line is created
    /// (201): <c>Assert.True(status == Conflict)</c> fails. <b>M-47</b> trips it; success status under the mutation: 201.</para>
    /// </summary>
    [Fact]
    public async Task ActingAssign_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceOrganisationsTableAsync(person, AdminClient(), () =>
        {
            var req = Json(HttpMethod.Post, $"/api/admin/reporting-lines/{person}/acting",
                new { managerId = Sty02Root, effectiveFrom = "2026-11-10" });
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
            return req;
        });

        await AssertDriftRefusedAsync(raced);
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id = @e AND relationship = 'ACTING'", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (10), W14; choreography B). The acting DELETE by the LocalAdmin: end the person's ACTING line (to
    /// <c>mgr03</c>, version 1, inserted by SQL): 409 and the exact sentence; the acting line still active. W14's new tree lock
    /// takes the root derived from the person's CURRENT organisation, so after the commit it is STY02 against the authorised STY01.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c>.</para>
    /// <para><b>Red conditions.</b> Today the route takes no tree lock and no compare: it closes the line and answers 204:
    /// <c>Assert.True(status == Conflict)</c> fails. <b>M-47</b> (the compare deleted, W14's tree lock kept) closes the line and
    /// answers 204: the same assertion; success status under the mutation: 204.</para>
    /// </summary>
    [Fact]
    public async Task ActingDelete_OrganisationChangedBeforeTheTreeLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await InsertLineAsync(person, "ACTING");

        var raced = await RaceOrganisationsTableAsync(person, AdminClient(), () =>
            IfMatchJson(HttpMethod.Delete, $"/api/admin/reporting-lines/{person}/acting", null, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("1|1", await ActiveLineAsync(person, "ACTING"));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (11), W15; choreography B). Remove person (<c>POST /api/admin/reporting-lines/{person}/remove</c>, no
    /// replacements: the person manages nobody, so no token is needed): 409 and the exact sentence; the person still active, and no
    /// reporting line or vikar row about them (none was seeded) has appeared.
    /// <para><b>Waits in</b> <c>SELECT * FROM organizations WHERE org_id ...</c>.</para>
    /// <para><b>Red conditions.</b> The request deactivates the person and answers 200: <c>Assert.True(status == Conflict)</c> fails
    /// (and <c>is_active</c> would be FALSE). <b>M-47</b> trips the status; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task RemovePerson_OrganisationChangedBeforeTheTreeLock_409_StillActive()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceOrganisationsTableAsync(person, HrClient(), () =>
            Json(HttpMethod.Post, $"/api/admin/reporting-lines/{person}/remove", new { replacements = new Dictionary<string, string>() }));

        await AssertDriftRefusedAsync(raced);
        Assert.True(await ScalarBoolAsync("SELECT is_active::text FROM users WHERE user_id = @u", person));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM reporting_lines WHERE employee_id = @e OR manager_id = @e", ("e", person)));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id = @e OR vikar_user_id = @e", ("e", person)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (12), W21; choreography A). Resolve an open SETTLED_YEAR worklist row as DISMISSED (reason, <c>If-Match
    /// "1"</c>, its real version; the row inserted by SQL, the shape of <c>BackdateWorklistMigrationTests</c>): 409 and the exact
    /// sentence; the row unresolved at its version, and no worklist row added.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (the terminated-inclusive locked read as the transaction's first statement).</para>
    /// <para><b>Red conditions.</b> No users row is locked today, so the request never blocks and resolves the row (200): the
    /// early-completion path fails on the 409. <b>M-47</b> trips the status after the wait; success status under the mutation:
    /// 200.</para>
    /// </summary>
    [Fact]
    public async Task WorklistResolve_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var worklistId = Guid.NewGuid();
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@id, @e, 'SETTLED_YEAR', 'VACATION', 2025, @t::jsonb, 'hr01')
            """, ("id", worklistId), ("e", person),
            ("t", """[{"kind":"AGREEMENT_CODE_CHANGE","eventId":"11111111-1111-1111-1111-111111111111","effectiveFrom":"2026-03-10","appendedAt":"2026-11-09T08:00:00+00:00","actorId":"hr01","baselineSettlementSequence":1,"baselineSettlementState":"SETTLED"}]"""));

        var raced = await RaceUsersRowAsync(person, HrClient(), _ => IfMatchJson(HttpMethod.Post,
            $"/api/hr/backdate-worklist/{worklistId}/resolve", new { resolution = "DISMISSED", reason = "s148 drift race" }, 1));

        await AssertDriftRefusedAsync(raced);
        Assert.Equal("open|1", await ScalarStringAsync(Cs,
            "SELECT CASE WHEN resolved_at IS NULL THEN 'open' ELSE 'resolved' END || '|' || version FROM hr_backdate_worklist WHERE worklist_id = @id",
            ("id", worklistId)));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13p (13), W22; choreography A). <c>PUT /api/admin/users/{person}/unit</c> homing the person at the
    /// organisation (<c>unitId</c> null, <c>If-Match "v+1"</c>): 409 and the exact sentence; the unit unchanged. The route keeps its
    /// organisation check on its own pre-read and compares the locked row with that pre-read.
    /// <para><b>Waits in</b> <c>users ... FOR UPDATE</c> (<c>UnitRepository.GetUserUnitForUpdateAsync</c>; the <c>unit-org-</c> advisory
    /// before it is not held).</para>
    /// <para><b>Red conditions.</b> After the wait the version check passes and the request applies the unit change (200): the status
    /// assertion fails. <b>M-47</b> trips it; success status under the mutation: 200.</para>
    /// </summary>
    [Fact]
    public async Task UserUnitPut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");

        var raced = await RaceUsersRowAsync(person, HrClient(), next => IfMatchJson(HttpMethod.Put,
            $"/api/admin/users/{person}/unit", new { unitId = (Guid?)null }, next));

        await AssertDriftRefusedAsync(raced);
        Assert.True(await ScalarBoolAsync("SELECT (unit_id IS NULL)::text FROM users WHERE user_id = @u", person));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  The choreographies
    // ═════════════════════════════════════════════════════════════════════════════════════

    private sealed record Raced(HttpResponseMessage Response, string Person, long VersionBefore, string? TrailBefore, string? MentionsBefore);

    /// <summary>Choreography A: hold the person's users row, send, wait for a users-row waiter, move, commit, await.</summary>
    private async Task<Raced> RaceUsersRowAsync(string person, HttpClient client, Func<long, HttpRequestMessage> build)
    {
        var version = await ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", person));
        var trail = await SelfChangeRefusalTests.WriteTrailAsync(Cs, person);
        var mentions = await MentionsAsync(person);

        await using var side = new NpgsqlConnection(Cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx))
        {
            lockCmd.Parameters.AddWithValue("u", person);
            await lockCmd.ExecuteScalarAsync();
        }

        // Next version: the one the side transaction produces, so a guessed If-Match passes once the request has the
        // lock and only the organisation differs.
        var requestTask = client.SendAsync(build(version + 1));

        var observed = await Payroll.PriorMonthExportRaceTests.WaitUntilAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query ILIKE '%FROM users%FOR UPDATE%'
                )
                """, conn);
            return await cmd.ExecuteScalarAsync() is true;
        }, TimeSpan.FromSeconds(30), () => requestTask.IsCompleted);

        await FailIfNotWaitingAsync(observed, requestTask, sideTx, "a users ... FOR UPDATE statement");

        await using (var moveCmd = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u", side, sideTx))
        {
            moveCmd.Parameters.AddWithValue("u", person);
            Assert.Equal(1, await moveCmd.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();

        return new Raced(await requestTask, person, version, trail, mentions);
    }

    /// <summary>
    /// Choreography B: hold <c>organizations</c> in ACCESS EXCLUSIVE, send, wait until a backend waits in the scope check's
    /// organisation read, move the person ON THE SAME CONNECTION, commit, await.
    /// </summary>
    private async Task<Raced> RaceOrganisationsTableAsync(string person, HttpClient client, Func<HttpRequestMessage> build)
    {
        var version = await ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", person));
        var trail = await SelfChangeRefusalTests.WriteTrailAsync(Cs, person);
        var mentions = await MentionsAsync(person);

        await using var side = new NpgsqlConnection(Cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("LOCK TABLE organizations IN ACCESS EXCLUSIVE MODE", side, sideTx))
            await lockCmd.ExecuteNonQueryAsync();

        var requestTask = client.SendAsync(build());

        var observed = await Payroll.PriorMonthExportRaceTests.WaitUntilAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query LIKE 'SELECT * FROM organizations WHERE org_id%'
                      AND query NOT ILIKE '%users%'
                )
                """, conn);
            return await cmd.ExecuteScalarAsync() is true;
        }, TimeSpan.FromSeconds(30), () => requestTask.IsCompleted);

        await FailIfNotWaitingAsync(observed, requestTask, sideTx, "SELECT * FROM organizations WHERE org_id ...");

        // The move runs on THE CONNECTION THAT HOLDS THE TABLE LOCK: a second connection would wait forever on the
        // foreign-key check against organizations.
        await using (var moveCmd = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u", side, sideTx))
        {
            moveCmd.Parameters.AddWithValue("u", person);
            Assert.Equal(1, await moveCmd.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();

        return new Raced(await requestTask, person, version, trail, mentions);
    }

    /// <summary>
    /// When the request completed before a waiter was seen it never reached the race window: release the side transaction, assert the
    /// response first (409 expected; on the code without the guard it is the write's success status) and fail on it; a timeout with
    /// the request still pending fails naming the statement it should have waited in. The same assertion is red under the mutation.
    /// </summary>
    private static async Task FailIfNotWaitingAsync(bool observed, Task<HttpResponseMessage> request, NpgsqlTransaction sideTx, string statement)
    {
        if (observed)
        {
            Assert.False(request.IsCompleted, "the request finished before the side transaction committed.");
            return;
        }

        await sideTx.RollbackAsync();
        if (request.IsCompleted)
        {
            var early = await request;
            var raw = await early.Content.ReadAsStringAsync();
            Assert.True(early.StatusCode == HttpStatusCode.Conflict,
                $"expected 409, got {(int)early.StatusCode}: {raw} (the request completed without waiting in {statement})");
            Assert.Fail($"the request completed ({(int)early.StatusCode}) before any backend was seen waiting in {statement}; it never reached the race window.");
        }
        Assert.Fail($"no backend was seen waiting in {statement} within the timeout and the request is still pending.");
    }

    private async Task AssertDriftRefusedAsync(Raced r)
    {
        var raw = await r.Response.Content.ReadAsStringAsync();
        Assert.True(r.Response.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)r.Response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        var members = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "error" }, members);
        Assert.Equal(DriftError, doc.RootElement.GetProperty("error").GetString());

        Assert.Equal("STY02", await UsersPrimaryOrgAsync(Cs, r.Person));
        Assert.Equal(r.VersionBefore + 1,
            await ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", r.Person)));
        Assert.Equal(r.TrailBefore, await SelfChangeRefusalTests.WriteTrailAsync(Cs, r.Person));
        Assert.Equal(r.MentionsBefore, await MentionsAsync(r.Person));
    }

    /// <summary>
    /// What names the person beyond <see cref="SelfChangeRefusalTests.WriteTrailAsync"/>: every outbox event whose stream or payload
    /// carries the person's id (the overtime, eligibility and approval streams do not end in it), every audit-projection row about
    /// them, every approval row, and the columns a unit change or a birth-date write touches. Ids are unique per fact, so the
    /// text match cannot meet another fact's rows.
    /// </summary>
    private Task<string?> MentionsAsync(string person) => ScalarStringAsync(Cs,
        """
        SELECT concat_ws('|',
          (SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE '%' || @u || '%' OR event_payload::text LIKE '%' || @u || '%'),
          (SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u),
          (SELECT COUNT(*) FROM approval_periods WHERE employee_id = @u),
          (SELECT COUNT(*) FROM reporting_line_audit a JOIN reporting_lines l ON l.reporting_line_id = a.reporting_line_id
             WHERE l.employee_id = @u),
          (SELECT concat_ws(',', birth_date::text, COALESCE(unit_id::text, 'none')) FROM users WHERE user_id = @u))
        """, ("u", person));

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Clients, requests, fixtures
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The LocalHR scoped to STY01 only: admitted by the organisation-scope arm alone (no edge, no unit leadership).</summary>
    private HttpClient HrClient() => LocalHrClient(_host, HrActor, "STY01");

    /// <summary>A LocalAdmin scoped to STY01 only, for W13 and W14 (floor LocalAdmin); no shared helper mints one.</summary>
    private HttpClient AdminClient()
    {
        var client = _host.CreateClient();
        var token = TokenService().GenerateToken(
            employeeId: AdminActor, name: AdminActor, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: "STY01",
            scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, "STY01", "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object? body)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private static HttpRequestMessage IfMatchJson(HttpMethod method, string url, object? body, long ifMatchVersion)
    {
        var req = Json(method, url, body);
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return req;
    }

    private async Task<Guid> InsertPeriodAsync(string person, string status, DateOnly start, DateOnly end)
    {
        var id = Guid.NewGuid();
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, 'STY01', @start, @end, 'MONTHLY', @status, 'HK', 'OK26', NOW(), @emp)
            """, ("id", id), ("emp", person), ("start", start), ("end", end), ("status", status));
        return id;
    }

    private Task<string?> PeriodStatusAsync(Guid period) =>
        ScalarStringAsync(Cs, "SELECT status FROM approval_periods WHERE period_id = @id", ("id", period));

    /// <summary>Covers every weekday of October 2026 with a full-day VACATION absence (October 2026 carries no Danish public holiday,
    /// asserted), the coverage the send command demands; the shape of <c>SendCommandMatrixTestBase.CoverMonthWithAbsencesAsync</c>.</summary>
    private async Task CoverOctoberWithAbsencesAsync(string person)
    {
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= DATE '2026-10-01' AND holiday_date <= DATE '2026-10-31'"));
        for (var d = Oct1; d <= Oct31; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await S120ContractAssert.ExecAsync(Cs,
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK26', NOW(), @seq)
                ON CONFLICT DO NOTHING
                """, ("emp", person), ("date", d), ("seq", Interlocked.Increment(ref _absenceSeq)));
        }
    }

    private async Task<Guid> InsertPendingPreApprovalAsync(string person)
    {
        var id = Guid.NewGuid();
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status, reason)
            VALUES (@id, @e, DATE '2026-11-09', DATE '2026-11-15', 10, 'PENDING', 's148')
            """, ("id", id), ("e", person));
        return id;
    }

    private Task<string?> PreApprovalStatusAsync(Guid id) =>
        ScalarStringAsync(Cs, "SELECT status FROM overtime_pre_approvals WHERE id = @id", ("id", id));

    /// <summary>A <c>vacation_settlements</c> row (sequence 1, version 1) with the minimal valid snapshot of
    /// <c>VacationSettlementEndpointTests.SeedSettlementRowAsync</c>.</summary>
    private async Task InsertSettlementAsync(string person, string state, decimal payout, decimal forfeit)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            transferAgreementDays = 0m,
            isFeriehindret = false,
        });
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, review_disposition, version)
            VALUES (@e, 'VACATION', 2025, 1, @state, 'YEAR_END', @snapshot::jsonb, 0, @payout, @forfeit, NULL, 1)
            """, ("e", person), ("state", state), ("snapshot", snapshot), ("payout", payout), ("forfeit", forfeit));
    }

    private Task<string?> SettlementStateAsync(string person) => ScalarStringAsync(Cs,
        "SELECT settlement_state || '|' || version FROM vacation_settlements WHERE employee_id = @e AND entitlement_year = 2025 AND sequence = 1",
        ("e", person));

    /// <summary>An active line of the given kind from the person to <c>mgr03</c> (STY01's root) in STY01, version 1.</summary>
    private async Task InsertLineAsync(string person, string relationship)
    {
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by)
            VALUES (@e, @m, 'STY01', @r, DATE '2026-01-01', 'MANUAL', 's148')
            """, ("e", person), ("m", Sty01Root), ("r", relationship));
    }

    /// <summary>"1|1" while the person's line of that kind is still active (<c>effective_to IS NULL</c>) at version 1.</summary>
    private Task<string?> ActiveLineAsync(string person, string relationship) => ScalarStringAsync(Cs,
        "SELECT COUNT(*) || '|' || COALESCE(MIN(version), 0) FROM reporting_lines WHERE employee_id = @e AND relationship = @r AND effective_to IS NULL",
        ("e", person), ("r", relationship));

    /// <summary>A boolean column as the shared scalar helper's text ("true"/"false"); the caller's SQL casts to text.</summary>
    private async Task<bool> ScalarBoolAsync(string sql, string person)
        => await ScalarStringAsync(Cs, sql, ("u", person)) == "true";
}
