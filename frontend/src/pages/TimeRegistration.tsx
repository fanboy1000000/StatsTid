import { useState } from 'react'
import { TimeEntryForm } from '../components/TimeEntryForm'
import { useTimeEntries } from '../hooks/useTimeEntries'

export function TimeRegistration() {
  const [employeeId, setEmployeeId] = useState('EMP001')
  const { entries, loading, error, registerEntry } = useTimeEntries(employeeId)

  return (
    <div>
      <h2>Tidsregistrering</h2>

      <label>
        Medarbejder-ID:
        <input
          type="text"
          value={employeeId}
          onChange={e => setEmployeeId(e.target.value)}
          style={{ marginLeft: 8, marginBottom: 16 }}
        />
      </label>

      <h3>Registrer ny tid</h3>
      <TimeEntryForm
        employeeId={employeeId}
        onSubmit={async (entry) => { await registerEntry(entry) }}
      />

      <h3 style={{ marginTop: 24 }}>Registrerede timer</h3>
      {loading && <p>Henter...</p>}
      {error && <p style={{ color: 'red' }}>{error}</p>}
      {!loading && entries.length === 0 && <p>Ingen registreringer fundet.</p>}
      {entries.length > 0 && (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={thStyle}>Dato</th>
              <th style={thStyle}>Timer</th>
              <th style={thStyle}>Start</th>
              <th style={thStyle}>Slut</th>
              <th style={thStyle}>Overenskomst</th>
              <th style={thStyle}>Opgave</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry, i) => (
              <tr key={i}>
                <td style={tdStyle}>{entry.date}</td>
                <td style={tdStyle}>{entry.hours}</td>
                <td style={tdStyle}>{entry.startTime ?? '-'}</td>
                <td style={tdStyle}>{entry.endTime ?? '-'}</td>
                <td style={tdStyle}>{entry.agreementCode}</td>
                <td style={tdStyle}>{entry.taskId ?? '-'}</td>
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
