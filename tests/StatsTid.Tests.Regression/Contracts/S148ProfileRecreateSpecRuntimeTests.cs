using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Organisation;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S148 / TASK-14851-T (AC-Q268k; ruling 14; QUAL-268) - the profile read's "not found" body is the body the committed
/// <c>docs/api/openapi.json</c> declares: the six members, each one's type and nullability.
///
/// <para>
/// <b>Kind F (CI, Docker; not run locally).</b> <b>Red conditions:</b> the spec committed at <c>319f8b68</c> declares no 404 response for
/// <c>GET /api/admin/employee-profiles/{employeeId}</c>, so the schema lookup fails. After TASK-14851's merge and before the Orchestrator's REGEN
/// writes the 404 schema into <c>openapi.json</c> this fact is red, by design; CI-W6 judges it after REGEN.
/// </para>
///
/// <para>
/// <b>Why by hand.</b> <see cref="SpecRuntimeMatcher.ResolveSuccessContract"/> reads success codes only, so the 404 schema is looked up here, its
/// key set, types and nullability are asserted by hand, and the public <see cref="SpecRuntimeMatcher.AssertMatches"/> then matches the real body
/// against it (presence, nullable admission, type kinds).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S148ProfileRecreateSpecRuntimeTests : IAsyncLifetime
{
    private const string SpecPath = "/api/admin/employee-profiles/{employeeId}";

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
    /// <b>F (CI).</b> A GlobalAdmin closes an HK person's profile (204) and reads it: 404, whose body (a) carries exactly the six members
    /// <c>error</c>, <c>canCreate</c>, <c>uncoveredSince</c>, <c>scheduledFrom</c>, <c>lastPartTimeFraction</c>, <c>lastPosition</c> and (b) matches
    /// the committed spec's 404 schema: the schema declares those six properties and no other; <c>error</c> is a string and <c>canCreate</c> a boolean,
    /// neither nullable; <c>uncoveredSince</c> and <c>scheduledFrom</c> are nullable date strings; <c>lastPartTimeFraction</c> a nullable number;
    /// <c>lastPosition</c> a nullable string. The runtime values are non-null for <c>error</c>, <c>canCreate</c>, <c>uncoveredSince</c> and
    /// <c>lastPartTimeFraction</c>, and null for <c>scheduledFrom</c> and <c>lastPosition</c>, so the nullable members are exercised both ways by
    /// the matcher's null-admission rule.
    /// <para><b>Product code between the readings:</b> the close, the GET's 404 read.</para>
    /// </summary>
    [Fact]
    public async Task Get_NoCurrentProfile_404BodyMatchesSpec()
    {
        var person = await TransferWriteTests.SeedPersonAsync(_harness.ConnectionString, "STY01", "HK");
        using var admin = TransferWriteTests.GlobalAdminClient(_factory, "ADMIN_S148_Q268_SPEC");
        var close = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{person}");
        close.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // the seed leaves users.version at 1
        using (var closed = await admin.SendAsync(close))
            Assert.True(closed.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)closed.StatusCode} {await closed.Content.ReadAsStringAsync()}");

        using var response = await admin.GetAsync($"/api/admin/employee-profiles/{person}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"expected 404, got {(int)response.StatusCode}: {body}");
        var runtime = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(runtime, new[]
        {
            "error", "canCreate", "uncoveredSince", "scheduledFrom", "lastPartTimeFraction", "lastPosition",
        }, "profile GET 404");

        var schema = Deref(ResolveNotFoundSchema());
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            new[] { "canCreate", "error", "lastPartTimeFraction", "lastPosition", "scheduledFrom", "uncoveredSince" },
            properties.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        AssertMember(properties, "error", "string", format: null, nullable: false);
        AssertMember(properties, "canCreate", "boolean", format: null, nullable: false);
        AssertMember(properties, "uncoveredSince", "string", format: "date", nullable: true);
        AssertMember(properties, "scheduledFrom", "string", format: "date", nullable: true);
        AssertMember(properties, "lastPartTimeFraction", "number", format: "double", nullable: true);
        AssertMember(properties, "lastPosition", "string", format: null, nullable: true);

        SpecRuntimeMatcher.AssertMatches(_spec, schema, runtime, "profile GET 404");

        Assert.Equal("Employee profile not found", runtime.GetProperty("error").GetString());
        Assert.True(runtime.GetProperty("canCreate").GetBoolean());
        Assert.Equal(JsonValueKind.Null, runtime.GetProperty("scheduledFrom").ValueKind);
        Assert.Equal(JsonValueKind.Null, runtime.GetProperty("lastPosition").ValueKind);
    }

    private JsonElement ResolveNotFoundSchema()
    {
        if (!_spec.TryGetProperty("paths", out var paths) || !paths.TryGetProperty(SpecPath, out var pathItem)
            || !pathItem.TryGetProperty("get", out var op) || !op.TryGetProperty("responses", out var responses))
            throw new XunitException($"The committed spec has no GET {SpecPath} with responses.");
        if (!responses.TryGetProperty("404", out var notFound))
            throw new XunitException($"The committed spec declares no 404 response for GET {SpecPath} (REGEN has not written it yet).");
        if (!notFound.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var media)
            || !media.TryGetProperty("schema", out var schema))
            throw new XunitException($"The committed spec's 404 for GET {SpecPath} has no application/json schema.");
        return schema;
    }

    /// <summary>Follows a <c>#/components/schemas/…</c> reference once (the generator emits the record as a $ref).</summary>
    private JsonElement Deref(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!["#/components/schemas/".Length..];
            return _spec.GetProperty("components").GetProperty("schemas").GetProperty(name);
        }
        return schema;
    }

    private static void AssertMember(JsonElement properties, string name, string type, string? format, bool nullable)
    {
        var member = properties.GetProperty(name);
        Assert.Equal(type, member.GetProperty("type").GetString());
        var actualFormat = member.TryGetProperty("format", out var f) ? f.GetString() : null;
        Assert.Equal(format, actualFormat);
        var actualNullable = member.TryGetProperty("nullable", out var n) && n.GetBoolean();
        Assert.Equal(nullable, actualNullable);
    }
}
