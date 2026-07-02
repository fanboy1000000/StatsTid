namespace StatsTid.Backend.Api.Contracts;

// S112 / TASK-11201 (Fork B retrofit, PAT-010/PAT-012) — the named response record for the
// organization admin MUTATIONS (POST create 201 / PUT update 200 / PUT move 200 on
// /api/admin/organizations). All three handlers previously returned the SAME anonymous 7-field
// shape; this record is an EXACT shape-copy — member order mirrors the prior anonymous member
// order (orgId … okVersion), serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default — NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>The organization mutation response body (POST create / PUT update / PUT move).
/// <paramref name="ParentOrgId"/> is null for a MAO (root). AgreementCode/OkVersion are the
/// vestigial org-tree fields (S99) — carried unchanged.</summary>
public sealed record OrganizationResponse(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion);
