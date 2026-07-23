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
//    S117: the nullable-complex WRAPPER [type: object + allOf: [$ref] + nullable: true] AND
//    required — the fired S113 nullable-$ref escalation)
//  - FilterProbeResponse.SometimesAbsent ≙ the VacationSettlementSnapshot future
//    ([JsonIgnore(WhenWritingNull)] → can be ABSENT from the wire → NOT required)
//  - FilterProbeResponse.Status ≙ periodStatus ([AllowedValues] → spec enum)
//  - FilterProbeShared ≙ the (today hypothetical) request∩response overlap — response truth applies.
//  - FilterProbeSeriesResponse.MaybeNumbers ≙ YearOverviewCategory.Saldo (CLR-nullable-ELEMENT
//    collection → S120: items.nullable: true — the nullable-ITEMS sibling, fixed at first firing
//    because the S120 ruling-#2 runtime pin REDs on the never-null items claim), with non-nullable /
//    nested / complex-element siblings pinning the negative control, the Items-chain descent, and
//    the defensive complex-element wrapper.

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

internal sealed record FilterProbeSeriesResponse(
    IReadOnlyList<decimal?> MaybeNumbers,
    IReadOnlyList<decimal> Numbers,
    IReadOnlyList<string?> MaybeNames,
    IReadOnlyList<IReadOnlyList<decimal?>> Grid,
    IReadOnlyList<FilterProbeChild?> MaybeChildren);

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
        var seriesRef = generator.GenerateSchema(typeof(FilterProbeSeriesResponse), repository);

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
                // The S120 nullable-ITEMS probe: a 200-referenced schema whose members are the
                // collection-element nullability matrix (scalar-nullable / non-nullable control /
                // nested / complex-element).
                ["/probe/series"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses { ["200"] = JsonResponse(seriesRef) },
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
        // required = ALL members EXCEPT the conditionally-ignored one (sometimesAbsent).
        // S117: the CLR-nullable complex `maybeChild` IS required now (it emits as the
        // nullable-complex wrapper — the fired escalation; see the dedicated pin below).
        // Alphabetically sorted (SortedSet — the committed spec's house style). NOTE: the
        // plain-nullable scalar `note` IS required ("always present, possibly JSON null" →
        // TS `string | null`, non-optional).
        Assert.Equal(
            new[] { "child", "maybeChild", "name", "note", "status", "tags" },
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

    // ──────────────── (3) the nullable-complex wrapper (S117 — escalation FIRED) ─────────────

    [Fact]
    public void ClrNullableComplexMember_EmitsAsNullableAllOfWrapper_AndIsRequired()
    {
        // The RosterEmployeeRow.outgoingVikar analogue, POST-escalation (S117): a CLR-nullable
        // complex member emits as the OAS-3.0.3-legal nullable-complex WRAPPER — type: object +
        // allOf: [$ref] + nullable: true — AND is required ("always present, possibly JSON null";
        // openapi-typescript renders it `T | null`, non-optional). A bare $ref here would either
        // drop the nullable flag (Swashbuckle) or, if required, over-claim never-null — the S113
        // conservative exclusion this escalation replaced.
        var schema = SchemaOf<FilterProbeResponse>();
        var maybeChild = schema.Properties["maybeChild"];
        Assert.Null(maybeChild.Reference); // the $ref MOVED into the allOf child
        Assert.Equal("object", maybeChild.Type);
        Assert.True(maybeChild.Nullable);
        var wrapped = Assert.Single(maybeChild.AllOf);
        Assert.Equal(Id<FilterProbeChild>(), wrapped.Reference?.Id);
        Assert.Contains("maybeChild", schema.Required);

        // The NON-nullable complex member is the control: a bare $ref (the truthful never-null
        // claim), required, NOT wrapped.
        Assert.NotNull(schema.Properties["child"].Reference);
        Assert.True(schema.Properties["child"].AllOf is null || schema.Properties["child"].AllOf.Count == 0);
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

    // ──────────── (6) the nullable-ITEMS sibling (S120 — fixed at FIRST firing) ──────────────

    [Fact]
    public void NullableValueTypeElementCollection_EmitsItemsNullable()
    {
        // The YearOverviewCategory.Saldo analogue (IReadOnlyList<decimal?>): Swashbuckle 6.6.2
        // drops items-position nullability, so the spec claims never-null elements while the
        // empty-config branch serves an ALL-null 12-array — the exact lie the S120 ruling-#2
        // runtime pin REDs on. The filter must stamp items.nullable: true (the element truth);
        // the ARRAY member itself stays non-nullable.
        var maybeNumbers = SchemaOf<FilterProbeSeriesResponse>().Properties["maybeNumbers"];
        Assert.Equal("array", maybeNumbers.Type);
        Assert.False(maybeNumbers.Nullable);
        Assert.Equal("number", maybeNumbers.Items.Type);
        Assert.True(maybeNumbers.Items.Nullable,
            "A CLR-nullable-element collection did not gain items.nullable: true (the S120 nullable-ITEMS fix).");
    }

    [Fact]
    public void NonNullableElementCollection_DoesNotGainItemsNullable()
    {
        // The control (≙ YearOverviewCategory.Afholdt, IReadOnlyList<decimal>): a non-nullable
        // element must NOT gain the flag — over-emission would claim nulls the wire never serves
        // (the generated TS would degrade every element access with a phantom `| null`).
        var numbers = SchemaOf<FilterProbeSeriesResponse>().Properties["numbers"];
        Assert.Equal("array", numbers.Type);
        Assert.Equal("number", numbers.Items.Type);
        Assert.False(numbers.Items.Nullable,
            "A NON-nullable-element collection gained items.nullable: true (over-emission).");
    }

    [Fact]
    public void NullableReferenceScalarElementCollection_EmitsItemsNullable()
    {
        // The NRT flavor (IReadOnlyList<string?>): element nullability read via
        // NullabilityInfoContext generic-argument info, not Nullable.GetUnderlyingType.
        var maybeNames = SchemaOf<FilterProbeSeriesResponse>().Properties["maybeNames"];
        Assert.Equal("string", maybeNames.Items.Type);
        Assert.True(maybeNames.Items.Nullable);
    }

    [Fact]
    public void NestedNullableElementCollection_DescendsTheItemsChain()
    {
        // IReadOnlyList<IReadOnlyList<decimal?>>: the OUTER element (the inner list) is
        // non-nullable — no flag on the first Items level — while the descent stamps the inner
        // Items (decimal?) nullable. Each Items level is decided by its OWN element.
        var grid = SchemaOf<FilterProbeSeriesResponse>().Properties["grid"];
        Assert.Equal("array", grid.Type);
        Assert.Equal("array", grid.Items.Type);
        Assert.False(grid.Items.Nullable);
        Assert.Equal("number", grid.Items.Items.Type);
        Assert.True(grid.Items.Items.Nullable,
            "The nested-array descent did not reach the inner Items level.");
    }

    [Fact]
    public void NullableComplexElementCollection_ItemsGetTheNullableComplexWrapper()
    {
        // DEFENSIVE generality (zero such members exist in today's closure — see the class doc):
        // a CLR-nullable COMPLEX element ($ref items cannot carry a sibling nullable in OAS 3.0)
        // gets the S117 wrapper applied to the ITEMS schema — type: object + allOf: [$ref] +
        // nullable: true — exactly like a nullable complex member.
        var maybeChildren = SchemaOf<FilterProbeSeriesResponse>().Properties["maybeChildren"];
        Assert.Equal("array", maybeChildren.Type);
        var items = maybeChildren.Items;
        Assert.Null(items.Reference); // the $ref MOVED into the allOf child
        Assert.Equal("object", items.Type);
        Assert.True(items.Nullable);
        var wrapped = Assert.Single(items.AllOf);
        Assert.Equal(Id<FilterProbeChild>(), wrapped.Reference?.Id);
    }
}
