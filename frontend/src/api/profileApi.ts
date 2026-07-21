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
//
// S119 / TASK-11901 (Typed API Contract retrofit Pass 6, PAT-012): all three
// calls ride the TYPED spec-keyed `apiFetchWithEtag` form. The hand-written
// `ProfileSaveRequest` local was DELETED in favor of the generated spec type
// (audited: same 6 keys; the 5 override members are spec-optional because they
// are not C#-`required` — the FE always sends all 6, byte-unchanged). The PUT
// threads the flexible precondition through the structured options: null etag →
// `ifNoneMatch: '*'` (the program's FIRST live use), else `ifMatch` with the
// ready RFC 7232 wire string. Error bodies stay DELIBERATELY UNTYPED (the
// 5-shape non-2xx surface incl. the nullable-`currentState` 412) — the failure
// path narrows `result.body` via a runtime type GUARD, never a cast.
import type { LocalAgreementProfile, ProfileSaveError } from '../hooks/useConfig'
import type { components } from '../lib/api-types'
import { parseVersionFromETag, formatVersionAsIfMatch, resolveEtag } from '../lib/etag'
import { apiFetchWithEtag } from '../lib/api'

// (Plain const declarations — the literal types are inferred, no `as const`
// needed on this no-`as` surface.)
const PROFILE_PATH = '/api/config/{orgId}/profile/{agreementCode}/{okVersion}'
const PROFILE_HISTORY_PATH =
  '/api/config/{orgId}/profile/{agreementCode}/{okVersion}/history'

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
  const result = await apiFetchWithEtag(PROFILE_PATH, {
    method: 'GET',
    params: { path: { orgId, agreementCode, okVersion } },
  })
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
  const result = await apiFetchWithEtag(PROFILE_HISTORY_PATH, {
    method: 'GET',
    params: { path: { orgId, agreementCode, okVersion } },
  })
  if (!result.ok) {
    return { ok: false, error: result.error, status: result.status }
  }
  return { ok: true, history: result.data.data ?? [] }
}

/**
 * The GENERATED spec request type (S119) — replaces the hand-written local.
 * `effectiveFrom` (ISO yyyy-MM-dd) is the only binder-required member; the five
 * override members are spec-optional (not C#-`required`) but the FE always
 * sends all six keys, byte-identical to the pre-S119 payload.
 */
export type ProfileSaveRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.ConfigEndpoints.ProfileSaveRequest']

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

/**
 * S119 — undeclared error payloads (the profile PUT's 5-shape non-2xx surface)
 * narrow via a runtime type GUARD on the no-`as` surface, never a cast. Every
 * `ProfileSaveError` member is optional (plus an index signature), so any
 * non-null object is structurally admissible; callers branch on `status`.
 */
function isProfileSaveError(body: unknown): body is ProfileSaveError {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
}

export async function saveProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
  body: ProfileSaveRequest,
  etag: string | null,
): Promise<ProfileSaveResult> {
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
  //
  // S119: the branches map 1:1 onto the typed options — `ifNoneMatch: '*'`
  // (the create-only precondition; mutually exclusive with `ifMatch` BY
  // CONSTRUCTION here) vs `ifMatch` with the ready RFC 7232 wire string. The
  // emitted headers are byte-identical to the legacy `headers:` composition.
  const params = { path: { orgId, agreementCode, okVersion } }
  let result
  if (etag === null) {
    result = await apiFetchWithEtag(PROFILE_PATH, {
      method: 'PUT',
      params,
      ifNoneMatch: '*',
      body,
    })
  } else {
    const parsedVersion = parseVersionFromETag(etag)
    result = await apiFetchWithEtag(PROFILE_PATH, {
      method: 'PUT',
      params,
      ifMatch: parsedVersion !== null ? formatVersionAsIfMatch(parsedVersion) : etag,
      body,
    })
  }

  if (!result.ok) {
    const parsed = isProfileSaveError(result.body) ? result.body : undefined
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
