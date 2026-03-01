import { useState, useMemo } from 'react'
import { WeekGrid } from '../components/WeekGrid'
import { FlexBalanceCard } from '../components/FlexBalanceCard'
import { useTimeEntries } from '../hooks/useTimeEntries'
import { useAbsences } from '../hooks/useAbsences'
import { useFlexBalance } from '../hooks/useFlexBalance'

function getMonday(dateStr: string): string {
  const d = new Date(dateStr)
  const day = d.getDay()
  const diff = d.getDate() - day + (day === 0 ? -6 : 1)
  d.setDate(diff)
  return d.toISOString().split('T')[0]
}

export function WeeklyView() {
  const [employeeId, setEmployeeId] = useState('EMP001')
  const today = new Date().toISOString().split('T')[0]
  const [selectedDate, setSelectedDate] = useState(today)

  const weekStart = useMemo(() => getMonday(selectedDate), [selectedDate])
  const weekEnd = useMemo(() => {
    const d = new Date(weekStart)
    d.setDate(d.getDate() + 6)
    return d.toISOString().split('T')[0]
  }, [weekStart])

  const { entries } = useTimeEntries(employeeId)
  const { absences } = useAbsences(employeeId)
  const { flexBalance, loading: flexLoading } = useFlexBalance(employeeId)

  const weekEntries = useMemo(
    () => entries.filter(e => e.date >= weekStart && e.date <= weekEnd),
    [entries, weekStart, weekEnd]
  )
  const weekAbsences = useMemo(
    () => absences.filter(a => a.date >= weekStart && a.date <= weekEnd),
    [absences, weekStart, weekEnd]
  )

  return (
    <div>
      <h2>Ugeoversigt</h2>

      <div style={{ display: 'flex', gap: 16, marginBottom: 16, alignItems: 'center' }}>
        <label>
          Medarbejder:
          <input type="text" value={employeeId} onChange={e => setEmployeeId(e.target.value)} style={{ marginLeft: 8 }} />
        </label>
        <label>
          Uge for dato:
          <input type="date" value={selectedDate} onChange={e => setSelectedDate(e.target.value)} style={{ marginLeft: 8 }} />
        </label>
        <span>Uge: {weekStart} - {weekEnd}</span>
      </div>

      <div style={{ display: 'flex', gap: 24, alignItems: 'flex-start' }}>
        <div style={{ flex: 1 }}>
          <WeekGrid weekStart={weekStart} entries={weekEntries} absences={weekAbsences} />
        </div>
        <FlexBalanceCard flexBalance={flexBalance} loading={flexLoading} />
      </div>
    </div>
  )
}
