// Local agreement profile API (S21 / ADR-017 D5; S22 / ADR-018 D7).
//
// These calls touch the profile-shaped endpoints introduced in S21 Phase 3:
//
//   GET    /api/config/{orgId}/profile/{agreementCode}/{okVersion}
//   GET    /api/config/{orgId}/profile/{agreementCode}/{okVersion}/history
//   PUT    /api/config/{orgId}/profile/{agreementCode}/{okVersion}
//
// The PUT requires either `If-Match: "<version>"` (for supersession) or
// `If-None-Match: *` (for first creation), and GET returns an `ETag` header.
//
// S22 / ADR-018 D7 wire-format change: the ETag/If-Match value used to be the
// profile UUID quoted (`"11111111-..."`). It is now a quoted decimal version
// (`"5"`). Parsing/formatting lives in `lib/etag.ts`; this module imports the
// helpers so the contract is explicit at the network boundary.
//
// `apiClient` from `lib/api.ts` does not currently expose response headers, so
// this module talks to `fetch` directly. (Cross-domain note: extending
// `apiClient` was the alternative — kept narrow here because only the profile
// endpoints need header access.)
import type { LocalAgreementProfile, ProfileSaveError } from '../hooks/useConfig'
import { parseVersionFromETag, formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

const TOKEN_KEY = 'statstid_token'

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function authHeaders(extra?: Record<string, string>): Record<string, string> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  const token = getToken()
  if (token) headers['Authorization'] = `Bearer ${token}`
  if (extra) {
    for (const [k, v] of Object.entries(extra)) headers[k] = v
  }
  return headers
}

export interface ProfileGetSuccess {
  ok: true
  profile: LocalAgreementProfile | null
  /** Raw RFC 7232 quoted ETag (S22: `"<version>"` numeric body). */
  etag: string | null
  /** Parsed integer `version` from the ETag header — convenience view. */
  version: number | null
}

export interface ProfileGetFailure {
  ok: false
  error: string
  status: number
}

export type ProfileGetResult = ProfileGetSuccess | ProfileGetFailure

export async function getCurrentProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
): Promise<ProfileGetResult> {
  const url = `/api/config/${encodeURIComponent(orgId)}/profile/${encodeURIComponent(agreementCode)}/${encodeURIComponent(okVersion)}`
  try {
    const res = await fetch(url, { method: 'GET', headers: authHeaders() })
    if (res.status === 404) {
      // No active profile is the "central applies" steady state, not an error.
      return { ok: true, profile: null, etag: null, version: null }
    }
    if (!res.ok) {
      const text = await res.text().catch(() => '')
      return { ok: false, error: text || `HTTP ${res.status}`, status: res.status }
    }
    const data = (await res.json()) as LocalAgreementProfile
    // S23 / TASK-2303: prefer ETag header, fall back to body `version` for
    // cross-origin deployments where the header isn't exposed. Strict body-
    // version validation guards against `"undefined"` token synthesis.
    const { etag, version } = resolveEtag(res.headers.get('ETag'), data)
    return { ok: true, profile: data, etag, version }
  } catch (e) {
    return { ok: false, error: String(e), status: 0 }
  }
}

export async function getProfileHistory(
  orgId: string,
  agreementCode: string,
  okVersion: string,
): Promise<{ ok: true; history: LocalAgreementProfile[] } | { ok: false; error: string; status: number }> {
  const url = `/api/config/${encodeURIComponent(orgId)}/profile/${encodeURIComponent(agreementCode)}/${encodeURIComponent(okVersion)}/history`
  try {
    const res = await fetch(url, { method: 'GET', headers: authHeaders() })
    if (!res.ok) {
      const text = await res.text().catch(() => '')
      return { ok: false, error: text || `HTTP ${res.status}`, status: res.status }
    }
    const data = (await res.json()) as LocalAgreementProfile[]
    return { ok: true, history: data ?? [] }
  } catch (e) {
    return { ok: false, error: String(e), status: 0 }
  }
}

export interface ProfileSaveRequest {
  effectiveFrom: string  // ISO yyyy-MM-dd
  weeklyNormHours: number | null
  maxFlexBalance: number | null
  flexCarryoverMax: number | null
  maxOvertimeHoursPerPeriod: number | null
  overtimeRequiresPreApproval: boolean | null
}

export interface ProfileSaveSuccess {
  ok: true
  savedProfile: LocalAgreementProfile
  /** Raw RFC 7232 quoted ETag returned by the PUT response (S22: `"<version>"`). */
  newEtag: string | null
  /** Parsed integer `version` from the new ETag header — convenience view. */
  newVersion: number | null
}

export interface ProfileSaveFailure {
  ok: false
  status: number
  error: string
  // Structured 412 / 400 details — callers branch on status to render banner / per-field errors.
  body?: ProfileSaveError
}

export type ProfileSaveResult = ProfileSaveSuccess | ProfileSaveFailure

export async function saveProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
  body: ProfileSaveRequest,
  etag: string | null,
): Promise<ProfileSaveResult> {
  const url = `/api/config/${encodeURIComponent(orgId)}/profile/${encodeURIComponent(agreementCode)}/${encodeURIComponent(okVersion)}`
  // ETag is the optimistic-concurrency cookie. Null means "I expect no current
  // profile" → If-None-Match: *. Non-null means "I expect this version" →
  // If-Match: "<version>". The backend (ConfigEndpoints.TryParseConcurrencyPrecondition)
  // requires exactly one to be set.
  //
  // S22 / ADR-018 D7: re-format the inbound etag through the helper so the
  // outgoing wire form is canonical (`"<integer>"`) regardless of what shape
  // the caller stored. If parsing fails (e.g. legacy quoted-UUID etag persisted
  // somewhere) we pass the raw value through — the backend strips quotes
  // anyway, so an unparseable string still surfaces a clean 412 with the
  // current `actualVersion`.
  let precondition: Record<string, string>
  if (etag === null) {
    precondition = { 'If-None-Match': '*' }
  } else {
    const parsedVersion = parseVersionFromETag(etag)
    precondition = parsedVersion !== null
      ? { 'If-Match': formatVersionAsIfMatch(parsedVersion) }
      : { 'If-Match': etag }
  }

  try {
    const res = await fetch(url, {
      method: 'PUT',
      headers: authHeaders(precondition),
      body: JSON.stringify(body),
    })

    if (!res.ok) {
      const text = await res.text().catch(() => '')
      let parsed: ProfileSaveError | undefined
      try {
        parsed = text ? (JSON.parse(text) as ProfileSaveError) : undefined
      } catch {
        // Non-JSON body — leave parsed undefined.
      }
      return { ok: false, status: res.status, error: text || `HTTP ${res.status}`, body: parsed }
    }

    const saved = (await res.json()) as LocalAgreementProfile
    // S23 / TASK-2303: same fallback as getCurrentProfile — header preferred,
    // body.version fallback with strict runtime validation, both null on
    // validation failure (no `"undefined"` token).
    const { etag: newEtag, version: newVersion } = resolveEtag(
      res.headers.get('ETag'),
      saved,
    )
    return {
      ok: true,
      savedProfile: saved,
      newEtag,
      newVersion,
    }
  } catch (e) {
    return { ok: false, status: 0, error: String(e) }
  }
}
