using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using StatsTid.Backend.Api;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StatsTid.Tests.Unit.OpenApi;

// ── Probe records (the synthesized closure) ──────────────────────────────────────────────
// Deliberate analogues of the real Contracts/ closure shapes:
//  - FilterProbeResponse.MaybeChild  ≙ RosterEmployeeRow.OutgoingVikar (CLR-nullable complex →
//    bare $ref → the nullable-$ref exception: NOT required)
//  - FilterProbeResponse.SometimesAbsent ≙ the VacationSettlementSnapshot future
//    ([JsonIgnore(WhenWritingNull)] → can be ABSENT from the wire → NOT required)
//  - FilterProbeResponse.Status ≙ periodStatus ([AllowedValues] → spec enum)
//  - FilterProbeShared ≙ the (today hypothetical) request∩response overlap — response truth applies.

internal sealed record FilterProbeGrandChild(string Tag);

internal sealed record FilterProbeChild(string Value, FilterProbeGrandChild GrandChild);

internal sealed record FilterProbeResponse(
    string Name,
    string? Note,
    FilterProbeChild Child,
    FilterProbeChild? MaybeChild,
    IReadOnlyList<string> Tags,
    [property: AllowedValues("OPEN", "CLOSED")] string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SometimesAbsent);

internal sealed record FilterProbeRequestOnly(
    string Name,
    [property: AllowedValues("X", "Y")] string Kind);

internal sealed record FilterProbeErrorShape(string Message);

internal sealed record FilterProbeShared(string Id, string? Label);

/// <summary>
/// S113 / TASK-11302 — unit pins on <see cref="ResponseStrictTypesFilter"/> (the strict-types
/// spec filter, TASK-11300): the response-reachable closure derivation + the required/enum
/// emission rules, exercised through the REAL Swashbuckle <see cref="SchemaGenerator"/> with the
/// production generator options mirrored from Program.cs (<c>SupportNonNullableReferenceTypes</c>,
/// FullName schema ids, camelCase Web serializer contract, the filter registered as the
/// ISchemaFilter half) and the document-filter half applied over a synthesized
/// <see cref="OpenApiDocument"/>. Non-Docker (pure in-memory generation), so the filter's policy
/// surface is provable without standing infra; the committed-spec + per-route Docker gates
/// (<c>OpenApiSpecRuntimeTests</c> / <c>S112*SpecRuntimeTests</c>) prove the same rules against
/// the real 28-schema closure.
/// </summary>
public sealed class ResponseStrictTypesFilterTests
{
    // Built ONCE (generation + document-filter application are deterministic and read-only after).
    private static readonly Lazy<OpenApiDocument> Doc = new(BuildAndApply);

