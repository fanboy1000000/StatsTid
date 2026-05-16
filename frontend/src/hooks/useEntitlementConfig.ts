import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// TASK-3009 (Phase 4d-2 / ADR-021 pending). Mirrors the S25 admin-strict hook
// shape from `useWageTypeMappings.ts` / `usePositionOverrides.ts`: live-row
// list, by-id GET with ETag header capture, create/update/delete mutations
// throwing a typed mutation error carrying status + 412 body so the page's
// banner-with-retry handler can read `expectedVersion` / `actualVersion`.

export type EntitlementType =
  | 'VACATION'
  | 'SPECIAL_HOLIDAY'
  | 'CARE_DAY'
  | 'CHILD_SICK'
  | 'SENIOR_DAY'

export type AccrualModel = 'IMMEDIATE' | 'MONTHLY_ACCRUAL'

export interface EntitlementConfig {
  configId: string
  entitlementType: EntitlementType
  agreementCode: string
  okVersion: string
  annualQuota: number
  accrualModel: AccrualModel
  resetMonth: number // 1-12
  carryoverMax: number
  proRateByPartTime: boolean
  isPerEpisode: boolean
  minAge: number | null
  description: string | null
  // ADR-019 D7 row-version optimistic-concurrency token.
  version: number
  effectiveFrom: string // ISO date
  effectiveTo: string | null // ISO date, null for live rows
}

/**
 * Editable subset per TASK-3009 spec (Risk R4 scope-trim). Natural-key fields
 * (entitlementType / agreementCode / okVersion), accrualModel + resetMonth
 * (frozen per ADR-021 Q1 sub-fork (i)), and server-managed fields
 * (effectiveFrom / effectiveTo / version / configId) are NOT in this patch.
 */
export interface EntitlementConfigPatch {
  annualQuota: number
  carryoverMax: number
  description: string | null
  proRateByPartTime: boolean
  isPerEpisode: boolean
  minAge: number | null
}

/**
 * Create request — natural-key fields + accrualModel + resetMonth must be
 * supplied (these are frozen post-create). The server stamps effectiveFrom
 * to today and effectiveTo to null. The page-level form should never let the
 * user pick `effectiveFrom` — per Q4 it is implicitly today.
 */
export interface EntitlementConfigCreateRequest extends EntitlementConfigPatch {
  entitlementType: EntitlementType
  agreementCode: string
  okVersion: string
  accrualModel: AccrualModel
  resetMonth: number
}

/**
 * Update request — backend `UpdateEntitlementConfigRequest` requires the full
 * shape: natural-key fields + frozen fields (accrualModel/resetMonth) + the
 * editable patch + explicit `effectiveFrom`. The natural-key + frozen fields
 * are validated against the predecessor row (422 if changed per Q1 sub-fork
 * (i)); `effectiveFrom` must equal today per the cycle-3 same-day-only-edit
 * validator. Callers source the frozen fields from the editing row's
 * `WithEtag<EntitlementConfig>` (the page already has them — they're displayed
 * read-only).
 */
export interface EntitlementConfigUpdateRequest extends EntitlementConfigPatch {
  entitlementType: EntitlementType
  agreementCode: string
  okVersion: string
  accrualModel: AccrualModel
  resetMonth: number
  effectiveFrom: string // ISO date — must be today
}

export type WithEtag<T> = T & { etag: string; version: number }

export interface EntitlementConfigMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
    currentState?: unknown
    supplied?: { reset_month?: number; accrual_model?: string }
    immutable?: string[]
  }
}

function decorateRow(row: EntitlementConfig): WithEtag<EntitlementConfig> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: EntitlementConfigMutationError['body'],
): EntitlementConfigMutationError {
  const err = new Error(errorMsg) as EntitlementConfigMutationError
  err.status = status
  err.body = body
  return err
}

/**
 * Live-row list of entitlement configs (server filters effective_to IS NULL).
 */
export function useEntitlementConfigList() {
  const [configs, setConfigs] = useState<WithEtag<EntitlementConfig>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<EntitlementConfig[]>('/api/admin/entitlement-configs')
    if (result.ok) {
      setConfigs(result.data.map(decorateRow))
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    fetchAll()
  }, [fetchAll])

  return { configs, loading, error, refetch: fetchAll }
}

/**
 * Single-config GET-by-id with ETag-header capture. The page uses this when
 * opening the edit dialog so the next PUT can compose `If-Match` against the
 * freshest version (not the list-snapshot version).
 */
export function useEntitlementConfig(configId: string | null) {
  const [config, setConfig] = useState<WithEtag<EntitlementConfig> | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfig = useCallback(async () => {
    if (!configId) {
      setConfig(null)
      return
    }
    setLoading(true)
    setError(null)
    const result = await apiFetchWithEtag<EntitlementConfig>(
      `/api/admin/entitlement-configs/${configId}`,
    )
    if (result.ok) {
      const { data, etag } = result.data
      const { etag: resolvedEtag } = resolveEtag(etag, data)
      setConfig({
        ...data,
        etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
      })
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [configId])

  useEffect(() => {
    fetchConfig()
  }, [fetchConfig])

  return { config, loading, error, refetch: fetchConfig }
}

/**
 * Mutation hooks — separated from the list query so the page can refetch the
 * list after any mutation without re-entering the mutation hook.
 */
export function useEntitlementConfigActions() {
  function withResponseEtag(
    data: EntitlementConfig,
    etag: string | null,
  ): WithEtag<EntitlementConfig> {
    const { etag: resolvedEtag } = resolveEtag(etag, data)
    return {
      ...data,
      etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
    }
  }

  const createConfig = async (
    body: EntitlementConfigCreateRequest,
  ): Promise<WithEtag<EntitlementConfig>> => {
    const result = await apiFetchWithEtag<EntitlementConfig>(
      '/api/admin/entitlement-configs',
      { method: 'POST', body: JSON.stringify(body) },
    )
    if (!result.ok) {
      throw makeMutationError(
        result.status,
        result.error,
        result.body as EntitlementConfigMutationError['body'],
      )
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  const updateConfig = async (
    configId: string,
    ifMatch: string,
    body: EntitlementConfigUpdateRequest,
  ): Promise<WithEtag<EntitlementConfig>> => {
    const result = await apiFetchWithEtag<EntitlementConfig>(
      `/api/admin/entitlement-configs/${configId}`,
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
    if (!result.ok) {
      throw makeMutationError(
        result.status,
        result.error,
        result.body as EntitlementConfigMutationError['body'],
      )
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  const deleteConfig = async (configId: string, ifMatch: string): Promise<void> => {
    const result = await apiFetchWithEtag<void>(
      `/api/admin/entitlement-configs/${configId}`,
      { method: 'DELETE', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(
        result.status,
        result.error,
        result.body as EntitlementConfigMutationError['body'],
      )
    }
  }

  return { createConfig, updateConfig, deleteConfig }
}
