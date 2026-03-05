import { useState, useEffect, useCallback } from 'react'
import type { TimeEntry } from '../types'
import { apiClient } from '../lib/api'

export function useTimeEntries(employeeId: string) {
  const [entries, setEntries] = useState<TimeEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchEntries = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<TimeEntry[]>(`/api/time-entries/${employeeId}`)
    if (result.ok) {
      setEntries(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchEntries() }, [fetchEntries])

  const registerEntry = async (entry: Omit<TimeEntry, 'registeredAt'>) => {
    const result = await apiClient.post<{ eventId: string }>('/api/time-entries', entry)
    if (!result.ok) throw new Error(result.error)
    await fetchEntries()
    return result.data
  }

  return { entries, loading, error, fetchEntries, registerEntry }
}
