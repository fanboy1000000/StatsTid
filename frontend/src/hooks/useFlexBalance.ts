import { useState, useEffect, useCallback } from 'react'
import type { FlexBalanceInfo } from '../types'

const API_BASE = 'http://localhost:5100'
const TOKEN_KEY = 'statstid_token'

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function handle401(res: Response) {
  if (res.status === 401) {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem('statstid_user')
    window.location.reload()
  }
}

export function useFlexBalance(employeeId: string) {
  const [flexBalance, setFlexBalance] = useState<FlexBalanceInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchBalance = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`${API_BASE}/api/flex-balance/${employeeId}`, {
        headers: token ? { 'Authorization': `Bearer ${token}` } : {},
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      const data = await res.json()
      setFlexBalance(data)
    } catch (e) {
      setError(String(e))
    } finally {
      setLoading(false)
    }
  }, [employeeId])

  useEffect(() => { fetchBalance() }, [fetchBalance])

  return { flexBalance, loading, error, fetchBalance }
}
