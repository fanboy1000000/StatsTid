namespace StatsTid.Backend.Api.Contracts;

// S119 / TASK-11900 (Fork B retrofit Pass 6, PAT-010/PAT-012) — named response records for the
// project endpoints (ProjectEndpoints). Each record is an EXACT shape-copy of the anonymous
// object its handler previously returned: same member NAMES, same ORDER, same nullability —
// serialized camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO
// [JsonPropertyName]. BYTE-IDENTICAL wire JSON on all sites (S119 is a zero-wire-change pass).
//
// NOTE: no response in this family carries isActive — every read path filters
// is_active = TRUE in the repository, so the flag is invariant-true and never emitted (the
// FE's phantom Project.isActive was prod bug #7, fixed FE-side at S119). No If-Match/ETag
// anywhere in this family; the two DELETEs are declared-204 body-less (no record).

/// <summary>
/// The 4-member project row — ONE record for the two sibling success sites (list rows on
/// GET /api/projects/{orgId} AND the POST create 201; identical shape — the S112 sibling
/// rule).
/// </summary>
public sealed record ProjectResponse(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int SortOrder);

/// <summary>
/// One row of GET /api/projects/{orgId}/available (bare array, 5 members) — the project row
/// plus the per-employee <c>selected</c> flag. A SEPARATE sibling of
/// <see cref="ProjectResponse"/> (the extra member makes it a different wire shape — never
/// model via extends/inheritance, the S115 lie-amplifier lesson).
/// </summary>
public sealed record AvailableProjectResponse(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int SortOrder,
    bool Selected);

/// <summary>
/// The POST /api/projects/{orgId}/select/{projectId} 200 echo (2 members). <c>selected</c> is
/// always true on the success path (carried for shape fidelity with the prior anonymous
/// object). The op binds NO request DTO — declared in tools/openapi-bodyless-declared.txt
/// (the S116 rule). The deselect DELETE returns 204 body-less (no record).
/// </summary>
public sealed record ProjectSelectionResponse(
    Guid ProjectId,
    bool Selected);

/// <summary>
/// The PUT /api/projects/{orgId}/{projectId} 200 echo (2 members). <c>updated</c> is always
/// true on the success path (shape fidelity).
/// </summary>
public sealed record ProjectUpdateResponse(
    Guid ProjectId,
    bool Updated);
