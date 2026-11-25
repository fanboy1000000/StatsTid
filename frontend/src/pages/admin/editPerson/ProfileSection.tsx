// S76b / TASK-7602 — the Profile section of the unified EditPersonDrawer.
// Deltidsfraktion + Stilling. HR-gated: rendered only for an HROrAbove actor
// (the drawer hides this section for a non-HR LocalAdmin per OQ-5b / R3
// create-path-HR-fields). Deltid/Stilling map to the employee-profiles-row PUT.
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the structured Enhed multi-tag
// picker + the free-text enhed label were REMOVED from this section.
//
// QUAL-268 (the owner's ruling of 2026-11-24) — with no profile covering today the section says so in
// Danish instead of showing dead fields, and offers "Opret ny profil" only when the server's 404 said a
// re-create is possible (`canCreate`); the form is `CreateProfilePanel`. A planned profile or an
// unreadable answer offers nothing. The button is locked on one's own record (the server refuses it).
import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import styles from '../EditPersonDrawer.module.css'
import { CreateProfilePanel, CREATE_PROFILE_FORM_ID } from './CreateProfilePanel'
import { formatEffectiveDateLong } from './EffectiveDatePicker'
import type { EmployeeProfileSnapshot, ProfileRead } from './employeeProfileApi'
import type { ProfileFields, SectionSaveState } from './types'

interface ProfileSectionProps {
  fields: ProfileFields
  onChange: (patch: Partial<ProfileFields>) => void
  /** False when no profile snapshot was captured (no live row) — disables fields. */
  hasProfile: boolean
  /** Per-section save outcome (committed/failed) for partial-failure honesty. */
  saveState: SectionSaveState
  /** Id of the self-lock note: set for the logged-in user's own record; locks the fraction and position (P-26) and points them at the note. */
  selfLockNoteId?: string
  disabled?: boolean
  /** What the profile read said when no profile covers today (null/undefined: one does, or still loading). */
  noProfile?: ProfileRead | null
  employeeId?: string
  /** The server-confirmed Copenhagen day; null when unresolvable (no create is offered then). */
  today?: string | null
  /** The person's first day of employment as the server holds it; '' when none. */
  employmentStart?: string
  onCreated?: (snapshot: EmployeeProfileSnapshot) => void
}

export function ProfileSection({
  fields,
  onChange,
  hasProfile,
  saveState,
  disabled = false,
  selfLockNoteId,
  noProfile = null,
  employeeId = '',
  today = null,
  employmentStart = '',
  onCreated,
}: ProfileSectionProps) {
  // The person the form was opened for: switching person closes it.
  const [openFor, setOpenFor] = useState<string | null>(null)
  const formOpen = openFor === employeeId
  const openButton = useRef<HTMLButtonElement>(null)
  const focusFirstField = useRef(false)
  useEffect(() => {
    if (hasProfile && focusFirstField.current) {
      focusFirstField.current = false
      document.getElementById('ep-part-time')?.focus()
    }
  }, [hasProfile])

  const setText =
    <K extends keyof ProfileFields>(key: K) =>
    (e: ChangeEvent<HTMLInputElement>) =>
      onChange({ [key]: e.target.value } as Pick<ProfileFields, K>)

  const fieldsDisabled = disabled || !hasProfile || !!selfLockNoteId
  const options = noProfile?.kind === 'none' ? noProfile.options : null
  const canCreate = options?.canCreate === true
  const scheduled = options !== null && !options.canCreate ? options.scheduledFrom : null
  // Unreadable: the read failed, or the 404 said neither "create" nor "planned".
  const unreadable = !hasProfile && noProfile !== null && !canCreate && scheduled === null
  const showFields = !(canCreate || scheduled !== null)

  return (
    <section className={styles.section} aria-labelledby="ep-profile-heading">
      <h3 id="ep-profile-heading" className={styles.sectionLabel}>
        Profil
      </h3>

      {saveState.status === 'failed' && (
        <div className={styles.sectionError} role="alert" data-testid="ep-profile-error">
          Profilen kunne ikke gemmes: {saveState.message}
        </div>
      )}

      {canCreate && options !== null && (
        <>
          <p className={styles.helperText} data-testid="ep-profile-none">
            {options.uncoveredSince
              ? `Medarbejderen har ingen ansættelsesprofil fra og med ${formatEffectiveDateLong(options.uncoveredSince)}. Uden en profil kan medarbejderen ikke registrere tid, og lønnen kan ikke beregnes for de dage.`
              : 'Medarbejderen har ingen ansættelsesprofil, der gælder i dag. Uden en profil kan medarbejderen ikke registrere tid, og lønnen kan ikke beregnes.'}
          </p>
          {today !== null && (
            <button
              ref={openButton}
              type="button"
              className={styles.submitBtn}
              onClick={() => setOpenFor(employeeId)}
              disabled={disabled || !!selfLockNoteId}
              aria-expanded={formOpen}
              aria-controls={CREATE_PROFILE_FORM_ID}
              aria-describedby={selfLockNoteId}
              data-testid="ep-profile-create"
            >
              Opret ny profil
            </button>
          )}
          {formOpen && today !== null && (
            <CreateProfilePanel
              key={`${employeeId}|${options.uncoveredSince}|${options.lastPartTimeFraction}|${options.lastPosition}`}
              employeeId={employeeId}
              options={options}
              today={today}
              employmentStart={employmentStart}
              disabled={disabled}
              onCreated={(snapshot) => {
                focusFirstField.current = true
                setOpenFor(null)
                onCreated?.(snapshot)
              }}
              onCancel={() => {
                setOpenFor(null)
                openButton.current?.focus()
              }}
            />
          )}
        </>
      )}

      {scheduled !== null && (
        <p className={styles.helperText} data-testid="ep-profile-scheduled">
          En ansættelsesprofil er planlagt fra {formatEffectiveDateLong(scheduled)}. Den dækker medarbejderen fra den dag, og der skal ikke gøres noget nu.
        </p>
      )}

      {unreadable && (
        <p className={styles.helperText} data-testid="ep-profile-unreadable">
          Profilen kunne ikke hentes. Luk panelet, og åbn medarbejderen igen.
        </p>
      )}

      {showFields && (
      <>
      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-partTime">
          Deltidsfraktion <span className={styles.required}>*</span>
        </label>
        <input
          className={styles.input}
          id="ep-partTime"
          type="number"
          required
          min={0.1}
          max={1.0}
          step={0.001}
          value={fields.partTimeFraction}
          onChange={setText('partTimeFraction')}
          disabled={fieldsDisabled}
          aria-describedby={selfLockNoteId}
          data-testid="ep-part-time"
        />
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-position">
          Stilling
        </label>
        <input
          className={styles.input}
          id="ep-position"
          type="text"
          maxLength={100}
          value={fields.position}
          onChange={setText('position')}
          disabled={fieldsDisabled}
          aria-describedby={selfLockNoteId}
          placeholder="f.eks. Fuldmægtig"
          data-testid="ep-position"
        />
      </div>
      </>
      )}
    </section>
  )
}
