// S76b / TASK-7602 — the Profile section of the unified EditPersonDrawer.
// Deltidsfraktion, Stilling, and (S97/TASK-9705) the structured Enhed multi-tag
// picker that REPLACES the free-text `enhedLabel` field. HR-gated: rendered only
// for an HROrAbove actor (the drawer hides this section for a non-HR LocalAdmin
// per OQ-5b / R3 create-path-HR-fields). Deltid/Stilling map to the
// employee-profiles-row PUT; the enhed tags map to the dedicated set-user-tags
// PUT (the drawer's single-save-path threads both).
import type { ChangeEvent } from 'react'
import styles from '../EditPersonDrawer.module.css'
import type { ProfileFields, SectionSaveState } from './types'
import { EnhedTagPicker } from './EnhedTagPicker'

interface ProfileSectionProps {
  fields: ProfileFields
  onChange: (patch: Partial<ProfileFields>) => void
  /** False when no profile snapshot was captured (no live row) — disables fields. */
  hasProfile: boolean
  /** Per-section save outcome (committed/failed) for partial-failure honesty. */
  saveState: SectionSaveState
  /** S97 — the person's Organisation (the enhed source for the tag picker). */
  organisationId: string
  /** S97 — the person's current enhed tag NAMES (comma-joined display label)
      used to seed the picker's initial selection. */
  currentTagNames?: string | null
  /** S97 — fired once when the picker seeds the initial tag selection, so the
      drawer can set the save-dirtiness baseline. */
  onEnhederSeed?: (ids: string[]) => void
  /** S97 — per-section save outcome for the set-user-tags PUT. */
  enhederSaveState: SectionSaveState
  disabled?: boolean
}

export function ProfileSection({
  fields,
  onChange,
  hasProfile,
  saveState,
  organisationId,
  currentTagNames,
  onEnhederSeed,
  enhederSaveState,
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

      {/* S97 / TASK-9705 — the structured Enhed multi-tag picker (replaces the
          S74 free-text field). The legacy `enhedLabel` is kept read-only as a
          fallback when the person has no structured tags. */}
      {enhederSaveState.status === 'failed' && (
        <div className={styles.sectionError} role="alert" data-testid="ep-enheder-save-error">
          Enhederne kunne ikke gemmes: {enhederSaveState.message}
        </div>
      )}
      <EnhedTagPicker
        organisationId={organisationId}
        selectedIds={fields.enhedIds}
        onChange={(ids) => onChange({ enhedIds: ids })}
        currentTagNames={currentTagNames}
        onSeed={onEnhederSeed}
        legacyLabel={fields.enhedLabel || null}
        disabled={fieldsDisabled}
      />
    </section>
  )
}
