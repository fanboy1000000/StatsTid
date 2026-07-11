import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData, SkemaRow, WorkTimeDay, WorkTimeInterval } from '../types'

export interface QuotaError {
  absenceType: string
  remaining: number
  requested: number
}

/**
 * S73 R4 — the save-month outcome, discriminated so the page's `fireCellSave`
 * can split the rejected-save policy: a `rejected` (HTTP 422) delta REVERTS the
 * affected cells to server truth (with the overlap guard) and counts RESOLVED
 * for the flush; every `failed` (400/401/403/404/409/5xx/network) keeps the
 * local edits + the B2 re-queue/retry posture (401/403 are credential-shaped —
 * a mid-edit session expiry never discards typed cells).
 */
export type SaveMonthResult =
  | { status: 'ok' }
  | { status: 'rejected'; httpStatus: number }
  | { status: 'failed'; httpStatus: number; error: string }

/**
 * S73 R4 — the typed body of an `absence_full_day_only` 422 (TASK-7301's served
 * shape) and the `absence_type_not_eligible` family. Surfaced as a Danish alert.
 */
export interface AbsenceRuleError {
  error: 'absence_full_day_only' | 'absence_type_not_eligible'
  absenceType: string
  date?: string
  requiredHours?: number
  message?: string
}

/** 422 from employee-approve / submit-and-approve — discriminated union. */
export interface CoverageValidationError {
  kind: 'coverage'
  missingDays: string[]
  coveredDays: number
  totalWorkdays: number
}

export interface AllocationUnbalancedDay {
  date: string
  worked: number
  allocated: number
  direction: 'under' | 'over'
}

export interface AllocationValidationError {
  kind: 'allocation'
  unbalancedDays: AllocationUnbalancedDay[]
}

export type ApprovalValidationError = CoverageValidationError | AllocationValidationError

/**
 * Derive OK version from a period-start month. The OK24 -> OK26 switch is
 * 2026-04-01: Jan-Mar 2026 (and earlier) -> OK24, Apr 2026 onwards -> OK26.
 */
export function deriveOkVersion(year: number, month: number): 'OK24' | 'OK26' {
  if (year > 2026) return 'OK26'
  if (year < 2026) return 'OK24'
  return month >= 4 ? 'OK26' : 'OK24'
}

interface UseSkemaResult {
  data: SkemaMonthData | null
  loading: boolean
  error: string | null
  quotaError: QuotaError | null
  /** S73 R4 — the typed `absence_full_day_only` / `absence_type_not_eligible`
      422 body, surfaced as a Danish alert; null when none. */
  absenceRuleError: AbsenceRuleError | null
  approvalValidationError: ApprovalValidationError | null
  clearQuotaError: () => void
  clearAbsenceRuleError: () => void
  clearApprovalValidationError: () => void
  refetch: () => void
  /** S73 R4 — returns the discriminated outcome so the page can split revert (422)
      vs retry (other non-2xx) inside `fireCellSave`. */
  saveMonth: (
    cells: { rowKey: string; date: string; hours: number | null }[],
    workTime?: WorkTimeDay[],
  ) => Promise<SaveMonthResult>
  employeeApprove: (periodId: string) => Promise<void>
  submitAndApprove: (orgId: string, agreementCode: string) => Promise<void>
  reopenPeriod: (periodId: string, reason?: string) => Promise<void>
}

/** Parse a 422 approval-gate body into a discriminated ApprovalValidationError, or null. */
function parseApprovalValidationError(raw: string): ApprovalValidationError | null {
  try {
    const body = JSON.parse(raw)
    if (body.kind === 'allocation' && Array.isArray(body.unbalancedDays)) {
      return { kind: 'allocation', unbalancedDays: body.unbalancedDays }
    }
    if (Array.isArray(body.missingDays)) {
      return {
        kind: 'coverage',
        missingDays: body.missingDays,
        coveredDays: body.coveredDays ?? 0,
        totalWorkdays: body.totalWorkdays ?? 0,
      }
    }
  } catch {
    // Not valid JSON
  }
  return null
}

