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

    /// <summary>Tree-settings (enforcement) READ (now HROrAbove / LocalHR floor): HR@MIN01 → 200
    /// (returns the PREFERRED default when no row exists). RED-on-old.</summary>
    [Fact]
    public async Task TreeSettingsRead_HrInScope_Returns200()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_setget_hr"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/settings");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>Tree-settings (enforcement) WRITE (now HROrAbove / LocalHR floor): HR@MIN01 PUTs the
    /// enforcement mode (PREFERRED) on STY01 → 200. Carries If-Match: "0" (no existing row = version
    /// 0). The enforcement toggle is a structural tree mutation, now an HR affordance. RED-on-old.</summary>
    [Fact]
    public async Task TreeSettingsWrite_HrInScope_Returns200()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "s91_setput_hr"));
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/reporting-lines/tree/{TargetOrg}/settings")
        {
            Content = JsonContent.Create(new { enforcementMode = "PREFERRED" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

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

    /// <summary>Tree-settings WRITE: the mixed HR@STY05 + Leader@MIN01 JWT → 403 on a STY01
    /// enforcement write (containment preserved at the LocalHR floor).</summary>
    [Fact]
    public async Task TreeSettingsWrite_OutOfScopeHr_Returns403()
    {
        var client = ClientWith(MixedHrLeaderToken("s91_setput_oos"));
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/reporting-lines/tree/{TargetOrg}/settings")
        {
            Content = JsonContent.Create(new { enforcementMode = "PREFERRED" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

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

    /// <summary>Tree-settings WRITE: a LocalLeader@MIN01 → 403 (below the HROrAbove policy).</summary>
    [Fact]
    public async Task TreeSettingsWrite_BelowHrLeader_Returns403()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalLeader, CoveringOrg, "s91_setput_leader"));
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/reporting-lines/tree/{TargetOrg}/settings")
        {
            Content = JsonContent.Create(new { enforcementMode = "PREFERRED" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

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
