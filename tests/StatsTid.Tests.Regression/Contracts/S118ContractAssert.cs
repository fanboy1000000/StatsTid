using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — tiny shared asserts for the Pass-5 per-route spec≡runtime classes
/// (<c>S118AgreementConfigSpecRuntimeTests</c> / <c>S118EntitlementConfigSpecRuntimeTests</c> /
/// <c>S118PositionOverrideSpecRuntimeTests</c> / <c>S118WageTypeMappingSpecRuntimeTests</c> /
/// <c>S118LoginSpecRuntimeTests</c>):
///
/// <list type="bullet">
///   <item><description><see cref="AssertExactKeySet"/> — the EXACT-key-set pin backing the
///     S118 owner rulings. Ruling #1 (the dead-branch class) says a config-family create 201
///     is ALWAYS the full entity — a resurrected <c>{configId}</c>-only fallback body fails
///     here with every missing member NAMED, and a phantom extra member fails symmetrically
///     (the spec≡runtime matcher alone cannot see an EXTRA runtime member — schemas declare
///     what MUST be there, not what must NOT).</description></item>
///   <item><description><see cref="EtagVersion"/> — reads the admin ETag (<c>"&lt;version&gt;"</c>)
///     off a create/by-id response so mutations compose <c>If-Match</c> AS THE FE DOES
///     (read the version first, then send it) instead of hard-coding tokens.</description></item>
/// </list>
///
/// <para><see cref="SpecRuntimeMatcher"/> + <see cref="SpecRuntimeTestSupport"/> are consumed
/// AS-IS (the S115 compatibility contract) — this file only ADDS sibling helpers.</para>
/// </summary>
internal static class S118ContractAssert
{
    /// <summary>Assert the serialized <paramref name="obj"/> carries EXACTLY the
    /// <paramref name="expected"/> key set — both directions (missing AND extra keys fail,
    /// each named). The ruling #1 / ruling #2 pin primitive.</summary>
    public static void AssertExactKeySet(JsonElement obj, IReadOnlyCollection<string> expected, string context)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new XunitException($"{context}: expected a JSON object but got '{obj.ValueKind}'.");

        var actual = obj.EnumerateObject().Select(p => p.Name).ToList();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || extra.Count > 0)
            throw new XunitException(
                $"{context}: key-set mismatch (expected exactly {expected.Count} members, got {actual.Count}). " +
                $"Missing: [{string.Join(", ", missing)}]. Extra: [{string.Join(", ", extra)}].");
    }

    /// <summary>Parse the admin-contract <c>ETag: "&lt;version&gt;"</c> header into the version
    /// the next mutation sends as <c>If-Match</c> (the FE composition flow).</summary>
    public static long EtagVersion(HttpResponseMessage response)
    {
        var tag = response.Headers.ETag?.Tag
            ?? throw new XunitException("Expected an ETag header on the response but none was present.");
        return long.Parse(tag.Trim('"'), CultureInfo.InvariantCulture);
    }
}
