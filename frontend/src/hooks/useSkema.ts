import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData, WorkTimeDay } from '../types'

export interface QuotaError {
  absenceType: string
  remaining: number
  requested: number
}

/** 422 from employee-approve / submit-and-approve — discriminated union. */
export interface CoverageValidationError {
  kind: 'coverage'
  missingDays: string[]
  coveredDays: number
  totalWorkdays: number
}

export interface AllocationUnbalancedDay {
  date: string
  worked: number
  allocated: number
  direction: 'under' | 'over'
}

export interface AllocationValidationError {
  kind: 'allocation'
  unbalancedDays: AllocationUnbalancedDay[]
}

export type ApprovalValidationError = CoverageValidationError | AllocationValidationError

/**
 * Derive OK version from a period-start month. The OK24 -> OK26 switch is
 * 2026-04-01: Jan-Mar 2026 (and earlier) -> OK24, Apr 2026 onwards -> OK26.
 */
export function deriveOkVersion(year: number, month: number): 'OK24' | 'OK26' {
  if (year > 2026) return 'OK26'
  if (year < 2026) return 'OK24'
  return month >= 4 ? 'OK26' : 'OK24'
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
  saveMonth: (
    cells: { rowKey: string; date: string; hours: number | null }[],
    workTime?: WorkTimeDay[],
  ) => Promise<boolean>
  employeeApprove: (periodId: string) => Promise<void>
  submitAndApprove: (orgId: string, agreementCode: string) => Promise<void>
  reopenPeriod: (periodId: string, reason?: string) => Promise<void>
}

/** Parse a 422 approval-gate body into a discriminated ApprovalValidationError, or null. */
function parseApprovalValidationError(raw: string): ApprovalValidationError | null {
  try {
    const body = JSON.parse(raw)
    if (body.kind === 'allocation' && Array.isArray(body.unbalancedDays)) {
      return { kind: 'allocation', unbalancedDays: body.unbalancedDays }
    }
    if (Array.isArray(body.missingDays)) {
      return {
        kind: 'coverage',
        missingDays: body.missingDays,
        coveredDays: body.coveredDays ?? 0,
        totalWorkdays: body.totalWorkdays ?? 0,
      }
    }
  } catch {
    // Not valid JSON
  }
  return null
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
    async (
      cells: { rowKey: string; date: string; hours: number | null }[],
      workTime?: WorkTimeDay[],
    ): Promise<boolean> => {
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
        workTime: workTime && workTime.length > 0 ? workTime : null,
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
          const validationError = parseApprovalValidationError(result.error)
          if (validationError) {
            setApprovalValidationError(validationError)
            await fetchData()
            return
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
      const okVersion = deriveOkVersion(year, month)

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
          const validationError = parseApprovalValidationError(approveResult.error)
          if (validationError) {
            setApprovalValidationError(validationError)
            // Still refetch so the grid shows current state (period was created but not approved)
            await fetchData()
            return
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