// ── S116 / TASK-11602 — the sanctioned LEGACY skema-family calls ─────────────
// The skema month GET + save POST are GRANDFATHERED untyped operations (the
// spec declares no response schema — `content?: never` — so they have NO typed
// form until the skema family is drained in a later pass). Their explicit-T
// legacy calls remain, pinned by these ROUTE HELPERS: the eslint tier for this
// file bans every explicit-T apiClient call EXCEPT one whose first argument is
// `SKEMA_MONTH_PATH(...)` / `SKEMA_SAVE_PATH(...)` (the S115
// `ELIGIBILITY_PATH` lint-pin precedent), so a future explicit-T call on any
// OTHER url in this file stays banned.
const SKEMA_MONTH_PATH = (employeeId: string, year: number, month: number) =>
  `/api/skema/${employeeId}/month?year=${year}&month=${month}`
const SKEMA_SAVE_PATH = (employeeId: string) => `/api/skema/${employeeId}/save`

export function useSkema(employeeId: string, year: number, month: number): UseSkemaResult {
  const [data, setData] = useState<SkemaMonthData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [quotaError, setQuotaError] = useState<QuotaError | null>(null)
  const [absenceRuleError, setAbsenceRuleError] = useState<AbsenceRuleError | null>(null)
  const [approvalValidationError, setApprovalValidationError] = useState<ApprovalValidationError | null>(null)

  const fetchData = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<SkemaMonthData>(
      SKEMA_MONTH_PATH(employeeId, year, month)
    )
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year, month])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  const clearQuotaError = useCallback(() => setQuotaError(null), [])
  const clearAbsenceRuleError = useCallback(() => setAbsenceRuleError(null), [])
  const clearApprovalValidationError = useCallback(() => setApprovalValidationError(null), [])

  const absenceTypesRef = useRef<Set<string>>(new Set())
  useEffect(() => {
    absenceTypesRef.current = new Set(data?.absenceTypes?.map(a => a.type) ?? [])
  }, [data])

  const saveMonth = useCallback(
    async (
      cells: { rowKey: string; date: string; hours: number | null }[],
      workTime?: WorkTimeDay[],
    ): Promise<SaveMonthResult> => {
      setQuotaError(null)
      setAbsenceRuleError(null)
      const absenceTypeSet = absenceTypesRef.current

      const entries = cells
        .filter(c => !absenceTypeSet.has(c.rowKey) && c.hours != null && c.hours !== 0)
        .map(c => ({ date: c.date, projectCode: c.rowKey, hours: c.hours }))

      const absences = cells
        .filter(c => absenceTypeSet.has(c.rowKey) && c.hours != null && c.hours !== 0)
        .map(c => ({ date: c.date, absenceType: c.rowKey, hours: c.hours }))

      const result = await apiClient.post<void>(SKEMA_SAVE_PATH(employeeId), {
        year,
        month,
        entries: entries.length > 0 ? entries : null,
        absences: absences.length > 0 ? absences : null,
        workTime: workTime && workTime.length > 0 ? workTime : null,
      })
      if (result.ok) {
        return { status: 'ok' }
      }
      // S73 R4 — a 422 is a server REJECTION (revert-and-resolve), discriminated
      // from every other non-2xx (retry). The 422 body carries one of three
      // shapes; parse each into its alert state. A non-JSON / unrecognised 422
      // still counts as a rejection (the cell reverts) but surfaces the raw text.
      if (result.status === 422) {
        try {
          const body = JSON.parse(result.error)
          if (body.absenceType && body.remaining !== undefined && body.requested !== undefined) {
            setQuotaError({
              absenceType: body.absenceType,
              remaining: body.remaining,
              requested: body.requested,
            })
          } else if (body.error === 'absence_full_day_only' || body.error === 'absence_type_not_eligible') {
            setAbsenceRuleError({
              error: body.error,
              absenceType: body.absenceType,
              date: body.date,
              requiredHours: body.requiredHours,
              message: body.message,
            })
          } else {
            setError(result.error)
          }
        } catch {
          // Non-JSON 422 — still a rejection; surface the raw text.
          setError(result.error)
        }
        return { status: 'rejected', httpStatus: 422 }
      }
      // Everything else (400/401/403/404/409/5xx/network) — keep local edits, retry.
      setError(result.error)
      return { status: 'failed', httpStatus: result.status, error: result.error }
    },
    [employeeId, year, month]
  )

  const employeeApprove = useCallback(
    async (periodId: string) => {
      // S116 typed switch — NAMED REQUEST DELTA: the legacy call sent a literal
      // `{}` body; the op binds NO request DTO (the handler takes only the
      // periodId route param — verified against ApprovalEndpoints.cs:1344), so
      // the typed form sends NO body. The backend never read it.
      const result = await apiClient.post('/api/approval/{periodId}/employee-approve', {
        params: { path: { periodId } },
      })
      if (result.ok) {
        setApprovalValidationError(null)
        await fetchData()
      } else {
        if (result.status === 422) {
          const validationError = parseApprovalValidationError(result.error)
          if (validationError) {
            setApprovalValidationError(validationError)
            await fetchData()
            return
          }
        }
        setError(result.error)
        await fetchData()
      }
    },
    [fetchData]
  )

  const submitAndApprove = useCallback(
    async (orgId: string, agreementCode: string) => {
      setApprovalValidationError(null)
      const periodStart = `${year}-${String(month).padStart(2, '0')}-01`
      const lastDay = new Date(year, month, 0).getDate()
      const periodEnd = `${year}-${String(month).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`
      const okVersion = deriveOkVersion(year, month)

      const submitResult = await apiClient.post('/api/approval/submit', {
        body: {
          employeeId,
          orgId,
          periodStart,
          periodEnd,
          periodType: 'MONTHLY',
          agreementCode,
          okVersion,
        },
      })
      if (!submitResult.ok) {
        setError(submitResult.error)
        return
      }

      // S116 typed switch — same NAMED no-body delta as employeeApprove above.
      const approveResult = await apiClient.post('/api/approval/{periodId}/employee-approve', {
        params: { path: { periodId: submitResult.data.periodId } },
      })
      if (approveResult.ok) {
        await fetchData()
      } else {
        if (approveResult.status === 422) {
          const validationError = parseApprovalValidationError(approveResult.error)
          if (validationError) {
            setApprovalValidationError(validationError)
            // Still refetch so the grid shows current state (period was created but not approved)
            await fetchData()
            return
          }
        }
        setError(approveResult.error)
        // Refetch even on error — the period may have been created (SUBMITTED) before approve failed
        await fetchData()
      }
    },
    [employeeId, year, month, fetchData]
  )

  const reopenPeriod = useCallback(
    async (periodId: string, reason?: string) => {
      const result = await apiClient.post('/api/approval/{periodId}/reopen', {
        params: { path: { periodId } },
        body: { reason: reason ?? 'Genåbnet af medarbejder' },
      })
      if (result.ok) {
        await fetchData()
      } else {
        setError(result.error)
      }
    },
    [fetchData]
  )

  return { data, loading, error, quotaError, absenceRuleError, approvalValidationError, clearQuotaError, clearAbsenceRuleError, clearApprovalValidationError, refetch: fetchData, saveMonth, employeeApprove, submitAndApprove, reopenPeriod }
}

