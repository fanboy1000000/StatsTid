import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useSkema } from '../hooks/useSkema'
import { SkemaGrid, type WorkInterval, type WorkIntervalsMap, type ManualHoursMap, type DailyNormMap } from '../components/SkemaGrid'
import { BalanceSummary } from '../components/BalanceSummary'
import { AllocationSummary, type AllocationProjectBreakdown } from '../components/AllocationSummary'
import { ComplianceWarnings } from '../components/ComplianceWarnings'
import { ProjectPicker } from '../components/ProjectPicker'
import { useBalanceSummary } from '../hooks/useBalanceSummary'
import { useCompliance } from '../hooks/useCompliance'
import { Button } from '../components/ui/Button'
import { hasMinRole } from '../lib/roles'
import { formatMonthLabel } from '../lib/locale'
import { Badge } from '../components/ui/Badge'
import { Alert } from '../components/ui/Alert'
import { Spinner } from '../components/ui/Spinner'
import { Card } from '../components/ui/Card'
import type { QuotaError, ApprovalValidationError } from '../hooks/useSkema'
import type { SkemaRow, WorkTimeDay } from '../types'
import styles from './SkemaPage.module.css'

const DANISH_ABSENCE_LABELS: Record<string, string> = {
  VACATION: 'Ferie',
  SPECIAL_HOLIDAY: 'Feriefridage',
  CARE_DAY: 'Omsorgsdage',
  CHILD_SICK: 'Barns sygedag',
  SENIOR_DAY: 'Seniordage',
}

