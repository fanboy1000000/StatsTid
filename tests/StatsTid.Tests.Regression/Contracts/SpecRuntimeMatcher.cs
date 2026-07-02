using System.Globalization;
using System.Text.Json;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S111 / TASK-11101 (Fork B typed-client) — the PER-ROUTE spec≡runtime matcher: the load-bearing
/// closure of the recurring "fetchEnheder" bug class. <c>.Produces&lt;T&gt;</c> is author-chosen
/// metadata that can LIE (most easily about array-ness — <c>.Produces&lt;OrgListItem&gt;</c> vs the
/// bare-array runtime). This matcher asserts the committed OpenAPI schema of an operation's SINGLE
/// DECLARED SUCCESS STATUS (200/201; explicit 204 = status + empty-body assert — S112 / TASK-11204)
/// STRUCTURALLY matches that operation's REAL serialized response, so the generated spec (and thus the
/// FE types derived from it) cannot agree with each other while disagreeing with the runtime bytes.
///
/// <para>It asserts, recursively: <b>root kind / array-ness</b> (an object schema vs an array response
/// is RED), <b>property presence</b> (every schema property is served), <b>camelCase keys</b> (a
/// serializer-policy regression on either side is RED), <b>nullable-required fidelity</b> (a scalar the
/// schema marks NON-nullable that the runtime serves <c>null</c> is RED; a <c>nullable:true</c> scalar
/// may be null), and <b>array item / dictionary value schemas</b> (the nested element shape).</para>
///
/// <para>NOTE: Swashbuckle does not populate <c>required</c> for record positional members, so the
/// nullable-required check rides the (correctly populated) <c>nullable</c> flag, enforced on SCALAR
/// leaves. <c>$ref</c>/object/array properties are checked for presence + recursed when non-null (an
/// OpenAPI-3.0 <c>$ref</c> cannot carry a sibling <c>nullable</c>, so a nullable ref-typed property —
/// e.g. <c>outgoingVikar</c> — is allowed to be null without a false RED).</para>
/// </summary>
public static class SpecRuntimeMatcher
{
    /// <summary>
    /// S112 / TASK-11204 — an operation's DECLARED success contract: its single declared 2xx status
    /// code and (for 200/201) the <c>application/json</c> schema node. A declared-204 operation has
    /// <see cref="Schema"/> <c>null</c> — there is no body to match, and
    /// <see cref="AssertSuccessMatches"/> instead asserts the response status is 204 with an EMPTY body.
    /// </summary>
    public readonly record struct SuccessContract(int StatusCode, JsonElement? Schema);

    /// <summary>
    /// S112 / TASK-11204 — resolve the operation's SINGLE declared 2xx success contract for
    /// <paramref name="path"/>+<paramref name="method"/> from a parsed OpenAPI <paramref name="spec"/>
    /// (replaces the S111 hard-coded "200" resolution; mutations declare 201/204 too). Throws when the
    /// operation is absent, declares NO 2xx (untyped), declares MULTIPLE 2xx (ambiguous — the per-route
    /// gate needs exactly one declared success), declares a 204 WITH a JSON schema (contradictory), or
    /// declares 200/201 WITHOUT an <c>application/json</c> schema (empty <c>.Produces</c>).
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

        string? successCode = null;
        JsonElement successResponse = default;
        foreach (var response in responses.EnumerateObject())
        {
            if (response.Name.Length == 3 && response.Name[0] == '2')
            {
                if (successCode is not null)
                    throw new XunitException(
                        $"{method.ToUpperInvariant()} {path} declares MULTIPLE 2xx responses ({successCode}, {response.Name}) — " +
                        "the per-route spec≡runtime gate needs exactly ONE declared success status.");
                successCode = response.Name;
                successResponse = response.Value;
            }
        }
        if (successCode is null)
            throw new XunitException($"{method.ToUpperInvariant()} {path} declares no 2xx response in the spec — the endpoint is not typed with .Produces.");

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

    /// <summary>
    /// S112 / TASK-11204 — assert the runtime response (status + raw body) satisfies the operation's
    /// declared <paramref name="contract"/> (from <see cref="ResolveSuccessContract"/>):
    /// <list type="bullet">
    ///   <item><description><b>Status fidelity</b> — the actual status MUST equal the single declared
    ///     2xx (a spec-declares-200-but-runtime-serves-201 is RED).</description></item>
    ///   <item><description><b>204</b> — the body MUST be empty (there is no schema to match).</description></item>
    ///   <item><description><b>200/201</b> — the body is parsed and matched structurally against the
    ///     declared schema via <see cref="AssertMatches"/>.</description></item>
    /// </list>
    /// </summary>
    public static void AssertSuccessMatches(JsonElement spec, SuccessContract contract, int actualStatusCode, string? body, string context)
    {
        if (actualStatusCode != contract.StatusCode)
            throw new XunitException(
                $"{context}: spec declares success status {contract.StatusCode} but the runtime returned {actualStatusCode} " +
                "(a status-code mismatch — the exact lie a mis-declared .Produces status can tell). RED.");

        if (contract.StatusCode == StatusCode204)
        {
            if (!string.IsNullOrEmpty(body))
                throw new XunitException($"{context}: spec declares 204 No Content but the runtime response carries a body: {Truncate(body)}. RED.");
            return;
        }

        if (contract.Schema is not JsonElement schema)
            throw new XunitException($"{context}: contract for {contract.StatusCode} carries no schema (programming error).");
        if (string.IsNullOrWhiteSpace(body))
            throw new XunitException($"{context}: spec declares a {contract.StatusCode} JSON body but the runtime response body is empty. RED.");

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
        if (contract.StatusCode != 200 || contract.Schema is not JsonElement schema)
            throw new XunitException(
                $"{method.ToUpperInvariant()} {path} declares {contract.StatusCode}, not 200 — resolve it via ResolveSuccessContract.");
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
