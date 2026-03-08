import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface PositionOverrideConfig {
  overrideId: string
  agreementCode: string
  okVersion: string
  positionCode: string
  status: string
  maxFlexBalance: number | null
  flexCarryoverMax: number | null
  normPeriodWeeks: number | null
  weeklyNormHours: number | null
  createdBy: string
  createdAt: string
  updatedAt: string
  description: string | null
}

export function usePositionOverrides() {
  const [data, setData] = useState<PositionOverrideConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<PositionOverrideConfig[]>('/api/admin/position-overrides')
    if (result.ok) {
      setData(result.data)
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  const create = async (body: Partial<PositionOverrideConfig>) => {
    const result = await apiClient.post<{ overrideId: string }>('/api/admin/position-overrides', body)
    if (result.ok) await fetchAll()
    return result
  }

  const update = async (overrideId: string, body: Partial<PositionOverrideConfig>) => {
    const result = await apiClient.put<void>(`/api/admin/position-overrides/${overrideId}`, body)
    if (result.ok) await fetchAll()
    return result
  }

  const deactivate = async (overrideId: string) => {
    const result = await apiClient.post<void>(`/api/admin/position-overrides/${overrideId}/deactivate`)
    if (result.ok) await fetchAll()
    return result
  }

  const activate = async (overrideId: string) => {
    const result = await apiClient.post<void>(`/api/admin/position-overrides/${overrideId}/activate`)
    if (result.ok) await fetchAll()
    return result
  }

  return { data, loading, error, refetch: fetchAll, create, update, deactivate, activate }
}
