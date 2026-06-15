// S76b / TASK-7603 — the "Godkendes af" (ledelseslinje approver) section of the
// unified EditPersonDrawer.
//
// EDIT mode: shows the current PRIMARY manager (resolved name) or a "+ Tildel
// godkender" affordance; the PersonPickerDialog assigns/reassigns via
// `POST /api/admin/reporting-lines` (If-None-Match:* on a FIRST assign, If-Match
// on a reassign) and "Fjern" removes via `DELETE .../{employeeId}` (If-Match).
//
// CREATE mode: the picker sets the draft `approverId` (no reporting-line call) —
// it is threaded into the SAME create POST (S74 R9 atomic create+assign). A root
// person shows "Øverste godkendelseslinje" (read-only, no approver required).
import { useCallback, useState } from 'react'
import { useToast } from '../../../components/ui/Toast'
import { useReportingLines } from '../../../hooks/useReportingLines'
import { PersonPickerDialog } from './PersonPickerDialog'
import styles from './LifecycleSections.module.css'

interface ApproverSectionProps {
  mode: 'create' | 'edit'
  /** The person being edited/created (for the picker title). */
  personName: string
  /** Edit mode — the person whose approver is being managed. */
  employeeId?: string
  /** True when the person sits at the top of a line (no approver required). */
  isRoot?: boolean
  /** Edit mode — the current PRIMARY manager id + display name (from the tree). */
  currentApproverId?: string | null
  currentApproverName?: string | null
  /** The current PRIMARY reporting line's ETag (If-Match for a reassign/remove).
      Absent ⇒ a FIRST assign (If-None-Match:*). */
  currentReportingLineEtag?: string | null
  /** Self + descendants — the cycle-prevention forbidden set (also enforced
      server-side via `excludeEmployeeId`). */
  forbidden?: Set<string>
  /** Create mode — the draft approver state, threaded into the create POST. */
  draftApproverId?: string | null
  draftApproverName?: string | null
  onDraftApproverChange?: (id: string | null, name: string | null) => void
  /** Fired after a successful assign/reassign/remove so the caller refetches. */
  onChanged?: () => void
  disabled?: boolean
}

