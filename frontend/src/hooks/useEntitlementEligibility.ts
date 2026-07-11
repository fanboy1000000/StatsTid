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
//
//   (C) employment-start date (S60 / TASK-6007 / ADR-030) — pro-rates monthly
//       vacation accrual for mid-year hires. HR-only read/write via GET/PUT
//       /api/admin/employees/{id}/employment-start-date with admin-strict
//       If-Match, exactly mirroring the (A) DOB surface.
//
// S115 / TASK-11502 — every call EXCEPT ONE switched to the typed spec-keyed
// `apiFetchWithEtag` form (PAT-012; the field-endpoint responses were typed in
// TASK-11501, and the strict spec shapes match the previous inline types
// field-for-field). THE ONE EXCEPTION — `fetchChildSickEligibility` — is the
// program's FIRST flag-and-defer op: `GET …/entitlement-eligibility/{type}`
// is genuinely POLYMORPHIC (its no-row branch OMITS the `effectiveFrom` /
// `version` KEYS rather than emitting null), so typing it would require a
// wire change (PAT-010-forbidden). It stays on the explicit-T legacy GET
// until a strict-types phase resolves it; the eslint tier for this file
// sanctions exactly that one-argument form.

// S115 — the (redundant) `as const` dropped: a primitive `const` already infers
// the literal type, and this file is on the no-`as` lint tier.
const SETTABLE_ENTITLEMENT_TYPE = 'CHILD_SICK'

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

/**
 * Snapshot of an employee's employment-start date + the `users.version`
 * concurrency token (S60 / TASK-6007 / ADR-030 — mirrors the S59 DOB field).
 */
export interface EmploymentStartDateSnapshot {
  employeeId: string
  employmentStartDate: string | null
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
  // Object.assign (not an `as` cast) — this file is on the S115 no-`as` tier.
  return Object.assign(new Error(msg), { status, body })
}

/** S115 — the error payloads (409/412/422 bodies) are UNDECLARED in the spec,
    so they arrive as `unknown`. Runtime type guard (not an `as` cast — this
    file is on the S115 no-`as` tier): passes the SAME runtime object through
    when it is an object; the target's members are all optional, so the claim
    is structurally sound (the useAdmin `isUserMutationErrorBody` precedent). */
function isEligibilityErrorBody(
  body: unknown,
): body is NonNullable<EligibilityMutationError['body']> {
  return typeof body === 'object' && body !== null
}

/** Narrow an unknown error payload to the optional `body` shape (undefined
    when the payload is absent or not an object). */
function toErrorBody(body: unknown): EligibilityMutationError['body'] {
  return isEligibilityErrorBody(body) ? body : undefined
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
    // S115 — DEFERRED (the polymorphic no-row branch omits keys; see the file
    // header). This explicit-T one-argument GET is the file's ONE sanctioned
    // legacy form.
    const result = await apiFetchWithEtag<{
      employeeId: string
      entitlementType: string
      eligible: boolean
      effectiveFrom?: string
      rowExists: boolean
      version?: number
    }>(ELIGIBILITY_PATH(employeeId))
    if (!result.ok) {
      throw makeError(result.status, result.error, toErrorBody(result.body))
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
    // S115 — typed etag PUT (the strict `EntitlementEligibilityUpdatedResponse`
    // matches the previous inline type field-for-field). The create-vs-update
    // precondition branch is UNCHANGED: live row → `If-Match: "<version>"`,
    // no row → the create-only `If-None-Match: *` (the S115 `ifNoneMatch`
    // option; byte-identical headers to the previous hand-built form).
    const params = { path: { employeeId, entitlementType: SETTABLE_ENTITLEMENT_TYPE } }
    const result =
      rowExists && currentVersion !== null
        ? await apiFetchWithEtag(
            '/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}',
            {
              method: 'PUT',
              params,
              ifMatch: formatVersionAsIfMatch(currentVersion),
              body: { eligible },
            },
          )
        : await apiFetchWithEtag(
            '/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}',
            {
              method: 'PUT',
              params,
              ifNoneMatch: '*',
              body: { eligible },
            },
          )
    if (!result.ok) {
      const body = toErrorBody(result.body)
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
    // S115 — typed etag GET (the strict `BirthDateResponse` matches the
    // previous inline type field-for-field).
    const result = await apiFetchWithEtag('/api/admin/employees/{employeeId}/birth-date', {
      method: 'GET',
      params: { path: { employeeId } },
    })
    if (!result.ok) {
      throw makeError(result.status, result.error, toErrorBody(result.body))
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
    // S115 — typed etag PUT, admin-strict If-Match unchanged.
    const result = await apiFetchWithEtag('/api/admin/employees/{employeeId}/birth-date', {
      method: 'PUT',
      params: { path: { employeeId } },
      ifMatch: formatVersionAsIfMatch(currentVersion),
      body: { birthDate },
    })
    if (!result.ok) {
      throw makeError(result.status, result.error, toErrorBody(result.body))
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      birthDate: data.birthDate,
      version: version ?? data.version,
    }
  }

  /**
   * HR-only employment-start read (S60 / TASK-6007). ETag stamps `users.version`
   * for the next If-Match PUT. Drives mid-year-hire vacation pro-rating.
   */
  const fetchEmploymentStartDate = async (
    employeeId: string,
  ): Promise<EmploymentStartDateSnapshot> => {
    // S115 — typed etag GET (the strict `EmploymentStartDateResponse` matches
    // the previous inline type field-for-field).
    const result = await apiFetchWithEtag(
      '/api/admin/employees/{employeeId}/employment-start-date',
      { method: 'GET', params: { path: { employeeId } } },
    )
    if (!result.ok) {
      throw makeError(result.status, result.error, toErrorBody(result.body))
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      employmentStartDate: data.employmentStartDate,
      version: version ?? data.version,
    }
  }

  /**
   * HR-only employment-start write, admin-strict If-Match (S60 / TASK-6007).
   * `employmentStartDate` null clears an unknown start date.
   */
  const setEmploymentStartDate = async (
    employeeId: string,
    employmentStartDate: string | null,
    currentVersion: number,
  ): Promise<EmploymentStartDateSnapshot> => {
    // S115 — typed etag PUT, admin-strict If-Match unchanged.
    const result = await apiFetchWithEtag(
      '/api/admin/employees/{employeeId}/employment-start-date',
      {
        method: 'PUT',
        params: { path: { employeeId } },
        ifMatch: formatVersionAsIfMatch(currentVersion),
        body: { employmentStartDate },
      },
    )
    if (!result.ok) {
      throw makeError(result.status, result.error, toErrorBody(result.body))
    }
    const { data, etag } = result.data
    const { version } = resolveEtag(etag, data)
    return {
      employeeId,
      employmentStartDate: data.employmentStartDate,
      version: version ?? data.version,
    }
  }

  return {
    fetchChildSickEligibility,
    setChildSick,
    fetchBirthDate,
    setBirthDate,
    fetchEmploymentStartDate,
    setEmploymentStartDate,
  }
}
