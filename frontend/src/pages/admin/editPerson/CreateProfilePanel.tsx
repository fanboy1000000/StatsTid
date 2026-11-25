// The small form that re-creates a closed employment profile (QUAL-268, the owner's ruling of
// 2026-11-24). It saves on its own, with its own date, and the server stays the authority: the
// section offers it only when the profile read said a create is possible, and every refusal is
// shown in Danish. Its inputs have no form owner (`form="pd-cp-detached"`): inside the drawer's
// <form> a half-typed date would block "Gem ændringer" and Enter would submit the drawer; the limits
// are checked in code instead. A save's answer belongs to the form it was issued for: a late answer
// for another person or another set of options is dropped (the generation, as in HrFactsSection).
import { useEffect, useRef, useState } from 'react'
import styles from '../EditPersonDrawer.module.css'
import { isCalendarDate, laterDate } from '../nearestMondayOffer'
import { createEmployeeProfile, type EmployeeProfileSnapshot, type NoProfileOptions } from './employeeProfileApi'
import { formatEffectiveDateLong } from './EffectiveDatePicker'

export const CREATE_PROFILE_FORM_ID = 'pd-cp-form'

const DRIFT_SENTENCE = "The user's organisation changed while this request was being processed; refresh and retry."

/** `yyyy-MM-dd` minus one day (UTC-anchored, so no timezone rolls it). */
function dayBefore(iso: string): string {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() - 1)
  return d.toISOString().slice(0, 10)
}

function codeText(code: string, nearestValid: unknown, fallback: string): string {
  switch (code) {
    case 'EFFECTIVE_FROM_NOT_TODAY_OR_PAST':
      return 'Datoen kan ikke ligge efter i dag.'
    case 'BEFORE_LAST_PROFILE_END': {
      const first = Array.isArray(nearestValid) ? nearestValid[0] : undefined
      return typeof first === 'string' && isCalendarDate(first)
        ? `Den nye profil kan tidligst gælde fra ${formatEffectiveDateLong(first)}, hvor den seneste profil ophørte.`
        : fallback
    }
    case 'MUST_BE_POSITIVE':
      return 'Deltidsfraktionen skal være større end 0.'
    case 'TOO_MANY_DECIMALS':
      return 'Brug højst tre decimaler.'
    case 'EXCEEDS_STORAGE_LIMIT':
      return 'Tallet er for stort til at blive gemt.'
    default:
      return fallback
  }
}

/** The status-tagged error `createEmployeeProfile` throws, as Danish sentences (one per failing field). */
function refusalTexts(err: unknown): string[] {
  const message = err instanceof Error ? err.message : String(err)
  const status = typeof err === 'object' && err !== null && 'status' in err ? err.status : undefined
  const body = typeof err === 'object' && err !== null && 'body' in err ? err.body : undefined
  const serverError =
    typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string' ? body.error : message
  const unmapped = `Profilen kunne ikke oprettes: ${serverError}`
  if (status === 400 && typeof body === 'object' && body !== null && 'fields' in body && Array.isArray(body.fields)) {
    const texts = body.fields.map((f: unknown) =>
      typeof f === 'object' && f !== null && 'code' in f && typeof f.code === 'string'
        ? codeText(f.code, 'nearestValid' in f ? f.nearestValid : undefined, unmapped)
        : unmapped,
    )
    if (texts.length > 0) return texts
  }
  if (status === 409 && serverError === 'Employee profile already exists') {
    return ['Medarbejderen har allerede en ansættelsesprofil, eller en er planlagt. Luk panelet, og åbn medarbejderen igen for at se den.']
  }
  if (status === 409 && serverError === DRIFT_SENTENCE) {
    return ['Medarbejderen er flyttet til en anden organisation, mens du gemte. Luk panelet, åbn medarbejderen igen, og prøv igen.']
  }
  if (status === 403) return ['Du har ikke rettigheder til at oprette en profil for denne medarbejder.']
  if (status === 422) return ['Datoen ligger før medarbejderens første ansættelsesdag.']
  return [unmapped]
}

