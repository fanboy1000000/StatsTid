import { apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S59 / TASK-5908 / ADR-029 — HR-only frontend hook for the two per-employee
// surfaces that drive entitlement eligibility:
//
//   (B) CHILD_SICK eligibility — a per-employee opt-in flag (default ineligible).
//       Read via GET /api/admin/employees/{id}/entitlement-eligibility/CHILD_SICK
//       and set via the sibling PUT. The eligibility row's existence is the
//       create-vs-update signal:
//         • No live row (GET → rowExists:false, no ETag): the toggle CREATES the
//           row with `If-None-Match: *`.
//         • Live row (GET → rowExists:true + ETag "<version>"): the toggle UPDATES
//           with `If-Match: "<version>"` (the version from the GET / prior PUT).
//       CHILD_SICK is the ONLY settable type — SENIOR_DAY is age-derived (DOB),
//       never a manual toggle (refinement line 117).
//
//       S59 follow-up (Step-7a BLOCKER 1): the backend write is now strictly
//       precondition-guarded. A blind `If-None-Match: *` against an existing row
//       is a 409 Conflict (create-only path; no blind overwrite of an HR-set
//       value). The frontend therefore READS first (fetchChildSickEligibility)
//       to learn rowExists + version, then composes the correct precondition.
//       A 409 (lost update — someone created the row between our read and write)
//       is surfaced as a clear message so HR can re-read and retry. The PUT/GET
//       response carries the authoritative `eligible` + `version` for
//       read-your-write within the session.
//
//   (A) date of birth — drives the age-derived SENIOR_DAY gate automatically.
//       HR-only read/write via GET/PUT /api/admin/employees/{id}/birth-date with
//       admin-strict If-Match. DOB never appears in any Employee-facing DTO/JWT/
//       export (TASK-5909) — this is the only read surface.

const SETTABLE_ENTITLEMENT_TYPE = 'CHILD_SICK' as const

/** Snapshot of an employee's CHILD_SICK eligibility + the concurrency token. */
export interface ChildSickEligibilitySnapshot {
  employeeId: string
  eligible: boolean
  // True when a live eligibility row exists server-side. Drives the
  // create (`If-None-Match: *`) vs update (`If-Match`) branch on the next write.
  rowExists: boolean
  // The row's version when `rowExists` is true; null when no live row exists
  // (or no write has yet returned a version this session).
  version: number | null
}

/** Snapshot of an employee's DOB + the `users.version` concurrency token. */
export interface BirthDateSnapshot {
  employeeId: string
  birthDate: string | null
  version: number
}

/** Structured mutation error so callers can branch on 412 / 422 / 428 / etc. */
export interface EligibilityMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
    // 409 lost-update (If-None-Match: * against an existing row) hands back the
    // current version so the caller can re-read and retry with If-Match.
    currentVersion?: number
    hint?: string
  }
}

function makeError(
  status: number,
  msg: string,
  body: EligibilityMutationError['body'],
): EligibilityMutationError {
  const err = new Error(msg) as EligibilityMutationError
  err.status = status
  err.body = body
  return err
}

const ELIGIBILITY_PATH = (employeeId: string) =>
  `/api/admin/employees/${encodeURIComponent(employeeId)}/entitlement-eligibility/${SETTABLE_ENTITLEMENT_TYPE}`

