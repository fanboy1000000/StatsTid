import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag, type ApiResult } from '../lib/api'

// S48 TASK-4808. Reporting-line admin hooks following the useAdmin.ts pattern.
// Reads go through apiClient; writes that carry If-Match / return ETag use
// apiFetchWithEtag. Types declared locally (same convention as useAdmin.ts).

export interface ReportingLineEntry {
  reportingLineId: string
  employeeId: string
  managerId: string
  treeRootOrgId: string
  relationship: string  // 'PRIMARY' | 'ACTING'
  effectiveFrom: string
  effectiveTo: string | null
  source: string
  version: number
  createdBy: string
  createdAt: string
}

export interface DirectReport extends ReportingLineEntry {
  employeeDisplayName: string
}

// S76b / TASK-7603 — the ledelseslinje/vikar/delete lifecycle contracts.

/** One person row from the server person-search (`GET /api/admin/users/search`).
    The server scope-filters to the caller's RBAC org-scope + excludes self +
    descendants (the cycle-prevention mirror) when `excludeEmployeeId` is supplied. */
export interface PersonSearchHit {
  userId: string
  displayName: string
  primaryOrgName: string | null
  enhedLabel: string | null
}

export interface PersonSearchResult {
  items: PersonSearchHit[]
  total: number
  limit: number
  offset: number
}

/** The admin-on-behalf vikar create body (`POST .../{managerId}/vikar`). No
    If-Match; the manager id is the path segment. `effectiveTo` is the INCLUSIVE
    "til og med" date; `reason` ∈ FERIE/SYGDOM/ORLOV/TJENESTEREJSE/ANDET. */
export interface CreateVikarBody {
  vikarUserId: string
  effectiveTo: string
  reason?: string
}

export interface VikarCreatedResult {
  vikarId: string
  managerId: string
  vikarUserId: string
  effectiveFrom: string
  effectiveTo: string
  reason: string
}

/** S76b / TASK-7603 (BLOCKER 3) — the single-manager active-vikar read shape
    (`GET .../{managerId}/vikar` → `{ activeVikar }`). Mirrors the roster's
    `outgoingVikar` so it maps 1:1 onto `VikarSection`'s `ActiveVikar`. */
export interface ActiveVikarDto {
  vikarUserId: string
  vikarDisplayName: string
  untilDate: string
  reason: string
}

/**
 * The typed gap list returned by the delete-with-reassignment 409 (BOTH the
 * out-of-tx preflight AND the authoritative in-lock-census second 409). The
 * server returns `{ error, reportsNeedingReassignment: string[], ... }` — the
 * caller collects a replacement approver per report and re-submits, repeating
 * until success (NOT a single round: a report assigned between preflight and
 * commit surfaces in the in-lock census's second 409).
 */
export interface ReassignmentGap {
  reportsNeedingReassignment: string[]
  message: string
}

/** The non-throwing result of `deletePersonWithReassignment` — `gap` is set on a
    409 (preflight OR in-lock census) so the dialog can re-prompt; `error` carries
    the honest message for any other non-OK status (400 cross-tree/transferred,
    422 bad replacement, 403 scope). */
export type DeletePersonResult =
  | { ok: true }
  | { ok: false; status: number; error: string; gap?: ReassignmentGap }

