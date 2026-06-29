namespace StatsTid.Backend.Api.Contracts;

// S106 / TASK-10601 (Enhedsspor Phase 3a, ADR-038 D1/D5, PAT-010) — named response records for the
// unified scoped FOREST read GET /api/admin/units/forest (the merged-admin left tree).
//
// The forest MERGES `organizations` (MAO + Organisation) with `units` (direktion→enhed beneath each
// Organisation) for DISPLAY only. The KEYSTONE (ADR-038 D5 / P7): units carry NO scope. Visibility is
// admitted SOLELY by the parent Organisation's OrgScopeValidator.GetAccessibleOrgsAsync (the exact
// accessible-org set — no descendant expansion); a unit node is included iff its organisation_id ∈ the
// actor's accessible orgs. There is NO per-unit visibility predicate and NO descendant/sibling
// widening. MAO ancestors render as read-only context (exactly as the S98 /organizations/tree does).
//
// BYTE-IDENTICAL wire JSON: plain PascalCase records serialized via the .NET 8 minimal-API
// JsonSerializerDefaults.Web camelCase default — NO [JsonPropertyName]. A dropped/renamed field is a
// one-line diff a reviewer sees + is caught RED by the registered contract test
// (ForestEndpointContractTests), closing the recurring "fetchEnheder" false-green bug class for the
// new forest surface BEFORE a FE consumer exists.

/// <summary>The GET /api/admin/units/forest envelope — <c>{ forest: [...] }</c> (NOT a bare array — the
/// S97/S99/S100 envelope-vs-bare-array distinction). The roots are the visible MAOs.</summary>
public sealed record ForestResponse(IReadOnlyList<ForestMaoNode> Forest);

/// <summary>A MAO (root authority unit) node — read-only display context for a scoped HR.
/// <paramref name="MemberCount"/> sums ONLY the visible child Organisations' reconciled totals (a
/// scoped HR's MAO total never includes a sibling Organisation it cannot see — the D5 count
/// non-leakage invariant).</summary>
public sealed record ForestMaoNode(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    long MemberCount,
    IReadOnlyList<ForestOrganisationNode> Organisations);

/// <summary>An ORGANISATION node (the smallest authority unit — the scope anchor) under a MAO.
/// <paramref name="MemberCount"/> = Σ(its top-level units' rolled-up counts) + <paramref
/// name="DirectMemberCount"/> (the Organisation-homed <c>unit_id</c>-NULL active users), which
/// reconciles to the S98 <c>employeeCount</c> by <c>primary_org_id</c>. <paramref name="Units"/> are
/// its TOP-LEVEL units (the unit sub-forest nests beneath them).</summary>
public sealed record ForestOrganisationNode(
    string OrgId,
    string OrgName,
    string OrgType,
    string? ParentOrgId,
    string MaterializedPath,
    string AgreementCode,
    string OkVersion,
    long MemberCount,
    long DirectMemberCount,
    IReadOnlyList<ForestUnitNode> Units);

/// <summary>A unit node (direktion…enhed) beneath an Organisation. <paramref name="Level"/> is the
/// DERIVED depth in the unit sub-tree (a top-level unit directly under the Organisation = 1; NO stored
/// level/path — the S100 enhed precedent). <paramref name="DirectMemberCount"/> = this unit's own
/// direct members (active users with <c>unit_id</c> = this unit); <paramref name="MemberCount"/> =
/// the rolled-up total (this unit + all descendant units, summed in memory up the depth-≤5 tree —
/// units ≪ people, so NO recursive SQL CTE). Units carry NO scope (the LOCKED D5 boundary) — these
/// nodes are display structure + counts only.</summary>
public sealed record ForestUnitNode(
    Guid UnitId,
    string OrganisationId,
    Guid? ParentUnitId,
    string Type,
    string Name,
    int Level,
    long Version,
    long DirectMemberCount,
    long MemberCount,
    IReadOnlyList<ForestUnitNode> Children);
