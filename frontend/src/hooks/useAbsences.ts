import { useState, useEffect, useCallback } from 'react'
import type { AbsenceEntry } from '../types'
import { apiClient } from '../lib/api'

export function useAbsences(employeeId: string) {
  const [absences, setAbsences] = useState<AbsenceEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchAbsences = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<AbsenceEntry[]>(`/api/absences/${employeeId}`)
    if (result.ok) {
      setAbsences(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchAbsences() }, [fetchAbsences])

  const registerAbsence = async (absence: AbsenceEntry) => {
    const result = await apiClient.post<{ eventId: string }>('/api/absences', absence)
    if (!result.ok) throw new Error(result.error)
    await fetchAbsences()
    return result.data
  }

  return { absences, loading, error, fetchAbsences, registerAbsence }
}
