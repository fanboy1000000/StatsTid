// SPRINT-141 / TASK-14111 — the effective-date control: the feature the rest
// of the sprint exists to support. Owner framing: "this person goes
// part-time on 1 November" must be something HR can decide TODAY and have
// the system hold until November, rather than something HR has to remember
// to come back and do on the day.
//
// Deliberately NOT under `components/ui/` (the design-sync gate cannot run
// on this machine — Python is absent — so a new component there would turn
// the close red with no local way to verify it before the fact). This
// follows the precedent `ScheduledChangeNotice.tsx` — the sibling task in
// this same wave — already set for the same reason: it lives beside the
// section components it decorates, not in the shared kit.
//
// ONE date governs the WHOLE "Gem ændringer" click, not one per field: the
// drawer performs exactly two dated writes on every save (the users PUT,
// which carries the agreement-code field, and the employee-profiles PUT),
// and both are one HR decision, not two. See `PersonDrawer.tsx` for how the
// single `effectiveFrom` state threads into both.
//
// SCOPE, stated precisely because the backend's own doc comments name the
// same distinction (`AdminEndpoints.cs`, the `UpdateUserRequest` handler):
// only the AGREEMENT CODE and the profile fields (deltid/stilling) are
// temporal — this date decides when THOSE take effect. Name, e-mail,
// organisation-placering and leadership are plain mutable columns with no
// "as of" concept in the domain model, and the same save always writes them
// IMMEDIATELY regardless of this control. The banner below is worded to
// that scope rather than claiming the whole save waits.
//
// Composes with `ScheduledChangeNotice` (B0 / OQ-6, TASK-14107) rather than
// duplicating it: that component shows a change someone ELSE already
// scheduled, and — for a field HR is dirtying today — the choice of whether
// today's edit applies only until that change or carries into it. This
// control answers a DIFFERENT question — when does HR's OWN edit, right now,
// take effect — and the two render as separate notices so neither's wording
// contradicts the other (see `writeEffectiveFrom` on `ScheduledChangeNotice`,
// which this file's caller also threads through for exactly that reason).
import styles from '../EditPersonDrawer.module.css'

/** "1. november 2026" — UTC-anchored so a plain `date` string never rolls
    back a day under a viewer's local timezone. A deliberate, small copy of
    `ScheduledChangeNotice.formatScheduledDate` rather than a shared import —
    this file has no other dependency on that component's internals, and
    duplicating four lines is cheaper than coupling two otherwise-independent
    presentational components. */
export function formatEffectiveDateLong(iso: string): string {
  try {
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('da-DK', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    })
  } catch {
    return iso
  }
}

export interface EffectiveDatePickerProps {
  /** ISO yyyy-MM-dd. */
  value: string
  onChange: (next: string) => void
  /**
   * ISO yyyy-MM-dd — the SAME "today" the save itself defaults to
   * (`todayIsoUtc()` in `useEditPerson.ts`), passed in rather than
   * recomputed here so this control's "is this future?" check can never
   * disagree with the value it was itself defaulted to when the drawer
   * opened.
   */
  today: string
  disabled?: boolean
}

/**
 * Requirement 1 (the default stays today): `value` is owned by the caller —
 * this component neither seeds nor resets it — so "today, unless HR
 * deliberately changes it" is guaranteed by the SAME open-time reset the
 * drawer already runs for every other field, not by logic duplicated here.
 *
 * Requirement 2 (say what will happen, before it happens): a save's ordinary
 * meaning is "this is true now". Dating one ahead inverts that — nothing
 * changes today — and a silent success would leave HR to find out by
 * accident, three weeks from now, that a colleague was never actually
 * updated. The banner below states the inversion plainly, computed purely
 * from the two dates already in hand (no server round-trip, so it can never
 * be an optimistic claim about what a save WILL do — seeing the actual
 * outcome is `ScheduledChangeNotice`'s job on the next open, off a
 * server-confirmed read).
 *
 * Requirement 4 (past dates remain allowed): no `min` attribute. Backdating
 * already existed at the API before this sprint; this control is simply the
 * first UI surface in this drawer that can drive either direction of it, and
 * narrowing it to future-only here would quietly take away a capability
 * nobody asked to lose.
 */
export function EffectiveDatePicker({ value, onChange, today, disabled }: EffectiveDatePickerProps) {
  // ISO yyyy-MM-dd strings compare lexicographically = chronologically —
  // the same idiom `EmploymentHistoryPage.tsx` already relies on for its own
  // sort, so this isn't a new assumption in the codebase.
  const isFuture = value > today

  return (
    <section className={styles.section} aria-labelledby="pd-effective-heading">
      <h3 id="pd-effective-heading" className={styles.sectionLabel}>
        Virkning
      </h3>
      <div className={styles.formField}>
        <label className={styles.formLabel} htmlFor="pd-effective-from">
          Overenskomstkode og deltid/stilling gælder fra
        </label>
        <input
          className={styles.input}
          id="pd-effective-from"
          type="date"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          data-testid="pd-effective-from"
        />
        <div className={styles.helperText}>
          Forudfyldt med dagens dato. Vælg en senere dato for at planlægge ændringen frem i tiden — eller en
          tidligere dato for at rette historikken. Navn, e-mail, organisation og ledelse gemmes straks, uanset
          denne dato.
        </div>
      </div>
      {isFuture && (
        <div className={styles.scheduledNotice} data-testid="pd-effective-future-notice">
          <p className={styles.scheduledText}>
            Overenskomstkoden og deltid/stilling ændres ikke i dag — de forbliver som nu, indtil{' '}
            <strong>{formatEffectiveDateLong(value)}</strong>, hvor denne ændring træder i kraft. (Navn, e-mail og
            organisation på denne side gemmes straks, uanset denne dato.)
          </p>
        </div>
      )}
    </section>
  )
}
