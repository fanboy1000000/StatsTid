import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

/**
 * S88 / TASK-8802 — the per-employee project-time allocation breakdown for the
 * leader Teamoversigt expandable detail row (GET
 * /api/approval/{employeeId}/allocation-breakdown?year=&month=).
 *
 * S116 / TASK-11602 — the read switched to the TYPED spec-keyed form (PAT-012
 * Pass 3); the hand-written `AllocationBreakdown`/`AllocationEntry` interfaces
 * were DELETED in favor of the GENERATED spec records below.
 *
 * Lazy by construction: this hook only mounts when a row is expanded (one open
 * at a time), so the fetch fires on expand and is per-employee fault-isolated —
 * a failed call sets `error` (it does NOT throw); the detail panel renders a
 * soft "Kunne ikke hente fordeling" while the rest of the panel still renders.
 *
 * `hasAllocationImbalance` is the AUTHORITATIVE per-day predicate computed by
 * the backend (TASK-8801) and EQUALS the table row's `hasWarning` exactly
 * (ADR-028 D4 — the allocation gate blocks in BOTH directions, per day). The
 * detail's imbalance UI drives off `hasAllocationImbalance`, never off a
 * month-level `underAllocated` scalar. `underAllocated`/`overAllocated` are
 * display aids (rounded month sums); `allocations[]` is a month-sum-by-taskId
 * display aid.
 */
export type AllocationEntry =
  components['schemas']['StatsTid.Backend.Api.Contracts.AllocationBreakdownItem']

export type AllocationBreakdown =
  components['schemas']['StatsTid.Backend.Api.Contracts.AllocationBreakdownResponse']

interface UseAllocationBreakdownResult {
  data: AllocationBreakdown | null
  loading: boolean
  error: string | null
  refetch: () => void
}

export function useAllocationBreakdown(
  employeeId: string,
  year: number,
  month: number,
): UseAllocationBreakdownResult {
  const [data, setData] = useState<AllocationBreakdown | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchData = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const res = await apiClient.get('/api/approval/{employeeId}/allocation-breakdown', {
      params: { path: { employeeId } },
      query: { year, month },
    })
    if (res.ok) {
      setData(res.data)
    } else {
      setError(res.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  return { data, loading, error, refetch: fetchData }
}
