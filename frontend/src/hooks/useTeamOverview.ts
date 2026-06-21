import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'

/**
 * S87 / TASK-8702 — one row of the leader Teamoversigt (the team-overview
 * aggregate, GET /api/approval/team-overview?year=&month=).
 *
 * The backend (TASK-8701) is the authority for this shape. The roster is the
 * leader's DESIGNATED-act-authority set (ADR-027 D13 see == act), extended to
 * emit zero-period reports as DRAFT rows (`periodId === null`). A null periodId
 * ⇒ no handling actions (nothing to approve/reject/reopen).
 *
 * `decisionAt` is NEUTRAL: rejects write approved_at too (no stored rejectedAt),
 * so `status` disambiguates APPROVED vs REJECTED. `hasWarning` mirrors ONLY the
 * allocation ("Ikke fordelt") arm of the approve gate — a named P1 narrowing, so
 * `hasWarning === false` does NOT mean "submittable".
 */
export interface TeamOverviewRow {
  /** null ⇒ no period this month (zero-period DRAFT row) ⇒ NO handling actions. */
  periodId: string | null
  employeeId: string
  displayName: string
  /** e.g. "AC" — the period's agreement when a period exists, else the user's. */
  agreement: string
  /** The raw backend status; mapped to the 4 display statuses by the page. */
  status: 'SUBMITTED' | 'EMPLOYEE_APPROVED' | 'APPROVED' | 'REJECTED' | 'DRAFT'
  submittedAt: string | null
  /** = approved_at (rejects write it too); status disambiguates. */
  decisionAt: string | null
  rejectionReason: string | null
  normExpected: number
  normRegistered: number
  flexBalance: number
  overtime: number
  ferieUsed: number
  ferieTotal: number
  awayToday: boolean
  hasWarning: boolean
  /**
   * S90 / TASK-9005 — the month is sent to lønkørsel (a payroll_export_records row
   * exists for this employee + (year, month)). Once true the month is corrections-only
   * (post-export lock, ADR-034) → the page HIDES the Genåbn control and shows a
   * "Sendt til lønkørsel" indicator instead. A read-only cross-context flag the backend
   * aggregate surfaces; the Backend never writes the Payroll-owned lock table.
   */
  payrollExported: boolean
  /** When `payrollExported`, the export timestamp; null otherwise. */
  payrollExportedAt?: string | null
}

interface TeamOverviewResponse {
  employees: TeamOverviewRow[]
}

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
    const result = await apiClient.get<TeamOverviewResponse>(
      `/api/approval/team-overview?year=${year}&month=${month}`,
    )
    if (result.ok) {
      setRows(result.data.employees ?? [])
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