// ════════════════════════════════════════════════════════════════════════════
// S72 / TASK-7203 — pure work-time helpers (SPRINT-72 R16: useSkema is the single
// owner of the workTime payload builder and the day panel's period arithmetic).
// Everything below is a PURE module-level export — unit-testable without DOM.
//
// NOTE (R16 transition): SkemaPage.tsx still carries its own private
// buildWorkTimePayload callback; that file is 7205-owned and must compile
// UNCHANGED this sprint, so the hook export below is the PARALLEL single owner
// that 7205 swaps the page onto. The R7 pin (manualHours carry-forward) lives
// and is tested HERE.
// ════════════════════════════════════════════════════════════════════════════

/**
 * R7 — manualHours preservation. The ADR-028 work-time save is a latest-wins
 * whole-day replace (`{date, intervals, manualHours}`), so EVERY write that
 * round-trips a day's workTime MUST carry the day's EXISTING manualHours or the
 * write silently clobbers it. This builder is the one place that contract is
 * enforced: callers pass the dirty dates plus the CURRENT (server-hydrated)
 * interval/manual maps, and each emitted day carries
 * `manualHours: <the day's existing value>` (0 when the day has none — the save
 * contract's "absent" value, byte-identical to the shipped page behavior; R5/R17
 * freeze the contract itself).
 */
