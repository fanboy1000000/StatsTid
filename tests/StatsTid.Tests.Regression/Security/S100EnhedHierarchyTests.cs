using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S100 / TASK-10005 / ADR-036 (hierarchy amendment) — the RED-on-old regression suite for the
/// HIERARCHICAL Enhed (a <c>enheder.parent_enhed_id</c> tree WITHIN each Organisation, with the
/// <c>level</c> derived as depth). The hierarchy is PURE DISPLAY metadata with <b>ZERO authority /
/// scope / approval meaning</b> (the ADR-036 invariant, unchanged — the Organisation is the only
/// authority unit). This fixture pins, end-to-end against the real Backend.Api:
///
/// <list type="number">
///   <item><b>The cycle guard (RED-on-old)</b> — Systemer > Drift > Vagt; move Systemer UNDER Vagt
///     (its own descendant) → 422; move Systemer under ITSELF → 422; a valid re-parent (Vagt → a
///     sibling of Systemer's child) → 200.</item>
///   <item><b>The held-lock interleave (the concurrency spine)</b> — hold the
///     <c>enhed-org-{org}</c> advisory on a side connection (reconstruct
///     <c>hashtext('enhed-org-' || org)</c>), fire a move, assert it BLOCKS on the held lock (a
///     <c>pg_locks</c>⋈<c>pg_stat_activity</c> waiter barrier, like S95/S97); release → it proceeds.
///     Proves the move serializes on the per-Organisation lock.</item>
///   <item><b>Delete re-parents children up</b> — Systemer > {Drift, Net}; delete Systemer → Drift +
///     Net become ROOTS (parent NULL), SURVIVE active, a per-child <c>EnhedMoved</c> emitted; a
///     NON-root delete (A > B > C; delete B) → C re-parents to A (the grandparent); a LEAF delete →
///     only <c>EnhedDeleted</c>, no <c>EnhedMoved</c>; the deleted enhed's <c>user_enheder</c> rows
///     STAY (S97 unchanged).</item>
///   <item><b>The move If-Match</b> — a stale-version move → 412; a move on a soft-deleted enhed →
///     404.</item>
///   <item><b>Create-under-parent</b> — under an active same-org parent → 201 (parentEnhedId set);
///     under a DIFFERENT-Organisation parent → 422; under a soft-deleted parent → 422.</item>
///   <item><b>The derived level</b> — a 3-deep chain; <c>GET /tree</c> returns level 1/2/3 + the
///     nesting.</item>
///   <item><b>P7 — the hierarchy grants NO authority (the #1 RED)</b> — A tagged to a PARENT enhed,
///     B tagged to its CHILD enhed (A an ANCESTOR of B's enhed): A canNOT
///     <c>IsEffectiveDesignatedApprover</c> B AND <c>ValidateEmployeeAccessAsync(A, B, …)</c> DENIES
///     (absent a reporting edge / HR scope). A shared ancestor/descendant grants NOTHING.</item>
/// </list>
///
/// <para><b>Topology (init.sql seed orgs, S92/ADR-035 flatten):</b> MIN01/MIN02 = MAO; STY01/STY02/STY03
/// under MIN01, STY04/STY05 under MIN02 = ORGANISATION. The enhed tree is wholly within ONE
/// Organisation (STY01 here). hr-over-STY01 covers STY01 (exact ORG_ONLY, S93 flat role-scope) and is
/// DISJOINT from STY04. All enheder + users are fresh. Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; mirrors <see cref="S97EnhedTests"/> (token minting +
/// the enhed CRUD helpers) and <see cref="StatsTid.Tests.Regression.ReportingLine.S95FlatOrgLockTests"/>
/// (the side-conn advisory-lock interleave harness, re-keyed to the <c>enhed-org-</c> prefix).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S100EnhedHierarchyTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Sty01 = "STY01"; // ORGANISATION (under MAO MIN01) — the enhed-tree home
    private const string Sty04 = "STY04"; // ORGANISATION (under MAO MIN02) — a DIFFERENT Organisation
    private const string Min01 = "MIN01"; // MAO

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (org tree MIN01/MIN02 + STY0x + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) The cycle guard (RED-on-old) — move-under-descendant / move-under-self → 422.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(1) Build Systemer(root) > Drift(child) > Vagt(grandchild). Moving Systemer UNDER
    /// Vagt (its own descendant) → 422 (the descendant-cycle CTE fires under the per-Organisation
    /// advisory lock); moving Systemer under ITSELF → 422 (the self-cycle short-circuit). A VALID
    /// re-parent (Vagt → a fresh sibling of Drift) → 200 (no cycle). RED-on-old: pre-S100 there is
    /// no move endpoint / cycle guard at all.</summary>
    [Fact]
    public async Task MoveEnhed_UnderOwnDescendant_OrSelf_Returns422_ValidReparent200()
    {
        var admin = GlobalAdminClient();
        var systemer = await CreateEnhedAsync(admin, Sty01, "S100 Systemer", parent: null);
        var drift = await CreateEnhedAsync(admin, Sty01, "S100 Drift", parent: systemer);
        var vagt = await CreateEnhedAsync(admin, Sty01, "S100 Vagt", parent: drift);

        // Systemer is at version 1 (a root). Moving it under its grandchild Vagt → a descendant
        // cycle → 422 (and NO change committed).
        var underDescendant = await MoveEnhedAsync(admin, systemer, newParent: vagt, ifMatch: 1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, underDescendant.StatusCode);

        // Moving Systemer under ITSELF → 422 (self-cycle).
        var underSelf = await MoveEnhedAsync(admin, systemer, newParent: systemer, ifMatch: 1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, underSelf.StatusCode);

        // Systemer is unchanged — still a root (parent NULL), still at version 1.
        Assert.Null(await SelectParentAsync(systemer));
        Assert.Equal(1, await SelectVersionAsync(systemer));

        // A VALID re-parent: move Vagt (grandchild, version 1) up to be a child of Systemer (a
        // sibling of Drift) → 200, no cycle (Systemer is an ANCESTOR of Vagt, never a descendant).
        var valid = await MoveEnhedAsync(admin, vagt, newParent: systemer, ifMatch: 1);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(systemer, await SelectParentAsync(vagt));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) The held-lock interleave (the concurrency spine) — the move serializes on the
    //      per-Organisation enhed-org-{org} advisory.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(2) HOLD the <c>enhed-org-{STY01}</c> advisory on a side connection (the same key the
    /// move endpoint derives from the enhed's Organisation), then fire a move and assert it BLOCKS on
    /// the held lock (a <c>pg_locks</c>⋈<c>pg_stat_activity</c> waiter barrier). Release → the parked
    /// move proceeds (200). Proves every enhed-tree move serializes on the single per-Organisation
    /// lock — the concurrency spine; two reciprocal moves can never both commit a cycle.</summary>
    [Fact]
    public async Task MoveEnhed_SerializesOnHeldOrganisationAdvisory_BlocksThenProceeds()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S100 Lock Root", parent: null);
        var leaf = await CreateEnhedAsync(admin, Sty01, "S100 Lock Leaf", parent: null);

        // 1. HOLD the STY01 enhed-org advisory key on a side connection.
        var (holdConn, holdTx) = await AcquireEnhedOrgLockOnSideConnAsync(Sty01);
        var lockReleased = false;
        try
        {
            // 2. Fire a move (leaf → under root). It must REACH and BLOCK on the held advisory key
            //    (the endpoint acquires enhed-org-{STY01} FIRST, before the cycle CTE).
            var moveTask = MoveEnhedAsync(admin, leaf, newParent: root, ifMatch: 1);

            // 3a. STRICT BARRIER: poll pg_locks until the endpoint backend is actually WAITING on
            //     OUR enhed-org advisory key — proves it REACHED + BLOCKED on the lock, not merely
            //     that it is slow.
            Assert.True(await WaitForEnhedOrgLockWaiterAsync(Sty01),
                "No backend was observed WAITING on the STY01 enhed-org advisory — the move did not serialize on the per-Organisation lock.");

            // 3b. PROOF OF BLOCKING: parked while we hold the key.
            Assert.False(await Task.WhenAny(moveTask, Task.Delay(500)) == moveTask,
                "The move completed while the enhed-org advisory was held — it did not serialize on the lock.");

            // 4. Release the held lock → the parked move acquires it and proceeds.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            lockReleased = true;

            var rsp = await moveTask;
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.Equal(root, await SelectParentAsync(leaf));
        }
        finally
        {
            if (!lockReleased)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Delete re-parents children up — per-child EnhedMoved; root → roots;
    //      non-root → grandparent; leaf → only EnhedDeleted; user_enheder rows STAY.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(3a) Deleting a ROOT enhed (Systemer) with children {Drift, Net} → both children
    /// become ROOTS (parent NULL), SURVIVE (active), Systemer is soft-deleted, and a per-child
    /// <c>EnhedMoved</c> is emitted for EACH (NOT a silent SQL update). The deleted Systemer's
    /// <c>user_enheder</c> rows STAY physically (S97 projection-filter, unchanged).</summary>
    [Fact]
    public async Task DeleteRootEnhed_ChildrenBecomeRoots_SurviveActive_PerChildEnhedMoved_TagsStay()
    {
        var admin = GlobalAdminClient();
        var systemer = await CreateEnhedAsync(admin, Sty01, "S100 Del Root Systemer", parent: null);
        var drift = await CreateEnhedAsync(admin, Sty01, "S100 Del Root Drift", parent: systemer);
        var net = await CreateEnhedAsync(admin, Sty01, "S100 Del Root Net", parent: systemer);

        // Tag a user with the to-be-deleted root, to prove the membership row survives the delete.
        var user = await SeedUserAsync("s100_delroot_user", Sty01);
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(admin, user, systemer)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(user));

        var movedBeforeDrift = await CountEnhedEventsAsync(drift, "EnhedMoved");
        var movedBeforeNet = await CountEnhedEventsAsync(net, "EnhedMoved");

        var del = await DeleteEnhedAsync(admin, systemer, ifMatch: 1);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Systemer soft-deleted; both children SURVIVE active and become ROOTS (parent NULL).
        Assert.True(await IsDeletedAsync(systemer));
        Assert.False(await IsDeletedAsync(drift));
        Assert.False(await IsDeletedAsync(net));
        Assert.Null(await SelectParentAsync(drift));
        Assert.Null(await SelectParentAsync(net));

        // A per-child EnhedMoved was emitted (NOT a silent SQL update — P3).
        Assert.Equal(movedBeforeDrift + 1, await CountEnhedEventsAsync(drift, "EnhedMoved"));
        Assert.Equal(movedBeforeNet + 1, await CountEnhedEventsAsync(net, "EnhedMoved"));

        // The deleted enhed's user_enheder rows STAY (S97 unchanged — projection-filtered at read).
        Assert.Equal(1, await CountUserEnhederAsync(user));
    }

    /// <summary>(3b) Deleting a NON-root enhed (B in A > B > C) → C re-parents to A (the grandparent,
    /// B's own parent), SURVIVES, and a single <c>EnhedMoved</c> is emitted for C.</summary>
    [Fact]
    public async Task DeleteNonRootEnhed_ChildReparentsToGrandparent_PerChildEnhedMoved()
    {
        var admin = GlobalAdminClient();
        var a = await CreateEnhedAsync(admin, Sty01, "S100 Del A", parent: null);
        var b = await CreateEnhedAsync(admin, Sty01, "S100 Del B", parent: a);
        var c = await CreateEnhedAsync(admin, Sty01, "S100 Del C", parent: b);

        var movedBeforeC = await CountEnhedEventsAsync(c, "EnhedMoved");

        // B is at version 1 (created as a child, never re-parented).
        var del = await DeleteEnhedAsync(admin, b, ifMatch: 1);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // B soft-deleted; C re-parents UP to A (the grandparent), still active.
        Assert.True(await IsDeletedAsync(b));
        Assert.False(await IsDeletedAsync(c));
        Assert.Equal(a, await SelectParentAsync(c));
        Assert.Equal(movedBeforeC + 1, await CountEnhedEventsAsync(c, "EnhedMoved"));
    }

    /// <summary>(3c) Deleting a LEAF enhed (no children) → only <c>EnhedDeleted</c>, NO
    /// <c>EnhedMoved</c> (no re-parent fan-out).</summary>
    [Fact]
    public async Task DeleteLeafEnhed_OnlyEnhedDeleted_NoEnhedMoved()
    {
        var admin = GlobalAdminClient();
        var leaf = await CreateEnhedAsync(admin, Sty01, "S100 Del Leaf", parent: null);

        var del = await DeleteEnhedAsync(admin, leaf, ifMatch: 1);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        Assert.True(await IsDeletedAsync(leaf));
        Assert.Equal(1, await CountEnhedEventsAsync(leaf, "EnhedDeleted"));
        // No re-parent → no EnhedMoved on the leaf's own stream.
        Assert.Equal(0, await CountEnhedEventsAsync(leaf, "EnhedMoved"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) The move If-Match — stale version → 412; move on a soft-deleted enhed → 404.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(4a) A move with a STALE (too-low) version → 412. The enhed is at version 2 (one
    /// prior rename); an <c>If-Match:"1"</c> fails the precondition → 412 (re-read reports the actual
    /// version 2).</summary>
    [Fact]
    public async Task MoveEnhed_StaleVersion_Returns412()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S100 Move Stale Root", parent: null);
        var child = await CreateEnhedAsync(admin, Sty01, "S100 Move Stale Child", parent: null);

        // Bump `child` to version 2 via a rename.
        Assert.Equal(HttpStatusCode.OK,
            (await PutEnhedRenameAsync(admin, child, "S100 Move Stale Child v2", ifMatch: 1)).StatusCode);

        var stale = await MoveEnhedAsync(admin, child, newParent: root, ifMatch: 1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        // No move landed.
        Assert.Null(await SelectParentAsync(child));
    }

    /// <summary>(4b) A move on a SOFT-DELETED enhed → 404. The enhed is soft-deleted (now version 2);
    /// a move targeting it (at any version) is NotFound (the pre-read sees <c>IsDeleted</c>).</summary>
    [Fact]
    public async Task MoveEnhed_SoftDeletedEnhed_Returns404()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S100 Move Deleted Root", parent: null);
        var victim = await CreateEnhedAsync(admin, Sty01, "S100 Move Deleted Victim", parent: null);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteEnhedAsync(admin, victim, ifMatch: 1)).StatusCode);

        // The victim is soft-deleted (now version 2) — a move on it → 404.
        var rsp = await MoveEnhedAsync(admin, victim, newParent: root, ifMatch: 2);
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) Create-under-parent — same-org parent 201; cross-org parent 422;
    //      soft-deleted parent 422.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(5a) Creating a child under an ACTIVE same-Organisation parent → 201, with the
    /// response carrying the <c>parentEnhedId</c> + the row persisting it.</summary>
    [Fact]
    public async Task CreateEnhed_UnderActiveSameOrgParent_Returns201_ParentSet()
    {
        var admin = GlobalAdminClient();
        var parent = await CreateEnhedAsync(admin, Sty01, "S100 Create Parent", parent: null);

        var rsp = await PostEnhedAsync(admin, Sty01, "S100 Create Child", parent: parent);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var childId = Guid.Parse(body.GetProperty("enhedId").GetString()!);
        Assert.Equal(parent, Guid.Parse(body.GetProperty("parentEnhedId").GetString()!));

        // The row persisted the parent edge.
        Assert.Equal(parent, await SelectParentAsync(childId));
    }

    /// <summary>(5b) Creating a child under a parent in a DIFFERENT Organisation → 422. The parent
    /// (on STY04) is active but cross-Organisation; the same-Organisation invariant rejects it.</summary>
    [Fact]
    public async Task CreateEnhed_UnderCrossOrgParent_Returns422()
    {
        var admin = GlobalAdminClient();
        // The parent lives on a DIFFERENT Organisation (STY04).
        var foreignParent = await CreateEnhedAsync(admin, Sty04, "S100 Foreign Parent", parent: null);

        // Try to create a child IN STY01 under the STY04 parent → 422.
        var rsp = await PostEnhedAsync(admin, Sty01, "S100 XOrg Child", parent: foreignParent);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>(5c) Creating a child under a SOFT-DELETED parent → 422 (the in-tx active-parent
    /// re-read returns null for a deleted parent).</summary>
    [Fact]
    public async Task CreateEnhed_UnderSoftDeletedParent_Returns422()
    {
        var admin = GlobalAdminClient();
        var parent = await CreateEnhedAsync(admin, Sty01, "S100 Dead Parent", parent: null);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteEnhedAsync(admin, parent, ifMatch: 1)).StatusCode);

        var rsp = await PostEnhedAsync(admin, Sty01, "S100 Orphan Child", parent: parent);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) The derived level — a 3-deep chain; GET /tree returns level 1/2/3 + nesting.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(6) Build a 3-deep enhed chain (Top > Mid > Bottom) in STY01. <c>GET
    /// /api/admin/organizations/tree</c> nests them under the STY01 Organisation with the
    /// <c>level</c> derived as depth: Top=1, Mid=2, Bottom=3, and each parent carries its child in
    /// the nested <c>children</c> array.</summary>
    [Fact]
    public async Task GetTree_ThreeDeepChain_DerivesLevel123_AndNests()
    {
        var admin = GlobalAdminClient();
        var top = await CreateEnhedAsync(admin, Sty01, "S100 Level Top", parent: null);
        var mid = await CreateEnhedAsync(admin, Sty01, "S100 Level Mid", parent: top);
        var bottom = await CreateEnhedAsync(admin, Sty01, "S100 Level Bottom", parent: mid);

        var rsp = await admin.GetAsync("/api/admin/organizations/tree");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tree = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // Locate the STY01 Organisation's enhed forest (MAO → organisations[] → enheder[]).
        var sty01Enheder = FindOrgEnheder(tree, Sty01);

        // The Top root (level 1) carries Mid (level 2) which carries Bottom (level 3).
        var topNode = FindEnhedNode(sty01Enheder, top);
        Assert.NotNull(topNode);
        Assert.Equal(1, topNode!.Value.GetProperty("level").GetInt32());

        var midNode = FindEnhedNode(topNode.Value.GetProperty("children"), mid);
        Assert.NotNull(midNode);
        Assert.Equal(2, midNode!.Value.GetProperty("level").GetInt32());

        var bottomNode = FindEnhedNode(midNode.Value.GetProperty("children"), bottom);
        Assert.NotNull(bottomNode);
        Assert.Equal(3, bottomNode!.Value.GetProperty("level").GetInt32());
    }

    /// <summary>(6b) The FLAT-list contract (distinct from the GET /tree forest above, and the guard
    /// that would have caught the S100 Step-7a dropped-field bug — the S99 <c>fetchEnheder</c> lesson):
    /// build a root enhed and a CHILD under it in STY01, then <c>GET
    /// /api/admin/enheder?organisationId=STY01</c> with a LocalHR-over-STY01 token. The returned
    /// <c>{ enheder: [...] }</c> rows MUST carry both <c>parentEnhedId</c> AND the server-derived
    /// <c>level</c> (depth, root = 1): the root → <c>parentEnhedId == null</c> + <c>level == 1</c>; the
    /// child → <c>parentEnhedId == &lt;root id&gt;</c> + <c>level == 2</c>. RED-on-old: pre-fix the
    /// endpoint dropped both fields, so the FE EnhederPanel saw every enhed as a root.</summary>
    [Fact]
    public async Task GetEnhederFlatList_CarriesParentEnhedId_AndDerivedLevel()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S100 Flat Root", parent: null);
        var child = await CreateEnhedAsync(admin, Sty01, "S100 Flat Child", parent: root);

        var hr = HrClient("s100_flat_hr", Sty01);
        var rsp = await hr.GetAsync($"/api/admin/enheder?organisationId={Sty01}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var enheder = body.GetProperty("enheder");

        var rootRow = FindEnhedNode(enheder, root);
        Assert.NotNull(rootRow);
        Assert.Equal(JsonValueKind.Null, rootRow!.Value.GetProperty("parentEnhedId").ValueKind);
        Assert.Equal(1, rootRow.Value.GetProperty("level").GetInt32());

        var childRow = FindEnhedNode(enheder, child);
        Assert.NotNull(childRow);
        Assert.Equal(root, Guid.Parse(childRow!.Value.GetProperty("parentEnhedId").GetString()!));
        Assert.Equal(2, childRow.Value.GetProperty("level").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (7) P7 — the hierarchy grants NO authority (the #1 RED).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(7) A tagged to a PARENT (ancestor) enhed, B tagged to its CHILD (descendant) enhed,
    /// both on STY01. A holds NO reporting edge over B and NO HR/Admin scope. Then:
    /// <list type="bullet">
    ///   <item><c>IsEffectiveDesignatedApproverAsync(A, B)</c> is FALSE — the parent/child enhed link
    ///         is not a reporting edge;</item>
    ///   <item>the org-scope validator DENIES A (carrying only an EMPLOYEE scope over STY01) access
    ///         to B at the LocalHR floor — the ancestor enhed contributes nothing to
    ///         <c>OrgScopeValidator</c>.</item>
    /// </list>
    /// A shared ancestor/descendant grants NOTHING — <c>parent_enhed_id</c> is absent from every
    /// authority path (ADR-036 zero-authority invariant, preserved under the hierarchy).</summary>
    [Fact]
    public async Task SharedAncestorDescendantEnhed_GrantsNoAuthority_NoEdge_NoScope()
    {
        var admin = GlobalAdminClient();
        var a = await SeedUserAsync("s100_p7_a", Sty01);
        var b = await SeedUserAsync("s100_p7_b", Sty01);

        // A parent (ancestor) enhed and its child (descendant) enhed.
        var parentEnhed = await CreateEnhedAsync(admin, Sty01, "S100 P7 Parent", parent: null);
        var childEnhed = await CreateEnhedAsync(admin, Sty01, "S100 P7 Child", parent: parentEnhed);

        // A → the ANCESTOR enhed; B → the DESCENDANT enhed (A's enhed is an ancestor of B's).
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(admin, a, parentEnhed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(admin, b, childEnhed)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(a));
        Assert.Equal(1, await CountUserEnhederAsync(b));

        // (i) The ancestor/descendant enhed link is NOT a reporting edge — A is not B's effective
        //     designated approver.
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(a, b));

        // (ii) The ancestor enhed does NOT contribute org-scope authority. A carries only an EMPLOYEE
        //      scope over STY01 (NOT HR/Admin); the floored validator must DENY A access to B.
        var validator = ResolveOrgScopeValidator();
        var actorA = ActorWithScopes(a, Sty01, new RoleScope(StatsTidRoles.Employee, Sty01, "ORG_ONLY"));
        var (allowed, _) = await validator.ValidateEmployeeAccessAsync(actorA, b, StatsTidRoles.LocalHR);
        Assert.False(allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var svc = NewTokenService();
        var token = svc.GenerateToken(
            employeeId: "s100_gadmin", name: "s100_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Min01,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A single-scope LocalHR client anchored at <paramref name="orgId"/> (ORG_ONLY, S93 flat
    /// role-scope) — covers exactly that Organisation, disjoint from others.</summary>
    private HttpClient HrClient(string actorId, string orgId)
    {
        var client = _factory.CreateClient();
        var svc = NewTokenService();
        var token = svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static ActorContext ActorWithScopes(string actorId, string orgId, params RoleScope[] scopes)
        => new(actorId, scopes.Length > 0 ? scopes[0].Role : StatsTidRoles.Employee, Guid.NewGuid(), orgId, scopes);

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private OrgScopeValidator ResolveOrgScopeValidator()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OrgScopeValidator>();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — enhed CRUD (HTTP) — create-under-parent / move / rename / delete / set-tags
    // ════════════════════════════════════════════════════════════════════════════════

    private static async Task<HttpResponseMessage> PostEnhedAsync(
        HttpClient client, string orgId, string name, Guid? parent)
        => await client.PostAsJsonAsync("/api/admin/enheder",
            new { organisationId = orgId, name, parentEnhedId = parent });

    /// <summary>POSTs an enhed (optionally under <paramref name="parent"/>) and returns its id,
    /// asserting 201.</summary>
    private async Task<Guid> CreateEnhedAsync(HttpClient client, string orgId, string name, Guid? parent)
    {
        var rsp = await PostEnhedAsync(client, orgId, name, parent);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("enhedId").GetString()!);
    }

    /// <summary>PUT /api/admin/enheder/{id}/move { newParentEnhedId } with the If-Match precondition.
    /// <paramref name="newParent"/> null = make the enhed a root.</summary>
    private static async Task<HttpResponseMessage> MoveEnhedAsync(
        HttpClient client, Guid enhedId, Guid? newParent, long ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/enheder/{enhedId}/move")
        {
            Content = JsonContent.Create(new { newParentEnhedId = newParent }),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ifMatch}\""));
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PutEnhedRenameAsync(
        HttpClient client, Guid enhedId, string name, long ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/enheder/{enhedId}")
        {
            Content = JsonContent.Create(new { name }),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ifMatch}\""));
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> DeleteEnhedAsync(HttpClient client, Guid enhedId, long ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/enheder/{enhedId}");
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ifMatch}\""));
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> SetTagsAsync(HttpClient client, string userId, params Guid[] enhedIds)
        => await client.PutAsJsonAsync($"/api/admin/users/{userId}/enheder", new { enhedIds });

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — GET /tree JSON navigation
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Finds the enhed forest (the <c>enheder</c> array) for the Organisation
    /// <paramref name="orgId"/> inside the <c>GET /tree</c> response (<c>{ tree: [ MAO →
    /// organisations[] → enheder[] ] }</c>).</summary>
    private static JsonElement FindOrgEnheder(JsonElement response, string orgId)
    {
        var tree = response.GetProperty("tree");
        foreach (var mao in tree.EnumerateArray())
        {
            if (!mao.TryGetProperty("organisations", out var orgs))
                continue;
            foreach (var org in orgs.EnumerateArray())
            {
                if (string.Equals(org.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                    return org.GetProperty("enheder");
            }
        }
        throw new Xunit.Sdk.XunitException($"Organisation '{orgId}' not found in the GET /tree forest.");
    }

    /// <summary>Returns the enhed node with id <paramref name="enhedId"/> from a (single-level)
    /// <c>enheder</c>/<c>children</c> array, or <c>null</c> if absent.</summary>
    private static JsonElement? FindEnhedNode(JsonElement enhederArray, Guid enhedId)
    {
        foreach (var node in enhederArray.EnumerateArray())
        {
            if (Guid.TryParse(node.GetProperty("enhedId").GetString(), out var id) && id == enhedId)
                return node;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — DB reads / seeding
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<Guid?> SelectParentAsync(Guid enhedId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT parent_enhed_id FROM enheder WHERE enhed_id = @id", conn);
        cmd.Parameters.AddWithValue("id", enhedId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task<long> SelectVersionAsync(Guid enhedId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM enheder WHERE enhed_id = @id", conn);
        cmd.Parameters.AddWithValue("id", enhedId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> IsDeletedAsync(Guid enhedId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT deleted_at IS NOT NULL FROM enheder WHERE enhed_id = @id", conn);
        cmd.Parameters.AddWithValue("id", enhedId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountUserEnhederAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM user_enheder WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Counts outbox events of <paramref name="eventType"/> on the <c>enhed-{enhedId}</c>
    /// stream (used to prove a per-child EnhedMoved IS / ISN'T emitted on a delete-reparent).</summary>
    private async Task<int> CountEnhedEventsAsync(Guid enhedId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = @t AND stream_id = @s", conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("s", $"enhed-{enhedId}");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Seeds a fresh user (users + employee_profiles + user_agreement_codes) on
    /// <paramref name="orgId"/>; the org FK parent is the existing init.sql ORGANISATION.</summary>
    private async Task<string> SeedUserAsync(string baseId, string orgId)
    {
        var userId = baseId + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, userId, orgId, "AC", "OK24", ensureOrg: false);
        return userId;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Held-lock interleave harness — the enhed-org-{org} advisory (mirrors S95's reporting-org-).
    //  The enhed tree is wholly within one Organisation, so the lock key is the Organisation id.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Holds the <c>enhed-org-{org}</c> xact advisory key (the per-Organisation enhed lock)
    /// on a SIDE connection so a test can deterministically BLOCK any enhed-tree mutator that takes
    /// the same key, until the returned tx is rolled back. The caller owns disposal.</summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireEnhedOrgLockOnSideConnAsync(string organisationId)
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('enhed-org-' || @org))", conn, tx))
        {
            cmd.Parameters.AddWithValue("org", organisationId);
            await cmd.ExecuteScalarAsync();
        }
        return (conn, tx);
    }

    /// <summary>Polls <c>pg_locks</c> (joined to <c>pg_stat_activity</c> to exclude this session)
    /// until at least one OTHER backend is WAITING (<c>granted = false</c>) on the
    /// <c>enhed-org-{org}</c> advisory key — proving an enhed-tree mutator actually REACHED and
    /// BLOCKED ON THE LOCK. Returns <c>true</c> once a waiter is seen; <c>false</c> on timeout.</summary>
    private async Task<bool> WaitForEnhedOrgLockWaiterAsync(string organisationId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks pl
                    JOIN pg_stat_activity sa ON sa.pid = pl.pid
                    WHERE pl.locktype = 'advisory'
                      AND pl.granted = FALSE
                      AND pl.pid <> pg_backend_pid()
                      AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                          = hashtext('enhed-org-' || @org)::bigint
                )
                """, conn))
            {
                cmd.Parameters.AddWithValue("org", organisationId);
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
