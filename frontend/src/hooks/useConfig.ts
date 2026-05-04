// Local agreement profile hooks (S21 / ADR-017 D5).
//
// The legacy per-row useEffectiveConfig + useLocalConfig hooks were removed
// in S21 Phase 3 (TASK-2107) — the endpoints they called no longer exist.
// This module now exposes profile-shaped hooks aligned to the three
// `/api/config/{orgId}/profile/{agreement}/{okVersion}` endpoints, plus the
// pre-existing `useConfigConstraints` (unchanged — it reads central agreement
// configs, which were never touched by S21).
//
// Scope: TASK-2109 frontend rework. Basic-functional only per the user's
// "no Phase-5 polish" decision.
import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import { parseVersionFromETag } from '../lib/etag'
import {
  getCurrentProfile,
  getProfileHistory,
  saveProfile as saveProfileApi,
  type ProfileSaveRequest,
} from '../api/profileApi'

// Re-export the helpers so call sites that already import from useConfig (the
// hook entry point) don't need a second import path. The canonical home is
// `lib/etag.ts`.
export { parseVersionFromETag, formatVersionAsIfMatch } from '../lib/etag'

// ── Types (inline; types.ts does not own them — see useAgreementConfigs.ts precedent). ──

/**
 * Local agreement profile (S21 / ADR-017 D1, S22 / ADR-018 D7). Shape mirrors
 * the backend's MapProfileResponse output. The five overridable fields are
 * nullable — NULL means "inherit central." createdAt is an ISO-8601 timestamp;
 * effectiveFrom and effectiveTo are ISO yyyy-MM-dd date strings.
 *
 * S22 added the `version` field: a monotonically increasing integer that
 * doubles as the optimistic-concurrency token (replaces profileId-as-ETag).
 * The wire-format ETag header carries this value as a quoted decimal — see
 * `lib/etag.ts`.
 */
export interface LocalAgreementProfile {
  profileId: string
  orgId: string
  agreementCode: string
  okVersion: string
  effectiveFrom: string
  effectiveTo: string | null
  weeklyNormHours: number | null
  maxFlexBalance: number | null
  flexCarryoverMax: number | null
  maxOvertimeHoursPerPeriod: number | null
  overtimeRequiresPreApproval: boolean | null
  createdBy: string
  createdAt: string
  version: number
}

/**
 * Per-field validation error inside the 400 payload (ADR-017 D9a).
 */
export interface ProfileFieldError {
  field?: string
  code?: string
  message?: string
  nearestValid?: string[]
}

/**
 * Error-response body shape returned by PUT (ADR-017 D9a, S22 ADR-018 D7).
 *   - 400 alignment / `EFFECTIVE_FROM_NOT_TODAY_OR_PAST` →
 *       `{ error, fields: ProfileFieldError[] }`
 *   - 400 backdate-before-predecessor (S22 InvalidProfileSupersessionException) →
 *       `{ error: "Invalid profile supersession", message }`
 *   - 412 stale-state → `{ error, expectedVersion, actualVersion, currentState }`
 *     (was `expectedProfileId`/`actualProfileId` pre-S22).
 */
export interface ProfileSaveError {
  error?: string
  fields?: ProfileFieldError[]
  message?: string
  expectedVersion?: number
  actualVersion?: number
  currentState?: LocalAgreementProfile | null
  [k: string]: unknown
}

/**
 * Central constraint reference (one row per active (agreement, OkVersion)).
 * Returned by GET /api/config/constraints — AgreementConfigRepository's ACTIVE
 * configs projected into a flat row. Read-only.
 */
export interface ConfigConstraint {
  agreementCode: string
  okVersion: string
  weeklyNormHours: number
  maxFlexBalance: number
  flexCarryoverMax: number
  hasOvertime: boolean
  hasMerarbejde: boolean
  eveningSupplementEnabled: boolean
  nightSupplementEnabled: boolean
  weekendSupplementEnabled: boolean
  holidaySupplementEnabled: boolean
  onCallDutyEnabled: boolean
  onCallDutyRate: number
}

