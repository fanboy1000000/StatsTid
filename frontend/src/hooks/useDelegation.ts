import { useCallback } from 'react'
import { apiClient, type ApiResult } from '../lib/api'

// S51 TASK-5106. Delegation hooks following the useReportingLines.ts pattern.
// Reads/writes go through apiClient (no ETag contract on delegation endpoints).

export interface DelegationStatus {
  active: boolean
  actingManagerId: string | null
  effectiveFrom: string | null
  effectiveTo: string | null
  delegatedEmployees: { employeeId: string; displayName: string }[]
}

export interface DelegationResult {
  delegatedCount: number
  skippedCount: number
  actingManagerId: string
  effectiveFrom: string
  effectiveTo: string
}

export function useDelegation() {
  const fetchStatus = useCallback(async (): Promise<ApiResult<DelegationStatus>> => {
    return apiClient.get<DelegationStatus>('/api/reporting-lines/delegate')
  }, [])

  const createDelegation = useCallback(async (
    body: { actingManagerId: string; effectiveTo: string },
  ): Promise<ApiResult<DelegationResult>> => {
    return apiClient.post<DelegationResult>('/api/reporting-lines/delegate', body)
  }, [])

  const cancelDelegation = useCallback(async (): Promise<ApiResult<void>> => {
    return apiClient.delete<void>('/api/reporting-lines/delegate')
  }, [])

  return { fetchStatus, createDelegation, cancelDelegation }
}
