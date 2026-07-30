using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S91 / TASK-9102 — the deliberate, owner-approved P7 privilege change that opens the
/// "Medarbejder administration" reporting-line TREE page to LocalHR. The backend endpoints the
/// page calls were lowered from the LocalAdmin tier (<c>LocalAdminOrAbove</c> policy + a
/// <c>StatsTidRoles.LocalAdmin</c> <see cref="OrgScopeValidator"/> floor) to the HR tier
/// (<c>HROrAbove</c> policy + a <c>StatsTidRoles.LocalHR</c> floor).
///
/// <para><b>What this fixture proves, per lowered endpoint:</b></para>
/// <list type="number">
///   <item><description><b>HR-IN-SCOPE NOW SUCCEEDS</b> — a single-scope <c>LocalHR@MIN01</c>
///   (which covers <c>STY01</c>) gets a 2xx. This is the RED-ON-OLD assertion: before the lower,
///   the <c>LocalAdminOrAbove</c> policy excluded a LocalHR token (403), and even past the policy
///   the LocalAdmin floor denied. The S91 change is exactly what makes these pass.</description></item>
///   <item><description><b>CONTAINMENT PRESERVED (out-of-scope HR still 403s)</b> — the mixed-role
///   <c>HR@STY05 + Leader@MIN01</c> JWT is denied a STY01 surface. Only the ROLE floor dropped
///   (LocalAdmin → LocalHR); the org-scope containment is unchanged. The primary role LocalHR
///   clears the <c>HROrAbove</c> policy, so the floored <see cref="OrgScopeValidator"/> is the
///   layer that bites — an HR actor stays bounded to its own org subtree (the S85/S76 leak class
///   does not reopen).</description></item>
///   <item><description><b>BELOW-HR STILL 403s</b> — a <c>LocalLeader@MIN01</c> token (which
///   genuinely covers STY01) is denied at the <c>HROrAbove</c> policy layer. The page is opened to
///   HR, NOT to leaders/employees.</description></item>
/// </list>
///
/// <para>Fixture/JWT conventions mirror <see cref="MixedRoleScopeLeakTests"/>: the same WAF
/// harness + seed org tree (<c>MIN01</c> covers <c>STY01</c>; <c>STY05</c> is disjoint), the same
/// token-minting helpers, and the same <see cref="RegressionSeed"/> employee seed.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S91TreePageHrAccessTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string TargetOrg = "STY01";    // /MIN01/STY01/ — the styrelse the tree page acts over
    private const string DisjointOrg = "STY05";  // /MIN02/STY05/ — disjoint HR home (out-of-scope actor)
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)

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

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) HR-IN-SCOPE NOW SUCCEEDS — RED-ON-OLD. Each of these was a LocalAdmin-tier
    //      surface; a LocalHR@MIN01 token was 403'd before S91 (policy + floor). The lower
    //      to HROrAbove + LocalHR floor is what turns each into a 2xx.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Medarbejdere roster read (now HROrAbove / LocalHR floor): HR@MIN01 reads the STY01
    /// roster → 200. RED-on-old: the pre-S91 LocalAdminOrAbove policy excluded the LocalHR token.</summary>
    [Fact]
    public async Task MedarbejdereRoster_HrInScope_Returns200()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_med_hr"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // S94 (TASK-9406): the tree-settings (enforcement) READ + WRITE in-scope HR cases were DELETED —
    // the GET/PUT /api/admin/reporting-lines/tree/{org}/settings endpoints are retired (ADR-035 OQ6).
    // The S91 HR-access lower is still proven by the surviving roster / picker / vikar / user-create cases.

    /// <summary>Person-search picker (now HROrAbove / LocalHR floor): HR@MIN01 → 200, and the STY01
    /// user IS returned (the floored accessible-org union now contributes the MIN01 subtree at the
    /// LocalHR floor). RED-on-old: the picker was a LocalAdmin surface.</summary>
    [Fact]
    public async Task PersonSearchPicker_HrInScope_Returns200AndSeesTargetOrgUser()
    {
        var emp = await SeedTargetEmployeeAsync("s91pick");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_pick_hr"));

        var rsp = await client.GetAsync("/api/admin/users/search?q=s91pick&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();
        Assert.Contains(emp, ids); // the floored LocalHR union now covers STY01
    }

    /// <summary>Active-vikar READ (now HROrAbove / LocalHR floor): HR@MIN01 reads a STY01 manager's
    /// active vikar → 200 (null when none). The gate validates the manager's CURRENT primary org at
    /// the LocalHR floor. RED-on-old.</summary>
    [Fact]
    public async Task ActiveVikarRead_HrInScope_Returns200()
    {
        var mgr = await SeedTargetEmployeeAsync("s91vikmgr");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_vik_hr"));

        var rsp = await client.GetAsync($"/api/admin/reporting-lines/{mgr}/vikar");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>User CREATE (now HROrAbove / LocalHR floor): HR@MIN01 creates a STY01 user → 201.
    /// No approver supplied (a bare create). RED-on-old: the pre-S91 LocalAdminOrAbove policy
    /// excluded the LocalHR token.</summary>
    [Fact]
    public async Task UserCreate_HrInScope_Returns201()
    {
        var newId = "s91new_" + Guid.NewGuid().ToString("N")[..8];
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_create_hr"));

        var rsp = await client.PostAsync("/api/admin/users", JsonContent.Create(new
        {
            userId = newId,
            username = newId,
            password = "password",
            displayName = "S91 New Person",
            primaryOrgId = TargetOrg,
            agreementCode = "AC",
            okVersion = "OK24",
        }));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) CONTAINMENT PRESERVED — an out-of-scope HR actor (HR@STY05 + Leader@MIN01) is
    //      STILL 403'd on the STY01 tree page. The lower dropped only the ROLE floor, NOT the
    //      org-scope containment. The JWT's primary role LocalHR clears the HROrAbove policy,
    //      so the floored OrgScopeValidator (now at the LocalHR floor) is the decisive layer.
    //      Pre-S91 the LocalAdmin floor denied for a different reason; post-S91 the LocalHR
    //      floor must STILL deny — proving HR is bounded to its own subtree (no S85/S76 leak).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Roster read: the mixed HR@STY05 + Leader@MIN01 JWT → 403 on the STY01 roster
    /// (containment preserved — the LocalHR floor skips the below-HR Leader scope that covers
    /// STY01, and the HR scope sits in the disjoint STY05).</summary>
    [Fact]
    public async Task MedarbejdereRoster_OutOfScopeHr_Returns403()
    {
        var client = ClientWith(MixedHrLeaderToken("s91_med_oos"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/medarbejdere");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // S94 (TASK-9406): the out-of-scope tree-settings WRITE containment case was DELETED — the
    // PUT /settings endpoint is retired (ADR-035 OQ6). Containment is still proven by the surviving
    // out-of-scope roster / picker / vikar / user-create cases.

    /// <summary>Person-search picker: the mixed HR@STY05 + Leader@MIN01 JWT's floored accessible-org
    /// union must NOT include STY01, so a STY01 user does NOT appear (containment preserved — the
    /// below-HR Leader@MIN01 scope no longer widens the picker, exactly the S76 picker-leak guard).</summary>
    [Fact]
    public async Task PersonSearchPicker_OutOfScopeHr_DoesNotReturnTargetOrgUser()
    {
        var emp = await SeedTargetEmployeeAsync("s91pickoos");
        var client = ClientWith(MixedHrLeaderToken("s91_pick_oos"));

        var rsp = await client.GetAsync("/api/admin/users/search?q=s91pickoos&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();
        Assert.DoesNotContain(emp, ids); // STY01 user out of the floored LocalHR accessible set
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S124 / TASK-12401 — the picker's OPTIONAL `organisationId` narrowing. The picker
    //  scopes itself to the SUBJECT's Organisation because a cross-Organisation reporting
    //  edge is rejected server-side anyway (ADR-027 D2), so offering other orgs' people was
    //  offering guaranteed-400 choices.
    //
    //  The parameter NARROWS ONLY. It is a SEPARATE conjunct AND-ed with the RBAC
    //  accessible-org predicate, never a substitute for it — the two INTERSECT. These two
    //  tests pin both halves of that contract; the second is the escalation guard and is the
    //  reason the parameter is safe to expose at all.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>IN-SCOPE narrowing: HR@STY01 searching WITH <c>organisationId=STY01</c> still sees
    /// the STY01 user, and searching with a DIFFERENT org id sees nobody. Proves the parameter
    /// actually filters (not silently ignored) in the direction it is meant to.</summary>
    [Fact]
    public async Task PersonSearchPicker_OrganisationId_NarrowsWithinTheActorsOwnScope()
    {
        var emp = await SeedTargetEmployeeAsync("s124narrow");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s124_narrow_hr"));

        // (a) the SUBJECT's own org — the user is still returned.
        var inOrg = await client.GetAsync(
            $"/api/admin/users/search?q=s124narrow&organisationId={TargetOrg}&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, inOrg.StatusCode);
        var inBody = await inOrg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(emp, inBody.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()));

        // (b) a DIFFERENT org — the same in-scope actor now sees nobody. `total` is asserted as
        // well as `items`: the narrowing conjunct must sit in the `matched` CTE that feeds BOTH,
        // not in the `page` slice — placed in `page` the items would empty while `total` kept
        // reporting the unnarrowed count, and the picker footer would lie.
        var otherOrg = await client.GetAsync(
            $"/api/admin/users/search?q=s124narrow&organisationId={DisjointOrg}&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, otherOrg.StatusCode);
        var otherBody = await otherOrg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(otherBody.GetProperty("items").EnumerateArray());
        Assert.Equal(0, otherBody.GetProperty("total").GetInt32());
    }

    /// <summary><b>THE ESCALATION GUARD.</b> An OUT-OF-SCOPE actor (the mixed HR@STY05 +
    /// Leader@MIN01 JWT) explicitly asks for <c>organisationId=STY01</c> — an Organisation it may
    /// NOT see. It must still get NOTHING: the parameter intersects with the RBAC accessible-org
    /// set, so it can only ever shrink a result, never widen one. Were the narrowing applied
    /// INSTEAD of the RBAC predicate, this request would hand the caller STY01's roster — a
    /// privilege escalation opened by a UX convenience.
    ///
    /// <para>Also asserts <b>200-with-nothing, NOT 403</b>. Answering "you may not see that org"
    /// would make the parameter an org-existence oracle: a caller could probe arbitrary ids and
    /// distinguish "exists but forbidden" from "does not exist". An empty page is indistinguishable
    /// across "no such org", "org I cannot see", and "org with no matches" — which is the point.</para></summary>
    [Fact]
    public async Task PersonSearchPicker_OutOfScopeActor_ForeignOrganisationId_StillReturnsNothing()
    {
        var emp = await SeedTargetEmployeeAsync("s124escal");
        var client = ClientWith(MixedHrLeaderToken("s124_escal_oos"));

        var rsp = await client.GetAsync(
            $"/api/admin/users/search?q=s124escal&organisationId={TargetOrg}&limit=200&offset=0");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode); // NOT 403 — no existence oracle
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();
        Assert.DoesNotContain(emp, ids);
        Assert.Empty(ids);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    /// <summary>Active-vikar READ: the mixed HR@STY05 + Leader@MIN01 JWT → 403 on a STY01 manager's
    /// vikar (containment preserved at the LocalHR floor).</summary>
    [Fact]
    public async Task ActiveVikarRead_OutOfScopeHr_Returns403()
    {
        var mgr = await SeedTargetEmployeeAsync("s91vikoos");
        var client = ClientWith(MixedHrLeaderToken("s91_vik_oos"));

        var rsp = await client.GetAsync($"/api/admin/reporting-lines/{mgr}/vikar");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>User CREATE: the mixed HR@STY05 + Leader@MIN01 JWT → 403 creating a STY01 user
    /// (containment preserved — an out-of-scope HR cannot mint a user into a styrelse it does not
    /// cover).</summary>
    [Fact]
    public async Task UserCreate_OutOfScopeHr_Returns403()
    {
        var newId = "s91oos_" + Guid.NewGuid().ToString("N")[..8];
        var client = ClientWith(MixedHrLeaderToken("s91_create_oos"));

        var rsp = await client.PostAsync("/api/admin/users", JsonContent.Create(new
        {
            userId = newId,
            username = newId,
            password = "password",
            displayName = "S91 OOS",
            primaryOrgId = TargetOrg,
            agreementCode = "AC",
            okVersion = "OK24",
        }));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) BELOW-HR STILL 403s — a LocalLeader@MIN01 token (which GENUINELY covers STY01)
    //      is refused at the HROrAbove policy layer. The page is opened to HR, not below.
    //      A single covering-but-below-HR token suffices to pin the policy-tier boundary
    //      across the lowered surfaces (the policy is shared by every lowered endpoint).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Roster read: a LocalLeader@MIN01 (covers STY01) → 403. The HROrAbove policy admits
    /// only GlobalAdmin/LocalAdmin/LocalHR; a leader is below the floor, refused at the policy.</summary>
    [Fact]
    public async Task MedarbejdereRoster_BelowHrLeader_Returns403()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s91_med_leader"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/medarbejdere");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // S94 (TASK-9406): the below-HR tree-settings WRITE case was DELETED — the PUT /settings endpoint
    // is retired (ADR-035 OQ6). The policy-tier boundary is still pinned by the surviving below-HR
    // roster / user-create / picker cases.

    /// <summary>User CREATE: a LocalLeader@MIN01 → 403 (below the HROrAbove policy). A leader cannot
    /// create users even within their own covering scope.</summary>
    [Fact]
    public async Task UserCreate_BelowHrLeader_Returns403()
    {
        var newId = "s91led_" + Guid.NewGuid().ToString("N")[..8];
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s91_create_leader"));

        var rsp = await client.PostAsync("/api/admin/users", JsonContent.Create(new
        {
            userId = newId,
            username = newId,
            password = "password",
            displayName = "S91 Leader",
            primaryOrgId = TargetOrg,
            agreementCode = "AC",
            okVersion = "OK24",
        }));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Person-search picker: a LocalLeader@MIN01 → 403 (below the HROrAbove policy).</summary>
    [Fact]
    public async Task PersonSearchPicker_BelowHrLeader_Returns403()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s91_pick_leader"));
        var rsp = await client.GetAsync("/api/admin/users/search?q=x&limit=50&offset=0");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ─────────────────────────────── clients / tokens / seeding ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>A single-scope token anchored at <paramref name="orgId"/> (ORG_ONLY, S93 flat role-scope).</summary>
    // ════════════════════════════════════════════════════════════════════════════════
    //  S124 / TASK-12404 — WHO MAY EDIT AN EMPLOYEE'S REGISTRATIONS.
    //
    //  Owner ruling 2026-07-30: "A manager can never edit an employee's registrations. Only HR and
    //  admins can." Before this, ANY non-Employee actor whose org-scope covered the target could
    //  write another employee's time data — LocalLeader included. Two endpoints carried that shape:
    //  the skema save, and POST /api/time-entries (which has NO approval-period check at all).
    //
    //  These are RED-on-old in the denial direction: both leader writes returned 2xx before the
    //  floor. The HR-allowed and SELF-allowed cases are the guards that the narrowing did not
    //  over-reach — the self case especially, since a leader is also an employee who must be able to
    //  register their OWN time.
    // ════════════════════════════════════════════════════════════════════════════════

    private static readonly object SkemaSavePayload = new
    {
        year = 2026,
        month = 5,
        entries = Array.Empty<object>(),
        absences = Array.Empty<object>(),
        workTime = Array.Empty<object>(),
    };

    private static object TimeEntryPayload(string employeeId) => new
    {
        employeeId,
        date = "2026-05-04",
        hours = 7.4m,
        activityType = "NORMAL",
        agreementCode = "AC",
        okVersion = "OK24",
    };

    /// <summary>THE RULING, denial direction: a LocalLeader covering the target's org may NOT save
    /// another employee's month. RED-on-old (was a 2xx).</summary>
    [Fact]
    public async Task SkemaSave_LeaderOnAnotherEmployee_Is403()
    {
        var emp = await SeedTargetEmployeeAsync("s124wr_leader");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s124_wr_leader"));

        var rsp = await client.PostAsync($"/api/skema/{emp}/save", JsonContent.Create(SkemaSavePayload));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>THE RULING, allowed direction: HR may. Anything but 403 proves the floor admits HR —
    /// the save's own business validation is not what this test is about.</summary>
    [Fact]
    public async Task SkemaSave_HrOnAnotherEmployee_Is200()
    {
        var emp = await SeedTargetEmployeeAsync("s124wr_hr");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s124_wr_hr"));

        var rsp = await client.PostAsync($"/api/skema/{emp}/save", JsonContent.Create(SkemaSavePayload));
        // Step-7a Codex: assert the DOCUMENTED success, not merely "not 403" — NotEqual(Forbidden)
        // also passes on 400/404/500 and would prove nothing about the write being admitted.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>THE SELF-EXEMPTION GUARD — the most easily broken part of this change. A LocalLeader
    /// is ALSO an employee who registers their own time. They are not Employee-role, so they fall
    /// through the same scope branch the floor sits on; applying the HR floor unconditionally would
    /// have locked every leader out of their OWN timesheet.</summary>
    [Fact]
    public async Task SkemaSave_LeaderOnTheirOwnMonth_Is200()
    {
        var self = await SeedTargetEmployeeAsync("s124wr_self");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, self));

        var rsp = await client.PostAsync($"/api/skema/{self}/save", JsonContent.Create(SkemaSavePayload));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>The same ruling on the OTHER member of the write class. This endpoint is the worse
    /// one: it has no approval-period status check, so before the floor a leader could write an
    /// employee's entry in any period state. RED-on-old.</summary>
    [Fact]
    public async Task TimeEntryCreate_LeaderOnAnotherEmployee_Is403()
    {
        var emp = await SeedTargetEmployeeAsync("s124te_leader");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s124_te_leader"));

        var rsp = await client.PostAsync("/api/time-entries", JsonContent.Create(TimeEntryPayload(emp)));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task TimeEntryCreate_HrOnAnotherEmployee_Is201()
    {
        var emp = await SeedTargetEmployeeAsync("s124te_hr");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s124_te_hr"));

        var rsp = await client.PostAsync("/api/time-entries", JsonContent.Create(TimeEntryPayload(emp)));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
    }

    [Fact]
    public async Task TimeEntryCreate_LeaderOnTheirOwnEntry_Is201()
    {
        var self = await SeedTargetEmployeeAsync("s124te_self");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, self));

        var rsp = await client.PostAsync("/api/time-entries", JsonContent.Create(TimeEntryPayload(self)));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
    }

    /// <summary>The TASK-12404 WRITE narrowing did not bleed into READS: HR still reads the month (its
    /// corrective tier is deliberately not month-gated).
    ///
    /// <para>This uses HR, not a leader, ON PURPOSE. An earlier version asserted a LEADER could read a
    /// month with NO approval period — which was the P7 leak Step-7a Codex caught, not a guarantee
    /// worth pinning. The leader read is month-gated; that behaviour is covered by the TASK-12405
    /// cases in <c>AllocationBreakdownEndpointTests</c> (200 once SENT, 403 while DRAFT/absent).</para></summary>
    [Fact]
    public async Task SkemaMonthRead_HrStillAllowed_TheWriteNarrowingDidNotBleedIntoReads()
    {
        var emp = await SeedTargetEmployeeAsync("s124rd_hr");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s124_rd_hr"));

        var rsp = await client.GetAsync($"/api/skema/{emp}/month?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    private static string AdminToken(string role, string orgId, string actorId)
    {
        var svc = NewTokenService();
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
    }

    /// <summary>The out-of-scope escalation shape: primary role LocalHR anchored in the DISJOINT
    /// STY05 (so the HR scope does NOT cover STY01), plus a below-HR LocalLeader scope on MIN01 that
    /// DOES cover STY01. The JWT's primary role clears the HROrAbove policy; the LocalHR-floored
    /// validator must skip the Leader scope and deny — containment preserved.</summary>
    private static string MixedHrLeaderToken(string actorId)
    {
        var svc = NewTokenService();
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: DisjointOrg,
            scopes: new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, DisjointOrg, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, CoveringOrg, "ORG_ONLY"),
            });
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private async Task<string> SeedTargetEmployeeAsync(string? prefix = null)
    {
        var employeeId = (prefix ?? "s91emp") + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, TargetOrg, "AC", "OK24", ensureOrg: false);
        return employeeId;
    }
}
