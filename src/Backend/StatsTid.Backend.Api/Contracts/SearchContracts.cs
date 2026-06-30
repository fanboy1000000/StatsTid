namespace StatsTid.Backend.Api.Contracts;

// S106 / TASK-10603 (Enhedsspor Phase 3a, ADR-038 D5, PAT-010) — named response records for the scoped
// units + people SEARCH read GET /api/admin/search (the merged-admin search overlay).
//
// The overlay renders TWO sections — ENHEDER (units) + MEDARBEJDERE (people) — each row carrying the
// node's full PATH (the breadcrumb the overlay shows). The search is SERVER-side because the FE
// lazy-loads per Organisation and a client filter cannot see un-loaded people.
//
// The KEYSTONE (ADR-038 D5 / P7): units carry NO scope. Visibility is admitted SOLELY by the actor's
// accessible-org set (OrgScopeValidator.GetAccessibleOrgsAsync, LocalHR floor — the SAME admission the
// existing GET /api/admin/users/search + the forest read use). Units are bounded by their immutable
// organisation_id ∈ that set — there is NO per-unit visibility predicate. A scoped HR gets NO
// cross-Organisation results (units OR people). Both a matched unit's ancestor chain and a matched
// person's home-unit chain stay WITHIN that one accessible Organisation (units belong to exactly one
// org), so the path build leaks nothing.
//
// BYTE-IDENTICAL wire JSON: plain PascalCase records serialized via the .NET 8 minimal-API
// JsonSerializerDefaults.Web camelCase default — NO [JsonPropertyName]. A dropped/renamed field is a
// one-line diff a reviewer sees + is caught RED by the registered contract test
// (SearchEndpointContractTests), closing the recurring "fetchEnheder" false-green bug class (S97 →
// S99 → S100) for the search surface BEFORE a FE consumer exists.

/// <summary>The GET /api/admin/search envelope — <c>{ units: [...], people: [...], unitsTotal, peopleTotal }</c>
/// (the design's TWO-section overlay shape; NOT a bare array). Both sections are scope-bounded + capped
/// per section (default 50 / cap 200), matching the GET /api/admin/users/search pagination convention.
///
/// <para>S110 / TASK-11002 — <paramref name="UnitsTotal"/> / <paramref name="PeopleTotal"/> are the EXACT
/// per-section match counts BEFORE the page slice (the <c>matched → total → page</c> CTE already computes
/// them; they were previously discarded). They drive the overlay's honest "viser X af Y" / "N flere —
/// forfin søgningen" truncation signal — a section whose capped list is shorter than its total is
/// truncated. NOT an <c>items.length == cap</c> heuristic (which false-positives at exactly <c>cap</c>
/// real hits). Each total is >= its section's returned-item count.</para></summary>
public sealed record SearchResponse(
    IReadOnlyList<UnitSearchResult> Units,
    IReadOnlyList<PersonSearchResult> People,
    int UnitsTotal,
    int PeopleTotal);

/// <summary>A matching ACTIVE unit (ENHEDER section). <paramref name="Path"/> is the full breadcrumb
/// the overlay displays — the chain of names from the Organisation (root) DOWN to the unit's immediate
/// parent, EXCLUSIVE of the unit's own <paramref name="Name"/> (e.g. <c>["S6 Ministerie-Styrelse",
/// "S6 A Direktion"]</c> for a unit nested two deep). A top-level unit's path is just
/// <c>[OrganisationName]</c>. Every path segment is within the unit's own (accessible) Organisation —
/// the D5 boundary holds by construction (a unit's ancestors share its immutable
/// <paramref name="OrganisationId"/>).</summary>
public sealed record UnitSearchResult(
    Guid UnitId,
    string OrganisationId,
    string Type,
    string Name,
    IReadOnlyList<string> Path);

/// <summary>A matching ACTIVE person (MEDARBEJDERE section). <paramref name="OrganisationId"/> is the
/// person's immutable primary Organisation (<c>primary_org_id</c>) — the SAME id the search scope admits
/// by (D5: a person is admitted by their Organisation, never by a unit). The merged-admin FE (S107)
/// filters the search people by the Afgrænsning scope SET against this id, NOT against the fragile
/// <paramref name="Path"/> text. <paramref name="Position"/> is the live <c>employee_profiles.position</c>
/// (nullable), <paramref name="UnitName"/> is the person's home-unit name (<c>null</c> = homed directly
/// at the Organisation). <paramref name="Path"/> is the breadcrumb from the Organisation (root) DOWN to
/// and INCLUDING the home unit (the unit chain is the person's container context; their
/// <paramref name="DisplayName"/> is the leaf). An Organisation-homed person's path is just
/// <c>[OrganisationName]</c>. The chain stays within the person's (accessible) primary Organisation —
/// no cross-Organisation leak.</summary>
public sealed record PersonSearchResult(
    string UserId,
    string OrganisationId,
    string DisplayName,
    string? Position,
    string? UnitName,
    IReadOnlyList<string> Path);
