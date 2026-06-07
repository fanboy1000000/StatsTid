import { useState, useMemo, useCallback } from 'react'
import type { SkemaRow } from '../types'
import { Dialog } from './ui/Dialog'
import { Button } from './ui/Button'
import { parseDanishNumber, formatDanishNumber } from '../lib/locale'
import { classifyAllocation, unallocated } from '../lib/allocation'
import styles from './SkemaGrid.module.css'

export interface WorkInterval {
  start: string  // "HH:mm" or "HH:mm:ss"
  end: string    // "HH:mm" or "HH:mm:ss"
}

export type WorkIntervalsMap = Map<string, WorkInterval[]>  // dateKey -> intervals
export type ManualHoursMap = Map<string, number>            // dateKey -> manual hours
export type DailyNormMap = Map<string, number | null>       // dateKey -> norm hours (null = blank)

interface SkemaGridProps {
  year: number
  month: number
  rows: SkemaRow[]
  cellValues: Map<string, number>
  readOnly: boolean
  onCellChange: (rowKey: string, date: string, hours: number | null) => void
  workIntervals?: WorkIntervalsMap
  onWorkIntervalsChange?: (date: string, intervals: WorkInterval[]) => void
  manualHours?: ManualHoursMap
  onManualHoursChange?: (date: string, hours: number | null) => void
  dailyNorm?: DailyNormMap
}

const DA_DAY_ABBREV = ['So', 'Ma', 'Ti', 'On', 'To', 'Fr', 'Lo']

function getDaysInMonth(year: number, month: number): Date[] {
  const days: Date[] = []
  const daysCount = new Date(year, month, 0).getDate()
  for (let d = 1; d <= daysCount; d++) {
    days.push(new Date(year, month - 1, d))
  }
  return days
}

