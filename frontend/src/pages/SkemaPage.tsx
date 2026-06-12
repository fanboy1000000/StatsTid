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
import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useSkema, buildWorkTimePayload, deriveSkemaRowBasis, periodHours } from '../hooks/useSkema'
import type { QuotaError, ApprovalValidationError, SkemaRowBasis } from '../hooks/useSkema'
import {
  SkemaGrid,
  type WorkInterval,
  type WorkIntervalsMap,
  type ManualHoursMap,
  type DailyNormMap,
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

const DANISH_ABSENCE_LABELS: Record<string, string> = {
  VACATION: 'Ferie',
  SPECIAL_HOLIDAY: 'Særlige feriedage',
  CARE_DAY: 'Omsorgsdage',
  CHILD_SICK: 'Barns sygedag',
  SENIOR_DAY: 'Seniordage',
}

function formatQuotaError(q: QuotaError): string {
  const label = DANISH_ABSENCE_LABELS[q.absenceType] ?? q.absenceType
  const remaining = q.remaining.toFixed(1).replace('.', ',')
  const requested = q.requested.toFixed(1).replace('.', ',')
  return `Du har overskredet din kvote for ${label}. Du har ${remaining} dage tilbage, men forsøgte at registrere ${requested} dage.`
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

export function SkemaPage() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''

  // Initial period: ?year=&month= from the Årsoversigt drill-in (year clamped to the
  // backend's supported 2000–2100 range — mirrors the year-overview endpoint's own
  // validation; an unclamped year ≥ 10000 throws in DateTime.DaysInMonth server-side
  // (Step-7a cycle-4 Codex) — month clamped 1..12), else today. Only seeds the initial
  // state — subsequent nav uses local setters.
  const [searchParams] = useSearchParams()
  const now = new Date()
  const paramYear = Number(searchParams.get('year'))
  const paramMonth = Number(searchParams.get('month'))
  const [year, setYear] = useState(
    Number.isInteger(paramYear) && paramYear >= 2000 && paramYear <= 2100
      ? paramYear
      : now.getFullYear(),
  )
  const [month, setMonth] = useState(
    Number.isInteger(paramMonth) && paramMonth >= 1 && paramMonth <= 12
      ? paramMonth
      : now.getMonth() + 1,
  )

  const { data, loading, error, quotaError, approvalValidationError, clearQuotaError, clearApprovalValidationError, refetch, saveMonth, employeeApprove, submitAndApprove, reopenPeriod } = useSkema(employeeId, year, month)
  const { orgId, agreementCode } = useAuth()
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
  const pendingChangesRef = useRef<{ rowKey: string; date: string; hours: number | null }[]>([])
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

  // Build cell values from data
  useEffect(() => {
    if (!data) {
      setLocalCells(new Map())
      return
    }
    const cells = new Map<string, number>()
    for (const entry of data.entries) {
      if (entry.hours !== 0) {
        cells.set(`${entry.projectCode}:${entry.date}`, entry.hours)
      }
    }
    for (const absence of data.absences) {
      if (absence.hours !== 0) {
        cells.set(`${absence.absenceType}:${absence.date}`, absence.hours)
      }
    }
    setLocalCells(cells)
    pendingChangesRef.current = []
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

  // Local editable work-time state (intervals + manual hours; D-B: manual hours
  // have no entry UI anymore but existing values keep counting and round-trip)
  const [localWorkIntervals, setLocalWorkIntervals] = useState<WorkIntervalsMap>(new Map())
  const [localManualHours, setLocalManualHours] = useState<ManualHoursMap>(new Map())

  // Rehydrate local state when backend data changes (after fetch/save/approve)
  useEffect(() => {
    setLocalWorkIntervals(workIntervalsFromData)
  }, [workIntervalsFromData])
  useEffect(() => {
    setLocalManualHours(manualHoursFromData)
  }, [manualHoursFromData])

  // Live refs so the debounced work-time save reads the latest local state
  const workIntervalsRef = useRef<WorkIntervalsMap>(localWorkIntervals)
  const manualHoursRef = useRef<ManualHoursMap>(localManualHours)
  useEffect(() => { workIntervalsRef.current = localWorkIntervals }, [localWorkIntervals])
  useEffect(() => { manualHoursRef.current = localManualHours }, [localManualHours])

  // Debounced work-time save (intervals + the preserved manual hours). The
  // payload ALWAYS builds through the hook's buildWorkTimePayload — the single
  // R16/R7 owner (manualHours carry-forward is enforced and tested THERE).
  const workTimeDirtyRef = useRef<Set<string>>(new Set())
  const workTimeSaveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  /** Flush pending debounced WORK-TIME saves (B2: fires any pending debounce
      immediately AND awaits the in-flight POST). Returns false when any
      involved save failed — callers must not refetch over unsaved state. */
  /** Fire a workTime save for the given dirty dates; on FAILURE the dates are
      re-queued (the local refs still hold the edits) so a later flush retries
      instead of forgetting (the c2 settled-failure fix). */
  const fireWorkTimeSave = useCallback((dates: string[]): Promise<boolean> => {
    const payload: WorkTimeDay[] = buildWorkTimePayload(
      dates,
      workIntervalsRef.current,
      manualHoursRef.current,
    )
    if (payload.length === 0) return Promise.resolve(true)
    const save = saveMonth([], payload).then((ok) => {
      if (!ok) for (const d of dates) workTimeDirtyRef.current.add(d)
      return ok
    })
    workTimeSavesInFlightRef.current.add(save)
    void save.finally(() => {
      workTimeSavesInFlightRef.current.delete(save)
    })
    return save
  }, [saveMonth])

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
    if (workTimeDirtyRef.current.size === 0) return inFlightOk
    const dates = [...workTimeDirtyRef.current]
    workTimeDirtyRef.current = new Set()
    const ok = await fireWorkTimeSave(dates)
    return inFlightOk && ok
  }, [fireWorkTimeSave])

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
    [scheduleWorkTimeSave]
  )

  // Approval status
  const approvalStatus = data?.approval?.status ?? 'DRAFT'
  const isReadOnly = approvalStatus === 'EMPLOYEE_APPROVED' || approvalStatus === 'APPROVED'

  /** Fire a cell save for the given changes; on FAILURE the affected cells are
      re-queued FROM LIVE LOCAL STATE — never the stale captured delta (Step-7a
      c2 settled-failure fix + the c3 overlapping-saves fix: a newer save that
      succeeded first must not be overwritten by an older failure's retry) —
      and never overwriting a NEWER pending entry for the same (rowKey, date).
      A cell whose live value is gone (cleared) is NOT re-queued (the recorded
      R17 inherited limitation: clearing never persists through this path). */
  const fireCellSave = useCallback((changes: { rowKey: string; date: string; hours: number | null }[]): Promise<boolean> => {
    const save = saveMonth(changes).then((ok) => {
      if (!ok) {
        for (const c of changes) {
          const hasNewer = pendingChangesRef.current.some(
            (p) => p.rowKey === c.rowKey && p.date === c.date
          )
          if (hasNewer) continue
          const live = localCellsRef.current.get(`${c.rowKey}:${c.date}`)
          if (live === undefined) continue
          pendingChangesRef.current.push({ rowKey: c.rowKey, date: c.date, hours: live })
        }
      }
      return ok
    })
    cellSavesInFlightRef.current.add(save)
    void save.finally(() => {
      cellSavesInFlightRef.current.delete(save)
    })
    return save
  }, [saveMonth])

  // Debounced cell save (grid inline edits + the panel's step-2 allocations)
  const handleCellChange = useCallback(
    (rowKey: string, date: string, hours: number | null) => {
      setLocalCells((prev) => {
        const next = new Map(prev)
        if (hours === null) {
          next.delete(`${rowKey}:${date}`)
        } else {
          next.set(`${rowKey}:${date}`, hours)
        }
        return next
      })

      // Track pending change
      const idx = pendingChangesRef.current.findIndex(
        (c) => c.rowKey === rowKey && c.date === date
      )
      if (idx >= 0) {
        pendingChangesRef.current[idx] = { rowKey, date, hours }
      } else {
        pendingChangesRef.current.push({ rowKey, date, hours })
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
    [fireCellSave]
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
    const pending = [...pendingChangesRef.current]
    pendingChangesRef.current = []
    if (pending.length === 0) return inFlightOk
    const ok = await fireCellSave(pending)
    return inFlightOk && ok
  }, [fireCellSave])

  // Cleanup save timers on unmount
  useEffect(() => {
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current)
      }
      if (workTimeSaveTimerRef.current) {
        clearTimeout(workTimeSaveTimerRef.current)
      }
    }
  }, [])

  // Month navigation
  const goToPrevMonth = useCallback(() => {
    setMonth((prev) => {
      if (prev === 1) {
        setYear((y) => y - 1)
        return 12
      }
      return prev - 1
    })
  }, [])

  const goToNextMonth = useCallback(() => {
    setMonth((prev) => {
      if (prev === 12) {
        setYear((y) => y + 1)
        return 1
      }
      return prev + 1
    })
  }, [])

  // ── Day panel (7203) — page-controlled (R16): the page owns the open day's
  // period rows (raw text) and translates VALID rows into the existing debounced
  // workTime save path; allocations ride the existing cell-save path. ──
  const [openDayDate, setOpenDayDate] = useState<string | null>(null)
  const [panelPeriods, setPanelPeriods] = useState<DayPanelPeriod[]>([])
  const panelIdRef = useRef(0)

  const handleOpenDay = useCallback((date: string) => {
    const intervals = workIntervalsRef.current.get(date) ?? []
    setPanelPeriods(
      intervals.map((iv) => ({ id: `srv-${++panelIdRef.current}`, from: iv.start, to: iv.end })),
    )
    setOpenDayDate(date)
  }, [])

  // Month nav drops any open day (the date belongs to the previous view); a
  // month that locks (approve) closes the panel — R5: the panel is unreachable
  // on locked months (the grid renders no triggers either).
  useEffect(() => { setOpenDayDate(null) }, [year, month])
  useEffect(() => { if (isReadOnly) setOpenDayDate(null) }, [isReadOnly])

  const handlePanelPeriodsChange = useCallback(
    (date: string, periods: DayPanelPeriod[]) => {
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
    [handleWorkIntervalsChange],
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

  const persistRowPreferences = useCallback(
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
      refetch()
    },
    [employeeId, flushCellSave, flushWorkTimeSave, refetch],
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

  const handleApprove = useCallback(async () => {
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
      } else if (orgId && agreementCode) {
        await submitAndApprove(orgId, agreementCode)
      }
    } finally {
      setApproving(false)
    }
  }, [data, employeeApprove, submitAndApprove, orgId, agreementCode, flushCellSave, flushWorkTimeSave, clearApprovalValidationError])

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
      {/* Header row: month nav (ghost buttons) left, Administrer projekter right */}
      <div className={styles.header}>
        <div className={styles.monthNav}>
          <Button variant="ghost" size="sm" onClick={goToPrevMonth}>
            &larr; Forrige
          </Button>
          <h2 className={styles.monthTitle}>{formatMonthLabel(year, month)}</h2>
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

      {/* Skema grid (self-bordered per the handoff — no Card wrapper) */}
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
        rowPreferences={effectivePrefs}
        onOpenDay={handleOpenDay}
        onOpenManager={openManager}
      />

      {/* Approval footer */}
      <div className={styles.footer}>
        <ApprovalFooter
          approval={data?.approval ?? null}
          onApprove={handleApprove}
          onReopen={reopenPeriod}
          approving={approving}
        />
      </div>

      {/* Day panel drawer (R5: unreachable on locked months) */}
      {openDayDate !== null && !isReadOnly && (
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
}

function ApprovalFooter({ approval, onApprove, onReopen, approving }: ApprovalFooterProps) {
  const [reopening, setReopening] = useState(false)
  const { role } = useAuth()

  const handleReopen = useCallback(async () => {
    if (!approval?.periodId) return
    setReopening(true)
    try {
      const reason = approval.status === 'APPROVED' ? 'Genåbnet af leder' : undefined
      await onReopen(approval.periodId, reason)
    } finally {
      setReopening(false)
    }
  }, [approval?.periodId, approval?.status, onReopen])

  if (!approval || approval.status === 'DRAFT' || approval.status === 'SUBMITTED') {
    return (
      <div className={styles.footerContent}>
        {approval?.employeeDeadline && (
          <span className={styles.deadline}>
            Frist: {formatDeadline(approval.employeeDeadline)}
          </span>
        )}
        <Button variant="primary" onClick={onApprove} disabled={approving}>
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
        <Button variant="secondary" onClick={handleReopen} disabled={reopening}>
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
          <Button variant="secondary" onClick={handleReopen} disabled={reopening}>
            {reopening ? 'Genåbner...' : 'Genåbn'}
          </Button>
        )}
      </div>
    )
  }

  if (approval.status === 'REJECTED') {
    return (
      <div className={styles.footerContent}>
        <Alert variant="error">
          Afvist: {approval.rejectionReason ?? 'Ingen begrundelse'}
        </Alert>
        <Button variant="primary" onClick={onApprove} disabled={approving}>
          {approving ? 'Godkender...' : 'Godkend måned'}
        </Button>
      </div>
    )
  }

  return null
}
