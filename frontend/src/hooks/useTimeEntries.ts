import { useState, useEffect, useCallback } from 'react'
import type { TimeEntry } from '../types'

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

export function useTimeEntries(employeeId: string) {
  const [entries, setEntries] = useState<TimeEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchEntries = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`${API_BASE}/api/time-entries/${employeeId}`, {
        headers: token ? { 'Authorization': `Bearer ${token}` } : {},
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      const data = await res.json()
      setEntries(data)
    } catch (e) {
      setError(String(e))
    } finally {
      setLoading(false)
    }
  }, [employeeId])

  useEffect(() => { fetchEntries() }, [fetchEntries])

  const registerEntry = async (entry: Omit<TimeEntry, 'registeredAt'>) => {
    const token = getToken()
    const res = await fetch(`${API_BASE}/api/time-entries`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(entry),
    })
    if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
    await fetchEntries()
    return res.json()
  }

  return { entries, loading, error, fetchEntries, registerEntry }
}