export function buildWorkTimePayload(
  dates: Iterable<string>,
  workIntervals: ReadonlyMap<string, readonly WorkTimeInterval[]>,
  manualHours: ReadonlyMap<string, number>,
): WorkTimeDay[] {
  return [...dates].map((date) => ({
    date,
    intervals: (workIntervals.get(date) ?? []).map((iv) => ({ start: iv.start, end: iv.end })),
    manualHours: manualHours.get(date) ?? 0,
  }))
}

/** A raw fra–til clock period as typed in the day panel (step 1). */
export interface ClockPeriod {
  from: string
  to: string
}

/** R6 intra-day warning threshold: worked > 9 t (handoff README:66). */
export const LONG_DAY_WARNING_HOURS = 9

/** S58 advisory mirror: a day's combined work time may not exceed 24 h. The
    backend 422 guard stays authoritative — this is inline advisory only. */
export const MAX_DAY_HOURS = 24

/** §J / SYSTEM_TARGET minimum daily rest between two working days, in hours. */
export const REST_MINIMUM_HOURS = 11

/**
 * Parse a clock-time string to minutes since midnight, or null when invalid.
 * Accepts the panel's raw typing forms ("9:05", "09:05", "09.05", "0905" —
 * prototype parseHM verbatim) plus the served "HH:mm:ss" interval format.
 */
function parseClockTime(value: string): number | null {
  const m = String(value).trim().match(/^(\d{1,2})[.:]?(\d{2})(?::(\d{2}))?$/)
  if (!m) return null
  const h = Number(m[1])
  const mi = Number(m[2])
  const s = m[3] ? Number(m[3]) : 0
  if (h > 23 || mi > 59 || s > 59) return null
  return h * 60 + mi + s / 60
}

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/**
 * EXACT single-period hours at 2-decimal rounding, or null when the period is
 * unparsable, reversed, or zero-length ("ugyldig" in the panel). R5: the
 * handoff's 0,1-rounding is DISPLAY-only — comparisons against the 0.005
 * allocation tolerance always use this exact 2-decimal value.
 */
export function periodHours(p: ClockPeriod): number | null {
  const f = parseClockTime(p.from)
  const t = parseClockTime(p.to)
  if (f === null || t === null || t <= f) return null
  return round2((t - f) / 60)
}

/**
 * EXACT day total over the VALID periods: minutes summed exactly, ONE 2-decimal
 * rounding at the end (matches the grid's calcIntervalHours basis — never a sum
 * of per-row-rounded values).
 */
export function periodsTotalHours(periods: readonly ClockPeriod[]): number {
  let totalMin = 0
  for (const p of periods) {
    const f = parseClockTime(p.from)
    const t = parseClockTime(p.to)
    if (f !== null && t !== null && t > f) totalMin += t - f
  }
  return round2(totalMin / 60)
}

/**
 * The day's worked total = clock periods + the day's existing manual hours
 * (owner ruling D-B: the manual-entry UI is dropped but existing manual_hours
 * keep counting in worked totals). Same basis as the grid's workedPerDay.
 */