function formatQuotaError(q: QuotaError): string {
  const label = DANISH_ABSENCE_LABELS[q.absenceType] ?? q.absenceType
  const remaining = q.remaining.toFixed(1).replace('.', ',')
  const requested = q.requested.toFixed(1).replace('.', ',')
  return `Du har overskredet din kvote for ${label}. Du har ${remaining} dage tilbage, men forsoegte at registrere ${requested} dage.`
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
  const pendingChangesRef = useRef<{ rowKey: string; date: string; hours: number | null }[]>([])
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

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

  // Build rows
  const rows: SkemaRow[] = useMemo(() => {
    if (!data) return []
    const projectRows: SkemaRow[] = (data.projects ?? []).map((p) => ({
      type: 'project' as const,
      key: p.projectCode,
      label: p.projectName,
    }))
    const absenceRows: SkemaRow[] = (data.absenceTypes ?? []).map((a) => ({
      type: 'absence' as const,
      key: a.type,
      label: a.label,
    }))
    return [...projectRows, ...absenceRows]
  }, [data])

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

  // Local editable work-time state (intervals + manual hours)
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

  // Debounced work-time save (intervals + manual hours)
  const workTimeDirtyRef = useRef<Set<string>>(new Set())
  const workTimeSaveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const buildWorkTimePayload = useCallback((): WorkTimeDay[] => {
    const dates = [...workTimeDirtyRef.current]
    return dates.map((date) => ({
      date,
      intervals: (workIntervalsRef.current.get(date) ?? []).map(iv => ({ start: iv.start, end: iv.end })),
      manualHours: manualHoursRef.current.get(date) ?? 0,
    }))
  }, [])

  const flushWorkTimeSave = useCallback(async (): Promise<boolean> => {
    if (workTimeSaveTimerRef.current) {
      clearTimeout(workTimeSaveTimerRef.current)
      workTimeSaveTimerRef.current = null
    }
    if (workTimeDirtyRef.current.size === 0) return true
    const payload = buildWorkTimePayload()
    workTimeDirtyRef.current = new Set()
    return saveMonth([], payload)
  }, [buildWorkTimePayload, saveMonth])

  const scheduleWorkTimeSave = useCallback((date: string) => {
    workTimeDirtyRef.current.add(date)
    if (workTimeSaveTimerRef.current) clearTimeout(workTimeSaveTimerRef.current)
    workTimeSaveTimerRef.current = setTimeout(() => {
      const payload = buildWorkTimePayload()
      workTimeDirtyRef.current = new Set()
      workTimeSaveTimerRef.current = null
      if (payload.length > 0) void saveMonth([], payload)
    }, 1000)
  }, [buildWorkTimePayload, saveMonth])

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

  const handleManualHoursChange = useCallback(
    (date: string, hours: number | null) => {
      setLocalManualHours(prev => {
        const next = new Map(prev)
        if (hours === null) {
          next.delete(date)
        } else {
          next.set(date, hours)
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

  // Debounced save
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
      saveTimerRef.current = setTimeout(async () => {
        const changes = [...pendingChangesRef.current]
        pendingChangesRef.current = []
        if (changes.length > 0) {
          await saveMonth(changes)
        }
      }, 1000)
    },
    [saveMonth]
  )

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

  // Allocation summary data (monthly worked vs project-allocated)
  const allocationSummary = useMemo(() => {
    const projectCodes = new Set((data?.projects ?? []).map(p => p.projectCode))
    // Worked = period interval hours + manual hours for the month
    let worked = 0
    for (const intervals of localWorkIntervals.values()) {
      for (const iv of intervals) {
        if (iv.start && iv.end) {
          const sp = iv.start.split(':').map(Number)
          const ep = iv.end.split(':').map(Number)
          const diff = (ep[0] * 60 + ep[1]) - (sp[0] * 60 + sp[1])
          if (diff > 0) worked += diff / 60
        }
      }
    }
    for (const h of localManualHours.values()) worked += h

    // Allocated per project (NORMAL allocation; absences excluded)
    const perProject = new Map<string, number>()
    for (const [key, val] of localCells) {
      const code = key.slice(0, key.lastIndexOf(':'))
      if (projectCodes.has(code)) {
        perProject.set(code, (perProject.get(code) ?? 0) + val)
      }
    }
    let allocated = 0
    for (const v of perProject.values()) allocated += v

    const projects: AllocationProjectBreakdown[] = (data?.projects ?? []).map(p => ({
      projectCode: p.projectCode,
      projectName: p.projectName,
      hours: Math.round((perProject.get(p.projectCode) ?? 0) * 100) / 100,
    }))

    return {
      worked: Math.round(worked * 100) / 100,
      allocated: Math.round(allocated * 100) / 100,
      projects,
    }
  }, [data, localWorkIntervals, localManualHours, localCells])

  const [approving, setApproving] = useState(false)
  const [projectPickerOpen, setProjectPickerOpen] = useState(false)

  const handleApprove = useCallback(async () => {
    clearApprovalValidationError()

    // Flush any pending debounced saves before approving
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current)
      saveTimerRef.current = null
    }
    const pending = [...pendingChangesRef.current]
    pendingChangesRef.current = []
    if (pending.length > 0) {
      const saved = await saveMonth(pending)
      if (!saved) return
    }

    // Flush any pending work-time (intervals + manual hours) before approving
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
  }, [data, employeeApprove, submitAndApprove, orgId, agreementCode, saveMonth, flushWorkTimeSave, clearApprovalValidationError])

  if (loading && !data) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="lg" />
        <p>Indlaeser skema...</p>
      </div>
    )
  }

  if (error && !data) {
    return (
      <Alert variant="error">
        Kunne ikke indlaese skema: {error}
      </Alert>
    )
  }

  return (
    <div className={styles.page}>
      {/* Header with month navigation */}
      <div className={styles.header}>
        <div className={styles.monthNav}>
          <Button variant="ghost" size="sm" onClick={goToPrevMonth}>
            &larr; Forrige
          </Button>
          <h2 className={styles.monthTitle}>{formatMonthLabel(year, month)}</h2>
          <Button variant="ghost" size="sm" onClick={goToNextMonth}>
            Naeste &rarr;
          </Button>
        </div>
        <Button variant="secondary" size="sm" onClick={() => setProjectPickerOpen(true)}>
          Administrer projekter
        </Button>
      </div>

      {/* Project picker dialog */}
      <ProjectPicker
        open={projectPickerOpen}
        onOpenChange={setProjectPickerOpen}
        onSelectionChanged={refetch}
      />

      {/* Balance summary */}
      <BalanceSummary data={balanceData} loading={balanceLoading} />

      {/* Compliance warnings */}
      <ComplianceWarnings result={complianceResult} loading={complianceLoading} />

      {/* Allocation summary (Fordeling af arbejdstid) */}
      <AllocationSummary
        workedHours={allocationSummary.worked}
        allocatedHours={allocationSummary.allocated}
        projects={allocationSummary.projects}
      />

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

      {/* Skema grid */}
      <Card>
        <SkemaGrid
          year={year}
          month={month}
          rows={rows}
          cellValues={localCells}
          readOnly={isReadOnly}
          onCellChange={handleCellChange}
          workIntervals={localWorkIntervals}
          onWorkIntervalsChange={handleWorkIntervalsChange}
          manualHours={localManualHours}
          onManualHoursChange={handleManualHoursChange}
          dailyNorm={dailyNorm}
        />
      </Card>

      {/* Approval footer */}
      <div className={styles.footer}>
        <ApprovalFooter
          approval={data?.approval ?? null}
          onApprove={handleApprove}
          onReopen={reopenPeriod}
          approving={approving}
        />
      </div>
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
          {approving ? 'Godkender...' : 'Godkend maaned'}
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
          {approving ? 'Godkender...' : 'Godkend maaned'}
        </Button>
      </div>
    )
  }

  return null
}
