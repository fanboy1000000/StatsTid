// S76b / TASK-7603 — the "Vikariering" section of the unified EditPersonDrawer.
//
// Rendered only for a person who APPROVES others (edit mode). Shows the active
// vikar ("Vikar: <name> til <until>") + "Afslut" (DELETE .../{managerId}/vikar),
// else a VikarForm (vikar picker [server-search] + "Til og med" date + "Årsag"
// select) that creates the admin-on-behalf vikar via POST .../{managerId}/vikar
// (the S76/7601 endpoint; NO If-Match). The 409 (manager already has an active
// vikar) and 400 (cross-tree / coverage / cycle) surface as honest Danish.
import { useCallback, useState } from 'react'
import { useToast } from '../../../components/ui/Toast'
import { useReportingLines } from '../../../hooks/useReportingLines'
import { PersonPickerDialog } from './PersonPickerDialog'
import styles from './LifecycleSections.module.css'

/** The manager_vikar.reason CHECK set (matches the backend). */
const VIKAR_REASONS = [
  { value: 'FERIE', label: 'Ferie' },
  { value: 'SYGDOM', label: 'Sygdom' },
  { value: 'ORLOV', label: 'Orlov' },
  { value: 'TJENESTEREJSE', label: 'Tjenesterejse' },
  { value: 'ANDET', label: 'Andet' },
] as const

export interface ActiveVikar {
  vikarUserId: string
  vikarDisplayName: string
  untilDate: string
  reason: string
}

interface VikarSectionProps {
  /** The absent manager (the person being edited). */
  managerId: string
  managerName: string
  /** The currently-active vikar, if any (from the tree's outgoingVikar). */
  activeVikar?: ActiveVikar | null
  /** Self + descendants — the cycle-prevention forbidden set (the vikar must not
      be one of the manager's own reports). Also enforced server-side. */
  forbidden?: Set<string>
  /** Fired after a successful create/end so the caller refetches. */
  onChanged?: () => void
  /** S86 — reveal the create form immediately on mount (the inline tree-row
      "+ Vikar" affordance is a single click → the form appears). The drawer does
      NOT pass this (its affordance is the "Opret vikariering" button). */
  autoOpenForm?: boolean
  /** S86 — fired when the inline create form is cancelled (Annullér) so the inline
      wrapper collapses back to its "+ Vikar" trigger. Drawer does NOT pass it. */
  onCancel?: () => void
  disabled?: boolean
}