export function dayWorkedHours(periods: readonly ClockPeriod[], manualHours: number): number {
  return round2(periodsTotalHours(periods) + manualHours)
}

/**
 * S58 mirror — indices of period rows that OVERLAP another valid row. Touching
 * boundaries (next.from === prev.to) are allowed; invalid/reversed rows are
 * ignored (they are flagged "ugyldig" separately). Mirrors the backend guard;
 * the backend 422 stays authoritative.
 */
export function overlappingPeriodIndices(periods: readonly ClockPeriod[]): Set<number> {
  const parsed: Array<{ i: number; f: number; t: number }> = []
  periods.forEach((p, i) => {
    const f = parseClockTime(p.from)
    const t = parseClockTime(p.to)
    if (f !== null && t !== null && t > f) parsed.push({ i, f, t })
  })
  parsed.sort((a, b) => a.f - b.f)
  const out = new Set<number>()
  if (parsed.length < 2) return out
  let maxEnd = parsed[0].t
  let maxEndIdx = parsed[0].i
  for (let k = 1; k < parsed.length; k++) {
    if (parsed[k].f < maxEnd) {
      out.add(parsed[k].i)
      out.add(maxEndIdx)
    }
    if (parsed[k].t > maxEnd) {
      maxEnd = parsed[k].t
      maxEndIdx = parsed[k].i
    }
  }
  return out
}

/** One §J adjacent-day rest violation (R6 trigger ii). */
export interface RestPeriodWarning {
  /** The exact rest gap in hours (2-decimal). */
  gapHours: number
  /** The earlier day — whose LAST interval end starts the rest gap ("YYYY-MM-DD"). */
  fromDate: string
  /** The later day — whose FIRST interval start ends the rest gap ("YYYY-MM-DD"). */
  toDate: string
}

