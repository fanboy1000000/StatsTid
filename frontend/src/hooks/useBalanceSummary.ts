import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface EntitlementInfo {
  type: string
  label: string
  totalQuota: number
  used: number
  planned: number
  carryoverIn: number
  remaining: number
  entitlementYear: number
}

export interface BalanceSummary {
  flexBalance: number
  flexDelta: number
  vacationDaysUsed: number
  vacationDaysEntitlement: number
  normHoursExpected: number
  normHoursActual: number
  overtimeHours: number
  agreementCode: string
  hasMerarbejde: boolean
  entitlements?: EntitlementInfo[]
}

export function useBalanceSummary(employeeId: string, year: number, month: number) {
  const [data, setData] = useState<BalanceSummary | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchBalance = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<BalanceSummary>(
      `/api/balance/${employeeId}/summary?year=${year}&month=${month}`
    )
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchBalance()
  }, [fetchBalance])

  return { data, loading, error, refetch: fetchBalance }
}
