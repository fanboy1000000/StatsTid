// S72 / TASK-7205 — the Skema PAGE INTEGRATION (design_handoff_skema README §1):
// the redesigned grid (7202), the day-panel drawer (7203) and the manager modal
// (7204) wired over the existing domain machinery — month nav, the 4-card
// balance strip (R10 HYBRID sourcing), ComplianceWarnings (KEPT per R9), the
// existing approval footer, and the D-C 1480px page-local width cap.
//
// Pinned rules implemented here:
//   R2  — the Flex card's "Denne måned" derives from the SAME local month state
//         the grid renders, via computeMonthFlexDelta (useBalanceSummary) — it
//         reconciles with the grid's Diff total BY CONSTRUCTION (one source).
//   R3  — the grid/panel receive ALL served data; `rowPreferences` filters
//         rendering only; the hidden-rows affordance links to the modal.
//         S72 Step-7a B1: the arithmetic/row basis is deriveSkemaRowBasis's
//         UNION of `catalogs` ∪ keys in the served entries/absences — the
//         legacy `projects`/`absenceTypes` fields are the VISIBLE selection
//         for configured users (7201) and are never the basis on their own.
//   R5  — locked months (EMPLOYEE_APPROVED/APPROVED): grid readOnly, the day
//         panel unreachable, saves disabled; the manager modal STAYS reachable
//         (preferences are month-independent view state — ADR-012 locks
//         registrations, not preferences).
//   R11 — the modal opens from BOTH the header button AND the panel's step-2
//         link; modal actions apply LIVE to the grid (optimistic overlay +
//         server-truth refetch).
//   R16 — the page consumes the HOOK's buildWorkTimePayload (the page's former
//         private copy is DELETED — R7 manualHours preservation lives in the
//         hook); preference writes FLUSH pending debounced saves (cells AND
//         workTime) BEFORE the PUT + month refetch, so the refetch can never
//         clobber in-flight local state. S72 Step-7a B2: the flush is failure-
//         AND in-flight-safe — the debounce callbacks retain their save
//         promise in a ref, flushes await it, and persistRowPreferences ABORTS
//         before the PUT when a flush save failed (no silent loss).
//   R6  — S72 Step-7a W2: the day panel receives the LOCALLY-OVERLAID month
//         workTime (the same merged view the save path uses), so the §J
//         adjacent-day analysis sees locally-edited NEIGHBOR days.
//   R9  — AllocationSummary + ProjectPicker retired; ComplianceWarnings kept;
//         the legacy add-row wiring (onWorkIntervalsChange/onManualHoursChange
//         grid plumbing for the deleted "Tilføj periode"/"Tilføj timer" rows)
//         removed from the page side.
import { useState, useMemo, useCallback, useRef, useEffect, useLayoutEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useCalendarToday } from '../contexts/CalendarContext'
import { useSkema, buildWorkTimePayload, deriveSkemaRowBasis, periodHours } from '../hooks/useSkema'
import type { QuotaError, AbsenceRuleError, AbsenceCapError, ApprovalValidationError, SkemaRowBasis, SaveBatch, SkemaActionError } from '../hooks/useSkema'
import {
  SkemaGrid,
  type WorkInterval,
  type WorkIntervalsMap,
  type ManualHoursMap,
  type DailyNormMap,
  type ConsumptionBasisMap,
} from '../components/SkemaGrid'
import { SkemaDayPanel, type DayPanelPeriod, type DayPanelProjectRow } from '../components/SkemaDayPanel'
import { SkemaProjectManager } from '../components/SkemaProjectManager'
import { BalanceSummary } from '../components/BalanceSummary'
import { ComplianceWarnings } from '../components/ComplianceWarnings'
import {
  useBalanceSummary,
  computeMonthFlexDelta,
  deriveMonthAbsenceUsage,
} from '../hooks/useBalanceSummary'
import { useCompliance } from '../hooks/useCompliance'
import {
  putSkemaRowPreferences,
  toRowPreferencesPutBody,
  type SkemaRowPreferencesInvalidPayload,
} from '../lib/api'
import { Button } from '../components/ui/Button'
import { hasMinRole } from '../lib/roles'
import { formatMonthLabel } from '../lib/locale'
import { Badge } from '../components/ui/Badge'
import { Alert } from '../components/ui/Alert'
import { Spinner } from '../components/ui/Spinner'
import type {
  SkemaRowPreferences,
  SkemaRowPreferenceProject,
  SkemaRowPreferenceAbsenceType,
  SkemaCatalogs,
  WorkTimeDay,
} from '../types'
import styles from './SkemaPage.module.css'

// S147 QUAL-231 (A-1): ONE vocabulary per error family. This map is keyed by ENTITLEMENT TYPE
// only (the five EntitlementMapping.AbsenceToEntitlementType values) and serves the QUOTA alert
// alone, because the quota 422 sends the entitlement type in `absenceType` and an ENGLISH
// `message`. It holds no stored absence code: the rule alerts (full-day / not-eligible) take the
// server's Danish `message`, else the served catalogue's `label` for the stored code.
const ENTITLEMENT_LABELS: Record<string, string> = {
  VACATION: 'Ferie',
  SPECIAL_HOLIDAY: 'Særlige feriedage',
  CARE_DAY: 'Omsorgsdage',
  CHILD_SICK: 'Barns sygedag',
  SENIOR_DAY: 'Seniordage',
}

function formatQuotaError(q: QuotaError): string {
  const label = ENTITLEMENT_LABELS[q.absenceType] ?? q.absenceType
  const remaining = q.remaining.toFixed(1).replace('.', ',')
  const requested = q.requested.toFixed(1).replace('.', ',')
  return `Du har overskredet din kvote for ${label}. Du har ${remaining} dage tilbage, men forsøgte at registrere ${requested} dage.`
}

/** S73 R4 — the new save-422 families (`absence_full_day_only` /
    `absence_type_not_eligible`) → a comprehensible Danish alert.
    S147 QUAL-231 (A-1): the server's composed Danish `message` leads when present; the page
    adds its own sentence about the rolled-back value. With no `message`, the stored code is
    named by `labelFor` (the served Skema catalogue's label; the raw code if it is not there). */
function formatAbsenceRuleError(e: AbsenceRuleError, labelFor: (absenceType: string) => string): string {
  const rolledBack = 'Registreringen blev derfor sat tilbage.'
  if (e.message) return `${e.message} ${rolledBack}`
  const label = labelFor(e.absenceType)
  if (e.error === 'absence_full_day_only') {
    const required =
      e.requiredHours !== undefined ? `${e.requiredHours.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')} t` : null
    return required
      ? `${label} skal registreres som en hel dag (${required}). ${rolledBack}`
      : `${label} skal registreres som en hel dag. ${rolledBack}`
  }
  // absence_type_not_eligible
  return `Du er ikke berettiget til ${label} på denne dato. ${rolledBack}`
}

/** S123 / TASK-12303 — the daily-norm ABSENCE cap 422 (a day's summed absence
    hours exceed the norm) → a comprehensible Danish alert. */
function formatAbsenceCapError(e: AbsenceCapError): string {
  const total = e.totalHours.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
  const max = e.maxHours.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
  return `Det samlede fravær for dagen (${total} t) overstiger normtiden (${max} t). Registreringen blev derfor sat tilbage.`
}

function formatDanishDate(dateStr: string): string {
  try {
    const d = new Date(dateStr + 'T00:00:00')
    return d.toLocaleDateString('da-DK', { weekday: 'long', day: 'numeric', month: 'long' })
  } catch {
    return dateStr
  }
}

function formatHoursDa(h: number): string {
  return h.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
}

function formatApprovalValidationError(err: ApprovalValidationError): string[] {
  if (err.kind === 'allocation') {
    return err.unbalancedDays.map((d) => {
      const dato = formatDanishDate(d.date)
      if (d.direction === 'under') {
        const remaining = formatHoursDa(d.worked - d.allocated)
        return `Fordel de resterende ${remaining} t på projekter for ${dato}`
      }
      const excess = formatHoursDa(d.allocated - d.worked)
      return `Registrér arbejdstid for de ${excess} t for ${dato}`
    })
  }
  const daysList = err.missingDays.map(formatDanishDate).join(', ')
  return [
    `Ikke alle arbejdsdage er dækket (${err.coveredDays} af ${err.totalWorkdays}). Følgende dage mangler registreringer: ${daysList}`,
  ]
}

function formatDeadline(dateStr: string | null): string {
  if (!dateStr) return ''
  try {
    const d = new Date(dateStr)
    return d.toLocaleDateString('da-DK', { day: 'numeric', month: 'long', year: 'numeric' })
  } catch {
    return dateStr
  }
}

