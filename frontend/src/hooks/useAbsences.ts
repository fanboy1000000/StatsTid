import { useState, useEffect, useCallback } from 'react'
import type { AbsenceEntry } from '../types'

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

export function useAbsences(employeeId: string) {
  const [absences, setAbsences] = useState<AbsenceEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchAbsences = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`${API_BASE}/api/absences/${employeeId}`, {
        headers: token ? { 'Authorization': `Bearer ${token}` } : {},
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      const data = await res.json()
      setAbsences(data)
    } catch (e) {
      setError(String(e))
    } finally {
      setLoading(false)
    }
  }, [employeeId])

  useEffect(() => { fetchAbsences() }, [fetchAbsences])

  const registerAbsence = async (absence: AbsenceEntry) => {
    const token = getToken()
    const res = await fetch(`${API_BASE}/api/absences`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(absence),
    })
    if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
    await fetchAbsences()
    return res.json()
  }

  return { absences, loading, error, fetchAbsences, registerAbsence }
}
