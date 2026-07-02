namespace StatsTid.Backend.Api.Contracts;

// S112 / TASK-11201 (Fork B retrofit, PAT-010/PAT-012) — named response records for the two
// UnitEndpoints handlers that still returned anonymous objects (the rename/move handlers already
// return the S104 UnitResponse). EXACT shape-copies: same member names/order/nullability as the
// prior anonymous shapes, serialized camelCase via the .NET 8 JsonSerializerDefaults.Web default —
// NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>The POST /api/admin/units/{id}/leaders 200 body — the confirmed designation
/// triple.</summary>
public sealed record UnitLeaderResponse(
    Guid UnitId,
    string UserId,
    string OrganisationId);

/// <summary>The PUT /api/admin/users/{userId}/unit 200 body (the SAME-Organisation unit-assign).
/// <paramref name="UnitId"/> is null when the person is homed directly at the Organisation;
/// <paramref name="Version"/> is the bumped users row version (also on the ETag header).</summary>
public sealed record UserUnitResponse(
    string UserId,
    Guid? UnitId,
    string PrimaryOrgId,
    long Version);
