import { useState, useEffect, useCallback, useRef } from 'react'
import type { TimeEntry } from '../types'
import { apiClient } from '../lib/api'

// The list GET rides the TYPED spec-keyed form and binds the SharedKernel
// `TimeEntry` rows (see the lie-audit note on the `types.ts` alias). There is
// no register call: POST /api/time-entries is retired; time is saved via Skema.

export function useTimeEntries(employeeId: string) {
  const [entries, setEntries] = useState<TimeEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const latestEntriesRequestId = useRef(0)

  const fetchEntries = useCallback(async () => {
    if (!employeeId) return
    const requestId = ++latestEntriesRequestId.current
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/time-entries/{employeeId}', {
      params: { path: { employeeId } },
    })
    // S126 / F2 — a newer request superseded this one while it was in flight; drop it.
    if (requestId !== latestEntriesRequestId.current) return
    if (result.ok) {
      setEntries(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchEntries() }, [fetchEntries])

  return { entries, loading, error, fetchEntries }
}
