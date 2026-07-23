import { useState, useEffect, useCallback } from 'react'
import type { components } from '../lib/api-types'
import type { TimeEntry } from '../types'
import { apiClient } from '../lib/api'

// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — both calls
// ride the TYPED spec-keyed forms: the list GET binds the SharedKernel
// `TimeEntry` rows (see the lie-audit note on the `types.ts` alias) and the
// register POST binds the spec request + the 201 receipt.

/** The register POST body — the GENERATED spec request. The FE form's payload
    key set is byte-unchanged (incl. the spec-deprecated `okVersion`, which the
    form still sends — an accepted, spec-declared optional). */
export type RegisterTimeEntryRequest =
  components['schemas']['StatsTid.Backend.Api.Contracts.RegisterTimeEntryRequest']

/** The 201 receipt — `{ eventId, streamId }`. The deleted hand-written inline
    type claimed only `{ eventId }`; the served `streamId` surfaces additively. */
export type TimeEntryCreated =
  components['schemas']['StatsTid.Backend.Api.Contracts.TimeEntryCreatedResponse']

export function useTimeEntries(employeeId: string) {
  const [entries, setEntries] = useState<TimeEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchEntries = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/time-entries/{employeeId}', {
      params: { path: { employeeId } },
    })
    if (result.ok) {
      setEntries(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchEntries() }, [fetchEntries])

  const registerEntry = async (entry: RegisterTimeEntryRequest): Promise<TimeEntryCreated> => {
    // UNCONDITIONED create (no If-Match/If-None-Match — the S120 census pin).
    const result = await apiClient.post('/api/time-entries', { body: entry })
    if (!result.ok) throw new Error(result.error)
    await fetchEntries()
    return result.data
  }

  return { entries, loading, error, fetchEntries, registerEntry }
}
