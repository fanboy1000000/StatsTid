import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'

// S65 / TASK-6503 — read-only year-overview contract consumed by ArsoversigtPage
// (Direction E Årsoversigt). Typed verbatim to the pinned API contract in
// SPRINT-65.md: every quantity is server-computed; the FE never recomputes a
// past/current/future classification — it derives those from `today` ONLY.

/** Header context line: identity + dated agreement/OK + weekly norm. */
export interface YearOverviewHeader {
  employeeName: string
  agreementCode: string
  okVersion: string
  /** merged-config WeeklyNorm × current PartTimeFraction; null if no profile/config. */
  weeklyNormHours: number | null
}

/** The designed 6 balance tiles (Feriefridage is matrix-only — no 7th tile). */
export interface YearOverviewTiles {
  flexBalance: number
  ferieRemaining: number
  careDayRemaining: number
  /** null when not seniorDayEligible. */
  seniorDayRemaining: number | null
  sickDaysYtd: number
  /** null when not childSickEligible. */
  childSickRemaining: number | null
  childSickEligible: boolean
  seniorDayEligible: boolean
}

/** One calendar month (index 0..11 = Jan..Dec of the selected year). */
export interface YearOverviewMonth {
  month: number
  /** Σ work_time_projection rows in the month (ADR-028). */
  workedHours: number
  /** Σ per-day norms; null if ANY norm-bearing day resolves null. */
  normHours: number | null
  /** workedHours − normHours for months ≤ today's month; null for future months. */
  diff: number | null
}

/** One absence category group (VACATION, SPECIAL_HOLIDAY, CARE_DAY, SENIOR_DAY). */
export interface YearOverviewCategory {
  type: string
  label: string
  /**
   * end-of-month remaining per month (index 0..11). A cell is null when the
   * server cannot compute a saldo — the graceful empty-config branch emits an
   * ALL-null array (`new decimal?[12]`) for any category with no entitlement
   * config under the employee's agreement/OK (e.g. AC_RESEARCH/AC_TEACHING),
   * so this is reachable in production. The UI renders null as an em-dash.
   */
  saldo: (number | null)[]
  /** day-equivalents consumed per month (index 0..11; future-dated = planlagt). */
  afholdt: number[]
  /** transferable amount — render ONLY in the boundaryMonth column when > 0. */
  transferable: number
  /** display anchor month (1..12) where transferable is emitted — 12 for all categories. */
  boundaryMonth: number
}

export interface YearOverview {
  employeeId: string
  year: number
  /** server date — SOLE past/current/future + "Nu" authority. */
  today: string
  header: YearOverviewHeader
  tiles: YearOverviewTiles
  months: YearOverviewMonth[]
  categories: YearOverviewCategory[]
}

export function useYearOverview(employeeId: string, year: number) {
  const [data, setData] = useState<YearOverview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Stale-response guard: rapid ←/→ year switches fire overlapping requests that
  // can resolve out of order. Each invocation claims a monotonically increasing
  // id; only the latest in-flight request is allowed to commit state, so an older
  // year's response can never overwrite a newer one.
  const latestRequestId = useRef(0)

  const fetchOverview = useCallback(async () => {
    if (!employeeId) return
    const requestId = ++latestRequestId.current
    setLoading(true)
    setError(null)
    const result = await apiClient.get<YearOverview>(
      `/api/balance/${employeeId}/year-overview?year=${year}`
    )
    // A newer request superseded this one while it was in flight — drop the result.
    if (requestId !== latestRequestId.current) return
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year])

  useEffect(() => {
    fetchOverview()
  }, [fetchOverview])

  return { data, loading, error, refetch: fetchOverview }
}
