// S99 / TASK-9901 — the structural mutations for the Organisation page
// (delete + move). These are GlobalAdmin-only server-side; the FE gate is
// convenience, not the security boundary (the backend re-checks every call).
//
// Both verbs surface STATUS-TAGGED errors so the dialogs can branch without
// re-reading the response:
//   DELETE /api/admin/organizations/{id}
//     • 204 → deleted
//     • 422 { error, employeeCount } → BLOCKED (has employees) — the delete dialog
//       switches to its "Kan ikke slette" branch with the count
//   PUT    /api/admin/organizations/{id}/move  { newParentOrgId }
//     • 200 → moved
//     • 400 → shape error (missing / self / no-op parent) — inline dialog error
//     • 422 → semantic error (subject is a MAO / target not an active MAO)

import { useCallback } from 'react'
import { apiClient } from '../lib/api'

/** A status-tagged structural-mutation error. `employeeCount` is parsed from a
    422 delete-blocked body so the delete dialog renders the count directly. */
export interface OrgStructureError extends Error {
  status: number
  employeeCount?: number
  body?: unknown
}

function parseBlockedBody(error: string): { employeeCount?: number; parsed?: unknown } {
  try {
    const parsed = JSON.parse(error) as { employeeCount?: number }
    if (parsed && typeof parsed.employeeCount === 'number') {
      return { employeeCount: parsed.employeeCount, parsed }
    }
    return { parsed }
  } catch {
    return {}
  }
}

function makeStructureError(
  status: number,
  message: string,
  employeeCount?: number,
  body?: unknown,
): OrgStructureError {
  const err = new Error(message) as OrgStructureError
  err.status = status
  err.employeeCount = employeeCount
  err.body = body
  return err
}

export function useOrganizationStructure() {
  /**
   * Soft-delete an Organisation / Ministeransvarsområde. Throws an
   * `OrgStructureError` on failure: a 422 carries the parsed `employeeCount`
   * (the delete dialog's BLOCKED branch); 204 resolves void.
   */
  const deleteOrganization = useCallback(async (orgId: string): Promise<void> => {
    const result = await apiClient.delete<void>(
      `/api/admin/organizations/${encodeURIComponent(orgId)}`,
    )
    if (!result.ok) {
      const { employeeCount, parsed } =
        result.status === 422 ? parseBlockedBody(result.error) : {}
      throw makeStructureError(result.status, result.error, employeeCount, parsed)
    }
  }, [])

  /**
   * Move an Organisation under a new MAO parent. Throws an `OrgStructureError`
   * on failure so the move dialog can map 400 (shape) / 422 (semantic) to an
   * inline error; 200 resolves void (the caller re-fetches the tree).
   */
  const moveOrganization = useCallback(
    async (orgId: string, newParentOrgId: string): Promise<void> => {
      const result = await apiClient.put<void>(
        `/api/admin/organizations/${encodeURIComponent(orgId)}/move`,
        { newParentOrgId },
      )
      if (!result.ok) {
        throw makeStructureError(result.status, result.error)
      }
    },
    [],
  )

  return { deleteOrganization, moveOrganization }
}
