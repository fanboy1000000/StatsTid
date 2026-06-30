// SPRINT-108 / TASK-10801 (Enhedsspor Phase 3b-2a) — the write layer for the
// merged "Organisation & medarbejdere" admin page's UNIT structure mutations,
// wired to the S104 UnitEndpoints (ADR-038 D3/D8/D10):
//
//   POST   /api/admin/units                       — create (child or top-level)
//   PUT    /api/admin/units/{id}        (If-Match) — rename
//   PUT    /api/admin/units/{id}/move   (If-Match) — re-parent (same Organisation)
//   DELETE /api/admin/units/{id}        (If-Match) — soft-delete + cascade UP
//   POST   /api/admin/units/{id}/leaders          — designate a peer leader
//   DELETE /api/admin/units/{id}/leaders/{userId}  — remove a leader designation
//
// The FE gate is UX only (P7): the backend re-checks the LocalHR floor + the
// concurrency/guards on every call — a forged request from a non-permitted actor
// is still 403'd server-side. These are non-GET mutators, so they are NOT subject
// to the GET-only contract-coverage lint (PAT-010); the URLs are still passed
// INLINE (no path-helper const) for consistency with the read hooks.
//
// Each call resolves a non-throwing `UnitMutationResult` carrying a Danish,
// user-facing message for the real failure statuses (412 stale / 409 dup-name /
// 422 parent- or member-validation / 403 / 404) so the drawer/dialog can surface
// them inline.

import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch } from '../lib/etag'
import type { UnitType } from '../pages/admin/enhedsspor/typeMaps'

/** A non-throwing mutation outcome. `error` is a Danish, user-facing message
    (empty string when `ok`). */
export interface UnitMutationResult {
  ok: boolean
  status: number
  error: string
}

/** S109 / TASK-10902 — the same-Organisation person unit-assign result, carrying
    the new `users.version` (the `/unit` endpoint bumps it) so the caller can thread
    read-your-write into a follow-up edit. `version` is null on failure. */
export interface AssignUnitResult extends UnitMutationResult {
  version: number | null
}

type MutationContext = 'create' | 'rename' | 'move' | 'delete' | 'leader' | 'assign'

const success = (): UnitMutationResult => ({ ok: true, status: 0, error: '' })

/** Map a failure status to a Danish, user-facing message. The unit create/move
    422 is a parent-validation rejection (rank / different Organisation / cycle);
    the leader 422 is the member-invariant ("a leader must be a member of the
    unit"). The unit DELETE has NO 422 — it is a confirm-and-cascade. */
function messageFor(status: number, context: MutationContext): string {
  switch (status) {
    case 412:
      return 'Enheden blev opdateret af en anden. Genindlæs og prøv igen.'
    case 409:
      return 'Der findes allerede en aktiv enhed med dette navn.'
    case 403:
      return 'Du har ikke rettigheder til denne handling.'
    case 404:
      return 'Enheden findes ikke længere. Genindlæs siden.'
    case 422:
      return context === 'leader'
        ? 'En leder skal være medarbejder i den enhed, vedkommende leder.'
        : context === 'assign'
          ? 'Den valgte placering er ugyldig (enheden er slettet eller hører til en anden organisation).'
          : 'Ugyldig placering for enheden.'
    case 400:
      return 'Ugyldige oplysninger. Tjek felterne og prøv igen.'
    default:
      return 'Noget gik galt. Prøv igen.'
  }
}

const fail = (status: number, context: MutationContext): UnitMutationResult => ({
  ok: false,
  status,
  error: messageFor(status, context),
})

export interface CreateUnitInput {
  organisationId: string
  parentUnitId: string | null
  type: UnitType
  name: string
}

export function useUnitMutations() {
  const createUnit = useCallback(async (input: CreateUnitInput): Promise<UnitMutationResult> => {
    const result = await apiClient.post('/api/admin/units', {
      organisationId: input.organisationId,
      parentUnitId: input.parentUnitId,
      type: input.type,
      name: input.name,
    })
    return result.ok ? success() : fail(result.status, 'create')
  }, [])

  const renameUnit = useCallback(
    async (unitId: string, name: string, version: number): Promise<UnitMutationResult> => {
      const result = await apiFetchWithEtag(`/api/admin/units/${encodeURIComponent(unitId)}`, {
        method: 'PUT',
        headers: { 'If-Match': formatVersionAsIfMatch(version) },
        body: JSON.stringify({ name }),
      })
      return result.ok ? success() : fail(result.status, 'rename')
    },
    [],
  )

  const moveUnit = useCallback(
    async (unitId: string, newParentUnitId: string | null, version: number): Promise<UnitMutationResult> => {
      const result = await apiFetchWithEtag(`/api/admin/units/${encodeURIComponent(unitId)}/move`, {
        method: 'PUT',
        headers: { 'If-Match': formatVersionAsIfMatch(version) },
        body: JSON.stringify({ newParentUnitId }),
      })
      return result.ok ? success() : fail(result.status, 'move')
    },
    [],
  )

  const deleteUnit = useCallback(
    async (unitId: string, version: number): Promise<UnitMutationResult> => {
      const result = await apiFetchWithEtag(`/api/admin/units/${encodeURIComponent(unitId)}`, {
        method: 'DELETE',
        headers: { 'If-Match': formatVersionAsIfMatch(version) },
      })
      return result.ok ? success() : fail(result.status, 'delete')
    },
    [],
  )

  const designateLeader = useCallback(
    async (unitId: string, userId: string): Promise<UnitMutationResult> => {
      const result = await apiClient.post(`/api/admin/units/${encodeURIComponent(unitId)}/leaders`, { userId })
      return result.ok ? success() : fail(result.status, 'leader')
    },
    [],
  )

  const removeLeader = useCallback(
    async (unitId: string, userId: string): Promise<UnitMutationResult> => {
      const result = await apiClient.delete(
        `/api/admin/units/${encodeURIComponent(unitId)}/leaders/${encodeURIComponent(userId)}`,
      )
      return result.ok ? success() : fail(result.status, 'leader')
    },
    [],
  )

  // S109 / TASK-10902 — the SAME-Organisation person unit-assign (PUT
  // /api/admin/users/{userId}/unit, If-Match on users.version). `unitId` null =
  // home directly at the Organisation. A target unit in a DIFFERENT Organisation
  // is a transfer and is 422'd here by design (it must go through PUT
  // /users/{id} with primaryOrgId — the placement router never sends a cross-Org
  // unit here). Returns the new users.version for read-your-write threading.
  const assignUserUnit = useCallback(
    async (userId: string, unitId: string | null, version: number): Promise<AssignUnitResult> => {
      const result = await apiFetchWithEtag<{ userId: string; unitId: string | null; primaryOrgId: string; version: number }>(
        `/api/admin/users/${encodeURIComponent(userId)}/unit`,
        {
          method: 'PUT',
          headers: { 'If-Match': formatVersionAsIfMatch(version) },
          body: JSON.stringify({ unitId }),
        },
      )
      if (result.ok) {
        return { ok: true, status: result.data.status, error: '', version: result.data.data.version }
      }
      return { ...fail(result.status, 'assign'), version: null }
    },
    [],
  )

  return { createUnit, renameUnit, moveUnit, deleteUnit, designateLeader, removeLeader, assignUserUnit }
}
