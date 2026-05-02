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
import {
  getCurrentProfile,
  getProfileHistory,
  saveProfile as saveProfileApi,
  type ProfileSaveRequest,
} from '../api/profileApi'

// ── Types (inline; types.ts does not own them — see useAgreementConfigs.ts precedent). ──

/**
 * Local agreement profile (S21 / ADR-017 D1). Shape mirrors the backend's
 * MapProfileResponse output. The five overridable fields are nullable —
 * NULL means "inherit central." createdAt is an ISO-8601 timestamp; effectiveFrom
 * and effectiveTo are ISO yyyy-MM-dd date strings.
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
 * 400-validation-error body shape returned by PUT (ADR-017 D9a). The backend
 * (`ConfigEndpoints`) returns `{ error, fields: ProfileFieldError[] }` for
 * both alignment failures and the `EFFECTIVE_FROM_NOT_TODAY_OR_PAST`
 * rejection. 412 stale-state responses use the same wrapper with no `fields`.
 */
export interface ProfileSaveError {
  error?: string
  fields?: ProfileFieldError[]
  message?: string
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
 * `profile = null` when no local profile exists (central applies). The `etag` is
 * required as the next save's If-Match precondition; null means "no profile" and
 * the next save must use If-None-Match: *.
 */
export function useCurrentProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
) {
  const [profile, setProfile] = useState<LocalAgreementProfile | null>(null)
  const [etag, setEtag] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!orgId || !agreementCode || !okVersion) {
      setProfile(null)
      setEtag(null)
      return
    }
    setLoading(true)
    setError(null)
    const result = await getCurrentProfile(orgId, agreementCode, okVersion)
    if (result.ok) {
      setProfile(result.profile)
      setEtag(result.etag)
    } else {
      setError(result.error)
      setProfile(null)
      setEtag(null)
    }
    setLoading(false)
  }, [orgId, agreementCode, okVersion])

  useEffect(() => { refresh() }, [refresh])

  return { profile, etag, loading, error, refresh }
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
 * Saves a profile via PUT. Returns the newly persisted profile + new ETag on
 * success. Throws a structured `Error` whose `cause` carries `{status, body}`
 * on failure — callers branch on `status === 412` for the stale-state banner
 * and on `status === 400` for per-field error rendering.
 */
export async function saveProfile(
  orgId: string,
  agreementCode: string,
  okVersion: string,
  body: ProfileSaveRequest,
  etag: string | null,
): Promise<{ savedProfile: LocalAgreementProfile; newEtag: string | null }> {
  const result = await saveProfileApi(orgId, agreementCode, okVersion, body, etag)
  if (result.ok) {
    return { savedProfile: result.savedProfile, newEtag: result.newEtag }
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
