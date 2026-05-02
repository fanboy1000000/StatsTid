// Date picker that enforces two constraint flavours from S21 / ADR-017:
//
//   1. `pastOrTodayOnly` — reject future dates. The backend's ConfigEndpoints
//      PUT rejects `effectiveFrom > today` with `EFFECTIVE_FROM_NOT_TODAY_OR_PAST`
//      (D2 + cycle-2 fix). Always set true for profile saves.
//   2. `mondayOnly` — reject non-Mondays. The alignment policy for
//      `WeeklyNormHours` (LocalAgreementProfileAlignmentPolicies) requires
//      Monday. Set true only when WeeklyNormHours is in the changed-fields set,
//      since the backend only validates alignment for changed fields.
//
// Implementation: a plain `<input type="date">` with onChange-time validation.
// Invalid choices are rejected (the input is reset to the previously valid
// value, or to empty) and an inline message is shown so the user understands
// why. No new dependency added — Phase 5 may swap in a richer picker.
//
// Scope: basic functional. No animation, no min-attr-driven calendar
// shading, no theming.
import { useEffect, useState, type ChangeEvent } from 'react'

interface MondayDatePickerProps {
  id: string
  value: string  // ISO yyyy-MM-dd or empty string
  onChange: (next: string) => void
  mondayOnly: boolean
  pastOrTodayOnly: boolean
  disabled?: boolean
}

export function MondayDatePicker({
  id,
  value,
  onChange,
  mondayOnly,
  pastOrTodayOnly,
  disabled = false,
}: MondayDatePickerProps) {
  const [warning, setWarning] = useState<string | null>(null)

  // Compute today's date in the browser's local zone as ISO yyyy-MM-dd.
  // Backend uses DateOnly (no zone) and compares against UTC today; for the
  // typical CET/CEST admin user the day boundaries align closely enough that
  // local-zone today is the right UI default. Any drift is caught server-side.
  const today = formatLocalDate(new Date())
  const maxAttr = pastOrTodayOnly ? today : undefined

  // Re-validate the currently selected date when constraints flip on. Without
  // this, an admin who picks a non-Monday date and *then* enables a
  // Monday-aligned override (e.g. WeeklyNormHours) keeps the invalid date in
  // state and the PUT is rejected server-side. The dynamic prop exists
  // precisely to handle that transition in the UI.
  useEffect(() => {
    if (!value) return
    const parsed = parseIsoDate(value)
    if (!parsed) return
    if (pastOrTodayOnly && value > today) {
      setWarning('Datoen kan ikke vaere i fremtiden.')
      onChange('')
      return
    }
    if (mondayOnly && parsed.getUTCDay() !== 1) {
      setWarning('Datoen skal vaere en mandag (krav fra WeeklyNormHours).')
      onChange('')
      return
    }
    setWarning(null)
    // Intentionally only re-run when constraint props change — re-running on
    // every value change would clobber the in-progress edit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mondayOnly, pastOrTodayOnly])

  function handleChange(e: ChangeEvent<HTMLInputElement>) {
    const next = e.target.value
    if (!next) {
      setWarning(null)
      onChange('')
      return
    }

    // Parse as a UTC midnight date — Date constructor on yyyy-MM-dd treats it
    // as UTC, which gives consistent dayOfWeek across timezones.
    const parsed = parseIsoDate(next)
    if (!parsed) {
      setWarning('Ugyldig dato.')
      return
    }

    if (pastOrTodayOnly && next > today) {
      setWarning('Datoen kan ikke vaere i fremtiden.')
      // Do not propagate the value — keep the previous one.
      return
    }

    if (mondayOnly && parsed.getUTCDay() !== 1) {
      // 1 = Monday in JS UTC day-of-week (0 = Sunday).
      setWarning('Datoen skal vaere en mandag (krav fra WeeklyNormHours).')
      return
    }

    setWarning(null)
    onChange(next)
  }

  return (
    <div>
      <input
        id={id}
        type="date"
        value={value}
        max={maxAttr}
        onChange={handleChange}
        disabled={disabled}
      />
      {warning && (
        <div role="alert" style={{ color: 'var(--color-error, #dc2626)', fontSize: '0.8125rem', marginTop: '0.25rem' }}>
          {warning}
        </div>
      )}
    </div>
  )
}

function formatLocalDate(d: Date): string {
  const yyyy = d.getFullYear().toString().padStart(4, '0')
  const mm = (d.getMonth() + 1).toString().padStart(2, '0')
  const dd = d.getDate().toString().padStart(2, '0')
  return `${yyyy}-${mm}-${dd}`
}

function parseIsoDate(iso: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return null
  const year = Number(m[1])
  const month = Number(m[2])
  const day = Number(m[3])
  // UTC midnight — avoids local-zone dayOfWeek shifts.
  const d = new Date(Date.UTC(year, month - 1, day))
  if (
    d.getUTCFullYear() !== year ||
    d.getUTCMonth() !== month - 1 ||
    d.getUTCDate() !== day
  ) {
    return null
  }
  return d
}
