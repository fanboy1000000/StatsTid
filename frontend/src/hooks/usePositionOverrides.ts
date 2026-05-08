import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

export interface PositionOverrideConfig {
  overrideId: string
  agreementCode: string
  okVersion: string
  positionCode: string
  status: string
  // S25 / TASK-2506 (ADR-019 pending): row-version optimistic-concurrency token.
  version: number
  maxFlexBalance: number | null
  flexCarryoverMax: number | null
  normPeriodWeeks: number | null
  weeklyNormHours: number | null
  createdBy: string
  createdAt: string
  updatedAt: string
  description: string | null
}

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

function decorateRow(row: PositionOverrideConfig): WithEtag<PositionOverrideConfig> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: PositionOverrideMutationError['body'],
): PositionOverrideMutationError {
  const err = new Error(errorMsg) as PositionOverrideMutationError
  err.status = status
  err.body = body
  return err
}

interface DeactivateActivateEnvelope {
  overrideId: string
  status: string
  deactivated?: boolean
  activated?: boolean
}

export function usePositionOverrides() {
  const [data, setData] = useState<WithEtag<PositionOverrideConfig>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<PositionOverrideConfig[]>('/api/admin/position-overrides')
    if (result.ok) {
      setData(result.data.map(decorateRow))
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  const create = async (
    body: Partial<PositionOverrideConfig>,
  ): Promise<WithEtag<PositionOverrideConfig>> => {
    const result = await apiFetchWithEtag<PositionOverrideConfig>(
      '/api/admin/position-overrides',
      { method: 'POST', body: JSON.stringify(body) },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as PositionOverrideMutationError['body'])
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  const update = async (
    overrideId: string,
    ifMatch: string,
    body: Partial<PositionOverrideConfig>,
  ): Promise<WithEtag<PositionOverrideConfig>> => {
    const result = await apiFetchWithEtag<PositionOverrideConfig>(
      `/api/admin/position-overrides/${overrideId}`,
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as PositionOverrideMutationError['body'])
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  const deactivate = async (
    overrideId: string,
    ifMatch: string,
  ): Promise<DeactivateActivateEnvelope & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag<DeactivateActivateEnvelope>(
      `/api/admin/position-overrides/${overrideId}/deactivate`,
      { method: 'POST', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as PositionOverrideMutationError['body'])
    }
    const { data: env, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...env, etag: resolvedEtag, version }
  }

  const activate = async (
    overrideId: string,
    ifMatch: string,
  ): Promise<DeactivateActivateEnvelope & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag<DeactivateActivateEnvelope>(
      `/api/admin/position-overrides/${overrideId}/activate`,
      { method: 'POST', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as PositionOverrideMutationError['body'])
    }
    const { data: env, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...env, etag: resolvedEtag, version }
  }

  return { data, loading, error, refetch: fetchAll, create, update, deactivate, activate }
}