/**
 * S127 / TASK-12707 — the ONE frontend mirror of the backend's Skema lock set.
 *
 * The server locks a month iff its period is `EMPLOYEE_APPROVED` or `APPROVED`
 * (`SkemaEndpoints.IsPeriodLocked`); a null period — nothing sent — is NOT
 * locked. Sending now goes straight to `EMPLOYEE_APPROVED` in one call, so
 * "sent" and "locked" became the same moment: the intermediate `SUBMITTED` state
 * is only reachable for legacy rows, which stay editable and re-sendable.
 *
 * This exists because the page carried TWO encodings of the same rule that had
 * already drifted: `isReadOnly` negated the lock set correctly, while the footer
 * enumerated `DRAFT || SUBMITTED` positively — so an editable grid could sit
 * under a footer that offered no way to send it. Both now read this.
 */
function isPeriodLocked(status: string | null | undefined): boolean {
  return status === 'EMPLOYEE_APPROVED' || status === 'APPROVED'
}

/** Normalize a VALIDATED panel period's raw clock text ("9:05", "09.05", "0905",
    "HH:mm:ss") to the wire "HH:mm" the work-time save expects. Only called on
    periods periodHours() already accepted, so a null here is unreachable belt. */
function toHHmm(value: string): string | null {
  const m = String(value).trim().match(/^(\d{1,2})[.:]?(\d{2})(?::\d{2})?$/)
  if (!m) return null
  const h = Number(m[1])
  const mi = Number(m[2])
  if (h > 23 || mi > 59) return null
  return `${String(h).padStart(2, '0')}:${String(mi).padStart(2, '0')}`
}

/** S73 Step-7a B2 — value-equality of a single day's work-time (intervals +
    manualHours), used as the overlap guard for the work-time 422 revert: revert a
    day only when its CURRENT local value still equals what the rejected save SENT. */
function workTimeDayEquals(
  aIntervals: WorkInterval[], aManual: number,
  bIntervals: WorkInterval[], bManual: number,
): boolean {
  if (aManual !== bManual) return false
  if (aIntervals.length !== bIntervals.length) return false
  for (let i = 0; i < aIntervals.length; i++) {
    if (aIntervals[i].start !== bIntervals[i].start || aIntervals[i].end !== bIntervals[i].end) {
      return false
    }
  }
  return true
}

/** Danish framing for a failed action: names the month it belongs to (the person may have moved
    on since), then the server's message. */
function formatActionError(e: SkemaActionError): string {
  const monthText = formatMonthLabel(e.year, e.month).toLowerCase()
  switch (e.kind) {
    case 'save':
      return `Kunne ikke gemme (${monthText}): ${e.message}`
    case 'approve':
      return `Kunne ikke godkende måneden (${monthText}): ${e.message}`
    case 'reopen':
      return `Kunne ikke genåbne måneden (${monthText}): ${e.message}`
  }
}

/** A queued cell edit with everything needed to send it LATER, correctly: its own month
    envelope and its classification (absence vs project row), both fixed when it was QUEUED. */
interface PendingCell {
  rowKey: string
  date: string
  hours: number | null
  year: number
  month: number
  isAbsence: boolean
}
interface MonthEnv { year: number; month: number }
interface RetainedDay { year: number; month: number; day: WorkTimeDay }

/** How long a save waits for the previous one to settle before it proceeds anyway. Ordering is
    only guaranteed while requests settle within this bound; a hung request is NOT aborted or
    replayed (its outcome is unknown), it just stops holding up everything behind it. */
const SAVE_QUEUE_WAIT_MS = 15_000

/** Resolves when `p` settles (either way) or after `ms`, whichever is first. */
function waitBounded(p: Promise<unknown>, ms: number): Promise<void> {
  return new Promise<void>((resolve) => {
    const timer = setTimeout(resolve, ms)
    const done = () => {
      clearTimeout(timer)
      resolve()
    }
    p.then(done, done)
  })
}

