using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Organisation;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S148 / TASK-14836-T (T-BC-5, ruling 21; AC-C11a's read) — the spec-equals-runtime gate for the HR-facts read:
/// the body <c>GET /api/admin/employees/{employeeId}/hr-facts</c> really serves is the one the committed
/// <c>docs/api/openapi.json</c> declares (key set, nullability, array-ness, the five-value enum on
/// <c>overtimeRegime</c>).
///
/// <para>
/// <b>Kind F (CI, Docker; not run locally).</b> Red conditions: on <c>1cc0cb1d</c> the route does not exist (the
/// first PUT answers 404, so <c>Assert.Equal(200, …)</c> fails). After TASK-14836's merge and before REGEN has
/// written the operation into <c>openapi.json</c>, <c>SpecRuntimeMatcher.ResolveSuccessContract</c> fails because the
/// spec has no such operation: this fact is green only once REGEN is on the tip, which is by design. The
/// Orchestrator adds the registry key <c>/api/admin/employees/{}/hr-facts</c> with this method (plan O-31).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S148HrFactsSpecRuntimeTests : IAsyncLifetime
{
    private const string SpecPath = "/api/admin/employees/{employeeId}/hr-facts";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F (CI).</b> A GlobalAdmin records HR facts for an HK person (the person is not the actor), then reads them:
    /// 200, the body matches the committed schema through the matcher, carries exactly the nine members, the
    /// ETag equals the open row's version, and the values are the ones recorded.
    /// </summary>
    [Fact]
    public async Task HrFacts_Get_SchemaMatchesRuntime()
    {
        var person = await TransferWriteTests.SeedPersonAsync(_harness.ConnectionString, "STY01", "HK");
        using var admin = TransferWriteTests.GlobalAdminClient(_factory, "ADMIN_S148_C11_SPEC");

        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{person}/hr-facts")
        {
            Content = JsonContent.Create(new { overtimeRegime = "OVERTIME", onFlexScheme = true }),
        };
        put.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var created = await admin.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/employees/{person}/hr-facts");
        using var response = await admin.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, SpecPath, "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "hr-facts GET 200");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, new[]
        {
            "employeeId", "asOf", "agreementCode", "allowedOvertimeRegimes", "overtimeRegime",
            "onFlexScheme", "effectiveFrom", "liveVersion", "liveEffectiveFrom",
        }, "hr-facts GET 200");
        Assert.Equal(person, root.GetProperty("employeeId").GetString());
        Assert.Equal("HK", root.GetProperty("agreementCode").GetString());
        Assert.Equal(new[] { "OVERTIME", "EXEMPT" },
            root.GetProperty("allowedOvertimeRegimes").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("OVERTIME", root.GetProperty("overtimeRegime").GetString());
        Assert.True(root.GetProperty("onFlexScheme").GetBoolean());
        Assert.Equal(1L, root.GetProperty("liveVersion").GetInt64());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }
}
