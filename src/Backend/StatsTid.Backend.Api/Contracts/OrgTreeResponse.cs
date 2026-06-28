namespace StatsTid.Backend.Api.Contracts;

// S101 / TASK-10101 — named response records for GET /api/admin/organizations/tree.
// S103 / TASK-10304 — the per-Organisation enhed nesting is retired with the legacy Enhed tables
// (the unit-based display returns in Enhedsspor Phase 3). The forest is now a MAO → organisations[].
//
// These records replace the anonymous nodes assembled in AdminEndpoints (the forest loop). The shape
// is camelCase via JsonSerializerDefaults.Web (no [JsonPropertyName]). The rollup
// `visibleChildren.Sum(c => (long)c.EmployeeCount)` is UNCHANGED.

/// <summary>The GET /api/admin/organizations/tree envelope — `{ tree: [...] }` (NOT a bare array).</summary>
public sealed record OrgTreeResponse(IReadOnlyList<OrgTreeMaoNode> Tree);

/// <summary>A MAO (root authority unit) node. <paramref name="EmployeeCount"/> sums ONLY the
/// visible child Organisations' counts. <paramref name="Organisations"/> are its visible children.</summary>
public sealed record OrgTreeMaoNode(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion,
    long EmployeeCount,
    IReadOnlyList<OrgTreeOrganisationNode> Organisations);

/// <summary>An ORGANISATION node (the smallest authority unit) under a MAO.</summary>
public sealed record OrgTreeOrganisationNode(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion,
    long EmployeeCount);
