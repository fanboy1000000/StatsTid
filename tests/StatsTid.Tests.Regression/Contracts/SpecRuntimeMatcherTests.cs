using System.Text.Json;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S111 / TASK-11101 — proves the <see cref="SpecRuntimeMatcher"/> is RED on the divergences the
/// per-route gate must catch (above all the array-ness LIE), and GREEN on a faithful spec. NON-Docker
/// (pure JSON), so the gate's closure is provable without standing infra — the companion
/// <see cref="OpenApiSpecRuntimeTests"/> runs it against the REAL committed spec + REAL responses.
/// </summary>
public sealed class SpecRuntimeMatcherTests
{
    // A tiny spec: components.OrgItem (object) used both as a bare-array element AND, in the lie cases,
    // as a direct object response — exactly the .Produces<OrgListItem> vs .Produces<IEnumerable<…>> fork.
    private const string SpecJson = """
    {
      "components": {
        "schemas": {
          "OrgItem": {
            "type": "object",
            "properties": {
              "orgId":       { "type": "string" },
              "orgName":     { "type": "string" },
              "parentOrgId": { "type": "string", "nullable": true }
            }
          }
        }
      }
    }
    """;

    private static JsonElement Spec() => JsonDocument.Parse(SpecJson).RootElement;
    private static JsonElement Json(string j) => JsonDocument.Parse(j).RootElement;
    private static JsonElement ArraySchema() => Json("""{ "type": "array", "items": { "$ref": "#/components/schemas/OrgItem" } }""");
    private static JsonElement ObjectSchema() => Json("""{ "$ref": "#/components/schemas/OrgItem" }""");

    [Fact]
    public void Green_WhenBareArraySchemaMatchesArrayResponse()
    {
        var spec = Spec();
        var response = Json("""[ { "orgId": "A", "orgName": "Alpha", "parentOrgId": null } ]""");
        // No throw == green. parentOrgId null is allowed (nullable:true scalar).
        SpecRuntimeMatcher.AssertMatches(spec, ArraySchema(), response, "GET /orgs");
    }

    [Fact]
    public void Red_OnArraynessLie_ObjectSchemaVsArrayRuntime()
    {
        // .Produces<OrgListItem> (object) while the runtime returns a bare ARRAY — the exact lie.
        var spec = Spec();
        var response = Json("""[ { "orgId": "A", "orgName": "Alpha", "parentOrgId": null } ]""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, ObjectSchema(), response, "GET /orgs"));
        Assert.Contains("array-ness", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Red_OnArraynessLie_ArraySchemaVsObjectRuntime()
    {
        // The inverse: .Produces<IEnumerable<T>> while the runtime returns a single OBJECT.
        var spec = Spec();
        var response = Json("""{ "orgId": "A", "orgName": "Alpha", "parentOrgId": null }""");
        Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, ArraySchema(), response, "GET /orgs"));
    }

    [Fact]
    public void Red_WhenSchemaDeclaresAFieldTheRuntimeOmits()
    {
        var spec = Spec();
        var response = Json("""[ { "orgId": "A", "parentOrgId": null } ]"""); // orgName dropped
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, ArraySchema(), response, "GET /orgs"));
        Assert.Contains("orgName", ex.Message);
    }

    [Fact]
    public void Red_WhenNonNullableScalarIsServedNull()
    {
        var spec = Spec();
        var response = Json("""[ { "orgId": "A", "orgName": null, "parentOrgId": null } ]"""); // orgName non-nullable
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, ArraySchema(), response, "GET /orgs"));
        Assert.Contains("NON-nullable", ex.Message);
    }

    [Fact]
    public void Red_WhenSchemaKeyIsNotCamelCase()
    {
        // Simulate a Swashbuckle-casing regression (PascalCase schema key) against a camelCase runtime.
        var spec = Json("""
        {
          "components": { "schemas": { "OrgItem": {
            "type": "object",
            "properties": { "OrgId": { "type": "string" } }
          } } }
        }
        """);
        var response = Json("""[ { "orgId": "A" } ]""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, ArraySchema(), response, "GET /orgs"));
        Assert.Contains("camelCase", ex.Message);
    }
}
