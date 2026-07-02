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
// S113 / TASK-11301 — the response types are the GENERATED spec types VERBATIM
// (`api-types.ts`, strict since the S113 `required`-emission): the S111 coercion
// + drift-guard scaffolding (the deleted apiNarrow module) is gone; a renamed or
// removed backend field is now a direct `tsc` error at the `setForest` call
// (the S97→S99→S100 "fetchEnheder" drift class, closed structurally). NOTE the
// spec models `orgType` as the shared `"MAO" | "ORGANISATION"` union on BOTH
// node kinds (the per-kind literal cannot be expressed by the generator); the
// two node kinds stay structurally distinct via `organisations` vs `units`.
//
// LINT (PAT-010): the URL is passed INLINE as a literal to apiClient.get so the
// contract-coverage lint (tools/check_endpoint_contracts.py) can enumerate it —
// a path-helper const would evade the gate (the documented blind spot).

import { useState, useCallback, useEffect } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

type Schemas = components['schemas']

/** A unit node (direktion…enhed) beneath an Organisation. `level` is the DERIVED
    depth in the unit sub-tree (a top-level unit directly under the Organisation =
    1). `directMemberCount` = this unit's own direct members; `memberCount` = the
    rolled-up total (this unit + all descendant units). Units carry NO scope. */
export type ForestUnitNode = Schemas['StatsTid.Backend.Api.Contracts.ForestUnitNode']

/** An ORGANISATION node (the smallest authority unit — the scope anchor) under a
    MAO. `memberCount` = Σ(top-level units' rolled-up counts) + `directMemberCount`
    (the Organisation-homed, unit-less active users). `units` are its TOP-LEVEL
    units (the unit sub-forest nests beneath them via `children`). */
export type ForestOrganisationNode = Schemas['StatsTid.Backend.Api.Contracts.ForestOrganisationNode']

/** A MAO (root authority unit) node — read-only display context for a scoped HR.
    `memberCount` sums ONLY the visible child Organisations (the D5 count
    non-leakage invariant — a scoped HR's MAO total never includes a sibling
    Organisation it cannot see). */
export type ForestMaoNode = Schemas['StatsTid.Backend.Api.Contracts.ForestMaoNode']

/** The GET /api/admin/units/forest envelope — `{ forest: [...] }` (NOT a bare
    array — the S97/S99 envelope-vs-bare-array distinction). The roots are the
    visible MAOs. */
export type ForestResponse = Schemas['StatsTid.Backend.Api.Contracts.ForestResponse']

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
    // `T`). `result.data` IS the strict spec `ForestResponse` (a renamed `forest`
    // key or node field → `tsc` error here; no coercion — S113).
    const result = await apiClient.get('/api/admin/units/forest')
    if (result.ok) {
      setForest(result.data.forest)
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
