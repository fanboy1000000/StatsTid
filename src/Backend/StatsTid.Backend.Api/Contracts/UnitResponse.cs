using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S113 / TASK-11300 (PAT-012 strict-types): the [property: AllowedValues] unit-type discriminators —
// emitted as spec enums by the ResponseStrictTypesFilter. Domain = the units CHECK /
// UnitEndpoints.TypeRank set ('direktion','omrade','kontor','team','enhed'). NOTE: UnitListItem /
// UnitListResponse are not yet spec-reachable (GET /api/admin/units is still grandfathered) — the
// attribute there is DORMANT until that op is retrofit-typed, at which point the enum emits
// automatically.

// S104 / TASK-10403 (ADR-038 D3, PAT-010) — named response records for the units admin endpoints.
//
// BYTE-IDENTICAL wire JSON: plain PascalCase records serialized via the .NET 8 minimal-API
// JsonSerializerDefaults.Web camelCase default — NO [JsonPropertyName]. A dropped/renamed field is a
// one-line diff a reviewer sees + is caught RED by the registered contract test
// (UnitEndpointContractTests), closing the recurring "fetchEnheder" false-green bug class
// (S97 → S99 → S100) for the new units surface.

/// <summary>The single-unit response shape (the body of POST / PUT-rename / PUT-move
/// /api/admin/units). <paramref name="ParentUnitId"/> is null for a top-level unit (directly under
/// the Organisation).</summary>
public sealed record UnitResponse(
    Guid UnitId,
    string OrganisationId,
    Guid? ParentUnitId,
    [property: AllowedValues("direktion", "omrade", "kontor", "team", "enhed")] string Type,
    string Name,
    long Version);

/// <summary>One unit list row (the element of the GET /api/admin/units envelope).</summary>
public sealed record UnitListItem(
    Guid UnitId,
    string OrganisationId,
    Guid? ParentUnitId,
    [property: AllowedValues("direktion", "omrade", "kontor", "team", "enhed")] string Type,
    string Name,
    long Version);

/// <summary>The GET /api/admin/units envelope — an OBJECT wrapping the array (NOT a bare array; the
/// envelope-vs-bare-array distinction is the S97/S99 "fetchEnheder" bug — pinned by the contract
/// test).</summary>
public sealed record UnitListResponse(IReadOnlyList<UnitListItem> Units);
