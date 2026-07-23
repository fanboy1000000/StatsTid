import { useState, useEffect, useCallback } from 'react'
import type { components } from '../lib/api-types'
import { apiClient } from '../lib/api'

// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — both calls
// ride the TYPED spec-keyed forms; the hand-written 2-member
// `CompensationChoice` interface was DELETED. Lie-audit: the wire GET shape
// has FOUR members (`employeeId`, `periodYear`, `compensationModel`, `source`)
// — the hand-written type omitted `employeeId`/`periodYear`. `source`'s VALUE
// differs by branch (explicit choice vs agreement default) but the key set is
// identical — same keys, NOT polymorphic (the S120 fact sheet). The PUT's
// request key set is byte-unchanged ({ periodYear, compensationModel });
// UNCONDITIONED (no If-Match/If-None-Match — the S120 census pin).

export type CompensationChoice =
  components['schemas']['StatsTid.Backend.Api.Contracts.CompensationChoiceResponse']

export type CompensationChoiceUpdate =
  components['schemas']['StatsTid.Backend.Api.Contracts.CompensationChoiceUpdateResponse']

interface UseCompensationChoiceResult {
  choice: CompensationChoice | null
  loading: boolean
  error: string | null
  updateChoice: (periodYear: number, compensationModel: string) => Promise<boolean>
}

export function useCompensationChoice(employeeId: string, periodYear: number): UseCompensationChoiceResult {
  const [choice, setChoice] = useState<CompensationChoice | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchChoice = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/overtime/{employeeId}/compensation-choice', {
      params: { path: { employeeId } },
      query: { periodYear },
    })
    if (result.ok) {
      setChoice(result.data)
    } else {
      // 404 means employee's agreement doesn't allow choice - not an error
      if (result.status === 404) {
        setChoice(null)
      } else {
        setError(result.error)
      }
    }
    setLoading(false)
  }, [employeeId, periodYear])

  useEffect(() => {
    fetchChoice()
  }, [fetchChoice])

  const updateChoice = useCallback(async (year: number, compensationModel: string): Promise<boolean> => {
    const result = await apiClient.put('/api/overtime/{employeeId}/compensation-choice', {
      params: { path: { employeeId } },
      body: { periodYear: year, compensationModel },
    })
    if (result.ok) {
      // Behavior-preserving state update (the pre-S120 logic verbatim); the
      // no-prev fallback now composes the FULL 4-member spec shape from the
      // 200 echo (`employeeId`/`periodYear` — members the deleted hand-written
      // type omitted) instead of inventing a partial object.
      const echo = result.data
      setChoice(prev =>
        prev
          ? { ...prev, compensationModel: echo.compensationModel }
          : {
              employeeId: echo.employeeId,
              periodYear: echo.periodYear,
              compensationModel: echo.compensationModel,
              source: 'EMPLOYEE_CHOICE',
            },
      )
      return true
    } else {
      setError(result.error)
      return false
    }
  }, [employeeId])

  return { choice, loading, error, updateChoice }
}
