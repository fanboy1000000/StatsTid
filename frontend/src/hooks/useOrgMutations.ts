// SPRINT-108 / TASK-10802 (Enhedsspor Phase 3b-2a) — the write layer for the
// merged "Organisation & medarbejdere" admin page's ORG / MAO structure
// mutations, wired to the S98/S99 org endpoints in AdminEndpoints.cs:
//
//   POST   /api/admin/organizations                 — create (MAO root or
//                                                      Organisation under a MAO)
//   PUT    /api/admin/organizations/{orgId}         — rename (name-only)
//   PUT    /api/admin/organizations/{orgId}/move    — re-parent under a new MAO
//   DELETE /api/admin/organizations/{orgId}         — soft-delete (blocked if it
//                                                      still has employees → 422)
//
// The FE gate is UX only (P7): the backend re-checks the role floor on every call
// (org create/rename = LocalAdmin; MAO-create + org move/delete = GlobalAdmin) and
// re-runs the structural guards (the blocked-if-employees count, the active-MAO
// target check) — a forged request from a non-permitted actor is still 403'd /
// 422'd server-side. These are non-GET mutators, so they are NOT subject to the
// GET-only contract-coverage lint (PAT-010); the URLs are still passed INLINE.
//
// Each call resolves a non-throwing `OrgMutationResult` (mirrors useUnitMutations)
// carrying a Danish, user-facing message for the real failure statuses + the
// parsed `employeeCount` from a 422 delete-blocked body so the delete dialog can
// flip to its BLOCKED branch with the authoritative count.

import { useCallback } from 'react'
import { apiClient } from '../lib/api'

export type OrgType = 'MAO' | 'ORGANISATION'

type OrgMutationContext = 'create' | 'rename' | 'move' | 'delete'

/** A non-throwing org-mutation outcome. `error` is a Danish, user-facing message
    (empty when `ok`); `employeeCount` is set only for a 422 delete-blocked body. */
export interface OrgMutationResult {
  ok: boolean
  status: number
  error: string
  employeeCount?: number
}

const success = (): OrgMutationResult => ({ ok: true, status: 0, error: '' })

/** Map a failure status to a Danish, user-facing message. The move 400 is a shape
    rejection (missing / self / no-op parent); the move 422 is a semantic rejection
    (subject is a MAO / target is not an active MAO). The delete 422 is handled via
    `employeeCount` (the BLOCKED branch), not this message. */
function messageFor(status: number, context: OrgMutationContext): string {
  switch (status) {
    case 403:
      return 'Du har ikke rettigheder til denne handling.'
    case 404:
      return 'Organisationen findes ikke længere. Genindlæs siden.'
    case 409:
      return 'Der findes allerede en aktiv organisation med dette navn.'
    case 422:
      return context === 'move'
        ? 'Flytningen blev afvist: målet skal være et aktivt ministerområde.'
        : 'Handlingen blev afvist.'
    case 400:
      return context === 'move'
        ? 'Ugyldig placering. Vælg et andet ministerområde.'
        : 'Ugyldige oplysninger. Tjek felterne og prøv igen.'
    default:
      return 'Noget gik galt. Prøv igen.'
  }
}

const fail = (status: number, context: OrgMutationContext): OrgMutationResult => ({
  ok: false,
  status,
  error: messageFor(status, context),
})

/** Parse the `employeeCount` from a 422 delete-blocked body (a JSON string from
    apiClient's error text). Falls back to undefined for a non-JSON body. */
function parseBlockedCount(error: string): number | undefined {
  try {
    const parsed = JSON.parse(error) as { employeeCount?: number }
    return typeof parsed?.employeeCount === 'number' ? parsed.employeeCount : undefined
  } catch {
    return undefined
  }
}

export interface CreateOrgInput {
  orgName: string
  orgType: OrgType
  /** The parent MAO orgId for an ORGANISATION; null for a top-level MAO. */
  parentOrgId: string | null
}

export function useOrgMutations() {
  const createOrg = useCallback(async (input: CreateOrgInput): Promise<OrgMutationResult> => {
    const result = await apiClient.post('/api/admin/organizations', {
      orgName: input.orgName,
      orgType: input.orgType,
      parentOrgId: input.parentOrgId,
    })
    return result.ok ? success() : fail(result.status, 'create')
  }, [])

  const renameOrg = useCallback(
    async (orgId: string, orgName: string): Promise<OrgMutationResult> => {
      const result = await apiClient.put(
        `/api/admin/organizations/${encodeURIComponent(orgId)}`,
        { orgName },
      )
      return result.ok ? success() : fail(result.status, 'rename')
    },
    [],
  )

  const moveOrg = useCallback(
    async (orgId: string, newParentOrgId: string): Promise<OrgMutationResult> => {
      const result = await apiClient.put(
        `/api/admin/organizations/${encodeURIComponent(orgId)}/move`,
        { newParentOrgId },
      )
      return result.ok ? success() : fail(result.status, 'move')
    },
    [],
  )

  const deleteOrg = useCallback(async (orgId: string): Promise<OrgMutationResult> => {
    const result = await apiClient.delete(
      `/api/admin/organizations/${encodeURIComponent(orgId)}`,
    )
    if (result.ok) return success()
    const employeeCount = result.status === 422 ? parseBlockedCount(result.error) : undefined
    return { ...fail(result.status, 'delete'), employeeCount }
  }, [])

  return { createOrg, renameOrg, moveOrg, deleteOrg }
}
