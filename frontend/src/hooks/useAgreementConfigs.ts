import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

export interface AgreementConfig {
  configId: string
  agreementCode: string
  okVersion: string
  status: 'DRAFT' | 'ACTIVE' | 'ARCHIVED'
  // S25 / TASK-2506 (ADR-019 pending): row-version optimistic-concurrency token.
  // Returned by GET list / by-id and every mutating endpoint's response body.
  version: number
  weeklyNormHours: number
  normPeriodWeeks: number
  normModel: string
  annualNormHours: number
  maxFlexBalance: number
  flexCarryoverMax: number
  hasOvertime: boolean
  hasMerarbejde: boolean
  overtimeThreshold50: number
  overtimeThreshold100: number
  eveningSupplementEnabled: boolean
  nightSupplementEnabled: boolean
  weekendSupplementEnabled: boolean
  holidaySupplementEnabled: boolean
  eveningStart: number
  eveningEnd: number
  nightStart: number
  nightEnd: number
  eveningRate: number
  nightRate: number
  weekendSaturdayRate: number
  weekendSundayRate: number
  holidayRate: number
  onCallDutyEnabled: boolean
  onCallDutyRate: number
  callInWorkEnabled: boolean
  callInMinimumHours: number
  callInRate: number
  travelTimeEnabled: boolean
  workingTravelRate: number
  nonWorkingTravelRate: number
  createdBy: string
  createdAt: string
  updatedAt: string
  publishedAt: string | null
  archivedAt: string | null
  clonedFromId: string | null
  description: string | null
}

/**
 * S25 / TASK-2506: list/by-id rows enriched with the wire-format `etag` so the
 * page-level mutation handler can thread it back as `If-Match` without
 * recomposing it from `version`. `version` is also kept on the row (per the
 * SPRINT-25.md L354 spec) — both are present for caller convenience.
 */
export type WithEtag<T> = T & { etag: string; version: number }

/**
 * Error thrown by the mutation methods when the backend returns 412
 * (stale If-Match) or 428 (missing If-Match). The caller's banner-with-retry
 * UX inspects `status` and (on 412) `body.expectedVersion` / `actualVersion`.
 */
export interface ConfigMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
    currentState?: unknown
  }
}

function decorateRow(config: AgreementConfig): WithEtag<AgreementConfig> {
  // List rows expose `version` in the body; we synthesize the matching wire
  // form via the helper so the page passes back exactly what the backend's
  // EtagHeaderHelper.TryParseIfMatch expects.
  return { ...config, etag: formatVersionAsIfMatch(config.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: ConfigMutationError['body'],
): ConfigMutationError {
  const err = new Error(errorMsg) as ConfigMutationError
  err.status = status
  err.body = body
  return err
}

export function useAgreementConfigs(statusFilter?: string) {
  const [configs, setConfigs] = useState<WithEtag<AgreementConfig>[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfigs = useCallback(async () => {
    setLoading(true)
    setError(null)
    const path = statusFilter
      ? `/api/agreement-configs?status=${statusFilter}`
      : '/api/agreement-configs'
    const result = await apiClient.get<AgreementConfig[]>(path)
    if (result.ok) {
      setConfigs(result.data.map(decorateRow))
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [statusFilter])

  useEffect(() => { fetchConfigs() }, [fetchConfigs])

  return { configs, loading, error, refetch: fetchConfigs }
}

export function useAgreementConfig(configId: string) {
  const [config, setConfig] = useState<WithEtag<AgreementConfig> | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfig = useCallback(async () => {
    if (!configId) return
    setLoading(true)
    setError(null)
    // Use header-aware fetch so we capture the by-id ETag header rather than
    // re-formatting body.version (the endpoint sets ETag: "<version>" — same
    // value, but the header is the canonical source).
    const result = await apiFetchWithEtag<AgreementConfig>(`/api/agreement-configs/${configId}`)
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

  useEffect(() => { fetchConfig() }, [fetchConfig])

  return { config, loading, error, refetch: fetchConfig }
}

export function useAgreementConfigActions() {
  // Helper: thread the new ETag from the response into the returned row so
  // callers can immediately compose the next If-Match without a refetch.
  function withResponseEtag(
    data: AgreementConfig,
    etag: string | null,
  ): WithEtag<AgreementConfig> {
    const { etag: resolvedEtag } = resolveEtag(etag, data)
    return {
      ...data,
      etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
    }
  }

  const createConfig = async (
    body: Partial<AgreementConfig>,
  ): Promise<WithEtag<AgreementConfig>> => {
    const result = await apiFetchWithEtag<AgreementConfig>('/api/agreement-configs', {
      method: 'POST',
      body: JSON.stringify(body),
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as ConfigMutationError['body'])
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  const updateConfig = async (
    configId: string,
    ifMatch: string,
    body: Partial<AgreementConfig>,
  ): Promise<WithEtag<AgreementConfig>> => {
    const result = await apiFetchWithEtag<AgreementConfig>(
      `/api/agreement-configs/${configId}`,
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as ConfigMutationError['body'])
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  const cloneConfig = async (
    configId: string,
    agreementCode?: string,
    okVersion?: string,
  ): Promise<WithEtag<AgreementConfig>> => {
    const params = new URLSearchParams()
    if (agreementCode) params.set('agreementCode', agreementCode)
    if (okVersion) params.set('okVersion', okVersion)
    const query = params.toString() ? `?${params.toString()}` : ''
    const result = await apiFetchWithEtag<AgreementConfig>(
      `/api/agreement-configs/${configId}/clone${query}`,
      { method: 'POST' },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as ConfigMutationError['body'])
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  // Publish/Archive endpoints return a small envelope (configId, status,
  // archivedConfigId/archivedAt, publishedAt) — NOT the full AgreementConfig.
  // We surface the response data plus the new version (parsed from ETag) so
  // the caller can compose the next If-Match if needed (rarely — the row's
  // status transition usually triggers a full refetch).
  interface PublishArchiveEnvelope {
    configId: string
    status: string
    archivedConfigId?: string | null
    archivedAt?: string | null
    publishedAt?: string | null
  }

  const publishConfig = async (
    configId: string,
    ifMatch: string,
  ): Promise<PublishArchiveEnvelope & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag<PublishArchiveEnvelope>(
      `/api/agreement-configs/${configId}/publish`,
      { method: 'POST', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as ConfigMutationError['body'])
    }
    const { data, etag } = result.data
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...data, etag: resolvedEtag, version }
  }

  const archiveConfig = async (
    configId: string,
    ifMatch: string,
  ): Promise<PublishArchiveEnvelope & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag<PublishArchiveEnvelope>(
      `/api/agreement-configs/${configId}/archive`,
      { method: 'POST', headers: { 'If-Match': ifMatch } },
    )
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body as ConfigMutationError['body'])
    }
    const { data, etag } = result.data
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...data, etag: resolvedEtag, version }
  }

  return { createConfig, updateConfig, cloneConfig, publishConfig, archiveConfig }
}
