// S97 / TASK-9705 — the structured-Enhed FE data layer (replaces the free-text
// `enhed_label`). S100 / TASK-10004 — Enhed is now HIERARCHICAL: an enhed carries
// a `parent_enhed_id` forming a tree WITHIN its Organisation, with a derived
// `level` (depth). It stays PURE display metadata with ZERO authority/scope/
// approval meaning (ADR-036 unchanged): managed by LocalHR+, org-scope-floored
// server-side. This hook owns the enhed CRUD reads/writes (create-under-parent +
// move/re-parent) + the set-user-tags PUT, following the useAdmin/
// useReportingLines convention (reads via apiClient; If-Match writes via
// apiFetchWithEtag) and the S86 `useResolveReportingLine` ETag-resolve discipline
// (rename/delete/move carry the row-version as `If-Match: "<version>"`).
//
// Backend contract (S97 + the S100 hierarchy adaptation, all org-scope-floored):
//   GET    /api/admin/enheder?organisationId=… → ACTIVE enheder for one Org (FLAT)
//   POST   /api/admin/enheder {organisationId, name, parentEnhedId?}
//            (409 dup, 400 if org=MAO, 422 cross-org/dead parent)
//   PUT    /api/admin/enheder/{id} {name}  (If-Match; 409 dup)  — rename
//   PUT    /api/admin/enheder/{id}/move {newParentEnhedId|null}  (If-Match;
//            422 cycle/cross-org/dead parent; 412/428 version/If-Match)
//   DELETE /api/admin/enheder/{id}  (If-Match; soft-delete — re-parents children up)
//   PUT    /api/admin/users/{userId}/enheder {enhedIds: []}  (set the tag set)

import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag, type ApiResult } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

/** One active Enhed row for an Organisation. `version` is the optimistic
    concurrency token; `etag` is its quoted wire form for the next If-Match.
    S100: `parentEnhedId` (null = a root enhed, directly under the Organisation)
    + the derived `level` (depth; a root enhed = 1) ride the FLAT list rows. */
export interface Enhed {
  enhedId: string
  organisationId: string
  name: string
  version: number
  /** The parent enhed id within the same Organisation; null = a root enhed. */
  parentEnhedId: string | null
  /** Derived depth (root enhed = 1). Optional — only the list/tree reads carry it. */
  level?: number
  /** RFC 7232 quoted form (`"<version>"`) for the rename/delete/move If-Match. */
  etag: string
}

/** The wire shape the GET/POST/PUT serve (no client-only `etag`). */
interface EnhedWire {
  enhedId: string
  organisationId: string
  name: string
  version: number
  parentEnhedId?: string | null
  level?: number
}

function toEnhed(data: EnhedWire, etag: string | null): Enhed {
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    enhedId: data.enhedId,
    organisationId: data.organisationId,
    name: data.name,
    version: data.version,
    parentEnhedId: data.parentEnhedId ?? null,
    level: data.level,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
  }
}

/** A status-tagged error so callers can branch on 409 (dup) / 412 (stale) /
    400 (MAO org) without re-reading the response. */
export interface EnhedMutationError extends Error {
  status: number
  body?: unknown
}

function makeEnhedError(status: number, message: string, body?: unknown): EnhedMutationError {
  const err = new Error(message) as EnhedMutationError
  err.status = status
  err.body = body
  return err
}