export function ApproverSection({
  mode,
  personName,
  employeeId,
  isRoot = false,
  currentApproverId,
  currentApproverName,
  currentReportingLineEtag,
  forbidden,
  draftApproverId,
  draftApproverName,
  onDraftApproverChange,
  onChanged,
  disabled = false,
}: ApproverSectionProps) {
  const { toast } = useToast()
  const { assignManager, removeManager } = useReportingLines()
  const [pickerOpen, setPickerOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  // Edit-mode local mirror so the row updates immediately after an assign/remove
  // without waiting for a parent refetch (the parent ALSO refetches via onChanged).
  const [localApproverId, setLocalApproverId] = useState<string | null | undefined>(undefined)
  const [localApproverName, setLocalApproverName] = useState<string | null>(null)
  const [localEtag, setLocalEtag] = useState<string | null | undefined>(undefined)

  const effectiveApproverId =
    localApproverId !== undefined ? localApproverId : currentApproverId ?? null
  const effectiveApproverName =
    localApproverId !== undefined ? localApproverName : currentApproverName ?? null
  const effectiveEtag = localEtag !== undefined ? localEtag : currentReportingLineEtag ?? null

  const todayIso = new Date().toISOString().slice(0, 10)

  const handlePick = useCallback(
    async (userId: string, displayName: string) => {
      setPickerOpen(false)

      // CREATE mode — just set the draft; the create POST plants the line.
      if (mode === 'create') {
        onDraftApproverChange?.(userId, displayName)
        return
      }

      if (!employeeId) return
      setBusy(true)
      // First assign → If-None-Match:*; reassign → If-Match the current line ETag.
      const ifMatch = effectiveApproverId && effectiveEtag ? effectiveEtag : undefined
      const result = await assignManager(
        { employeeId, managerId: userId, effectiveFrom: todayIso },
        ifMatch,
      )
      setBusy(false)
      if (result.ok) {
        setLocalApproverId(userId)
        setLocalApproverName(displayName)
        // The assign response carries the new line's version; compose its ETag.
        setLocalEtag(`"${result.data.version}"`)
        toast({ title: 'Godkender tildelt', variant: 'success' })
        onChanged?.()
      } else {
        const msg =
          result.status === 412
            ? 'Ledelseslinjen er ændret af en anden. Genindlæs og prøv igen.'
            : result.status === 400
              ? 'Godkenderen skal være i samme styrelse og må ikke skabe en cyklus.'
              : result.status === 409
                ? 'Der findes allerede en godkender. Genindlæs og prøv igen.'
                : `Kunne ikke tildele godkender (HTTP ${result.status}).`
        toast({ title: 'Tildeling mislykkedes', description: msg, variant: 'error' })
      }
    },
    [
      mode,
      employeeId,
      effectiveApproverId,
      effectiveEtag,
      assignManager,
      onDraftApproverChange,
      onChanged,
      toast,
      todayIso,
    ],
  )

  const handleRemove = useCallback(async () => {
    if (mode === 'create') {
      onDraftApproverChange?.(null, null)
      return
    }
    if (!employeeId || !effectiveEtag) return
    setBusy(true)
    const result = await removeManager(employeeId, effectiveEtag)
    setBusy(false)
    if (result.ok) {
      setLocalApproverId(null)
      setLocalApproverName(null)
      setLocalEtag(null)
      toast({ title: 'Godkender fjernet', variant: 'success' })
      onChanged?.()
    } else {
      const msg =
        result.status === 412
          ? 'Ledelseslinjen er ændret af en anden. Genindlæs og prøv igen.'
          : result.status === 409
            ? 'Personen er øverst i linjen og kan ikke fjernes som godkender.'
            : `Kunne ikke fjerne godkender (HTTP ${result.status}).`
      toast({ title: 'Handling mislykkedes', description: msg, variant: 'error' })
    }
  }, [mode, employeeId, effectiveEtag, removeManager, onDraftApproverChange, onChanged, toast])

  // Which approver to display: create → the draft; edit → the effective.
  const shownId = mode === 'create' ? draftApproverId ?? null : effectiveApproverId
  const shownName = mode === 'create' ? draftApproverName ?? null : effectiveApproverName

  return (
    <section className={styles.section} aria-labelledby="ep-approver-heading">
      <h3 id="ep-approver-heading" className={styles.sectionLabel}>
        Ledelseslinje
      </h3>

      <div className={styles.recRow}>
        <span className={styles.recKey}>
          Godkendes af
          {!isRoot && <span className={styles.required}> *</span>}
        </span>
        <span className={styles.recValue}>
          {isRoot ? (
            <span className={styles.muted} data-testid="approver-root">
              Øverste godkendelseslinje
            </span>
          ) : shownId ? (
            <span className={styles.assigned} data-testid="approver-assigned">
              <span className={styles.assignedName}>{shownName ?? shownId}</span>
              <button
                type="button"
                className={styles.link}
                onClick={() => setPickerOpen(true)}
                disabled={disabled || busy}
                data-testid="approver-change"
              >
                Skift
              </button>
              <button
                type="button"
                className={styles.linkDanger}
                onClick={handleRemove}
                disabled={disabled || busy}
                data-testid="approver-remove"
              >
                Fjern
              </button>
            </span>
          ) : (
            <button
              type="button"
              className={styles.assignEmpty}
              onClick={() => setPickerOpen(true)}
              disabled={disabled || busy}
              data-testid="approver-assign"
            >
              + Tildel godkender
            </button>
          )}
        </span>
      </div>

      <PersonPickerDialog
        open={pickerOpen}
        title={`Vælg godkender for ${personName || 'medarbejder'}`}
        currentId={shownId}
        forbidden={forbidden}
        excludeEmployeeId={mode === 'edit' ? employeeId : undefined}
        onPick={handlePick}
        onClose={() => setPickerOpen(false)}
      />
    </section>
  )
}
