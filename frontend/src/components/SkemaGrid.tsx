import { useState, useMemo, useCallback } from 'react'
import type { SkemaRow } from '../types'
import { Dialog } from './ui/Dialog'
import { Button } from './ui/Button'
import styles from './SkemaGrid.module.css'

export interface WorkInterval {
  start: string  // "HH:mm" or "HH:mm:ss"
  end: string    // "HH:mm" or "HH:mm:ss"
}

export type WorkIntervalsMap = Map<string, WorkInterval[]>  // dateKey -> intervals

interface SkemaGridProps {
  year: number
  month: number
  rows: SkemaRow[]
  cellValues: Map<string, number>
  readOnly: boolean
  onCellChange: (rowKey: string, date: string, hours: number | null) => void
  timerHoursToday?: number
  workIntervals?: WorkIntervalsMap
  onWorkIntervalsChange?: (date: string, intervals: WorkInterval[]) => void
  dailyNormHours?: number  // e.g. 7.4 for 37h/week
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
  dailyNormHours = 7.4,
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

  // Compute work hours per day from intervals
  const workHoursPerDay = useMemo(() => {
    const map = new Map<string, number>()
    if (workIntervals) {
      for (const [dateKey, intervals] of workIntervals) {
        const hours = calcIntervalHours(intervals)
        if (hours > 0) map.set(dateKey, hours)
      }
    }
    return map
  }, [workIntervals])

  // Work hours sum for the month
  const workHoursSum = useMemo(() => {
    let sum = 0
    for (const h of workHoursPerDay.values()) sum += h
    return Math.round(sum * 100) / 100
  }, [workHoursPerDay])

  // Difference per day: arbejdstid hours - norm (only when hours are registered)
  const diffPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const day of days) {
      const dateKey = formatDateKey(day)
      const worked = workHoursPerDay.get(dateKey) ?? 0
      if (worked > 0) {
        const norm = isWeekend(day) ? 0 : dailyNormHours
        map.set(dateKey, Math.round((worked - norm) * 100) / 100)
      }
    }
    return map
  }, [days, workHoursPerDay, dailyNormHours])

  const diffSum = useMemo(() => {
    let sum = 0
    for (const d of diffPerDay.values()) sum += d
    return Math.round(sum * 100) / 100
  }, [diffPerDay])

  const handleCellChange = useCallback(
    (rowKey: string, date: string, value: string) => {
      const num = value === '' ? null : parseFloat(value)
      if (num !== null && isNaN(num)) return
      onCellChange(rowKey, date, num === 0 ? null : num)
    },
    [onCellChange]
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

  const handleDialogSave = useCallback(() => {
    if (dialogDate && onWorkIntervalsChange) {
      const validIntervals = dialogIntervals.filter(iv => iv.start && iv.end)
      onWorkIntervalsChange(dialogDate, validIntervals)
    }
    setDialogDate(null)
  }, [dialogDate, dialogIntervals, onWorkIntervalsChange])

  const handleDialogClose = useCallback((open: boolean) => {
    if (!open) {
      // Save on close
      if (dialogDate && onWorkIntervalsChange) {
        const validIntervals = dialogIntervals.filter(iv => iv.start && iv.end)
        onWorkIntervalsChange(dialogDate, validIntervals)
      }
      setDialogDate(null)
    }
  }, [dialogDate, dialogIntervals, onWorkIntervalsChange])

  const dialogTotalHours = useMemo(() => calcIntervalHours(dialogIntervals), [dialogIntervals])

  return (
    <>
      <div className={styles.gridWrapper}>
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
            {/* Arbejdstid row */}
            {workIntervals && onWorkIntervalsChange && (
              <tr className={styles.arbejdstidRow}>
                <td className={styles.rowLabel}>Arbejdstid</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const hours = workHoursPerDay.get(dateKey) ?? 0
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
                  {workHoursSum > 0 ? formatHours(workHoursSum) : ''}
                </td>
              </tr>
            )}

            {/* Difference row */}
            <tr className={styles.diffRow}>
              <td className={styles.rowLabel}>Difference</td>
              {days.map((day) => {
                const dateKey = formatDateKey(day)
                const diff = diffPerDay.get(dateKey)
                const hasValue = diff !== undefined && diff !== 0
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
                        <span className={styles.cellDisplay}>{val ? val : ''}</span>
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
                        <span className={styles.cellDisplay}>{val ? val : ''}</span>
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

          <div className={styles.intervalActions}>
            <Button variant="primary" size="sm" onClick={handleDialogSave}>
              Gem
            </Button>
          </div>
        </div>
      </Dialog>
    </>
  )
}