export function SkemaPage() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''

  // Initial period: ?year=&month= from the Årsoversigt drill-in (year clamped to the
  // backend's supported 2000–2100 range — mirrors the year-overview endpoint's own
  // validation; an unclamped year ≥ 10000 throws in DateTime.DaysInMonth server-side
  // (Step-7a cycle-4 Codex) — month clamped 1..12), else today. Only seeds the initial
  // state — subsequent nav uses local setters.
  //
  // S143 / TASK-14303 — "today" is the server-confirmed Europe/Copenhagen business day from
  // `useCalendarToday()` (`contexts/CalendarContext.tsx`), NEVER `new Date()`. The chosen
  // year/month is not a display default: `useSkema` below sends it as the period envelope of
  // BOTH `POST /api/skema/{employeeId}/save` and `POST /api/approval/send` — the second of which
  // CREATES the approval period. A device in the wrong time zone (or with a wrong clock) used to
  // file a person's hours into, and submit for approval, the wrong month — silently. The
  // `today` string is already an authoritative `YYYY-MM-DD` calendar day; it is split into
  // year/month by string position, never re-parsed through a `Date`, so no zone conversion is
  // introduced here.
  const [searchParams] = useSearchParams()
  const today = useCalendarToday()
  const todayYear = Number(today.slice(0, 4))
  const todayMonth = Number(today.slice(5, 7))
  const paramYear = Number(searchParams.get('year'))
  const paramMonth = Number(searchParams.get('month'))
  const [year, setYear] = useState(
    Number.isInteger(paramYear) && paramYear >= 2000 && paramYear <= 2100
      ? paramYear
      : todayYear,
  )
  const [month, setMonth] = useState(
    Number.isInteger(paramMonth) && paramMonth >= 1 && paramMonth <= 12
      ? paramMonth
      : todayMonth,
  )

  // Every month load awaits the page's save queue first (set below, once the queue exists).
  const savesIdleRef = useRef<() => Promise<void>>(() => Promise.resolve())
  const awaitSavesIdle = useCallback(() => savesIdleRef.current(), [])
  const { data, loading, error, actionError, loadMismatch, clearActionError, quotaError, absenceRuleError, absenceCapError, approvalValidationError, clearQuotaError, clearAbsenceRuleError, clearAbsenceCapError, clearApprovalValidationError, refetch, saveBatch, employeeApprove, submitAndApprove, reopenPeriod } = useSkema(employeeId, year, month, awaitSavesIdle)
  // S127 / TASK-12707 — the `useAuth()` read here is gone with the send body it
  // fed: `orgId`/`agreementCode` were only ever arguments to the retired
  // `/api/approval/submit` payload. (`ApprovalFooter` keeps its own `useAuth()`
  // for the reopen role check.)
  const { data: balanceData, loading: balanceLoading } = useBalanceSummary(employeeId, year, month)
  const { result: complianceResult, loading: complianceLoading } = useCompliance(employeeId, year, month)

  // Local cell values for immediate editing
  const [localCells, setLocalCells] = useState<Map<string, number>>(new Map())
  // Live mirror for the save-retry path (Step-7a c3): a failed save must rebuild
  // its retry from the CURRENT local value, never its stale captured delta.
  const localCellsRef = useRef<Map<string, number>>(localCells)
  useEffect(() => {
    localCellsRef.current = localCells
  }, [localCells])
  // S73 R4 — server-truth cell map (the last refetched/successful month): a 422
  // rejection reverts the affected cells to THIS value. Built the same way as the
  // initial localCells, kept in a ref so the memoized fireCellSave reads it fresh.
  const serverCellsRef = useRef<Map<string, number>>(new Map())
  const pendingChangesRef = useRef<PendingCell[]>([])
  // Failed saves that belong to a month other than the one now held (or to data hydrated since),
  // kept WITH their own envelope so they are retried under their own month — never under the
  // month on screen — and never dropped by hydration while still unsaved. Keyed by cell / day;
  // any newer edit or send of the same cell / day supersedes the retained row.
  const retainedCellsRef = useRef<Map<string, PendingCell>>(new Map())
  const retainedDaysRef = useRef<Map<string, RetainedDay>>(new Map())
  const lastSendSeqRef = useRef<Map<string, number>>(new Map())
  /** The month of the data now held (set in the hydration layout effect). */
  const dataMonthRef = useRef<MonthEnv | null>(null)
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // S72 Step-7a B2 (+ the c2 settled-failure fix) — in-flight save promises
  // (cells / workTime): the debounce callbacks clear their pending state BEFORE
  // the POST resolves, so a flush must await (and read the outcome of) every
  // save that has already left — otherwise a preference PUT + refetch could
  // overtake an unresolved save and clobber it. SETS, not single slots: a newer
  // save must never evict an older unresolved one from the flush's view. A save
  // that settles with FAILURE RE-QUEUES its deltas into the pending structures
  // (the local edits still hold the values), so a later flush retries them and
  // returns false only while persistence genuinely failed — no silent loss, no
  // forgotten-failure window.
  const cellSavesInFlightRef = useRef<Set<Promise<boolean>>>(new Set())
  const workTimeSavesInFlightRef = useRef<Set<Promise<boolean>>>(new Set())

  // Build cell values from data (derived; copied into `localCells` DURING RENDER by the
  // S147 hydration block below, not by an effect).
  const cellsFromData = useMemo<Map<string, number>>(() => {
    const cells = new Map<string, number>()
    if (!data) return cells
    for (const entry of data.entries) {
      if (entry.hours !== 0) {
        // S120 — `projectCode` is NULLABLE on the wire (the spec truth the
        // hand-written type hid); the degenerate null key normalizes to '' —
        // the SAME normalization deriveSkemaRowBasis applies, so those hours
        // stay inside the Diff/I alt arithmetic.
        cells.set(`${entry.projectCode ?? ''}:${entry.date}`, entry.hours)
      }
    }
    for (const absence of data.absences) {
      if (absence.hours !== 0) {
        cells.set(`${absence.absenceType}:${absence.date}`, absence.hours)
      }
    }
    return cells
  }, [data])

  // The R3 arithmetic/row basis (S72 Step-7a B1): the UNION of the served
  // catalogs and every key in the month's served entries/absences — NOT the
  // legacy `projects`/`absenceTypes` fields, which are the VISIBLE selection
  // for configured users (7201). Visibility stays the grid's rowPreferences
  // rendering filter; deactivated projects with historical hours enter the
  // basis labeled by their code.
  const rowBasis = useMemo<SkemaRowBasis>(
    () =>
      data
        ? deriveSkemaRowBasis(data)
        : { rows: [], projectKeys: new Set<string>(), absenceKeys: new Set<string>() },
    [data],
  )
  const rows = rowBasis.rows

  // Build work intervals + manual hours maps from backend workTime
  const workIntervalsFromData = useMemo<WorkIntervalsMap>(() => {
    const map: WorkIntervalsMap = new Map()
    for (const wt of data?.workTime ?? []) {
      if (wt.intervals && wt.intervals.length > 0) {
        map.set(wt.date, wt.intervals.map(iv => ({ start: iv.start, end: iv.end })))
      }
    }
    return map
  }, [data])

  const manualHoursFromData = useMemo<ManualHoursMap>(() => {
    const map: ManualHoursMap = new Map()
    for (const wt of data?.workTime ?? []) {
      if (wt.manualHours && wt.manualHours !== 0) {
        map.set(wt.date, wt.manualHours)
      }
    }
    return map
  }, [data])

  // Daily norm map (per-day; null -> blank)
  const dailyNorm = useMemo<DailyNormMap>(() => {
    const map: DailyNormMap = new Map()
    for (const dn of data?.dailyNorm ?? []) {
      map.set(dn.date, dn.hours)
    }
    return map
  }, [data])

  // S73 R5 — the served per-day ADR-032 consumption basis (the full-day snap
  // value; null = no dated profile covers the day → no snap, server fail-closes).
  const consumptionBasis = useMemo<ConsumptionBasisMap>(() => {
    const map: ConsumptionBasisMap = new Map()
    for (const cb of data?.consumptionBasis ?? []) {
      map.set(cb.date, cb.hours)
    }
    return map
  }, [data])

  // Local editable work-time state (intervals + manual hours; D-B: manual hours
  // have no entry UI anymore but existing values keep counting and round-trip)
  const [localWorkIntervals, setLocalWorkIntervals] = useState<WorkIntervalsMap>(new Map())
  const [localManualHours, setLocalManualHours] = useState<ManualHoursMap>(new Map())

  // Live refs so the debounced work-time save reads the latest local state
  const workIntervalsRef = useRef<WorkIntervalsMap>(localWorkIntervals)
  const manualHoursRef = useRef<ManualHoursMap>(localManualHours)
  useEffect(() => { workIntervalsRef.current = localWorkIntervals }, [localWorkIntervals])
  useEffect(() => { manualHoursRef.current = localManualHours }, [localManualHours])

  // S73 Step-7a B2 — server-truth work-time maps (the last refetched/successful
  // month): a work-time 422 reverts the affected day's intervals + manualHours to
  // THESE values (the mirror of serverCellsRef for cells). Kept in refs so the
  // memoized fireWorkTimeSave reads them fresh without re-binding.
  const serverWorkIntervalsRef = useRef<WorkIntervalsMap>(new Map())
  const serverManualHoursRef = useRef<ManualHoursMap>(new Map())

  // Send-order protection for the 422-revert truth: a per-key sequence number is taken
  // when a save is SENT; a successful save advances server truth for a key only when its
  // number is newer than the one that last advanced it, so an older save completing after
  // a newer one can never move truth backwards. `hydrationEpochRef` bumps whenever fresh
  // data is hydrated: a save result that returns from an earlier epoch (another month, or
  // before a refetch) must not touch the current data's local or server state.
  const saveSeqRef = useRef(0)
  const cellAdvancedSeqRef = useRef<Map<string, number>>(new Map())
  const workTimeAdvancedSeqRef = useRef<Map<string, number>>(new Map())
  const hydrationEpochRef = useRef(0)

  const workTimeDirtyRef = useRef<Set<string>>(new Set())
  const workTimeSaveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // S147 / QUAL-241 — rehydrate ALL editable local state (cells, work intervals, manual
  // hours) from the served data DURING RENDER ("adjust state while rendering": React
  // discards this render and re-renders at once, before anything is committed). This used
  // to be three effects, which left one committed render in which the grid and every day
  // trigger were live while local state was still empty (first load) or still the previous
  // month's (month change / refetch). A day panel opened in that gap seeded no periods and
  // a period edit there replaced the day's stored intervals with the partial set. With the
  // copy done in render, no committed render exists whose local state is not the current
  // `data`, so there is nothing to gate and no delay to show.
  const [hydratedFrom, setHydratedFrom] = useState<typeof data>(null)
  if (data !== hydratedFrom) {
    setHydratedFrom(data)
    setLocalCells(cellsFromData)
    setLocalWorkIntervals(workIntervalsFromData)
    setLocalManualHours(manualHoursFromData)
  }

  // The ref-side of hydration (server truth for the 422 revert, the pending-save queues, the
  // send-order bookkeeping, the epoch) belongs to the data it came from too, but a ref write is
  // a side effect and must not happen in render. A layout effect runs synchronously inside the
  // commit of the hydrated render — before the browser can paint and before any event handler
  // or passive effect can run — so nothing can observe the new state with the old refs. This
  // holds for THIS tree; it would not if a child focused an element (autoFocus / focus() in its
  // own layout phase) and an event handler ran from that, since child layout effects run before
  // this one. (The state copy above must stay in render; that is what removes the un-hydrated
  // commit. Fresh Map copies, so a later save-success mutation never aliases the memoized
  // "from data" maps.)
  //
  // Pending edits that belong to ANOTHER month than the data now held are not stale: they are
  // moved to the retained store with their own envelope instead of being dropped.
  useLayoutEffect(() => {
    const previous = dataMonthRef.current
    const next: MonthEnv | null = data ? { year: data.year, month: data.month } : null
    const sameAsNext = (e: MonthEnv) => next != null && e.year === next.year && e.month === next.month
    for (const c of pendingChangesRef.current) {
      if (!sameAsNext(c)) retainedCellsRef.current.set(`${c.rowKey}:${c.date}`, c)
    }
    if (previous && !sameAsNext(previous) && workTimeDirtyRef.current.size > 0) {
      // The refs still hold the PREVIOUS month's local state here (they are mirrored by passive
      // effects, which have not run for this commit), so the dirty days can be frozen now.
      for (const row of buildWorkTimePayload(workTimeDirtyRef.current, workIntervalsRef.current, manualHoursRef.current)) {
        retainedDaysRef.current.set(row.date, { year: previous.year, month: previous.month, day: row })
      }
    }
    dataMonthRef.current = next
    serverCellsRef.current = new Map(cellsFromData)
    serverWorkIntervalsRef.current = new Map(workIntervalsFromData)
    serverManualHoursRef.current = new Map(manualHoursFromData)
    pendingChangesRef.current = []
    workTimeDirtyRef.current = new Set()
    cellAdvancedSeqRef.current = new Map()
    workTimeAdvancedSeqRef.current = new Map()
    hydrationEpochRef.current += 1
  }, [data, hydratedFrom, cellsFromData, workIntervalsFromData, manualHoursFromData])

  // S147 / QUAL-241 fix-up 1 — the edit surfaces are locked while the displayed month and
  // the held `data` disagree. useSkema keeps the PREVIOUS month's data on screen while the
  // next month loads, and on a FAILED fetch it keeps it for good (loading drops to false),
  // so the test is month IDENTITY, never `loading`: otherwise a failed fetch would unlock
  // the old month's data under the new month's day columns. `busyCount` is the second
  // lock: the row-preference sequence and approve/send (flush → request → reload of the SAME
  // month) re-hydrate local state from the reloaded response, which would overwrite anything
  // typed while they were in flight — we lock rather than merge, because the flush before the
  // request has just persisted everything, so nothing is lost and there is no merge logic to
  // get wrong. It is a COUNTER, not a flag: overlapping sequences (two modal actions in a row)
  // must keep the lock until the LAST one has finished.
  const monthMismatch = data != null && (data.year !== year || data.month !== month)
  const [busyCount, setBusyCount] = useState(0)
  const busy = busyCount > 0
  const editLocked = monthMismatch || busy
  const withBusy = useCallback(async <T,>(work: () => Promise<T>): Promise<T> => {
    setBusyCount((c) => c + 1)
    try {
      return await work()
    } finally {
      setBusyCount((c) => c - 1)
    }
  }, [])


  // Debounced work-time save (intervals + the preserved manual hours). The
  // payload ALWAYS builds through the hook's buildWorkTimePayload — the single
  // R16/R7 owner (manualHours carry-forward is enforced and tested THERE).

  /** Serial save queue — ONE save POST in flight at a time; the next leaves when the previous
      has settled. The server applies saves in arrival order, so this is what makes "the
      server's last write is our last write" true and what lets the 422-revert truth be
      advanced strictly in send order. Every save carries its own fully classified payload and
      month (fixed when it was queued), so a save made in March still goes to March, classified
      as it was then. A rejected or failed save never blocks the queue.
      ORDERING IS ONLY GUARANTEED WHILE REQUESTS SETTLE WITHIN SAVE_QUEUE_WAIT_MS: a hung POST
      cannot be told from a slow one, so after the bound the next save proceeds (the hung one is
      neither aborted nor replayed — its outcome is unknown). */
  const saveTailRef = useRef<Promise<unknown>>(Promise.resolve())
  const saveSerial = useCallback(
    (batch: SaveBatch) => {
      const run = waitBounded(saveTailRef.current, SAVE_QUEUE_WAIT_MS).then(() => saveBatch(batch))
      saveTailRef.current = run.then(() => undefined, () => undefined)
      return run
    },
    [saveBatch],
  )
  // A month load is ordered after every queued save (bounded the same way).
  useLayoutEffect(() => {
    savesIdleRef.current = () => waitBounded(saveTailRef.current, SAVE_QUEUE_WAIT_MS)
  }, [])

  /** Send frozen work-time rows under their own month. On FAILURE the rows are re-queued: for
      the month still on screen the dates go back to the dirty set (the local refs still hold
      the edits, so a retry rebuilds from live state); otherwise the frozen rows are retained
      with their envelope. */
  const sendWorkTime = useCallback((env: MonthEnv, payload: WorkTimeDay[]): Promise<boolean> => {
    if (payload.length === 0) return Promise.resolve(true)
    const dates = payload.map((r) => r.date)
    const epoch = hydrationEpochRef.current
    const seq = ++saveSeqRef.current
    // What each date SENDS (the frozen rows), so a 422 revert can be overlap-guarded: revert a
    // day ONLY when its CURRENT local work-time still equals what this save sent.
    const sentByDate = new Map(payload.map((r) => [r.date, { intervals: r.intervals, manualHours: r.manualHours }]))
    for (const d of dates) {
      lastSendSeqRef.current.set(`d:${d}`, seq)
      retainedDaysRef.current.delete(d)
    }
    const save = saveSerial({ year: env.year, month: env.month, entries: null, absences: null, workTime: payload }).then((result) => {
      const current = dataMonthRef.current
      const here =
        current != null && current.year === env.year && current.month === env.month && epoch === hydrationEpochRef.current
      if (!here) {
        // Not the data now held (month navigation, refetch): never touch the current local or
        // server-truth state with it. A failure is RETAINED with its own envelope.
        if (result.status !== 'failed') return true
        for (const row of payload) {
          if (lastSendSeqRef.current.get(`d:${row.date}`) === seq) {
            retainedDaysRef.current.set(row.date, { year: env.year, month: env.month, day: row })
          }
        }
        return false
      }
      if (result.status === 'rejected') {
        // 422 — REVERT each affected day's work-time to server truth (mirror of
        // fireCellSave). The alert is already surfaced by the hook; the revert
        // is what makes "422 resolved" honest (the rejected edit no longer lingers
        // for a later preference flush / refetch to silently discard). Counts
        // RESOLVED (the day reverted → nothing stale).
        const revertDates: string[] = []
        for (const d of dates) {
          const sent = sentByDate.get(d)!
          // Overlap guard: skip a day a NEWER edit has changed since the send.
          if (!workTimeDayEquals(
            sent.intervals, sent.manualHours,
            workIntervalsRef.current.get(d) ?? [],
            manualHoursRef.current.get(d) ?? 0,
          )) continue
          revertDates.push(d)
        }
        if (revertDates.length > 0) {
          setLocalWorkIntervals((prev) => {
            const next = new Map(prev)
            for (const d of revertDates) {
              const truth = serverWorkIntervalsRef.current.get(d)
              if (truth && truth.length > 0) next.set(d, truth.map((iv) => ({ ...iv })))
              else next.delete(d)
            }
            return next
          })
          setLocalManualHours((prev) => {
            const next = new Map(prev)
            for (const d of revertDates) {
              const truth = serverManualHoursRef.current.get(d)
              if (truth && truth !== 0) next.set(d, truth)
              else next.delete(d)
            }
            return next
          })
        }
        return true // resolved: reverted to server truth.
      }
      if (result.status === 'ok') {
        // S73 Step-7a cycle-2 B2 — ADVANCE server truth on a SUCCESSFUL save, so a later 422
        // reverts to the last KNOWN-GOOD server state, not the originally loaded one. Saves
        // settle strictly in send order (the serial queue); the per-day send-order number is a
        // belt-and-braces guard.
        for (const d of dates) {
          const sent = sentByDate.get(d)!
          if ((workTimeAdvancedSeqRef.current.get(d) ?? 0) > seq) continue
          workTimeAdvancedSeqRef.current.set(d, seq)
          if (sent.intervals.length > 0)
            serverWorkIntervalsRef.current.set(d, sent.intervals.map((iv) => ({ ...iv })))
          else serverWorkIntervalsRef.current.delete(d)
          if (sent.manualHours !== 0)
            serverManualHoursRef.current.set(d, sent.manualHours)
          else serverManualHoursRef.current.delete(d)
        }
        return true
      }
      // status 'failed' (400/401/403/404/409/5xx/network) — re-queue the dirty
      // dates (B2 retry; the local refs still hold the edits).
      for (const d of dates) workTimeDirtyRef.current.add(d)
      return false
    })
    workTimeSavesInFlightRef.current.add(save)
    void save.finally(() => {
      workTimeSavesInFlightRef.current.delete(save)
    })
    return save
  }, [saveSerial])

  /** Fire a workTime save for the given dirty dates (the month on screen; the payload is built
      from the live refs NOW). */
  const fireWorkTimeSave = useCallback((dates: string[]): Promise<boolean> => {
    const env = dataMonthRef.current
    if (!env) return Promise.resolve(true)
    return sendWorkTime(env, buildWorkTimePayload(dates, workIntervalsRef.current, manualHoursRef.current))
  }, [sendWorkTime])

  /** Send every retained (failed, other-month) work-time row under its own month. */
  const sendRetainedDays = useCallback((): Promise<boolean> => {
    const groups = new Map<string, { env: MonthEnv; rows: WorkTimeDay[] }>()
    for (const r of retainedDaysRef.current.values()) {
      const k = `${r.year}-${r.month}`
      const g = groups.get(k) ?? { env: { year: r.year, month: r.month }, rows: [] }
      g.rows.push(r.day)
      groups.set(k, g)
    }
    retainedDaysRef.current = new Map()
    return Promise.all([...groups.values()].map((g) => sendWorkTime(g.env, g.rows))).then((rs) => rs.every(Boolean))
  }, [sendWorkTime])

  /** Flush pending debounced WORK-TIME saves (B2: fires any pending debounce
      immediately AND awaits the in-flight POST). Returns false when any
      involved save failed — callers must not refetch over unsaved state. */
  const flushWorkTimeSave = useCallback(async (): Promise<boolean> => {
    if (workTimeSaveTimerRef.current) {
      clearTimeout(workTimeSaveTimerRef.current)
      workTimeSaveTimerRef.current = null
    }
    // Await every POST that already left (B2: sets, so no save is evicted) —
    // their outcomes count toward the flush result; failures re-queue, so the
    // retry below picks them up in the same flush.
    const inFlight = [...workTimeSavesInFlightRef.current]
    const inFlightOk = (await Promise.all(inFlight)).every(Boolean)
    const retained = sendRetainedDays()
    if (workTimeDirtyRef.current.size === 0) return inFlightOk && (await retained)
    const dates = [...workTimeDirtyRef.current]
    workTimeDirtyRef.current = new Set()
    const ok = await fireWorkTimeSave(dates)
    return inFlightOk && ok && (await retained)
  }, [fireWorkTimeSave, sendRetainedDays])

  const scheduleWorkTimeSave = useCallback((date: string) => {
    workTimeDirtyRef.current.add(date)
    if (workTimeSaveTimerRef.current) clearTimeout(workTimeSaveTimerRef.current)
    workTimeSaveTimerRef.current = setTimeout(() => {
      workTimeSaveTimerRef.current = null
      const dates = [...workTimeDirtyRef.current]
      workTimeDirtyRef.current = new Set()
      void fireWorkTimeSave(dates)
    }, 1000)
  }, [fireWorkTimeSave])

  const handleWorkIntervalsChange = useCallback(
    (date: string, intervals: WorkInterval[]) => {
      if (editLocked) return
      setLocalWorkIntervals(prev => {
        const next = new Map(prev)
        if (intervals.length === 0) {
          next.delete(date)
        } else {
          next.set(date, intervals)
        }
        return next
      })
      scheduleWorkTimeSave(date)
    },
    [scheduleWorkTimeSave, editLocked]
  )

  // Approval status
  const approvalStatus = data?.approval?.status ?? 'DRAFT'
  // The approved/submitted-month lock (the S147 month-identity / prefs locks are `editLocked`).
  const isPeriodReadOnly = isPeriodLocked(approvalStatus)
  const isReadOnly = isPeriodReadOnly || editLocked
  // The day panel closes only for the two "wrong data" reasons, not for a prefs sequence
  // (the manager modal opens FROM the panel; closing it per action would lose the context).
  const panelBlocked = isPeriodReadOnly || monthMismatch

  /** S73 R4 — fire a cell save for the given changes and split the rejected-save
      policy INSIDE the failure handler:

      • status 'ok'        → resolved (returns true).
      • status 'rejected'  (HTTP 422 ONLY) → REVERT the affected cells to server
        truth + surface the alert; counts RESOLVED (returns true) so the flush
        proceeds (dropped + reverted leaves nothing stale). The revert is GUARDED
        by the S72-c3 overlap shape: a cell reverts ONLY when its CURRENT local
        value still equals the value the rejected save SENT (and no newer pending
        edit owns it) — a newer edit keeps the cell.
      • status 'failed'    (400/401/403/404/409/5xx/network) → KEEP the edits + the B2
        re-queue/retry posture (returns false). 401/403 are credential-shaped — a
        mid-edit session expiry must never discard typed cells. For the month still on
        screen the re-queue rebuilds FROM LIVE LOCAL STATE (Step-7a c2/c3), never the
        stale captured delta, and never over a NEWER pending entry (a cleared cell, live
        undefined, is not re-queued — the R17 inherited limitation). For any other month
        (the person navigated on) the frozen rows are RETAINED with their own envelope.

      A change set is split by month; each group is one POST under its own envelope, with
      the classification (absence vs project) fixed when the edit was queued. */
  const fireCellGroup = useCallback((changes: PendingCell[]): Promise<boolean> => {
    const env: MonthEnv = { year: changes[0].year, month: changes[0].month }
    const epoch = hydrationEpochRef.current
    const seq = ++saveSeqRef.current
    const entries: NonNullable<SaveBatch['entries']> = []
    const absences: NonNullable<SaveBatch['absences']> = []
    for (const c of changes) {
      lastSendSeqRef.current.set(`c:${c.rowKey}:${c.date}`, seq)
      retainedCellsRef.current.delete(`${c.rowKey}:${c.date}`)
      if (c.hours == null || c.hours === 0) continue
      if (c.isAbsence) absences.push({ date: c.date, absenceType: c.rowKey, hours: c.hours })
      else entries.push({ date: c.date, projectCode: c.rowKey, hours: c.hours })
    }
    const save = saveSerial({ year: env.year, month: env.month, entries, absences, workTime: null }).then((result) => {
      const current = dataMonthRef.current
      const here =
        current != null && current.year === env.year && current.month === env.month && epoch === hydrationEpochRef.current
      if (!here) {
        // Not the data now held: never touch the current local or server-truth state with it.
        if (result.status !== 'failed') return true
        for (const c of changes) {
          const stillLatest = lastSendSeqRef.current.get(`c:${c.rowKey}:${c.date}`) === seq
          const hasNewerPending = pendingChangesRef.current.some((p) => p.rowKey === c.rowKey && p.date === c.date)
          if (stillLatest && !hasNewerPending) retainedCellsRef.current.set(`${c.rowKey}:${c.date}`, c)
        }
        return false
      }
      if (result.status === 'ok') {
        // S73 Step-7a cycle-2 B2 — ADVANCE server truth on a SUCCESSFUL cell save (symmetric
        // to the work-time fix), even when a newer local edit exists. Saves settle in send
        // order (the serial queue); the per-cell number is a belt-and-braces guard. A clear
        // (null/0) sets server truth to absent.
        for (const c of changes) {
          const cellKey = `${c.rowKey}:${c.date}`
          const sent = c.hours === null || c.hours === 0 ? undefined : c.hours
          if ((cellAdvancedSeqRef.current.get(cellKey) ?? 0) > seq) continue
          cellAdvancedSeqRef.current.set(cellKey, seq)
          if (sent === undefined) serverCellsRef.current.delete(cellKey)
          else serverCellsRef.current.set(cellKey, sent)
        }
        return true
      }
      if (result.status === 'rejected') {
        // 422 — revert the affected cells to server truth, overlap-guarded.
        const reverts: { rowKey: string; date: string; hours: number | null }[] = []
        for (const c of changes) {
          const hasNewer = pendingChangesRef.current.some(
            (p) => p.rowKey === c.rowKey && p.date === c.date,
          )
          if (hasNewer) continue
          const cellKey = `${c.rowKey}:${c.date}`
          const live = localCellsRef.current.get(cellKey)
          // The overlap guard: revert ONLY when the live value still equals what
          // was SENT (a newer edit owns the cell otherwise). `c.hours` null/0
          // means the sent value was a clear — compare against an absent live.
          const sent = c.hours === null || c.hours === 0 ? undefined : c.hours
          if (live !== sent) continue
          const truth = serverCellsRef.current.get(cellKey)
          reverts.push({ rowKey: c.rowKey, date: c.date, hours: truth ?? null })
        }
        if (reverts.length > 0) {
          setLocalCells((prev) => {
            const next = new Map(prev)
            for (const r of reverts) {
              const key = `${r.rowKey}:${r.date}`
              if (r.hours === null) next.delete(key)
              else next.set(key, r.hours)
            }
            return next
          })
        }
        // Resolved: the rejected deltas dropped + reverted → nothing stale.
        return true
      }
      // status 'failed' — re-queue FROM LIVE LOCAL STATE (B2), retryable, keeping the row's
      // own envelope and classification.
      for (const c of changes) {
        const hasNewer = pendingChangesRef.current.some(
          (p) => p.rowKey === c.rowKey && p.date === c.date,
        )
        if (hasNewer) continue
        const live = localCellsRef.current.get(`${c.rowKey}:${c.date}`)
        if (live === undefined) continue
        pendingChangesRef.current.push({ ...c, hours: live })
      }
      return false
    })
    cellSavesInFlightRef.current.add(save)
    void save.finally(() => {
      cellSavesInFlightRef.current.delete(save)
    })
    return save
  }, [saveSerial])

  const fireCellSave = useCallback((changes: PendingCell[]): Promise<boolean> => {
    const groups = new Map<string, PendingCell[]>()
    for (const c of changes) {
      const k = `${c.year}-${c.month}`
      const g = groups.get(k)
      if (g) g.push(c)
      else groups.set(k, [c])
    }
    return Promise.all([...groups.values()].map(fireCellGroup)).then((rs) => rs.every(Boolean))
  }, [fireCellGroup])

  /** The absence-type keys of the data held — how a cell is classified when it is QUEUED. */
  const absenceTypeSet = useMemo(() => new Set((data?.absenceTypes ?? []).map((a) => a.type)), [data])

  // Debounced cell save (grid inline edits + the panel's step-2 allocations)
  const handleCellChange = useCallback(
    (rowKey: string, date: string, hours: number | null) => {
      if (editLocked) return
      setLocalCells((prev) => {
        const next = new Map(prev)
        if (hours === null) {
          next.delete(`${rowKey}:${date}`)
        } else {
          next.set(`${rowKey}:${date}`, hours)
        }
        return next
      })

      // Track pending change — with its own envelope and classification, fixed NOW.
      const row: PendingCell = { rowKey, date, hours, year, month, isAbsence: absenceTypeSet.has(rowKey) }
      retainedCellsRef.current.delete(`${rowKey}:${date}`) // a newer edit supersedes a retained retry
      const idx = pendingChangesRef.current.findIndex(
        (c) => c.rowKey === rowKey && c.date === date
      )
      if (idx >= 0) {
        pendingChangesRef.current[idx] = row
      } else {
        pendingChangesRef.current.push(row)
      }

      // Debounce save
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current)
      }
      saveTimerRef.current = setTimeout(() => {
        saveTimerRef.current = null
        const changes = [...pendingChangesRef.current]
        pendingChangesRef.current = []
        if (changes.length === 0) return
        void fireCellSave(changes)
      }, 1000)
    },
    [fireCellSave, editLocked, year, month, absenceTypeSet]
  )

  /** Flush any pending debounced CELL saves (used before approve and before
      preference writes — R16). B2: fires the pending debounce immediately AND
      awaits the in-flight POST; returns false when any involved save failed. */
  const flushCellSave = useCallback(async (): Promise<boolean> => {
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current)
      saveTimerRef.current = null
    }
    // Await every POST that already left (B2: sets, so no save is evicted) —
    // failures re-queue, so the retry below picks them up in the same flush.
    const inFlight = [...cellSavesInFlightRef.current]
    const inFlightOk = (await Promise.all(inFlight)).every(Boolean)
    const pending = [...pendingChangesRef.current, ...retainedCellsRef.current.values()]
    pendingChangesRef.current = []
    retainedCellsRef.current = new Map()
    if (pending.length === 0) return inFlightOk
    const ok = await fireCellSave(pending)
    return inFlightOk && ok
  }, [fireCellSave])

  // On unmount (leaving the page) the pending debounced saves are SENT, not dropped — the
  // person typed them. `firePendingNow` also clears both timers. (It is defined below, so it
  // is reached through a ref that is kept current each render.)
  const firePendingNowRef = useRef<() => void>(() => {})
  useEffect(() => {
    return () => {
      firePendingNowRef.current()
    }
  }, [])

  /** S147 / QUAL-241 fix-up 1 — fire every pending debounced save NOW, synchronously, from
      the refs as they are at this instant, through the current month's `saveMonth`. Month
      navigation calls it BEFORE changing the month: the debounce timers would otherwise
      fire after the next month hydrated, build the old day's payload from refs that no
      longer hold it and send {intervals: [], manualHours: 0} — the whole-day replace
      would wipe the stored times. (Unlike flushWorkTimeSave this does not first await the
      in-flight POSTs: the awaits are what let the month change slip in before the read.) */
  const firePendingNow = useCallback(() => {
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current)
      saveTimerRef.current = null
    }
    const changes = [...pendingChangesRef.current, ...retainedCellsRef.current.values()]
    pendingChangesRef.current = []
    retainedCellsRef.current = new Map()
    if (changes.length > 0) void fireCellSave(changes)
    if (workTimeSaveTimerRef.current) {
      clearTimeout(workTimeSaveTimerRef.current)
      workTimeSaveTimerRef.current = null
    }
    const dates = [...workTimeDirtyRef.current]
    workTimeDirtyRef.current = new Set()
    if (dates.length > 0) void fireWorkTimeSave(dates)
    void sendRetainedDays()
  }, [fireCellSave, fireWorkTimeSave, sendRetainedDays])

  useEffect(() => {
    firePendingNowRef.current = firePendingNow
  })

  // Month navigation. The next year/month are computed OUTSIDE any state updater (an updater
  // that calls another setter is impure and double-invokes under StrictMode).
  const goToPrevMonth = useCallback(() => {
    firePendingNow()
    if (month === 1) {
      setYear(year - 1)
      setMonth(12)
    } else {
      setMonth(month - 1)
    }
  }, [firePendingNow, month, year])

  const goToNextMonth = useCallback(() => {
    firePendingNow()
    if (month === 12) {
      setYear(year + 1)
      setMonth(1)
    } else {
      setMonth(month + 1)
    }
  }, [firePendingNow, month, year])

  // ── Day panel (7203) — page-controlled (R16): the page owns the open day's
  // period rows (raw text) and translates VALID rows into the existing debounced
  // workTime save path; allocations ride the existing cell-save path. ──
  const [openDayDate, setOpenDayDate] = useState<string | null>(null)
  const [panelPeriods, setPanelPeriods] = useState<DayPanelPeriod[]>([])
  const panelIdRef = useRef(0)

  const handleOpenDay = useCallback((date: string) => {
    // S147 / QUAL-241 — seed from the committed STATE (which the render-time hydration
    // above guarantees is the current data's), not from `workIntervalsRef`, which is only
    // mirrored by an effect after commit.
    const intervals = localWorkIntervals.get(date) ?? []
    setPanelPeriods(
      intervals.map((iv) => ({ id: `srv-${++panelIdRef.current}`, from: iv.start, to: iv.end })),
    )
    setOpenDayDate(date)
  }, [localWorkIntervals])

  // Month nav drops any open day (the date belongs to the previous view); a
  // month that locks (approve) closes the panel — R5: the panel is unreachable
  // on locked months (the grid renders no triggers either).
  useEffect(() => { setOpenDayDate(null) }, [year, month])
  useEffect(() => { if (panelBlocked) setOpenDayDate(null) }, [panelBlocked])

  const handlePanelPeriodsChange = useCallback(
    (date: string, periods: DayPanelPeriod[]) => {
      if (editLocked) return
      setPanelPeriods(periods)
      // Persist only VALID periods (invalid/mid-typing rows render "ugyldig" in
      // the panel and are excluded; the work-time save is a latest-wins whole-day
      // replace, so every change re-sends the full valid set). The save goes
      // through the existing debounced workTime path → the hook's
      // buildWorkTimePayload (R16) which carries the day's manualHours (R7).
      const intervals: WorkInterval[] = []
      for (const p of periods) {
        if (periodHours(p) === null) continue
        const start = toHHmm(p.from)
        const end = toHHmm(p.to)
        if (start !== null && end !== null) intervals.push({ start, end })
      }
      handleWorkIntervalsChange(date, intervals)
    },
    [handleWorkIntervalsChange, editLocked],
  )

  const handlePanelAllocationChange = useCallback(
    (date: string, projectKey: string, hours: number | null) => {
      handleCellChange(projectKey, date, hours)
    },
    [handleCellChange],
  )

  // ── Manager modal (7204) — per-action persistence with the R16 sequence ──
  const [managerOpen, setManagerOpen] = useState(false)
  const [rowPrefsError, setRowPrefsError] = useState<SkemaRowPreferencesInvalidPayload | null>(null)
  const [prefsSaveError, setPrefsSaveError] = useState<string | null>(null)
  // Optimistic overlay: the modal/grid update LIVE per action (R11); server
  // truth (the refetched month) replaces the overlay when it lands.
  const [optimisticPrefs, setOptimisticPrefs] = useState<SkemaRowPreferences | null>(null)
  useEffect(() => { setOptimisticPrefs(null) }, [data])

  const effectivePrefs = useMemo<SkemaRowPreferences>(() => {
    if (optimisticPrefs) return optimisticPrefs
    if (data?.rowPreferences) return data.rowPreferences
    // Pre-7201 fallback shape: every served row in served order — renders
    // identically to the grid's own no-preferences fallback (R12).
    return {
      configured: false,
      projects: (data?.projects ?? []).map((p, i) => ({
        projectId: p.projectId,
        projectCode: p.projectCode,
        projectName: p.projectName,
        sortOrder: i,
      })),
      absenceTypes: (data?.absenceTypes ?? []).map((a, i) => ({
        type: a.type,
        label: a.label,
        // S120 — the spec row-preference row REQUIRES the flag; thread the
        // served value through (rendering-only — the wire body drops it).
        fullDayOnly: a.fullDayOnly,
        sortOrder: i,
      })),
    }
  }, [optimisticPrefs, data])

  const catalogs = useMemo<SkemaCatalogs>(
    () =>
      data?.catalogs ?? {
        projects: data?.projects ?? [],
        absenceTypes: data?.absenceTypes ?? [],
      },
    [data],
  )

  const runPrefsSequence = useCallback(
    async (next: SkemaRowPreferences) => {
      setRowPrefsError(null)
      setPrefsSaveError(null)
      setOptimisticPrefs(next) // live grid/modal update (R11)
      // R16 sequence (failure- and in-flight-safe — S72 Step-7a B2):
      // (1) FLUSH pending debounced saves — cells AND workTime — firing any
      //     pending debounce immediately and awaiting in-flight POSTs; ABORT
      //     before the PUT when a flush save failed (no silent loss, and no
      //     refetch to clobber the unsaved local edits);
      // (2) ONE PUT; (3) refetch the month (the grid updates from server truth).
      // The whole sequence runs under the busy lock (edits locked until the refetched
      // month has hydrated).
      const cellsFlushed = await flushCellSave()
      const workTimeFlushed = await flushWorkTimeSave()
      if (!cellsFlushed || !workTimeFlushed) {
        setOptimisticPrefs(null) // revert — nothing was persisted
        setPrefsSaveError(
          'ventende registreringer kunne ikke gemmes, så rækkerne blev ikke ændret. Prøv igen.',
        )
        return
      }
      const result = await putSkemaRowPreferences(
        employeeId,
        toRowPreferencesPutBody(next.projects, next.absenceTypes),
      )
      if (!result.ok) {
        setOptimisticPrefs(null) // revert — the server rejected the replacement
        if (result.invalid) {
          setRowPrefsError(result.invalid) // 422 offender list → the modal's Alert
        } else {
          setPrefsSaveError(result.error)
        }
        return
      }
      await refetch()
    },
    [employeeId, flushCellSave, flushWorkTimeSave, refetch],
  )

  // Preference sequences run ONE AT A TIME (flush → PUT → refetch), in click order, so the final
  // server row set is the last action's and never depends on which PUT arrived last. The
  // optimistic overlay is set at once (the modal updates live); the whole thing runs under the
  // busy lock, which is a counter.
  const prefsTailRef = useRef<Promise<unknown>>(Promise.resolve())
  const persistRowPreferences = useCallback(
    (next: SkemaRowPreferences) =>
      withBusy(async () => {
        setOptimisticPrefs(next)
        const turn = prefsTailRef.current
        let finish!: () => void
        prefsTailRef.current = new Promise<void>((r) => { finish = r })
        try {
          await turn
          await runPrefsSequence(next)
        } finally {
          finish()
        }
      }),
    [withBusy, runPrefsSequence],
  )

  const handleProjectsChange = useCallback(
    (next: SkemaRowPreferenceProject[]) => {
      void persistRowPreferences({ ...effectivePrefs, configured: true, projects: next })
    },
    [effectivePrefs, persistRowPreferences],
  )

  const handleAbsenceTypesChange = useCallback(
    (next: SkemaRowPreferenceAbsenceType[]) => {
      void persistRowPreferences({ ...effectivePrefs, configured: true, absenceTypes: next })
    },
    [effectivePrefs, persistRowPreferences],
  )

  const openManager = useCallback(() => setManagerOpen(true), [])
  const closeManager = useCallback(() => {
    setManagerOpen(false)
    setRowPrefsError(null)
  }, [])

  // ── Balance strip inputs (R10 HYBRID) — the B1 union basis keys (R3:
  // visibility-independent, including deactivated keys with historical hours) ──
  const projectKeys = rowBasis.projectKeys
  const absenceKeys = rowBasis.absenceKeys

  // R2: the Flex card's "Denne måned" computes over the SAME local month state
  // the grid renders (cells + intervals + manual + served norms) — never from
  // /summary, never refetched.
  const monthFlexDelta = useMemo<number | null>(() => {
    if (!data) return null
    return computeMonthFlexDelta({
      year,
      month,
      cellValues: localCells,
      projectKeys,
      absenceKeys,
      workIntervals: localWorkIntervals,
      manualHours: localManualHours,
      dailyNorm,
    })
  }, [data, year, month, localCells, projectKeys, absenceKeys, localWorkIntervals, localManualHours, dailyNorm])

  const monthAbsenceUsage = useMemo(() => deriveMonthAbsenceUsage(data?.absences), [data])

  // S72 Step-7a W2 — the §J analysis must see locally-edited NEIGHBOR days:
  // the panel receives the LOCALLY-OVERLAID month workTime (the SAME merged
  // view the save path reads — local intervals/manual edits over the served
  // rows, built through the hook's payload builder), never raw data.workTime.
  // Editing day N's periods and opening day N+1 must reflect N's LOCAL state.
  const localMonthWorkTime = useMemo<WorkTimeDay[]>(() => {
    const dates = new Set<string>([...localWorkIntervals.keys(), ...localManualHours.keys()])
    return buildWorkTimePayload(dates, localWorkIntervals, localManualHours)
  }, [localWorkIntervals, localManualHours])

  // ── Day panel derived props ──
  const panelProjectRows = useMemo<DayPanelProjectRow[]>(
    () =>
      [...effectivePrefs.projects]
        .sort((a, b) => a.sortOrder - b.sortOrder)
        .map((p) => ({ key: p.projectCode, label: p.projectName })),
    [effectivePrefs],
  )

  // The recorded 7203 pin: `allocations` spans ALL SERVED projects — not just
  // the visible rows — so the panel's Resterende agrees with the grid's ✓/amber.
  // S72 Step-7a B1: "all served" = the union basis keys (`catalogs` ∪ entry
  // keys), NOT the legacy `projects` field (the visible selection since 7201).
  const panelAllocations = useMemo<ReadonlyMap<string, number>>(() => {
    const map = new Map<string, number>()
    if (!openDayDate) return map
    for (const key of rowBasis.projectKeys) {
      const v = localCells.get(`${key}:${openDayDate}`)
      if (v != null && v !== 0) map.set(key, v)
    }
    return map
  }, [openDayDate, rowBasis, localCells])

  const [approving, setApproving] = useState(false)

  const handleApprove = useCallback(
    () =>
      withBusy(async () => {
        if (monthMismatch) return // S147: never approve the previous month under the new month's title
        clearApprovalValidationError()

        // Flush any pending debounced saves (cells, then work-time) before approving
        const cellsSaved = await flushCellSave()
        if (!cellsSaved) return
        const workTimeSaved = await flushWorkTimeSave()
        if (!workTimeSaved) return

        setApproving(true)
        try {
          if (data?.approval?.periodId) {
            await employeeApprove(data.approval.periodId)
          } else {
            // S127 / TASK-12707 — the `orgId && agreementCode` guard is GONE with the
            // arguments it guarded. `/api/approval/send` resolves org, agreement code
            // and OK version server-side, so those two auth-context values are no
            // longer inputs — and the guard had become a silent dead end: a missing
            // agreementCode made "Godkend måned" do nothing at all, with no error.
            await submitAndApprove()
          }
        } finally {
          setApproving(false)
        }
      }),
    [data, monthMismatch, employeeApprove, submitAndApprove, flushCellSave, flushWorkTimeSave, clearApprovalValidationError, withBusy],
  )

  if (loading && !data) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="lg" />
        <p>Indlæser skema...</p>
      </div>
    )
  }

  if (error && !data) {
    return (
      <Alert variant="error">
        Kunne ikke indlæse skema: {error}
      </Alert>
    )
  }

  return (
    <div className={styles.page}>
      {/* S147 fix-up 1 — a failed month LOAD keeps the previous month's data on screen
          (locked by monthMismatch); say so instead of leaving a silently read-only grid.
          `error` means a failed load and nothing else (action failures are `actionError`). */}
      {error && data && (
        <Alert variant="error">
          Kunne ikke indlæse skema: {error}
        </Alert>
      )}
      {/* S147 fix-up 2 — a failed save / approve / send / reopen: its own dismissible alert,
          worded for the action, showing the server's message. Cleared by the next success of
          the same kind (in the hook) or by the person. */}
      {actionError && (
        <Alert variant="error" onDismiss={clearActionError}>
          {formatActionError(actionError)}
        </Alert>
      )}
      {/* S147 fix-up 3 — the load came back with the wrong month even after the one automatic
          retry: say so and let the person retry (never a silent lock). */}
      {loadMismatch && !loading && !error && (
        <Alert variant="error">
          Kunne ikke indlæse den valgte måned.{' '}
          <Button variant="ghost" size="sm" onClick={() => void refetch()}>
            Prøv igen
          </Button>
        </Alert>
      )}
      {/* Header row: month nav (ghost buttons) left, Administrer projekter right */}
      <div className={styles.header}>
        <div className={styles.monthNav}>
          <Button variant="ghost" size="sm" onClick={goToPrevMonth}>
            &larr; Forrige
          </Button>
          <h2 className={styles.monthTitle}>{formatMonthLabel(year, month)}</h2>
          {/* A live region that is ALWAYS mounted (one inserted with its text already in it is
              often not announced); its text is set only while a month loads or a save sequence
              runs. It sits outside the aria-busy wrapper below, which assistive tech may mute. */}
          <span role="status" className={styles.monthLoading}>
            {monthMismatch && loading ? 'Indlæser…' : busy ? 'Gemmer…' : ''}
          </span>
          <Button variant="ghost" size="sm" onClick={goToNextMonth}>
            Næste &rarr;
          </Button>
        </div>
        <Button variant="secondary" size="sm" onClick={openManager}>
          Administrer projekter
        </Button>
      </div>

      {/* The 4-card balance strip (R10 HYBRID: /summary headlines + month-GET-derived values) */}
      <BalanceSummary
        data={balanceData}
        loading={balanceLoading}
        month={month}
        monthFlexDelta={monthFlexDelta}
        fullDayNormAtMonthEnd={data?.fullDayNormAtMonthEnd ?? null}
        monthAbsenceUsage={monthAbsenceUsage}
      />

      {/* Alerts row — ComplianceWarnings KEPT (R9: the server-derived EU-WTD signal) */}
      <ComplianceWarnings result={complianceResult} loading={complianceLoading} />

      {/* Quota error */}
      {quotaError && (
        <Alert variant="error" onDismiss={clearQuotaError}>
          {formatQuotaError(quotaError)}
        </Alert>
      )}

      {/* S73 R4 — full-day-only / not-eligible save rejection (422). The cell
          reverted to server truth; this alert explains why. */}
      {absenceRuleError && (
        <Alert variant="error" onDismiss={clearAbsenceRuleError}>
          {formatAbsenceRuleError(absenceRuleError, (t) => data?.absenceTypes.find((a) => a.type === t)?.label ?? t)}
        </Alert>
      )}

      {/* S123 — daily-norm ABSENCE cap 422 (shape-discriminated, not the generic
          swallow). The cell reverted to server truth (all-422 revert path); this
          alert explains why the day's summed absence exceeded the norm. */}
      {absenceCapError && (
        <Alert variant="error" onDismiss={clearAbsenceCapError}>
          {formatAbsenceCapError(absenceCapError)}
        </Alert>
      )}

      {/* Approval validation error (422 — coverage or allocation) */}
      {approvalValidationError && (
        <Alert variant="error" onDismiss={clearApprovalValidationError}>
          {(() => {
            const messages = formatApprovalValidationError(approvalValidationError)
            if (messages.length === 1) return messages[0]
            return (
              <ul className={styles.validationList}>
                {messages.map((m, i) => <li key={i}>{m}</li>)}
              </ul>
            )
          })()}
        </Alert>
      )}

      {/* Non-422 row-preference save failure (the 422 offender list renders
          inside the modal via saveError) */}
      {prefsSaveError && (
        <Alert variant="error" onDismiss={() => setPrefsSaveError(null)}>
          Kunne ikke gemme rækkeindstillingerne: {prefsSaveError}
        </Alert>
      )}

      {/* Skema grid (self-bordered per the handoff — no Card wrapper). aria-busy covers the
          grid and the footer only, on a wrapper that generates a real box (display: contents is
          dropped from the accessibility tree by some browsers). */}
      <div aria-busy={(monthMismatch && loading) || busy} className={styles.busyRegion}>
      <SkemaGrid
        year={year}
        month={month}
        rows={rows}
        cellValues={localCells}
        readOnly={isReadOnly}
        onCellChange={handleCellChange}
        workIntervals={localWorkIntervals}
        manualHours={localManualHours}
        dailyNorm={dailyNorm}
        consumptionBasis={consumptionBasis}
        rowPreferences={effectivePrefs}
        onOpenDay={handleOpenDay}
        onOpenManager={openManager}
      />

      {/* Approval footer */}
      <div className={styles.footer}>
        <ApprovalFooter
          approval={data?.approval ?? null}
          onApprove={handleApprove}
          onReopen={monthMismatch ? async () => {} : reopenPeriod}
          approving={approving}
          locked={editLocked}
        />
      </div>
      </div>

      {/* Day panel drawer (R5: unreachable on locked months) */}
      {openDayDate !== null && !panelBlocked && (
        <SkemaDayPanel
          open
          date={openDayDate}
          periods={panelPeriods}
          manualHours={localManualHours.get(openDayDate) ?? 0}
          projectRows={panelProjectRows}
          allocations={panelAllocations}
          dailyNorm={dailyNorm.get(openDayDate) ?? null}
          monthWorkTime={localMonthWorkTime}
          boundaryWorkTime={data?.boundaryWorkTime}
          onPeriodsChange={handlePanelPeriodsChange}
          onAllocationChange={handlePanelAllocationChange}
          onClose={() => setOpenDayDate(null)}
          onOpenManager={openManager}
        />
      )}

      {/* Manager modal (R5: stays reachable on locked months — month-independent
          view preferences; R11: opens from the header button AND the panel link) */}
      <SkemaProjectManager
        open={managerOpen}
        rowPreferences={effectivePrefs}
        catalogs={catalogs}
        onProjectsChange={handleProjectsChange}
        onAbsenceTypesChange={handleAbsenceTypesChange}
        onClose={closeManager}
        saveError={rowPrefsError}
      />
    </div>
  )
}

