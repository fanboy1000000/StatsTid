import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

/**
 * S87 / TASK-8702 — one row of the leader Teamoversigt (the team-overview
 * aggregate, GET /api/approval/team-overview?year=&month=).
 *
 * S116 / TASK-11602 — the read switched to the TYPED spec-keyed form (PAT-012
 * Pass 3) and the hand-written `TeamOverviewRow`/`TeamOverviewResponse`
 * interfaces were DELETED: the row type below is the GENERATED spec record
 * (`TeamOverviewEmployeeRow`) verbatim, so a backend field drop/rename/type
 * change is a `tsc` error here rather than a silent prod break.
 *
 * VIEW-layer semantics that stay true of the spec shape (backend TASK-8701 is
 * the authority):
 *  - The roster is the leader's DESIGNATED-act-authority set (ADR-027 D13
 *    see == act), extended to emit zero-period reports as DRAFT rows
 *    (`periodId === null`). A null periodId ⇒ no handling actions.
 *  - `decisionAt` is NEUTRAL: rejects write approved_at too (no stored
 *    rejectedAt), so `status` disambiguates APPROVED vs REJECTED.
 *  - `hasWarning` mirrors ONLY the allocation ("Ikke fordelt") arm of the
 *    approve gate — a named P1 narrowing, so `hasWarning === false` does NOT
 *    mean "submittable".
 *  - `payrollExported` (S90 / TASK-9005): the month is sent to lønkørsel
 *    (post-export lock, ADR-034) → the page hides the Genåbn control and shows
 *    a "Sendt til lønkørsel" indicator; `payrollExportedAt` carries the export
 *    timestamp (null otherwise).
 */
export type TeamOverviewRow =
  components['schemas']['StatsTid.Backend.Api.Contracts.TeamOverviewEmployeeRow']

export interface UseTeamOverviewResult {
  rows: TeamOverviewRow[]
  loading: boolean
  error: string | null
  refetch: () => Promise<void>
}

/**
 * Fetches the team-overview aggregate for the given (year, month). One fetch
 * backs the whole table + KPI band + filters. Refetch after any mutation
 * (approve/reject/reopen/bulk) so the page never optimistically diverges.
 */
export function useTeamOverview(year: number, month: number): UseTeamOverviewResult {
  const [rows, setRows] = useState<TeamOverviewRow[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchOverview = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/approval/team-overview', {
      query: { year, month },
    })
    if (result.ok) {
      setRows(result.data.employees)
    } else {
      setError(result.error)
      setRows([])
    }
    setLoading(false)
  }, [year, month])

  useEffect(() => {
    fetchOverview()
  }, [fetchOverview])

  return { rows, loading, error, refetch: fetchOverview }
}
