import { useState } from 'react'
import { useAbsences } from '../hooks/useAbsences'
import { ABSENCE_TYPES, AGREEMENT_CODES } from '../types'

export function AbsenceRegistration() {
  const [employeeId, setEmployeeId] = useState('EMP001')
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
    <div>
      <h2>Fravaersregistrering</h2>

      <label>
        Medarbejder-ID:
        <input type="text" value={employeeId} onChange={e => setEmployeeId(e.target.value)} style={{ marginLeft: 8, marginBottom: 16 }} />
      </label>

      <h3>Registrer fravaer</h3>
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 400 }}>
        <label>
          Dato:
          <input type="date" value={date} onChange={e => setDate(e.target.value)} required />
        </label>
        <label>
          Type:
          <select value={absenceType} onChange={e => setAbsenceType(e.target.value)}>
            {ABSENCE_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
          </select>
        </label>
        <label>
          Timer:
          <input type="number" step="0.1" value={hours} onChange={e => setHours(e.target.value)} required />
        </label>
        <label>
          Overenskomst:
          <select value={agreementCode} onChange={e => setAgreementCode(e.target.value)}>
            {AGREEMENT_CODES.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
        <button type="submit" disabled={submitting}>
          {submitting ? 'Registrerer...' : 'Registrer fravaer'}
        </button>
      </form>

      <h3 style={{ marginTop: 24 }}>Registreret fravaer</h3>
      {loading && <p>Henter...</p>}
      {error && <p style={{ color: 'red' }}>{error}</p>}
      {!loading && absences.length === 0 && <p>Ingen fravaer fundet.</p>}
      {absences.length > 0 && (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={thStyle}>Dato</th>
              <th style={thStyle}>Type</th>
              <th style={thStyle}>Timer</th>
              <th style={thStyle}>Overenskomst</th>
            </tr>
          </thead>
          <tbody>
            {absences.map((a, i) => (
              <tr key={i}>
                <td style={tdStyle}>{a.date}</td>
                <td style={tdStyle}>{a.absenceType}</td>
                <td style={tdStyle}>{a.hours}</td>
                <td style={tdStyle}>{a.agreementCode}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

const thStyle: React.CSSProperties = { textAlign: 'left', padding: 8, borderBottom: '2px solid #333' }
const tdStyle: React.CSSProperties = { padding: 8, borderBottom: '1px solid #ccc' }
