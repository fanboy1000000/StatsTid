using System.Text.Json;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S101 / TASK-10102 — shared assertions over a parsed <see cref="JsonElement"/> for endpoint
/// RESPONSE-SHAPE contract tests. These pin the exact wire shape the FE list-hooks consume so the
/// recurring "fetchEnheder" bug class (S97 → S99 → S100 — a hook test mocks the right envelope
/// [vitest green] while the real endpoint serves a different shape → prod breaks) is caught by a
/// RED backend test the moment the shape drifts.
///
/// <para>Two shape families: an ENVELOPE (an OBJECT with a named array property, e.g.
/// <c>{ enheder: [...] }</c> / <c>{ tree: [...] }</c>) vs a BARE ARRAY (the top-level JSON IS the
/// array, e.g. <c>GET /api/admin/organizations</c>). Mixing them up IS the bug — assert the family
/// explicitly. <see cref="HasFields"/> pins required-PRESENCE (additive backend fields don't break
/// the contract); <see cref="FieldKind"/> pins nullability/kind (e.g. <c>parentEnhedId</c> is
/// <see cref="JsonValueKind.Null"/> at a root and <see cref="JsonValueKind.String"/> at a child).</para>
///
/// <para>The camelCase keys are asserted LITERALLY (<c>"enhedId"</c> etc.) — this is the load-bearing
/// guard catching any future global <c>AddJsonOptions</c>/serializer-policy regression RED (the
/// .NET 8 minimal-API <c>JsonSerializerDefaults.Web</c> default camelCases the PascalCase records).</para>
/// </summary>
public static class ContractAssert
{
    /// <summary>Asserts <paramref name="root"/> is an OBJECT carrying the array property
    /// <paramref name="arrayProperty"/> (the envelope shape, e.g. <c>{ enheder: [...] }</c>) — NOT a
    /// bare array. Returns the inner array element for further assertions.
    /// RED-on-old: an envelope→bare-array drift (the S97/S99/S100 bug) fails here.</summary>
    public static JsonElement IsEnvelope(JsonElement root, string arrayProperty)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new XunitException(
                $"Expected an envelope OBJECT carrying '{arrayProperty}', but the root JSON was '{root.ValueKind}' " +
                $"(a bare array where an envelope was expected — the 'fetchEnheder' bug).");
        if (!root.TryGetProperty(arrayProperty, out var array))
            throw new XunitException($"Envelope is missing the '{arrayProperty}' property. Present keys: {KeysOf(root)}.");
        if (array.ValueKind != JsonValueKind.Array)
            throw new XunitException($"Envelope property '{arrayProperty}' is '{array.ValueKind}', expected an Array.");
        return array;
    }

    /// <summary>Asserts <paramref name="root"/> is a BARE ARRAY at the top level (NOT an envelope
    /// object). RED-on-old: a bare-array→envelope drift fails here.</summary>
    public static JsonElement IsArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new XunitException(
                $"Expected a BARE ARRAY at the root, but the root JSON was '{root.ValueKind}' " +
                $"(an envelope where a bare array was expected).");
        return root;
    }

    /// <summary>Asserts <paramref name="obj"/> is an object carrying EVERY <paramref name="fields"/>
    /// (the exact camelCase keys, literally). Required-PRESENCE only — additive backend fields are
    /// allowed (an additive field must NOT break the contract). RED-on-old: a dropped field fails.</summary>
    public static void HasFields(JsonElement obj, params string[] fields)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new XunitException($"Expected an object to check fields on, got '{obj.ValueKind}'.");
        var missing = fields.Where(f => !obj.TryGetProperty(f, out _)).ToList();
        if (missing.Count > 0)
            throw new XunitException(
                $"Object is missing required field(s) [{string.Join(", ", missing)}]. Present keys: {KeysOf(obj)}.");
    }

    /// <summary>Asserts the property <paramref name="field"/> on <paramref name="obj"/> is present and
    /// its <see cref="JsonValueKind"/> is one of <paramref name="allowedKinds"/> (kind/nullability —
    /// e.g. <c>parentEnhedId</c> may be <see cref="JsonValueKind.Null"/> at a root or
    /// <see cref="JsonValueKind.String"/> at a child).</summary>
    public static void FieldKind(JsonElement obj, string field, params JsonValueKind[] allowedKinds)
    {
        if (!obj.TryGetProperty(field, out var value))
            throw new XunitException($"Object is missing field '{field}'. Present keys: {KeysOf(obj)}.");
        if (!allowedKinds.Contains(value.ValueKind))
            throw new XunitException(
                $"Field '{field}' has kind '{value.ValueKind}', expected one of [{string.Join(", ", allowedKinds)}].");
    }

    private static string KeysOf(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object
            ? string.Join(", ", obj.EnumerateObject().Select(p => p.Name))
            : $"<{obj.ValueKind}>";
}
