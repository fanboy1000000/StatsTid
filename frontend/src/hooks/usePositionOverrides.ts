import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S118 / TASK-11801 (Typed API Contract retrofit Pass 5, PAT-012) — every call
// rides the TYPED structured forms; the hand-written `PositionOverrideConfig`
// interface (audited FAITHFUL) and the merged deactivate/activate envelope were
// DELETED in favor of the GENERATED spec types. `status` is now the spec enum
// ("ACTIVE" | "INACTIVE" — DB CHECK authority).

/** The GENERATED spec row (S118) — replaces the hand-written interface. */
export type PositionOverrideConfig =
  components['schemas']['StatsTid.Backend.Api.Contracts.PositionOverrideResponse']

export type PositionOverrideCreateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.PositionOverrideEndpoints.CreatePositionOverrideRequest']

export type PositionOverrideUpdateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.PositionOverrideEndpoints.UpdatePositionOverrideRequest']

type DeactivateResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.PositionOverrideDeactivateResponse']
type ActivateResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.PositionOverrideActivateResponse']

/**
 * S25 / TASK-2506: row enriched with the wire-format `etag` for next-mutation
 * `If-Match` composition.
 */
export type WithEtag<T> = T & { etag: string; version: number }

export interface PositionOverrideMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
    currentState?: unknown
  }
}

/** S118 — undeclared error payloads narrow via a runtime type guard, never a
    cast (PAT-012 no-`as` surface; all members optional → structurally sound). */
function isMutationErrorBody(
  body: unknown,
): body is NonNullable<PositionOverrideMutationError['body']> {
  return typeof body === 'object' && body !== null
}

function decorateRow(row: PositionOverrideConfig): WithEtag<PositionOverrideConfig> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: unknown,
): PositionOverrideMutationError {
  // Object.assign (not an `as` cast) — this file is on the no-`as` surface.
  return Object.assign(new Error(errorMsg), {
    status,
    body: isMutationErrorBody(body) ? body : undefined,
  })
}

export function usePositionOverrides() {
  const [data, setData] = useState<WithEtag<PositionOverrideConfig>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get('/api/admin/position-overrides')
    if (result.ok) {
      setData(result.data.map(decorateRow))
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  // UNCONDITIONED create (no precondition — S118 demand map). 201 → the full
  // entity (the S118 backend closed the create fork: INSERT…RETURNING).
  const create = async (
    body: PositionOverrideCreateRequest,
  ): Promise<WithEtag<PositionOverrideConfig>> => {
    const result = await apiFetchWithEtag('/api/admin/position-overrides', {
      method: 'POST',
      body,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  const update = async (
    overrideId: string,
    ifMatch: string,
    body: PositionOverrideUpdateRequest,
  ): Promise<WithEtag<PositionOverrideConfig>> => {
    const result = await apiFetchWithEtag('/api/admin/position-overrides/{overrideId}', {
      method: 'PUT',
      params: { path: { overrideId } },
      ifMatch,
      body,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  const deactivate = async (
    overrideId: string,
    ifMatch: string,
  ): Promise<DeactivateResponse & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag('/api/admin/position-overrides/{overrideId}/deactivate', {
      method: 'POST',
      params: { path: { overrideId } },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data: env, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...env, etag: resolvedEtag, version }
  }

  const activate = async (
    overrideId: string,
    ifMatch: string,
  ): Promise<ActivateResponse & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag('/api/admin/position-overrides/{overrideId}/activate', {
      method: 'POST',
      params: { path: { overrideId } },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data: env, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...env, etag: resolvedEtag, version }
  }

  return { data, loading, error, refetch: fetchAll, create, update, deactivate, activate }
}
