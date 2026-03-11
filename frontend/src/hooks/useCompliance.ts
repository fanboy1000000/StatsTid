import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface ComplianceViolation {
  violationType: 'DAILY_REST' | 'WEEKLY_REST' | 'MAX_DAILY_HOURS' | 'WEEKLY_MAX_HOURS'
  date: string
  actualValue: number
  thresholdValue: number
  severity: 'WARNING' | 'VIOLATION'
  isVoluntaryExempt: boolean
  message: string
}

export interface ComplianceCheckResult {
  ruleId: string
  employeeId: string
  success: boolean
  violations: ComplianceViolation[]
  warnings: ComplianceViolation[]
}

export interface CompensatoryRestEntry {
  id: string
  employeeId: string
  sourceDate: string
  compensatoryDate: string | null
  hours: number
  status: 'PENDING' | 'GRANTED' | 'EXPIRED'
  createdAt: string
}

interface UseComplianceResult {
  result: ComplianceCheckResult | null
  loading: boolean
  error: string | null
  refetch: () => void
}

export function useCompliance(employeeId: string, year: number, month: number): UseComplianceResult {
  const [result, setResult] = useState<ComplianceCheckResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchData = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const res = await apiClient.get<ComplianceCheckResult>(
      `/api/compliance/${employeeId}/period?year=${year}&month=${month}`
    )
    if (res.ok) {
      setResult(res.data)
    } else {
      setError(res.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  return { result, loading, error, refetch: fetchData }
}

export function useCompensatoryRest(employeeId: string) {
  const [entries, setEntries] = useState<CompensatoryRestEntry[]>([])
  const [loading, setLoading] = useState(false)

  const fetchData = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    const res = await apiClient.get<CompensatoryRestEntry[]>(
      `/api/compliance/${employeeId}/compensatory-rest`
    )
    if (res.ok) {
      setEntries(res.data)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  return { entries, loading, refetch: fetchData }
}
