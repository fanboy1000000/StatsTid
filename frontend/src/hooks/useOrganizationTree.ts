// S99 / TASK-9901 — the Organisation-page data layer (the FE half of the
// redesigned Global administration → Organisation screen). Consumes the S98
// aggregated tree GET + the S98 structural mutations (delete / move) and the
// S99 name-only create.
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the Enhed surface is REMOVED. The
// aggregated tree is now MAO → Organisation only; the Organisation nodes no
// longer carry an `enheder` array.
//
// Backend contract (S98 + the S99 name-only create adaptation):
//   GET    /api/admin/organizations/tree          → { tree: MaoNode[] } (visibility-bounded)
//   POST   /api/admin/organizations               → name-only {orgName, orgType, parentOrgId?} → 201
//   PUT    /api/admin/organizations/{id}           → rename {orgName} (COALESCE-safe)
//   DELETE /api/admin/organizations/{id}           → soft-delete; 422 {error, employeeCount} if blocked
//   PUT    /api/admin/organizations/{id}/move      → {newParentOrgId}; 400 shape / 422 semantic

import { useState, useCallback, useEffect } from 'react'
import { apiClient } from '../lib/api'

/** An Organisation node (depth 1) under a MAO. Holds the rolled-up employee
    count (Organisations are leaves of the structural tree). */
export interface TreeOrganisationNode {
  orgId: string
  orgName: string
  orgType: 'ORGANISATION'
  parentOrgId: string
  materializedPath: string
  employeeCount: number
}

/** A Ministeransvarsområde node (depth 0, root). Groups organisations. */
export interface TreeMaoNode {
  orgId: string
  orgName: string
  orgType: 'MAO'
  employeeCount: number
  organisations: TreeOrganisationNode[]
}

interface TreeResponse {
  tree: TreeMaoNode[]
}

/**
 * The aggregated org tree (GET /tree). Returns the visibility-bounded MAO roots
 * with their organisations and the rolled-up employee counts (never stored —
 * always server-aggregated). `fetchTree` is exposed so every mutation can
 * re-pull the tree (the counts roll up).
 */
export function useOrganizationTree() {
  const [tree, setTree] = useState<TreeMaoNode[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchTree = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<TreeResponse>('/api/admin/organizations/tree')
    if (result.ok) {
      setTree(result.data.tree ?? [])
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    void fetchTree()
  }, [fetchTree])

  return { tree, loading, error, fetchTree }
}
