namespace StatsTid.Backend.Api.Contracts;

// S101 / TASK-10101 — named response records (the "fetchEnheder" bug class, Pass 1).
//
// These replace the anonymous `Results.Ok(new { … })` shapes of GET /api/admin/enheder so the
// wire contract is a NAMED, diff-reviewable type instead of an inline literal. The wire JSON is
// BYTE-IDENTICAL: the .NET 8 minimal-API JsonSerializerDefaults.Web default applies the camelCase
// policy (no ConfigureHttpJsonOptions/AddJsonOptions override exists in Program.cs), so PascalCase
// members serialize to the same camelCase keys the FE useEnheder.fetchEnheder hook consumes. NO
// [JsonPropertyName] attributes — per-member attrs would MASK a future global policy regression;
// the contract tests assert the camelCase keys LITERALLY instead (Pass1EndpointContractTests).
// `null` is still emitted (no DefaultIgnoreCondition=WhenWritingNull) → parentEnhedId: null at root.

/// <summary>The GET /api/admin/enheder envelope — an OBJECT wrapping the array (NOT a bare array;
/// the S97/S99/S100 bug was the FE mocking the envelope while the endpoint shape drifted). The
/// `enheder` property name is the load-bearing envelope key.</summary>
public sealed record EnhedListResponse(IReadOnlyList<EnhedListItem> Enheder);

/// <summary>One flat enhed row. <paramref name="ParentEnhedId"/> is null for a root enhed (level 1);
/// <paramref name="Level"/> is the server-derived depth (root = 1).</summary>
public sealed record EnhedListItem(
    Guid EnhedId,
    string OrganisationId,
    string Name,
    long Version,
    Guid? ParentEnhedId,
    int Level);
