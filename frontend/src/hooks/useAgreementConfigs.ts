import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S118 / TASK-11801 (Typed API Contract retrofit Pass 5, PAT-012) — every call
// in this hook rides the TYPED structured forms: reads via the spec-keyed
// `apiClient.get` / `apiFetchWithEtag(pathKey, { method: 'GET', params })`,
// If-Match mutations via `apiFetchWithEtag(pathKey, { method, params, ifMatch,
// body })`, the UNCONDITIONED create via the same form with NO precondition
// option. The hand-written 43-field `AgreementConfig` interface was DELETED —
// it OMITTED 5 compliance fields the backend emits (`maxDailyHours`,
// `minimumRestHours`, `restPeriodDerogationAllowed`,
// `weeklyMaxHoursReferencePeriod`, `voluntaryUnsocialHoursAllowed`); the spec
// type surfaces them additively.

/** The GENERATED spec row (S118) — replaces the hand-written interface. */
export type AgreementConfig =
  components['schemas']['StatsTid.Backend.Api.Contracts.AgreementConfigResponse']

/** The by-id GET shape: the config + inline `entitlements` (each row carries
    the S73 `fullDayOnly` flag — additive, spec-required) + the shared-key
    `entitlementsReadOnly` marker. */
export type AgreementConfigWithEntitlements =
  components['schemas']['StatsTid.Backend.Api.Contracts.AgreementConfigWithEntitlementsResponse']

/** The create/update request body — the GENERATED spec type (Swashbuckle
    inference from the backend's `AgreementConfigRequest`). */
export type AgreementConfigRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.AgreementConfigEndpoints.AgreementConfigRequest']

type PublishResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.AgreementConfigPublishResponse']
type ArchiveResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.AgreementConfigArchiveResponse']

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

/** S118 — the 412/428 error payload is UNDECLARED in the spec (error bodies are
    not typed), so it arrives as `unknown`. This runtime type guard (not an `as`
    cast — the file is on the no-`as` surface) passes the SAME runtime object
    through when it is an object; the target's members are all optional, so the
    claim is structurally sound (the S113 `useAdmin` precedent). */
function isMutationErrorBody(body: unknown): body is NonNullable<ConfigMutationError['body']> {
  return typeof body === 'object' && body !== null
}

/** S118 — runtime narrowing for thrown mutation errors so page-level handlers
    can branch on `status`/`body` without an `as` cast. */
export function isConfigMutationError(err: unknown): err is ConfigMutationError {
  return err instanceof Error && 'status' in err && typeof err.status === 'number'
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
  body: unknown,
): ConfigMutationError {
  // Object.assign (not an `as` cast) — this file is on the no-`as` surface.
  return Object.assign(new Error(errorMsg), {
    status,
    body: isMutationErrorBody(body) ? body : undefined,
  })
}

export function useAgreementConfigs(statusFilter?: string) {
  const [configs, setConfigs] = useState<WithEtag<AgreementConfig>[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfigs = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/agreement-configs', {
      query: statusFilter ? { status: statusFilter } : undefined,
    })
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
  const [config, setConfig] = useState<WithEtag<AgreementConfigWithEntitlements> | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfig = useCallback(async () => {
    if (!configId) return
    setLoading(true)
    setError(null)
    // Use header-aware fetch so we capture the by-id ETag header rather than
    // re-formatting body.version (the endpoint sets ETag: "<version>" — same
    // value, but the header is the canonical source).
    const result = await apiFetchWithEtag('/api/agreement-configs/{configId}', {
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

  // UNCONDITIONED create (no If-Match / If-None-Match — S118 demand map).
  // 201 → the full entity (the S118 backend closed the create fork:
  // INSERT…RETURNING always returns the row).
  const createConfig = async (
    body: AgreementConfigRequest,
  ): Promise<WithEtag<AgreementConfig>> => {
    const result = await apiFetchWithEtag('/api/agreement-configs', {
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
    body: AgreementConfigRequest,
  ): Promise<WithEtag<AgreementConfig>> => {
    const result = await apiFetchWithEtag('/api/agreement-configs/{configId}', {
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

  // UNCONDITIONED clone-create (no body, optional query overrides) — 201 → the
  // full cloned entity (the create fork closed alongside the plain create).
  const cloneConfig = async (
    configId: string,
    agreementCode?: string,
    okVersion?: string,
  ): Promise<WithEtag<AgreementConfig>> => {
    const result = await apiFetchWithEtag('/api/agreement-configs/{configId}/clone', {
      method: 'POST',
      params: { path: { configId } },
      query: { agreementCode, okVersion },
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  // Publish/Archive endpoints return a small spec envelope (NOT the full
  // AgreementConfig): publish → { configId, status, archivedConfigId,
  // publishedAt }; archive → { configId, status, archivedAt }. We surface the
  // response data plus the new version (parsed from ETag) so the caller can
  // compose the next If-Match if needed (rarely — the row's status transition
  // usually triggers a full refetch).
  const publishConfig = async (
    configId: string,
    ifMatch: string,
  ): Promise<PublishResponse & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag('/api/agreement-configs/{configId}/publish', {
      method: 'POST',
      params: { path: { configId } },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data, etag } = result.data
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...data, etag: resolvedEtag, version }
  }

  const archiveConfig = async (
    configId: string,
    ifMatch: string,
  ): Promise<ArchiveResponse & { etag: string | null; version: number | null }> => {
    const result = await apiFetchWithEtag('/api/agreement-configs/{configId}/archive', {
      method: 'POST',
      params: { path: { configId } },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    const { data, etag } = result.data
    const { etag: resolvedEtag, version } = resolveEtag(etag, null)
    return { ...data, etag: resolvedEtag, version }
  }

  return { createConfig, updateConfig, cloneConfig, publishConfig, archiveConfig }
}
