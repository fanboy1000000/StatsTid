import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface AccrualSeriesPoint {
  monthEnd: string
  earned: number
  isSelected: boolean
}

export interface AccrualSeriesEntitlement {
  type: string
  label: string
  annualQuota: number
  entitlementYear: number
  ferieaarStart: string
  points: AccrualSeriesPoint[]
}

export interface AccrualSeries {
  employeeId: string
  year: number
  month: number
  series: AccrualSeriesEntitlement[]
}

export function useAccrualSeries(employeeId: string, year: number, month: number) {
  const [data, setData] = useState<AccrualSeries | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchSeries = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<AccrualSeries>(
      `/api/balance/${employeeId}/series?year=${year}&month=${month}`
    )
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchSeries()
  }, [fetchSeries])

  return { data, loading, error, refetch: fetchSeries }
}
