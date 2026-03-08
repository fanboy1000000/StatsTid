import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface AgreementConfig {
  configId: string
  agreementCode: string
  okVersion: string
  status: 'DRAFT' | 'ACTIVE' | 'ARCHIVED'
  weeklyNormHours: number
  normPeriodWeeks: number
  normModel: string
  annualNormHours: number
  maxFlexBalance: number
  flexCarryoverMax: number
  hasOvertime: boolean
  hasMerarbejde: boolean
  overtimeThreshold50: number
  overtimeThreshold100: number
  eveningSupplementEnabled: boolean
  nightSupplementEnabled: boolean
  weekendSupplementEnabled: boolean
  holidaySupplementEnabled: boolean
  eveningStart: number
  eveningEnd: number
  nightStart: number
  nightEnd: number
  eveningRate: number
  nightRate: number
  weekendSaturdayRate: number
  weekendSundayRate: number
  holidayRate: number
  onCallDutyEnabled: boolean
  onCallDutyRate: number
  callInWorkEnabled: boolean
  callInMinimumHours: number
  callInRate: number
  travelTimeEnabled: boolean
  workingTravelRate: number
  nonWorkingTravelRate: number
  createdBy: string
  createdAt: string
  updatedAt: string
  publishedAt: string | null
  archivedAt: string | null
  clonedFromId: string | null
  description: string | null
}

export function useAgreementConfigs(statusFilter?: string) {
  const [configs, setConfigs] = useState<AgreementConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfigs = useCallback(async () => {
    setLoading(true)
    setError(null)
    const path = statusFilter
      ? `/api/agreement-configs?status=${statusFilter}`
      : '/api/agreement-configs'
    const result = await apiClient.get<AgreementConfig[]>(path)
    if (result.ok) {
      setConfigs(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [statusFilter])

  useEffect(() => { fetchConfigs() }, [fetchConfigs])

  return { configs, loading, error, refetch: fetchConfigs }
}

export function useAgreementConfig(configId: string) {
  const [config, setConfig] = useState<AgreementConfig | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchConfig = useCallback(async () => {
    if (!configId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<AgreementConfig>(`/api/agreement-configs/${configId}`)
    if (result.ok) {
      setConfig(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [configId])

  useEffect(() => { fetchConfig() }, [fetchConfig])

  return { config, loading, error, refetch: fetchConfig }
}

export function useAgreementConfigActions() {
  const createConfig = async (body: Partial<AgreementConfig>) => {
    const result = await apiClient.post<AgreementConfig>('/api/agreement-configs', body)
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const updateConfig = async (configId: string, body: Partial<AgreementConfig>) => {
    const result = await apiClient.put<AgreementConfig>(`/api/agreement-configs/${configId}`, body)
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const cloneConfig = async (configId: string, agreementCode?: string, okVersion?: string) => {
    const params = new URLSearchParams()
    if (agreementCode) params.set('agreementCode', agreementCode)
    if (okVersion) params.set('okVersion', okVersion)
    const query = params.toString() ? `?${params.toString()}` : ''
    const result = await apiClient.post<AgreementConfig>(`/api/agreement-configs/${configId}/clone${query}`)
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const publishConfig = async (configId: string) => {
    const result = await apiClient.post<AgreementConfig>(`/api/agreement-configs/${configId}/publish`)
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const archiveConfig = async (configId: string) => {
    const result = await apiClient.post<AgreementConfig>(`/api/agreement-configs/${configId}/archive`)
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  return { createConfig, updateConfig, cloneConfig, publishConfig, archiveConfig }
}
