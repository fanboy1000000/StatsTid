import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// TASK-3009 (Phase 4d-2 / ADR-021 pending). Mirrors the S25 admin-strict hook
// shape from `useWageTypeMappings.ts` / `usePositionOverrides.ts`: live-row
// list, by-id GET with ETag header capture, create/update/delete mutations
// throwing a typed mutation error carrying status + 412 body so the page's
// banner-with-retry handler can read `expectedVersion` / `actualVersion`.
//
// S118 / TASK-11801 (Typed API Contract retrofit Pass 5, PAT-012) — every call
// rides the TYPED structured forms; the hand-written `EntitlementConfig` row
// interface and the hand-written create/update request interfaces were DELETED
// in favor of the GENERATED spec types below (audited FAITHFUL — same field
// sets). NOTE the wire types carry `entitlementType` / `accrualModel` as OPEN
// `string` (deliberately not spec-enums); narrow via the runtime guards in
// `lib/entitlementConstants.ts` where the UI needs the closed set.

/** The GENERATED spec row (S118) — replaces the hand-written interface. */
export type EntitlementConfig =
  components['schemas']['StatsTid.Backend.Api.Contracts.EntitlementConfigResponse']

/**
 * Create request — the GENERATED spec type. Natural-key fields + accrualModel
 * + resetMonth must be supplied (frozen post-create); the server stamps
 * effectiveFrom to today when omitted. The page-level form should never let
 * the user pick `effectiveFrom` — per Q4 it is implicitly today.
 */
export type EntitlementConfigCreateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.EntitlementConfigEndpoints.CreateEntitlementConfigRequest']

/**
 * Update request — the GENERATED spec type. Backend requires the full shape:
 * natural-key fields + frozen fields (accrualModel/resetMonth, validated
 * against the predecessor per Q1 sub-fork (i)) + the editable patch + explicit
 * `effectiveFrom` (must equal today per the cycle-3 same-day-only-edit
 * validator) + the round-tripped S73 `fullDayOnly` flag.
 */
export type EntitlementConfigUpdateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.EntitlementConfigEndpoints.UpdateEntitlementConfigRequest']

/**
 * Editable subset per TASK-3009 spec (Risk R4 scope-trim) — now DERIVED from
 * the spec create-request type rather than hand-written.
 */
export type EntitlementConfigPatch = Pick<
  EntitlementConfigCreateRequest,
  'annualQuota' | 'carryoverMax' | 'description' | 'proRateByPartTime' | 'isPerEpisode' | 'minAge'
>

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

/** S118 — undeclared error payloads narrow via a runtime type guard, never a
    cast (PAT-012 no-`as` surface; all members optional → structurally sound). */
function isMutationErrorBody(
  body: unknown,
): body is NonNullable<EntitlementConfigMutationError['body']> {
  return typeof body === 'object' && body !== null
}

/** S118 — runtime narrowing for thrown mutation errors (page-side handlers). */
export function isEntitlementConfigMutationError(
  err: unknown,
): err is EntitlementConfigMutationError {
  return err instanceof Error && 'status' in err && typeof err.status === 'number'
}

function decorateRow(row: EntitlementConfig): WithEtag<EntitlementConfig> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: unknown,
): EntitlementConfigMutationError {
  // Object.assign (not an `as` cast) — this file is on the no-`as` surface.
  return Object.assign(new Error(errorMsg), {
    status,
    body: isMutationErrorBody(body) ? body : undefined,
  })
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
    const result = await apiClient.get('/api/admin/entitlement-configs')
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
    const result = await apiFetchWithEtag('/api/admin/entitlement-configs/{configId}', {
      method: 'GET',
      params: { path: { configId } },
    })
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

  // UNCONDITIONED create (no precondition — S118 demand map); 201 → the row.
  const createConfig = async (
    body: EntitlementConfigCreateRequest,
  ): Promise<WithEtag<EntitlementConfig>> => {
    const result = await apiFetchWithEtag('/api/admin/entitlement-configs', {
      method: 'POST',
      body,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  const updateConfig = async (
    configId: string,
    ifMatch: string,
    body: EntitlementConfigUpdateRequest,
  ): Promise<WithEtag<EntitlementConfig>> => {
    const result = await apiFetchWithEtag('/api/admin/entitlement-configs/{configId}', {
      method: 'PUT',
      params: { path: { configId } },
      ifMatch,
      body,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  // If-Match DELETE → declared 204 (typed data = undefined; no ETag stamped).
  const deleteConfig = async (configId: string, ifMatch: string): Promise<void> => {
    const result = await apiFetchWithEtag('/api/admin/entitlement-configs/{configId}', {
      method: 'DELETE',
      params: { path: { configId } },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
  }

  return { createConfig, updateConfig, deleteConfig }
}
