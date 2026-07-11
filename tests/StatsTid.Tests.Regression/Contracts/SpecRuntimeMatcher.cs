using System.Globalization;
using System.Text.Json;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S111 / TASK-11101 (Fork B typed-client) — the PER-ROUTE spec≡runtime matcher: the load-bearing
/// closure of the recurring "fetchEnheder" bug class. <c>.Produces&lt;T&gt;</c> is author-chosen
/// metadata that can LIE (most easily about array-ness — <c>.Produces&lt;OrgListItem&gt;</c> vs the
/// bare-array runtime). This matcher asserts the committed OpenAPI schema of an operation's DECLARED
/// SUCCESS STATUS (200/201; explicit 204 = status + empty-body assert — S112 / TASK-11204; a
/// HOMOGENEOUS multi-2xx set sharing ONE schema <c>$ref</c>, e.g. the 201-or-200 conditional assigns —
/// S115 / TASK-11500) STRUCTURALLY matches that operation's REAL serialized response, so the generated
/// spec (and thus the FE types derived from it) cannot agree with each other while disagreeing with the
/// runtime bytes.
///
/// <para>It asserts, recursively: <b>root kind / array-ness</b> (an object schema vs an array response
/// is RED), <b>property presence</b> (every schema property is served), <b>required-fidelity</b>
/// (S113 / TASK-11302 — every member the schema lists in <c>required</c> is PRESENT in the serialized
/// response; required-but-absent is RED with the member name), <b>camelCase keys</b> (a
/// serializer-policy regression on either side is RED), <b>nullable-required fidelity</b> (a scalar the
/// schema marks NON-nullable that the runtime serves <c>null</c> is RED; a <c>nullable:true</c> scalar
/// may be null), <b>enum-fidelity</b> (S113 / TASK-11302 — where a schema carries <c>enum</c>, a
/// NON-NULL serialized value must be IN the set; null admissibility is governed by <c>nullable</c>
/// alone — null is admissible iff the member is nullable, never by enum membership), and
/// <b>array item / dictionary value schemas</b> (the nested element shape).</para>
///
/// <para>NOTE (updated S113 / TASK-11302): the <c>ResponseStrictTypesFilter</c> now POPULATES
/// <c>required</c> on the response-reachable schema closure (required = every member EXCEPT
/// conditionally-ignored [<c>JsonIgnore(WhenWritingNull/Default)</c>] and CLR-nullable bare-<c>$ref</c>
/// members — see that filter's class doc), so required-fidelity is enforced directly off the
/// <c>required</c> array. The nullable-required check still rides the <c>nullable</c> flag, enforced
/// on SCALAR leaves. <c>$ref</c>/object/array properties are checked for presence + recursed when
/// non-null (an OpenAPI-3.0 <c>$ref</c> cannot carry a sibling <c>nullable</c>, so a nullable
/// ref-typed property — e.g. <c>outgoingVikar</c>, which the filter deliberately EXCLUDES from
/// <c>required</c> — is allowed to be null or absent without a false RED).</para>
/// </summary>
public static class SpecRuntimeMatcher
{
    /// <summary>
    /// S112 / TASK-11204 (extended S115 / TASK-11500) — an operation's DECLARED success contract:
    /// its declared 2xx status codes (a SINGLE status for almost every operation; a HOMOGENEOUS
    /// conditional-status SET — e.g. the 201-or-200 reporting-line assigns — ONLY when every declared
    /// 2xx carries the SAME schema <c>$ref</c>) and the ONE <c>application/json</c> schema node they
    /// share. A declared-204 operation has <see cref="Schema"/> <c>null</c> — there is no body to
    /// match, and <see cref="AssertSuccessMatches"/> instead asserts the response status is 204 with
    /// an EMPTY body (a 204 can NEVER be a member of a multi-status set — heterogeneous by content).
    /// </summary>
    public readonly record struct SuccessContract(IReadOnlyList<int> StatusCodes, JsonElement? Schema)
    {
        /// <summary>The S112 single-status shape, kept as a constructor overload so the Docker
        /// per-route classes that hand-build injected-lie contracts compile UNMODIFIED
        /// (ripple-containment — the set-shape stays internal to Matcher + Support).</summary>
        public SuccessContract(int statusCode, JsonElement? schema)
            : this(new[] { statusCode }, schema)
        {
        }

        /// <summary>The single declared status — the S112 surface, kept for the single-status
        /// consumers (the Docker classes' truth/lie asserts compile UNMODIFIED). Reading it on a
        /// homogeneous MULTI-status contract is a programming error (use <see cref="StatusCodes"/>).</summary>
        public int StatusCode => StatusCodes.Count == 1
            ? StatusCodes[0]
            : throw new XunitException(
                $"SuccessContract carries MULTIPLE declared statuses ({DescribeStatuses()}) — read StatusCodes, not StatusCode.");

        /// <summary>Human-readable declared-status set for diagnostics ("200" / "201 or 200").</summary>
        public string DescribeStatuses() => string.Join(" or ", StatusCodes);
    }

