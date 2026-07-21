import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S118 / TASK-11801 (Typed API Contract retrofit Pass 5, PAT-012) — the list
// GET, the unconditioned create POST and the If-Match DELETE ride the TYPED
// structured forms; the hand-written `WageTypeMappingItem` interface (audited
// FAITHFUL) was DELETED in favor of the GENERATED spec type.
//
// ── THE DEFERRED PUT (a NAMED DEFERRED DEFECT, the S118 W2-ruling class) ─────
// The spec `UpdateWageTypeMappingRequest` REQUIRES `effectiveFrom` (C#
// `required DateOnly` — binder-enforced: absence → 400 before the handler
// runs; WageTypeMappingEndpoints.cs:743). The FE update body has NEVER sent
// it, so every wage-type-mapping edit from the admin page is a LIVE 400
// DEAD-END today. Wiring the field in would be an FE request-payload change
// on a RULE-BEARING date (the S29 same-day-only-edit validator) — barred this
// pass ("zero request-payload changes", the W2 exclusion class). The PUT
// therefore CANNOT compile against the spec-derived typed body and stays on
// the legacy explicit-T form, pinned by the route helper below (the S115
// ELIGIBILITY_PATH / S116 SKEMA_*_PATH precedent — the helper is the lint
// sanction boundary). A future deliberate fix adds `effectiveFrom` AND
// graduates the call to the typed form in the same change.

/** The GENERATED spec row (S118) — replaces the hand-written interface. */
export type WageTypeMappingItem =
  components['schemas']['StatsTid.Backend.Api.Contracts.WageTypeMappingResponse']

export type WageTypeMappingCreateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.WageTypeMappingEndpoints.CreateWageTypeMappingRequest']

/** The CURRENT (defective — see the header note) update payload: the spec
    update request MINUS the binder-required `effectiveFrom` the FE does not
    send yet. Spec-derived so the deferred defect is visible in one place. */
export type WageTypeMappingUpdateBody = Omit<
  components['schemas']['StatsTid.Backend.Api.Endpoints.WageTypeMappingEndpoints.UpdateWageTypeMappingRequest'],
  'effectiveFrom'
>

/** The route-helper PIN for the ONE sanctioned legacy explicit-T call (the
    deferred PUT). Every other explicit-T call in this file stays lint-banned. */
const WAGE_TYPE_MAPPING_UPDATE_PATH = () => '/api/admin/wage-type-mappings'

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

  // DEFERRED — the legacy explicit-T If-Match PUT (see the header note): the
  // payload is byte-identical to the pre-S118 call and deliberately NOT the
  // typed form, because the spec body REQUIRES `effectiveFrom` and adding it
  // is a barred request-payload change this pass.
  const updateMapping = async (
    ifMatch: string,
    body: WageTypeMappingUpdateBody,
  ): Promise<WithEtag<WageTypeMappingItem>> => {
    const result = await apiFetchWithEtag<WageTypeMappingItem>(
      WAGE_TYPE_MAPPING_UPDATE_PATH(),
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
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
