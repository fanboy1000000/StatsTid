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
/// S104 / TASK-10403 (ADR-038 D3, PAT-010) — the endpoint RESPONSE-CONTRACT suite for the typed
/// <c>units</c> admin surface. Runs end-to-end against the real Backend.Api via
/// <see cref="StatsTidWebApplicationFactory"/> and pins the wire shape a future FE units hook will
/// consume, closing the recurring "fetchEnheder" false-green bug class (S97 → S99 → S100) for the new
/// surface BEFORE a FE consumer exists:
///
/// <list type="number">
///   <item><c>POST /api/admin/units</c> → the single <see cref="StatsTid.Backend.Api.Contracts.UnitResponse"/>
///     shape (unitId, organisationId, parentUnitId, type, name, version) — camelCase, literally.</item>
///   <item><c>GET /api/admin/units</c> → the <c>{ units: [...] }</c> ENVELOPE (NOT a bare array — the
///     S97/S99 envelope-vs-bare-array drift), each item carrying the same field-set.</item>
/// </list>
///
/// <para>RED-on-old: the records are PascalCase; a dropped field, a renamed key, or an
/// envelope↔bare-array drift fails the relevant <see cref="ContractAssert"/> assertion.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UnitEndpointContractTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Sty01 = "STY01"; // ORGANISATION (under MAO MIN01) — the unit-tree home
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

    /// <summary>POST creates a top-level unit under STY01; the 201 body is the UnitResponse shape
    /// (camelCase, literally). Then GET returns the { units: [...] } ENVELOPE carrying the same
    /// field-set. RED-on-old: a dropped field fails HasFields; a bare-array drift fails IsEnvelope.</summary>
    [Fact]
    public async Task CreateThenList_PostIsUnitResponse_ListIsEnvelope()
    {
        var admin = GlobalAdminClient();

        // ── POST /api/admin/units — the single UnitResponse shape ──
        var createRsp = await admin.PostAsJsonAsync("/api/admin/units",
            new { organisationId = Sty01, type = "team", name = "S104-contract-team" });
        Assert.Equal(HttpStatusCode.Created, createRsp.StatusCode);

        var created = await createRsp.Content.ReadFromJsonAsync<JsonElement>();
        ContractAssert.HasFields(created, "unitId", "organisationId", "parentUnitId", "type", "name", "version");
        Assert.Equal(Sty01, created.GetProperty("organisationId").GetString());
        Assert.Equal("team", created.GetProperty("type").GetString());
        Assert.Equal("S104-contract-team", created.GetProperty("name").GetString());
        // A top-level unit (no parent) → parentUnitId is Null on the wire.
        ContractAssert.FieldKind(created, "parentUnitId", JsonValueKind.Null);
        var unitId = created.GetProperty("unitId").GetString();

        // ── GET /api/admin/units — the { units: [...] } ENVELOPE ──
        var listRsp = await admin.GetAsync($"/api/admin/units?organisationId={Sty01}");
        Assert.Equal(HttpStatusCode.OK, listRsp.StatusCode);
        var body = await listRsp.Content.ReadFromJsonAsync<JsonElement>();

        var units = ContractAssert.IsEnvelope(body, "units");
        var node = FindUnit(units, unitId!)
            ?? throw new XunitException($"Unit '{unitId}' missing from GET /api/admin/units.");
        ContractAssert.HasFields(node, "unitId", "organisationId", "parentUnitId", "type", "name", "version");
        Assert.Equal("team", node.GetProperty("type").GetString());
        ContractAssert.FieldKind(node, "parentUnitId", JsonValueKind.Null);
    }

    // ── Helpers ──

    private static JsonElement? FindUnit(JsonElement unitsArray, string unitId)
    {
        foreach (var u in unitsArray.EnumerateArray())
        {
            if (string.Equals(u.GetProperty("unitId").GetString(), unitId, StringComparison.OrdinalIgnoreCase))
                return u;
        }
        return null;
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var svc = NewTokenService();
        var token = svc.GenerateToken(
            employeeId: "s104_gadmin", name: "s104_gadmin", role: StatsTidRoles.GlobalAdmin,
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
