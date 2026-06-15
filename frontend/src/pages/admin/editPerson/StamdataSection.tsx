// S76b / TASK-7602 — the Stamdata section of the unified EditPersonDrawer.
// Navn/Visningsnavn, E-mail, Organisation, Overenskomst, and a READ-ONLY/derived
// "Norm" line (NOT editable — the prototype's "Bruger-ID tildeles automatisk").
// Always editable (LocalAdmin floor); maps to the users-row PUT.
import type { ChangeEvent } from 'react'
import styles from '../EditPersonDrawer.module.css'
import { AGREEMENT_CODES, type Organization, type StamdataFields } from './types'

interface StamdataSectionProps {
  fields: StamdataFields
  onChange: (patch: Partial<StamdataFields>) => void
  organizations: Organization[]
  /** When set, a read-only line shows the immutable Bruger-ID (edit mode). */
  userId?: string
  /** Derived weekly norm display string (e.g. "37,0 t/uge"); read-only. */
  normDisplay?: string
  disabled?: boolean
}

export function StamdataSection({
  fields,
  onChange,
  organizations,
  userId,
  normDisplay,
  disabled = false,
}: StamdataSectionProps) {
  const set =
    <K extends keyof StamdataFields>(key: K) =>
    (e: ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
      onChange({ [key]: e.target.value } as Pick<StamdataFields, K>)

  return (
    <section className={styles.section} aria-labelledby="ep-stamdata-heading">
      <h3 id="ep-stamdata-heading" className={styles.sectionLabel}>
        Stamdata
      </h3>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-displayName">
          Navn <span className={styles.required}>*</span>
        </label>
        <input
          className={styles.input}
          id="ep-displayName"
          type="text"
          required
          value={fields.displayName}
          onChange={set('displayName')}
          disabled={disabled}
          data-testid="ep-display-name"
        />
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-email">
          E-mail
        </label>
        <input
          className={styles.input}
          id="ep-email"
          type="email"
          value={fields.email}
          onChange={set('email')}
          disabled={disabled}
          placeholder="bruger@example.dk"
          data-testid="ep-email"
        />
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-primaryOrg">
          Organisation <span className={styles.required}>*</span>
        </label>
        <select
          className={styles.select}
          id="ep-primaryOrg"
          value={fields.primaryOrgId}
          onChange={set('primaryOrgId')}
          disabled={disabled}
          data-testid="ep-primary-org"
        >
          {organizations.map((org) => (
            <option key={org.orgId} value={org.orgId}>
              {org.orgName} ({org.orgId})
            </option>
          ))}
        </select>
      </div>

      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="ep-agreement">
          Overenskomst <span className={styles.required}>*</span>
        </label>
        <select
          className={styles.select}
          id="ep-agreement"
          value={fields.agreementCode}
          onChange={set('agreementCode')}
          disabled={disabled}
          data-testid="ep-agreement"
        >
          {AGREEMENT_CODES.map((code) => (
            <option key={code} value={code}>
              {code}
            </option>
          ))}
        </select>
      </div>

      {/* Norm is DERIVED (weekly-norm × deltidsfraktion) — read-only, never an
          editable field (SPRINT-76 R3 "Norm read-only/derived"). */}
      <div className={styles.formField}>
        <span className={styles.formLabel}>Norm (t/uge)</span>
        <div className={styles.staticValue} data-testid="ep-norm">
          {normDisplay ?? 'Beregnes automatisk'}
        </div>
      </div>

      {/* Bruger-ID: immutable in edit; "tildeles automatisk" copy lives on the
          create credentials block. */}
      {userId && (
        <div className={styles.formField}>
          <span className={styles.formLabel}>Bruger-ID</span>
          <div className={styles.staticValue} data-testid="ep-user-id">
            {userId}
          </div>
        </div>
      )}
    </section>
  )
}
