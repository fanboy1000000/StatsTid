import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useSkema } from '../hooks/useSkema'
import { useTimer } from '../hooks/useTimer'
import { SkemaGrid, type WorkInterval, type WorkIntervalsMap } from '../components/SkemaGrid'
import { TimerControl } from '../components/TimerControl'
import { BalanceSummary } from '../components/BalanceSummary'
import { ComplianceWarnings } from '../components/ComplianceWarnings'
import { useBalanceSummary } from '../hooks/useBalanceSummary'
import { useCompliance } from '../hooks/useCompliance'
import { Button } from '../components/ui/Button'
import { Badge } from '../components/ui/Badge'
import { Alert } from '../components/ui/Alert'
import { Spinner } from '../components/ui/Spinner'
import { Card } from '../components/ui/Card'
import type { QuotaError } from '../hooks/useSkema'
import type { SkemaRow } from '../types'
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

const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

function formatMonthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
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

function toTimeString(d: Date): string {
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`
}

function toWorkInterval(checkInAt: string, checkOutAt: string | null): WorkInterval {
  const start = new Date(checkInAt)
  const end = checkOutAt ? new Date(checkOutAt) : start
  return { start: toTimeString(start), end: toTimeString(end) }
}

export function SkemaPage() {
  const { user, orgId } = useAuth()
  const employeeId = user?.employeeId ?? ''

  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)

  const { data, loading, error, quotaError, clearQuotaError, refetch, saveMonth, employeeApprove } = useSkema(employeeId, year, month)
  const { session, loading: timerLoading, checkIn, checkOut, elapsed, sessions: timerSessions, checkInClientTime } = useTimer(employeeId)
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
      if (entry.hours > 0) {
        cells.set(`${entry.projectCode}:${entry.date}`, entry.hours)
      }
    }
    for (const absence of data.absences) {
      if (absence.hours > 0) {
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

  // Build work intervals map from arrival/departure data
  const workIntervals = useMemo<WorkIntervalsMap>(() => {
    const map: WorkIntervalsMap = new Map()
    if (data?.arrivalDepartures) {
      for (const ad of data.arrivalDepartures) {
        if (ad.arrival || ad.departure) {
          map.set(ad.date, [{ start: ad.arrival ?? '', end: ad.departure ?? '' }])
        }
      }
    }
    return map
  }, [data])

  // Local work intervals state (for edits before save)
  const [localWorkIntervals, setLocalWorkIntervals] = useState<WorkIntervalsMap>(new Map())
  const syncedSessionIdsRef = useRef<Set<string>>(new Set())
  const activeSessionIdRef = useRef<string | null>(null)

  // Sync from data when it changes
  useEffect(() => {
    setLocalWorkIntervals(workIntervals)
  }, [workIntervals])

  // Sync timer sessions into work intervals for today
  // Uses session ID tracking — once synced, a session is never re-added (even if user deletes the interval)
  useEffect(() => {
    if (timerSessions.length === 0) return
    const todayKey = formatTodayKey()

    for (const s of timerSessions) {
      if (syncedSessionIdsRef.current.has(s.sessionId)) {
        // Already synced — but check if active session just completed (check-out)
        if (activeSessionIdRef.current === s.sessionId && !s.isActive && s.checkOutAt) {
          const updatedEnd = toTimeString(new Date(s.checkOutAt))
          setLocalWorkIntervals(prev => {
            const next = new Map(prev)
            const existing = next.get(todayKey) ?? []
            if (existing.length > 0) {
              const updated = [...existing]
              updated[updated.length - 1] = { ...updated[updated.length - 1], end: updatedEnd }
              next.set(todayKey, updated)
            }
            return next
          })
          activeSessionIdRef.current = null
        }
        continue
      }

      // New session — mark as synced and append interval
      syncedSessionIdsRef.current.add(s.sessionId)
      const interval = toWorkInterval(s.checkInAt, s.checkOutAt)

      if (s.isActive) {
        activeSessionIdRef.current = s.sessionId
      }

      setLocalWorkIntervals(prev => {
        const next = new Map(prev)
        const existing = next.get(todayKey) ?? []
        next.set(todayKey, [...existing, interval])
        return next
      })
    }
  }, [timerSessions])

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
    },
    []
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

  // Cleanup save timer on unmount
  useEffect(() => {
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current)
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

  // Compute timer hours for today
  const timerHoursToday = useMemo(() => {
    if (!session?.isActive || !session.checkInAt) return 0
    const checkInTime = new Date(session.checkInAt).getTime()
    const endTime = session.checkOutAt ? new Date(session.checkOutAt).getTime() : Date.now()
    return (endTime - checkInTime) / (1000 * 60 * 60)
  }, [session])

  // Compute allocated hours for today
  const allocatedHoursToday = useMemo(() => {
    const todayKey = formatTodayKey()
    let total = 0
    for (const [key, val] of localCells) {
      if (key.endsWith(`:${todayKey}`)) {
        total += val
      }
    }
    return total
  }, [localCells])

  // Show warning if clocked vs allocated differ
  const showTimerWarning =
    session != null &&
    timerHoursToday > 0 &&
    Math.abs(allocatedHoursToday - timerHoursToday) > 0.1

  // Handle employee approve
  const handleApprove = useCallback(async () => {
    if (data?.approval?.periodId) {
      await employeeApprove(data.approval.periodId)
    }
  }, [data, employeeApprove])

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
      </div>

      {/* Balance summary */}
      <BalanceSummary data={balanceData} loading={balanceLoading} />

      {/* Compliance warnings */}
      <ComplianceWarnings result={complianceResult} loading={complianceLoading} />

      {/* Timer control (hidden when read-only) */}
      {!isReadOnly && (
        <TimerControl
          session={session}
          onCheckIn={checkIn}
          onCheckOut={checkOut}
          loading={timerLoading}
          todayIntervals={localWorkIntervals.get(formatTodayKey()) ?? []}
          checkInClientTime={checkInClientTime}
        />
      )}

      {/* Timer warning */}
      {showTimerWarning && (
        <Alert variant="warning">
          Registrerede timer ({allocatedHoursToday.toFixed(1)}t) afviger fra stemplede timer (
          {timerHoursToday.toFixed(1)}t)
        </Alert>
      )}

      {/* Quota error */}
      {quotaError && (
        <Alert variant="error" onDismiss={clearQuotaError}>
          {formatQuotaError(quotaError)}
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
          timerHoursToday={timerHoursToday}
          workIntervals={localWorkIntervals}
          onWorkIntervalsChange={handleWorkIntervalsChange}
        />
      </Card>

      {/* Approval footer */}
      <div className={styles.footer}>
        <ApprovalFooter
          approval={data?.approval ?? null}
          onApprove={handleApprove}
          onRefetch={refetch}
        />
      </div>
    </div>
  )
}

function formatTodayKey(): string {
  const today = new Date()
  const y = today.getFullYear()
  const m = String(today.getMonth() + 1).padStart(2, '0')
  const d = String(today.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
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
  onRefetch: () => void
}

function ApprovalFooter({ approval, onApprove, onRefetch }: ApprovalFooterProps) {
  if (!approval || approval.status === 'DRAFT') {
    return (
      <div className={styles.footerContent}>
        {approval?.employeeDeadline && (
          <span className={styles.deadline}>
            Frist: {formatDeadline(approval.employeeDeadline)}
          </span>
        )}
        <Button variant="primary" onClick={onApprove}>
          Godkend maaned
        </Button>
      </div>
    )
  }

  if (approval.status === 'EMPLOYEE_APPROVED') {
    return (
      <div className={styles.footerContent}>
        <Badge variant="info">Godkendt af dig</Badge>
        <span className={styles.footerText}>Afventer leder</span>
        {approval.managerDeadline && (
          <span className={styles.deadline}>
            Lederfrist: {formatDeadline(approval.managerDeadline)}
          </span>
        )}
      </div>
    )
  }

  if (approval.status === 'APPROVED') {
    return (
      <div className={styles.footerContent}>
        <Badge variant="success">Godkendt</Badge>
      </div>
    )
  }

  if (approval.status === 'REJECTED') {
    return (
      <div className={styles.footerContent}>
        <Alert variant="error">
          Afvist: {approval.rejectionReason ?? 'Ingen begrundelse'}
        </Alert>
        <Button variant="secondary" onClick={onRefetch}>
          Genaabn
        </Button>
      </div>
    )
  }

  return null
}