interface CreateProfilePanelProps {
  employeeId: string
  options: NoProfileOptions
  /** The server-confirmed Copenhagen day. */
  today: string
  /** The person's first day of employment as the server holds it; '' when none. */
  employmentStart: string
  disabled?: boolean
  /** True while the create request is in flight (the drawer holds its own save meanwhile). */
  onPendingChange?: (pending: boolean) => void
  onCreated: (snapshot: EmployeeProfileSnapshot) => void
  onCancel: () => void
}

export function CreateProfilePanel({
  employeeId,
  options,
  today,
  employmentStart,
  disabled = false,
  onPendingChange,
  onCreated,
  onCancel,
}: CreateProfilePanelProps) {
  // The earliest day: the first day without a profile, or the employment start when that is later and
  // already past (days before the start are not a gap). Neither: no earliest, and today is chosen.
  const start = employmentStart && employmentStart <= today ? employmentStart : null
  const min = laterDate(options.uncoveredSince, start)
  const [date, setDate] = useState(min ?? today)
  const [fraction, setFraction] = useState((options.lastPartTimeFraction ?? 1).toFixed(3))
  const [position, setPosition] = useState(options.lastPosition ?? '')
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)
  const dateRef = useRef<HTMLInputElement>(null)
  const generation = useRef(0)

  useEffect(() => {
    dateRef.current?.focus()
  }, [])
  // The drawer's "Gem ændringer" waits while a create is pending; unmounting clears it.
  useEffect(() => {
    onPendingChange?.(saving)
    return () => onPendingChange?.(false)
  }, [saving, onPendingChange])
  // Cleanup runs when the person changes or the panel closes: answers issued before are then stale.
  useEffect(
    () => () => {
      generation.current++
    },
    [employeeId],
  )

  const dateOk = isCalendarDate(date) && date <= today && (min === null || date >= min)
  const fractionNumber = Number(fraction)
  const fractionOk = fraction.trim() !== '' && Number.isFinite(fractionNumber) && fractionNumber >= 0.1 && fractionNumber <= 1
  const canSave = dateOk && fractionOk && !saving && !disabled

  // Why the submit is dead, under the field (QUAL-268 ruling 3); the pre-fill is never clamped.
  const dateError = dateOk
    ? null
    : !isCalendarDate(date)
      ? 'Vælg en dato.'
      : date > today
        ? 'Datoen kan ikke ligge efter i dag.'
        : min === options.uncoveredSince
          ? `Den nye profil kan tidligst gælde fra ${formatEffectiveDateLong(min ?? '')}, hvor den seneste profil ophørte.`
          : 'Datoen ligger før medarbejderens første ansættelsesdag.'
  const fractionError = fractionOk ? null : 'Deltidsfraktionen skal være et tal mellem 0,1 og 1,0.'

  const gap = dateOk && min !== null && date > min
  const gapEnd = gap ? dayBefore(date) : null
  const helperText =
    min !== null && min === options.uncoveredSince
      ? `Forudfyldt med ${formatEffectiveDateLong(min)}, den første dag uden profil, så der ikke opstår et hul. Datoen kan ikke ligge efter i dag.`
      : min !== null
        ? `Forudfyldt med ${formatEffectiveDateLong(min)}, medarbejderens første ansættelsesdag. Datoen kan ikke ligge før den dag eller efter i dag.`
        : 'Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.'
  const describedBy = ['pd-cp-date-help', gap ? 'pd-cp-gap' : null, dateError ? 'pd-cp-date-error' : null]
    .filter(Boolean)
    .join(' ')

  async function save() {
    if (!canSave) return
    const issuedFor = generation.current
    setSaving(true)
    setErrors([])
    try {
      const snapshot = await createEmployeeProfile(employeeId, {
        effectiveFrom: date,
        partTimeFraction: fractionNumber,
        position: position.trim() || null,
      })
      if (issuedFor !== generation.current) return
      onCreated(snapshot)
    } catch (err) {
      if (issuedFor !== generation.current) return
      setErrors(refusalTexts(err))
      setSaving(false)
    }
  }

  return (
    <fieldset id={CREATE_PROFILE_FORM_ID} data-testid="pd-cp-form">
      <legend className={styles.formLabel}>Ny ansættelsesprofil</legend>

      {errors.length > 0 && (
        <div className={styles.sectionError} role="alert" data-testid="pd-cp-error">
          {errors.map((text, i) => (
            <div key={i}>{text}</div>
          ))}
        </div>
      )}

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="pd-cp-date">
          Gælder fra
        </label>
        <input
          ref={dateRef}
          className={styles.input}
          id="pd-cp-date"
          type="date"
          form="pd-cp-detached"
          min={min ?? undefined}
          max={today}
          value={date}
          onChange={(e) => setDate(e.target.value)}
          disabled={saving || disabled}
          aria-describedby={describedBy}
          aria-invalid={dateError ? true : undefined}
          data-testid="pd-cp-date"
        />
        <div className={styles.helperText} id="pd-cp-date-help">
          {helperText}
        </div>
        {dateError && (
          <div className={styles.sectionError} id="pd-cp-date-error" role="status" data-testid="pd-cp-date-error">
            {dateError}
          </div>
        )}
        {gap && gapEnd !== null && min !== null && (
          <div className={styles.scheduledNotice} id="pd-cp-gap" role="status" data-testid="pd-cp-gap">
            {gapEnd === min
              ? `${formatEffectiveDateLong(min)} får ingen profil, og lønnen for den måned kan ikke beregnes, før dagen har en profil. Vælg ${formatEffectiveDateLong(min)}, hvis der ikke skal være et hul.`
              : `Dagene fra og med ${formatEffectiveDateLong(min)} til og med ${formatEffectiveDateLong(gapEnd)} får ingen profil, og lønnen for de måneder kan ikke beregnes, før dagene har en profil. Vælg ${formatEffectiveDateLong(min)}, hvis der ikke skal være et hul.`}
          </div>
        )}
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="pd-cp-part-time">
          Deltidsfraktion <span className={styles.required}>*</span>
        </label>
        <input
          className={styles.input}
          id="pd-cp-part-time"
          type="number"
          form="pd-cp-detached"
          min={0.1}
          max={1.0}
          step={0.001}
          value={fraction}
          onChange={(e) => setFraction(e.target.value)}
          disabled={saving || disabled}
          aria-invalid={fractionError ? true : undefined}
          aria-describedby={fractionError ? 'pd-cp-part-time-error' : undefined}
          data-testid="pd-cp-part-time"
        />
        {fractionError && (
          <div className={styles.sectionError} id="pd-cp-part-time-error" role="status" data-testid="pd-cp-part-time-error">
            {fractionError}
          </div>
        )}
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="pd-cp-position">
          Stilling
        </label>
        <input
          className={styles.input}
          id="pd-cp-position"
          type="text"
          form="pd-cp-detached"
          maxLength={100}
          value={position}
          onChange={(e) => setPosition(e.target.value)}
          disabled={saving || disabled}
          placeholder="f.eks. Fuldmægtig"
          data-testid="pd-cp-position"
        />
      </div>

      <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
        <button
          type="button"
          className={styles.submitBtn}
          onClick={() => void save()}
          disabled={!canSave}
          data-testid="pd-cp-submit"
        >
          {saving ? 'Opretter...' : 'Opret profil'}
        </button>
        <button type="button" className={styles.cancelBtn} onClick={onCancel} disabled={saving || disabled} data-testid="pd-cp-cancel">
          Annuller
        </button>
      </div>
    </fieldset>
  )
}
