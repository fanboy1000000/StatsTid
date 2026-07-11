using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StatsTid.Backend.Api;

/// <summary>
/// S113 / TASK-11300 (PAT-012 strict-types phase) — makes the RESPONSE side of the committed
/// OpenAPI spec strict, with ZERO wire-byte change (PAT-010): pure spec metadata.
///
/// <para><b>What it does (document-filter half).</b> Computes the <b>response-reachable schema
/// closure</b> — every schema <c>$ref</c>'d from any operation's 2xx response content, transitively
/// closed over <c>components/schemas</c> (nested object refs, array <c>items</c>, dictionary
/// <c>additionalProperties</c>, allOf/anyOf/oneOf). For each closure schema it sets
/// <c>required</c> = ALL of its properties EXCEPT any property whose CLR member carries
/// <c>[JsonIgnore(Condition = WhenWritingNull/WhenWritingDefault)]</c>. It also reads
/// <c>[AllowedValues(...)]</c> (System.ComponentModel.DataAnnotations) off closure members and
/// emits <c>enum: [...]</c> on that property's schema — Swashbuckle 6.6.2 does NOT map
/// AllowedValues natively, so it is read explicitly here.</para>
///
/// <para><b>Why required=all-members is TRUTHFUL, not aspirational.</b> The minimal-API runtime
/// serializer runs on the .NET 8 <c>JsonSerializerDefaults.Web</c> defaults with NO
/// <c>DefaultIgnoreCondition</c> override (verified at Program.cs — the only JSON config touched is
/// Swashbuckle's naming policy): it null-emits every record member, so every closure member is
/// ALWAYS present on the wire (empirically confirmed against all 26 real per-route responses in
/// S112). Nullable members stay <c>nullable: true</c> AND become required — "always present,
/// possibly JSON null" → the generated TS is <c>T | null</c>, non-optional. The conditional-ignore
/// exception is DEFENSIVE: zero such members exist in today's 28-schema closure, but
/// <c>SharedKernel/Models/VacationSettlementSnapshot.cs</c> carries
/// <c>[JsonIgnore(WhenWritingNull/WhenWritingDefault)]</c> ×4 and WILL enter the closure at the
/// payroll retrofit pass — a conditionally-ignored member can be ABSENT from the wire and must
/// never be claimed required.</para>
///
/// <para><b>The nullable-complex wrapper (S117 — the S113 nullable-$ref escalation FIRED).</b> A
/// CLR-nullable COMPLEX member generates from Swashbuckle as a BARE <c>$ref</c> — OpenAPI 3.0
/// forbids a sibling <c>nullable</c> on <c>$ref</c>, and Swashbuckle 6.6.2 silently DROPS the flag.
/// S113 handled this conservatively by EXCLUDING such members from <c>required</c> (the generated
/// TS stayed <c>member?: T</c>, consumed via FE Omit/normalization scaffolding), with a numbered
/// escalation trigger: at 3+ members, do the truthful emission. S117's settlement retrofit would
/// have created the 3rd member (<c>SettlementReversalResponse.Successor</c>), so the escalation
/// fired: this filter now REWRITES every CLR-nullable complex member's property schema to the
/// OAS-3.0.3-legal nullable-complex form — <c>type: object</c> + <c>allOf: [$ref]</c> +
/// <c>nullable: true</c> — AND includes it in <c>required</c> ("always present, possibly JSON
/// null", exactly like nullable scalars). <c>openapi-typescript@7.13.0</c> renders the wrapper as
/// <c>T | null</c>, non-optional (empirically verified, S117 Step-4). The resolution side of the
/// coordinated change lives in the SpecRuntimeMatcher's <c>Deref</c>: it resolves
/// THROUGH the wrapper (so inner required/enum fidelity recurses) and REDs a bare-<c>$ref</c>
/// member serving null — truthful nullability on a complex member ALWAYS carries the wrapper now.
/// Retro-applied to the 2 pre-existing residual members (<c>RosterEmployeeRow.OutgoingVikar</c>,
/// <c>ActiveVikarResponse.ActiveVikar</c>) — the nullable-$ref residual class is CLOSED (0 members).</para>
///
/// <para><b>What it does NOT touch.</b> Schemas outside the closure (today: all 58 request DTO
/// schemas) are byte-UNTOUCHED — their <c>required</c> arrays are the C#-<c>required</c>-keyword
/// binder-enforced truth Swashbuckle already emits, a DIFFERENT (request-validation) semantic this
/// filter must not overwrite or extend.</para>
///
/// <para><b>OVERLAP POLICY</b> (a schema reachable from BOTH a requestBody and a 2xx response —
/// DISJOINT today, so this is a documented policy, not exercised code-path behavior): the
/// response-truth <c>required</c> APPLIES (the schema is in the closure, so it is processed like
/// any other closure schema). Rationale: (1) serialization truth dominates — on the response side
/// "required = every non-conditionally-ignored member" is a FACT of the null-emitting serializer,
/// not a claim; (2) the request side loses nothing — its binder-enforced <c>required</c> members
/// (the C# <c>required</c> subset) are necessarily a SUBSET of all-members, so no binder-enforced
/// member is dropped, and the request contract only ever TIGHTENS; (3) the tradeoff accepted: the
/// generated TS request type for such a shared schema would demand members the binder does not
/// actually require — over-strict toward SENDING complete data, which fails safe (an FE compile
/// error, never a missing-field runtime surprise). The durable fix for a real overlap is splitting
/// the shared record into a request record and a response record (the PAT-012 paved road already
/// mandates named RESPONSE records, so an overlap is a smell to begin with).</para>
///
/// <para><b>How the schema→CLR mapping works (schema-filter half).</b> <c>required</c>/enum
/// decisions need the CLR member (attributes are not carried into <see cref="OpenApiSchema"/>), so
/// the SAME instance is registered as both an <see cref="ISchemaFilter"/> (runs per generated
/// schema; records schemaId → CLR type via
/// <see cref="SchemaRepository.TryLookupByType(Type, out OpenApiSchema)"/>) and an
/// <see cref="IDocumentFilter"/> (runs once at the end, after every schema is generated, and
/// resolves each spec property back to its CLR <see cref="PropertyInfo"/> by the same camelCase
/// naming policy the generator uses). FAIL-CONSERVATIVE: an unresolvable CLR type or property is
/// SKIPPED (no required/enum emission for it) rather than guessed — the TASK-11302 filter tests +
/// the spec-inspection validation criteria (full required arrays on the closure) catch a silent
/// mapping regression loudly.</para>
/// </summary>
public sealed class ResponseStrictTypesFilter : ISchemaFilter, IDocumentFilter
{
    private readonly ConcurrentDictionary<string, Type> _clrTypeBySchemaId = new(StringComparer.Ordinal);