    private static string Id<T>() => typeof(T).FullName!.Replace("+", ".");
    private static OpenApiSchema SchemaOf<T>() => Doc.Value.Components.Schemas[Id<T>()];
    private static OpenApiSchema Ref(string id) =>
        new() { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = id } };
    private static OpenApiResponse JsonResponse(OpenApiSchema schema) => new()
    {
        Description = "ok",
        Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = schema } },
    };

    private static OpenApiDocument BuildAndApply()
    {
        var filter = new ResponseStrictTypesFilter();

        // Mirror the Program.cs AddSwaggerGen generator config (the parts that shape schemas):
        // SupportNonNullableReferenceTypes + FullName schema ids + the Web (camelCase) serializer
        // contract + the filter as the ISchemaFilter half (records schemaId → CLR type).
        var generatorOptions = new SchemaGeneratorOptions
        {
            SchemaIdSelector = t => t.FullName!.Replace("+", "."),
            SupportNonNullableReferenceTypes = true,
        };
        generatorOptions.SchemaFilters.Add(filter);
        var generator = new SchemaGenerator(
            generatorOptions,
            new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var repository = new SchemaRepository("v1");

        var responseRef = generator.GenerateSchema(typeof(FilterProbeResponse), repository);
        var requestOnlyRef = generator.GenerateSchema(typeof(FilterProbeRequestOnly), repository);
        var errorRef = generator.GenerateSchema(typeof(FilterProbeErrorShape), repository);
        var sharedRef = generator.GenerateSchema(typeof(FilterProbeShared), repository);

        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                // Response root (200) + request-only body + a NON-2xx (422) ref — only the 200
                // side may enter the closure.
                ["/probe"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Post] = new OpenApiOperation
                        {
                            RequestBody = new OpenApiRequestBody
                            {
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/json"] = new() { Schema = requestOnlyRef },
                                },
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = JsonResponse(responseRef),
                                ["422"] = JsonResponse(errorRef),
                            },
                        },
                    },
                },
                // The OVERLAP op: the SAME schema referenced from BOTH the requestBody and the
                // 2xx response (disjoint in the real spec today — this pins the documented policy).
                ["/probe/shared"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Put] = new OpenApiOperation
                        {
                            RequestBody = new OpenApiRequestBody
                            {
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/json"] = new() { Schema = sharedRef },
                                },
                            },
                            Responses = new OpenApiResponses { ["200"] = JsonResponse(sharedRef) },
                        },
                    },
                },
                // A closure-reachable schema with NO recorded CLR mapping (hand-added below, never
                // seen by the ISchemaFilter half) — the fail-conservative skip path.
                ["/probe/unmapped"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses { ["200"] = JsonResponse(Ref("UnmappedShape")) },
                        },
                    },
                },
            },
            Components = new OpenApiComponents { Schemas = repository.Schemas },
        };

        document.Components.Schemas["UnmappedShape"] = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema> { ["field"] = new() { Type = "string" } },
        };

        filter.Apply(document, new DocumentFilterContext(Array.Empty<ApiDescription>(), generator, repository));
        return document;
    }

    // ────────────────────────────── (1) the closure derivation ──────────────────────────────

    [Fact]
    public void ResponseReferencedSchema_GetsRequired_OnEveryUnexceptedMember()
    {
        // required = ALL members EXCEPT the CLR-nullable $ref (maybeChild) and the
        // conditionally-ignored member (sometimesAbsent). Alphabetically sorted (SortedSet —
        // the committed spec's house style). NOTE: the plain-nullable scalar `note` IS required
        // ("always present, possibly JSON null" → TS `string | null`, non-optional).
        Assert.Equal(
            new[] { "child", "name", "note", "status", "tags" },
            SchemaOf<FilterProbeResponse>().Required);
    }

    [Fact]
    public void Closure_IsTransitive_NestedAndDepth2SchemasGetRequiredToo()
    {
        // FilterProbeChild is reached only THROUGH the response root; FilterProbeGrandChild only
        // through FilterProbeChild (depth 2) — both are in the closure and get full required.
        Assert.Equal(new[] { "grandChild", "value" }, SchemaOf<FilterProbeChild>().Required);
        Assert.Equal(new[] { "tag" }, SchemaOf<FilterProbeGrandChild>().Required);
    }

    [Fact]
    public void RequestOnlySchema_IsNotTouched_NoRequiredAndNoEnumLeak()
    {
        // Request DTO schemas carry the binder-enforced (C# `required` keyword) semantic — this
        // filter must not overwrite or extend them. FilterProbeRequestOnly has no such members,
        // so required stays EMPTY; and its [AllowedValues] member must NOT leak an enum (enum
        // emission is closure-gated by construction).
        var schema = SchemaOf<FilterProbeRequestOnly>();
        Assert.True(schema.Required is null || schema.Required.Count == 0,
            $"Request-only schema unexpectedly got required: [{string.Join(", ", schema.Required ?? new HashSet<string>())}]");
        var kind = schema.Properties["kind"];
        Assert.True(kind.Enum is null || kind.Enum.Count == 0,
            "Request-only [AllowedValues] member leaked an enum into a request schema.");
    }

    [Fact]
    public void NonSuccessResponseSchema_IsNotInTheClosure()
    {
        // FilterProbeErrorShape is referenced ONLY from a 422 response — the closure roots are
        // 2xx content only, so it is untouched.
        var schema = SchemaOf<FilterProbeErrorShape>();
        Assert.True(schema.Required is null || schema.Required.Count == 0,
            "A schema referenced only from a NON-2xx response entered the closure.");
    }

    [Fact]
    public void UnmappableClosureSchema_IsSkippedFailConservative()
    {
        // In the closure (2xx-referenced) but with NO recorded schemaId→CLR mapping — the filter
        // must SKIP it (no guessed required), never invent claims it cannot verify.
        var schema = Doc.Value.Components.Schemas["UnmappedShape"];
        Assert.True(schema.Required is null || schema.Required.Count == 0,
            "The fail-conservative skip regressed: an unmappable closure schema got required.");
    }

    // ─────────────────────────── (2) the conditional-ignore skip ────────────────────────────

    [Fact]
    public void ConditionallyIgnoredMember_IsNeverRequired()
    {
        // [JsonIgnore(WhenWritingNull)] → the member can be ABSENT from the wire (not just null)
        // — claiming it required would be the exact lie the strict phase exists to kill. This is
        // the VacationSettlementSnapshot future: RED here if it is ever claimed required.
        var schema = SchemaOf<FilterProbeResponse>();
        Assert.Contains("sometimesAbsent", schema.Properties.Keys); // still IN the schema…
        Assert.DoesNotContain("sometimesAbsent", schema.Required);  // …but never required.
    }

    // ─────────────────────────── (3) the nullable-$ref exception ────────────────────────────

    [Fact]
    public void ClrNullableComplexMember_EmitsAsBareRef_AndIsNotRequired()
    {
        // The RosterEmployeeRow.outgoingVikar analogue: a CLR-nullable complex member emits as a
        // bare $ref (OpenAPI 3.0 cannot carry nullable on a $ref; Swashbuckle drops the flag) —
        // marking it required would over-claim never-null to the generated TS. Excluded.
        var schema = SchemaOf<FilterProbeResponse>();
        Assert.NotNull(schema.Properties["maybeChild"].Reference); // a bare $ref, as production emits
        Assert.DoesNotContain("maybeChild", schema.Required);

        // The NON-nullable complex member is the control: also a bare $ref, but required.
        Assert.NotNull(schema.Properties["child"].Reference);
        Assert.Contains("child", schema.Required);
    }

    // ─────────────────────────── (4) [AllowedValues] → enum emission ────────────────────────

    [Fact]
    public void AllowedValues_OnAClosureMember_EmitsTheEnumSet()
    {
        var status = SchemaOf<FilterProbeResponse>().Properties["status"];
        Assert.NotNull(status.Enum);
        Assert.Equal(
            new[] { "OPEN", "CLOSED" },
            status.Enum.Select(v => Assert.IsType<OpenApiString>(v).Value));
    }

    // ────────────────────────────── (5) the overlap policy ──────────────────────────────────

    [Fact]
    public void SchemaReferencedFromBothRequestAndResponse_GetsResponseTruthRequired()
    {
        // The documented OVERLAP POLICY: a schema reachable from BOTH a requestBody and a 2xx
        // response is in the closure and gets the response-truth required (all members — the
        // binder-enforced request subset is necessarily contained; over-strict toward SENDING,
        // which fails safe as an FE compile error).
        Assert.Equal(new[] { "id", "label" }, SchemaOf<FilterProbeShared>().Required);
    }
}
