// S76b / TASK-7602 — the Profile section of the unified EditPersonDrawer.
// Deltidsfraktion + Stilling. HR-gated: rendered only for an HROrAbove actor
// (the drawer hides this section for a non-HR LocalAdmin per OQ-5b / R3
// create-path-HR-fields). Deltid/Stilling map to the employee-profiles-row PUT.
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the structured Enhed multi-tag
// picker + the free-text enhed label were REMOVED from this section.
import type { ChangeEvent } from 'react'
import styles from '../EditPersonDrawer.module.css'
import type { ProfileFields, SectionSaveState } from './types'

interface ProfileSectionProps {
  fields: ProfileFields
  onChange: (patch: Partial<ProfileFields>) => void
  /** False when no profile snapshot was captured (no live row) — disables fields. */
  hasProfile: boolean
  /** Per-section save outcome (committed/failed) for partial-failure honesty. */
  saveState: SectionSaveState
  disabled?: boolean
}

export function ProfileSection({
  fields,
  onChange,
  hasProfile,
  saveState,
  disabled = false,
}: ProfileSectionProps) {
  const setText =
    <K extends keyof ProfileFields>(key: K) =>
    (e: ChangeEvent<HTMLInputElement>) =>
      onChange({ [key]: e.target.value } as Pick<ProfileFields, K>)

  const fieldsDisabled = disabled || !hasProfile
  const noProfileTitle = !hasProfile ? 'Profil ikke fundet for denne medarbejder' : undefined

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
          title={noProfileTitle}
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
          title={noProfileTitle}
          placeholder="f.eks. Fuldmægtig"
          data-testid="ep-position"
        />
      </div>
    </section>
  )
}
