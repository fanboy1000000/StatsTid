import type { TimeEntry, AbsenceEntry } from '../types'

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
  const normStatus = totalHours >= 37 ? 'Norm opfyldt' : `${(37 - totalHours).toFixed(1)}t under norm`

  return (
    <div>
      <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: 12 }}>
        <thead>
          <tr>
            {DAY_NAMES.map((name, i) => (
              <th key={i} style={{ padding: 8, borderBottom: '2px solid #333', textAlign: 'center', minWidth: 80 }}>
                {name}<br />
                <small>{days[i]}</small>
              </th>
            ))}
            <th style={{ padding: 8, borderBottom: '2px solid #333' }}>Total</th>
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
                <td key={day} style={{ padding: 8, borderBottom: '1px solid #ccc', textAlign: 'center', verticalAlign: 'top' }}>
                  {dayHours > 0 && <div>{dayHours.toFixed(1)}t</div>}
                  {dayAbsences.map((a, i) => (
                    <div key={i} style={{ fontSize: '0.8em', color: '#666' }}>
                      {a.absenceType} ({absenceHours.toFixed(1)}t)
                    </div>
                  ))}
                  {dayHours === 0 && dayAbsences.length === 0 && <div style={{ color: '#ccc' }}>-</div>}
                </td>
              )
            })}
            <td style={{ padding: 8, borderBottom: '1px solid #ccc', textAlign: 'center', fontWeight: 'bold' }}>
              {totalHours.toFixed(1)}t
            </td>
          </tr>
        </tbody>
      </table>
      <div style={{
        padding: 8,
        backgroundColor: totalHours >= 37 ? '#e6ffe6' : '#fff3e6',
        borderRadius: 4,
        display: 'inline-block'
      }}>
        {normStatus}
      </div>
    </div>
  )
}
