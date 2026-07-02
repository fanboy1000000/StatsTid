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
// S113 / TASK-11301 — the response types are the GENERATED spec types (the
// backend names the row schema `RosterEmployeeRow` and the resolution entry
// `RosterNameRef`; the FE keeps its own names as aliases): the S111 coercion
// + drift-guard scaffolding (the deleted apiNarrow module) is gone; a renamed or
// removed backend field is now a direct `tsc` error at the `setByOrg` call (the
// S97→S99→S100 "fetchEnheder" drift class, closed structurally).
//
// THE ONE spec-vs-wire exception (S113, documented): the generator emits a
// nullable $ref property (`outgoingVikar`) as OPTIONAL (`?: T`) because OpenAPI
// 3.0 cannot mark a bare `$ref` nullable — but the backend SERIALIZES an explicit
// `"outgoingVikar": null`. `RosterRow` therefore overrides that single property
// to `?: RosterOutgoingVikar | null` so the runtime `null` stays typed; every
// other field is derived VERBATIM from the generated schema.
//
// LINT (PAT-010): the URL is passed INLINE as a literal to apiClient.get so the
// contract-coverage lint (tools/check_endpoint_contracts.py) can enumerate it —
// a path-helper const would evade the gate (the documented blind spot).

import { useState, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

type Schemas = components['schemas']

/** Per-away-manager vikar annotation — present IFF this person is an away-manager
    currently covered by an active vikar (the absent leader's own row). The
    inverse "Vikar for X" tag on the stand-in is derived by INVERTING this within
    the loaded set (no separate field). */
export type RosterOutgoingVikar = Schemas['StatsTid.Backend.Api.Contracts.RosterOutgoingVikar']

/** One employee row in the unit-tagged structural roster (the spec
    `RosterEmployeeRow` with the single nullable-$ref override — see the module
    header). `structuralApproverId` is the person's assigned active PRIMARY
    manager — THE TREE KEY (raw edge); `unitId` null = Organisation-homed;
    `primaryReportingLineVersion` is the active PRIMARY reporting_lines.version
    etag (null for a root/orphan). */
export type RosterRow = Omit<
  Schemas['StatsTid.Backend.Api.Contracts.RosterEmployeeRow'],
  'outgoingVikar'
> & {
  /** the wire serializes an explicit `null` (nullable-$ref spec exception). */
  outgoingVikar?: RosterOutgoingVikar | null
}

/** A DISPLAY-ONLY by-id resolution entry — labels an upward-reference / cross-unit
    leader chip even for an id NOT in the active roster body (e.g. an inactive
    leader). It admits nobody into scope. */
export type RosterNameResolutionEntry = Schemas['StatsTid.Backend.Api.Contracts.RosterNameRef']

/** The GET …/medarbejdere envelope — `{ employees, pendingCountByManager,
    nameResolution }` (NOT a bare array — the S97/S99 envelope distinction).
    Derived from the spec `RosterResponse` with the `RosterRow` override threaded
    through `employees`. */
export type RosterResponse = Omit<
  Schemas['StatsTid.Backend.Api.Contracts.RosterResponse'],
  'employees'
> & {
  employees: RosterRow[]
}

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
    // (URL-encoded). `result.data` IS the strict spec `RosterResponse`, directly
    // assignable to the FE view (only the nullable-$ref override differs — S113).
    const result = await apiClient.get(
      '/api/admin/reporting-lines/tree/{organisationId}/medarbejdere',
      { params: { path: { organisationId } } },
    )
    loadingRef.current.delete(organisationId)
    if (result.ok) {
      loadedRef.current.add(organisationId)
      setByOrg((prev) => ({ ...prev, [organisationId]: result.data }))
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
