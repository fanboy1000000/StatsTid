import { useState } from 'react'
import { useAbsences } from '../hooks/useAbsences'
import { useAuth } from '../hooks/useAuth'
import { ABSENCE_TYPES, AGREEMENT_CODES } from '../types'
import { Card, FormField, Input, Button, Table, Alert, Badge, Spinner } from '../components/ui'
import styles from './AbsenceRegistration.module.css'

export function AbsenceRegistration() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''
  const { absences, loading, error, registerAbsence } = useAbsences(employeeId)

  const [date, setDate] = useState('')
  const [absenceType, setAbsenceType] = useState<string>(ABSENCE_TYPES[0].value)
  const [hours, setHours] = useState('7.4')
  const [agreementCode, setAgreementCode] = useState('AC')
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    try {
      await registerAbsence({
        employeeId,
        date,
        absenceType,
        hours: parseFloat(hours),
        agreementCode,
        okVersion: 'OK24',
      })
      setHours('7.4')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Fravaersregistrering</h2>

      <Card header="Registrer fravaer">
        <form onSubmit={handleSubmit} className={styles.form}>
          <FormField label="Dato" htmlFor="absence-date" required>
            <Input
              id="absence-date"
              type="date"
              value={date}
              onChange={e => setDate(e.target.value)}
              required
            />
          </FormField>
          <FormField label="Type" htmlFor="absence-type" required>
            <select
              id="absence-type"
              className={styles.select}
              value={absenceType}
              onChange={e => setAbsenceType(e.target.value)}
            >
              {ABSENCE_TYPES.map(t => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </FormField>
          <FormField label="Timer" htmlFor="absence-hours" required>
            <Input
              id="absence-hours"
              type="number"
              step="0.1"
              value={hours}
              onChange={e => setHours(e.target.value)}
              required
            />
          </FormField>
          <FormField label="Overenskomst" htmlFor="absence-agreement">
            <select
              id="absence-agreement"
              className={styles.select}
              value={agreementCode}
              onChange={e => setAgreementCode(e.target.value)}
            >
              {AGREEMENT_CODES.map(c => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </FormField>
          <div className={styles.actions}>
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Registrerer...' : 'Registrer fravaer'}
            </Button>
          </div>
        </form>
      </Card>

      <h3 className={styles.sectionTitle}>Registreret fravaer</h3>
      {loading && (
        <div className={styles.loadingWrapper}>
          <Spinner size="sm" />
          <span>Henter...</span>
        </div>
      )}
      {error && <Alert variant="error">{error}</Alert>}
      {!loading && absences.length === 0 && <p className={styles.empty}>Ingen fravaer fundet.</p>}
      {absences.length > 0 && (
        <Table headers={['Dato', 'Type', 'Timer', 'Overenskomst']} striped>
          {absences.map((a, i) => (
            <tr key={i}>
              <td>{a.date}</td>
              <td><Badge variant="info">{a.absenceType}</Badge></td>
              <td>{a.hours}</td>
              <td>{a.agreementCode}</td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  )
}
