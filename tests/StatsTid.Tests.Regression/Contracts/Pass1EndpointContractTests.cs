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
/// for the Pass-1 endpoints, the wire shape the FE hooks consume:
///
/// <list type="number">
///   <item><c>GET /api/admin/organizations/tree</c> → the <c>{ tree: [...] }</c> ENVELOPE with the
///     MAO + Organisation node field-sets the FE reads.</item>
///   <item><c>GET /api/admin/organizations</c> → a BARE ARRAY (NOT an envelope), each item carrying
///     the real <c>MapOrgResponse</c> fields.</item>
/// </list>
///
/// <para>S103 / TASK-10305 (Enhedsspor Phase 1a): the legacy Enhed model was dropped — the
/// <c>GET /api/admin/enheder</c> endpoint and the per-Organisation <c>enheder[]</c> nesting on the
/// tree are gone (units return in S104+). The enheder contract test + the nested-enhed tree
/// assertions are retired here; the organizations/tree + organizations contracts remain.</para>
///
/// <para>The camelCase keys are asserted LITERALLY via <see cref="ContractAssert"/> — the load-bearing
/// guard catching any future global serializer-policy regression. RED-on-old: the records (S101) are
/// PascalCase; a dropped field or an envelope↔bare-array drift fails the relevant assertion.</para>
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
    //  (1) GET /api/admin/organizations/tree — the { tree: [...] } ENVELOPE + node field-sets.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The tree contract: <c>{ tree: [...] }</c> envelope with the MAO + Organisation node
    /// field-sets the FE reads (TreeMaoNode / TreeOrganisationNode in
    /// frontend/src/hooks/useOrganizationTree.ts). S103 / TASK-10305: the per-Organisation
    /// <c>enheder[]</c> nesting was retired with the legacy Enhed model (units return in S104+), so
    /// the org node no longer carries an <c>enheder</c> array. RED-on-old: an envelope→bare-array
    /// drift fails IsEnvelope; a dropped MAO/Organisation field fails HasFields/FieldKind.</summary>
    [Fact]
    public async Task GetTree_IsEnvelope_MaoAndOrgNodesCarryFields()
    {
        var admin = GlobalAdminClient();

        var rsp = await admin.GetAsync("/api/admin/organizations/tree");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // Envelope { tree: [...] }, NOT a bare array.
        var tree = ContractAssert.IsEnvelope(body, "tree");

        // Pin the MAO + Organisation NODE field-sets the FE reads — a dropped field must go RED.
        var min01Node = FindMaoNode(tree, Min01)
            ?? throw new XunitException($"MAO '{Min01}' missing from the GET /tree forest.");
        // TreeMaoNode: orgId, orgName, orgType('MAO'), employeeCount, organisations[].
        ContractAssert.HasFields(min01Node, "orgId", "orgName", "orgType", "employeeCount", "organisations");
        Assert.Equal("MAO", min01Node.GetProperty("orgType").GetString());
        ContractAssert.FieldKind(min01Node, "organisations", JsonValueKind.Array);

        var sty01Node = FindOrgNode(tree, Sty01)
            ?? throw new XunitException($"Organisation '{Sty01}' missing from the GET /tree forest.");
        // TreeOrganisationNode: orgId, orgName, orgType('ORGANISATION'), parentOrgId,
        // materializedPath, employeeCount.
        ContractAssert.HasFields(sty01Node,
            "orgId", "orgName", "orgType", "parentOrgId", "materializedPath", "employeeCount");
        Assert.Equal("ORGANISATION", sty01Node.GetProperty("orgType").GetString());
        // STY01 is under MAO MIN01 → parentOrgId is a non-null String on the node.
        ContractAssert.FieldKind(sty01Node, "parentOrgId", JsonValueKind.String);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) GET /api/admin/organizations — a BARE ARRAY (NOT an envelope).
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

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — JSON navigation
    // ════════════════════════════════════════════════════════════════════════════════

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
