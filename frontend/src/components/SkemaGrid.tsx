import { useMemo, useCallback } from 'react'
import type { SkemaRow } from '../types'
import styles from './SkemaGrid.module.css'

interface SkemaGridProps {
  year: number
  month: number
  rows: SkemaRow[]
  cellValues: Map<string, number>
  readOnly: boolean
  onCellChange: (rowKey: string, date: string, hours: number | null) => void
  timerHoursToday?: number
  arrivalDepartures?: Map<string, { arrival: string | null; departure: string | null }>
  onArrivalDepartureChange?: (date: string, field: 'arrival' | 'departure', value: string) => void
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

export function SkemaGrid({
  year,
  month,
  rows,
  cellValues,
  readOnly,
  onCellChange,
  arrivalDepartures,
  onArrivalDepartureChange,
}: SkemaGridProps) {
  const days = useMemo(() => getDaysInMonth(year, month), [year, month])

  const projectRows = useMemo(() => rows.filter((r) => r.type === 'project'), [rows])
  const absenceRows = useMemo(() => rows.filter((r) => r.type === 'absence'), [rows])

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

  const handleCellChange = useCallback(
    (rowKey: string, date: string, value: string) => {
      const num = value === '' ? null : parseFloat(value)
      if (num !== null && isNaN(num)) return
      onCellChange(rowKey, date, num === 0 ? null : num)
    },
    [onCellChange]
  )

  return (
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
          {/* Arrival/Departure row */}
          {arrivalDepartures && onArrivalDepartureChange && (
            <>
              <tr className={styles.arrivalRow}>
                <td className={styles.rowLabel}>Ankomst</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const todayCell = isToday(day)
                  const ad = arrivalDepartures.get(dateKey)
                  return (
                    <td key={dateKey} className={`${styles.cell} ${getColumnClass(day)}`}>
                      {todayCell && !readOnly ? (
                        <input
                          type="time"
                          className={styles.timeInput}
                          value={ad?.arrival ?? ''}
                          onChange={(e) => onArrivalDepartureChange(dateKey, 'arrival', e.target.value)}
                        />
                      ) : (
                        <span className={styles.timeDisplay}>{ad?.arrival ?? ''}</span>
                      )}
                    </td>
                  )
                })}
                <td className={styles.sumCell}></td>
              </tr>
              <tr className={styles.departureRow}>
                <td className={styles.rowLabel}>Afgang</td>
                {days.map((day) => {
                  const dateKey = formatDateKey(day)
                  const todayCell = isToday(day)
                  const ad = arrivalDepartures.get(dateKey)
                  return (
                    <td key={dateKey} className={`${styles.cell} ${getColumnClass(day)}`}>
                      {todayCell && !readOnly ? (
                        <input
                          type="time"
                          className={styles.timeInput}
                          value={ad?.departure ?? ''}
                          onChange={(e) => onArrivalDepartureChange(dateKey, 'departure', e.target.value)}
                        />
                      ) : (
                        <span className={styles.timeDisplay}>{ad?.departure ?? ''}</span>
                      )}
                    </td>
                  )
                })}
                <td className={styles.sumCell}></td>
              </tr>
            </>
          )}

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
  )
}