    /// <summary>Schema-filter half: record schemaId → CLR type for every registered (ref-able)
    /// schema. Inline member schemas (primitives etc.) are not registered and are skipped.</summary>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type is null)
            return;

        if (context.SchemaRepository.TryLookupByType(context.Type, out var referenceSchema)
            && referenceSchema.Reference?.Id is { Length: > 0 } schemaId)
        {
            _clrTypeBySchemaId[schemaId] = context.Type;
        }
    }

    /// <summary>Document-filter half: compute the response-reachable closure and apply
    /// required-strictness + AllowedValues enums to closure schemas ONLY.</summary>
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null || schemas.Count == 0)
            return;

        foreach (var schemaId in ComputeResponseReachableClosure(document, schemas))
        {
            if (!schemas.TryGetValue(schemaId, out var schema))
                continue;
            if (schema.Properties is null || schema.Properties.Count == 0)
                continue;
            // Fail-conservative: no recorded CLR type → leave the schema untouched (see class doc).
            if (!_clrTypeBySchemaId.TryGetValue(schemaId, out var clrType))
                continue;

            // Swashbuckle's own request-side required arrays serialize alphabetically sorted
            // (see e.g. CreateUserRequest) — SortedSet keeps the committed spec's house style
            // and is deterministic across runs regardless of property-dictionary ordering.
            var required = new SortedSet<string>(StringComparer.Ordinal);

            var nullability = new NullabilityInfoContext();
            foreach (var (propertyName, propertySchema) in schema.Properties)
            {
                var member = ResolveClrProperty(clrType, propertyName);
                if (member is null)
                    continue; // fail-conservative: unmappable property → not claimed required

                if (!IsConditionallyIgnored(member))
                {
                    // S117 — a CLR-nullable COMPLEX member (a bare $ref, which cannot carry a
                    // sibling nullable in OpenAPI 3.0) is rewritten to the legal nullable-complex
                    // WRAPPER (type: object + allOf: [$ref] + nullable: true) so it can be claimed
                    // required TRUTHFULLY ("always present, possibly JSON null" — the null-emitting
                    // serializer fact, same as nullable scalars). See the class doc.
                    if (IsNullableRef(member, propertySchema, nullability))
                        WrapAsNullableComplex(propertySchema);
                    required.Add(propertyName);
                }

                // [AllowedValues] → enum. Read explicitly (6.6.2 has no native mapping). Emitted
                // ONLY inside the closure by construction — a mistakenly-attributed request DTO
                // member can never leak an enum into a request schema through this filter.
                if (member.GetCustomAttribute<AllowedValuesAttribute>() is { } allowed)
                {
                    propertySchema.Enum = allowed.Values
                        .OfType<string>()
                        .Select(v => (IOpenApiAny)new OpenApiString(v))
                        .ToList();
                }
            }

            if (required.Count > 0)
                schema.Required = required;
        }
    }

    /// <summary>Every components-schema id reachable from any operation's 2xx response content,
    /// transitively closed over nested refs (self-referential schemas — e.g. ForestUnitNode.children
    /// — terminate via the visited set).</summary>
    private static IReadOnlyCollection<string> ComputeResponseReachableClosure(
        OpenApiDocument document, IDictionary<string, OpenApiSchema> schemas)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations.Values)
            {
                if (operation.Responses is null)
                    continue;
                foreach (var (statusCode, response) in operation.Responses)
                {
                    // "200", "201", … and the "2XX" range form all start with '2'.
                    if (statusCode.Length == 0 || statusCode[0] != '2')
                        continue;
                    if (response.Content is null)
                        continue;
                    foreach (var mediaType in response.Content.Values)
                        CollectRefs(mediaType.Schema, roots);
                }
            }
        }

        var closure = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.Count > 0)
        {
            var schemaId = pending.Pop();
            if (!closure.Add(schemaId))
                continue;
            if (!schemas.TryGetValue(schemaId, out var definition))
                continue;

            var nested = new HashSet<string>(StringComparer.Ordinal);
            CollectChildRefs(definition, nested);
            foreach (var nestedId in nested)
            {
                if (!closure.Contains(nestedId))
                    pending.Push(nestedId);
            }
        }
        return closure;
    }

    /// <summary>A $ref node is a LEAF here (its definition is walked from components by the closure
    /// loop, which is what makes self-reference terminate); an inline node recurses into its
    /// children.</summary>
    private static void CollectRefs(OpenApiSchema? schema, ISet<string> into)
    {
        if (schema is null)
            return;
        if (schema.Reference?.Id is { Length: > 0 } id)
        {
            into.Add(id);
            return;
        }
        CollectChildRefs(schema, into);
    }

    /// <summary>Walk every child position that can carry a schema ref: array items, dictionary
    /// values (additionalProperties), object properties, and the composition keywords.</summary>
    private static void CollectChildRefs(OpenApiSchema schema, ISet<string> into)
    {
        CollectRefs(schema.Items, into);
        CollectRefs(schema.AdditionalProperties, into);
        CollectRefs(schema.Not, into);
        if (schema.AllOf is not null)
            foreach (var s in schema.AllOf) CollectRefs(s, into);
        if (schema.AnyOf is not null)
            foreach (var s in schema.AnyOf) CollectRefs(s, into);
        if (schema.OneOf is not null)
            foreach (var s in schema.OneOf) CollectRefs(s, into);
        if (schema.Properties is not null)
            foreach (var p in schema.Properties.Values) CollectRefs(p, into);
    }

    /// <summary>Resolve a spec property name back to the CLR property, using the SAME name
    /// derivation the generator used: an explicit [JsonPropertyName] wins (none exist in Contracts/
    /// by convention — defensive), else the camelCase policy configured for Swashbuckle in
    /// Program.cs.</summary>
    private static PropertyInfo? ResolveClrProperty(Type clrType, string specPropertyName)
    {
        foreach (var property in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var wireName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (string.Equals(wireName, specPropertyName, StringComparison.Ordinal))
                return property;
        }
        return null;
    }

    /// <summary>TRUE for [JsonIgnore(WhenWritingNull)] / [JsonIgnore(WhenWritingDefault)] — the
    /// member can be ABSENT from the wire, so it must not be claimed required. (Condition=Always
    /// members never reach here — Swashbuckle excludes them from the schema entirely;
    /// Condition=Never members are always serialized → required.)</summary>
    private static bool IsConditionallyIgnored(PropertyInfo member) =>
        member.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
            is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault;

    /// <summary>TRUE for a CLR-NULLABLE member whose spec schema is a bare $ref (a nullable complex
    /// type, e.g. RosterEmployeeRow.OutgoingVikar) — S117: such a member is rewritten by
    /// <see cref="WrapAsNullableComplex"/> to the OAS-3.0.3-legal nullable-complex wrapper AND
    /// claimed required (a bare $ref cannot carry nullable:true; required-without-nullable would be
    /// a never-null over-claim — see the class doc's nullable-complex wrapper paragraph).</summary>
    private static bool IsNullableRef(
        PropertyInfo member, OpenApiSchema propertySchema, NullabilityInfoContext nullability)
    {
        if (propertySchema.Reference is null)
            return false;
        return Nullable.GetUnderlyingType(member.PropertyType) is not null
            || nullability.Create(member).WriteState == NullabilityState.Nullable;
    }

    /// <summary>S117 — rewrite a CLR-nullable complex member's bare-$ref property schema IN PLACE to
    /// the OAS-3.0.3-legal nullable-complex form: <c>type: object</c> + <c>allOf: [$ref]</c> +
    /// <c>nullable: true</c>. (A schema node with <c>Reference</c> set serializes as ONLY the $ref —
    /// the reference must MOVE into the allOf child for the sibling keywords to survive.)</summary>
    private static void WrapAsNullableComplex(OpenApiSchema propertySchema)
    {
        var innerRef = propertySchema.Reference;
        propertySchema.Reference = null;
        propertySchema.Type = "object";
        propertySchema.Nullable = true;
        propertySchema.AllOf = new List<OpenApiSchema> { new() { Reference = innerRef } };
    }
}
