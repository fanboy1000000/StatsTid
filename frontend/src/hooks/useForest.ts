// SPRINT-107 / TASK-10702 (Enhedsspor Phase 3b-1) — the data layer for the merged
// "Organisation & medarbejdere" admin page's LEFT org-structure tree.
//
// Consumes the S106 unified scoped FOREST read:
//   GET /api/admin/units/forest → { forest: ForestMaoNode[] }
//
// The forest MERGES `organizations` (MAO + Organisation) with `units`
// (direktion→enhed beneath each Organisation) for DISPLAY only. It is ALREADY
// scope-bounded server-side (ADR-038 D5 / P7): a unit node is admitted SOLELY by
// its parent Organisation's accessible-org set — there is NO per-unit visibility
// predicate and NO client-side scope logic. The FE renders EXACTLY what the read
// returns; MAO ancestors are read-only context.
//
// The named interfaces below mirror the backend's serialized (camelCase) wire
// shape VERBATIM (src/Backend/.../Contracts/ForestContracts.cs), pinned by
// ForestEndpointContractTests — the S97→S99→S100 "fetchEnheder" drift-class fix:
// the FE type must NOT diverge from the backend's actual JSON.
//
// LINT (PAT-010): the URL is passed INLINE as a literal to apiClient.get so the
// contract-coverage lint (tools/check_endpoint_contracts.py) can enumerate it —
// a path-helper const would evade the gate (the documented blind spot).

import { useState, useCallback, useEffect } from 'react'
import { apiClient } from '../lib/api'
import { coerceApiResponse, type Assert, type AssertFieldsInSpec } from '../lib/apiNarrow'
import type { UnitType } from '../pages/admin/enhedsspor/typeMaps'

/** A unit node (direktion…enhed) beneath an Organisation. `level` is the DERIVED
    depth in the unit sub-tree (a top-level unit directly under the Organisation =
    1). `directMemberCount` = this unit's own direct members; `memberCount` = the
    rolled-up total (this unit + all descendant units). Units carry NO scope. */
export interface ForestUnitNode {
  unitId: string
  organisationId: string
  parentUnitId: string | null
  type: UnitType
  name: string
  level: number
  version: number
  directMemberCount: number
  memberCount: number
  children: ForestUnitNode[]
}

/** An ORGANISATION node (the smallest authority unit — the scope anchor) under a
    MAO. `memberCount` = Σ(top-level units' rolled-up counts) + `directMemberCount`
    (the Organisation-homed, unit-less active users). `units` are its TOP-LEVEL
    units (the unit sub-forest nests beneath them via `children`). */
export interface ForestOrganisationNode {
  orgId: string
  orgName: string
  orgType: 'ORGANISATION'
  parentOrgId: string | null
  materializedPath: string
  agreementCode: string
  okVersion: string
  memberCount: number
  directMemberCount: number
  units: ForestUnitNode[]
}

/** A MAO (root authority unit) node — read-only display context for a scoped HR.
    `memberCount` sums ONLY the visible child Organisations (the D5 count
    non-leakage invariant — a scoped HR's MAO total never includes a sibling
    Organisation it cannot see). */
export interface ForestMaoNode {
  orgId: string
  orgName: string
  orgType: 'MAO'
  parentOrgId: string | null
  materializedPath: string
  memberCount: number
  organisations: ForestOrganisationNode[]
}

/** The GET /api/admin/units/forest envelope — `{ forest: [...] }` (NOT a bare
    array — the S97/S99 envelope-vs-bare-array distinction). The roots are the
    visible MAOs. */
export interface ForestResponse {
  forest: ForestMaoNode[]
}

// S111 / TASK-11102 — compile-time drift guards: every field these FE-strict
// interfaces read must exist in the matching spec schema. A renamed/removed
// backend field → `tsc` error here (the S97→S100 "fetchEnheder" drift class).
export type _ForestDrift = [
  Assert<AssertFieldsInSpec<ForestResponse, 'StatsTid.Backend.Api.Contracts.ForestResponse'>>,
  Assert<AssertFieldsInSpec<ForestMaoNode, 'StatsTid.Backend.Api.Contracts.ForestMaoNode'>>,
  Assert<
    AssertFieldsInSpec<ForestOrganisationNode, 'StatsTid.Backend.Api.Contracts.ForestOrganisationNode'>
  >,
  Assert<AssertFieldsInSpec<ForestUnitNode, 'StatsTid.Backend.Api.Contracts.ForestUnitNode'>>,
]

/**
 * The unified scoped forest (GET /api/admin/units/forest) for the left
 * org-structure tree. Returns the visibility-bounded MAO roots with their
 * Organisations and the nested unit sub-trees + the rolled-up member counts
 * (never stored — always server-aggregated). `fetchForest` is exposed so the
 * page can re-pull (TASK-10704's Afgrænsning re-fetches / re-filters).
 */
export function useForest() {
  const [forest, setForest] = useState<ForestMaoNode[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchForest = useCallback(async () => {
    setLoading(true)
    setError(null)
    // S111 / TASK-11102 — typed via the OpenAPI literal path key (no hand-written
    // `T`). `result.data.forest` is type-checked against the spec envelope (a
    // renamed `forest` key → `tsc` error here); `coerceApiResponse` re-narrows the
    // spec-loose element type to the FE-strict `ForestMaoNode[]`.
    const result = await apiClient.get('/api/admin/units/forest')
    if (result.ok) {
      setForest(coerceApiResponse<ForestMaoNode[]>(result.data.forest ?? []))
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    void fetchForest()
  }, [fetchForest])

  return { forest, loading, error, fetchForest }
}
