import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag, type ApiResult } from '../lib/api'

// S48 TASK-4808. Reporting-line admin hooks following the useAdmin.ts pattern.
// Reads go through apiClient; writes that carry If-Match / return ETag use
// apiFetchWithEtag.
//
// S115 / TASK-11502 — the reporting-lines slice switched to the TYPED
// spec-keyed forms (PAT-012): the backend typed the family in TASK-11501, so
// every call below binds its response type from the OpenAPI path key. The
// local view interfaces are KEPT where the strict spec type assigns directly
// (S113 discipline: a backend field rename/removal is a `tsc` error at the
// assignment site), and CORRECTED where the typed switch surfaced a lie
// (see `DirectReport`).

/** Local VIEW type — the strict spec `ReportingLineResponse` assigns directly
    (field-for-field identical, incl. the nullable `effectiveTo`). */
export interface ReportingLineEntry {
  reportingLineId: string
  employeeId: string
  managerId: string
  organisationId: string
  relationship: string  // 'PRIMARY' | 'ACTING'
  effectiveFrom: string
  effectiveTo: string | null
  source: string
  version: number
  createdBy: string
  createdAt: string
}

/** S115 / TASK-11502 — CORRECTED to the REAL wire shape (the spec
    `DirectReportItem`). The previous hand-written `DirectReport extends
    ReportingLineEntry` claimed `effectiveTo` / `createdBy` / `createdAt` —
    fields the reports endpoint NEVER serves (its row is a handler-level
    SUBSET of the line shape) — and claimed a non-null `employeeDisplayName`
    where the backend declares it nullable. No live consumer read the phantom
    fields (only `relationship` is consumed), so this was a latent contract
    lie, not a live bug — surfaced by the typed switch (the S97→S100 class),
    corrected here, not silently. */
export interface DirectReport {
  reportingLineId: string
  employeeId: string
  employeeDisplayName: string | null
  managerId: string
  organisationId: string
  relationship: string
  effectiveFrom: string
  source: string
  version: number
}

// S76b / TASK-7603 — the ledelseslinje/vikar/delete lifecycle contracts.

/** One person row from the server person-search (`GET /api/admin/users/search`).
    The server scope-filters to the caller's RBAC org-scope + excludes self +
    descendants (the cycle-prevention mirror) when `excludeEmployeeId` is supplied.

    S113 / TASK-11301 — local VIEW type; the strict spec `UserSearchItem` assigns
    directly. NOTE `primaryOrgName` stays DELIBERATELY WIDER (`string | null`)
    than the spec's non-null `string` (benign widening; consumers already
    null-guard) — flagged, not silently narrowed. */
export interface PersonSearchHit {
  userId: string
  displayName: string
  primaryOrgName: string | null
}

/** The users-search envelope view — the strict spec `UserSearchResponse`
    (`{ items, total, limit, offset }`) assigns directly. */
export interface PersonSearchResult {
  items: PersonSearchHit[]
  total: number
  limit: number
  offset: number
}

/** The admin-on-behalf vikar create body (`POST .../{managerId}/vikar`). No
    If-Match; the manager id is the path segment. `effectiveTo` is the INCLUSIVE
    "til og med" date; `reason` ∈ FERIE/SYGDOM/ORLOV/TJENESTEREJSE/ANDET.
    S115 — deliberately STRICTER than the spec `AdminVikarRequest` (whose
    members are all optional): the endpoint 400s without them, so the local
    body type keeps them required; it assigns to the spec body directly. */
export interface CreateVikarBody {
  vikarUserId: string
  effectiveTo: string
  reason?: string
}

/** Local VIEW type — the strict spec `AdminVikarCreatedResponse` assigns
    directly (S115, field-for-field identical). */
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
    `outgoingVikar` so it maps 1:1 onto `VikarSection`'s `ActiveVikar`.
    S115 — the strict spec `ActiveVikarInfo` assigns directly; the envelope's
    optional-vs-null mismatch is normalized in `fetchActiveVikar` (see there). */
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

/** S115 — the 409 gap payload is an UNDECLARED error body (error shapes are
    not spec-typed), so it arrives as `unknown`. Runtime type guard (not an
    `as` cast — this file is on the S115 no-`as` tier): all target members are
    optional, so the claim is structurally sound (the same trust level as the
    previous inline cast; the `Array.isArray` check at the call site stays). */
function isGapErrorBody(
  body: unknown,
): body is { error?: string; reportsNeedingReassignment?: string[] } {
  return typeof body === 'object' && body !== null
}

