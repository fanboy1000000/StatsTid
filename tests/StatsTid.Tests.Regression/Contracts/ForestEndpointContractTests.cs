using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S106 / TASK-10601 (ADR-038 D1/D5, PAT-010) — the endpoint RESPONSE-CONTRACT test for the unified
/// scoped FOREST read <c>GET /api/admin/units/forest</c>. Runs end-to-end against the real Backend.Api
/// via <see cref="StatsTidWebApplicationFactory"/> and pins the wire shape a future FE forest hook will
/// consume, closing the recurring "fetchEnheder" false-green bug class (S97 → S99 → S100) for the
/// merged-admin forest surface BEFORE a FE consumer exists.
///
/// <para>The init.sql baseline seeds a 4-level demo unit chain under STY02 (Statens IT) —
/// Direktion → Driftsomraadet → IT-Drift → Team Infrastruktur — with members, so a GlobalAdmin forest
/// carries a DEEP nested unit node to assert against (the S100 nested-drop is the bug class).</para>
///
/// <list type="number">
///   <item><c>{ forest: [...] }</c> ENVELOPE (NOT a bare array — the S97/S99 distinction).</item>
///   <item>MAO node fields (camelCase, literally) + the nested <c>organisations</c> array.</item>
///   <item>ORGANISATION node fields incl. <c>memberCount</c>/<c>directMemberCount</c> + the nested
///     <c>units</c> array.</item>
///   <item>A DEEP unit node (≥ level 2) carrying <c>unitId</c>/<c>type</c>/<c>level</c>/
///     <c>directMemberCount</c>/<c>memberCount</c>/<c>children</c> — camelCase, literally.</item>
/// </list>
///
/// <para>RED-on-old: the records are PascalCase; a dropped field, a renamed key, an envelope↔bare-array
/// drift, or a future global <c>AddJsonOptions</c>/serializer regression fails the relevant
/// <see cref="ContractAssert"/> assertion.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ForestEndpointContractTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Sty02 = "STY02"; // ORGANISATION carrying the demo unit chain
    private const string Min01 = "MIN01"; // MAO

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree + the demo STY02 unit chain)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>The forest is the <c>{ forest: [...] }</c> envelope; each MAO carries its fields + a
    /// nested <c>organisations</c> array; STY02 carries its fields + a nested <c>units</c> array; and a
    /// DEEP unit node carries the full unit field-set (camelCase, literally). RED-on-old: a dropped
    /// field fails HasFields; a bare-array drift fails IsEnvelope; a nested-unit drop fails the deep
    /// assertion (the S100 bug).</summary>
    [Fact]
    public async Task GetForest_IsEnvelope_MaoOrgAndDeepUnitNodesCarryFields()
    {
        var admin = GlobalAdminClient();

        var rsp = await admin.GetAsync("/api/admin/units/forest");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // ── 1) the { forest: [...] } ENVELOPE (NOT a bare array). ──
        var forest = ContractAssert.IsEnvelope(body, "forest");

        // ── 2) the MAO node — find MIN01, assert fields + the nested organisations array. ──
        var min01 = FindByOrgId(forest, Min01)
            ?? throw new XunitException("MAO MIN01 missing from the forest.");
        ContractAssert.HasFields(min01,
            "orgId", "orgName", "orgType", "parentOrgId", "materializedPath", "memberCount", "organisations");
        Assert.Equal("MAO", min01.GetProperty("orgType").GetString());
        var organisations = min01.GetProperty("organisations");
        Assert.Equal(JsonValueKind.Array, organisations.ValueKind);

        // ── 3) the ORGANISATION node — STY02, assert fields + the nested units array. ──
        var sty02 = FindByOrgId(organisations, Sty02)
            ?? throw new XunitException("ORGANISATION STY02 missing under MIN01.");
        ContractAssert.HasFields(sty02,
            "orgId", "orgName", "orgType", "parentOrgId", "materializedPath",
            "agreementCode", "okVersion", "memberCount", "directMemberCount", "units");
        Assert.Equal("ORGANISATION", sty02.GetProperty("orgType").GetString());
        var units = sty02.GetProperty("units");
        Assert.Equal(JsonValueKind.Array, units.ValueKind);
        Assert.NotEmpty(units.EnumerateArray());

        // ── 4) a DEEP unit node (≥ level 2) — the demo chain nests Direktion → Driftsomraadet → … ──
        var deep = FindDeepUnit(units, minLevel: 2)
            ?? throw new XunitException("No nested unit node (level ≥ 2) found under STY02 — the demo unit chain did not nest (the S100 drop).");
        ContractAssert.HasFields(deep,
            "unitId", "organisationId", "parentUnitId", "type", "name", "level",
            "version", "directMemberCount", "memberCount", "children");
        // A deep unit has a non-null parentUnitId (a String on the wire); a top-level unit has null.
        ContractAssert.FieldKind(deep, "parentUnitId", JsonValueKind.String);
        Assert.True(deep.GetProperty("level").GetInt32() >= 2);
        ContractAssert.FieldKind(deep, "children", JsonValueKind.Array);

        // ── 5) the NULLABILITY pin — a TOP-LEVEL unit (directly under the Organisation, the roots of
        //    STY02's `units` array) carries a NULL parentUnitId + derived level 1. This pins the
        //    Null-at-root / String-at-child kind (the S100 `parentEnhedId` precedent — a JSON-null at
        //    the root vs a String at a child), catching a future serializer/shape drift on the boundary.
        var topUnit = units.EnumerateArray().First();
        ContractAssert.FieldKind(topUnit, "parentUnitId", JsonValueKind.Null);
        Assert.Equal(1, topUnit.GetProperty("level").GetInt32());
    }

    // ── Helpers ──

    private static JsonElement? FindByOrgId(JsonElement nodes, string orgId)
    {
        foreach (var n in nodes.EnumerateArray())
            if (string.Equals(n.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                return n;
        return null;
    }

    /// <summary>DFS for the first unit node whose derived <c>level</c> ≥ <paramref name="minLevel"/>.</summary>
    private static JsonElement? FindDeepUnit(JsonElement units, int minLevel)
    {
        foreach (var u in units.EnumerateArray())
        {
            if (u.GetProperty("level").GetInt32() >= minLevel)
                return u;
            var hit = FindDeepUnit(u.GetProperty("children"), minLevel);
            if (hit is not null) return hit;
        }
        return null;
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "s106_gadmin", name: "s106_gadmin", role: StatsTidRoles.GlobalAdmin,
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
}
