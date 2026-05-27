import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { ApprovalPeriod } from '../types'

export function useApprovals(employeeId: string) {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchPeriods = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ApprovalPeriod[]>(`/api/approval/${employeeId}`)
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchPeriods() }, [fetchPeriods])

  const submitPeriod = async (body: { employeeId: string; periodStart: string; periodEnd: string; periodType: string }) => {
    const result = await apiClient.post<ApprovalPeriod>('/api/approval/submit', body)
    if (!result.ok) throw new Error(result.error)
    await fetchPeriods()
    return result.data
  }

  const approvePeriod = async (periodId: string, confirmFallback?: boolean) => {
    const url = confirmFallback
      ? `/api/approval/${periodId}/approve?confirmFallback=true`
      : `/api/approval/${periodId}/approve`
    const result = await apiClient.post<ApprovalPeriod>(url)
    if (!result.ok) throw new Error(result.error)
    await fetchPeriods()
    return result.data
  }

  const rejectPeriod = async (periodId: string, reason: string, confirmFallback?: boolean) => {
    const url = confirmFallback
      ? `/api/approval/${periodId}/reject?confirmFallback=true`
      : `/api/approval/${periodId}/reject`
    const result = await apiClient.post<ApprovalPeriod>(url, { reason })
    if (!result.ok) throw new Error(result.error)
    await fetchPeriods()
    return result.data
  }

  return { periods, loading, error, fetchPeriods, submitPeriod, approvePeriod, rejectPeriod }
}

export function usePendingApprovals() {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchPending = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ApprovalPeriod[]>('/api/approval/pending')
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchPending() }, [fetchPending])

  return { periods, loading, error, fetchPending }
}

export function usePendingMyReports() {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchPendingMyReports = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ApprovalPeriod[]>('/api/approval/pending?my-reports=true')
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchPendingMyReports() }, [fetchPendingMyReports])

  return { periods, loading, error, fetchPendingMyReports }
}

export function useApprovalsByMonth(year: number, month: number) {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchByMonth = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ApprovalPeriod[]>(
      `/api/approval/by-month?year=${year}&month=${month}`
    )
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [year, month])

  useEffect(() => { fetchByMonth() }, [fetchByMonth])

  return { periods, loading, error, refetch: fetchByMonth }
}

export function useMyReportsByMonth(year: number, month: number) {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchByMonth = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ApprovalPeriod[]>(
      `/api/approval/by-month?year=${year}&month=${month}&my-reports=true`
    )
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [year, month])

  useEffect(() => { fetchByMonth() }, [fetchByMonth])

  return { periods, loading, error, refetch: fetchByMonth }
}
