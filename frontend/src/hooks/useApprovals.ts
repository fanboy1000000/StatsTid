import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { ApprovalPeriod } from '../types'

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
