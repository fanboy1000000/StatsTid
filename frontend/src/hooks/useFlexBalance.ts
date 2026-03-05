import { useState, useEffect, useCallback } from 'react'
import type { FlexBalanceInfo } from '../types'
import { apiClient } from '../lib/api'

export function useFlexBalance(employeeId: string) {
  const [flexBalance, setFlexBalance] = useState<FlexBalanceInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchBalance = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<FlexBalanceInfo>(`/api/flex-balance/${employeeId}`)
    if (result.ok) {
      setFlexBalance(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchBalance() }, [fetchBalance])

  return { flexBalance, loading, error, fetchBalance }
}
