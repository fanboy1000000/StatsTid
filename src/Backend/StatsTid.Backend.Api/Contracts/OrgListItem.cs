using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S113 / TASK-11300 (PAT-012 strict-types): the [property: AllowedValues] orgType discriminator —
// emitted as a spec enum by the ResponseStrictTypesFilter. Domain = the init.sql CHECK
// (org_type IN ('MAO','ORGANISATION')).

// S101 / TASK-10101 — the named element record for GET /api/admin/organizations.
//
// This endpoint returns a BARE ARRAY (Results.Ok(list.Select(MapOrgResponse))) — the response is
// NOT an envelope; the FE useAdmin hook consumes apiClient.get<Organization[]> directly. The record
// is the array ELEMENT (replaces the anonymous shape returned by MapOrgResponse). BYTE-IDENTICAL wire
// JSON (camelCase via JsonSerializerDefaults.Web; no [JsonPropertyName]). The fields mirror the
// CURRENT MapOrgResponse exactly: orgId, orgName, orgType, parentOrgId, materializedPath,
// agreementCode, okVersion — okVersion is the S76b additive read field for the unified-editor create.

/// <summary>One organisation list row (the element of the bare-array GET /api/admin/organizations
/// response). <paramref name="ParentOrgId"/> is null for a MAO (root).</summary>
public sealed record OrgListItem(
    string OrgId,
    string OrgName,
    [property: AllowedValues("MAO", "ORGANISATION")] string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion);
