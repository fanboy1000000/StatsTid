// S76b / TASK-7603 — the "Fjern medarbejder fra afgrænsning" danger zone of the
// unified EditPersonDrawer (delete-with-reassignment).
//
// A TWO-STEP (potentially N-round) dialog:
//   1. submit POST .../{employeeId}/remove with an empty replacements map;
//   2. on a 409 PREFLIGHT, parse the reports-needing-a-replacement list and render
//      a replacement-approver picker per report; resubmit with the `replacements`
//      map;
//   3. on a SECOND 409 (the in-lock census surfaced a report assigned BETWEEN the
//      preflight and the commit) MERGE the new gap list into the existing
//      selections and re-prompt — we do NOT assume a single round.
// Success → close + signal the caller to refetch.
import { useCallback, useState } from 'react'
import { useToast } from '../../../components/ui/Toast'
import { useReportingLines } from '../../../hooks/useReportingLines'
import { PersonPickerDialog } from './PersonPickerDialog'
import styles from './LifecycleSections.module.css'

interface DangerSectionProps {
  employeeId: string
  personName: string
  /** Self + descendants — the forbidden set for the replacement-approver pickers
      (a replacement cannot be the removed person; server also guards). */
  forbidden?: Set<string>
  /** Fired after a successful removal so the caller refetches + closes. */
  onRemoved?: () => void
  disabled?: boolean
}

type DialogPhase =
  | { kind: 'closed' }
  | { kind: 'confirm' }
  // The gap list (report ids needing a replacement) + the per-report selections.
  | { kind: 'reassign'; gap: string[]; message: string }