function addDays(date: string, delta: number): string {
  const d = new Date(date + 'T00:00:00')
  d.setDate(d.getDate() + delta)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${dd}`
}

/** First interval start / last interval end (minutes) over a day's VALID intervals. */
function dayClockBounds(wt: WorkTimeDay | undefined): { firstStart: number; lastEnd: number } | null {
  if (!wt) return null
  let firstStart = Infinity
  let lastEnd = -Infinity
  for (const iv of wt.intervals ?? []) {
    const s = parseClockTime(iv.start)
    const e = parseClockTime(iv.end)
    if (s !== null && e !== null && e > s) {
      if (s < firstStart) firstStart = s
      if (e > lastEnd) lastEnd = e
    }
  }
  return firstStart === Infinity ? null : { firstStart, lastEnd }
}

/**
 * R6 (trigger ii) — the SYSTEM_TARGET §J adjacent-day 11-hour rest analysis,
 * client-side and WARNING-ONLY (the rule engine stays definitive; the in-context
 * VoluntaryUnsocialHours choice is a recorded follow-up, deliberately absent).
 *
 * Checks the gap between the PREVIOUS calendar day's last interval end and the
 * given day's first interval start, and symmetrically toward the NEXT day. At
 * month edges the adjacent day resolves from the served `boundaryWorkTime`
 * (prev-month last day / next-month first day — month entries win on duplicate
 * dates). Days with no interval data impose no constraint; a day with no own
 * intervals returns null (nothing to anchor).
 *
 * Returns the violations (1–2 entries, previous-gap first) or null when none.
 */
export function analyzeRestPeriods(
  monthWorkTime: readonly WorkTimeDay[],
  boundaryWorkTime: readonly WorkTimeDay[] | undefined,
  date: string,
): RestPeriodWarning[] | null {
  const byDate = new Map<string, WorkTimeDay>()
  for (const wt of boundaryWorkTime ?? []) byDate.set(wt.date, wt)
  for (const wt of monthWorkTime) byDate.set(wt.date, wt) // month entries win
  const today = dayClockBounds(byDate.get(date))
  if (!today) return null

  const warnings: RestPeriodWarning[] = []
  const minutesInDay = 24 * 60
  const restMinimumMinutes = REST_MINIMUM_HOURS * 60

  const prevDate = addDays(date, -1)
  const prev = dayClockBounds(byDate.get(prevDate))
  if (prev) {
    const gapMin = minutesInDay - prev.lastEnd + today.firstStart
    if (gapMin < restMinimumMinutes) {
      warnings.push({ gapHours: round2(gapMin / 60), fromDate: prevDate, toDate: date })
    }
  }

  const nextDate = addDays(date, 1)
  const next = dayClockBounds(byDate.get(nextDate))
  if (next) {
    const gapMin = minutesInDay - today.lastEnd + next.firstStart
    if (gapMin < restMinimumMinutes) {
      warnings.push({ gapHours: round2(gapMin / 60), fromDate: date, toDate: nextDate })
    }
  }

  return warnings.length > 0 ? warnings : null
}

// ════════════════════════════════════════════════════════════════════════════
// S72 Step-7a fix-forward (B1 + W1) — the R3/R12 row basis and the R2 diff
// arithmetic each get ONE pure owner here, consumed by SkemaPage,
// ApprovalDetailPanel, SkemaGrid and useBalanceSummary.
// ════════════════════════════════════════════════════════════════════════════

/** The R3/R12 ARITHMETIC/row basis derived from a served month (Step-7a B1). */
export interface SkemaRowBasis {
  /** ALL rows the arithmetic spans (projects first, then absences), render-ready
      for the no-preferences (R12 full-record) surfaces. */
  rows: SkemaRow[]
  /** ALL project keys in the basis (R3 — visibility-independent). */
  projectKeys: ReadonlySet<string>
  /** ALL absence-type keys in the basis (R3). */
  absenceKeys: ReadonlySet<string>
}

/**
 * S72 Step-7a B1 — R3/R12: the arithmetic/row basis is the UNION of the full
 * served `catalogs` and every key present in the month's served
 * `entries`/`absences`. The legacy `projects`/`absenceTypes` fields are the
 * VISIBLE selection for configured users (container-aware since 7201) and must
 * NEVER be the arithmetic basis on their own — hidden rows carrying hours would
 * vanish from I alt / remainder / panel-Resterende / gate mirroring. They are
 * folded in only as the pre-7201 fallback (no `catalogs` served) and as a belt.
 * Keys present in the served data but absent from every catalog (deactivated
 * projects with historical hours) are labeled by their code. RENDERING
 * visibility stays driven by `rowPreferences` — the grid filters rendering only.
 */
export function deriveSkemaRowBasis(
  data: Pick<SkemaMonthData, 'projects' | 'absenceTypes' | 'entries' | 'absences' | 'catalogs'>,
): SkemaRowBasis {
  const projectRows: SkemaRow[] = []
  const projectKeys = new Set<string>()
  const addProject = (key: string, label: string) => {
    if (projectKeys.has(key)) return
    projectKeys.add(key)
    projectRows.push({ type: 'project', key, label })
  }
  for (const p of data.catalogs?.projects ?? []) addProject(p.projectCode, p.projectName)
  for (const p of data.projects ?? []) addProject(p.projectCode, p.projectName)
  // Deactivated projects with historical hours: absent from every catalog —
  // they still carry served hours, so they enter the basis labeled by code.
  for (const e of data.entries ?? []) addProject(e.projectCode, e.projectCode)

  const absenceRows: SkemaRow[] = []
  const absenceKeys = new Set<string>()
  // S73 / TASK-7302 — the served full-day-only flag threads onto the absence row
  // (R3/R5): true → an entry SNAPS to the day's consumption basis + the "hele
  // dage" note renders. Sourced from the served absence-type DTOs (catalogs first,
  // then the visible-selection field). dedup keeps the FIRST-seen flag.
  const addAbsence = (key: string, label: string, fullDayOnly?: boolean) => {
    if (absenceKeys.has(key)) return
    absenceKeys.add(key)
    // Only attach the flag when TRUE — the emitted row shape stays byte-identical
    // to the pre-S73 contract for ordinary types (the S72 deriveSkemaRowBasis
    // pins toEqual exact objects). `undefined` reads falsy at the snap/note sites.
    absenceRows.push(
      fullDayOnly ? { type: 'absence', key, label, fullDayOnly: true } : { type: 'absence', key, label },
    )
  }
  for (const a of data.catalogs?.absenceTypes ?? []) addAbsence(a.type, a.label, a.fullDayOnly)
  for (const a of data.absenceTypes ?? []) addAbsence(a.type, a.label, a.fullDayOnly)
  // Served-history rows absent from every catalog: no DTO carries the flag → false.
  for (const a of data.absences ?? []) addAbsence(a.absenceType, a.absenceType)

  return { rows: [...projectRows, ...absenceRows], projectKeys, absenceKeys }
}

/** Inputs for the R2 month diff arithmetic (W1 — the ONE computation). */
export interface MonthDiffInputs {
  year: number
  month: number
  /** All cells, keyed `${rowKey}:${YYYY-MM-DD}` — the grid's `cellValues` basis. */
  cellValues: ReadonlyMap<string, number>
  /** ALL served project keys (R3 — visibility-independent; deriveSkemaRowBasis). */
  projectKeys: ReadonlySet<string>
  /** ALL served absence-type keys (R3). */
  absenceKeys: ReadonlySet<string>
  workIntervals: ReadonlyMap<string, readonly WorkTimeInterval[]>
  manualHours: ReadonlyMap<string, number>
  /** Served per-day norm (null = academic ANNUAL_ACTIVITY → the day is skipped, R1). */
  dailyNorm: ReadonlyMap<string, number | null>
}

/**
 * S72 Step-7a W1 — R2's SINGLE computation owner. Diff(day) = (workTime periods
 * + manualHours + absence hours) − served norm. A day is ABSENT from the result
 * (renders BLANK) when its norm is null/missing (R1) or when it carries NO
 * registration at all — no workTime, no absence, no allocation (the R2 pinned
 * pro-handoff change). Per-day values round to 2 decimals. The grid's Diff row,
 * the grid's trailing total AND the Flex card's "Denne måned"
 * (useBalanceSummary.computeMonthFlexDelta) all consume THIS computation —
 * there is no copy to drift.
 */
export function computeDayDiffs(inputs: MonthDiffInputs): Map<string, number> {
  const diffs = new Map<string, number>()
  const daysInMonth = new Date(inputs.year, inputs.month, 0).getDate()
  for (let d = 1; d <= daysInMonth; d++) {
    const dateKey = `${inputs.year}-${String(inputs.month).padStart(2, '0')}-${String(d).padStart(2, '0')}`
    const norm = inputs.dailyNorm.get(dateKey)
    if (norm === null || norm === undefined) continue
    const intervals = inputs.workIntervals.get(dateKey) ?? []
    const worked = round2(
      periodsTotalHours(intervals.map((iv) => ({ from: iv.start, to: iv.end }))) +
        (inputs.manualHours.get(dateKey) ?? 0),
    )
    let absence = 0
    for (const key of inputs.absenceKeys) {
      absence += inputs.cellValues.get(`${key}:${dateKey}`) ?? 0
    }
    absence = round2(absence)
    let allocated = 0
    for (const key of inputs.projectKeys) {
      allocated += inputs.cellValues.get(`${key}:${dateKey}`) ?? 0
    }
    allocated = round2(allocated)
    if (worked === 0 && absence === 0 && allocated === 0) continue
    diffs.set(dateKey, round2(worked + absence - norm))
  }
  return diffs
}

/** R2 — the trailing Diff total / the Flex card's "Denne måned": Σ of the
    RENDERED per-day diffs (absent days contribute nothing), 2-decimal. */
export function computeMonthDiffTotal(diffs: ReadonlyMap<string, number>): number {
  let sum = 0
  for (const d of diffs.values()) sum += d
  return round2(sum)
}
