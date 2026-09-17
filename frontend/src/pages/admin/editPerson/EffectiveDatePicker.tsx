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
//
// SPRINT-END BLOCKER FIX (2026-09-14, verified by the coordinator) — this
// sprint's own defect class (a same-values write silently reverting a
// scheduled change) came back through the picker itself. Picking a date AT
// OR AFTER an existing scheduled change's own start put the write INSIDE
// that change's interval, but the drawer still pre-filled the profile/
// agreement-code fields from TODAY's values — so an untouched field sent
// today's stale values dated into the scheduled interval, which the backend
// (correctly, comparing against the row that actually covers that date)
// read as a genuine correction and wrote forward, quietly overwriting the
// colleague's scheduled decision. `relateToScheduled` below names the three
// relations a picked date can have to a scheduled change; `PersonDrawer.tsx`
// uses it to (a) re-baseline the form fields to the SCHEDULED row's values
// once the date falls inside it, so an untouched field now sends the
// scheduled value back (a genuine no-op) rather than today's, and (b) refuse
// the date entirely when it falls beyond the scheduled row's own end, where
// no further row is known and guessing would be worse than refusing.
import styles from '../EditPersonDrawer.module.css'

/**
 * How a candidate write date relates to an EXISTING scheduled change's own
 * interval (`effectiveFrom` .. `effectiveTo`, where a `null` end means
 * open-ended). `scheduled` is `null` when nothing is scheduled for that
 * field at all — always 'before' in that case, which is the ordinary,
 * unaffected path.
 *
 * - **'before'** — `date` is strictly earlier than the scheduled change's
 *   own start (or nothing is scheduled). The write may TRUNCATE the
 *   scheduled row; OQ-6's apply-until / carry-forward choice is the right
 *   question here, unchanged from before this fix.
 * - **'covers'** — `date` falls ON or AFTER the scheduled change's start,
 *   and before its end (or the end is `null`). The write's own date lands
 *   INSIDE the scheduled interval — the sprint-end BLOCKER's shape. The
 *   form must pre-fill from the SCHEDULED row's values here, not today's.
 * - **'beyond'** — the scheduled change has a bounded end and `date` is at
 *   or after it. A further row must exist to cover that date and this
 *   payload (one hop ahead only) does not carry it — refuse rather than
 *   guess.
 */
export type ScheduleRelation = 'before' | 'covers' | 'beyond'

export function relateToScheduled(
  scheduled: { effectiveFrom: string; effectiveTo: string | null } | null,
  date: string,
): ScheduleRelation {
  if (!scheduled) return 'before'
  // ISO yyyy-MM-dd strings compare lexicographically = chronologically —
  // the same idiom this file already relies on for `isFuture` below.
  if (date < scheduled.effectiveFrom) return 'before'
  if (scheduled.effectiveTo !== null && date >= scheduled.effectiveTo) return 'beyond'
  return 'covers'
}

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
   * (`todayIso()` in `useEditPerson.ts`), passed in rather than
   * recomputed here so this control's "is this future?" check can never
   * disagree with the value it was itself defaulted to when the drawer
   * opened.
   *
   * S142 / TASK-14209 — OQ-12: `null` when the caller's zone-resolve failed
   * (`copenhagenToday()` threw — the runtime cannot resolve Europe/Copenhagen).
   * The caller has already shown its OWN message and blocked the save in that
   * case (`blockedReason`), so this component just skips the future/past
   * classification below rather than guessing against a day it does not have.
   */
  today: string | null
  disabled?: boolean
  /**
   * SPRINT-END BLOCKER FIX — non-null when `value` is at or beyond an
   * existing scheduled change's own END (`relateToScheduled` === 'beyond'
   * for the profile fields and/or the agreement code). Rendered INSTEAD of
   * the notices below, and the caller separately disables Save: this
   * payload only carries one hop ahead, so there is no honest baseline to
   * pre-fill from and the drawer refuses rather than guesses.
   */
  blockedReason?: string | null
  /**
   * SPRINT-END BLOCKER FIX — non-null when `value` falls AT OR AFTER an
   * existing scheduled change's own start (`relateToScheduled` === 'covers'
   * for the profile fields and/or the agreement code). The fields below
   * have been RE-PRE-FILLED from that scheduled row rather than from
   * today's values (see `PersonDrawer.tsx`'s baseline-tracking effects), so
   * this note tells HR why the values changed and what leaving vs. editing
   * them now does. Ignored when `blockedReason` is set.
   */
  coversScheduledNote?: string | null
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
export function EffectiveDatePicker({
  value,
  onChange,
  today,
  disabled,
  blockedReason,
  coversScheduledNote,
}: EffectiveDatePickerProps) {
  // ISO yyyy-MM-dd strings compare lexicographically = chronologically —
  // the same idiom `EmploymentHistoryPage.tsx` already relies on for its own
  // sort, so this isn't a new assumption in the codebase. `today === null`
  // (S142/OQ-12, zone unresolvable) never classifies as future — the caller
  // is already showing `blockedReason` and refusing the save in that case,
  // so this is cosmetic-only and must not guess.
  const isFuture = today !== null && value > today

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
      {blockedReason ? (
        <div className={styles.sectionError} data-testid="pd-effective-blocked">
          {blockedReason}
        </div>
      ) : (
        <>
          {/*
            S141 Step-7a cycle 2 — this notice is SUPPRESSED when the picked date falls inside an
            already-scheduled change, because in that shape its sentence is FALSE.

            It promises the values "forbliver som nu, indtil {picked}" — remain as they are until the
            picked date. That is true only when nothing is scheduled in between. If a colleague has
            scheduled a change from 1 December and HR picks 15 December, the values change on the
            FIRST of December, not the fifteenth — and the covers notice directly below would then
            sit under a sentence contradicting it, which is precisely what this component's own
            header says it exists to prevent. The absorption that introduced the covers notice
            corrected the OTHER wrong sentence and left this one standing; the sprint log's claim
            that both were fixed was wrong, and this is the correction.
          */}
          {isFuture && !coversScheduledNote && (
            <div className={styles.scheduledNotice} data-testid="pd-effective-future-notice">
              <p className={styles.scheduledText}>
                Overenskomstkoden og deltid/stilling ændres ikke i dag — de forbliver som nu, indtil{' '}
                <strong>{formatEffectiveDateLong(value)}</strong>, hvor denne ændring træder i kraft. (Navn, e-mail
                og organisation på denne side gemmes straks, uanset denne dato.)
              </p>
            </div>
          )}
          {coversScheduledNote && (
            <div className={styles.scheduledNotice} data-testid="pd-effective-covers-notice">
              <p className={styles.scheduledText}>{coversScheduledNote}</p>
            </div>
          )}
        </>
      )}
    </section>
  )
}