// ── Hooks ──

/**
 * Loads the currently active profile for a (org, agreement, OkVersion). Returns
 * `profile = null` when no local profile exists (central applies).
 *
 * Both `etag` (raw RFC 7232 quoted header value) and `version` (parsed
 * integer) are exposed; they carry the same information. `etag` is the
 * canonical wire form — pass it through to the save call. `version` is the
 * convenience parsed view for UI that wants to display the version number or
 * compare to a 412 body's `expectedVersion`/`actualVersion`. Both are null
 * when no profile exists; the next save must use If-None-Match: *.
 *
 * S22 / ADR-018 D7 wire-format change: pre-S22 the ETag carried the profile
 * UUID; post-S22 it carries the integer `version`. The hook contract is
 * unchanged — call sites pass `etag` opaquely to `saveProfile`.
 */
export function useCurrentProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
) {
  const [profile, setProfile] = useState<LocalAgreementProfile | null>(null)
  const [etag, setEtag] = useState<string | null>(null)
  const [version, setVersion] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!orgId || !agreementCode || !okVersion) {
      setProfile(null)
      setEtag(null)
      setVersion(null)
      return
    }
    setLoading(true)
    setError(null)
    const result = await getCurrentProfile(orgId, agreementCode, okVersion)
    if (result.ok) {
      setProfile(result.profile)
      setEtag(result.etag)
      setVersion(parseVersionFromETag(result.etag))
    } else {
      setError(result.error)
      setProfile(null)
      setEtag(null)
      setVersion(null)
    }
    setLoading(false)
  }, [orgId, agreementCode, okVersion])

  useEffect(() => { refresh() }, [refresh])

  return { profile, etag, version, loading, error, refresh }
}

/**
 * Loads the closed-predecessor history for a (org, agreement, OkVersion) tuple.
 * Backend orders most-recently-closed first.
 */
export function useProfileHistory(
  orgId: string,
  agreementCode: string,
  okVersion: string,
) {
  const [history, setHistory] = useState<LocalAgreementProfile[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!orgId || !agreementCode || !okVersion) {
      setHistory([])
      return
    }
    setLoading(true)
    setError(null)
    const result = await getProfileHistory(orgId, agreementCode, okVersion)
    if (result.ok) {
      setHistory(result.history)
    } else {
      setError(result.error)
      setHistory([])
    }
    setLoading(false)
  }, [orgId, agreementCode, okVersion])

  useEffect(() => { refresh() }, [refresh])

  return { history, loading, error, refresh }
}

/**
 * Saves a profile via PUT. Returns the newly persisted profile + new ETag
 * (raw quoted form) and parsed version on success. Throws a structured
 * `Error` carrying `{status, body}` on failure — callers branch on
 * `status === 412` for the stale-state banner and on `status === 400` for
 * per-field error rendering. S22 / ADR-018 D7: 400 may also be a
 * backdate-before-predecessor rejection (`error: "Invalid profile
 * supersession"` with no `fields`); callers fall through to the banner.
 */
export async function saveProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
  body: ProfileSaveRequest,
  etag: string | null,
): Promise<{ savedProfile: LocalAgreementProfile; newEtag: string | null; newVersion: number | null }> {
  const result = await saveProfileApi(orgId, agreementCode, okVersion, body, etag)
  if (result.ok) {
    return {
      savedProfile: result.savedProfile,
      newEtag: result.newEtag,
      newVersion: result.newVersion,
    }
  }
  const err = new Error(result.error || `HTTP ${result.status}`) as Error & {
    status?: number
    body?: ProfileSaveError
  }
  err.status = result.status
  err.body = result.body
  throw err
}

// ── Unchanged: central constraint reference. ──

export function useConfigConstraints() {
  const [constraints, setConstraints] = useState<ConfigConstraint[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConstraints = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ConfigConstraint[]>('/api/config/constraints')
    if (result.ok) {
      setConstraints(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchConstraints() }, [fetchConstraints])

  return { constraints, loading, error, fetchConstraints }
}
