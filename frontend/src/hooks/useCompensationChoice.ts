import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

export interface CompensationChoice {
  compensationModel: string
  source: string
}

interface UseCompensationChoiceResult {
  choice: CompensationChoice | null
  loading: boolean
  error: string | null
  updateChoice: (periodYear: number, compensationModel: string) => Promise<boolean>
}

export function useCompensationChoice(employeeId: string, periodYear: number): UseCompensationChoiceResult {
  const [choice, setChoice] = useState<CompensationChoice | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchChoice = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<CompensationChoice>(
      `/api/overtime/${employeeId}/compensation-choice?periodYear=${periodYear}`
    )
    if (result.ok) {
      setChoice(result.data)
    } else {
      // 404 means employee's agreement doesn't allow choice - not an error
      if (result.status === 404) {
        setChoice(null)
      } else {
        setError(result.error)
      }
    }
    setLoading(false)
  }, [employeeId, periodYear])

  useEffect(() => {
    fetchChoice()
  }, [fetchChoice])

  const updateChoice = useCallback(async (year: number, compensationModel: string): Promise<boolean> => {
    const result = await apiClient.put<void>(
      `/api/overtime/${employeeId}/compensation-choice`,
      { periodYear: year, compensationModel }
    )
    if (result.ok) {
      setChoice(prev => prev ? { ...prev, compensationModel } : { compensationModel, source: 'EMPLOYEE_CHOICE' })
      return true
    } else {
      setError(result.error)
      return false
    }
  }, [employeeId])

  return { choice, loading, error, updateChoice }
}