export function useEntitlementEligibility() {
  /**
   * HR-only read of the CURRENT LIVE CHILD_SICK eligibility (S59 follow-up,
   * Step-7a BLOCKER 1). Resolves the create-vs-update signal up front:
   *   • Live row     → 200 { eligible, rowExists:true, version } + ETag "<version>".
   *   • No live row  → 200 { eligible:false, rowExists:false } with NO ETag.
   * The returned `{ eligible, rowExists, version }` pre-populates the toggle AND
   * stamps the precondition for the subsequent write (If-Match when rowExists,
   * else If-None-Match: *).
   */
  const fetchChildSickEligibility = async (
    employeeId: string,
  ): Promise<ChildSickEligibilitySnapshot> => {
    const result = await apiFetchWithEtag<{
      employeeId: string
      entitlementType: string
      eligible: boolean
      effectiveFrom?: string
      rowExists: boolean
      version?: number
    }>(ELIGIBILITY_PATH(employeeId))
    if (!result.ok) {
      throw makeError(result.status, result.error, result.body as EligibilityMutationError['body'])
    }
    const { data, etag } = result.data
    // version is present (and ETag-stamped) ONLY when rowExists; resolveEtag
    // prefers the header but falls back to the body field (CORS-exposed-header
    // gap, S23 / TASK-2303).
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      eligible: data.eligible,
      rowExists: data.rowExists,
      version: data.rowExists ? (version ?? data.version ?? null) : null,
    }
  }

  /**
   * Set CHILD_SICK eligibility (read-then-If-Match, S59 follow-up). When no live
   * row exists (`rowExists === false`) the write CREATES the row with
   * `If-None-Match: *`; otherwise it UPDATES with `If-Match: "<version>"` (the
   * version learned from `fetchChildSickEligibility` or a prior write). The
   * backend rejects a blind `If-None-Match: *` against an existing row with 409
   * (lost update); callers should catch that, re-read, and retry. Returns the
   * new snapshot (authoritative `eligible` + `version`) for read-your-write.
   */
  const setChildSick = async (
    employeeId: string,
    eligible: boolean,
    rowExists: boolean,
    currentVersion: number | null,
  ): Promise<ChildSickEligibilitySnapshot> => {
    const precondition: Record<string, string> =
      rowExists && currentVersion !== null
        ? { 'If-Match': formatVersionAsIfMatch(currentVersion) }
        : { 'If-None-Match': '*' }

    const result = await apiFetchWithEtag<{
      employeeId: string
      entitlementType: string
      eligible: boolean
      effectiveFrom: string
      version: number
    }>(ELIGIBILITY_PATH(employeeId), {
      method: 'PUT',
      headers: precondition,
      body: JSON.stringify({ eligible }),
    })
    if (!result.ok) {
      const body = result.body as EligibilityMutationError['body']
      if (result.status === 409) {
        // Lost update: a live row appeared between our read and this create.
        // Surface a clear, actionable Danish-friendly message; the page layer
        // re-reads (fetchChildSickEligibility) so HR can retry with If-Match.
        throw makeError(
          409,
          'Berettigelsen blev ændret af en anden administrator. Genindlæs og prøv igen.',
          body,
        )
      }
      throw makeError(result.status, result.error, body)
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      eligible: data.eligible,
      // After any successful write a live row exists.
      rowExists: true,
      version: version ?? data.version,
    }
  }

  /** HR-only DOB read. ETag stamps `users.version` for the next If-Match PUT. */
  const fetchBirthDate = async (
    employeeId: string,
  ): Promise<BirthDateSnapshot> => {
    const result = await apiFetchWithEtag<{
      employeeId: string
      birthDate: string | null
      version: number
    }>(`/api/admin/employees/${encodeURIComponent(employeeId)}/birth-date`)
    if (!result.ok) {
      throw makeError(result.status, result.error, result.body as EligibilityMutationError['body'])
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      birthDate: data.birthDate,
      version: version ?? data.version,
    }
  }

  /** HR-only DOB write, admin-strict If-Match. `birthDate` null clears the DOB. */
  const setBirthDate = async (
    employeeId: string,
    birthDate: string | null,
    currentVersion: number,
  ): Promise<BirthDateSnapshot> => {
    const result = await apiFetchWithEtag<{
      employeeId: string
      birthDate: string | null
      version: number
    }>(`/api/admin/employees/${encodeURIComponent(employeeId)}/birth-date`, {
      method: 'PUT',
      headers: { 'If-Match': formatVersionAsIfMatch(currentVersion) },
      body: JSON.stringify({ birthDate }),
    })
    if (!result.ok) {
      throw makeError(result.status, result.error, result.body as EligibilityMutationError['body'])
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      birthDate: data.birthDate,
      version: version ?? data.version,
    }
  }

  return { fetchChildSickEligibility, setChildSick, fetchBirthDate, setBirthDate }
}
