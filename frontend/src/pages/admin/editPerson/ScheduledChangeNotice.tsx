// SPRINT-141 / TASK-14107 — B0 (the owner's visibility requirement, asked
// directly: "Should it not be visible to an HR employee looking at a page,
// that another has scheduled a change?"). This renders the read-only notice
// ("a change is already scheduled from DATE") wherever a profile or
// agreement-code value is shown, and — only when the field(s) HR is
// currently editing overlap that scheduled change — the OQ-6 (a) choice the
// owner ruled: apply the edit only until the scheduled date, or carry it
// into the scheduled change as well.
//
// Deliberately NOT under `components/ui/` (the design-sync gate cannot run on
// this machine — Python is absent — so a new component there would turn the
// close red with no local way to verify it; this file lives beside the
// section components it decorates, like every other editPerson/* file).
import styles from '../EditPersonDrawer.module.css'

export interface ScheduledChangeCarryForwardChoice {
  /** true = "also update the scheduled change" (OQ-6 branch 2). */
  checked: boolean
  onChange: (next: boolean) => void
  disabled?: boolean
}

export interface ScheduledChangeNoticeProps {
  /** ISO date (yyyy-MM-dd) the scheduled change takes effect. */
  effectiveFrom: string
  /** Danish, human-readable summary of what the scheduled change sets, e.g.
      "Deltidsfraktion 0,600 · Fuldmægtig". */
  summary: string
  /** Present ONLY when the field(s) HR just edited also carry this scheduled
      change — renders the OQ-6 (a) choice. Absent = pure visibility (B0),
      nothing to decide. */
  carryForward?: ScheduledChangeCarryForwardChoice
  /**
   * S141 / TASK-14111 — the date THIS save will actually use, ONLY when the
   * drawer's effective-date picker has moved it away from today (the caller
   * omits this when the write is dated today, which is the common case).
   *
   * Composition note, so the picker and this notice never talk past each
   * other: the carry-forward copy below used to say "Gemmer du nu" ("if you
   * save now"), which was always true because the drawer had no way to save
   * anything else. Once a save can be dated ahead (or backdated), "nu" is
   * simply wrong for it — the edit takes effect on the picked date, not on
   * today. This prop lets the SAME copy stay correct in both cases without
   * this component knowing what "today" is (the caller already computed
   * that once, for the picker itself).
   */
  writeEffectiveFrom?: string
  testId: string
}

/** "1. november 2026" — UTC-anchored so a plain `date` string never rolls
    back a day under a viewer's local timezone (mirrors the codebase's other
    `formatDaLongDate`-shaped helpers, e.g. `admin/opfoelgning/followUpFormat.ts`). */
export function formatScheduledDate(iso: string): string {
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

export function ScheduledChangeNotice({
  effectiveFrom,
  summary,
  carryForward,
  writeEffectiveFrom,
  testId,
}: ScheduledChangeNoticeProps) {
  const dateText = formatScheduledDate(effectiveFrom)
  // undefined/omitted = the write is dated today, the previous (and still
  // most common) wording. Present = name the real date rather than "nu".
  const writeDateText = writeEffectiveFrom ? formatScheduledDate(writeEffectiveFrom) : null
  return (
    <div className={styles.scheduledNotice} data-testid={testId}>
      <p className={styles.scheduledText}>
        En ændring er allerede planlagt fra <strong>{dateText}</strong>: {summary}.
      </p>
      {carryForward && (
        <div className={styles.scheduledChoice}>
          <p className={styles.scheduledText}>
            {writeDateText
              ? carryForward.checked
                ? `Gemmer du med virkning fra ${writeDateText}, opdateres også den planlagte ændring fra ${dateText} (kun det felt, du har rettet her).`
                : `Gemmer du med virkning fra ${writeDateText}, gælder ændringen kun indtil ${dateText}, hvor den planlagte ændring træder i kraft.`
              : carryForward.checked
                ? `Gemmer du nu, opdateres også den planlagte ændring fra ${dateText} (kun det felt, du har rettet her).`
                : `Gemmer du nu, gælder ændringen kun indtil ${dateText}, hvor den planlagte ændring træder i kraft.`}
          </p>
          <label className={styles.checkboxRow}>
            <input
              type="checkbox"
              checked={carryForward.checked}
              onChange={(e) => carryForward.onChange(e.target.checked)}
              disabled={carryForward.disabled}
              data-testid={`${testId}-carry-forward`}
            />
            Opdatér også den planlagte ændring fra {dateText}
          </label>
        </div>
      )}
    </div>
  )
}
