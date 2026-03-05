import { useState, useMemo } from 'react'
import { WeekGrid } from '../components/WeekGrid'
import { FlexBalanceCard } from '../components/FlexBalanceCard'
import { useTimeEntries } from '../hooks/useTimeEntries'
import { useAbsences } from '../hooks/useAbsences'
import { useFlexBalance } from '../hooks/useFlexBalance'
import { useAuth } from '../hooks/useAuth'
import { FormField, Input, Card } from '../components/ui'
import styles from './WeeklyView.module.css'

function getMonday(dateStr: string): string {
  const d = new Date(dateStr)
  const day = d.getDay()
  const diff = d.getDate() - day + (day === 0 ? -6 : 1)
  d.setDate(diff)
  return d.toISOString().split('T')[0]
}

export function WeeklyView() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''
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
    <div className={styles.page}>
      <h2 className={styles.title}>Ugeoversigt</h2>

      <div className={styles.controls}>
        <FormField label="Uge for dato" htmlFor="week-date">
          <Input
            id="week-date"
            type="date"
            value={selectedDate}
            onChange={e => setSelectedDate(e.target.value)}
          />
        </FormField>
        <span className={styles.weekRange}>Uge: {weekStart} - {weekEnd}</span>
      </div>

      <div className={styles.content}>
        <Card className={styles.gridWrapper}>
          <WeekGrid weekStart={weekStart} entries={weekEntries} absences={weekAbsences} />
        </Card>
        <FlexBalanceCard flexBalance={flexBalance} loading={flexLoading} />
      </div>
    </div>
  )
}