export function VikarSection({
  managerId,
  managerName,
  activeVikar,
  forbidden,
  onChanged,
  autoOpenForm = false,
  onCancel,
  disabled = false,
}: VikarSectionProps) {
  const { toast } = useToast()
  const { createVikar, endVikar } = useReportingLines()
  const [busy, setBusy] = useState(false)
  const [showForm, setShowForm] = useState(autoOpenForm)
  const [pickerOpen, setPickerOpen] = useState(false)

  // Form state.
  const [vikarUserId, setVikarUserId] = useState<string | null>(null)
  const [vikarName, setVikarName] = useState<string | null>(null)
  const [until, setUntil] = useState('')
  const [reason, setReason] = useState<string>('FERIE')

  // Local mirror so the row reflects the just-created/ended vikar immediately.
  const [localVikar, setLocalVikar] = useState<ActiveVikar | null | undefined>(undefined)
  const effectiveVikar = localVikar !== undefined ? localVikar : activeVikar ?? null

  const resetForm = useCallback(() => {
    setShowForm(false)
    setVikarUserId(null)
    setVikarName(null)
    setUntil('')
    setReason('FERIE')
  }, [])

  const handleCreate = useCallback(async () => {
    if (!vikarUserId || !until) return
    setBusy(true)
    const result = await createVikar(managerId, {
      vikarUserId,
      effectiveTo: until,
      reason,
    })
    setBusy(false)
    if (result.ok) {
      setLocalVikar({
        vikarUserId: result.data.vikarUserId,
        vikarDisplayName: vikarName ?? result.data.vikarUserId,
        untilDate: result.data.effectiveTo,
        reason: result.data.reason,
      })
      resetForm()
      toast({ title: 'Vikariering oprettet', variant: 'success' })
      onChanged?.()
    } else {
      // Honest, status-discriminated Danish messages.
      const msg =
        result.status === 409
          ? 'Manageren har allerede en aktiv vikar. Afslut den først.'
          : result.status === 400
            ? 'Vikaren skal være i samme styrelse, dække alle managerens medarbejdere og må ikke være en af managerens egne medarbejdere.'
            : result.status === 404
              ? 'Manageren blev ikke fundet.'
              : `Kunne ikke oprette vikariering (HTTP ${result.status}).`
      toast({ title: 'Vikariering mislykkedes', description: msg, variant: 'error' })
    }
  }, [vikarUserId, until, reason, vikarName, managerId, createVikar, resetForm, toast, onChanged])

  const handleEnd = useCallback(async () => {
    setBusy(true)
    const result = await endVikar(managerId)
    setBusy(false)
    if (result.ok) {
      setLocalVikar(null)
      toast({ title: 'Vikariering afsluttet', variant: 'success' })
      onChanged?.()
    } else {
      const msg =
        result.status === 404
          ? 'Der er ingen aktiv vikar at afslutte.'
          : `Kunne ikke afslutte vikariering (HTTP ${result.status}).`
      toast({ title: 'Handling mislykkedes', description: msg, variant: 'error' })
    }
  }, [managerId, endVikar, toast, onChanged])

  return (
    <section className={styles.section} aria-labelledby="ep-vikar-heading">
      <h3 id="ep-vikar-heading" className={styles.sectionLabel}>
        Vikariering
      </h3>

      {effectiveVikar ? (
        <div className={styles.vikarActive} data-testid="vikar-active">
          <div className={styles.vikarLine}>
            <span className={styles.vikarLabel}>Vikar</span>
            <strong>{effectiveVikar.vikarDisplayName}</strong>
            <span className={styles.muted}>til {effectiveVikar.untilDate}</span>
          </div>
          <button
            type="button"
            className={styles.linkDanger}
            onClick={handleEnd}
            disabled={disabled || busy}
            data-testid="vikar-end"
          >
            Afslut
          </button>
        </div>
      ) : showForm ? (
        <div className={styles.vikarForm} data-testid="vikar-form">
          <div className={styles.formField}>
            <span className={styles.formLabel}>Vikar overtager godkendelse</span>
            <button
              type="button"
              className={styles.pickerTrigger}
              onClick={() => setPickerOpen(true)}
              disabled={disabled || busy}
              data-testid="vikar-pick"
            >
              {vikarName ?? vikarUserId ?? 'Vælg vikar…'}
            </button>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel} htmlFor="ep-vikar-until">
              Til og med
            </label>
            <input
              id="ep-vikar-until"
              className={styles.input}
              type="date"
              value={until}
              onChange={(e) => setUntil(e.target.value)}
              disabled={disabled || busy}
              data-testid="vikar-until"
            />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel} htmlFor="ep-vikar-reason">
              Årsag
            </label>
            <select
              id="ep-vikar-reason"
              className={styles.select}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={disabled || busy}
              data-testid="vikar-reason"
            >
              {VIKAR_REASONS.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </select>
          </div>
          <div className={styles.formActions}>
            <button
              type="button"
              className={styles.primaryBtn}
              onClick={handleCreate}
              disabled={disabled || busy || !vikarUserId || !until}
              data-testid="vikar-create"
            >
              Opret vikariering
            </button>
            <button
              type="button"
              className={styles.ghostBtn}
              onClick={() => {
                resetForm()
                onCancel?.()
              }}
              disabled={busy}
            >
              Annullér
            </button>
          </div>
        </div>
      ) : (
        <div>
          <button
            type="button"
            className={styles.secondaryBtn}
            onClick={() => setShowForm(true)}
            disabled={disabled || busy}
            data-testid="vikar-open-form"
          >
            Opret vikariering
          </button>
          <span className={styles.muted}> Når medarbejderen er fraværende.</span>
        </div>
      )}

      <PersonPickerDialog
        open={pickerOpen}
        title={`Vælg vikar for ${managerName || 'manager'}`}
        currentId={vikarUserId}
        forbidden={forbidden}
        excludeEmployeeId={managerId}
        onPick={(id, name) => {
          setVikarUserId(id)
          setVikarName(name)
          setPickerOpen(false)
        }}
        onClose={() => setPickerOpen(false)}
      />
    </section>
  )
}
