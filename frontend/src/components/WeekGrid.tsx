import type { TimeEntry, AbsenceEntry } from '../types'
import { Badge } from './ui'
import styles from './WeekGrid.module.css'

interface Props {
  weekStart: string
  entries: TimeEntry[]
  absences: AbsenceEntry[]
}

const DAY_NAMES = ['Man', 'Tir', 'Ons', 'Tor', 'Fre', 'Lor', 'Son']

export function WeekGrid({ weekStart, entries, absences }: Props) {
  const startDate = new Date(weekStart)
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(startDate)
    d.setDate(d.getDate() + i)
    return d.toISOString().split('T')[0]
  })

  const totalHours = entries.reduce((sum, e) => sum + e.hours, 0)
  const normMet = totalHours >= 37
  const normStatus = normMet ? 'Norm opfyldt' : `${(37 - totalHours).toFixed(1)}t under norm`

  return (
    <div className={styles.grid}>
      <table className={styles.table}>
        <thead>
          <tr>
            {DAY_NAMES.map((name, i) => (
              <th key={i} className={styles.dayHeader}>
                {name}<br />
                <span className={styles.dayHeaderDate}>{days[i]}</span>
              </th>
            ))}
            <th className={styles.dayHeader}>Total</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            {days.map((day) => {
              const dayEntries = entries.filter(e => e.date === day)
              const dayAbsences = absences.filter(a => a.date === day)
              const dayHours = dayEntries.reduce((sum, e) => sum + e.hours, 0)
              const absenceHours = dayAbsences.reduce((sum, a) => sum + a.hours, 0)

              return (
                <td key={day} className={styles.dayCell}>
                  {dayHours > 0 && <div>{dayHours.toFixed(1)}t</div>}
                  {dayAbsences.map((a, i) => (
                    <div key={i} className={styles.absenceText}>
                      {a.absenceType} ({absenceHours.toFixed(1)}t)
                    </div>
                  ))}
                  {dayHours === 0 && dayAbsences.length === 0 && (
                    <div className={styles.emptyDay}>-</div>
                  )}
                </td>
              )
            })}
            <td className={styles.totalCell}>
              {totalHours.toFixed(1)}t
            </td>
          </tr>
        </tbody>
      </table>
      <Badge variant={normMet ? 'success' : 'warning'}>
        {normStatus}
      </Badge>
    </div>
  )
}