export function useReportingLines() {
  const fetchEmployeeLines = useCallback(
    async (
      employeeId: string,
    ): Promise<ApiResult<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>> => {
      // S115 — typed spec-keyed GET (the strict `EmployeeReportingLinesResponse`
      // envelope assigns directly to the view; `buildUrl` encodeURIComponents
      // the path param exactly as the previous hand-built URL did).
      return apiClient.get('/api/admin/reporting-lines/{employeeId}', {
        params: { path: { employeeId } },
      })
    },
    [],
  )

  const fetchDirectReports = useCallback(
    async (managerId: string): Promise<ApiResult<DirectReport[]>> => {
      // S115 — typed spec-keyed GET (the bare `DirectReportItem[]` array — see
      // the corrected `DirectReport` view above).
      return apiClient.get('/api/admin/reporting-lines/{managerId}/reports', {
        params: { path: { managerId } },
      })
    },
    [],
  )

  const assignManager = useCallback(
    async (
      body: { employeeId: string; managerId: string; effectiveFrom: string },
      ifMatch?: string,
    ): Promise<ApiResult<ReportingLineEntry>> => {
      // S115 — typed etag POST. THE PRECONDITION ROW (vitest-pinned in
      // __tests__/useReportingLines.test.ts): a FIRST assign sends
      // `If-None-Match: *` (create-only), a reassign sends the line's
      // `If-Match` — byte-identical to the previous hand-built headers. The
      // response is the shared `ReportingLineResponse` for BOTH the 201
      // (first-assign) and 200 (reassign) branches (the S115 homogeneous
      // multi-2xx declaration; `SuccessDataOf` resolves the same T).
      const result = ifMatch
        ? await apiFetchWithEtag('/api/admin/reporting-lines', { method: 'POST', ifMatch, body })
        : await apiFetchWithEtag('/api/admin/reporting-lines', {
            method: 'POST',
            ifNoneMatch: '*',
            body,
          })
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
      // S115 — typed etag DELETE (declared 204 → `undefined` data), strict If-Match.
      const result = await apiFetchWithEtag('/api/admin/reporting-lines/{employeeId}', {
        method: 'DELETE',
        params: { path: { employeeId } },
        ifMatch,
      })
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: undefined }
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
      // S112 / TASK-11203 — the typed spec-keyed GET (the `{items,total,limit,
      // offset}` envelope is the strict spec `UserSearchResponse`, directly
      // assignable to the view — S113); `buildUrl` skips undefined query params,
      // matching the previous hand-built query string byte-for-byte.
      const result = await apiClient.get('/api/admin/users/search', {
        query: {
          q: params.q || undefined,
          excludeEmployeeId: params.excludeEmployeeId || undefined,
          limit: params.limit ?? undefined,
          offset: params.offset ?? undefined,
        },
      })
      if (!result.ok) return result
      return { ok: true, data: result.data }
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
      // S115 — typed etag POST, NO precondition (the endpoint takes no If-Match).
      // The strict `AdminVikarCreatedResponse` assigns directly to the view.
      const result = await apiFetchWithEtag('/api/admin/reporting-lines/{managerId}/vikar', {
        method: 'POST',
        params: { path: { managerId } },
        body,
      })
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
      // S115 — typed spec-keyed GET. `ActiveVikarResponse.activeVikar` is a
      // nullable-COMPLEX member: the wire ALWAYS emits the key (null-or-object),
      // but the S113 nullable-$ref exclusion makes the generated member OPTIONAL
      // (`activeVikar?: ActiveVikarInfo`, no `| null`). Consumed optional-as-
      // nullable and normalized `?? null` HERE so the hook's public
      // `ActiveVikarDto | null` contract (and every consumer's none-sentinel)
      // is untouched — the deliberate alternative to a RosterRow-style
      // Omit-override view for a single-member envelope.
      const result = await apiClient.get('/api/admin/reporting-lines/{managerId}/vikar', {
        params: { path: { managerId } },
      })
      if (!result.ok) return result
      return { ok: true, data: { activeVikar: result.data.activeVikar ?? null } }
    },
    [],
  )

  // S76b / TASK-7603 — admin revokes the manager's active vikar (revoke-safe;
  // 404 if none). No body, no If-Match.
  const endVikar = useCallback(
    async (managerId: string): Promise<ApiResult<void>> => {
      // S115 — typed etag DELETE, NO precondition. This is a genuine
      // 200-with-body route (`AdminVikarRevokedResponse`); the hook keeps its
      // `ApiResult<void>` contract and discards the body (no consumer reads it).
      const result = await apiFetchWithEtag('/api/admin/reporting-lines/{managerId}/vikar', {
        method: 'DELETE',
        params: { path: { managerId } },
      })
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
      // S115 — typed etag POST, NO precondition (the typed 200 body —
      // `RemoveWithReassignmentResponse` — is deliberately unused; the 409 gap
      // protocol below is an ERROR body and stays runtime-narrowed).
      const result = await apiFetchWithEtag('/api/admin/reporting-lines/{employeeId}/remove', {
        method: 'POST',
        params: { path: { employeeId } },
        body: { replacements },
      })
      if (result.ok) {
        return { ok: true }
      }
      // 409 → parse the gap list (preflight or in-lock census). The server shape
      // is `{ error, reportsNeedingReassignment: string[], reportsNeedingReassignmentCount }`.
      if (result.status === 409) {
        const body = isGapErrorBody(result.body) ? result.body : undefined
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
    searchPeople,
    createVikar,
    fetchActiveVikar,
    endVikar,
    deletePersonWithReassignment,
  }
}
