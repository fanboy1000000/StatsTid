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
/// S97 / TASK-9707 / ADR-035 — the RED-on-old regression suite for the structured Enhed
/// feature (the <c>enheder</c> entity table + the <c>user_enheder</c> multi-tag link replacing
/// the free-text <c>employee_profiles.enhed_label</c>). An Enhed is PURE DISPLAY metadata with
/// <b>ZERO authority / scope / approval meaning</b> — the Organisation is the only authority unit
/// (ADR-035). This fixture pins, end-to-end against the real Backend.Api:
///
/// <list type="number">
///   <item><b>P7 NO-AUTHORITY (the #1 guarantee)</b> — (a) a cross-Organisation tag is rejected
///     (400, no row); (b) two users sharing ONE enhed grant each other NO authority
///     (<c>IsEffectiveDesignatedApprover</c> false + <c>ValidateEmployeeAccessAsync</c> denies
///     absent an edge / HR scope).</item>
///   <item><b>Transfer-clears-tags + spurious-clear guard (RED-on-old)</b> — a primary_org change
///     clears <c>user_enheder</c> + emits <c>UserEnhederChanged(empty)</c>; a NON-transfer edit
///     (display-name) does NOT touch the tags.</item>
///   <item><b>Set-tags TOCTOU</b> — set-tags vs a concurrent transfer serializes on the user-row
///     <c>FOR UPDATE</c>; no cross-Organisation row survives.</item>
///   <item><b>Create-MAO guard (RED-on-old)</b> — POST under a MAO → 400; under an ORGANISATION → 201.</item>
///   <item><b>Dedup</b> — active-name dup create / rename → 409; delete-then-recreate same name → 201.</item>
///   <item><b>Soft-delete projection-filter</b> — a deleted enhed leaves <c>user_enheder</c> physically
///     intact (no fan-out), the roster/search display falls back, and the enhed is unpickable in set-tags (400).</item>
///   <item><b>Org-scope containment (RED-on-old)</b> — an HR scoped to STY01 cannot GET/POST/tag in STY04 (403);
///     in-scope → 200/201.</item>
///   <item><b>Scope-leak (P7)</b> — the <c>?enhedId=</c> search is org-bounded: a same-name enhed in another
///     org cannot bleed cross-org users in.</item>
///   <item><b>Multi-tag</b> — a user with 2 enheder; the roster/search returns BOTH (joined display).</item>
/// </list>
///
/// <para>The migration "no user loses metadata" + idempotency D-test lives in the sibling
/// <see cref="S97EnhedBackfillSeederTests"/> (it seeds its own labeled profile because the CI greenfield
/// baseline is all-NULL <c>enhed_label</c> by design — Step-0b BLOCKER A).</para>
///
/// <para><b>Topology (init.sql seed orgs, S92/ADR-035 flatten):</b> MIN01/MIN02 = MAO; STY01/STY02/STY03
/// under MIN01, STY04/STY05 under MIN02 = ORGANISATION. hr-over-STY01 covers STY01 (exact ORG_ONLY, S93
/// flat role-scope) and is DISJOINT from STY04. All test users + enheder are fresh (distinct from seed).
/// Endpoint-level via <see cref="StatsTidWebApplicationFactory"/>; mirrors <see cref="S91TreePageHrAccessTests"/>
/// (token minting + org-scope floor) and <see cref="StatsTid.Tests.Regression.ReportingLine.S95FlatOrgLockTests"/>
/// (the side-conn FOR-UPDATE interleave harness).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S97EnhedTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Sty01 = "STY01"; // ORGANISATION (under MAO MIN01) — the in-scope home
    private const string Sty02 = "STY02"; // ORGANISATION (under MAO MIN01) — a DIFFERENT Organisation, same MAO
    private const string Sty04 = "STY04"; // ORGANISATION (under MAO MIN02) — disjoint (out-of-scope for an STY01 HR)
    private const string Min01 = "MIN01"; // MAO — holds no enheder

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
    //  (1) P7 NO-AUTHORITY — the #1 guarantee.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(1a) A cross-Organisation tag is rejected. A user homed on STY01 cannot be tagged with
    /// an enhed that belongs to STY04 (a DIFFERENT Organisation) — the set-tags endpoint validates each
    /// enhed_id against the user's CURRENT (FOR-UPDATE'd) primary_org's ACTIVE enheder → 400, and NO
    /// <c>user_enheder</c> row lands. Proves the same-Organisation invariant: an Enhed never crosses
    /// Organisations.</summary>
    [Fact]
    public async Task SetTags_CrossOrganisationEnhed_Returns400_NoRow()
    {
        var user = await SeedUserAsync("s97_xorg_user", Sty01);
        // A GlobalAdmin can create enheder anywhere; the foreign enhed lives on STY04.
        var foreignEnhed = await CreateEnhedAsync(GlobalAdminClient(), Sty04, "S97 XOrg Foreign");

        var rsp = await SetTagsAsync(GlobalAdminClient(), user, foreignEnhed);

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Equal(0, await CountUserEnhederAsync(user));
    }

    /// <summary>(1b) A shared enhed grants NO authority. Two users (A, B) on ONE Organisation (STY01)
    /// are tagged with the SAME enhed. A holds NO reporting edge over B and NO HR/Admin scope. Then:
    /// <list type="bullet">
    ///   <item><c>IsEffectiveDesignatedApproverAsync(A, B)</c> is FALSE — the enhed is not a reporting edge;</item>
    ///   <item>the org-scope validator DENIES A (carrying only an EMPLOYEE scope over STY01) access to B
    ///         at the LocalHR floor — the shared enhed contributes nothing to <c>OrgScopeValidator</c>.</item>
    /// </list>
    /// Proves the Enhed surface is ABSENT from every authority path.</summary>
    [Fact]
    public async Task SharedEnhed_GrantsNoAuthority_NoEdge_NoScope()
    {
        var a = await SeedUserAsync("s97_shared_a", Sty01);
        var b = await SeedUserAsync("s97_shared_b", Sty01);
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Shared Enhed");

        // Tag BOTH A and B with the same enhed.
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), a, enhed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), b, enhed)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(a));
        Assert.Equal(1, await CountUserEnhederAsync(b));

        // (i) The shared enhed is NOT a reporting edge — A is not B's effective designated approver.
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(a, b));

        // (ii) The shared enhed does NOT contribute org-scope authority. A carries only an EMPLOYEE
        //      scope over STY01 (NOT HR/Admin); the floored validator must DENY A access to B.
        var validator = ResolveOrgScopeValidator();
        var actorA = ActorWithScopes(a, Sty01, new RoleScope(StatsTidRoles.Employee, Sty01, "ORG_ONLY"));
        var (allowed, _) = await validator.ValidateEmployeeAccessAsync(actorA, b, StatsTidRoles.LocalHR);
        Assert.False(allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) Transfer-clears-tags + spurious-clear guard (RED-on-old).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(2a) RED-on-old: a TRANSFER (the users PUT changing primary_org_id) CLEARS the user's
    /// enhed tags in the same tx + emits <c>UserEnhederChanged(empty)</c>. Pre-S97 (no clear logic) the
    /// rows would survive a cross-Organisation move, violating the same-Organisation invariant.</summary>
    [Fact]
    public async Task Transfer_ClearsTags_AndEmitsEmptyEvent_RedOnOld()
    {
        var user = await SeedUserAsync("s97_transfer", Sty01);
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Transfer Enhed");
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), user, enhed)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(user));

        var eventsBefore = await CountUserEnhederChangedEventsAsync(user);

        // Transfer STY01 → STY02 (both ORGANISATIONs; a GlobalAdmin covers both).
        var rsp = await TransferUserAsync(GlobalAdminClient(), user, Sty02);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Tags cleared (physically removed) + a fresh UserEnhederChanged(empty) emitted.
        Assert.Equal(0, await CountUserEnhederAsync(user));
        Assert.True(await CountUserEnhederChangedEventsAsync(user) > eventsBefore,
            "A UserEnhederChanged event must be emitted on the transfer-clear.");
    }

    /// <summary>(2b) RED-on-old spurious-clear guard: a NON-transfer edit (display-name only,
    /// primary_org UNCHANGED) does NOT touch <c>user_enheder</c> and emits NO new
    /// <c>UserEnhederChanged</c>. A naive unconditional clear (the BLOCKER-B failure mode) would wipe
    /// tags on EVERY users PUT.</summary>
    [Fact]
    public async Task NonTransferEdit_DoesNotClearTags_RedOnOld()
    {
        var user = await SeedUserAsync("s97_nontransfer", Sty01);
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 NonTransfer Enhed");
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), user, enhed)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(user));

        var eventsBefore = await CountUserEnhederChangedEventsAsync(user);

        // A display-name-only edit (primary_org unchanged) → the org-change predicate is false.
        var rsp = await EditDisplayNameAsync(GlobalAdminClient(), user, "S97 Renamed Person");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(1, await CountUserEnhederAsync(user)); // tag PRESERVED
        Assert.Equal(eventsBefore, await CountUserEnhederChangedEventsAsync(user)); // no spurious clear
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Set-tags TOCTOU — set-tags vs a concurrent transfer serializes on the user-row FOR UPDATE.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(3) The set-tags FOR-UPDATE serializes against a concurrent transfer. We HOLD the user
    /// row <c>FOR UPDATE</c> on a side tx that ALSO moves the user STY01 → STY04, then fire a set-tags
    /// PUT carrying the user's CURRENT (STY01) enhed. The PUT must BLOCK on the row lock (proven by a
    /// pg_locks waiter barrier). When the side tx COMMITS (user now on STY04), the set-tags re-reads
    /// primary_org under its own lock → STY04 → the STY01 enhed fails validation → 400, and NO
    /// cross-Organisation <c>user_enheder</c> row survives.</summary>
    [Fact]
    public async Task SetTags_ConcurrentTransfer_SerializesOnForUpdate_NoCrossOrgRow()
    {
        var user = await SeedUserAsync("s97_toctou", Sty01);
        var sty01Enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 TOCTOU STY01");

        // 1. Side tx: lock the user row FOR UPDATE and transfer it STY01 → STY04 (not yet committed).
        var holdConn = new NpgsqlConnection(_harness.ConnectionString);
        await holdConn.OpenAsync();
        var holdTx = await holdConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var committed = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT primary_org_id FROM users WHERE user_id = @u FOR UPDATE", holdConn, holdTx))
            {
                lockCmd.Parameters.AddWithValue("u", user);
                await lockCmd.ExecuteScalarAsync();
            }
            await using (var moveCmd = new NpgsqlCommand(
                "UPDATE users SET primary_org_id = @new WHERE user_id = @u", holdConn, holdTx))
            {
                moveCmd.Parameters.AddWithValue("new", Sty04);
                moveCmd.Parameters.AddWithValue("u", user);
                await moveCmd.ExecuteNonQueryAsync();
            }

            // 2. Fire set-tags with the STY01 enhed — must BLOCK on the held user-row lock.
            var setTags = SetTagsAsync(GlobalAdminClient(), user, sty01Enhed);

            // 3. Barrier: a backend must be WAITING on a lock (the set-tags FOR UPDATE blocks on our row).
            Assert.True(await WaitForRowLockWaiterAsync(),
                "No backend was observed WAITING on a lock — set-tags did not serialize on the user-row FOR UPDATE.");
            Assert.False(await Task.WhenAny(setTags, Task.Delay(500)) == setTags,
                "set-tags completed while the user row was held FOR UPDATE — it did not serialize.");

            // 4. Commit the transfer (user now on STY04) and release the lock.
            await holdTx.CommitAsync();
            await holdConn.DisposeAsync();
            committed = true;

            // 5. set-tags re-reads primary_org (STY04) → the STY01 enhed is now foreign → 400.
            var rsp = await setTags;
            Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        }
        finally
        {
            if (!committed)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }

        // No cross-Organisation row survived (the STY01 enhed was never tagged onto the now-STY04 user).
        Assert.Equal(0, await CountUserEnhederAsync(user));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) Create-MAO guard (RED-on-old).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(4) RED-on-old: POST /api/admin/enheder under a MAO (MIN01) → 400 (a MAO holds no
    /// enheder); under an ORGANISATION (STY01) → 201. Pre-guard, an enhed could be created under a MAO.</summary>
    [Fact]
    public async Task CreateEnhed_UnderMao_Returns400_UnderOrganisation_Returns201()
    {
        var maoRsp = await PostEnhedAsync(GlobalAdminClient(), Min01, "S97 Under MAO");
        Assert.Equal(HttpStatusCode.BadRequest, maoRsp.StatusCode);

        var orgRsp = await PostEnhedAsync(GlobalAdminClient(), Sty01, "S97 Under Org");
        Assert.Equal(HttpStatusCode.Created, orgRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) Dedup — active-name dup 409; delete-then-recreate same name → 201 (partial-unique).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(5a) Creating a second ACTIVE enhed with the same (Organisation, lower(name)) → 409
    /// (the partial-unique idx_enheder_active_name).</summary>
    [Fact]
    public async Task CreateEnhed_ActiveNameDuplicate_Returns409()
    {
        await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Dup Name");
        var dup = await PostEnhedAsync(GlobalAdminClient(), Sty01, "S97 Dup Name");
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    /// <summary>(5b) Renaming an enhed TO an existing active name in the same Organisation → 409.</summary>
    [Fact]
    public async Task RenameEnhed_ToExistingActiveName_Returns409()
    {
        await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Rename Target");
        var other = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Rename Source");

        var rsp = await PutEnhedRenameAsync(GlobalAdminClient(), other, "S97 Rename Target", ifMatch: 1);
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
    }

    /// <summary>(5c) Delete-then-recreate the SAME name → 201. The partial-unique only constrains
    /// ACTIVE rows, so a soft-deleted name is recreatable.</summary>
    [Fact]
    public async Task DeleteThenRecreateSameName_Returns201()
    {
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Recreatable");
        var del = await DeleteEnhedAsync(GlobalAdminClient(), enhed, ifMatch: 1);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var recreate = await PostEnhedAsync(GlobalAdminClient(), Sty01, "S97 Recreatable");
        Assert.Equal(HttpStatusCode.Created, recreate.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) Soft-delete projection-filter — no fan-out untag; display falls back; unpickable.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(6) Soft-deleting an enhed that has tagged users leaves the <c>user_enheder</c> row
    /// PHYSICALLY intact (NO fan-out untag write), but the row is projection-FILTERED everywhere: the
    /// search display falls back to the org name, and the dead enhed is unpickable in set-tags (400).</summary>
    [Fact]
    public async Task SoftDelete_NoFanOutUntag_DisplayFallsBack_AndUnpickable()
    {
        var user = await SeedUserAsync("s97_softdel", Sty01, displayName: "S97 SoftDel Person");
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 ToDelete");
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), user, enhed)).StatusCode);
        Assert.Equal(1, await CountUserEnhederAsync(user));

        // Soft-delete the enhed.
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteEnhedAsync(GlobalAdminClient(), enhed, ifMatch: 1)).StatusCode);

        // The membership row PHYSICALLY remains (no fan-out untag write).
        Assert.Equal(1, await CountUserEnhederAsync(user));

        // The display is projection-FILTERED: the user's ACTIVE-enhed set is now EMPTY (the same
        // active-only join the search/roster display uses, so the display falls back to enhed_label ??
        // org name — never the dead enhed's name).
        var enhedRepo = new EnhedRepository(_dbFactory);
        Assert.Empty(await enhedRepo.GetUserActiveEnhedIdsAsync(user));

        // The dead enhed is unpickable: re-setting it → 400 (FilterValidActiveEnhedIdsForOrg excludes it).
        var rePick = await SetTagsAsync(GlobalAdminClient(), user, enhed);
        Assert.Equal(HttpStatusCode.BadRequest, rePick.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (7) Org-scope containment (RED-on-old) — HR over STY01 cannot reach STY04.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(7a) An HR scoped to STY01 GETs STY01 enheder → 200 (in-scope) but GETs STY04 enheder
    /// → 403 (out-of-scope). The org-scope floor (ValidateOrgAccessAsync LocalHR) is enforced on the
    /// list endpoint. RED-on-old at the feature level: the endpoint did not exist pre-S97.</summary>
    [Fact]
    public async Task ListEnheder_HrInScope200_OutOfScope403()
    {
        var hr = HrClient("s97_hr_list", Sty01);

        var inScope = await hr.GetAsync($"/api/admin/enheder?organisationId={Sty01}");
        Assert.Equal(HttpStatusCode.OK, inScope.StatusCode);

        var outOfScope = await hr.GetAsync($"/api/admin/enheder?organisationId={Sty04}");
        Assert.Equal(HttpStatusCode.Forbidden, outOfScope.StatusCode);
    }

    /// <summary>(7b) An HR scoped to STY01 POSTs an enhed in STY01 → 201 (in-scope) but POSTs in STY04
    /// → 403 (out-of-scope). Containment preserved on the create endpoint.</summary>
    [Fact]
    public async Task CreateEnhed_HrInScope201_OutOfScope403()
    {
        var hr = HrClient("s97_hr_create", Sty01);

        var inScope = await PostEnhedAsync(hr, Sty01, "S97 HR InScope");
        Assert.Equal(HttpStatusCode.Created, inScope.StatusCode);

        var outOfScope = await PostEnhedAsync(hr, Sty04, "S97 HR OutOfScope");
        Assert.Equal(HttpStatusCode.Forbidden, outOfScope.StatusCode);
    }

    /// <summary>(7c) An HR scoped to STY01 cannot set tags on an STY04 user → 403 (the set-user-tags
    /// floor is ValidateEmployeeAccessAsync over the target's CURRENT org). Containment preserved.</summary>
    [Fact]
    public async Task SetTags_HrOutOfScopeUser_Returns403()
    {
        var sty04User = await SeedUserAsync("s97_hr_tag_oos", Sty04);
        var sty04Enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty04, "S97 OOS Tag Enhed");

        var hr = HrClient("s97_hr_tag", Sty01);
        var rsp = await SetTagsAsync(hr, sty04User, sty04Enhed);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (8) Scope-leak (P7) — the ?enhedId= filter is org-bounded (no cross-org name bleed).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(8) Two enheder with the SAME name exist in DIFFERENT Organisations (STY01 + STY04),
    /// each tagging a user in its own org. An HR covering STY01 filters the search by the STY01 enhed's
    /// id → sees ONLY the STY01 user. The filter sits INSIDE the org-bound `matched` CTE and is keyed by
    /// enhed_id (not name), so the same-name STY04 enhed/user cannot bleed in. P7 scope-leak guard.</summary>
    [Fact]
    public async Task SearchByEnhedId_SameNameOtherOrg_NoCrossOrgBleed()
    {
        const string sharedName = "S97 Shared Name";
        var sty01User = await SeedUserAsync("s97_leak_sty01", Sty01, displayName: "S97 Leak STY01");
        var sty04User = await SeedUserAsync("s97_leak_sty04", Sty04, displayName: "S97 Leak STY04");

        var sty01Enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, sharedName);
        var sty04Enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty04, sharedName);
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), sty01User, sty01Enhed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetTagsAsync(GlobalAdminClient(), sty04User, sty04Enhed)).StatusCode);

        // HR covering STY01 filters by the STY01 enhed → ONLY the STY01 user, never the STY04 one.
        var hr = HrClient("s97_leak_hr", Sty01);
        var rsp = await hr.GetAsync($"/api/admin/users/search?q=S97+Leak&enhedId={sty01Enhed}&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var ids = (await rsp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();

        Assert.Contains(sty01User, ids);
        Assert.DoesNotContain(sty04User, ids);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (9) Multi-tag — a user with 2 enheder; the search returns BOTH (joined display).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(9) A user tagged with TWO enheder in their org: the search display joins both names
    /// (string_agg). Proves multi-tag membership + the joined display text.</summary>
    [Fact]
    public async Task MultiTag_TwoEnheder_SearchJoinsBoth()
    {
        var user = await SeedUserAsync("s97_multi", Sty01, displayName: "S97 Multi Person");
        var alpha = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Alpha");
        var beta = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Beta");

        var rsp = await SetTagsAsync(GlobalAdminClient(), user, alpha, beta);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(2, await CountUserEnhederAsync(user));

        // Read the JOINED display via the COMMON UNFILTERED ?q= search (no enhedId filter — the path
        // most consumers hit, now that the @enhedId typing defect is fixed). The enhed_tags display
        // aggregates ALL the user's active tags, so the joined string carries BOTH names.
        var hit = await SearchSingleUnfilteredAsync(GlobalAdminClient(), "s97_multi");
        var display = hit.GetProperty("enhedLabel").GetString();
        Assert.NotNull(display);
        Assert.Contains("S97 Alpha", display!);
        Assert.Contains("S97 Beta", display!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (10) BLOCKER 1 — set-tags POST-LOCK floor re-check (TOCTOU floor-escape closed).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(10a) BLOCKER 1 — the set-tags floor is re-checked UNDER the user-row lock against
    /// the LOCKED (current) org. We seed the user on STY01 (so the STY01 HR's PRE-LOCK
    /// ValidateEmployeeAccessAsync floor PASSES), then hold the user row in a side tx and move it to
    /// STY04. The STY01 HR fires set-tags carrying an STY01 enhed; it BLOCKS on the held lock. When
    /// the side tx commits (user now on STY04), the in-lock re-check (ValidateOrgAccessAsync over the
    /// LOCKED STY04 org) DENIES the STY01 HR → 403, NO row lands. Pre-fix (no post-lock re-check) the
    /// STY01-scoped HR could tag a user now in STY04 — a floor-escape.</summary>
    [Fact]
    public async Task SetTags_HrLosesScopeOnConcurrentTransfer_PostLockReCheckDenies_Returns403()
    {
        var user = await SeedUserAsync("s97_floor_escape", Sty01);
        var sty01Enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 FloorEscape STY01");
        var hr = HrClient("s97_floor_hr", Sty01); // scoped to STY01 ONLY (disjoint from STY04)

        var holdConn = new NpgsqlConnection(_harness.ConnectionString);
        await holdConn.OpenAsync();
        var holdTx = await holdConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var committed = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT primary_org_id FROM users WHERE user_id = @u FOR UPDATE", holdConn, holdTx))
            {
                lockCmd.Parameters.AddWithValue("u", user);
                await lockCmd.ExecuteScalarAsync();
            }
            await using (var moveCmd = new NpgsqlCommand(
                "UPDATE users SET primary_org_id = @new WHERE user_id = @u", holdConn, holdTx))
            {
                moveCmd.Parameters.AddWithValue("new", Sty04);
                moveCmd.Parameters.AddWithValue("u", user);
                await moveCmd.ExecuteNonQueryAsync();
            }

            // The STY01 HR's pre-lock floor passes (user still reads STY01 outside the held tx), so
            // the request enters the tx and BLOCKS on the held user-row lock.
            var setTags = SetTagsAsync(hr, user, sty01Enhed);
            Assert.True(await WaitForRowLockWaiterAsync(),
                "No backend was observed WAITING on a lock — set-tags did not serialize on the user-row FOR UPDATE.");

            await holdTx.CommitAsync();
            await holdConn.DisposeAsync();
            committed = true;

            // The user is now STY04; the in-lock re-check denies the STY01-only HR → 403.
            var rsp = await setTags;
            Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        }
        finally
        {
            if (!committed)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }

        Assert.Equal(0, await CountUserEnhederAsync(user));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (11) BLOCKER 2 — enhed rename/delete IN-UPDATE optimistic concurrency (no lost update).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(11a) BLOCKER 2 — two concurrent <c>If-Match:"1"</c> renames: exactly ONE 200 and
    /// ONE 412 (no lost update). The version predicate lives INSIDE the write tx, so the second
    /// rename matches 0 rows → 412 (re-read shows the bumped version). Pre-fix both would have
    /// committed (the C#-side version check raced the unconstrained UPDATE).</summary>
    [Fact]
    public async Task RenameEnhed_TwoConcurrentIfMatch1_OneWins_OneConflicts_NoLostUpdate()
    {
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Concurrent Rename");

        var a = PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 Rename A", ifMatch: 1);
        var b = PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 Rename B", ifMatch: 1);
        var results = await Task.WhenAll(a, b);

        var codes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.Contains(HttpStatusCode.PreconditionFailed, codes); // 412 — the loser, no lost update
        Assert.Single(codes.Where(c => c == HttpStatusCode.OK));
    }

    /// <summary>(11b) BLOCKER 2 — a rename with a STALE (too-low) version → 412. The enhed is at
    /// version 2 (one prior rename); an <c>If-Match:"1"</c> matches 0 rows in the UPDATE → 412 (the
    /// re-read reports the actual version 2).</summary>
    [Fact]
    public async Task RenameEnhed_StaleVersion_Returns412()
    {
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Stale Rename");
        var first = await PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 Stale Rename v2", ifMatch: 1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); // now version 2

        var stale = await PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 Stale Rename v3", ifMatch: 1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    /// <summary>(11c) BLOCKER 2 — a delete with a STALE version → 412 (the UPDATE matches 0 rows;
    /// re-read shows the row is still active at a higher version).</summary>
    [Fact]
    public async Task DeleteEnhed_StaleVersion_Returns412()
    {
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Stale Delete");
        var bump = await PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 Stale Delete v2", ifMatch: 1);
        Assert.Equal(HttpStatusCode.OK, bump.StatusCode); // now version 2

        var stale = await DeleteEnhedAsync(GlobalAdminClient(), enhed, ifMatch: 1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    /// <summary>(11d) BLOCKER 2 — delete-then-rename: once an enhed is soft-deleted, a subsequent
    /// rename matches 0 rows (the <c>deleted_at IS NULL</c> predicate fails) → 404, and NO
    /// <c>EnhedRenamed</c> event is emitted on the 0-row update (the event count is unchanged from
    /// just after the delete).</summary>
    [Fact]
    public async Task DeleteThenRename_Returns404_NoEventOnZeroRow()
    {
        var enhed = await CreateEnhedAsync(GlobalAdminClient(), Sty01, "S97 Delete Then Rename");
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteEnhedAsync(GlobalAdminClient(), enhed, ifMatch: 1)).StatusCode);

        var renamedEventsBefore = await CountEnhedEventsAsync(enhed, "EnhedRenamed");

        // The row is soft-deleted (now at version 2); a rename at version 2 matches 0 rows
        // (deleted_at IS NOT NULL) → 404, no event.
        var rsp = await PutEnhedRenameAsync(GlobalAdminClient(), enhed, "S97 After Delete", ifMatch: 2);
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
        Assert.Equal(renamedEventsBefore, await CountEnhedEventsAsync(enhed, "EnhedRenamed"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (D) Person-search WITHOUT an enhedId filter — the common unfiltered path returns 200.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The person-search WITHOUT an <c>enhedId</c> filter (the path most consumers hit, incl. the
    /// S91 pickers) returns 200. The optional <c>@enhedId</c> is bound with an explicit
    /// <c>NpgsqlDbType.Uuid</c> type in <c>ApprovalPeriodRepository.SearchPeopleAsync</c>, so an
    /// unsupplied filter no longer trips <c>42P08 (could not determine data type)</c> → no 500.
    /// </summary>
    [Fact]
    public async Task NullEnhedIdSearch_Returns200_NotError()
    {
        await SeedUserAsync("s97_nullsearch", Sty01, displayName: "S97 NullSearch Person");

        var rsp = await GlobalAdminClient().GetAsync("/api/admin/users/search?q=s97_nullsearch&limit=200&offset=0");

        Assert.NotEqual(HttpStatusCode.InternalServerError, rsp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var svc = NewTokenService();
        var token = svc.GenerateToken(
            employeeId: "s97_gadmin", name: "s97_gadmin", role: StatsTidRoles.GlobalAdmin,
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
    //  Helpers — enhed CRUD + set-tags (HTTP)
    // ════════════════════════════════════════════════════════════════════════════════

    private static async Task<HttpResponseMessage> PostEnhedAsync(HttpClient client, string orgId, string name)
        => await client.PostAsJsonAsync("/api/admin/enheder", new { organisationId = orgId, name });

    /// <summary>POSTs an enhed and returns its id, asserting 201.</summary>
    private async Task<Guid> CreateEnhedAsync(HttpClient client, string orgId, string name)
    {
        var rsp = await PostEnhedAsync(client, orgId, name);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("enhedId").GetString()!);
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
    //  Helpers — users PUT (transfer / display-name) with the If-Match version dance
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> TransferUserAsync(HttpClient client, string userId, string newOrgId)
    {
        var version = await ReadUserVersionAsync(client, userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { primaryOrgId = newOrgId, effectiveFrom = today }),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return await client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> EditDisplayNameAsync(HttpClient client, string userId, string displayName)
    {
        var version = await ReadUserVersionAsync(client, userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { displayName, effectiveFrom = today }),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return await client.SendAsync(req);
    }

    private static async Task<long> ReadUserVersionAsync(HttpClient client, string userId)
    {
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        getRsp.EnsureSuccessStatusCode();
        var tag = getRsp.Headers.ETag!.Tag.Trim('"');
        return long.Parse(tag);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — search / DB reads / seeding
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Runs the person search for <paramref name="q"/> filtered by <paramref name="enhedId"/>
    /// and returns the SINGLE matching item (asserts exactly one). The returned <c>enhedLabel</c>
    /// display aggregates ALL the user's active tags, independent of the filter.</summary>
    private async Task<JsonElement> SearchSingleByEnhedAsync(HttpClient client, string q, Guid enhedId)
    {
        var rsp = await client.GetAsync($"/api/admin/users/search?q={q}&enhedId={enhedId}&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var items = (await rsp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        return items[0];
    }

    /// <summary>Runs the person search for <paramref name="q"/> with NO enhedId filter (the common
    /// unfiltered path) and returns the SINGLE matching item (asserts exactly one). The joined
    /// <c>enhedLabel</c> display aggregates ALL the user's active tags.</summary>
    private async Task<JsonElement> SearchSingleUnfilteredAsync(HttpClient client, string q)
    {
        var rsp = await client.GetAsync($"/api/admin/users/search?q={q}&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var items = (await rsp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        return items[0];
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

    private async Task<int> CountUserEnhederChangedEventsAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'UserEnhederChanged' AND stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", $"user-{userId}");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Counts outbox events of <paramref name="eventType"/> on the
    /// <c>enhed-{enhedId}</c> stream (used to prove NO event is emitted on a 0-row UPDATE).</summary>
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
    /// <paramref name="orgId"/>; the org FK parent is the existing init.sql ORGANISATION (ensureOrg
    /// false). Optionally sets the display name.</summary>
    private async Task<string> SeedUserAsync(string baseId, string orgId, string? displayName = null)
    {
        var userId = baseId + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, userId, orgId, "AC", "OK24", ensureOrg: false);
        if (displayName is not null)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET display_name = @dn WHERE user_id = @u", conn);
            cmd.Parameters.AddWithValue("dn", displayName);
            cmd.Parameters.AddWithValue("u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        return userId;
    }

    /// <summary>Polls <c>pg_stat_activity</c> until at least one OTHER backend is WAITING on a Lock
    /// (<c>wait_event_type = 'Lock'</c>) — proving the set-tags request REACHED + BLOCKED on the held
    /// user-row FOR UPDATE (a row-lock wait surfaces as a transactionid/tuple lock, observed here via
    /// the activity-level wait-event). Returns <c>true</c> once a waiter is seen; <c>false</c> on timeout.</summary>
    private async Task<bool> WaitForRowLockWaiterAsync(int timeoutMs = 5000)
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
                    FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND wait_event_type = 'Lock'
                      AND state = 'active'
                )
                """, conn))
            {
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
