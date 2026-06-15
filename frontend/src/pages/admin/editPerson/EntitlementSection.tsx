// S76b / TASK-7602 — the Entitlement section of the unified EditPersonDrawer.
// Fødselsdato (drives the age-derived SENIOR_DAY gate), Ansættelsesdato
// (pro-rates mid-year-hire accrual), and the Barns-sygedag (CHILD_SICK) opt-in
// toggle. HR-gated: rendered only for an HROrAbove actor. Maps to three
// independent HR-only PUTs (DOB / employment-start admin-strict If-Match;
// CHILD_SICK its own If-None-Match:* / If-Match contract).
import type { ChangeEvent } from 'react'
import styles from '../EditPersonDrawer.module.css'
import type { EntitlementFields, SectionSaveState } from './types'

interface EntitlementSectionProps {
  fields: EntitlementFields
  onChange: (patch: Partial<EntitlementFields>) => void
  /** Called when the child-sick toggle changes — marks it dirty in the drawer. */
  onChildSickToggle: (next: boolean) => void
  /** users.version was captured → DOB/employment-start are writable. */
  hasDateVersions: boolean
  birthDateSave: SectionSaveState
  employmentStartSave: SectionSaveState
  childSickSave: SectionSaveState
  disabled?: boolean
}

export function EntitlementSection({
  fields,
  onChange,
  onChildSickToggle,
  hasDateVersions,
  birthDateSave,
  employmentStartSave,
  childSickSave,
  disabled = false,
}: EntitlementSectionProps) {
  const setDate =
    <K extends 'birthDate' | 'employmentStartDate'>(key: K) =>
    (e: ChangeEvent<HTMLInputElement>) =>
      onChange({ [key]: e.target.value } as Pick<EntitlementFields, K>)

  const datesDisabled = disabled || !hasDateVersions

  return (
    <section className={styles.section} aria-labelledby="ep-entitlement-heading">
      <h3 id="ep-entitlement-heading" className={styles.sectionLabel}>
        Berettigelse
      </h3>

      {(birthDateSave.status === 'failed' ||
        employmentStartSave.status === 'failed' ||
        childSickSave.status === 'failed') && (
        <div className={styles.sectionError} role="alert" data-testid="ep-entitlement-error">
          {birthDateSave.status === 'failed' && (
            <div>Fødselsdato kunne ikke gemmes: {birthDateSave.message}</div>
          )}
          {employmentStartSave.status === 'failed' && (
            <div>Ansættelsesdato kunne ikke gemmes: {employmentStartSave.message}</div>
          )}
          {childSickSave.status === 'failed' && (
            <div>Barns sygedag kunne ikke gemmes: {childSickSave.message}</div>
          )}
        </div>
      )}

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-birthDate">
          Fødselsdato
        </label>
        <input
          className={styles.input}
          id="ep-birthDate"
          type="date"
          value={fields.birthDate}
          onChange={setDate('birthDate')}
          disabled={datesDisabled}
          title={!hasDateVersions ? 'Kunne ikke indlæse fødselsdato' : undefined}
          data-testid="ep-birth-date"
        />
        <div className={styles.helperText}>
          Seniordage tildeles automatisk fra det fyldte 62. år ud fra fødselsdatoen.
        </div>
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-employmentStart">
          Ansættelsesdato
        </label>
        <input
          className={styles.input}
          id="ep-employmentStart"
          type="date"
          value={fields.employmentStartDate}
          onChange={setDate('employmentStartDate')}
          disabled={datesDisabled}
          title={!hasDateVersions ? 'Kunne ikke indlæse ansættelsesdato' : undefined}
          data-testid="ep-employment-start"
        />
        <div className={styles.helperText}>
          Bruges til at beregne optjent ferie for medarbejdere ansat midt i ferieåret.
        </div>
      </div>

      <div className={styles.formField}>
        <label className={styles.checkboxRow} htmlFor="ep-childSick">
          <input
            id="ep-childSick"
            type="checkbox"
            checked={fields.childSickEligible}
            disabled={disabled}
            onChange={(e) => onChildSickToggle(e.target.checked)}
            data-testid="ep-child-sick"
          />
          <span>Barns sygedag – berettiget</span>
        </label>
        <div className={styles.helperText}>
          Giver medarbejderen adgang til at registrere barns 1./2./3. sygedag.
        </div>
      </div>
    </section>
  )
}