export function useEnheder() {
  /**
   * List the ACTIVE enheder for one Organisation. Returns the raw `ApiResult`
   * (non-throwing) — the panel surfaces an out-of-scope 403 / non-Org honestly.
   * The list GET serves the rows with a per-row `version` body field (no
   * collection ETag), so each row's If-Match is composed from `version`.
   */
  const fetchEnheder = useCallback(
    async (organisationId: string): Promise<ApiResult<Enhed[]>> => {
      // The backend serves an OBJECT envelope `{ enheder: [...] }`
      // (AdminEndpoints.cs `Results.Ok(new { enheder = … })`), NOT a bare array.
      const result = await apiClient.get<{ enheder: EnhedWire[] }>(
        `/api/admin/enheder?organisationId=${encodeURIComponent(organisationId)}`,
      )
      if (!result.ok) return result
      // No collection ETag — each row carries its own `version`.
      return { ok: true, data: result.data.enheder.map((row) => toEnhed(row, null)) }
    },
    [],
  )

  /** Create an Enhed under an Organisation. With `parentEnhedId` it becomes a
      CHILD of that enhed (same Organisation); without it, a root enhed. 409 =
      active-name dup; 400 = the org is a MAO (an Enhed belongs to an
      ORGANISATION); 422 = cross-org / dead parent. Throws on failure. */
  const createEnhed = useCallback(
    async (
      organisationId: string,
      name: string,
      parentEnhedId?: string | null,
    ): Promise<Enhed> => {
      // Omit `parentEnhedId` entirely when absent (a root enhed) so the wire body
      // stays minimal; include it (even null) only when the caller passes one.
      const body: Record<string, unknown> = { organisationId, name }
      if (parentEnhedId !== undefined && parentEnhedId !== null) {
        body.parentEnhedId = parentEnhedId
      }
      const result = await apiFetchWithEtag<EnhedWire>('/api/admin/enheder', {
        method: 'POST',
        body: JSON.stringify(body),
      })
      if (!result.ok) {
        throw makeEnhedError(result.status, result.error, result.body)
      }
      const { data, etag } = result.data
      return toEnhed(data, etag)
    },
    [],
  )

  /** Rename an Enhed (If-Match the current row version). 409 = active-name dup;
      412 = stale version. Throws on failure. */
  const renameEnhed = useCallback(
    async (enhedId: string, name: string, ifMatch: string): Promise<Enhed> => {
      const result = await apiFetchWithEtag<EnhedWire>(
        `/api/admin/enheder/${encodeURIComponent(enhedId)}`,
        {
          method: 'PUT',
          headers: { 'If-Match': ifMatch },
          body: JSON.stringify({ name }),
        },
      )
      if (!result.ok) {
        throw makeEnhedError(result.status, result.error, result.body)
      }
      const { data, etag } = result.data
      return toEnhed(data, etag)
    },
    [],
  )

  /** Re-parent an Enhed within its Organisation (If-Match the current row
      version). `newParentEnhedId = null` makes it a root enhed. 422 = a cycle
      (self / descendant) / cross-org / dead parent; 412 = stale version; 428 =
      missing If-Match. Throws a status-tagged error on failure. */
  const moveEnhed = useCallback(
    async (
      enhedId: string,
      newParentEnhedId: string | null,
      ifMatch: string,
    ): Promise<Enhed> => {
      const result = await apiFetchWithEtag<EnhedWire>(
        `/api/admin/enheder/${encodeURIComponent(enhedId)}/move`,
        {
          method: 'PUT',
          headers: { 'If-Match': ifMatch },
          body: JSON.stringify({ newParentEnhedId }),
        },
      )
      if (!result.ok) {
        throw makeEnhedError(result.status, result.error, result.body)
      }
      const { data, etag } = result.data
      return toEnhed(data, etag)
    },
    [],
  )

  /** Soft-delete an Enhed (If-Match the current row version). The children
      re-parent UP to the deleted enhed's parent (server-side); memberships are
      projection-filtered (no fan-out untag). Throws on failure. */
  const deleteEnhed = useCallback(
    async (enhedId: string, ifMatch: string): Promise<void> => {
      const result = await apiFetchWithEtag<unknown>(
        `/api/admin/enheder/${encodeURIComponent(enhedId)}`,
        {
          method: 'DELETE',
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        throw makeEnhedError(result.status, result.error, result.body)
      }
    },
    [],
  )

  /**
   * Set a user's FULL tag set (idempotent overwrite). The server validates each
   * `enhedId` ∈ the ACTIVE enheder of the user's `primary_org_id` (a dead/foreign
   * enhed → 400). No If-Match (the set is the authoritative latest-wins overwrite;
   * the server FOR-UPDATE-locks the user row vs a concurrent transfer). Throws on
   * failure so the drawer's single-save-path surfaces it per-section.
   */
  const setUserEnheder = useCallback(
    async (userId: string, enhedIds: string[]): Promise<void> => {
      const result = await apiClient.put<void>(
        `/api/admin/users/${encodeURIComponent(userId)}/enheder`,
        { enhedIds },
      )
      if (!result.ok) {
        throw makeEnhedError(result.status, result.error, result.body)
      }
    },
    [],
  )

  return {
    fetchEnheder,
    createEnhed,
    renameEnhed,
    moveEnhed,
    deleteEnhed,
    setUserEnheder,
  }
}