export function useReportingLines() {
  const fetchEmployeeLines = useCallback(
    async (
      employeeId: string,
    ): Promise<ApiResult<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>> => {
      return apiClient.get<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}`,
      )
    },
    [],
  )

  const fetchDirectReports = useCallback(
    async (managerId: string): Promise<ApiResult<DirectReport[]>> => {
      return apiClient.get<DirectReport[]>(
        `/api/admin/reporting-lines/${encodeURIComponent(managerId)}/reports`,
      )
    },
    [],
  )

  const assignManager = useCallback(
    async (
      body: { employeeId: string; managerId: string; effectiveFrom: string },
      ifMatch?: string,
    ): Promise<ApiResult<ReportingLineEntry>> => {
      const headers: Record<string, string> = {}
      if (ifMatch) {
        headers['If-Match'] = ifMatch
      } else {
        headers['If-None-Match'] = '*'
      }
      const result = await apiFetchWithEtag<ReportingLineEntry>(
        '/api/admin/reporting-lines',
        {
          method: 'POST',
          body: JSON.stringify(body),
          headers,
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  const removeManager = useCallback(
    async (
      employeeId: string,
      ifMatch: string,
    ): Promise<ApiResult<void>> => {
      const result = await apiFetchWithEtag<void>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}`,
        {
          method: 'DELETE',
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: undefined }
    },
    [],
  )

  const fetchTreeSettings = useCallback(
    async (treeRootOrgId: string): Promise<ApiResult<{ enforcementMode: string; version: number }>> => {
      return apiClient.get<{ enforcementMode: string; version: number }>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}/settings`,
      )
    },
    [],
  )

  const updateTreeSettings = useCallback(
    async (
      treeRootOrgId: string,
      body: { enforcementMode: string },
      ifMatch: string,
    ): Promise<ApiResult<{ enforcementMode: string; version: number }>> => {
      const result = await apiFetchWithEtag<{ enforcementMode: string; version: number }>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}/settings`,
        {
          method: 'PUT',
          body: JSON.stringify(body),
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  // S76b / TASK-7603 — server person-search for the approver/vikar pickers.
  // Scope-filtered + self/descendant-excluded server-side (scales to 2000+).
  const searchPeople = useCallback(
    async (params: {
      q?: string
      excludeEmployeeId?: string
      limit?: number
      offset?: number
    }): Promise<ApiResult<PersonSearchResult>> => {
      const qs = new URLSearchParams()
      if (params.q) qs.set('q', params.q)
      if (params.excludeEmployeeId) qs.set('excludeEmployeeId', params.excludeEmployeeId)
      if (params.limit != null) qs.set('limit', String(params.limit))
      if (params.offset != null) qs.set('offset', String(params.offset))
      const query = qs.toString()
      return apiClient.get<PersonSearchResult>(
        `/api/admin/users/search${query ? `?${query}` : ''}`,
      )
    },
    [],
  )

  // S76b / TASK-7603 — admin-on-behalf vikar create (the S76/7601 endpoint).
  // NO If-Match. 409 = the manager already has an active vikar (one-active);
  // 400 = cross-tree / coverage / cycle / bad reason / bad date. The caller maps
  // status → honest Danish message (the typed `status`/`error` are exposed).
  const createVikar = useCallback(
    async (
      managerId: string,
      body: CreateVikarBody,
    ): Promise<ApiResult<VikarCreatedResult>> => {
      const result = await apiFetchWithEtag<VikarCreatedResult>(
        `/api/admin/reporting-lines/${encodeURIComponent(managerId)}/vikar`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  // S76b / TASK-7603 (BLOCKER 3) — the SINGLE-manager active-vikar read. The
  // unified EditPersonDrawer is opened from the UserManagement LIST (no tree
  // context), so `LifecycleSections` cannot get the active vikar from a tree
  // roster row. This serves the manager's OWN active manager_vikar row (+ the
  // stand-in's display name) | null, so an away-manager's vikar surfaces and can
  // be revoked. Read-only; LocalAdmin floor. `activeVikar` is null when none.
  const fetchActiveVikar = useCallback(
    async (managerId: string): Promise<ApiResult<{ activeVikar: ActiveVikarDto | null }>> => {
      return apiClient.get<{ activeVikar: ActiveVikarDto | null }>(
        `/api/admin/reporting-lines/${encodeURIComponent(managerId)}/vikar`,
      )
    },
    [],
  )

  // S76b / TASK-7603 — admin revokes the manager's active vikar (revoke-safe;
  // 404 if none). No body, no If-Match.
  const endVikar = useCallback(
    async (managerId: string): Promise<ApiResult<void>> => {
      const result = await apiFetchWithEtag<unknown>(
        `/api/admin/reporting-lines/${encodeURIComponent(managerId)}/vikar`,
        { method: 'DELETE' },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: undefined }
    },
    [],
  )

  // S76b / TASK-7603 — delete-with-reassignment. `POST .../{employeeId}/remove`
  // with the `{ reportEmployeeId → replacementApproverId }` map. The 409 (BOTH the
  // preflight AND the in-lock-census second 409) carries the typed gap list so the
  // dialog can re-prompt; the caller re-submits with the merged map and repeats
  // until success (the in-lock census can surface a NEW report after the
  // preflight). NO If-Match.
  const deletePersonWithReassignment = useCallback(
    async (
      employeeId: string,
      replacements: Record<string, string>,
    ): Promise<DeletePersonResult> => {
      const result = await apiFetchWithEtag<unknown>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}/remove`,
        {
          method: 'POST',
          body: JSON.stringify({ replacements }),
        },
      )
      if (result.ok) {
        return { ok: true }
      }
      // 409 → parse the gap list (preflight or in-lock census). The server shape
      // is `{ error, reportsNeedingReassignment: string[], reportsNeedingReassignmentCount }`.
      if (result.status === 409) {
        const body = result.body as
          | { error?: string; reportsNeedingReassignment?: string[] }
          | undefined
        if (body && Array.isArray(body.reportsNeedingReassignment)) {
          return {
            ok: false,
            status: 409,
            error: body.error ?? 'Manglende erstatningsgodkender.',
            gap: {
              reportsNeedingReassignment: body.reportsNeedingReassignment,
              message: body.error ?? 'Manglende erstatningsgodkender.',
            },
          }
        }
      }
      return { ok: false, status: result.status, error: result.error }
    },
    [],
  )

  return {
    fetchEmployeeLines,
    fetchDirectReports,
    assignManager,
    removeManager,
    fetchTreeSettings,
    updateTreeSettings,
    searchPeople,
    createVikar,
    fetchActiveVikar,
    endVikar,
    deletePersonWithReassignment,
  }
}
