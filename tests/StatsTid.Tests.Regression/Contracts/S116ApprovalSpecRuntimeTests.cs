using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S116 / TASK-11603 — the per-route spec≡runtime gate extended to the APPROVAL family drained in
/// Pass 3 (10 ops): the two BARE-array list reads (pending + by-month, BOTH query branches each —
/// the my-reports branch and the scope-aggregate branch serialize the SAME shared 9-field
/// <c>ApprovalPeriodListItem</c>), the WIDER 14-field <c>GET /api/approval/{employeeId}</c> bare
/// array, the team-overview envelope proven on BOTH return sites (the empty-roster early return
/// <c>{employees: []}</c> AND the assembled 18-field roster row incl. the zero-period DRAFT row's
/// null periodId and the REJECTED row's populated decisionAt/rejectionReason), the
/// allocation-breakdown envelope (nested allocations element exercised non-vacuously), and the
/// five action receipts (submit / approve / employee-approve / reopen share ONE
/// <c>{periodId, status}</c> record; reject adds the echoed <c>reason</c>).
///
/// <para><b>REAL state machine (the S116 AC):</b> every asserted period status is driven through
/// the REAL API transitions — NEVER an SQL-faked status. S127 / TASK-12712 retired
/// <c>POST /api/approval/submit</c>; the month-keyed <c>POST /api/approval/send</c> now creates the
/// period AND transitions it straight to EMPLOYEE_APPROVED (SUBMITTED is no longer producible by any
/// route). The transitions are: send → approve ⇒ APPROVED; send → reject ⇒ REJECTED; send ⇒
/// EMPLOYEE_APPROVED; send → reopen ⇒ DRAFT. Because <c>/send</c> gates on workday coverage and
/// allocation, each driven employee's whole March 2026 month is first covered with a full-day absence
/// per expected weekday (the vacuous-month recipe — coverage passes, and with no work-time / NORMAL
/// rows the allocation gate is vacuously balanced).</para>
///
/// <para><b>The submittedAt states.</b> Both send adapters stamp <c>submitted_at</c>, so the
/// null-submittedAt state is no longer reachable on a pending (EMPLOYEE_APPROVED) row. It is reached
/// honestly by a reopened DRAFT (send → reopen NULLs submitted_at), surfaced on
/// <c>/api/approval/by-month</c> because that read does not filter by status. The populated state is
/// the e4 EMPLOYEE_APPROVED row.</para>
///
/// <para><b>Seed disjointness (an explicit S116 acceptance criterion):</b> every Organisation
/// (S116AMAO + S116A01–S116A06) and user (s116a_*) is FRESH — no id overlaps with the
/// <c>ApprovalConcurrencyHardeningTests</c> (STY02 + s78_*), <c>S94FlatApprovalTests</c>
/// (STY02/STY05 + s94_*) or <c>ApprovalAtomicTests</c> (STY02 + EMP_FR_AP_*) fixtures.</para>
///
/// <para>Each MUTATION op acts on its OWN dedicated employee/Organisation (xUnit gives no
/// intra-class order guarantee); the read tests consume the fixture-driven roster org S116A01.
/// ONE Docker fixture for the whole family (FAIL-002 discipline). Matcher + Support consumed
/// AS-IS (the S115 compatibility contract).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S116ApprovalSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "S116AMAO";
    private const string RosterOrg = "S116A01";   // the fixture-driven read org (mgr + e1..e5)
    // The per-op employees m1..m5 live in S116A02..S116A06 (seeded in SeedAsync); /send resolves the
    // org server-side, so no per-op org constant is needed.

    private const string GlobalAdminId = "s116a_gadmin";
    private const string ManagerId = "s116a_mgr";
    private const int SendYear = 2026;   // the month-keyed /send target; the read tests query month=3
    private const int SendMonth = 3;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    // Fixture-driven period ids (REAL state machine, driven in InitializeAsync).
    private Guid _e1Approved;         // s116a_e1 → APPROVED
    private Guid _e2Rejected;         // s116a_e2 → REJECTED
    private Guid _e4EmpApproved;      // s116a_e4 → EMPLOYEE_APPROVED (submittedAt POPULATED)
    private Guid _e5ReopenedDraft;    // s116a_e5 → send then reopen ⇒ DRAFT with NULL submittedAt

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree)

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await SeedAsync(conn);
        }

        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();

        // Drive the FIXTURE periods through the REAL state machine (no SQL-faked statuses). SendAsync
        // covers each employee's month first, then posts /send (→ EMPLOYEE_APPROVED).

        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);

        // e1: send → approve ⇒ APPROVED (approvedBy/approvedAt populated; /send already reached
        // EMPLOYEE_APPROVED, and approve accepts it).
        _e1Approved = await SendAsync(admin, "s116a_e1");
        await PostActionAsync(admin, $"/api/approval/{_e1Approved}/approve", jsonBody: null);

        // e2: send → reject ⇒ REJECTED (rejectionReason populated; decisionAt written; reject accepts
        // EMPLOYEE_APPROVED).
        _e2Rejected = await SendAsync(admin, "s116a_e2");
        await PostActionAsync(admin, $"/api/approval/{_e2Rejected}/reject",
            """{ "reason": "S116 afvist — dokumentation mangler" }""");

        // e4: send ⇒ EMPLOYEE_APPROVED (submittedAt POPULATED — the pending/by-month populated state;
        // the reachable manager-visible state now that SUBMITTED is un-producible).
        _e4EmpApproved = await SendAsync(admin, "s116a_e4");

        // e5: send → reopen ⇒ DRAFT (reopen NULLs submitted_at). The only honest way to a null
        // submitted_at now that both send adapters stamp it. NOT pending (drops out of /pending);
        // surfaced on /by-month, which does not filter by status.
        _e5ReopenedDraft = await SendAsync(admin, "s116a_e5");
        await PostActionAsync(admin, $"/api/approval/{_e5ReopenedDraft}/reopen",
            """{ "reason": "S116 genåbnet for DRAFT null-submittedAt state" }""");
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET /api/approval/pending — BOTH query branches (bare array, shared 9-field element).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The SCOPE-AGGREGATE branch (no my-reports): a GLOBAL actor's pending list. Asserts
    /// the bare-array element against the spec (enum fidelity on status/periodType rides the matcher)
    /// AND pins the POPULATED submittedAt on the e4 EMPLOYEE_APPROVED row. S127 / TASK-12712: the
    /// null-submittedAt state is no longer reachable on a pending row (both send adapters stamp
    /// submitted_at), so it moved to the /by-month reopened-DRAFT assertion; e5 is a reopened DRAFT and
    /// is NOT pending, so it does not appear here.</summary>
    [Fact]
    public async Task Pending_ScopeBranch_BareArraySchemaMatchesRuntime_SubmittedAtPopulated()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/pending"),
            "/api/approval/pending", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        var e4 = FindByPeriodId(rows, _e4EmpApproved);
        Assert.Equal("EMPLOYEE_APPROVED", e4.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, e4.GetProperty("submittedAt").ValueKind); // POPULATED
        Assert.Equal("MONTHLY", e4.GetProperty("periodType").GetString());
    }

    /// <summary>The MY-REPORTS branch (<c>?my-reports=true</c>): the designated manager's own pending
    /// set — the SAME shared element record from the OTHER return site. The e4 EMPLOYEE_APPROVED row's
    /// POPULATED submittedAt again, proving the branch serializes the identical shape (e5 is a reopened
    /// DRAFT and is not pending).</summary>
    [Fact]
    public async Task Pending_MyReportsBranch_BareArraySchemaMatchesRuntime_SubmittedAtPopulated()
    {
        using var mgr = CreateActorClient(ManagerId, StatsTidRoles.LocalLeader, RosterOrg,
            new RoleScope(StatsTidRoles.LocalLeader, RosterOrg, "ORG_ONLY"));
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, mgr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/pending?my-reports=true"),
            "/api/approval/pending", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        var e4 = FindByPeriodId(rows, _e4EmpApproved);
        Assert.Equal(JsonValueKind.String, e4.GetProperty("submittedAt").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET /api/approval/by-month — BOTH query branches (same shared element).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ByMonth_ScopeBranch_BareArraySchemaMatchesRuntime_EnumFidelityAcrossStatuses()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/by-month?year=2026&month=3"),
            "/api/approval/by-month", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        // Enum fidelity exercised on REAL rows across the state machine's reachable statuses
        // (the matcher already rejects any out-of-set value on EVERY row; these pin the literals).
        Assert.Equal("APPROVED", FindByPeriodId(rows, _e1Approved).GetProperty("status").GetString());
        Assert.Equal("REJECTED", FindByPeriodId(rows, _e2Rejected).GetProperty("status").GetString());
        var e4 = FindByPeriodId(rows, _e4EmpApproved);
        Assert.Equal("EMPLOYEE_APPROVED", e4.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, e4.GetProperty("submittedAt").ValueKind); // POPULATED
        // The honest DRAFT-null pin: /by-month does not filter by status, so the reopened DRAFT row
        // (send → reopen NULLs submitted_at) yields a GENUINE null — no SQL fakery.
        var e5 = FindByPeriodId(rows, _e5ReopenedDraft);
        Assert.Equal("DRAFT", e5.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, e5.GetProperty("submittedAt").ValueKind);   // NULL state
    }

    [Fact]
    public async Task ByMonth_MyReportsBranch_BareArraySchemaMatchesRuntime()
    {
        using var mgr = CreateActorClient(ManagerId, StatsTidRoles.LocalLeader, RosterOrg,
            new RoleScope(StatsTidRoles.LocalLeader, RosterOrg, "ORG_ONLY"));
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, mgr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/by-month?year=2026&month=3&my-reports=true"),
            "/api/approval/by-month", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        // The manager's designated reports carry all four fixture periods this month (e5 is a reopened
        // DRAFT — by-month does not filter by status, so it is present too).
        FindByPeriodId(rows, _e1Approved);
        FindByPeriodId(rows, _e2Rejected);
        FindByPeriodId(rows, _e4EmpApproved);
        FindByPeriodId(rows, _e5ReopenedDraft);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET /api/approval/{employeeId} — the WIDER 14-field element; nullable
    //  approvedBy/approvedAt/rejectionReason in BOTH states.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmployeePeriods_Get_14FieldElement_NullableDecisionFieldsBothStates()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);

        // The APPROVED employee: approvedBy/approvedAt POPULATED, rejectionReason null.
        var approvedBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/s116a_e1"),
            "/api/approval/{employeeId}", "get");
        var approvedRow = FindByPeriodId(JsonDocument.Parse(approvedBody).RootElement, _e1Approved);
        Assert.Equal(GlobalAdminId, approvedRow.GetProperty("approvedBy").GetString());
        Assert.Equal(JsonValueKind.String, approvedRow.GetProperty("approvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, approvedRow.GetProperty("rejectionReason").ValueKind);
        Assert.Equal("OK24", approvedRow.GetProperty("okVersion").GetString());

        // The REJECTED employee: rejectionReason POPULATED (echoed through the state machine).
        var rejectedBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/s116a_e2"),
            "/api/approval/{employeeId}", "get");
        var rejectedRow = FindByPeriodId(JsonDocument.Parse(rejectedBody).RootElement, _e2Rejected);
        Assert.Equal("S116 afvist — dokumentation mangler", rejectedRow.GetProperty("rejectionReason").GetString());

        // The EMPLOYEE_APPROVED employee: all three MANAGER-decision fields NULL (pre-manager-decision
        // — /send has employee-approved it but no manager has approved or rejected it).
        var empApprovedBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/s116a_e4"),
            "/api/approval/{employeeId}", "get");
        var empApprovedRow = FindByPeriodId(JsonDocument.Parse(empApprovedBody).RootElement, _e4EmpApproved);
        Assert.Equal(JsonValueKind.Null, empApprovedRow.GetProperty("approvedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, empApprovedRow.GetProperty("approvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, empApprovedRow.GetProperty("rejectionReason").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET /api/approval/team-overview — BOTH return sites of the envelope.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Return site 1 (the empty-roster early return): an actor with NO designated
    /// reports gets <c>{ employees: [] }</c> — the SAME envelope shape, empty.</summary>
    [Fact]
    public async Task TeamOverview_EmptyRosterSite_EnvelopeSchemaMatchesRuntime()
    {
        using var lonely = CreateActorClient("s116a_lonely", StatsTidRoles.LocalLeader, RosterOrg,
            new RoleScope(StatsTidRoles.LocalLeader, RosterOrg, "ORG_ONLY"));
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, lonely,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/team-overview?year=2026&month=3"),
            "/api/approval/team-overview", "get");

        var employees = JsonDocument.Parse(body).RootElement.GetProperty("employees");
        Assert.Equal(JsonValueKind.Array, employees.ValueKind);
        Assert.Equal(0, employees.GetArrayLength());
    }

    /// <summary>Return site 2 (the assembled roster): the 18-field handler-assembled row, with the
    /// state-dependent nullables pinned — the zero-period DRAFT row's NULL periodId, the REJECTED
    /// row's POPULATED decisionAt + rejectionReason, and the APPROVED row's NULL rejectionReason.</summary>
    [Fact]
    public async Task TeamOverview_AssembledRosterSite_18FieldRow_StateDependentNullables()
    {
        using var mgr = CreateActorClient(ManagerId, StatsTidRoles.LocalLeader, RosterOrg,
            new RoleScope(StatsTidRoles.LocalLeader, RosterOrg, "ORG_ONLY"));
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, mgr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/approval/team-overview?year=2026&month=3"),
            "/api/approval/team-overview", "get");

        var employees = JsonDocument.Parse(body).RootElement.GetProperty("employees");
        Assert.Equal(5, employees.GetArrayLength()); // e1..e5 — the manager's full designated set

        // The zero-period DRAFT row (e3 has NO approval_periods row): null periodId + DRAFT.
        var draftRow = FindByEmployeeId(employees, "s116a_e3");
        Assert.Equal(JsonValueKind.Null, draftRow.GetProperty("periodId").ValueKind);
        Assert.Equal("DRAFT", draftRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, draftRow.GetProperty("submittedAt").ValueKind);

        // The REJECTED row: decisionAt + rejectionReason POPULATED.
        var rejectedRow = FindByEmployeeId(employees, "s116a_e2");
        Assert.Equal("REJECTED", rejectedRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, rejectedRow.GetProperty("decisionAt").ValueKind);
        Assert.Equal("S116 afvist — dokumentation mangler", rejectedRow.GetProperty("rejectionReason").GetString());

        // The APPROVED row: rejectionReason NULL (decisionAt populated).
        var approvedRow = FindByEmployeeId(employees, "s116a_e1");
        Assert.Equal("APPROVED", approvedRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, approvedRow.GetProperty("decisionAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, approvedRow.GetProperty("rejectionReason").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET /api/approval/{employeeId}/allocation-breakdown — nested allocations element.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllocationBreakdown_Get200_EnvelopeAndNestedAllocationsElementMatchRuntime()
    {
        // S128 / TASK-12804 — the read is now leader-tier month-gated (RES-002): a LocalLeader may
        // fetch the breakdown only for a month the employee has SENT, and e3 deliberately has NO
        // approval_periods row (it is the TeamOverview zero-period DRAFT fixture), which the
        // fail-closed gate 403s. Seed a SUBMITTED row for exactly this month so the test keeps
        // verifying what it always verified — the 200 contract envelope. Direct SQL (not /send):
        // SUBMITTED is un-producible by any route since S127/TASK-12712, this status is an auth-gate
        // precondition rather than an asserted period state, and e3's seeded NORMAL projection row
        // would fail /send's allocation gate anyway.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO approval_periods
                    (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                     agreement_code, ok_version, submitted_at, submitted_by)
                VALUES
                    (gen_random_uuid(), 's116a_e3', 'S116A01', '2026-03-01', '2026-03-31', 'MONTHLY',
                     'SUBMITTED', 'HK', 'OK24', NOW(), 's116a_e3')
                ON CONFLICT DO NOTHING
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        using var mgr = CreateActorClient(ManagerId, StatsTidRoles.LocalLeader, RosterOrg,
            new RoleScope(StatsTidRoles.LocalLeader, RosterOrg, "ORG_ONLY"));
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, mgr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                "/api/approval/s116a_e3/allocation-breakdown?year=2026&month=3"),
            "/api/approval/{employeeId}/allocation-breakdown", "get");

        // Non-vacuous: the seeded S116TASK1 projection row surfaces as a NESTED allocations
        // element (the {taskId, hours} item schema was actually exercised by the matcher walk).
        var allocations = JsonDocument.Parse(body).RootElement.GetProperty("allocations");
        Assert.True(allocations.GetArrayLength() >= 1, "allocation-breakdown returned an empty allocations array — seed missing");
        Assert.Equal("S116TASK1", allocations[0].GetProperty("taskId").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The five ACTION receipts — each op on its OWN dedicated employee/Organisation,
    //  driven through the REAL state machine INSIDE the test.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Send — the FIRST adapter (<c>POST /api/approval/send</c>), the retired
    /// <c>/api/approval/submit</c>'s replacement (S127 / TASK-12712). The shared action receipt
    /// (<c>{periodId, status}</c>); the create arm transitions straight to EMPLOYEE_APPROVED. The month
    /// is absence-covered first so the coverage + allocation gates pass.</summary>
    [Fact]
    public async Task Send_Post200_SchemaMatchesRuntime_StatusLiteralEMPLOYEE_APPROVED()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        await SeedAbsenceCoveredMonthAsync("s116a_m1", SendYear, SendMonth);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/approval/send",
                SendBody("s116a_m1")),
            "/api/approval/send", "post");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approve_Post200_SchemaMatchesRuntime_StatusLiteralAPPROVED()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var periodId = await SendAsync(admin, "s116a_m2"); // → EMPLOYEE_APPROVED, then manager-approve
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, $"/api/approval/{periodId}/approve"),
            "/api/approval/{periodId}/approve", "post");
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("APPROVED", root.GetProperty("status").GetString());
        Assert.Equal(periodId, root.GetProperty("periodId").GetGuid());
    }

    /// <summary>EmployeeApprove — the SECOND (by-id) send adapter. To exercise it the period must be in
    /// a re-sendable state: /send creates+approves it, reopen returns it to DRAFT, then this by-id
    /// adapter re-sends it (the whole-month guard passes — /send made it a whole calendar month — and
    /// the month is still absence-covered).</summary>
    [Fact]
    public async Task EmployeeApprove_Post200_SchemaMatchesRuntime_StatusLiteralEMPLOYEE_APPROVED()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var periodId = await SendAsync(admin, "s116a_m3");
        await PostActionAsync(admin, $"/api/approval/{periodId}/reopen",
            """{ "reason": "S116 employee-approve-op setup" }""");
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, $"/api/approval/{periodId}/employee-approve"),
            "/api/approval/{periodId}/employee-approve", "post");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString());
    }

    /// <summary>Reject — the <c>+reason</c> SIBLING record (NOT the shared action receipt): the
    /// request reason is echoed back on the wire.</summary>
    [Fact]
    public async Task Reject_Post200_SiblingSchemaMatchesRuntime_ReasonEchoed()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var periodId = await SendAsync(admin, "s116a_m4"); // → EMPLOYEE_APPROVED, then manager-reject
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, $"/api/approval/{periodId}/reject",
                """{ "reason": "S116 reject-op afvisningsgrund" }"""),
            "/api/approval/{periodId}/reject", "post");
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("REJECTED", root.GetProperty("status").GetString());
        Assert.Equal("S116 reject-op afvisningsgrund", root.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Reopen_Post200_SchemaMatchesRuntime_StatusLiteralDRAFT()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var periodId = await SendAsync(admin, "s116a_m5"); // /send reaches EMPLOYEE_APPROVED directly
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, $"/api/approval/{periodId}/reopen",
                """{ "reason": "S116 reopen-op" }"""),
            "/api/approval/{periodId}/reopen", "post");
        Assert.Equal("DRAFT", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString());
    }

    // ── Support ──

    private static string SendBody(string employeeId) =>
        $$"""{ "employeeId": "{{employeeId}}", "year": {{SendYear}}, "month": {{SendMonth}} }""";

    /// <summary>Cover the March month's expected weekdays then send it via the REAL <c>/send</c>
    /// adapter (server-resolved org/agreement/okVersion, MONTHLY, → EMPLOYEE_APPROVED); returns the
    /// created periodId. Used by the fixture chain and per-op test setup.</summary>
    private async Task<Guid> SendAsync(HttpClient client, string employeeId)
    {
        await SeedAbsenceCoveredMonthAsync(employeeId, SendYear, SendMonth);
        var body = await PostActionAsync(client, "/api/approval/send", SendBody(employeeId));
        return JsonDocument.Parse(body).RootElement.GetProperty("periodId").GetGuid();
    }

    private long _absenceOutboxSeq = 51_160_000;

    /// <summary>The vacuous-month coverage recipe (AC-6 shape): a full-day absence on every expected
    /// weekday of the month so the send command's workday-coverage gate passes, while the allocation
    /// gate stays vacuously balanced (no work-time / NORMAL rows are seeded for these employees).
    /// Weekends are skipped; March 2026 carries no Danish public holiday, so covering every weekday is
    /// exactly the expected-workday set. Direct projection insert — the coverage read only asks whether
    /// each expected workday carries ≥1 registration.</summary>
    private async Task SeedAbsenceCoveredMonthAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        for (var d = new DateOnly(year, month, 1); d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage, agreement_code, ok_version, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @outbox)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("outbox", _absenceOutboxSeq++);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>A REAL state-machine transition call (setup, not the op under test): POST and
    /// require 200, surfacing the response body on failure (seed/authority diagnostics).</summary>
    private static async Task<string> PostActionAsync(HttpClient client, string url, string? jsonBody)
    {
        using var response = await client.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, url, jsonBody));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"State-machine setup call POST {url} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    /// <summary>A client for an arbitrary actor/role/scope set (the family's non-GlobalAdmin
    /// actors: the designated manager + the empty-roster leader). Mirrors the Support helper's
    /// JWT minting — Support itself is consumed AS-IS (S115 compatibility contract).</summary>
    private HttpClient CreateActorClient(string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JsonElement FindByPeriodId(JsonElement array, Guid periodId)
    {
        foreach (var el in array.EnumerateArray())
            if (el.GetProperty("periodId").ValueKind == JsonValueKind.String
                && el.GetProperty("periodId").GetGuid() == periodId)
                return el;
        throw new XunitException($"Expected a row with periodId {periodId} in: {array.GetRawText()}");
    }

    private static JsonElement FindByEmployeeId(JsonElement array, string employeeId)
    {
        foreach (var el in array.EnumerateArray())
            if (el.GetProperty("employeeId").GetString() == employeeId)
                return el;
        throw new XunitException($"Expected a row with employeeId {employeeId} in: {array.GetRawText()}");
    }

    // ── Fixture seed — FRESH MAO + Organisations + users, DISJOINT from the approval suites
    //    (ApprovalConcurrencyHardeningTests: STY02+s78_*; S94FlatApprovalTests: STY02/STY05+s94_*;
    //    ApprovalAtomicTests: STY02+EMP_FR_AP_*). Periods are NEVER seeded here — the state
    //    machine drives them via the API (InitializeAsync + per-op tests). ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S116AMAO', 'S116 Godkendelse Ministerie',  'MAO',          NULL,       '/S116AMAO/',           'AC', 'OK24'),
                ('S116A01',  'S116 Roster-styrelse',         'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A01/',   'HK', 'OK24'),
                ('S116A02',  'S116 Indsend-op',              'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A02/',   'HK', 'OK24'),
                ('S116A03',  'S116 Godkend-op',              'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A03/',   'HK', 'OK24'),
                ('S116A04',  'S116 MedarbGodkend-op',        'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A04/',   'HK', 'OK24'),
                ('S116A05',  'S116 Afvis-op',                'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A05/',   'HK', 'OK24'),
                ('S116A06',  'S116 Genåbn-op',               'ORGANISATION', 'S116AMAO', '/S116AMAO/S116A06/',   'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // S127 / TASK-12701a — a project catalogue for every fixture org (AC-14a). Projects are
        // strictly org-scoped (ProjectRepository.GetByOrgAsync), so a fixture org without one leaves
        // its employees with an EMPTY project set: they can never allocate their hours, and once the
        // S127 submit-time allocation gate lands, no period in these orgs could reach a
        // manager-visible state. Seeded alongside the orgs so the fixture never depends on the
        // baseline's STY02 rows. project_id is omitted (defaults to gen_random_uuid(); allocations
        // key on project_code, SkemaEndpoints.cs:1609).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by) VALUES
                ('S116AMAO', 'ADM-01',   'Administration og ledelse',   1, 'seed'),
                ('S116A01',  'DRIFT-01', 'Daglig drift',                1, 'seed'),
                ('S116A01',  'PROJ-01',  'Tvaergaaende projekt',        2, 'seed'),
                ('S116A02',  'DRIFT-01', 'Daglig drift',                1, 'seed'),
                ('S116A03',  'DRIFT-01', 'Daglig drift',                1, 'seed'),
                ('S116A04',  'DRIFT-01', 'Daglig drift',                1, 'seed'),
                ('S116A05',  'DRIFT-01', 'Daglig drift',                1, 'seed'),
                ('S116A06',  'DRIFT-01', 'Daglig drift',                1, 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s116a_mgr', 's116a_mgr', '$2a$11$fake', 'S116 Teamleder',            's116a_mgr@test.dk', 'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_e1',  's116a_e1',  '$2a$11$fake', 'S116 Godkendt Medarbejder', 's116a_e1@test.dk',  'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_e2',  's116a_e2',  '$2a$11$fake', 'S116 Afvist Medarbejder',   's116a_e2@test.dk',  'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_e3',  's116a_e3',  '$2a$11$fake', 'S116 Kladde Medarbejder',   's116a_e3@test.dk',  'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_e4',  's116a_e4',  '$2a$11$fake', 'S116 Indsendt Medarbejder', 's116a_e4@test.dk',  'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_e5',  's116a_e5',  '$2a$11$fake', 'S116 NulIndsendt Medarb',   's116a_e5@test.dk',  'S116A01', 'HK', 'OK24', TRUE),
                ('s116a_m1',  's116a_m1',  '$2a$11$fake', 'S116 Indsend-op Medarb',    's116a_m1@test.dk',  'S116A02', 'HK', 'OK24', TRUE),
                ('s116a_m2',  's116a_m2',  '$2a$11$fake', 'S116 Godkend-op Medarb',    's116a_m2@test.dk',  'S116A03', 'HK', 'OK24', TRUE),
                ('s116a_m3',  's116a_m3',  '$2a$11$fake', 'S116 MedarbGodk-op Medarb', 's116a_m3@test.dk',  'S116A04', 'HK', 'OK24', TRUE),
                ('s116a_m4',  's116a_m4',  '$2a$11$fake', 'S116 Afvis-op Medarb',      's116a_m4@test.dk',  'S116A05', 'HK', 'OK24', TRUE),
                ('s116a_m5',  's116a_m5',  '$2a$11$fake', 'S116 Genåbn-op Medarb',     's116a_m5@test.dk',  'S116A06', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // The designated-approver edges: e1..e5 report to the manager (the my-reports branches,
        // the team-overview roster and the allocation-breakdown auth all resolve through these).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by) VALUES
                ('s116a_e1', 's116a_mgr', 'S116A01', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116a_e2', 's116a_mgr', 'S116A01', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116a_e3', 's116a_mgr', 'S116A01', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116a_e4', 's116a_mgr', 'S116A01', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116a_e5', 's116a_mgr', 'S116A01', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // The manager's LeaderOrAbove DB floor (the R5 predicate requires an active LOCAL_LEADER+
        // role assignment — the JWT role alone does not satisfy IsActiveLeaderOrAboveAsync).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                ('s116a_mgr', 'LOCAL_LEADER', 'S116A01', 'ORG_ONLY', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // One NORMAL + task-tagged projection row for the zero-period employee (e3) so the
        // allocation-breakdown's NESTED allocations element is exercised non-vacuously. A weekday
        // OUTSIDE any driven period (e3 has no period) — no state-machine gate is affected.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, task_id, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (@eventId, 's116a_e3', '2026-03-10', 7.4, 'S116TASK1', 'NORMAL', 'HK', 'OK24', FALSE, NOW(), 511601)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("eventId", Guid.Parse("51160000-0000-0000-0000-00000000a301"));
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
