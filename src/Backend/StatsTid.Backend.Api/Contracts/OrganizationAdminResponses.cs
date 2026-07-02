using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S113 / TASK-11300 (PAT-012 strict-types): the [property: AllowedValues] orgType discriminator —
// emitted as a spec enum by the ResponseStrictTypesFilter. Domain = the init.sql CHECK
// (org_type IN ('MAO','ORGANISATION')).

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
    [property: AllowedValues("MAO", "ORGANISATION")] string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion);
