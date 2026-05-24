import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData } from '../types'

export interface QuotaError {
  absenceType: string
  remaining: number
  requested: number
}

interface UseSkemaResult {
  data: SkemaMonthData | null
  loading: boolean
  error: string | null
  quotaError: QuotaError | null
  clearQuotaError: () => void
  refetch: () => void
  saveMonth: (cells: { rowKey: string; date: string; hours: number | null }[]) => Promise<void>
  employeeApprove: (periodId: string) => Promise<void>
  submitAndApprove: (orgId: string, agreementCode: string) => Promise<void>
}

export function useSkema(employeeId: string, year: number, month: number): UseSkemaResult {
  const [data, setData] = useState<SkemaMonthData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [quotaError, setQuotaError] = useState<QuotaError | null>(null)

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

  const clearQuotaError = useCallback(() => setQuotaError(null), [])

  const saveMonth = useCallback(
    async (cells: { rowKey: string; date: string; hours: number | null }[]) => {
      setQuotaError(null)
      const result = await apiClient.post<void>(`/api/skema/${employeeId}/save`, {
        year,
        month,
        cells,
      })
      if (!result.ok) {
        if (result.status === 422) {
          try {
            const body = JSON.parse(result.error)
            if (body.absenceType && body.remaining !== undefined && body.requested !== undefined) {
              setQuotaError({
                absenceType: body.absenceType,
                remaining: body.remaining,
                requested: body.requested,
              })
              return
            }
          } catch {
            // Not valid JSON, fall through to generic error
          }
        }
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

  const submitAndApprove = useCallback(
    async (orgId: string, agreementCode: string) => {
      const periodStart = `${year}-${String(month).padStart(2, '0')}-01`
      const lastDay = new Date(year, month, 0).getDate()
      const periodEnd = `${year}-${String(month).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`
      const okVersion = year >= 2026 ? 'OK26' : 'OK24'

      const submitResult = await apiClient.post<{ periodId: string }>('/api/approval/submit', {
        employeeId,
        orgId,
        periodStart,
        periodEnd,
        periodType: 'MONTHLY',
        agreementCode,
        okVersion,
      })
      if (!submitResult.ok) {
        setError(submitResult.error)
        return
      }

      const approveResult = await apiClient.post<void>(
        `/api/approval/${submitResult.data.periodId}/employee-approve`, {}
      )
      if (approveResult.ok) {
        await fetchData()
      } else {
        setError(approveResult.error)
      }
    },
    [employeeId, year, month, fetchData]
  )

  return { data, loading, error, quotaError, clearQuotaError, refetch: fetchData, saveMonth, employeeApprove, submitAndApprove }
}