interface ApprovalFooterProps {
  approval: {
    periodId: string
    status: string
    employeeDeadline: string | null
    managerDeadline: string | null
    employeeApprovedAt: string | null
    rejectionReason: string | null
  } | null
  onApprove: () => void
  onReopen: (periodId: string, reason?: string) => Promise<void>
  approving?: boolean
  /** S147 fix-up 1 — the held data is for another month than the one shown: approve, send
      and reopen are DISABLED (not enabled-but-ignored) until the right month is in. */
  locked?: boolean
}

function ApprovalFooter({ approval, onApprove, onReopen, approving, locked = false }: ApprovalFooterProps) {
  const [reopening, setReopening] = useState(false)
  const { role } = useAuth()

  const handleReopen = useCallback(async () => {
    if (locked || !approval?.periodId) return
    setReopening(true)
    try {
      const reason = approval.status === 'APPROVED' ? 'Genåbnet af leder' : undefined
      await onReopen(approval.periodId, reason)
    } finally {
      setReopening(false)
    }
  }, [approval?.periodId, approval?.status, onReopen, locked])

  // S127 / TASK-12707 — REJECTED keeps its own branch and must be tested FIRST:
  // it is unlocked (so it is sendable) but it also owes the employee the
  // rejection reason, which the generic branch below does not render.
  if (approval && approval.status === 'REJECTED') {
    return (
      <div className={styles.footerContent}>
        <Alert variant="error">
          Afvist: {approval.rejectionReason ?? 'Ingen begrundelse'}
        </Alert>
        <Button variant="primary" onClick={onApprove} disabled={approving || locked}>
          {approving ? 'Godkender...' : 'Godkend måned'}
        </Button>
      </div>
    )
  }

  // S127 / TASK-12707 — the send affordance is the NEGATION of the lock set, not
  // an enumeration of `DRAFT || SUBMITTED`. That enumeration mirrored the old
  // two-step flow, where SUBMITTED was the intermediate state the FE itself
  // created; the backend never locked on it. Keying on `isPeriodLocked` binds
  // this to the same rule as the grid's `isReadOnly` and to the backend's own
  // set, so an editable month always has a way to be sent.
  if (!approval || !isPeriodLocked(approval.status)) {
    return (
      <div className={styles.footerContent}>
        {approval?.employeeDeadline && (
          <span className={styles.deadline}>
            Frist: {formatDeadline(approval.employeeDeadline)}
          </span>
        )}
        <Button variant="primary" onClick={onApprove} disabled={approving || locked}>
          {approving ? 'Godkender...' : 'Godkend måned'}
        </Button>
      </div>
    )
  }

  if (approval.status === 'EMPLOYEE_APPROVED') {
    return (
      <div className={styles.footerContent}>
        <Badge variant="info">Indsendt</Badge>
        <span className={styles.footerText}>Afventer leder godkendelse</span>
        {approval.managerDeadline && (
          <span className={styles.deadline}>
            Lederfrist: {formatDeadline(approval.managerDeadline)}
          </span>
        )}
        <Button variant="secondary" onClick={handleReopen} disabled={reopening || locked}>
          {reopening ? 'Genåbner...' : 'Genåbn'}
        </Button>
      </div>
    )
  }

  if (approval.status === 'APPROVED') {
    const canReopen = hasMinRole(role, 'LocalLeader')
    return (
      <div className={styles.footerContent}>
        <Badge variant="success">Godkendt</Badge>
        {canReopen && (
          <Button variant="secondary" onClick={handleReopen} disabled={reopening || locked}>
            {reopening ? 'Genåbner...' : 'Genåbn'}
          </Button>
        )}
      </div>
    )
  }

  return null
}
