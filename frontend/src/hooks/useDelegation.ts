import { useCallback } from 'react'
import { apiClient, type ApiResult } from '../lib/api'
import type { components } from '../lib/api-types'

// S51 TASK-5106. Delegation hooks following the useReportingLines.ts pattern.
// Reads/writes go through apiClient (no ETag contract on delegation endpoints).
//
// S116 / TASK-11602 — all three call sites switched to the TYPED spec-keyed
// forms (PAT-012 Pass 3); the hand-written `DelegationStatus`/`DelegationResult`
// interfaces were DELETED in favor of the GENERATED spec records below. Honest
// deltas the spec surfaced:
//  - `delegatedEmployees[].displayName` is `string | null` on the wire (the
//    hand-written interface claimed non-null);
//  - the DELETE returns a genuine 200 `{revokedCount}` body (the S115
//    DELETE-vikar precedent) — previously declared `<void>` and discarded; the
//    typed form derives it, and consumers may keep discarding it.

export type DelegationStatus =
  components['schemas']['StatsTid.Backend.Api.Contracts.DelegationStatusResponse']

export type DelegationResult =
  components['schemas']['StatsTid.Backend.Api.Contracts.DelegationCreateResponse']

export type DelegationRevokeResult =
  components['schemas']['StatsTid.Backend.Api.Contracts.DelegationRevokeResponse']

export function useDelegation() {
  const fetchStatus = useCallback(async (): Promise<ApiResult<DelegationStatus>> => {
    return apiClient.get('/api/reporting-lines/delegate')
  }, [])

  const createDelegation = useCallback(async (
    body: { actingManagerId: string; effectiveTo: string },
  ): Promise<ApiResult<DelegationResult>> => {
    return apiClient.post('/api/reporting-lines/delegate', { body })
  }, [])

  const cancelDelegation = useCallback(async (): Promise<ApiResult<DelegationRevokeResult>> => {
    return apiClient.delete('/api/reporting-lines/delegate')
  }, [])

  return { fetchStatus, createDelegation, cancelDelegation }
}
