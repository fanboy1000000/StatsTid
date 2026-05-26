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
// S53 / TASK-5304: Auth header logic consolidated through `apiFetchWithEtag`
// from `lib/api.ts` (which also handles 401 auto-clear + reload). The previous
// local `TOKEN_KEY` / `getToken` / `authHeaders` helpers are removed.
import type { LocalAgreementProfile, ProfileSaveError } from '../hooks/useConfig'
import { parseVersionFromETag, formatVersionAsIfMatch, resolveEtag } from '../lib/etag'
import { apiFetchWithEtag } from '../lib/api'

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
  const result = await apiFetchWithEtag<LocalAgreementProfile>(url, { method: 'GET' })
  if (!result.ok) {
    if (result.status === 404) {
      // No active profile is the "central applies" steady state, not an error.
      return { ok: true, profile: null, etag: null, version: null }
    }
    return { ok: false, error: result.error, status: result.status }
  }
  const { data, etag: rawEtag } = result.data
  // S23 / TASK-2303: prefer ETag header, fall back to body `version` for
  // cross-origin deployments where the header isn't exposed. Strict body-
  // version validation guards against `"undefined"` token synthesis.
  const { etag, version } = resolveEtag(rawEtag, data)
  return { ok: true, profile: data, etag, version }
}

export async function getProfileHistory(
  orgId: string,
  agreementCode: string,
  okVersion: string,
): Promise<{ ok: true; history: LocalAgreementProfile[] } | { ok: false; error: string; status: number }> {
  const url = `/api/config/${encodeURIComponent(orgId)}/profile/${encodeURIComponent(agreementCode)}/${encodeURIComponent(okVersion)}/history`
  const result = await apiFetchWithEtag<LocalAgreementProfile[]>(url, { method: 'GET' })
  if (!result.ok) {
    return { ok: false, error: result.error, status: result.status }
  }
  return { ok: true, history: result.data.data ?? [] }
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

  const result = await apiFetchWithEtag<LocalAgreementProfile>(url, {
    method: 'PUT',
    headers: precondition,
    body: JSON.stringify(body),
  })

  if (!result.ok) {
    let parsed: ProfileSaveError | undefined
    try {
      parsed = result.body ? (result.body as ProfileSaveError) : undefined
    } catch {
      // Non-structured body — leave parsed undefined.
    }
    return { ok: false, status: result.status, error: result.error, body: parsed }
  }

  const saved = result.data.data
  // S23 / TASK-2303: same fallback as getCurrentProfile — header preferred,
  // body.version fallback with strict runtime validation, both null on
  // validation failure (no `"undefined"` token).
  const { etag: newEtag, version: newVersion } = resolveEtag(
    result.data.etag,
    saved,
  )
  return {
    ok: true,
    savedProfile: saved,
    newEtag,
    newVersion,
  }
}
