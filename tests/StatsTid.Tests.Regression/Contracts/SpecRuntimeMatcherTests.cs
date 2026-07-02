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

    // ════════════════════════════════════════════════════════════════════════════════
    //  S112 / TASK-11204 — declared-success-status resolution (200/201/204) + status fidelity.
    //  A mini spec with one op per declared-success shape: POST /things (201), GET /things (200
    //  bare array), DELETE /things/{id} (204 no body), PUT /things/{id} (an ILLEGAL double-2xx).
    // ════════════════════════════════════════════════════════════════════════════════

    private const string OpsSpecJson = """
    {
      "paths": {
        "/things": {
          "get": {
            "responses": {
              "200": { "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/OrgItem" } } } } }
            }
          },
          "post": {
            "responses": {
              "201": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OrgItem" } } } }
            }
          }
        },
        "/things/{id}": {
          "delete": {
            "responses": { "204": { "description": "No Content" } }
          },
          "put": {
            "responses": {
              "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OrgItem" } } } },
              "201": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OrgItem" } } } }
            }
          }
        }
      },
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

    private static JsonElement OpsSpec() => JsonDocument.Parse(OpsSpecJson).RootElement;

    [Fact]
    public void Resolves201_AndMatchesA201Response()
    {
        // A create op declares 201 — the contract resolves to 201 + the schema, and a REAL 201
        // response with the declared shape is GREEN.
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things", "post");
        Assert.Equal(201, contract.StatusCode);
        Assert.NotNull(contract.Schema);

        SpecRuntimeMatcher.AssertSuccessMatches(
            spec, contract, 201, """{ "orgId": "A", "orgName": "Alpha", "parentOrgId": null }""", "POST /things");
    }

    [Fact]
    public void Red_OnStatusLie_SpecDeclares200ButRuntimeReturned201()
    {
        // THE required failure mode: the spec declares 200 but the endpoint actually returns 201
        // (e.g. Results.Created behind a .Produces<T>(200)) — a status-code mismatch is RED even
        // when the BODY would match the schema perfectly.
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things", "get");
        Assert.Equal(200, contract.StatusCode);

        var ex = Assert.Throws<XunitException>(() => SpecRuntimeMatcher.AssertSuccessMatches(
            spec, contract, 201, """[ { "orgId": "A", "orgName": "Alpha", "parentOrgId": null } ]""", "GET /things"));
        Assert.Contains("status", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("201", ex.Message);
    }

    [Fact]
    public void Resolves204_NoSchema_AndEmptyBodyIsGreen()
    {
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things/{id}", "delete");
        Assert.Equal(204, contract.StatusCode);
        Assert.Null(contract.Schema);

        // A real 204 with an empty body is GREEN (both null and "" count as empty).
        SpecRuntimeMatcher.AssertSuccessMatches(spec, contract, 204, "", "DELETE /things/{id}");
        SpecRuntimeMatcher.AssertSuccessMatches(spec, contract, 204, null, "DELETE /things/{id}");
    }

    [Fact]
    public void Red_WhenDeclared204ResponseCarriesABody()
    {
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things/{id}", "delete");
        var ex = Assert.Throws<XunitException>(() => SpecRuntimeMatcher.AssertSuccessMatches(
            spec, contract, 204, """{ "unexpected": true }""", "DELETE /things/{id}"));
        Assert.Contains("204", ex.Message);
        Assert.Contains("body", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Red_WhenDeclared204ButRuntimeReturned200()
    {
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things/{id}", "delete");
        var ex = Assert.Throws<XunitException>(() => SpecRuntimeMatcher.AssertSuccessMatches(
            spec, contract, 200, "", "DELETE /things/{id}"));
        Assert.Contains("status", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Red_WhenOpDeclaresMultiple2xxResponses()
    {
        // Ambiguity guard: the gate needs exactly ONE declared success status per operation.
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.ResolveSuccessContract(OpsSpec(), "/things/{id}", "put"));
        Assert.Contains("MULTIPLE", ex.Message);
    }

    [Fact]
    public void Resolve200Schema_BackwardCompatible_ResolvesA200Op_AndThrowsOnA201Op()
    {
        // The S111 surface still resolves a 200-declared op unchanged…
        var spec = OpsSpec();
        var schema = SpecRuntimeMatcher.Resolve200Schema(spec, "/things", "get");
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);

        // …and refuses an op whose declared success is 201 (use ResolveSuccessContract there).
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.Resolve200Schema(spec, "/things", "post"));
        Assert.Contains("201", ex.Message);
    }
}
