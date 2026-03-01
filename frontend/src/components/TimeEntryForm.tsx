import { useState } from 'react'
import { AGREEMENT_CODES } from '../types'

interface Props {
  employeeId: string
  onSubmit: (entry: {
    employeeId: string
    date: string
    hours: number
    startTime?: string
    endTime?: string
    taskId?: string
    activityType?: string
    agreementCode: string
    okVersion: string
  }) => Promise<void>
}

export function TimeEntryForm({ employeeId, onSubmit }: Props) {
  const [date, setDate] = useState('')
  const [hours, setHours] = useState('7.4')
  const [startTime, setStartTime] = useState('')
  const [endTime, setEndTime] = useState('')
  const [taskId, setTaskId] = useState('')
  const [activityType, setActivityType] = useState('')
  const [agreementCode, setAgreementCode] = useState('AC')
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    try {
      await onSubmit({
        employeeId,
        date,
        hours: parseFloat(hours),
        startTime: startTime || undefined,
        endTime: endTime || undefined,
        taskId: taskId || undefined,
        activityType: activityType || undefined,
        agreementCode,
        okVersion: 'OK24',
      })
      setHours('7.4')
      setStartTime('')
      setEndTime('')
      setTaskId('')
      setActivityType('')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 400 }}>
      <label>
        Dato:
        <input type="date" value={date} onChange={e => setDate(e.target.value)} required />
      </label>
      <label>
        Timer:
        <input type="number" step="0.1" value={hours} onChange={e => setHours(e.target.value)} required />
      </label>
      <label>
        Starttid:
        <input type="time" value={startTime} onChange={e => setStartTime(e.target.value)} />
      </label>
      <label>
        Sluttid:
        <input type="time" value={endTime} onChange={e => setEndTime(e.target.value)} />
      </label>
      <label>
        Opgave:
        <input type="text" value={taskId} onChange={e => setTaskId(e.target.value)} placeholder="Valgfri" />
      </label>
      <label>
        Aktivitetstype:
        <input type="text" value={activityType} onChange={e => setActivityType(e.target.value)} placeholder="Valgfri" />
      </label>
      <label>
        Overenskomst:
        <select value={agreementCode} onChange={e => setAgreementCode(e.target.value)}>
          {AGREEMENT_CODES.map(c => <option key={c} value={c}>{c}</option>)}
        </select>
      </label>
      <button type="submit" disabled={submitting}>
        {submitting ? 'Registrerer...' : 'Registrer tid'}
      </button>
    </form>
  )
}
