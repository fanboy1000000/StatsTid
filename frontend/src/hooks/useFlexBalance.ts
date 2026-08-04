import { useState, useEffect, useCallback, useRef } from 'react'
import type { FlexBalanceInfo } from '../types'
import { apiClient } from '../lib/api'

// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — the read
// rides the TYPED spec-keyed form. The response is the ruled ONE shape (owner
// ruling #1): `previousBalance`/`delta`/`reason` are always present and NULL
// (never absent) when the employee has no flex history; the vestigial
// `message` member was dropped backend-side (no reader existed). The FE
// companion edit lives in `FlexBalanceCard.tsx` (presence-guards → value-guards).

export function useFlexBalance(employeeId: string) {
  const [flexBalance, setFlexBalance] = useState<FlexBalanceInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const latestBalanceRequestId = useRef(0)

  const fetchBalance = useCallback(async () => {
    if (!employeeId) return
    const requestId = ++latestBalanceRequestId.current
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/flex-balance/{employeeId}', {
      params: { path: { employeeId } },
    })
    // S126 / F2 — a newer request superseded this one while it was in flight; drop it.
    if (requestId !== latestBalanceRequestId.current) return
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
