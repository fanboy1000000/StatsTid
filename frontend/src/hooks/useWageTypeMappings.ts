import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S118 / TASK-11801 (Typed API Contract retrofit Pass 5, PAT-012) — the list
// GET, the unconditioned create POST and the If-Match DELETE ride the TYPED
// structured forms; the hand-written `WageTypeMappingItem` interface (audited
// FAITHFUL) was DELETED in favor of the GENERATED spec type.
//
// S121 / TASK-12101 — the S118 NAMED DEFERRED DEFECT is FIXED: the spec
// `UpdateWageTypeMappingRequest` no longer requires `effectiveFrom` (S121
// ruling #1 — the server defaults it to today, `WageTypeMappingEndpoints.cs`
// compute-once), so the FE payload's long-standing omission is now LEGAL and
// the update PUT graduated from the legacy explicit-T form to the TYPED
// structured form with the SAME bytes. The `WAGE_TYPE_MAPPING_UPDATE_PATH`
// route-helper pin and its eslint carve-out are gone — this file is on the
// FULL lint tier.

/** The GENERATED spec row (S118) — replaces the hand-written interface. */
export type WageTypeMappingItem =
  components['schemas']['StatsTid.Backend.Api.Contracts.WageTypeMappingResponse']

export type WageTypeMappingCreateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.WageTypeMappingEndpoints.CreateWageTypeMappingRequest']

/** The GENERATED spec update request (S121 — replaces the defective
    `Omit<…, 'effectiveFrom'>` payload type; `effectiveFrom` is optional on
    the wire and the FE deliberately omits it — server-defaulted today). */
export type WageTypeMappingUpdateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.WageTypeMappingEndpoints.UpdateWageTypeMappingRequest']

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

/** S118 — undeclared error payloads narrow via a runtime type guard, never a
    cast (PAT-012 no-`as` surface; all members optional → structurally sound). */
function isMutationErrorBody(
  body: unknown,
): body is NonNullable<WageTypeMappingMutationError['body']> {
  return typeof body === 'object' && body !== null
}

function decorateRow(row: WageTypeMappingItem): WithEtag<WageTypeMappingItem> {
  return { ...row, etag: formatVersionAsIfMatch(row.version) }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: unknown,
): WageTypeMappingMutationError {
  // Object.assign (not an `as` cast) — this file is on the no-`as` surface.
  return Object.assign(new Error(errorMsg), {
    status,
    body: isMutationErrorBody(body) ? body : undefined,
  })
}

export function useWageTypeMappings() {
  const [data, setData] = useState<WithEtag<WageTypeMappingItem>[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get('/api/admin/wage-type-mappings')
    if (result.ok) {
      setData(result.data.map(decorateRow))
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  // UNCONDITIONED create (no precondition — S118 demand map). 201 → the row
  // envelope (version = 1; ETag: "1" stamped).
  const create = async (
    body: WageTypeMappingCreateRequest,
  ): Promise<WithEtag<WageTypeMappingItem>> => {
    const result = await apiFetchWithEtag('/api/admin/wage-type-mappings', {
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

  // S121 — the GRADUATED typed If-Match PUT (see the header note): the payload
  // BYTES are unchanged (still no `effectiveFrom` — now a deliberate
  // server-default omission per ruling #1, not a dead-end).
  const updateMapping = async (
    ifMatch: string,
    body: WageTypeMappingUpdateRequest,
  ): Promise<WithEtag<WageTypeMappingItem>> => {
    const result = await apiFetchWithEtag('/api/admin/wage-type-mappings', {
      method: 'PUT',
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

  // If-Match DELETE → declared 204 (typed data = undefined; NO ETag stamped —
  // resource gone). The natural key travels as the typed query.
  const deleteMapping = async (
    timeType: string,
    okVersion: string,
    agreementCode: string,
    position: string,
    ifMatch: string,
  ): Promise<void> => {
    const result = await apiFetchWithEtag('/api/admin/wage-type-mappings', {
      method: 'DELETE',
      query: { timeType, okVersion, agreementCode, position },
      ifMatch,
    })
    if (!result.ok) {
      throw makeMutationError(result.status, result.error, result.body)
    }
    await fetchAll()
  }

  return { data, loading, error, refetch: fetchAll, create, updateMapping, deleteMapping }
}
