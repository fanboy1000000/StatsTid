import { useState } from 'react'
import { AGREEMENT_CODES } from '../types'
import { FormField, Input, Button } from './ui'
import styles from './TimeEntryForm.module.css'

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
  const [submitError, setSubmitError] = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setSubmitError(null)
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
    } catch (err) {
      // S128 / TASK-12803 — surface the server's refusal instead of swallowing it as an
      // unhandled rejection (the try/finally had no catch). The load-bearing case is the new
      // period-locked 409 (ApprovalPeriodSaveLock): its body is `{ "error": "..." }`, which
      // useTimeEntries throws as the raw body text — parse it for the message, fall back raw.
      let message = err instanceof Error ? err.message : String(err)
      try {
        const parsed: unknown = JSON.parse(message)
        if (parsed && typeof parsed === 'object' && 'error' in parsed
            && typeof (parsed as { error: unknown }).error === 'string') {
          message = (parsed as { error: string }).error
        }
      } catch {
        // Not a JSON body — keep the raw message.
      }
      setSubmitError(message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className={styles.form}>
      <FormField label="Dato" htmlFor="entry-date" required>
        <Input
          id="entry-date"
          type="date"
          value={date}
          onChange={e => setDate(e.target.value)}
          required
        />
      </FormField>
      <FormField label="Timer" htmlFor="entry-hours" required>
        <Input
          id="entry-hours"
          type="number"
          step="0.1"
          value={hours}
          onChange={e => setHours(e.target.value)}
          required
        />
      </FormField>
      <FormField label="Starttid" htmlFor="entry-start">
        <Input
          id="entry-start"
          type="time"
          value={startTime}
          onChange={e => setStartTime(e.target.value)}
        />
      </FormField>
      <FormField label="Sluttid" htmlFor="entry-end">
        <Input
          id="entry-end"
          type="time"
          value={endTime}
          onChange={e => setEndTime(e.target.value)}
        />
      </FormField>
      <FormField label="Opgave" htmlFor="entry-task">
        <Input
          id="entry-task"
          type="text"
          value={taskId}
          onChange={e => setTaskId(e.target.value)}
          placeholder="Valgfri"
        />
      </FormField>
      <FormField label="Aktivitetstype" htmlFor="entry-activity">
        <Input
          id="entry-activity"
          type="text"
          value={activityType}
          onChange={e => setActivityType(e.target.value)}
          placeholder="Valgfri"
        />
      </FormField>
      <FormField label="Overenskomst" htmlFor="entry-agreement">
        <select
          id="entry-agreement"
          className={styles.select}
          value={agreementCode}
          onChange={e => setAgreementCode(e.target.value)}
        >
          {AGREEMENT_CODES.map(c => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
      </FormField>
      {submitError && (
        <div className={styles.alert} role="alert" data-testid="time-entry-error">
          {submitError}
        </div>
      )}
      <div className={styles.actions}>
        <Button type="submit" disabled={submitting}>
          {submitting ? 'Registrerer...' : 'Registrer tid'}
        </Button>
      </div>
    </form>
  )
}