export function DangerSection({
  employeeId,
  personName,
  forbidden,
  onRemoved,
  disabled = false,
}: DangerSectionProps) {
  const { toast } = useToast()
  const { deletePersonWithReassignment } = useReportingLines()
  const [phase, setPhase] = useState<DialogPhase>({ kind: 'closed' })
  const [busy, setBusy] = useState(false)
  // reportEmployeeId → replacementApproverId (+ name for display).
  const [replacements, setReplacements] = useState<Record<string, string>>({})
  const [replacementNames, setReplacementNames] = useState<Record<string, string>>({})
  // Which report's replacement picker is open (null = none).
  const [pickerForReport, setPickerForReport] = useState<string | null>(null)

  const reset = useCallback(() => {
    setPhase({ kind: 'closed' })
    setReplacements({})
    setReplacementNames({})
    setPickerForReport(null)
    setBusy(false)
  }, [])

  // The single submit path — used by the first (empty) submit AND every resubmit.
  // Handles BOTH 409s: a 409 always re-renders the (possibly-merged) gap list so
  // a report assigned between preflight and commit is re-prompted.
  const submit = useCallback(
    async (map: Record<string, string>) => {
      setBusy(true)
      const result = await deletePersonWithReassignment(employeeId, map)
      setBusy(false)

      if (result.ok) {
        toast({ title: 'Medarbejder fjernet', variant: 'success' })
        reset()
        onRemoved?.()
        return
      }

      if (result.status === 409 && result.gap) {
        // Merge: keep existing selections, surface any NEW report id (the in-lock
        // census case) — we re-prompt for the full current gap list.
        setPhase({
          kind: 'reassign',
          gap: result.gap.reportsNeedingReassignment,
          message: result.gap.message,
        })
        return
      }

      // 400 (cross-tree / transferred report), 422 (bad replacement), 403 (scope),
      // or anything else — honest message; keep the dialog open so the admin can
      // adjust the replacements.
      const msg =
        result.status === 400
          ? 'En medarbejder er flyttet til en anden styrelse eller erstatningen er ugyldig. Ret den og prøv igen.'
          : result.status === 422
            ? 'En valgt erstatningsgodkender er ugyldig. Vælg en anden.'
            : result.status === 403
              ? 'Du har ikke adgang til at fjerne denne medarbejder.'
              : `Kunne ikke fjerne medarbejderen (HTTP ${result.status}).`
      toast({ title: 'Fjernelse mislykkedes', description: msg, variant: 'error' })
    },
    [employeeId, deletePersonWithReassignment, toast, reset, onRemoved],
  )

  const handleConfirmSubmit = useCallback(() => {
    // First attempt: empty replacements → the preflight tells us which reports
    // need one (or removes directly if the person approves no one).
    void submit({})
  }, [submit])

  const handleReassignSubmit = useCallback(() => {
    void submit(replacements)
  }, [submit, replacements])

  // Every gap report must have a replacement before the resubmit is enabled.
  const gapList = phase.kind === 'reassign' ? phase.gap : []
  const allChosen = gapList.every((r) => !!replacements[r])

  return (
    <section className={styles.section} aria-labelledby="ep-danger-heading">
      <div className={styles.dangerZone}>
        <button
          type="button"
          className={styles.linkDanger}
          onClick={() => setPhase({ kind: 'confirm' })}
          disabled={disabled || busy}
          data-testid="danger-open"
        >
          Fjern medarbejder fra afgrænsning
        </button>
      </div>

      {/* Step 1 — confirm. */}
      {phase.kind === 'confirm' && (
        <div className={styles.dangerDialog} role="dialog" aria-modal="true" data-testid="danger-confirm">
          <p className={styles.dangerText}>
            Fjern <strong>{personName || employeeId}</strong> fra afgrænsningen? Hvis personen
            godkender andre, skal du vælge en erstatningsgodkender for hver.
          </p>
          <div className={styles.formActions}>
            <button
              type="button"
              className={styles.dangerBtn}
              onClick={handleConfirmSubmit}
              disabled={busy}
              data-testid="danger-confirm-submit"
            >
              {busy ? 'Fjerner…' : 'Fjern medarbejder'}
            </button>
            <button type="button" className={styles.ghostBtn} onClick={reset} disabled={busy}>
              Annullér
            </button>
          </div>
        </div>
      )}

      {/* Step 2..N — per-report replacement-approver pickers (re-rendered on EACH
          409, so a NEW report from the in-lock census is re-prompted). */}
      {phase.kind === 'reassign' && (
        <div className={styles.dangerDialog} role="dialog" aria-modal="true" data-testid="danger-reassign">
          <p className={styles.dangerText} data-testid="danger-reassign-message">
            {phase.message}
          </p>
          <ul className={styles.gapList}>
            {gapList.map((reportId) => (
              <li key={reportId} className={styles.gapRow} data-testid={`gap-row-${reportId}`}>
                <span className={styles.gapReport}>{reportId}</span>
                <button
                  type="button"
                  className={styles.link}
                  onClick={() => setPickerForReport(reportId)}
                  disabled={busy}
                  data-testid={`gap-pick-${reportId}`}
                >
                  {replacementNames[reportId] ?? replacements[reportId] ?? 'Vælg erstatningsgodkender'}
                </button>
              </li>
            ))}
          </ul>
          <div className={styles.formActions}>
            <button
              type="button"
              className={styles.dangerBtn}
              onClick={handleReassignSubmit}
              disabled={busy || !allChosen}
              data-testid="danger-reassign-submit"
            >
              {busy ? 'Fjerner…' : 'Fjern og omfordel'}
            </button>
            <button type="button" className={styles.ghostBtn} onClick={reset} disabled={busy}>
              Annullér
            </button>
          </div>
        </div>
      )}

      {/* The replacement-approver picker for the active gap report. */}
      <PersonPickerDialog
        open={pickerForReport !== null}
        title="Vælg erstatningsgodkender"
        currentId={pickerForReport ? replacements[pickerForReport] : null}
        forbidden={forbidden}
        excludeEmployeeId={employeeId}
        onPick={(userId, displayName) => {
          if (pickerForReport) {
            setReplacements((prev) => ({ ...prev, [pickerForReport]: userId }))
            setReplacementNames((prev) => ({ ...prev, [pickerForReport]: displayName }))
          }
          setPickerForReport(null)
        }}
        onClose={() => setPickerForReport(null)}
      />
    </section>
  )
}
