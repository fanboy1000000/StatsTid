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
    //  Extended S115 / TASK-11500 — the homogeneous multi-2xx contract. One op per declared-success
    //  shape: POST /things (201), GET /things (200 bare array), DELETE /things/{id} (204 no body),
    //  PUT /things/{id} (a LEGAL homogeneous 200+201 — ONE shared $ref, the reporting-line
    //  first-assign-201/reassign-200 case), PATCH /things/{id} (an ILLEGAL double-2xx: two DIFFERENT
    //  $refs), POST /things/{id}/archive (an ILLEGAL 204+200 pair — heterogeneous BY CONTENT),
    //  PUT /things/{id}/label (an ILLEGAL pair of INLINE schemas — identical, still rejected).
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
          },
          "patch": {
            "responses": {
              "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OrgItem" } } } },
              "201": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OtherItem" } } } }
            }
          }
        },
        "/things/{id}/archive": {
          "post": {
            "responses": {
              "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/OrgItem" } } } },
              "204": { "description": "No Content" }
            }
          }
        },
        "/things/{id}/label": {
          "put": {
            "responses": {
              "200": { "content": { "application/json": { "schema": { "type": "object", "properties": { "label": { "type": "string" } } } } } },
              "201": { "content": { "application/json": { "schema": { "type": "object", "properties": { "label": { "type": "string" } } } } } }
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
          },
          "OtherItem": {
            "type": "object",
            "properties": {
              "otherId": { "type": "string" }
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
        Assert.Equal(new[] { 201 }, contract.StatusCodes);
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
        Assert.Equal(new[] { 200 }, contract.StatusCodes);

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
        Assert.Equal(new[] { 204 }, contract.StatusCodes);
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
    public void Red_WhenMulti2xxDeclaresDifferentSchemaRefs()
    {
        // The surviving ambiguity guard (S115 update of the S112 blanket rejection): a multi-2xx set
        // whose members carry DIFFERENT $refs is heterogeneous — still REJECTED at resolution.
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.ResolveSuccessContract(OpsSpec(), "/things/{id}", "patch"));
        Assert.Contains("MULTIPLE", ex.Message);
        Assert.Contains("DIFFERENT", ex.Message);
        Assert.Contains("OrgItem", ex.Message);
        Assert.Contains("OtherItem", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S115 / TASK-11500 — the HOMOGENEOUS conditional-status extension: multiple declared 2xx is
    //  acceptable IFF every declared 2xx carries the SAME schema $ref (the 201-or-200-from-ONE-shape
    //  reporting-line assigns). Heterogeneous sets stay REJECTED; an undeclared runtime status
    //  stays RED.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Green_WhenMulti2xxSharesOneSchemaRef_BothDeclaredStatusesMatch()
    {
        // The legal case: PUT declares 200 AND 201, both carrying the SAME $ref — the contract
        // resolves to the declared-status SET + the one shared schema, and a runtime response with
        // EITHER declared status and the shared shape is GREEN (first-assign 201 / reassign 200).
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things/{id}", "put");
        Assert.Equal(new[] { 200, 201 }, contract.StatusCodes);
        Assert.NotNull(contract.Schema);

        var body = """{ "orgId": "A", "orgName": "Alpha", "parentOrgId": null }""";
        SpecRuntimeMatcher.AssertSuccessMatches(spec, contract, 200, body, "PUT /things/{id}"); // the reassign branch
        SpecRuntimeMatcher.AssertSuccessMatches(spec, contract, 201, body, "PUT /things/{id}"); // the first-assign branch
    }

    [Fact]
    public void Red_WhenMulti2xxPairs204NoContentWithA200Schema()
    {
        // Heterogeneous BY CONTENT (not just by $ref): a no-body 204 can never share a schema with a
        // body-bearing 200 — the pair is REJECTED at resolution.
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.ResolveSuccessContract(OpsSpec(), "/things/{id}/archive", "post"));
        Assert.Contains("204", ex.Message);
        Assert.Contains("MULTIPLE", ex.Message);
    }

    [Fact]
    public void Red_WhenMulti2xxSchemasAreInline()
    {
        // Two INLINE (non-$ref) schemas — STRUCTURALLY IDENTICAL, still rejected conservatively:
        // records always emit $ref, so an inline schema on a multi-2xx op is the tripwire against
        // inline-schema drift.
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.ResolveSuccessContract(OpsSpec(), "/things/{id}/label", "put"));
        Assert.Contains("INLINE", ex.Message);
        Assert.Contains("$ref", ex.Message);
    }

    [Fact]
    public void Red_WhenRuntimeStatusIsUndeclaredOnAMulti2xxOp()
    {
        // Status fidelity survives the extension: the multi-2xx contract admits ONLY its declared
        // members — a 202 against a declared {200, 201} is RED even with a perfectly matching body.
        var spec = OpsSpec();
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, "/things/{id}", "put");
        var ex = Assert.Throws<XunitException>(() => SpecRuntimeMatcher.AssertSuccessMatches(
            spec, contract, 202, """{ "orgId": "A", "orgName": "Alpha", "parentOrgId": null }""", "PUT /things/{id}"));
        Assert.Contains("status", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("202", ex.Message);
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  S113 / TASK-11302 — REQUIRED-fidelity + ENUM-fidelity (the strict-types phase).
    //  A strict schema shaped like the real closure output of ResponseStrictTypesFilter:
    //  `required` populated (ALL members — S117 fired the nullable-$ref escalation, so the
    //  CLR-nullable complex `detail` — the RosterEmployeeRow.outgoingVikar analogue — now
    //  carries the nullable-complex WRAPPER form [type: object + allOf: [$ref] +
    //  nullable: true] and IS required) + a string discriminator `enum` (the periodStatus
    //  analogue) in both a NON-nullable and a nullable flavor.
    // ════════════════════════════════════════════════════════════════════════════════

    private const string StrictSpecJson = """
    {
      "components": {
        "schemas": {
          "StrictItem": {
            "type": "object",
            "required": [ "detail", "id", "status", "maybeStatus", "note" ],
            "properties": {
              "id":          { "type": "string" },
              "status":      { "type": "string", "enum": [ "OPEN", "SUBMITTED", "APPROVED" ] },
              "maybeStatus": { "type": "string", "enum": [ "A", "B" ], "nullable": true },
              "note":        { "type": "string", "nullable": true },
              "detail":      { "type": "object", "allOf": [ { "$ref": "#/components/schemas/StrictDetail" } ], "nullable": true }
            }
          },
          "StrictDetail": {
            "type": "object",
            "required": [ "name" ],
            "properties": { "name": { "type": "string" } }
          }
        }
      }
    }
    """;

    private static JsonElement StrictSpec() => JsonDocument.Parse(StrictSpecJson).RootElement;
    private static JsonElement StrictSchema() => Json("""{ "$ref": "#/components/schemas/StrictItem" }""");

    [Fact]
    public void Green_WhenRequiredMembersPresent_AndEnumValuesInSet()
    {
        // The faithful case: every required member on the wire (note null-but-PRESENT satisfies
        // required — "always present, possibly JSON null"), enum values in-set, and the S117
        // nullable-complex WRAPPER member `detail` (required, nullable:true at the property level)
        // serving a non-null object — recursed THROUGH the wrapper into StrictDetail (whose own
        // `required: [name]` is enforced; the dedicated S117 section below proves both directions).
        var spec = StrictSpec();
        var response = Json("""{ "id": "1", "status": "OPEN", "maybeStatus": null, "note": null, "detail": { "name": "d" } }""");
        SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict");
    }

    [Fact]
    public void Red_WhenRequiredMemberIsAbsentFromRuntime()
    {
        // required lists "note" but the wire omits the KEY entirely (≠ serving null) — the exact
        // claim `required` makes to the generated TS, lying. RED with the member name.
        var spec = StrictSpec();
        var response = Json("""{ "id": "1", "status": "OPEN", "maybeStatus": "A", "detail": null }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict"));
        Assert.Contains("note", ex.Message);
        Assert.Contains("REQUIRED", ex.Message);
    }

    [Fact]
    public void Red_WhenRequiredListsAMemberOutsideProperties()
    {
        // A `required` entry with NO matching `properties` declaration is still enforced (the
        // required walk is independent of the property-presence walk).
        var spec = Json("""
        {
          "components": { "schemas": { "StrictItem": {
            "type": "object",
            "required": [ "ghost" ],
            "properties": { "id": { "type": "string" } }
          } } }
        }
        """);
        var response = Json("""{ "id": "1" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict"));
        Assert.Contains("ghost", ex.Message);
        Assert.Contains("REQUIRED", ex.Message);
    }

    [Fact]
    public void Red_WhenEnumValueIsOutOfSet()
    {
        // A NON-NULL out-of-set discriminator — the generated TS literal union would lie. RED.
        var spec = StrictSpec();
        var response = Json("""{ "id": "1", "status": "REJECTED", "maybeStatus": null, "note": null, "detail": null }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict"));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REJECTED", ex.Message);
    }

    [Fact]
    public void Red_WhenEnumValueIsOutOfSet_BehindADollarRef()
    {
        // The enum check runs AFTER deref — an enum declared on a $ref'd component schema is
        // enforced the same as an inline one.
        var spec = Json("""
        {
          "components": { "schemas": {
            "StrictItem": {
              "type": "object",
              "properties": { "kind": { "$ref": "#/components/schemas/KindEnum" } }
            },
            "KindEnum": { "type": "string", "enum": [ "MAO", "ORGANISATION" ] }
          } }
        }
        """);
        var response = Json("""{ "kind": "ENHED" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict"));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ENHED", ex.Message);
    }

    [Fact]
    public void Green_WhenNullableEnumMemberIsNull()
    {
        // THE null rule: null is admissible IFF the member is nullable — enum membership never
        // judges null. maybeStatus is nullable:true with enum [A,B] (null NOT in the set) → green.
        var spec = StrictSpec();
        var response = Json("""{ "id": "1", "status": "APPROVED", "maybeStatus": null, "note": "n", "detail": null }""");
        SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict");
    }

    [Fact]
    public void Red_WhenNonNullableEnumMemberIsNull()
    {
        // The inverse of the null rule: status is a NON-nullable enum scalar — a served null is RED
        // (via the nullable-required check; the enum set never legitimizes a null).
        var spec = StrictSpec();
        var response = Json("""{ "id": "1", "status": null, "maybeStatus": "A", "note": null, "detail": null }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, StrictSchema(), response, "GET /strict"));
        Assert.Contains("NON-nullable", ex.Message);
        Assert.Contains("status", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S117 / TASK-11700 — the nullable-complex WRAPPER mechanism (the fired S113
    //  nullable-$ref escalation). A fixture shaped like the filter's post-S117 output: the
    //  CLR-nullable complex `maybeInner` = the wrapper form (type: object + allOf: [$ref] +
    //  nullable: true, REQUIRED); the never-nullable complex `inner` = a bare $ref (also
    //  required — the never-null claim). The wrapped schema carries its own `required` AND
    //  an `enum` member so the recursion proof exercises the REAL fidelity path (Step-0b
    //  Codex pin: a shallow kind check could pass a partial recursion).
    //
    //  S117 Step-7a fail-closed additions (W1/W2 + NOTE-1), pinned by the four extra root
    //  schemas: NeverNullWrapperItem (a wrapper WITHOUT nullable — a never-null claim;
    //  serving null is RED, serving a valid object is green), TwoElementAllOfItem and
    //  AllOfWithPropertiesItem (IMPURE allOf compositions — resolution THROWS with the named
    //  impurity instead of walking as an empty object, the vacuous-pass hazard), and
    //  EnumOnWrapperItem (a validation keyword ON the wrapper node — resolution would
    //  discard it silently, so it too FAILS CLOSED).
    // ════════════════════════════════════════════════════════════════════════════════

    private const string WrapperSpecJson = """
    {
      "components": {
        "schemas": {
          "WrapperItem": {
            "type": "object",
            "required": [ "id", "inner", "maybeInner" ],
            "properties": {
              "id":         { "type": "string" },
              "inner":      { "$ref": "#/components/schemas/InnerDetail" },
              "maybeInner": { "type": "object", "allOf": [ { "$ref": "#/components/schemas/InnerDetail" } ], "nullable": true }
            }
          },
          "InnerDetail": {
            "type": "object",
            "required": [ "name", "kind" ],
            "properties": {
              "name": { "type": "string" },
              "kind": { "type": "string", "enum": [ "ALPHA", "BETA" ] }
            }
          },
          "NeverNullWrapperItem": {
            "type": "object",
            "required": [ "wrapped" ],
            "properties": {
              "wrapped": { "type": "object", "allOf": [ { "$ref": "#/components/schemas/InnerDetail" } ] }
            }
          },
          "TwoElementAllOfItem": {
            "type": "object",
            "properties": {
              "wrapped": { "allOf": [ { "$ref": "#/components/schemas/InnerDetail" }, { "type": "object" } ], "nullable": true }
            }
          },
          "AllOfWithPropertiesItem": {
            "type": "object",
            "properties": {
              "wrapped": { "type": "object", "allOf": [ { "$ref": "#/components/schemas/InnerDetail" } ], "nullable": true,
                           "properties": { "extra": { "type": "string" } } }
            }
          },
          "EnumOnWrapperItem": {
            "type": "object",
            "properties": {
              "wrapped": { "type": "object", "allOf": [ { "$ref": "#/components/schemas/InnerDetail" } ], "nullable": true,
                           "enum": [ "X" ] }
            }
          }
        }
      }
    }
    """;

    private static JsonElement WrapperSpec() => JsonDocument.Parse(WrapperSpecJson).RootElement;
    private static JsonElement WrapperSchema() => Json("""{ "$ref": "#/components/schemas/WrapperItem" }""");

    [Fact]
    public void Green_WrappedMemberServingNullAndServingAValidObject_BothPass()
    {
        // The green direction: the wrapper's property-level nullable:true admits null, and a
        // valid object resolves THROUGH the wrapper (both branches of the null-or-object
        // envelope — the activeVikar/outgoingVikar retro-close semantics).
        var spec = WrapperSpec();
        var valid = """{ "name": "n", "kind": "ALPHA" }""";
        SpecRuntimeMatcher.AssertMatches(spec, WrapperSchema(),
            Json($$"""{ "id": "1", "inner": {{valid}}, "maybeInner": null }"""), "GET /wrapped");
        SpecRuntimeMatcher.AssertMatches(spec, WrapperSchema(),
            Json($$"""{ "id": "1", "inner": {{valid}}, "maybeInner": {{valid}} }"""), "GET /wrapped");
    }

    [Fact]
    public void Red_WhenBareRefMemberIsServedNull()
    {
        // THE S117 structural-null policy flip: `inner` is a BARE $ref — post-S117 that is a
        // never-null claim (truthful nullability on a complex member always carries the wrapper),
        // so the runtime serving null is RED. Pre-S117 this exact case was silently permitted
        // ("a null there is permitted, no recursion").
        var spec = WrapperSpec();
        var response = Json("""{ "id": "1", "inner": null, "maybeInner": null }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, WrapperSchema(), response, "GET /wrapped"));
        Assert.Contains("inner", ex.Message);
        Assert.Contains("bare $ref", ex.Message);
    }

    [Fact]
    public void Red_WhenInnerRequiredMemberIsMissing_ThroughTheWrapper()
    {
        // The recursion proof, direction 1 (the W1 vacuous-pass hazard): a MISSING REQUIRED
        // member INSIDE the wrapped object must be RED — if Deref did not resolve through the
        // wrapper, the walk would see an empty `type: object`, check nothing, and pass silently.
        var spec = WrapperSpec();
        var response = Json("""{ "id": "1", "inner": { "name": "n", "kind": "ALPHA" }, "maybeInner": { "kind": "BETA" } }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, WrapperSchema(), response, "GET /wrapped"));
        Assert.Contains("name", ex.Message);
        Assert.Contains("REQUIRED", ex.Message);
        Assert.Contains("maybeInner", ex.Message); // the failure is INSIDE the wrapped member's context
    }

    [Fact]
    public void Red_WhenInnerEnumValueIsOutOfSet_ThroughTheWrapper()
    {
        // The recursion proof, direction 2: an OUT-OF-SET enum value on a member INSIDE the
        // wrapped object must be RED — together with the missing-required case this proves the
        // FULL Match() fidelity path executes behind the wrapper (not a shallow kind check).
        var spec = WrapperSpec();
        var response = Json("""{ "id": "1", "inner": { "name": "n", "kind": "ALPHA" }, "maybeInner": { "name": "m", "kind": "GAMMA" } }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, WrapperSchema(), response, "GET /wrapped"));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GAMMA", ex.Message);
    }

    // ─────────────────────────── S117 Step-7a — the fail-closed pair ───────────────────────────

    [Fact]
    public void Red_WhenNeverNullWrapperMemberIsServedNull_AndGreenOnAValidObject()
    {
        // W1: a WRAPPER-SHAPED member (allOf) WITHOUT nullable:true is a never-null claim exactly
        // like a bare $ref — the runtime serving null must not slide through the permissive
        // structural branch. RED on null…
        var spec = WrapperSpec();
        var schema = Json("""{ "$ref": "#/components/schemas/NeverNullWrapperItem" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, schema, Json("""{ "wrapped": null }"""), "GET /neverNull"));
        Assert.Contains("wrapped", ex.Message);
        Assert.Contains("never-null", ex.Message);
        Assert.Contains("allOf", ex.Message);

        // …and green on a valid object — proving the RED above is the NULL, not the wrapper shape
        // (the pure non-nullable wrapper still resolves through and recurses normally).
        SpecRuntimeMatcher.AssertMatches(spec, schema,
            Json("""{ "wrapped": { "name": "n", "kind": "ALPHA" } }"""), "GET /neverNull");
    }

    [Fact]
    public void Throws_WhenAllOfHasTwoElements_TheImpureCompositionFailsClosed()
    {
        // W2: a 2-element allOf is NOT the S117 pure wrapper — the filter never emits it, so it is
        // spec drift or a foreign emission. Resolution must THROW (naming the impurity), never
        // fall through to the empty-object walk that would vacuous-pass every inner check.
        var spec = WrapperSpec();
        var schema = Json("""{ "$ref": "#/components/schemas/TwoElementAllOfItem" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, schema,
                Json("""{ "wrapped": { "name": "n", "kind": "ALPHA" } }"""), "GET /impure"));
        Assert.Contains("2-element allOf", ex.Message);
        Assert.Contains("FAILS CLOSED", ex.Message);
    }

    [Fact]
    public void Throws_WhenAllOfCarriesOwnProperties_TheImpureCompositionFailsClosed()
    {
        // W2: allOf ALONGSIDE the schema's own `properties` is equally impure — a hybrid the
        // matcher cannot walk truthfully (which half owns required/enum?). THROW with the named
        // impurity; never a silent partial walk.
        var spec = WrapperSpec();
        var schema = Json("""{ "$ref": "#/components/schemas/AllOfWithPropertiesItem" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, schema,
                Json("""{ "wrapped": { "name": "n", "kind": "ALPHA", "extra": "e" } }"""), "GET /impure"));
        Assert.Contains("own properties", ex.Message);
        Assert.Contains("FAILS CLOSED", ex.Message);
    }

    [Fact]
    public void Throws_WhenTheWrapperNodeCarriesAnEnum_TheDiscardedKeywordFailsClosed()
    {
        // W2 / NOTE-1: resolution DISCARDS the wrapper node, so an `enum` (or `required`) stamped
        // ON the wrapper — e.g. an [AllowedValues] that landed on a wrapped member — would be
        // silently UNENFORCED if the wrapper resolved through. An otherwise-PURE wrapper carrying
        // a validation keyword must therefore THROW (move the keyword to the inner schema), even
        // when the served value is a perfectly valid inner object.
        var spec = WrapperSpec();
        var schema = Json("""{ "$ref": "#/components/schemas/EnumOnWrapperItem" }""");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(spec, schema,
                Json("""{ "wrapped": { "name": "n", "kind": "ALPHA" } }"""), "GET /impure"));
        Assert.Contains("validation keywords", ex.Message);
        Assert.Contains("inner schema", ex.Message);
        Assert.Contains("FAILS CLOSED", ex.Message);
    }
}
