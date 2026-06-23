using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S70 / TASK-7003 (ADR-033 slice 3a, SPRINT-70 R9) — the terminated-employee access path:
/// the R9e denial matrix + the R9c allowlist proofs + the R9a repository pins.
///
/// <para><b>What S68's B2 was:</b> once an employee is deactivated (<c>is_active = FALSE</c>),
/// every <c>UserRepository</c> read and the shared
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> stop resolving them, so HR got
/// 403/404 on the leaver's settlement surfaces. The S70 fix is ADDITIVE at both layers
/// (new <c>...IncludingTerminatedAsync</c> methods + a new validator), wired into EXACTLY three
/// per-employee-target surfaces (R9c): the settlement manual-resolve endpoint, the
/// reconcile-payout endpoint, and the year-overview read. Everything else keeps denying.</para>
///
/// <para><b>Matrix summary (R9e):</b> non-HR (Employee/LocalLeader) → 403 on each allowlisted
/// endpoint for a terminated target; HR outside the leaver's org subtree → 403; HR inside the
/// subtree → SUCCEEDS (the B2 resolution proof); a terminated target via a NON-allowlisted
/// <c>ValidateEmployeeAccessAsync</c> caller → still 403; a terminated employee's OWN still-valid
/// JWT → the existing validator's own-data branch passes but active-only endpoints 404 on the
/// filtered <c>GetByIdAsync</c> (pre-existing, pinned). Plus the R9c payout-pending pin: the
/// org-filtered collection's <c>users</c> join carries no <c>is_active</c> predicate, so a
/// deactivated leaver's rows still appear (TEST pin only — no query change in S70).</para>
///
/// <para><b>R9f (Step-5a hardening, Codex 2B, 2026-06-10; f2 re-hardened cycle-3 per Codex
/// cycle-2 B2, 2026-06-11):</b> (f1) for a TERMINATED target the ADMITTING scope must itself be
/// HROrAbove (incl. the GLOBAL branch) — mixed-role/mixed-org JWT escalation pins; (f2) the
/// year-overview body read is read-first-REVALIDATE: self → terminated-inclusive; non-self →
/// active-only read FIRST, on null re-run the terminated-inclusive validator against CURRENT
/// target state, deny ⇒ the same 404 as not-found (no terminated-vs-nonexistent oracle). The
/// earlier privilege-keyed f2 shape trusted the un-org-bound PRIMARY role, so a mixed-role JWT
/// kept the TOCTOU leak. The selection is pinned DIRECTLY on the extracted production method
/// <see cref="BalanceEndpoints.ReadYearOverviewTargetAsync"/> (HTTP cannot interleave the flip
/// inside one request; the sealed repo/validator preclude doubles).</para>
///
/// <para>Fixture/JWT conventions mirror <see cref="Settlement.VacationSettlementEndpointTests"/>
/// (same WAF harness, token minting, direct <c>vacation_settlements</c> seeding).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TerminatedEmployeeAccessTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";        // target employees' org (/MIN01/STY01/)
    private const string DisjointOrg = "STY05";  // /MIN02/STY05/ — disjoint from STY01
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)
    private const string VacationType = "VACATION";
    private const int SettledYear = 2025;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (org tree MIN01/STY01/STY05 + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9a — repository pins: terminated-inclusive reads return the inactive row;
    // every ACTIVE-ONLY read still refuses it (the shared filter is untouched).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An <c>is_active = FALSE</c> row: the active-only reads (standalone, in-tx,
    /// with-version) all return null — and EVERY new <c>...IncludingTerminatedAsync</c>
    /// counterpart returns the row (IsActive false, version intact).</summary>
    [Fact]
    public async Task Repo_TerminatedRow_ActiveOnlyReadsNull_TerminatedInclusiveReadsReturnIt()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var repo = new UserRepository(new DbConnectionFactory(_harness.ConnectionString));

        // Existing active-only reads — all refuse the deactivated row (byte-untouched semantics).
        Assert.Null(await repo.GetByIdAsync(employeeId));
        Assert.Null(await repo.GetByIdWithVersionAsync(employeeId));

        // New standalone terminated-inclusive reads.
        var user = await repo.GetByIdIncludingTerminatedAsync(employeeId);
        Assert.NotNull(user);
        Assert.False(user!.IsActive);
        Assert.Equal(OrgId, user.PrimaryOrgId);

        var withVersion = await repo.GetByIdWithVersionIncludingTerminatedAsync(employeeId);
        Assert.NotNull(withVersion);
        Assert.False(withVersion!.Value.User.IsActive);
        Assert.Equal(1L, withVersion.Value.Version); // users.version seeds at 1

        // In-tx counterparts (the R9d settlement-pass overload + the FOR-UPDATE If-Match read).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        Assert.Null(await repo.GetByIdAsync(conn, tx, employeeId)); // existing in-tx read: still filtered
        var inTx = await repo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId);
        Assert.NotNull(inTx);
        Assert.False(inTx!.IsActive);

        var lockedRead = await repo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId);
        Assert.NotNull(lockedRead);
        Assert.False(lockedRead!.Value.User.IsActive);
        Assert.Equal(1L, lockedRead.Value.Version);

        await tx.RollbackAsync();
    }

    /// <summary>The R9a guarded end-date write addresses an INACTIVE row (no false 404):
    /// correct version → version bump + the lifecycle tuple persists; stale version →
    /// <see cref="OptimisticConcurrencyException"/> carrying the ACTUAL version (412 material,
    /// not 404); unknown user → <see cref="KeyNotFoundException"/>.</summary>
    [Fact]
    public async Task Repo_SetEmploymentEndDate_GuardedWrite_WorksOnInactiveRow()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var repo = new UserRepository(new DbConnectionFactory(_harness.ConnectionString));
        var endDate = new DateOnly(2026, 3, 31);

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var newVersion = await repo.SetEmploymentEndDateIncludingTerminatedAsync(
                conn, tx, employeeId, endDate, endDateDeactivated: true, isActive: false,
                expectedVersion: 1);
            Assert.Equal(2L, newVersion);
            await tx.CommitAsync();
        }

        var (persistedEndDate, persistedProvenance, persistedActive, persistedVersion) =
            await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(endDate, persistedEndDate);
        Assert.True(persistedProvenance);
        Assert.False(persistedActive);
        Assert.Equal(2L, persistedVersion);

        // Stale If-Match on the INACTIVE row → OCC with the actual version (NOT a 404-shaped
        // KeyNotFound — the probe deliberately carries no is_active filter).
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var occ = await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
                repo.SetEmploymentEndDateIncludingTerminatedAsync(
                    conn, tx, employeeId, null, endDateDeactivated: false, isActive: true,
                    expectedVersion: 1));
            Assert.Equal(2L, occ.ActualVersion);
            await tx.RollbackAsync();
        }

        // Unknown user → KeyNotFound (the row genuinely does not exist).
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                repo.SetEmploymentEndDateIncludingTerminatedAsync(
                    conn, tx, "no_such_user_s70", null, endDateDeactivated: false, isActive: true,
                    expectedVersion: 1));
            await tx.RollbackAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9b — validator-level matrix for the NEW ValidateEmployeeAccessIncludingTerminatedAsync
    // + the R9e terminated-self pin on the EXISTING validator's own-data branch.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>New-validator matrix, terminated target: HR-in-subtree allowed; HR-disjoint
    /// denied; in-scope LocalLeader denied (the R9b HROrAbove gate on terminated data);
    /// Employee-only denied outright (NO own-data branch on this surface — decision documented
    /// on the validator). ACTIVE-target parity: an in-scope LocalLeader is still allowed (the
    /// new validator must not regress the pre-existing leader read surface it now fronts).</summary>
    [Fact]
    public async Task Validator_IncludingTerminated_Matrix()
    {
        var terminated = await SeedTerminatedEmployeeAsync();
        var active = await SeedActiveEmployeeAsync();
        var validator = MakeValidator();

        // HR inside the subtree → allowed (the B2 fix at the validator layer).
        var hrIn = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            HrActor("hr_v_in", CoveringOrg), terminated);
        Assert.True(hrIn.Allowed);

        // HR outside the subtree → denied (subtree binding unchanged).
        var hrOut = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            HrActor("hr_v_out", DisjointOrg), terminated);
        Assert.False(hrOut.Allowed);

        // In-scope LocalLeader, terminated target → denied (HROrAbove gate).
        var leader = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            LeaderActor("ldr_v", OrgId), terminated);
        Assert.False(leader.Allowed);

        // Employee-only actor → denied outright, even for their own id (no own-data branch).
        var self = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            EmployeeActor(terminated, OrgId), terminated);
        Assert.False(self.Allowed);

        // ACTIVE-target parity: in-scope leader still allowed (identical to the shared
        // validator's non-Employee path — the R9c wiring must not strip leader access
        // to ACTIVE employees on the year-overview surface).
        var leaderActive = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            LeaderActor("ldr_v2", OrgId), active);
        Assert.True(leaderActive.Allowed);
    }

    /// <summary>R9e terminated-self pin, validator half: the EXISTING validator's own-data
    /// short-circuit passes for a terminated employee's own id (it never resolves the target
    /// row at all) — the denial happens at the resource layer instead (next test). Pinned so
    /// the S70 access work is on record as NOT having changed the shared validator.</summary>
    [Fact]
    public async Task Validator_Existing_OwnDataBranch_TerminatedSelf_StillPasses()
    {
        var terminated = await SeedTerminatedEmployeeAsync();
        var validator = MakeValidator();

        var self = await validator.ValidateEmployeeAccessAsync(
            EmployeeActor(terminated, OrgId), terminated);
        Assert.True(self.Allowed);

        // And the SAME existing validator still refuses the terminated target for everyone
        // else — here an in-subtree HR — because its target read is is_active-filtered.
        var hrIn = await validator.ValidateEmployeeAccessAsync(
            HrActor("hr_v_legacy", CoveringOrg), terminated);
        Assert.False(hrIn.Allowed);
        Assert.Equal("Target employee not found", hrIn.Reason);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9c/R9e — endpoint matrix: the manual-resolve endpoint.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>B2 proof #1: in-subtree HR FORFEIT-resolves a terminated leaver's
    /// PENDING_REVIEW settlement → 200 SETTLED (this exact call was 403 in S68).</summary>
    [Fact]
    public async Task Resolve_TerminatedTarget_HrInScope_Succeeds()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 0m, forfeit: 20m, version: 1);

        var rsp = await ResolveAsync(HrClient(CoveringOrg), employeeId, SettledYear,
            disposition: "FORFEIT", ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SETTLED", body.GetProperty("settlementState").GetString());
        Assert.Equal(20m, body.GetProperty("forfeitDays").GetDecimal());
    }

    /// <summary>Non-HR on a terminated target's resolve → 403 (Employee and LocalLeader are
    /// both outside the HROrAbove endpoint policy; the new validator never even runs).</summary>
    [Fact]
    public async Task Resolve_TerminatedTarget_NonHr_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 0m, forfeit: 20m, version: 1);

        var empRsp = await ResolveAsync(ClientWith(EmployeeToken("emp_qa_r", OrgId)),
            employeeId, SettledYear, disposition: "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, empRsp.StatusCode);

        var leaderRsp = await ResolveAsync(ClientWith(LeaderToken("ldr_qa_r", OrgId)),
            employeeId, SettledYear, disposition: "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
    }

    /// <summary>HR outside the leaver's org subtree → 403 on resolve (subtree binding holds
    /// on the terminated-inclusive path).</summary>
    [Fact]
    public async Task Resolve_TerminatedTarget_HrOutOfScope_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 0m, forfeit: 20m, version: 1);

        var rsp = await ResolveAsync(HrClient(DisjointOrg), employeeId, SettledYear,
            disposition: "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9c/R9e — endpoint matrix: the reconcile-payout endpoint.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>B2 proof #2: in-subtree HR reconciles a terminated leaver's §24 payout
    /// bucket → 200 with the reconciliation marker set.</summary>
    [Fact]
    public async Task ReconcilePayout_TerminatedTarget_HrInScope_Succeeds()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1);

        var rsp = await ReconcilePayoutAsync(HrClient(CoveringOrg), employeeId, SettledYear, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("payoutReconciledBy").GetString()));
    }

    /// <summary>Non-HR (Employee + LocalLeader) → 403 on a terminated target's reconcile-payout.</summary>
    [Fact]
    public async Task ReconcilePayout_TerminatedTarget_NonHr_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1);

        var empRsp = await ReconcilePayoutAsync(ClientWith(EmployeeToken("emp_qa_rp", OrgId)),
            employeeId, SettledYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, empRsp.StatusCode);

        var leaderRsp = await ReconcilePayoutAsync(ClientWith(LeaderToken("ldr_qa_rp", OrgId)),
            employeeId, SettledYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
    }

    /// <summary>HR outside the subtree → 403 on a terminated target's reconcile-payout.</summary>
    [Fact]
    public async Task ReconcilePayout_TerminatedTarget_HrOutOfScope_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1);

        var rsp = await ReconcilePayoutAsync(HrClient(DisjointOrg), employeeId, SettledYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9c/R9e — endpoint matrix: the year-overview read.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>B2 proof #3: in-subtree HR reads a terminated leaver's year-overview → 200
    /// (the surface HR needs to review a leaver's PENDING_REVIEW year; was 403/404 pre-S70).</summary>
    [Fact]
    public async Task YearOverview_TerminatedTarget_HrInScope_Returns200()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();

        var rsp = await HrClient(CoveringOrg).GetAsync(YearOverviewUrl(employeeId));

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(employeeId, body.GetProperty("employeeId").GetString());
    }

    /// <summary>Non-HR on a terminated target's year-overview → 403: a FOREIGN Employee via the
    /// inline own-data guard; an IN-SCOPE LocalLeader via the new validator's HROrAbove gate
    /// (the endpoint policy is EmployeeOrAbove, so the validator is the layer that bites —
    /// terminated-employee data is HR-only).</summary>
    [Fact]
    public async Task YearOverview_TerminatedTarget_NonHr_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();

        var empRsp = await ClientWith(EmployeeToken("emp_qa_yo", OrgId))
            .GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, empRsp.StatusCode);

        var leaderRsp = await ClientWith(LeaderToken("ldr_qa_yo", OrgId))
            .GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
    }

    /// <summary>HR outside the subtree → 403 on a terminated target's year-overview.</summary>
    [Fact]
    public async Task YearOverview_TerminatedTarget_HrOutOfScope_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var rsp = await HrClient(DisjointOrg).GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>ACTIVE-target parity over HTTP: an in-scope LocalLeader still reads an ACTIVE
    /// subordinate's year-overview → 200 after the R9c validator swap (defends the pre-existing
    /// leader surface — see also <c>YearOverviewTests.Auth_LeaderInScope_Returns200</c> — against
    /// any future "unconditional HROrAbove" tightening of the new validator).</summary>
    [Fact]
    public async Task YearOverview_ActiveTarget_LeaderInScope_Still200()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        var rsp = await ClientWith(LeaderToken("ldr_qa_act", OrgId)).GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>PINNED CONSEQUENCE of the R9c body-read swap (documented in the handler): a
    /// terminated employee's OWN still-valid JWT now renders their OWN year-overview → 200
    /// (the inline Employee own-data branch + the terminated-inclusive body read). Read-only
    /// own data, bounded by the JWT lifetime — re-login is impossible (GetByUsernameAsync is
    /// is_active-filtered). Contrast: every NON-allowlisted endpoint still 404s for the same
    /// token (next test). Pinned so the behavior is a recorded decision, not an accident.</summary>
    [Fact]
    public async Task YearOverview_TerminatedSelf_Returns200_PinnedR9cConsequence()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var rsp = await ClientWith(EmployeeToken(employeeId, OrgId)).GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9e — NON-allowlisted surfaces keep denying (the allowlist is the whole fix).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A terminated target via a NON-allowlisted <c>ValidateEmployeeAccessAsync</c>
    /// caller (the balance /summary read) → 403 even for in-subtree HR: the shared validator's
    /// active-only target resolution is untouched, so the leaver is "not found" to it.</summary>
    [Fact]
    public async Task NonAllowlisted_BalanceSummary_TerminatedTarget_HrInScope_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var rsp = await HrClient(CoveringOrg).GetAsync(SummaryUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>R9e terminated-self pin, endpoint half (pre-existing behavior): a terminated
    /// employee's OWN still-valid JWT passes the own-data ownership check on a NON-allowlisted
    /// endpoint (balance /summary), but the handler then 404s on the active-only
    /// <c>GetByIdAsync</c> — the resource layer, not the ownership layer, is what locks a
    /// leaver out of the general surfaces.</summary>
    [Fact]
    public async Task NonAllowlisted_BalanceSummary_TerminatedSelf_Returns404()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var rsp = await ClientWith(EmployeeToken(employeeId, OrgId)).GetAsync(SummaryUrl(employeeId));
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9c — payout-pending inactive-inclusive pin (no query change; TEST deliverable only).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The payout-pending collection's <c>users</c> join carries no <c>is_active</c>
    /// predicate (org-filtered collection endpoint, NOT validator-wired): a deactivated
    /// leaver's SETTLED+payout row still appears for in-subtree HR. Pinned per R9c so a future
    /// "tidy-up" cannot silently drop leavers from the §24 operator worklist.</summary>
    [Fact]
    public async Task PayoutPending_TerminatedEmployee_RowStillAppears()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1);

        var rsp = await HrClient(CoveringOrg).GetAsync("/api/vacation-settlements/payout-pending");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("items").EnumerateArray(),
            it => it.GetProperty("employeeId").GetString() == employeeId
                  && it.GetProperty("payoutDays").GetDecimal() == 5m);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9f — Step-5a hardening (Codex 2B, 2026-06-10):
    // (f1) per-scope HROrAbove floor for terminated targets — the primary-role
    //      gate alone admitted a mixed-role JWT via a below-HR covering scope;
    // (f2) privilege-keyed body read on the year-overview — keying the
    //      terminated-inclusive read on the validator outcome alone left a
    //      validate-while-active → read-after-deactivation TOCTOU window.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>R9f1 validator matrix: a MIXED-role actor (primary LocalHR in DISJOINT
    /// STY05 + a LocalLeader scope covering the target via MIN01) passes the primary-role
    /// gate but its only COVERING scope is below the HR floor → terminated target DENIED
    /// (pre-fix this exact shape was allowed — the escalation the floor closes). ACTIVE
    /// target with the same actor → still allowed (the floor is terminated-only; pinned
    /// leader behavior byte-identical). GLOBAL-branch floor: a GLOBAL scope whose Role is
    /// LocalLeader does NOT admit a terminated target. Positive control on the same
    /// branch: a GlobalAdmin GLOBAL scope still admits one (the floor passes HROrAbove,
    /// it does not block GLOBAL per se).</summary>
    [Fact]
    public async Task Validator_IncludingTerminated_PerScopeFloor_MixedRoleMatrix()
    {
        var terminated = await SeedTerminatedEmployeeAsync();
        var active = await SeedActiveEmployeeAsync();
        var validator = MakeValidator();

        var mixed = MixedHrLeaderActor("mix_v");
        var term = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(mixed, terminated);
        Assert.False(term.Allowed);

        var act = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(mixed, active);
        Assert.True(act.Allowed);

        // GLOBAL branch: primary role minted LocalHR via the disjoint HR scope; the GLOBAL
        // scope itself carries Role LocalLeader → below the floor → denied.
        var globalLeader = new ActorContext(
            "mix_v_g", StatsTidRoles.LocalHR, Guid.NewGuid(), DisjointOrg,
            new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, DisjointOrg, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, null, "GLOBAL"),
            });
        var glb = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(globalLeader, terminated);
        Assert.False(glb.Allowed);

        // Positive control: HROrAbove GLOBAL scope → terminated target still admitted.
        var globalAdmin = new ActorContext(
            "ga_v", StatsTidRoles.GlobalAdmin, Guid.NewGuid(), null,
            new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        var ga = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(globalAdmin, terminated);
        Assert.True(ga.Allowed);
    }

    /// <summary>R9f1 over HTTP, year-overview: the mixed-role JWT (primary LocalHR@STY05
    /// disjoint + LocalLeader@MIN01 covering the target) → terminated target → 403 — the
    /// Leader scope no longer launders terminated access. Same token, ACTIVE target →
    /// 200 (the floor is terminated-only; active-target behavior unchanged).</summary>
    [Fact]
    public async Task YearOverview_TerminatedTarget_MixedRoleJwt_Returns403()
    {
        var terminated = await SeedTerminatedEmployeeAsync();
        var active = await SeedActiveEmployeeAsync();
        var client = ClientWith(MixedHrLeaderToken("mix_yo"));

        var terminatedRsp = await client.GetAsync(YearOverviewUrl(terminated));
        Assert.Equal(HttpStatusCode.Forbidden, terminatedRsp.StatusCode);

        var activeRsp = await client.GetAsync(YearOverviewUrl(active));
        Assert.Equal(HttpStatusCode.OK, activeRsp.StatusCode);
    }

    /// <summary>R9f1 over HTTP, an HROrAbove-allowlisted WRITE surface (manual resolve):
    /// the mixed-role JWT's primary role LocalHR PASSES the HROrAbove endpoint policy —
    /// the validator's per-scope floor is the layer that must bite → 403.</summary>
    [Fact]
    public async Task Resolve_TerminatedTarget_MixedRoleJwt_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, SettledYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 0m, forfeit: 20m, version: 1);

        var rsp = await ResolveAsync(ClientWith(MixedHrLeaderToken("mix_rs")), employeeId,
            SettledYear, disposition: "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>R9f1 GLOBAL-branch floor over HTTP: a token whose GLOBAL scope carries Role
    /// LocalLeader (primary role minted LocalHR via a second, disjoint HR scope) →
    /// terminated target's year-overview → 403 — pre-fix the GLOBAL branch admitted ANY
    /// scope unconditionally.</summary>
    [Fact]
    public async Task YearOverview_TerminatedTarget_GlobalScopeBelowHrFloor_Returns403()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var rsp = await ClientWith(GlobalLeaderScopeToken("mix_glb"))
            .GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>R9f2 TOCTOU pin, REPO-layer half (kept from cycle-2 — the cycle-2 W correctly
    /// noted this pins the repository PRIMITIVES, not the handler's read-path selection; the
    /// selection itself is now pinned by
    /// <see cref="YearOverview_BodyRead_MixedRoleActor_ActiveToInactiveFlip_FailsClosed"/> on
    /// the extracted production method). Race order: (1) the terminated-inclusive validator
    /// ALLOWS an in-scope LocalLeader while the target is ACTIVE; (2) the target deactivates;
    /// (3) the filtered <c>GetByIdAsync</c> — the FIRST read the handler performs for every
    /// non-self actor — returns null while the inclusive read would have returned the row.
    /// End-state over HTTP: a fresh request by the same leader is 403 at the validator.</summary>
    [Fact]
    public async Task YearOverview_ActiveToInactiveFlip_NonHrRead_FailsClosed()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        var validator = MakeValidator();
        var repo = new UserRepository(new DbConnectionFactory(_harness.ConnectionString));

        // 1. In-scope LocalLeader validates while the target is ACTIVE → allowed.
        var preFlip = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(
            LeaderActor("ldr_toctou", OrgId), employeeId);
        Assert.True(preFlip.Allowed);

        // 2. The target deactivates BETWEEN validation and the body read.
        await DeactivateEmployeeAsync(employeeId);

        // 3. The non-HR read path fails closed; the inclusive read would have leaked.
        Assert.Null(await repo.GetByIdAsync(employeeId));
        Assert.NotNull(await repo.GetByIdIncludingTerminatedAsync(employeeId));

        // End-state HTTP assertion: the same leader, now-terminated target → 403.
        var rsp = await ClientWith(LeaderToken("ldr_toctou", OrgId))
            .GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>R9f2 cycle-3 (Codex cycle-2 B2) — the HANDLER's read-path selection, pinned on
    /// the extracted production method <see cref="BalanceEndpoints.ReadYearOverviewTargetAsync"/>
    /// (the exact code <c>MapYearOverview</c> executes for its body read; HTTP cannot
    /// interleave the deactivation between the auth gate and the body read within one request,
    /// and the sealed repo/validator types preclude interception doubles). The mixed-role actor
    /// (primary LocalHR@STY05 disjoint + LocalLeader@MIN01 covering the target) gate-validates
    /// while the target is ACTIVE → allowed (the f1 per-scope floor is inert for active
    /// targets, by design); the target flips inactive; the body read must fail CLOSED (null ⇒
    /// the handler's 404). The previous privilege-keyed shape returned the TERMINATED row here:
    /// primary role LocalHR passed <c>IsAtLeast</c> even though the actor's only COVERING scope
    /// is below the HR floor — the primary role is not org-bound. The re-validation inside the
    /// read path applies the f1 floor against CURRENT state and denies.</summary>
    [Fact]
    public async Task YearOverview_BodyRead_MixedRoleActor_ActiveToInactiveFlip_FailsClosed()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        var validator = MakeValidator();
        var repo = new UserRepository(new DbConnectionFactory(_harness.ConnectionString));
        var mixed = MixedHrLeaderActor("mix_bodyread");

        // 1. The handler's auth gate admits while the target is ACTIVE (covering Leader scope).
        var gate = await validator.ValidateEmployeeAccessIncludingTerminatedAsync(mixed, employeeId);
        Assert.True(gate.Allowed);

        // 2. Deactivation lands BETWEEN the gate and the body read.
        await DeactivateEmployeeAsync(employeeId);

        // 3. The production body read: active-only first → null → re-validation (f1 floor,
        //    CURRENT state) denies → null → the handler 404s. No terminated data served.
        var user = await BalanceEndpoints.ReadYearOverviewTargetAsync(
            repo, validator, mixed, employeeId, CancellationToken.None);
        Assert.Null(user);
    }

    /// <summary>R9f2 cycle-3 positive/negative controls on the SAME production read path:
    /// (a) an HROrAbove-covering actor reading a TERMINATED target goes filtered-read-null →
    /// re-validation ALLOWS → terminated-inclusive read returns the row (the handler's 200 —
    /// the B2-fix audience keeps working THROUGH the new shape); (b) terminated SELF
    /// short-circuits to the inclusive read (the pinned R9e consequence, no validator call);
    /// (c) a genuinely NONEXISTENT target with a fully-privileged actor → re-validation denies
    /// ("Target employee not found") → null — the SAME null/404 a below-floor actor gets for a
    /// terminated target (no terminated-vs-nonexistent oracle).</summary>
    [Fact]
    public async Task YearOverview_BodyRead_SelectionMatrix_HrSelfAndNonexistent()
    {
        var employeeId = await SeedTerminatedEmployeeAsync();
        var validator = MakeValidator();
        var repo = new UserRepository(new DbConnectionFactory(_harness.ConnectionString));

        // (a) HR in subtree, terminated target → re-validate → inclusive read returns the row.
        var hrRead = await BalanceEndpoints.ReadYearOverviewTargetAsync(
            repo, validator, HrActor("hr_bodyread", CoveringOrg), employeeId, CancellationToken.None);
        Assert.NotNull(hrRead);
        Assert.False(hrRead!.IsActive);

        // (b) Terminated self → unconditional inclusive read (self branch).
        var selfRead = await BalanceEndpoints.ReadYearOverviewTargetAsync(
            repo, validator, EmployeeActor(employeeId, OrgId), employeeId, CancellationToken.None);
        Assert.NotNull(selfRead);
        Assert.False(selfRead!.IsActive);

        // (c) Nonexistent target, in-subtree HR actor → null (the uniform 404).
        var ghost = await BalanceEndpoints.ReadYearOverviewTargetAsync(
            repo, validator, HrActor("hr_bodyread2", CoveringOrg), "no_such_user_s70_yo",
            CancellationToken.None);
        Assert.Null(ghost);
    }

    /// <summary>R9f2 cycle-3, the cycle-2 W choreography over HTTP: the mixed-role JWT reads
    /// an ACTIVE target → 200; the target deactivates; the SAME client repeats the GET → must
    /// fail closed with no terminated data. DECLARED DEVIATION from the review's "404"
    /// expectation: over two SEQUENTIAL requests the per-request AUTH GATE re-runs first and
    /// already denies this actor on the now-terminated target (f1 floor: the covering scope is
    /// only LocalLeader) → 403. The 404 fail-closed path belongs to the IN-REQUEST window
    /// (gate passed while the target was active, flip lands before the body read), which HTTP
    /// cannot deterministically interleave — that window is pinned by
    /// <see cref="YearOverview_BodyRead_MixedRoleActor_ActiveToInactiveFlip_FailsClosed"/>
    /// directly on the production read method. Either way: terminated data is never served,
    /// and this end-to-end pin would catch any future regression that drops the gate.</summary>
    [Fact]
    public async Task YearOverview_MixedRoleJwt_ActiveThenFlip_SecondRequestFailsClosed()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        var client = ClientWith(MixedHrLeaderToken("mix_flip_http"));

        var activeRsp = await client.GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.OK, activeRsp.StatusCode);

        await DeactivateEmployeeAsync(employeeId);

        var postFlip = await client.GetAsync(YearOverviewUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, postFlip.StatusCode);
        var body = await postFlip.Content.ReadAsStringAsync();
        Assert.DoesNotContain("employeeId", body); // denial envelope only — no overview payload
    }

    // ─────────────────────────────── URLs / HTTP helpers ───────────────────────────────

    private static string YearOverviewUrl(string employeeId) =>
        $"/api/balance/{employeeId}/year-overview?year={DateTime.UtcNow.Year}";

    private static string SummaryUrl(string employeeId) =>
        $"/api/balance/{employeeId}/summary?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}";

    private static async Task<HttpResponseMessage> ResolveAsync(
        HttpClient client, string employeeId, int year, string disposition, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/resolve")
        {
            Content = JsonContent.Create(new { disposition }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> ReconcilePayoutAsync(
        HttpClient client, string employeeId, int year, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/reconcile-payout")
        {
            Content = JsonContent.Create(new { }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── clients / tokens / actors ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient(string scopeOrgId) => ClientWith(HrToken("hr_s70_qa", scopeOrgId));

    private static string HrToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });
    }

    private static string EmployeeToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string LeaderToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") });
    }

    /// <summary>R9f1 escalation shape: primary role LocalHR (highest of the scopes, as the
    /// minting convention dictates) anchored in DISJOINT STY05, plus a LocalLeader scope on
    /// MIN01 that COVERS the target org STY01 — pre-fix, the HR primary role passed the gate
    /// and the Leader scope admitted the terminated target.</summary>
    private static string MixedHrLeaderToken(string actorId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: DisjointOrg,
            scopes: new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, DisjointOrg, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, CoveringOrg, "ORG_ONLY"),
            });
    }

    /// <summary>R9f1 GLOBAL-branch shape: primary role LocalHR via a disjoint HR scope; the
    /// GLOBAL scope itself carries Role LocalLeader (below the terminated-target floor).</summary>
    private static string GlobalLeaderScopeToken(string actorId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: DisjointOrg,
            scopes: new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, DisjointOrg, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, null, "GLOBAL"),
            });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    private static ActorContext HrActor(string actorId, string scopeOrgId) => new(
        actorId, StatsTidRoles.LocalHR, Guid.NewGuid(), scopeOrgId,
        new[] { new RoleScope(StatsTidRoles.LocalHR, scopeOrgId, "ORG_ONLY") });

    private static ActorContext LeaderActor(string actorId, string scopeOrgId) => new(
        actorId, StatsTidRoles.LocalLeader, Guid.NewGuid(), scopeOrgId,
        new[] { new RoleScope(StatsTidRoles.LocalLeader, scopeOrgId, "ORG_ONLY") });

    private static ActorContext EmployeeActor(string actorId, string orgId) => new(
        actorId, StatsTidRoles.Employee, Guid.NewGuid(), orgId,
        new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });

    /// <summary>Validator-layer twin of <see cref="MixedHrLeaderToken"/>.</summary>
    private static ActorContext MixedHrLeaderActor(string actorId) => new(
        actorId, StatsTidRoles.LocalHR, Guid.NewGuid(), DisjointOrg,
        new[]
        {
            new RoleScope(StatsTidRoles.LocalHR, DisjointOrg, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalLeader, CoveringOrg, "ORG_ONLY"),
        });

    private OrgScopeValidator MakeValidator()
    {
        var factory = new DbConnectionFactory(_harness.ConnectionString);
        return new OrgScopeValidator(
            new OrganizationRepository(factory),
            new UserRepository(factory),
            NullLogger<OrgScopeValidator>.Instance);
    }

    // ─────────────────────────────── seeding / reads ───────────────────────────────

    private async Task<string> SeedActiveEmployeeAsync()
    {
        var employeeId = "emp_s70_ta_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Seeds a normal employee, then deactivates the user row directly (the S70
    /// lifecycle endpoint is TASK-7002; these tests pin the ACCESS layer, which keys on
    /// <c>is_active = FALSE</c> however it came about — manual admin PUT or end-date flip).</summary>
    private async Task<string> SeedTerminatedEmployeeAsync()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        await DeactivateEmployeeAsync(employeeId);
        return employeeId;
    }

    /// <summary>Flips an existing user row to <c>is_active = FALSE</c> directly — used both by
    /// <see cref="SeedTerminatedEmployeeAsync"/> and by the R9f2 TOCTOU pin, which needs the
    /// flip to happen BETWEEN validation and the body read.</summary>
    private async Task DeactivateEmployeeAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(DateOnly? EndDate, bool Provenance, bool IsActive, long Version)>
        ReadEndDateTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_end_date, end_date_deactivated, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    /// <summary>Direct <c>vacation_settlements</c> seed with a minimal valid snapshot — mirrors
    /// <see cref="Settlement.VacationSettlementEndpointTests"/> (trigger YEAR_END; the S70
    /// TERMINATION trigger rows are TASK-7004's to test).</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string state,
        decimal transfer, decimal payout, decimal forfeit, long version)
    {
        var snapshotJson = JsonSerializer.Serialize(new
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
            transferAgreementDays = transfer,
            isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 1, @state, 'YEAR_END', @snapshot::jsonb, @transfer, @payout, @forfeit,
                 NULL, @version)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transfer);
        cmd.Parameters.AddWithValue("payout", payout);
        cmd.Parameters.AddWithValue("forfeit", forfeit);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync();
    }
}
