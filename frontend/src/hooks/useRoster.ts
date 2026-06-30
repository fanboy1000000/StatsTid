// SPRINT-107 / TASK-10703 (Enhedsspor Phase 3b-1) — the data layer for the
// recursive right "Struktur" of the merged "Organisation & medarbejdere" admin
// page.
//
// Consumes the S106 unit-tagged per-Organisation roster read:
//   GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere
//     → { employees: Row[], pendingCountByManager: {...}, nameResolution: {...} }
//
// LAZY + CACHED PER ORGANISATION. The Struktur recurses a unit and its child
// units — which all live in ONE Organisation — so a single roster fetch covers
// the whole sub-tree. The hook fetches a roster the first time an Organisation is
// needed and caches it; clicking through sibling/child units of the same
// Organisation re-uses the cached roster (no refetch). Selecting a unit in a
// DIFFERENT Organisation fetches (and caches) that one.
//
// The named interfaces mirror the backend's serialized (camelCase) wire shape
// VERBATIM, pinned by the S106 roster contract test (RosterEndpoint /
// S106RosterUnitTagTests) — the S97→S99→S100 "fetchEnheder" drift-class fix: the
// FE type must NOT diverge from the backend's actual JSON.
//
// LINT (PAT-010): the URL is passed INLINE as a literal to apiClient.get so the
// contract-coverage lint (tools/check_endpoint_contracts.py) can enumerate it —
// a path-helper const would evade the gate (the documented blind spot).

import { useState, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import { coerceApiResponse, type Assert, type AssertFieldsInSpec } from '../lib/apiNarrow'

/** Per-away-manager vikar annotation — present IFF this person is an away-manager
    currently covered by an active vikar (the absent leader's own row). The
    inverse "Vikar for X" tag on the stand-in is derived by INVERTING this within
    the loaded set (no separate field). */
export interface RosterOutgoingVikar {
  vikarUserId: string
  vikarDisplayName: string
  untilDate: string // ISO 'YYYY-MM-DD'
  reason: string
}

/** One employee row in the unit-tagged structural roster (field names are the
    S106 contract, verbatim camelCase). */
export interface RosterRow {
  employeeId: string
  displayName: string
  position: string | null
  /** the person's assigned active PRIMARY manager — THE TREE KEY (raw edge). */
  structuralApproverId: string | null
  periodStatus: 'OPEN' | 'SUBMITTED' | 'APPROVED'
  outgoingVikar: RosterOutgoingVikar | null
  isRoot: boolean
  isOrphan: boolean
  /** the unit the person belongs to (null = Organisation-homed, unit-less). */
  unitId: string | null
  unitName: string | null
  /** the aggregated designated leaders of THIS row's unit (every member of a unit
      carries the same set; an empty array — never null — for a unit-less row). */
  leaderIds: string[]
  /** the active PRIMARY reporting_lines.version etag; null for a root/orphan. */
  primaryReportingLineVersion: number | null
}

/** A DISPLAY-ONLY by-id resolution entry — labels an upward-reference / cross-unit
    leader chip even for an id NOT in the active roster body (e.g. an inactive
    leader). It admits nobody into scope. */
export interface RosterNameResolutionEntry {
  userId: string
  displayName: string
  position: string | null
  unitName: string | null
}

/** The GET …/medarbejdere envelope — `{ employees, pendingCountByManager,
    nameResolution }` (NOT a bare array — the S97/S99 envelope distinction). */
export interface RosterResponse {
  employees: RosterRow[]
  pendingCountByManager: Record<string, number>
  nameResolution: Record<string, RosterNameResolutionEntry>
}

// S111 / TASK-11102 — compile-time drift guards (see apiNarrow.ts). The backend
// names the row schema `RosterEmployeeRow` and the resolution entry `RosterNameRef`
// (the FE keeps its own names); the asserts map each FE-strict interface to its
// spec schema so a renamed/removed backend field fails `tsc` here.
export type _RosterDrift = [
  Assert<AssertFieldsInSpec<RosterResponse, 'StatsTid.Backend.Api.Contracts.RosterResponse'>>,
  Assert<AssertFieldsInSpec<RosterRow, 'StatsTid.Backend.Api.Contracts.RosterEmployeeRow'>>,
  Assert<AssertFieldsInSpec<RosterOutgoingVikar, 'StatsTid.Backend.Api.Contracts.RosterOutgoingVikar'>>,
  Assert<
    AssertFieldsInSpec<RosterNameResolutionEntry, 'StatsTid.Backend.Api.Contracts.RosterNameRef'>
  >,
]

/**
 * The lazy, per-Organisation roster cache. `loadRoster(organisationId)` fetches
 * (once) and caches; `byOrg[organisationId]` exposes the cached response. Calling
 * `loadRoster` for an already-loaded / in-flight Organisation is a no-op, so the
 * panel can call it on every selection without refetching the same Organisation.
 */
export function useRoster() {
  const [byOrg, setByOrg] = useState<Record<string, RosterResponse>>({})
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Refs (not state) so loadRoster stays a STABLE callback — the panel keys an
  // effect on it without re-running on every cache update.
  const loadedRef = useRef<Set<string>>(new Set())
  const loadingRef = useRef<Set<string>>(new Set())

  // The single fetch+cache body; `loadRoster` and `refetchRoster` share it. The
  // in-flight guard prevents a duplicate fetch for the same Organisation.
  const fetchInto = useCallback(async (organisationId: string) => {
    if (loadingRef.current.has(organisationId)) return
    loadingRef.current.add(organisationId)
    setLoading(true)
    setError(null)
    // S111 / TASK-11102 — typed via the OpenAPI TEMPLATED path key with the
    // structured `params.path` shape; `apiClient` interpolates `{organisationId}`
    // (URL-encoded). `coerceApiResponse` re-narrows the spec-loose response to the
    // FE-strict `RosterResponse` (drift caught by `_RosterDrift` above).
    const result = await apiClient.get(
      '/api/admin/reporting-lines/tree/{organisationId}/medarbejdere',
      { params: { path: { organisationId } } },
    )
    loadingRef.current.delete(organisationId)
    if (result.ok) {
      loadedRef.current.add(organisationId)
      setByOrg((prev) => ({ ...prev, [organisationId]: coerceApiResponse<RosterResponse>(result.data) }))
    } else {
      setError(result.error)
    }
    // S107 Step-7a (Codex P2): keep `loading` true while ANY Organisation is still
    // in flight (an in-flight count), so fast A→B cross-Org navigation does NOT flash
    // the empty-state for B when A's request completes first.
    setLoading(loadingRef.current.size > 0)
  }, [])

  const loadRoster = useCallback(async (organisationId: string) => {
    if (loadedRef.current.has(organisationId)) return
    await fetchInto(organisationId)
  }, [fetchInto])

  // SPRINT-108 / TASK-10801 — force a re-pull after a structure mutation (the
  // cached roster is now stale: a re-homed member, a leader change). Drops the
  // loaded flag and re-fetches even when already cached.
  const refetchRoster = useCallback(async (organisationId: string) => {
    loadedRef.current.delete(organisationId)
    await fetchInto(organisationId)
  }, [fetchInto])

  return { byOrg, loading, error, loadRoster, refetchRoster }
}
