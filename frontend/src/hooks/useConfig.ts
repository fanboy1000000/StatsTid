import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { LocalConfiguration, ConfigConstraint } from '../types'

export function useEffectiveConfig(orgId: string) {
  const [config, setConfig] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfig = useCallback(async () => {
    if (!orgId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<Record<string, string>>(`/api/config/${orgId}`)
    if (result.ok) {
      setConfig(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [orgId])

  useEffect(() => { fetchConfig() }, [fetchConfig])

  return { config, loading, error, fetchConfig }
}

export function useLocalConfig(orgId: string) {
  const [configs, setConfigs] = useState<LocalConfiguration[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfigs = useCallback(async () => {
    if (!orgId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<LocalConfiguration[]>(`/api/config/${orgId}/local`)
    if (result.ok) {
      setConfigs(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [orgId])

  useEffect(() => { fetchConfigs() }, [fetchConfigs])

  const createOverride = async (body: { configArea: string; configKey: string; configValue: string; effectiveFrom: string; effectiveTo?: string; agreementCode: string; okVersion: string }) => {
    const result = await apiClient.post<LocalConfiguration>(`/api/config/${orgId}`, body)
    if (!result.ok) throw new Error(result.error)
    await fetchConfigs()
    return result.data
  }

  const deactivateOverride = async (configId: string) => {
    const result = await apiClient.delete<void>(`/api/config/${orgId}/${configId}`)
    if (!result.ok) throw new Error(result.error)
    await fetchConfigs()
  }

  return { configs, loading, error, fetchConfigs, createOverride, deactivateOverride }
}

export function useConfigConstraints() {
  const [constraints, setConstraints] = useState<ConfigConstraint[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConstraints = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<ConfigConstraint[]>('/api/config/constraints')
    if (result.ok) {
      setConstraints(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchConstraints() }, [fetchConstraints])

  return { constraints, loading, error, fetchConstraints }
}
