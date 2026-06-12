import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import { computeDayDiffs, computeMonthDiffTotal, type MonthDiffInputs } from './useSkema'

export interface EntitlementInfo {
  type: string
  label: string
  totalQuota: number
  used: number
  planned: number
  carryoverIn: number
  remaining: number
  earned: number
  entitlementYear: number
}

// S61/ADR-030: overtime/afspadsering balance block returned verbatim by
// `/api/balance/{id}/summary`. Display values only — never recomputed client-side.
export interface OvertimeBalanceInfo {
  accumulated: number
  paidOut: number
  afspadseringUsed: number
  remaining: number
  compensationModel: string
}

export interface BalanceSummary {
  flexBalance: number
  flexDelta: number
  vacationDaysUsed: number
  vacationDaysEntitlement: number
  normHoursExpected: number
  normHoursActual: number
  overtimeHours: number
  agreementCode: string
  hasMerarbejde: boolean
  entitlements?: EntitlementInfo[]
  overtimeBalance?: OvertimeBalanceInfo | null
}

export function useBalanceSummary(employeeId: string, year: number, month: number) {
  const [data, setData] = useState<BalanceSummary | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchBalance = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<BalanceSummary>(
      `/api/balance/${employeeId}/summary?year=${year}&month=${month}`
    )
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchBalance()
  }, [fetchBalance])

  return { data, loading, error, refetch: fetchBalance }
}

// ════════════════════════════════════════════════════════════════════════════
// S72 / TASK-7205 — pure month-GET derivations for the 4-card balance strip
// (SPRINT-72 R10 HYBRID sourcing + owner ruling D-A). `/summary` keeps serving
// the headline saldi; everything month-scoped derives from the month GET's data
// via these PURE helpers — one computation owner shared by SkemaPage (over its
// LOCAL grid state, so the Flex card reconciles with the grid's Diff total BY
// CONSTRUCTION — the R2 pin) and ApprovalDetailPanel (over the served data).
// ════════════════════════════════════════════════════════════════════════════

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/** The Flex-card inputs ARE the R2 diff inputs — one shape, one owner (W1). */
export type MonthFlexDeltaInputs = MonthDiffInputs

/**
 * R2 — the month flex delta ("Denne måned"). S72 Step-7a W1: this is a pure
 * DELEGATION to useSkema's single R2 computation owner (computeDayDiffs +
 * computeMonthDiffTotal) — the same helpers the grid's Diff row and trailing
 * total consume, so the Flex card reconciles with the grid BY CONSTRUCTION
 * (there is no second implementation to drift). `/summary.flexDelta` is the
 * LAST event's delta — never a month aggregate — so this value can NOT come
 * from `/summary` (Reviewer W3, R10).
 */
export function computeMonthFlexDelta(inputs: MonthFlexDeltaInputs): number {
  return computeMonthDiffTotal(computeDayDiffs(inputs))
}

/** Per-absence-type month usage for the day cards' "Afholdt i <måned>" line. */
export interface MonthAbsenceUsage {
  /** Σ the type's served absence HOURS in the viewed month. */
  hours: number
  /** Σ the type's served recorded `feriedage` — null rows SKIPPED (ADR-032
      persists null feriedage on zero-norm days; SPRINT-72 R10 / Reviewer N4). */
  days: number
}

/**
 * S72 Step-7a Reviewer W1 — the served `absences[].absenceType` is the ABSENCE
 * type, not the entitlement type the cards key on. The backend's
 * `EntitlementMapping.AbsenceToEntitlementType` resolves e.g.
 * `SPECIAL_HOLIDAY_ALLOWANCE` → `SPECIAL_HOLIDAY`; without the alias the
 * Særlige card would show "Afholdt 0,0 t" for a month whose I alt includes
 * those hours (rows reach the projection via non-Skema surfaces today). Only
 * the card-relevant non-identity aliases are mirrored here — everything else
 * aggregates under its own key.
 */
const ABSENCE_TO_ENTITLEMENT_ALIASES: ReadonlyMap<string, string> = new Map([
  ['SPECIAL_HOLIDAY_ALLOWANCE', 'SPECIAL_HOLIDAY'],
])

/**
 * R10 — sums the served month absences per ENTITLEMENT key (absence types
 * resolved through the alias map above). `feriedage` is the ADR-032 recorded
 * day-equivalent served verbatim by the month GET; rows where it is
 * null/undefined contribute their HOURS but are skipped in the DAYS sum.
 */
export function deriveMonthAbsenceUsage(
  absences:
    | readonly { absenceType: string; hours: number; feriedage?: number | null }[]
    | undefined,
): Map<string, MonthAbsenceUsage> {
  const map = new Map<string, MonthAbsenceUsage>()
  for (const a of absences ?? []) {
    const key = ABSENCE_TO_ENTITLEMENT_ALIASES.get(a.absenceType) ?? a.absenceType
    const usage = map.get(key) ?? { hours: 0, days: 0 }
    usage.hours = round2(usage.hours + a.hours)
    if (a.feriedage !== null && a.feriedage !== undefined) {
      usage.days = round2(usage.days + a.feriedage)
    }
    map.set(key, usage)
  }
  return map
}
