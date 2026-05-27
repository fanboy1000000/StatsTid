import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData } from '../types'

export interface QuotaError {
  absenceType: string
  remaining: number
  requested: number
}

export interface ApprovalValidationError {
  missingDays: string[]
  coveredDays: number
  totalWorkdays: number
}

interface UseSkemaResult {
  data: SkemaMonthData | null
  loading: boolean
  error: string | null
  quotaError: QuotaError | null
  approvalValidationError: ApprovalValidationError | null
  clearQuotaError: () => void
  clearApprovalValidationError: () => void
  refetch: () => void
  saveMonth: (cells: { rowKey: string; date: string; hours: number | null }[]) => Promise<boolean>
  employeeApprove: (periodId: string) => Promise<void>
  submitAndApprove: (orgId: string, agreementCode: string) => Promise<void>
  reopenPeriod: (periodId: string, reason?: string) => Promise<void>
}

export function useSkema(employeeId: string, year: number, month: number): UseSkemaResult {
  const [data, setData] = useState<SkemaMonthData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [quotaError, setQuotaError] = useState<QuotaError | null>(null)
  const [approvalValidationError, setApprovalValidationError] = useState<ApprovalValidationError | null>(null)

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
  const clearApprovalValidationError = useCallback(() => setApprovalValidationError(null), [])

  const absenceTypesRef = useRef<Set<string>>(new Set())
  useEffect(() => {
    absenceTypesRef.current = new Set(data?.absenceTypes?.map(a => a.type) ?? [])
  }, [data])

  const saveMonth = useCallback(
    async (cells: { rowKey: string; date: string; hours: number | null }[]): Promise<boolean> => {
      setQuotaError(null)
      const absenceTypeSet = absenceTypesRef.current

      const entries = cells
        .filter(c => !absenceTypeSet.has(c.rowKey) && c.hours != null && c.hours !== 0)
        .map(c => ({ date: c.date, projectCode: c.rowKey, hours: c.hours }))

      const absences = cells
        .filter(c => absenceTypeSet.has(c.rowKey) && c.hours != null && c.hours !== 0)
        .map(c => ({ date: c.date, absenceType: c.rowKey, hours: c.hours }))

      const result = await apiClient.post<void>(`/api/skema/${employeeId}/save`, {
        year,
        month,
        entries: entries.length > 0 ? entries : null,
        absences: absences.length > 0 ? absences : null,
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
              return false
            }
          } catch {
            // Not valid JSON, fall through to generic error
          }
        }
        setError(result.error)
        return false
      }
      return true
    },
    [employeeId, year, month]
  )

  const employeeApprove = useCallback(
    async (periodId: string) => {
      const result = await apiClient.post<void>(`/api/approval/${periodId}/employee-approve`, {})
      if (result.ok) {
        setApprovalValidationError(null)
        await fetchData()
      } else {
        if (result.status === 422) {
          try {
            const body = JSON.parse(result.error)
            if (body.missingDays && Array.isArray(body.missingDays)) {
              setApprovalValidationError({
                missingDays: body.missingDays,
                coveredDays: body.coveredDays ?? 0,
                totalWorkdays: body.totalWorkdays ?? 0,
              })
              await fetchData()
              return
            }
          } catch {
            // Not valid JSON, fall through to generic error
          }
        }
        setError(result.error)
        await fetchData()
      }
    },
    [fetchData]
  )

  const submitAndApprove = useCallback(
    async (orgId: string, agreementCode: string) => {
      setApprovalValidationError(null)
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
        if (approveResult.status === 422) {
          try {
            const body = JSON.parse(approveResult.error)
            if (body.missingDays && Array.isArray(body.missingDays)) {
              setApprovalValidationError({
                missingDays: body.missingDays,
                coveredDays: body.coveredDays ?? 0,
                totalWorkdays: body.totalWorkdays ?? 0,
              })
              // Still refetch so the grid shows current state (period was created but not approved)
              await fetchData()
              return
            }
          } catch {
            // Not valid JSON, fall through to generic error
          }
        }
        setError(approveResult.error)
        // Refetch even on error — the period may have been created (SUBMITTED) before approve failed
        await fetchData()
      }
    },
    [employeeId, year, month, fetchData]
  )

  const reopenPeriod = useCallback(
    async (periodId: string, reason?: string) => {
      const result = await apiClient.post<void>(`/api/approval/${periodId}/reopen`, { reason: reason ?? 'Genåbnet af medarbejder' })
      if (result.ok) {
        await fetchData()
      } else {
        setError(result.error)
      }
    },
    [fetchData]
  )

  return { data, loading, error, quotaError, approvalValidationError, clearQuotaError, clearApprovalValidationError, refetch: fetchData, saveMonth, employeeApprove, submitAndApprove, reopenPeriod }
}
