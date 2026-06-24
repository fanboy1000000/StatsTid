namespace StatsTid.Backend.Api.Contracts;

// S101 / TASK-10101 — named response records for GET /api/admin/organizations/tree.
//
// The forest is a MAO → organisations[] → enheder[] (a nested enhed sub-tree via parentEnhedId).
// These records replace the anonymous nodes assembled in AdminEndpoints (the forest loop) and
// BuildEnhedForest. The shape is BYTE-IDENTICAL on the wire (camelCase via JsonSerializerDefaults.Web;
// no [JsonPropertyName]). CRITICAL: the rollup `visibleChildren.Sum(c => (long)c.EmployeeCount)` and
// the BuildEnhedForest recursion are UNCHANGED — only the node TYPE moved from anonymous to these
// named records (each still exposes EmployeeCount / Children so the rollup + recursion compile as-is).

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

/// <summary>An ORGANISATION node (the smallest authority unit) under a MAO.
/// <paramref name="Enheder"/> is the nested enhed forest (pure display metadata).</summary>
public sealed record OrgTreeOrganisationNode(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion,
    long EmployeeCount,
    IReadOnlyList<OrgTreeEnhedNode> Enheder);

/// <summary>An enhed node inside an Organisation's nested sub-tree. <paramref name="ParentEnhedId"/>
/// is null at the root of the sub-tree; <paramref name="Level"/> is depth (root = 1);
/// <paramref name="Children"/> nests the sub-enheder. PURE DISPLAY metadata — zero authority.</summary>
public sealed record OrgTreeEnhedNode(
    Guid EnhedId,
    Guid? ParentEnhedId,
    int Level,
    string Name,
    long TaggedUserCount,
    IReadOnlyList<OrgTreeEnhedNode> Children);
