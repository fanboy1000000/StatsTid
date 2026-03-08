import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface WageTypeMappingItem {
  timeType: string
  wageType: string
  okVersion: string
  agreementCode: string
  position: string
  description: string | null
}

export function useWageTypeMappings() {
  const [data, setData] = useState<WageTypeMappingItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAll = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<WageTypeMappingItem[]>('/api/admin/wage-type-mappings')
    if (result.ok) {
      setData(result.data)
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchAll() }, [fetchAll])

  const create = async (body: WageTypeMappingItem) => {
    const result = await apiClient.post<void>('/api/admin/wage-type-mappings', body)
    if (result.ok) await fetchAll()
    return result
  }

  const updateMapping = async (body: WageTypeMappingItem) => {
    const result = await apiClient.put<void>('/api/admin/wage-type-mappings', body)
    if (result.ok) await fetchAll()
    return result
  }

  const deleteMapping = async (timeType: string, okVersion: string, agreementCode: string, position: string) => {
    const params = new URLSearchParams({ timeType, okVersion, agreementCode, position })
    const result = await apiClient.delete<void>(`/api/admin/wage-type-mappings?${params}`)
    if (result.ok) await fetchAll()
    return result
  }

  return { data, loading, error, refetch: fetchAll, create, updateMapping, deleteMapping }
}
