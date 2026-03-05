import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData } from '../types'

interface UseSkemaResult {
  data: SkemaMonthData | null
  loading: boolean
  error: string | null
  refetch: () => void
  saveMonth: (cells: { rowKey: string; date: string; hours: number | null }[]) => Promise<void>
  employeeApprove: (periodId: string) => Promise<void>
}

export function useSkema(employeeId: string, year: number, month: number): UseSkemaResult {
  const [data, setData] = useState<SkemaMonthData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchData = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<SkemaMonthData>(
      `/api/skema/${employeeId}/month?year=${year}&month=${month}`
    )
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  const saveMonth = useCallback(
    async (cells: { rowKey: string; date: string; hours: number | null }[]) => {
      const result = await apiClient.post<void>(`/api/skema/${employeeId}/save`, {
        year,
        month,
        cells,
      })
      if (!result.ok) {
        setError(result.error)
      }
    },
    [employeeId, year, month]
  )

  const employeeApprove = useCallback(
    async (periodId: string) => {
      const result = await apiClient.post<void>(`/api/approval/${periodId}/employee-approve`, {})
      if (result.ok) {
        await fetchData()
      } else {
        setError(result.error)
      }
    },
    [fetchData]
  )

  return { data, loading, error, refetch: fetchData, saveMonth, employeeApprove }
}