    /// <summary>
    /// S112 / TASK-11204 (extended S115 / TASK-11500) — resolve the operation's declared 2xx success
    /// contract for <paramref name="path"/>+<paramref name="method"/> from a parsed OpenAPI
    /// <paramref name="spec"/> (replaces the S111 hard-coded "200" resolution; mutations declare
    /// 201/204 too). A SINGLE declared 2xx resolves exactly as before. MULTIPLE declared 2xx is
    /// acceptable IFF every declared 2xx carries the SAME schema <c>$ref</c> (the homogeneous
    /// conditional-status case: one shared shape behind 201-or-200) — the contract then carries the
    /// declared-status SET plus the one shared schema. Throws when the operation is absent, declares
    /// NO 2xx (untyped), declares a 204 WITH a JSON schema (contradictory), declares 200/201 WITHOUT
    /// an <c>application/json</c> schema (empty <c>.Produces</c>), or declares a HETEROGENEOUS
    /// multi-2xx set — specifically including a 204-no-content paired with a body-bearing status
    /// (heterogeneous BY CONTENT), any member with an INLINE (non-<c>$ref</c>) schema (rejected
    /// conservatively even if structurally identical — records always emit <c>$ref</c>; this is the
    /// tripwire against inline-schema drift), and members whose <c>$ref</c>s differ.
    /// </summary>
    public static SuccessContract ResolveSuccessContract(JsonElement spec, string path, string method)
    {
        if (!spec.TryGetProperty("paths", out var paths))
            throw new XunitException("Spec has no 'paths'.");
        if (!paths.TryGetProperty(path, out var pathItem))
            throw new XunitException($"Spec has no path '{path}'. (Is the committed openapi.json stale? Regenerate with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.)");
        if (!pathItem.TryGetProperty(method, out var op))
            throw new XunitException($"Path '{path}' has no '{method}' operation.");
        if (!op.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
            throw new XunitException($"{method.ToUpperInvariant()} {path} has no 'responses' in the spec — the endpoint is not typed with .Produces.");

        var declared = new List<(string Code, JsonElement Response)>();
        foreach (var response in responses.EnumerateObject())
        {
            if (response.Name.Length == 3 && response.Name[0] == '2')
                declared.Add((response.Name, response.Value));
        }
        if (declared.Count == 0)
            throw new XunitException($"{method.ToUpperInvariant()} {path} declares no 2xx response in the spec — the endpoint is not typed with .Produces.");

        // Single declared 2xx — the S112 behavior, byte-unchanged.
        if (declared.Count == 1)
        {
            var (successCode, successResponse) = declared[0];
            var statusCode = int.Parse(successCode, CultureInfo.InvariantCulture);
            if (statusCode == StatusCode204)
            {
                // A No-Content declaration must not carry a JSON schema — that would be a contract lie.
                if (successResponse.TryGetProperty("content", out var content204)
                    && content204.TryGetProperty("application/json", out _))
                    throw new XunitException($"{method.ToUpperInvariant()} {path} declares 204 No Content WITH an application/json schema — a 204 has no body.");
                return new SuccessContract(statusCode, null);
            }

            if (!successResponse.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var media)
                || !media.TryGetProperty("schema", out var schema))
                throw new XunitException($"{method.ToUpperInvariant()} {path} {successCode} has no application/json schema — the endpoint is not typed (empty .Produces).");
            return new SuccessContract(statusCode, schema);
        }

        // S115 / TASK-11500 — MULTIPLE declared 2xx: acceptable IFF homogeneous (every declared 2xx
        // carries the SAME schema $ref — the 201-or-200-from-ONE-shape conditional case). Everything
        // else stays REJECTED.
        var codesText = string.Join(", ", declared.Select(d => d.Code));
        var statusCodes = new List<int>(declared.Count);
        string? sharedRef = null;
        JsonElement sharedSchema = default;
        foreach (var (code, response) in declared)
        {
            var statusCode = int.Parse(code, CultureInfo.InvariantCulture);
            if (statusCode == StatusCode204)
                throw new XunitException(
                    $"{method.ToUpperInvariant()} {path} declares MULTIPLE 2xx responses ({codesText}) including 204 No Content — " +
                    "heterogeneous BY CONTENT (a no-body status cannot share a schema with a body-bearing one); " +
                    "the per-route spec≡runtime gate accepts multiple 2xx ONLY when every one carries the SAME schema $ref.");
            if (!response.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var media)
                || !media.TryGetProperty("schema", out var schema))
                throw new XunitException(
                    $"{method.ToUpperInvariant()} {path} declares MULTIPLE 2xx responses ({codesText}) but {code} has no application/json schema — " +
                    "heterogeneous BY CONTENT; the gate accepts multiple 2xx ONLY when every one carries the SAME schema $ref.");
            if (schema.ValueKind != JsonValueKind.Object
                || !schema.TryGetProperty("$ref", out var refEl) || refEl.GetString() is not string refPath)
                throw new XunitException(
                    $"{method.ToUpperInvariant()} {path} declares MULTIPLE 2xx responses ({codesText}) where {code} carries an INLINE (non-$ref) schema — " +
                    "rejected conservatively even if structurally identical (records always emit $ref; an inline schema here is drift). " +
                    "The gate accepts multiple 2xx ONLY when every one carries the SAME schema $ref.");
            if (sharedRef is null)
            {
                sharedRef = refPath;
                sharedSchema = schema;
            }
            else if (!string.Equals(sharedRef, refPath, StringComparison.Ordinal))
            {
                throw new XunitException(
                    $"{method.ToUpperInvariant()} {path} declares MULTIPLE 2xx responses ({codesText}) with DIFFERENT schemas ('{sharedRef}' vs '{refPath}') — " +
                    "the per-route spec≡runtime gate accepts multiple 2xx ONLY when every one carries the SAME schema $ref.");
            }
            statusCodes.Add(statusCode);
        }
        return new SuccessContract(statusCodes, sharedSchema);
    }

    /// <summary>
    /// S112 / TASK-11204 (extended S115 / TASK-11500) — assert the runtime response (status + raw
    /// body) satisfies the operation's declared <paramref name="contract"/> (from
    /// <see cref="ResolveSuccessContract"/>):
    /// <list type="bullet">
    ///   <item><description><b>Status fidelity</b> — the actual status MUST be a MEMBER of the
    ///     declared 2xx set (single-status: equality, exactly as before; a homogeneous multi-status
    ///     contract admits any DECLARED member, but an UNDECLARED runtime status stays RED — a
    ///     spec-declares-200-but-runtime-serves-201 is RED).</description></item>
    ///   <item><description><b>204</b> — the body MUST be empty (there is no schema to match).</description></item>
    ///   <item><description><b>200/201 (and any declared body-bearing member)</b> — the body is parsed
    ///     and matched structurally against the ONE declared/shared schema via
    ///     <see cref="AssertMatches"/> (all downstream fidelity — property presence, camelCase,
    ///     nullable, required, enum — unchanged).</description></item>
    /// </list>
    /// </summary>
    public static void AssertSuccessMatches(JsonElement spec, SuccessContract contract, int actualStatusCode, string? body, string context)
    {
        if (!contract.StatusCodes.Contains(actualStatusCode))
            throw new XunitException(
                $"{context}: spec declares success status {contract.DescribeStatuses()} but the runtime returned {actualStatusCode} " +
                "(a status-code mismatch — the exact lie a mis-declared .Produces status can tell; an UNDECLARED runtime status is RED even on a multi-2xx operation). RED.");

        if (contract.StatusCodes.Count == 1 && contract.StatusCodes[0] == StatusCode204)
        {
            if (!string.IsNullOrEmpty(body))
                throw new XunitException($"{context}: spec declares 204 No Content but the runtime response carries a body: {Truncate(body)}. RED.");
            return;
        }

        if (contract.Schema is not JsonElement schema)
            throw new XunitException($"{context}: contract for {contract.DescribeStatuses()} carries no schema (programming error).");
        if (string.IsNullOrWhiteSpace(body))
            throw new XunitException($"{context}: spec declares a {contract.DescribeStatuses()} JSON body but the runtime response body is empty. RED.");

        var json = JsonDocument.Parse(body).RootElement;
        Match(spec, schema, json, context);
    }

    /// <summary>Resolve the <c>application/json</c> <c>200</c> response schema node for
    /// <paramref name="path"/>+<paramref name="method"/> from a parsed OpenAPI <paramref name="spec"/>
    /// (the S111 surface, kept for the 200-only proof reads — now a thin wrapper over
    /// <see cref="ResolveSuccessContract"/>). Throws if the operation is untyped or its single
    /// declared success is not 200.</summary>
    public static JsonElement Resolve200Schema(JsonElement spec, string path, string method)
    {
        var contract = ResolveSuccessContract(spec, path, method);
        if (contract.StatusCodes.Count != 1 || contract.StatusCodes[0] != 200 || contract.Schema is not JsonElement schema)
            throw new XunitException(
                $"{method.ToUpperInvariant()} {path} declares {contract.DescribeStatuses()}, not 200 — resolve it via ResolveSuccessContract.");
        return schema;
    }

    private const int StatusCode204 = 204;

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    /// <summary>Assert the <paramref name="schema"/> (with <c>$ref</c>s resolvable against
    /// <paramref name="spec"/>'s <c>components.schemas</c>) structurally matches the real serialized
    /// <paramref name="json"/> response.</summary>
    public static void AssertMatches(JsonElement spec, JsonElement schema, JsonElement json, string context)
        => Match(spec, schema, json, context);

    private static void Match(JsonElement spec, JsonElement schema, JsonElement json, string ctx)
    {
        schema = Deref(spec, schema);

        // S113 / TASK-11302 — ENUM-fidelity: where the (deref'd) schema carries an `enum`, a NON-NULL
        // serialized value must be IN the declared set (an out-of-set discriminator means the FE's
        // generated literal union lies). Null NEVER reaches here through the property loop (the null
        // path is governed by the `nullable` flag alone — null admissible iff the member is nullable),
        // but a root-level null is guarded anyway.
        if (json.ValueKind != JsonValueKind.Null
            && schema.TryGetProperty("enum", out var enumSet) && enumSet.ValueKind == JsonValueKind.Array)
        {
            var inSet = false;
            foreach (var candidate in enumSet.EnumerateArray())
            {
                if (JsonValueEquals(candidate, json))
                {
                    inSet = true;
                    break;
                }
            }
            if (!inSet)
                throw new XunitException(
                    $"{ctx}: runtime value {json.GetRawText()} is NOT in the schema's enum set {enumSet.GetRawText()} " +
                    "(an out-of-set discriminator — the generated TS literal union would lie). RED.");
        }

        // An array schema (the bare-array case + any nested list). Array-ness is the headline lie.
        if (IsArray(schema))
        {
            if (json.ValueKind != JsonValueKind.Array)
                throw new XunitException(
                    $"{ctx}: spec schema is an ARRAY but the runtime response is '{json.ValueKind}' " +
                    "(an array-ness mismatch — the exact lie .Produces<T> can tell). RED.");
            if (schema.TryGetProperty("items", out var items))
                foreach (var el in json.EnumerateArray())
                    Match(spec, items, el, ctx + "[]");
            return;
        }

        // An object schema (records, dictionaries). Includes the inverse array-ness lie.
        if (IsObject(schema))
        {
            if (json.ValueKind != JsonValueKind.Object)
                throw new XunitException(
                    $"{ctx}: spec schema is an OBJECT but the runtime response is '{json.ValueKind}' " +
                    "(an array-ness/shape mismatch). RED.");

            // S113 / TASK-11302 — REQUIRED-fidelity: every member the schema lists in `required` must
            // be PRESENT in the real serialized response. The strict-types filter claims required =
            // always-on-the-wire to the generated TS (non-optional members) — a required-but-absent
            // member is that exact claim lying, RED with the member name. Checked FIRST (before the
            // property-presence walk) so a member both declared and required fails with the
            // required-specific message; it also covers a `required` entry with no matching
            // `properties` declaration.
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var requiredName in required.EnumerateArray())
                {
                    var name = requiredName.GetString();
                    if (name is not null && !json.TryGetProperty(name, out _))
                        throw new XunitException(
                            $"{ctx}: schema lists '{name}' as REQUIRED but the runtime response omits it " +
                            "(the generated TS would claim a member the wire does not carry). RED.");
                }
            }

            if (schema.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateObject())
                {
                    var name = prop.Name;
                    if (!IsCamelCase(name))
                        throw new XunitException($"{ctx}: schema property '{name}' is not camelCase (a serializer-policy regression). RED.");
                    if (!json.TryGetProperty(name, out var val))
                        throw new XunitException($"{ctx}: schema declares property '{name}' but the runtime response omits it. RED.");

                    var pschema = Deref(spec, prop.Value);
                    var nullable = IsNullable(prop.Value) || IsNullable(pschema);
                    var scalar = IsScalar(pschema);

                    if (val.ValueKind == JsonValueKind.Null)
                    {
                        // A NON-nullable SCALAR the runtime serves null is a fidelity break. Structural
                        // ($ref/object/array) nullable properties cannot carry nullable on a $ref in
                        // OpenAPI 3.0, so a null there is permitted (no recursion).
                        if (scalar && !nullable)
                            throw new XunitException($"{ctx}.{name}: schema marks it NON-nullable but the runtime served null. RED.");
                    }
                    else
                    {
                        Match(spec, prop.Value, val, $"{ctx}.{name}");
                    }
                }
            }

            // Dictionary values (additionalProperties carries a value schema, e.g. nameResolution).
            if (schema.TryGetProperty("additionalProperties", out var addl) && addl.ValueKind == JsonValueKind.Object)
                foreach (var entry in json.EnumerateObject())
                    if (entry.Value.ValueKind != JsonValueKind.Null)
                        Match(spec, addl, entry.Value, $"{ctx}.{entry.Name}");
            return;
        }

        // Scalar leaf — a loose kind check (DateOnly→date string, Guid→uuid string both pass as String).
        AssertScalarKind(schema, json, ctx);
    }

    private static JsonElement Deref(JsonElement spec, JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var refEl))
        {
            var refPath = refEl.GetString() ?? "";
            const string prefix = "#/components/schemas/";
            if (!refPath.StartsWith(prefix, StringComparison.Ordinal))
                throw new XunitException($"Unsupported $ref '{refPath}' (only local component refs are resolved).");
            var name = refPath.Substring(prefix.Length);
            var resolved = spec.GetProperty("components").GetProperty("schemas").GetProperty(name);
            return Deref(spec, resolved);
        }
        return schema;
    }

    private static bool IsArray(JsonElement schema)
        => (schema.TryGetProperty("type", out var t) && t.GetString() == "array")
           || schema.TryGetProperty("items", out _);

    private static bool IsObject(JsonElement schema)
        => (schema.TryGetProperty("type", out var t) && t.GetString() == "object")
           || schema.TryGetProperty("properties", out _)
           || schema.TryGetProperty("additionalProperties", out _);

    private static bool IsScalar(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var t)) return false;
        var type = t.GetString();
        return type is "string" or "integer" or "number" or "boolean";
    }

    private static bool IsNullable(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
           && schema.TryGetProperty("nullable", out var n)
           && n.ValueKind == JsonValueKind.True;

    private static bool IsCamelCase(string name)
        => name.Length > 0 && (char.IsLower(name[0]) || !char.IsLetter(name[0]));

    /// <summary>S113 — enum-member equality: ordinal string compare for the (dominant) string
    /// discriminator case; kind + raw-text equality otherwise (numeric enums are not used in the
    /// closure today, so no numeric normalization is attempted).</summary>
    private static bool JsonValueEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
            return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
        return a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText();
    }

    private static void AssertScalarKind(JsonElement schema, JsonElement json, string ctx)
    {
        if (!schema.TryGetProperty("type", out var t)) return; // free-form / unknown — skip
        var type = t.GetString();
        var ok = type switch
        {
            "string" => json.ValueKind == JsonValueKind.String,
            "integer" => json.ValueKind == JsonValueKind.Number,
            "number" => json.ValueKind == JsonValueKind.Number,
            "boolean" => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => true,
        };
        if (!ok)
            throw new XunitException($"{ctx}: schema type '{type}' but runtime kind '{json.ValueKind}'. RED.");
    }
}
