// Date picker that enforces two constraint flavours from S21 / ADR-017:
//
//   1. `pastOrTodayOnly` — reject future dates. The backend's ConfigEndpoints
//      PUT rejects `effectiveFrom > today` with `EFFECTIVE_FROM_NOT_TODAY_OR_PAST`
//      (D2 + cycle-2 fix). Always set true for profile saves. S142: "today" on
//      BOTH sides is now the Europe/Copenhagen calendar day — see the note at
//      the `copenhagenToday()` call below.
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
import { copenhagenToday } from '../../lib/copenhagenDate'

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

  // S142 / TASK-14201 (census row 59) — today is the EUROPE/COPENHAGEN calendar day.
  //
  // This value does three things: it caps the native picker via `max` below, it gates the
  // onChange refusal, and it gates the re-validation effect. All three must agree with the
  // server, whose `ConfigEndpoints` PUT (census row 14, moved in the SAME commit) rejects
  // `effectiveFrom > today` against the Copenhagen day.
  //
  // The comment this replaces claimed the boundaries "align closely enough" for a CET/CEST admin
  // and that "any drift is caught server-side". Both halves were wrong, which is why it is quoted
  // rather than deleted. The drift was not caught: the server compared against the UTC day, so
  // between Danish midnight and UTC midnight the picker OFFERED a date the server then refused as
  // "in the future" — an admin working at 00:30 was told the day on their own wall calendar had
  // not arrived. And "align closely enough" only ever described a browser sitting in Denmark; the
  // browser zone is not a proxy for Copenhagen, it is a third calendar. An effective date is a
  // fact about Danish employment law, not about where the person filling in the form is sitting
  // (owner ruling OQ-1), so a laptop on New York time must still be offered — and must still
  // accept — the Danish today.
  // S142 / owner ruling OQ-12 (2026-09-17) — WHERE the "no degraded mode" rule is enforced, and
  // where it is PRESENTED, are deliberately two different places.
  //
  // `copenhagenToday()` still throws on a runtime that cannot resolve Europe/Copenhagen, and that
  // is right: it is the single source of truth for a business date, and a helper that quietly fell
  // back to the browser's zone would silently reinstate the exact defect S142 removes (OQ-11 took
  // the same line on the server, which refuses to boot).
  //
  // But a server and a browser fail differently. A server either starts or does not, and an
  // operator reads the log. An uncaught throw during RENDER blanks the whole config editor: the
  // admin gets a white screen, no explanation, and every unrelated field on the page becomes
  // unreachable too — for a fault that has nothing to do with those fields. So the UI boundary
  // catches it and disables THIS control with an explicit message. A wrong date still cannot be
  // entered and the failure is still loud; the damage is just confined to the control that
  // actually depends on the zone.
  //
  // Unreachable on any supported runtime — every current browser ships IANA tz data. Settled now
  // because it is cheap now and awkward once someone is staring at a blank screen.
  let today: string | null = null
  let zoneError: string | null = null
  try {
    today = copenhagenToday()
  } catch {
    zoneError =
      'Datoen kan ikke vaelges: denne browser kan ikke bestemme den danske kalenderdag ' +
      '(tidszonedata for Europe/Copenhagen mangler). Proev en anden browser eller opdater den.'
  }

  const zoneUnavailable = zoneError !== null
  const maxAttr = pastOrTodayOnly && today !== null ? today : undefined

  // Re-validate the currently selected date when constraints flip on. Without
  // this, an admin who picks a non-Monday date and *then* enables a
  // Monday-aligned override (e.g. WeeklyNormHours) keeps the invalid date in
  // state and the PUT is rejected server-side. The dynamic prop exists
  // precisely to handle that transition in the UI.
  useEffect(() => {
    if (!value) return
    const parsed = parseIsoDate(value)
    if (!parsed) return
    // `today === null` only when the zone is unresolvable (OQ-12). The control is disabled in that
    // state, so there is nothing to re-validate — and comparing against a missing day would be
    // guessing, which is the one thing this sprint refuses to do with a business date.
    if (pastOrTodayOnly && today !== null && value > today) {
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

    if (pastOrTodayOnly && today !== null && next > today) {
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
        disabled={disabled || zoneUnavailable}
      />
      {zoneError && (
        <div role="alert" style={{ color: 'var(--color-error)', fontSize: '0.8125rem', marginTop: '0.25rem' }}>
          {zoneError}
        </div>
      )}
      {warning && (
        <div role="alert" style={{ color: 'var(--color-error)', fontSize: '0.8125rem', marginTop: '0.25rem' }}>
          {warning}
        </div>
      )}
    </div>
  )
}

// S142 / TASK-14201: the browser-local `formatLocalDate(d)` helper that used to live here
// (getFullYear/getMonth/getDate) was DELETED rather than left unused. It is not a neutral
// utility — it answers "what day is it where this laptop is", which is never the right question
// for a StatsTid business date, and leaving it in the file is an invitation to reuse it. Use
// `copenhagenToday()` from `src/lib/copenhagenDate.ts`.

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
