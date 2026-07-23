import { useState, useEffect, useCallback } from 'react'
import type { components } from '../lib/api-types'
import { apiClient } from '../lib/api'

// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — both reads
// ride the TYPED spec-keyed forms; the hand-written `ComplianceCheckResult`/
// `ComplianceViolation`/`CompensatoryRestEntry` interfaces were DELETED.
//
// THE INTEGER-ENUM REALITY (the TASK-12000 declared discrepancy — a MASKED
// LIVE PROD BUG on this surface): `violationType` and `severity` are INTEGERS
// on the wire (the CLR enums serialize numerically — no
// JsonStringEnumConverter in the HTTP path; the spec truthfully declares
// `type: integer`). The deleted hand-written union typed them as STRINGS
// ('DAILY_REST' | …, 'WARNING' | 'VIOLATION'), so every FE string comparison
// (`severity === 'VIOLATION'` in ComplianceWarnings) could NEVER match a real
// response. It was ALSO false-exhaustive: 4 of the 6 CLR violation types
// (missing OVERTIME_EXCEEDED/OVERTIME_UNAPPROVED). The named constants below
// carry the CLR order, verified against
// src/SharedKernel/.../ComplianceCheckResult.cs. An OUT-OF-RANGE value fails
// `tsc` against the generated `0 | 1 | …` spec unions — but the spec's integer
// set is ORDER-invariant, so a reordered CLR enum would regenerate byte-
// identically and nothing FE-side can see a transposition. The name↔value
// correspondence is pinned backend-side by ComplianceEnumWireOrderTests
// (Step-7a Reviewer W1); if that pin fires, update these constants in the
// SAME change.

export type ComplianceSeverity =
  components['schemas']['StatsTid.SharedKernel.Models.ComplianceSeverity']
export type ComplianceViolationType =
  components['schemas']['StatsTid.SharedKernel.Models.ComplianceViolationType']

/** The CLR `ComplianceSeverity` order (WARNING=0, VIOLATION=1). */
export const COMPLIANCE_SEVERITY: Record<'WARNING' | 'VIOLATION', ComplianceSeverity> = {
  WARNING: 0,
  VIOLATION: 1,
}

/** The CLR `ComplianceViolationType` order (DAILY_REST=0 … OVERTIME_UNAPPROVED=5). */
export const COMPLIANCE_VIOLATION_TYPE: Record<
  | 'DAILY_REST'
  | 'WEEKLY_REST'
  | 'MAX_DAILY_HOURS'
  | 'WEEKLY_MAX_HOURS'
  | 'OVERTIME_EXCEEDED'
  | 'OVERTIME_UNAPPROVED',
  ComplianceViolationType
> = {
  DAILY_REST: 0,
  WEEKLY_REST: 1,
  MAX_DAILY_HOURS: 2,
  WEEKLY_MAX_HOURS: 3,
  OVERTIME_EXCEEDED: 4,
  OVERTIME_UNAPPROVED: 5,
}

export type ComplianceViolation =
  components['schemas']['StatsTid.SharedKernel.Models.ComplianceViolation']

/** The 200 body — structurally always the full result post-ruling #3 (the
    defensive literal-null branch now 502s upstream-invalid). */
export type ComplianceCheckResult =
  components['schemas']['StatsTid.SharedKernel.Models.ComplianceCheckResult']

/** GET /api/compliance/{employeeId}/compensatory-rest row (7 members; the
    deleted hand-written interface was value-faithful — same members, same
    `status` value set, now spec-declared). */
export type CompensatoryRestEntry =
  components['schemas']['StatsTid.Backend.Api.Contracts.CompensatoryRestItem']

interface UseComplianceResult {
  result: ComplianceCheckResult | null
  loading: boolean
  error: string | null
  refetch: () => void
}

export function useCompliance(employeeId: string, year: number, month: number): UseComplianceResult {
  const [result, setResult] = useState<ComplianceCheckResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchData = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const res = await apiClient.get('/api/compliance/{employeeId}/period', {
      params: { path: { employeeId } },
      query: { year, month },
    })
    if (res.ok) {
      setResult(res.data)
    } else {
      setError(res.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  return { result, loading, error, refetch: fetchData }
}

export function useCompensatoryRest(employeeId: string) {
  const [entries, setEntries] = useState<CompensatoryRestEntry[]>([])
  const [loading, setLoading] = useState(false)

  const fetchData = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    const res = await apiClient.get('/api/compliance/{employeeId}/compensatory-rest', {
      params: { path: { employeeId } },
    })
    if (res.ok) {
      setEntries(res.data)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  return { entries, loading, refetch: fetchData }
}
