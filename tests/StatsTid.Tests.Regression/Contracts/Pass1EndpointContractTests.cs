using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S101 / TASK-10102 — the Pass-1 endpoint RESPONSE-CONTRACT suite (the durable guard for the
/// recurring "fetchEnheder" bug class: S97 → S99 → S100, where a FE list-hook test mocked the right
/// envelope [vitest green] while the real endpoint served a different shape → prod broke). These run
/// end-to-end against the real Backend.Api via <see cref="StatsTidWebApplicationFactory"/> and pin,
/// for the 3 Pass-1 endpoints, the wire shape the FE hooks consume:
///
/// <list type="number">
///   <item><c>GET /api/admin/enheder</c> → the <c>{ enheder: [...] }</c> ENVELOPE (NOT a bare array —
///     the S97/S99 bug), each row carrying <c>enhedId/organisationId/name/version/parentEnhedId/level</c>
///     with <c>parentEnhedId</c> null at a root + a string at a child (folds in the S100
///     <c>GetEnhederFlatList_CarriesParentEnhedId_AndDerivedLevel</c> contract).</item>
///   <item><c>GET /api/admin/organizations/tree</c> → the <c>{ tree: [...] }</c> ENVELOPE with a DEEP
///     (≥2) nested enhed node carrying <c>level</c> + <c>children</c> + the <c>parentEnhedId</c> kind
///     (the S100 bug was a NESTED drop — assert the nesting, not just the top level).</item>
///   <item><c>GET /api/admin/organizations</c> → a BARE ARRAY (NOT an envelope), each item carrying
///     the real <c>MapOrgResponse</c> fields.</item>
/// </list>
///
/// <para>The camelCase keys are asserted LITERALLY via <see cref="ContractAssert"/> — the load-bearing
/// guard catching any future global serializer-policy regression. RED-on-old: the records (S101) are
/// PascalCase; a dropped field or an envelope↔bare-array drift fails the relevant assertion. The tests
/// are RED if the records break the shape — proving byte-identity is preserved while they stay GREEN.</para>
///
/// <para>Harness mirrors <see cref="StatsTid.Tests.Regression.Security.S100EnhedHierarchyTests"/>
/// (DockerHarness + token minting + the MAO→organisations[]→enheder[] walk).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Pass1EndpointContractTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Sty01 = "STY01"; // ORGANISATION (under MAO MIN01) — the enhed-tree home
    private const string Min01 = "MIN01"; // MAO

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (org tree MIN01/MIN02 + STY0x + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) GET /api/admin/enheder — the { enheder: [...] } ENVELOPE (NOT a bare array).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The flat-list contract: <c>{ enheder: [...] }</c> (the S97/S99 envelope, NOT a bare
    /// array), each row HasFields(enhedId, organisationId, name, version, parentEnhedId, level), with
    /// parentEnhedId Null + level 1 for the root and parentEnhedId String + level 2 for the child.
    /// Folds in the S100 <c>GetEnhederFlatList_CarriesParentEnhedId_AndDerivedLevel</c> test.
    /// RED-on-old: pre-records an envelope→bare-array drift fails IsEnvelope; a dropped
    /// parentEnhedId/level fails HasFields/FieldKind.</summary>
    [Fact]
    public async Task GetEnheder_IsEnvelope_RowsCarryParentAndLevel()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S101 Contract Root", parent: null);
        var child = await CreateEnhedAsync(admin, Sty01, "S101 Contract Child", parent: root);

        var hr = HrClient("s101_enheder_hr", Sty01);
        var rsp = await hr.GetAsync($"/api/admin/enheder?organisationId={Sty01}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // Envelope { enheder: [...] }, NOT a bare array (the load-bearing S97/S99 guard).
        var enheder = ContractAssert.IsEnvelope(body, "enheder");

        var rootRow = FindEnhedNode(enheder, root)
            ?? throw new XunitException("root enhed missing from GET /enheder.");
        // Each row carries the full field-set the FE EnhederPanel reads — camelCase, literally.
        ContractAssert.HasFields(rootRow, "enhedId", "organisationId", "name", "version", "parentEnhedId", "level");
        // A root: parentEnhedId is Null, level == 1.
        ContractAssert.FieldKind(rootRow, "parentEnhedId", JsonValueKind.Null);
        Assert.Equal(1, rootRow.GetProperty("level").GetInt32());

        var childRow = FindEnhedNode(enheder, child)
            ?? throw new XunitException("child enhed missing from GET /enheder.");
        ContractAssert.HasFields(childRow, "enhedId", "organisationId", "name", "version", "parentEnhedId", "level");
        // A child: parentEnhedId is a String == the root id, level == 2.
        ContractAssert.FieldKind(childRow, "parentEnhedId", JsonValueKind.String);
        Assert.Equal(root, Guid.Parse(childRow.GetProperty("parentEnhedId").GetString()!));
        Assert.Equal(2, childRow.GetProperty("level").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) GET /api/admin/organizations/tree — the { tree: [...] } ENVELOPE + DEEP nesting.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The tree contract: <c>{ tree: [...] }</c> envelope, and a representative DEEP enhed
    /// node (a ≥2-deep chain under a visible Organisation) carries <c>level</c> (1/2) + <c>children</c>
    /// + the <c>parentEnhedId</c> kind (Null at the root, String at the child) — asserting the NESTING,
    /// since the S100 bug was a NESTED drop (the top-level node looked fine). RED-on-old: an
    /// envelope→bare-array drift fails IsEnvelope; a dropped nested level/children/parentEnhedId
    /// fails HasFields/FieldKind on the deep node.</summary>
    [Fact]
    public async Task GetTree_IsEnvelope_DeepEnhedNodeNestsLevelChildrenParent()
    {
        var admin = GlobalAdminClient();
        var root = await CreateEnhedAsync(admin, Sty01, "S101 Tree Root", parent: null);
        var child = await CreateEnhedAsync(admin, Sty01, "S101 Tree Child", parent: root);

        var rsp = await admin.GetAsync("/api/admin/organizations/tree");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // Envelope { tree: [...] }, NOT a bare array.
        var tree = ContractAssert.IsEnvelope(body, "tree");

        // Pin the MAO + Organisation NODE field-sets the FE reads (TreeMaoNode /
        // TreeOrganisationNode in frontend/src/hooks/useOrganizationTree.ts) — a
        // dropped field on those records must go RED too, not just the enhed leaf.
        var min01Node = FindMaoNode(tree, Min01)
            ?? throw new XunitException($"MAO '{Min01}' missing from the GET /tree forest.");
        // TreeMaoNode: orgId, orgName, orgType('MAO'), employeeCount, organisations[].
        ContractAssert.HasFields(min01Node, "orgId", "orgName", "orgType", "employeeCount", "organisations");
        Assert.Equal("MAO", min01Node.GetProperty("orgType").GetString());
        ContractAssert.FieldKind(min01Node, "organisations", JsonValueKind.Array);

        var sty01Node = FindOrgNode(tree, Sty01)
            ?? throw new XunitException($"Organisation '{Sty01}' missing from the GET /tree forest.");
        // TreeOrganisationNode: orgId, orgName, orgType('ORGANISATION'), parentOrgId,
        // materializedPath, employeeCount, enheder[].
        ContractAssert.HasFields(sty01Node,
            "orgId", "orgName", "orgType", "parentOrgId", "materializedPath", "employeeCount", "enheder");
        Assert.Equal("ORGANISATION", sty01Node.GetProperty("orgType").GetString());
        // STY01 is under MAO MIN01 → parentOrgId is a non-null String on the node.
        ContractAssert.FieldKind(sty01Node, "parentOrgId", JsonValueKind.String);
        ContractAssert.FieldKind(sty01Node, "enheder", JsonValueKind.Array);

        // Walk the MAO → organisations[] → enheder[] forest to STY01's enhed forest.
        var sty01Enheder = FindOrgEnheder(tree, Sty01);

        // The root enhed node (level 1) carries `children` + a Null parentEnhedId.
        var rootNode = FindEnhedNode(sty01Enheder, root)
            ?? throw new XunitException("root enhed node missing from the GET /tree forest.");
        ContractAssert.HasFields(rootNode, "enhedId", "parentEnhedId", "level", "name", "taggedUserCount", "children");
        ContractAssert.FieldKind(rootNode, "parentEnhedId", JsonValueKind.Null);
        ContractAssert.FieldKind(rootNode, "children", JsonValueKind.Array);
        Assert.Equal(1, rootNode.GetProperty("level").GetInt32());

        // The DEEP (nested) child node — the S100 NESTED-drop guard: level 2, String parentEnhedId,
        // reachable ONLY through the root's `children` (assert the nesting, not just the top level).
        var childNode = FindEnhedNode(rootNode.GetProperty("children"), child)
            ?? throw new XunitException("nested child enhed node missing from the root's children.");
        ContractAssert.HasFields(childNode, "enhedId", "parentEnhedId", "level", "name", "taggedUserCount", "children");
        ContractAssert.FieldKind(childNode, "parentEnhedId", JsonValueKind.String);
        Assert.Equal(root, Guid.Parse(childNode.GetProperty("parentEnhedId").GetString()!));
        Assert.Equal(2, childNode.GetProperty("level").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) GET /api/admin/organizations — a BARE ARRAY (NOT an envelope).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The org-list contract: a BARE ARRAY at the root (NOT an envelope — the FE
    /// <c>useAdmin</c> hook consumes <c>apiClient.get&lt;Organization[]&gt;</c> directly), each item
    /// HasFields(orgId, orgName, orgType, parentOrgId, materializedPath, agreementCode, okVersion).
    /// RED-on-old: a bare-array→envelope drift fails IsArray; a dropped field fails HasFields.</summary>
    [Fact]
    public async Task GetOrganizations_IsBareArray_ItemsCarryOrgFields()
    {
        var admin = GlobalAdminClient();
        var rsp = await admin.GetAsync("/api/admin/organizations");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // A BARE ARRAY at the root, NOT an envelope (the FE reads Organization[] directly).
        var array = ContractAssert.IsArray(body);
        Assert.True(array.GetArrayLength() > 0, "the seeded org tree should yield ≥1 org row.");

        // Locate the STY01 row and assert the full MapOrgResponse field-set (camelCase, literally).
        var sty01 = FindOrg(array, Sty01)
            ?? throw new XunitException($"Organisation '{Sty01}' missing from GET /organizations.");
        ContractAssert.HasFields(sty01,
            "orgId", "orgName", "orgType", "parentOrgId", "materializedPath", "agreementCode", "okVersion");
        Assert.Equal(Sty01, sty01.GetProperty("orgId").GetString());
        Assert.Equal("ORGANISATION", sty01.GetProperty("orgType").GetString());
        // STY01 is under MAO MIN01 → parentOrgId is a String (a MAO root would be Null).
        ContractAssert.FieldKind(sty01, "parentOrgId", JsonValueKind.String);

        // The MAO MIN01 row: parentOrgId is Null (a root) — proves the nullable field serializes.
        var min01 = FindOrg(array, Min01)
            ?? throw new XunitException($"MAO '{Min01}' missing from GET /organizations.");
        ContractAssert.FieldKind(min01, "parentOrgId", JsonValueKind.Null);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens  (mirror S100)
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var svc = NewTokenService();
        var token = svc.GenerateToken(
            employeeId: "s101_gadmin", name: "s101_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Min01,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A single-scope LocalHR client anchored at <paramref name="orgId"/> (ORG_ONLY, S93 flat
    /// role-scope) — covers exactly that Organisation.</summary>
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

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — enhed CRUD (HTTP)  (mirror S100)
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<Guid> CreateEnhedAsync(HttpClient client, string orgId, string name, Guid? parent)
    {
        var rsp = await client.PostAsJsonAsync("/api/admin/enheder",
            new { organisationId = orgId, name, parentEnhedId = parent });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var b = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(b.GetProperty("enhedId").GetString()!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — JSON navigation  (lifted from S100EnhedHierarchyTests:373-388,576-599)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Finds the enhed forest (the <c>enheder</c> array) for the Organisation
    /// <paramref name="orgId"/> inside a <c>GET /tree</c> response's <c>tree</c> array
    /// (<c>[ MAO → organisations[] → enheder[] ]</c>).</summary>
    private static JsonElement FindOrgEnheder(JsonElement treeArray, string orgId)
    {
        foreach (var mao in treeArray.EnumerateArray())
        {
            if (!mao.TryGetProperty("organisations", out var orgs))
                continue;
            foreach (var org in orgs.EnumerateArray())
            {
                if (string.Equals(org.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                    return org.GetProperty("enheder");
            }
        }
        throw new XunitException($"Organisation '{orgId}' not found in the GET /tree forest.");
    }

    /// <summary>Returns the MAO node (a top-level <c>tree[]</c> element) with id
    /// <paramref name="orgId"/>, or <c>null</c> if absent.</summary>
    private static JsonElement? FindMaoNode(JsonElement treeArray, string orgId)
    {
        foreach (var mao in treeArray.EnumerateArray())
        {
            if (string.Equals(mao.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                return mao;
        }
        return null;
    }

    /// <summary>Returns the Organisation node (a <c>MAO.organisations[]</c> element) with id
    /// <paramref name="orgId"/> anywhere in the forest, or <c>null</c> if absent.</summary>
    private static JsonElement? FindOrgNode(JsonElement treeArray, string orgId)
    {
        foreach (var mao in treeArray.EnumerateArray())
        {
            if (!mao.TryGetProperty("organisations", out var orgs))
                continue;
            foreach (var org in orgs.EnumerateArray())
            {
                if (string.Equals(org.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                    return org;
            }
        }
        return null;
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

    /// <summary>Returns the org row with id <paramref name="orgId"/> from the bare-array
    /// <c>GET /organizations</c> response, or <c>null</c> if absent.</summary>
    private static JsonElement? FindOrg(JsonElement orgArray, string orgId)
    {
        foreach (var org in orgArray.EnumerateArray())
        {
            if (string.Equals(org.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                return org;
        }
        return null;
    }
}
