import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

export interface WageTypeMappingItem {
  timeType: string
  wageType: string
  okVersion: string
  agreementCode: string
  position: string
  description: string | null
  // S25 / TASK-2506 (ADR-019 pending): row-version optimistic-concurrency token.
  version: number
}

export type WithEtag<T> = T & { etag: string; version: number }

export interface WageTypeMappingMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
    currentState?: unknown
  }
}

function decorateRow(row: WageTypeMappingItem): WithEtag<WageTypeMappingItem> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: WageTypeMappingMutationError['body'],
): WageTypeMappingMutationError {
  const err = new Error(errorMsg) as WageTypeMappingMutationError
  err.status = status
  err.body = body
  return err
}

export function useWageTypeMappings() {
  const [data, setData] = useState<WithEtag<WageTypeMappingItem>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<WageTypeMappingItem[]>('/api/admin/wage-type-mappings')
    if (result.ok) {
      setData(result.data.map(decorateRow))
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  // The CREATE response on the wire is a small envelope (timeType, wageType,
  // okVersion, agreementCode, position, description, version=1). We expose the
  // same shape with the etag attached for caller convenience.
  const create = async (
    body: Omit<WageTypeMappingItem, 'version'>,
  ): Promise<WithEtag<WageTypeMappingItem>> => {
    const result = await apiFetchWithEtag<WageTypeMappingItem>(
      '/api/admin/wage-type-mappings',
      { method: 'POST', body: JSON.stringify(body) },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as WageTypeMappingMutationError['body'])
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  const updateMapping = async (
    ifMatch: string,
    body: Omit<WageTypeMappingItem, 'version'>,
  ): Promise<WithEtag<WageTypeMappingItem>> => {
    const result = await apiFetchWithEtag<WageTypeMappingItem>(
      '/api/admin/wage-type-mappings',
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as WageTypeMappingMutationError['body'])
    }
    const { data: row, etag } = result.data
    await fetchAll()
    const { etag: resolvedEtag } = resolveEtag(etag, row)
    return { ...row, etag: resolvedEtag ?? formatVersionAsIfMatch(row.version) }
  }

  // DELETE returns 204 No Content with NO ETag header (resource gone).
  const deleteMapping = async (
    timeType: string,
    okVersion: string,
    agreementCode: string,
    position: string,
    ifMatch: string,
  ): Promise<void> => {
    const params = new URLSearchParams({ timeType, okVersion, agreementCode, position })
    const result = await apiFetchWithEtag<void>(
      `/api/admin/wage-type-mappings?${params}`,
      { method: 'DELETE', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as WageTypeMappingMutationError['body'])
    }
    await fetchAll()
  }

  return { data, loading, error, refetch: fetchAll, create, updateMapping, deleteMapping }
}
