import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

// S116 / TASK-11602 — the pending/by-month reads switched to the TYPED spec-keyed
// forms (PAT-012 Pass 3). The element type is the GENERATED spec record (the
// shared 9-field `ApprovalPeriodListItem` both endpoints serve) — the previous
// hand-written `types.ts` ApprovalPeriod claimed 4 PHANTOM members
// (employeeApprovedAt/By, employeeDeadline, managerDeadline) the backend never
// serves on these routes (the S116 L2 consolidation; both hand-written variants
// deleted).
export type ApprovalPeriod =
  components['schemas']['StatsTid.Backend.Api.Contracts.ApprovalPeriodListItem']

export function usePendingApprovals() {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchPending = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/approval/pending')
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
    const result = await apiClient.get('/api/approval/pending', {
      query: { 'my-reports': true },
    })
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
    const result = await apiClient.get('/api/approval/by-month', {
      query: { year, month },
    })
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
    const result = await apiClient.get('/api/approval/by-month', {
      query: { year, month, 'my-reports': true },
    })
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