function formatDateKey(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function isToday(date: Date): boolean {
  const today = new Date()
  return (
    date.getFullYear() === today.getFullYear() &&
    date.getMonth() === today.getMonth() &&
    date.getDate() === today.getDate()
  )
}

function isWeekend(date: Date): boolean {
  const day = date.getDay()
  return day === 0 || day === 6
}

function calcIntervalHours(intervals: WorkInterval[]): number {
  let totalSec = 0
  for (const iv of intervals) {
    if (iv.start && iv.end) {
      const sp = iv.start.split(':').map(Number)
      const ep = iv.end.split(':').map(Number)
      const startSec = sp[0] * 3600 + sp[1] * 60 + (sp[2] ?? 0)
      const endSec = ep[0] * 3600 + ep[1] * 60 + (ep[2] ?? 0)
      const diff = endSec - startSec
      if (diff > 0) totalSec += diff
    }
  }
  return Math.round((totalSec / 3600) * 100) / 100
}

// S58 TASK-5802 — parse "HH:mm"/"HH:mm:ss" to seconds-since-midnight (null if malformed).
function parseTimeToSeconds(value: string): number | null {
  if (!value) return null
  const parts = value.split(':').map(Number)
  if (parts.length < 2 || parts.some(isNaN)) return null
  const [h, m, s = 0] = parts
  if (h < 0 || h > 23 || m < 0 || m > 59 || s < 0 || s > 59) return null
  return h * 3600 + m * 60 + s
}

// S58 TASK-5802 — true if any two positive-duration intervals overlap. Sort by start;
// overlap iff an interval starts strictly before the previous one ends. Touching
// boundaries (next.start === prev.end) are allowed. Mirrors the backend guard.
function intervalsOverlap(intervals: WorkInterval[]): boolean {
  const parsed: Array<[number, number]> = []
  for (const iv of intervals) {
    const s = parseTimeToSeconds(iv.start)
    const e = parseTimeToSeconds(iv.end)
    if (s !== null && e !== null && e > s) parsed.push([s, e])
  }
  parsed.sort((a, b) => a[0] - b[0])
  for (let i = 1; i < parsed.length; i++) {
    if (parsed[i][0] < parsed[i - 1][1]) return true
  }
  return false
}

// S58 TASK-5802 — validate one day's work time (Arbejdstid). Returns a Danish error
// message or null. Same three checks as the authoritative backend guard so the UI
// blocks before save: negative manual hours, overlapping intervals, total > 24h.
function validateWorkDay(intervals: WorkInterval[], manualHours: number): string | null {
  if (manualHours < 0) return 'Manuelt registrerede timer kan ikke være negative.'
  if (intervalsOverlap(intervals)) return 'Arbejdsperioderne overlapper hinanden.'
  const total = Math.round((calcIntervalHours(intervals) + manualHours) * 100) / 100
  if (total > 24) return `Arbejdstid må ikke overstige 24 timer (i alt ${formatHours(total) || '0t'}).`
  return null
}

function formatHours(h: number): string {
  if (h === 0) return ''
  const hours = Math.floor(h)
  const mins = Math.round((h - hours) * 60)
  if (mins === 0) return `${hours}t`
  return `${hours}t ${mins}m`
}

function formatDiff(diff: number): string {
  if (diff === 0) return ''
  const sign = diff > 0 ? '+' : '-'
  const abs = Math.abs(diff)
  const hours = Math.floor(abs)
  const mins = Math.round((abs - hours) * 60)
  if (mins === 0) return `${sign}${hours}t`
  return `${sign}${hours}t ${mins}m`
}

/** Signed hours with Danish decimal comma (e.g. "+7,4 t", "0 t"). */
function formatSignedHours(value: number): string {
  const sign = value > 0 ? '+' : value < 0 ? '-' : ''
  return `${sign}${formatDanishNumber(Math.abs(value), 2)} t`
}

function formatDateLabel(dateKey: string): string {
  const [y, m, d] = dateKey.split('-')
  return `${d}/${m}-${y}`
}

export function SkemaGrid({
  year,
  month,
  rows,
  cellValues,
  readOnly,
  onCellChange,
  workIntervals,
  onWorkIntervalsChange,
  manualHours,
  onManualHoursChange,
  dailyNorm,
}: SkemaGridProps) {
  const days = useMemo(() => getDaysInMonth(year, month), [year, month])

  const projectRows = useMemo(() => rows.filter((r) => r.type === 'project'), [rows])
  const absenceRows = useMemo(() => rows.filter((r) => r.type === 'absence'), [rows])

  const [dialogDate, setDialogDate] = useState<string | null>(null)
  const [dialogIntervals, setDialogIntervals] = useState<WorkInterval[]>([])

  const getColumnClass = useCallback(
    (date: Date) => {
      const classes: string[] = []
      if (isWeekend(date)) classes.push(styles.weekend)
      if (isToday(date)) classes.push(styles.today)
      return classes.join(' ')
    },
    []
  )

  // Compute row sums
  const rowSums = useMemo(() => {
    const sums = new Map<string, number>()
    for (const row of rows) {
      let sum = 0
      for (const day of days) {
        const key = `${row.key}:${formatDateKey(day)}`
        sum += cellValues.get(key) ?? 0
      }
      sums.set(row.key, sum)
    }
    return sums
  }, [rows, days, cellValues])

  // Compute column (daily) totals
  const dayTotals = useMemo(() => {
    const totals = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      let total = 0
      for (const row of rows) {
        total += cellValues.get(`${row.key}:${dateKey}`) ?? 0
      }
      totals.set(dateKey, total)
    }
    return totals
  }, [rows, days, cellValues])

  const grandTotal = useMemo(() => {
    let sum = 0
    for (const v of rowSums.values()) {
      sum += v
    }
    return sum
  }, [rowSums])

  // "Tilføj periode" hours per day (summed work intervals)
  const periodHoursPerDay = useMemo(() => {
    const map = new Map<string, number>()
    if (workIntervals) {
      for (const [dateKey, intervals] of workIntervals) {
        const hours = calcIntervalHours(intervals)
        if (hours > 0) map.set(dateKey, hours)
      }
    }
    return map
  }, [workIntervals])

  const periodHoursSum = useMemo(() => {
    let sum = 0
    for (const h of periodHoursPerDay.values()) sum += h
    return Math.round(sum * 100) / 100
  }, [periodHoursPerDay])

  // "Tilføj timer" — direct manual daily hours
  const manualHoursSum = useMemo(() => {
    let sum = 0
    if (manualHours) for (const h of manualHours.values()) sum += h
    return Math.round(sum * 100) / 100
  }, [manualHours])

  // Worked per day = period interval hours + manual hours
  const workedPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      const period = periodHoursPerDay.get(dateKey) ?? 0
      const manual = manualHours?.get(dateKey) ?? 0
      const worked = Math.round((period + manual) * 100) / 100
      if (worked > 0) map.set(dateKey, worked)
    }
    return map
  }, [days, periodHoursPerDay, manualHours])

  // Diff. fra normtid per day: (worked ?? 0) - dailyNorm.hours.
  // Shown on every day that HAS a norm (norm>0) so a workday with no registered
  // work time still surfaces its full -norm shortfall; also shown when work time
  // was registered on a 0-norm day (e.g. weekend overtime -> +worked). Blank when
  // norm is null (academic ANNUAL_ACTIVITY) or when there is neither norm nor work.
  // formatDiff(0) renders blank, so a perfectly-on-norm day shows nothing.
  const diffPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      const norm = dailyNorm?.get(dateKey)
      if (norm === null || norm === undefined) continue // academic ANNUAL_ACTIVITY -> blank
      const worked = workedPerDay.get(dateKey) ?? 0
      if (norm <= 0 && worked <= 0) continue // 0-norm day with no work -> blank
      map.set(dateKey, Math.round((worked - norm) * 100) / 100)
    }
    return map
  }, [days, workedPerDay, dailyNorm])

  const diffSum = useMemo(() => {
    let sum = 0
    for (const d of diffPerDay.values()) sum += d
    return Math.round(sum * 100) / 100
  }, [diffPerDay])

  // Allocated per day = sum of project-row cells (absences excluded)
  const allocatedPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      let total = 0
      for (const row of projectRows) {
        total += cellValues.get(`${row.key}:${dateKey}`) ?? 0
      }
      map.set(dateKey, Math.round(total * 100) / 100)
    }
    return map
  }, [days, projectRows, cellValues])

  // Ikke fordelt (unallocated) per day = worked - allocated.
  // Parallel to "Diff. fra normtid": shown on every day that has a norm (norm>0)
  // as well as any day with work time or allocation. An empty norm-day (0 - 0)
  // classifies as balanced (✓) — nothing is left to distribute yet. Days with no
  // norm and no activity stay blank.
  const unallocatedPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      const norm = dailyNorm?.get(dateKey)
      const hasNorm = norm !== null && norm !== undefined && norm > 0
      const worked = workedPerDay.get(dateKey) ?? 0
      const allocated = allocatedPerDay.get(dateKey) ?? 0
      if (!hasNorm && worked === 0 && allocated === 0) continue
      map.set(dateKey, unallocated(worked, allocated))
    }
    return map
  }, [days, workedPerDay, allocatedPerDay, dailyNorm])

  const workedSumMonth = useMemo(() => {
    let sum = 0
    for (const h of workedPerDay.values()) sum += h
    return Math.round(sum * 100) / 100
  }, [workedPerDay])

  const allocatedSumMonth = useMemo(() => {
    let sum = 0
    for (const h of allocatedPerDay.values()) sum += h
    return Math.round(sum * 100) / 100
  }, [allocatedPerDay])

  const unallocatedMonth = useMemo(
    () => unallocated(workedSumMonth, allocatedSumMonth),
    [workedSumMonth, allocatedSumMonth]
  )

  // S56 Step 7a WARNING fix: the month-total cell must NOT net days against each other —
  // the backend gate is PER-DAY, so a +X day and an offsetting -X day both fail the gate
  // even though their sum is 0. The total is "balanced" (green ✓) ONLY when every gated day
  // (worked>0 or allocated>0) is individually balanced; this matches the set the approval
  // gate accepts.
  const allDaysBalanced = useMemo(() => {
    for (const day of days) {
      const dateKey = formatDateKey(day)
      const worked = workedPerDay.get(dateKey) ?? 0
      const allocated = allocatedPerDay.get(dateKey) ?? 0
      if (worked === 0 && allocated === 0) continue
      if (classifyAllocation(worked, allocated) !== 'balanced') return false
    }
    return true
  }, [days, workedPerDay, allocatedPerDay])

  const handleCellChange = useCallback(
    (rowKey: string, date: string, value: string) => {
      const num = value === '' ? null : parseDanishNumber(value)
      if (num !== null && isNaN(num)) return
      onCellChange(rowKey, date, num === 0 ? null : num)
    },
    [onCellChange]
  )

  // ADR-032 D3 — norm prefill for absence-type cells. Absence types are permanent
  // grid rows, so the prefill trigger is the FIRST FOCUS of an EMPTY absence cell:
  // seed the input with that day's norm (the same per-day value the "Diff. fra
  // normtid" row reads). Never overwrites an existing value; skipped entirely on
  // zero/null-norm days (weekends, academic ANNUAL_ACTIVITY, missing norm). The
  // seeded value stays fully editable — partial days remain legal.
  const handleAbsenceFocus = useCallback(
    (rowKey: string, date: string, currentValue: number | undefined) => {
      if (currentValue != null) return // existing value — never overwrite
      const norm = dailyNorm?.get(date)
      if (norm === null || norm === undefined || norm <= 0) return // zero/null-norm — no prefill
      onCellChange(rowKey, date, norm)
    },
    [dailyNorm, onCellChange]
  )

  const handleManualHoursChange = useCallback(
    (date: string, value: string) => {
      if (!onManualHoursChange) return
      const num = value === '' ? null : parseDanishNumber(value)
      if (num !== null && isNaN(num)) return
      if (num !== null && num < 0) return // S58 TASK-5802 — reject negative manual hours
      onManualHoursChange(date, num === 0 ? null : num)
    },
    [onManualHoursChange]
  )

  const openDialog = useCallback((dateKey: string) => {
    if (readOnly) return
    const existing = workIntervals?.get(dateKey) ?? []
    setDialogDate(dateKey)
    setDialogIntervals(existing.length > 0 ? existing.map(iv => ({ ...iv })) : [{ start: '', end: '' }])
  }, [readOnly, workIntervals])

  const handleAddInterval = useCallback(() => {
    setDialogIntervals(prev => [...prev, { start: '', end: '' }])
  }, [])

  const handleRemoveInterval = useCallback((index: number) => {
    setDialogIntervals(prev => prev.filter((_, i) => i !== index))
  }, [])

  const handleIntervalChange = useCallback((index: number, field: 'start' | 'end', value: string) => {
    setDialogIntervals(prev => prev.map((iv, i) => i === index ? { ...iv, [field]: value } : iv))
  }, [])

  // S58 TASK-5802 — the day's manual hours combine with the dialog's intervals for the
  // 24h/overlap check, so an invalid mix cannot be committed from the dialog.
  const dialogManualHours = dialogDate ? (manualHours?.get(dialogDate) ?? 0) : 0
  const dialogError = useMemo(
    () => validateWorkDay(dialogIntervals.filter(iv => iv.start && iv.end), dialogManualHours),
    [dialogIntervals, dialogManualHours]
  )

  const handleDialogSave = useCallback(() => {
    if (dialogDate && onWorkIntervalsChange) {
      const validIntervals = dialogIntervals.filter(iv => iv.start && iv.end)
      // Block save when invalid — keep the dialog open so the user can correct it.
      if (validateWorkDay(validIntervals, manualHours?.get(dialogDate) ?? 0)) return
      onWorkIntervalsChange(dialogDate, validIntervals)
    }
    setDialogDate(null)
  }, [dialogDate, dialogIntervals, onWorkIntervalsChange, manualHours])

  const handleDialogClose = useCallback((open: boolean) => {
    if (!open) {
      // Save on close — but never persist an invalid (overlapping / >24h) day.
      if (dialogDate && onWorkIntervalsChange) {
        const validIntervals = dialogIntervals.filter(iv => iv.start && iv.end)
        if (!validateWorkDay(validIntervals, manualHours?.get(dialogDate) ?? 0)) {
          onWorkIntervalsChange(dialogDate, validIntervals)
        }
      }
      setDialogDate(null)
    }
  }, [dialogDate, dialogIntervals, onWorkIntervalsChange, manualHours])

  const dialogTotalHours = useMemo(() => calcIntervalHours(dialogIntervals), [dialogIntervals])

  return (
    <>
      <div className={`${styles.gridWrapper} ${readOnly ? styles.gridReadOnly : ''}`}>
        <table className={styles.grid}>
          <thead>
            <tr>
              <th className={styles.labelHeader}></th>
              {days.map((day) => (
                <th key={formatDateKey(day)} className={`${styles.dayHeader} ${getColumnClass(day)}`}>
                  <div className={styles.dayAbbrev}>{DA_DAY_ABBREV[day.getDay()]}</div>
                  <div className={styles.dayNumber}>{day.getDate()}</div>
                </th>
              ))}
              <th className={styles.sumHeader}>Sum</th>
            </tr>
          </thead>
          <tbody>
            {/* "Tilføj periode" row — work intervals via dialog */}
            {workIntervals && onWorkIntervalsChange && (
              <tr className={styles.arbejdstidRow}>
                <td className={styles.rowLabel}>Tilføj periode</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const hours = periodHoursPerDay.get(dateKey) ?? 0
                  return (
                    <td
                      key={dateKey}
                      className={`${styles.cell} ${styles.arbejdstidCell} ${getColumnClass(day)} ${!readOnly ? styles.clickable : ''}`}
                      onClick={() => openDialog(dateKey)}
                    >
                      <span className={styles.arbejdstidDisplay}>
                        {hours > 0 ? formatHours(hours) : ''}
                      </span>
                    </td>
                  )
                })}
                <td className={styles.sumCell}>
                  {periodHoursSum > 0 ? formatHours(periodHoursSum) : ''}
                </td>
              </tr>
            )}

            {/* "Tilføj timer" row — direct numeric daily hours */}
            {manualHours && onManualHoursChange && (
              <tr className={styles.manualHoursRow}>
                <td className={styles.rowLabel}>Tilføj timer</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const val = manualHours.get(dateKey)
                  // S58 TASK-5802 — flag a day whose combined worked time exceeds 24h.
                  const over24 = (workedPerDay.get(dateKey) ?? 0) > 24
                  return (
                    <td
                      key={dateKey}
                      className={`${styles.cell} ${over24 ? styles.allocOver : ''} ${getColumnClass(day)}`}
                      title={over24 ? 'Arbejdstid må ikke overstige 24 timer på en dag' : undefined}
                    >
                      {readOnly ? (
                        <span className={styles.cellDisplay}>
                          {val != null ? formatDanishNumber(val, 2) : ''}
                        </span>
                      ) : (
                        <input
                          type="text"
                          inputMode="decimal"
                          className={styles.cellInput}
                          value={val != null ? formatDanishNumber(val, 2) : ''}
                          onChange={(e) => handleManualHoursChange(dateKey, e.target.value)}
                        />
                      )}
                    </td>
                  )
                })}
                <td className={styles.sumCell}>
                  {manualHoursSum > 0 ? formatDanishNumber(manualHoursSum, 2) : ''}
                </td>
              </tr>
            )}

            {/* "Diff. fra normtid" row */}
            <tr className={styles.diffRow}>
              <td className={styles.rowLabel}>Diff. fra normtid</td>
              {days.map((day) => {
                const dateKey = formatDateKey(day)
                const diff = diffPerDay.get(dateKey)
                return (
                  <td
                    key={dateKey}
                    className={`${styles.cell} ${styles.diffCell} ${getColumnClass(day)}`}
                  >
                    <span className={styles.diffDisplay}>
                      {diff !== undefined ? formatDiff(diff) : ''}
                    </span>
                  </td>
                )
              })}
              <td className={styles.sumCell}>
                {formatDiff(diffSum)}
              </td>
            </tr>

            {/* Project rows */}
            {projectRows.map((row) => (
              <tr key={row.key}>
                <td className={styles.rowLabel}>{row.label}</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const cellKey = `${row.key}:${dateKey}`
                  const val = cellValues.get(cellKey)
                  return (
                    <td key={dateKey} className={`${styles.cell} ${getColumnClass(day)}`}>
                      {readOnly ? (
                        <span className={styles.cellDisplay}>{val != null ? val : ''}</span>
                      ) : (
                        <input
                          type="number"
                          step="0.1"
                          min="0"
                          max="24"
                          className={styles.cellInput}
                          value={val ?? ''}
                          onChange={(e) => handleCellChange(row.key, dateKey, e.target.value)}
                        />
                      )}
                    </td>
                  )
                })}
                <td className={styles.sumCell}>{rowSums.get(row.key) || ''}</td>
              </tr>
            ))}

            {/* Ikke fordelt (unallocated) row — read-only computed */}
            {(workIntervals || manualHours) && (
              <tr className={styles.unallocatedRow}>
                <td className={styles.rowLabel}>Ikke fordelt</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const worked = workedPerDay.get(dateKey) ?? 0
                  const allocated = allocatedPerDay.get(dateKey) ?? 0
                  const value = unallocatedPerDay.get(dateKey)
                  const hasValue = value !== undefined
                  const state = hasValue ? classifyAllocation(worked, allocated) : null
                  const stateClass =
                    state === 'balanced' ? styles.allocBalanced
                      : state === 'under' ? styles.allocUnder
                      : state === 'over' ? styles.allocOver
                      : ''
                  return (
                    <td
                      key={dateKey}
                      className={`${styles.cell} ${styles.unallocatedCell} ${stateClass} ${getColumnClass(day)}`}
                    >
                      <span className={styles.unallocatedDisplay}>
                        {!hasValue ? '' : state === 'balanced' ? '✓' : formatSignedHours(value)}
                      </span>
                    </td>
                  )
                })}
                <td
                  className={`${styles.sumCell} ${allDaysBalanced ? styles.allocBalanced : styles.allocUnbalanced}`}
                  title={allDaysBalanced ? undefined : 'Ikke alle dage er fordelt — godkendelse blokeres til hver dag balancerer'}
                >
                  {allDaysBalanced ? '✓' : formatSignedHours(unallocatedMonth)}
                </td>
              </tr>
            )}

            {/* Separator */}
            {absenceRows.length > 0 && (
              <tr className={styles.separatorRow}>
                <td colSpan={days.length + 2} className={styles.separator}></td>
              </tr>
            )}

            {/* Absence rows */}
            {absenceRows.map((row) => (
              <tr key={row.key}>
                <td className={styles.rowLabel}>{row.label}</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const cellKey = `${row.key}:${dateKey}`
                  const val = cellValues.get(cellKey)
                  return (
                    <td key={dateKey} className={`${styles.cell} ${getColumnClass(day)}`}>
                      {readOnly ? (
                        <span className={styles.cellDisplay}>{val != null ? val : ''}</span>
                      ) : (
                        <input
                          type="number"
                          step="0.1"
                          min="0"
                          max="24"
                          className={styles.cellInput}
                          value={val ?? ''}
                          onFocus={() => handleAbsenceFocus(row.key, dateKey, val)}
                          onChange={(e) => handleCellChange(row.key, dateKey, e.target.value)}
                        />
                      )}
                    </td>
                  )
                })}
                <td className={styles.sumCell}>{rowSums.get(row.key) || ''}</td>
              </tr>
            ))}

            {/* Total row */}
            <tr className={styles.totalRow}>
              <td className={styles.rowLabel}>Total</td>
              {days.map((day) => {
                const dateKey = formatDateKey(day)
                const total = dayTotals.get(dateKey) ?? 0
                return (
                  <td key={dateKey} className={`${styles.totalCell} ${getColumnClass(day)}`}>
                    {total || ''}
                  </td>
                )
              })}
              <td className={styles.grandTotal}>{grandTotal || ''}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {/* Work intervals dialog */}
      <Dialog
        open={dialogDate !== null}
        onOpenChange={handleDialogClose}
        title={`Arbejdstid — ${dialogDate ? formatDateLabel(dialogDate) : ''}`}
        description="Registrer dine arbejdsperioder for dagen."
      >
        <div className={styles.intervalsContainer}>
          {dialogIntervals.map((iv, index) => (
            <div key={index} className={styles.intervalRow}>
              <label className={styles.intervalLabel}>Fra</label>
              <input
                type="time"
                className={styles.intervalTimeInput}
                value={iv.start}
                onChange={(e) => handleIntervalChange(index, 'start', e.target.value)}
              />
              <label className={styles.intervalLabel}>Til</label>
              <input
                type="time"
                className={styles.intervalTimeInput}
                value={iv.end}
                onChange={(e) => handleIntervalChange(index, 'end', e.target.value)}
              />
              {dialogIntervals.length > 1 && (
                <button
                  className={styles.removeIntervalBtn}
                  onClick={() => handleRemoveInterval(index)}
                  aria-label="Fjern periode"
                >
                  &#x2715;
                </button>
              )}
            </div>
          ))}

          <Button variant="ghost" size="sm" onClick={handleAddInterval}>
            + Tilfoej periode
          </Button>

          <div className={styles.intervalSummary}>
            <span>Total: <strong>{formatHours(dialogTotalHours)}</strong></span>
          </div>

          {dialogError && (
            <div className={styles.intervalError} role="alert">{dialogError}</div>
          )}

          <div className={styles.intervalActions}>
            <Button variant="primary" size="sm" onClick={handleDialogSave} disabled={!!dialogError}>
              Gem
            </Button>
          </div>
        </div>
      </Dialog>
    </>
  )
}
