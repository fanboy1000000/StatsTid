import { TimeEntryForm } from '../components/TimeEntryForm'
import { useTimeEntries } from '../hooks/useTimeEntries'
import { useAuth } from '../hooks/useAuth'
import { Table, Spinner, Alert, Card } from '../components/ui'
import styles from './TimeRegistration.module.css'

export function TimeRegistration() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''
  const { entries, loading, error, registerEntry } = useTimeEntries(employeeId)

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Tidsregistrering</h2>

      <Card header="Registrer ny tid">
        <TimeEntryForm
          employeeId={employeeId}
          onSubmit={async (entry) => { await registerEntry(entry) }}
        />
      </Card>

      <h3 className={styles.sectionTitle}>Registrerede timer</h3>
      {loading && (
        <div className={styles.loadingWrapper}>
          <Spinner size="sm" />
          <span>Henter...</span>
        </div>
      )}
      {error && <Alert variant="error">{error}</Alert>}
      {!loading && entries.length === 0 && <p className={styles.empty}>Ingen registreringer fundet.</p>}
      {entries.length > 0 && (
        <Table headers={['Dato', 'Timer', 'Start', 'Slut', 'Overenskomst', 'Opgave']} striped>
          {entries.map((entry, i) => (
            <tr key={i}>
              <td>{entry.date}</td>
              <td>{entry.hours}</td>
              <td>{entry.startTime ?? '-'}</td>
              <td>{entry.endTime ?? '-'}</td>
              <td>{entry.agreementCode}</td>
              <td>{entry.taskId ?? '-'}</td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  )
}
