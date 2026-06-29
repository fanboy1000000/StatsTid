// SPRINT-108 / TASK-10802 (Enhedsspor Phase 3b-2a) — the ORG / MAO structure-
// mutation dialogs for the merged "Organisation & medarbejdere" admin page. These
// PORT the OrganisationPage (S99) create / rename / move / 2-branch-delete dialog
// LOGIC onto the merged page, restyled to the enhedsspor centred-dialog look:
//
//   • OrgCreateDialog — name-only create (a MAO root OR an Organisation under a
//                       MAO; same POST keyed off orgType + parentOrgId).
//   • OrgRenameDialog — the rename-warning dialog (name slår igennem på SLS /
//                       rapporter / historik).
//   • OrgMoveDialog   — re-parent an Organisation under another MAO (the target is
//                       always a MAO; ORGANISATION-only — a MAO is a root).
//   • OrgDeleteDialog — the 2-branch dialog (blocked = has employees → 422-with-
//                       count / empty = confirm). NO third branch, NO flat-Enhed
//                       untag (those do not exist in the org tree).
//
// These are presentational: the caller (StrukturPanel for the per-node mutations,
// MaoCreateAction for the top-level MAO-create) owns the action state, wires the
// S98/S99 mutations (useOrgMutations) + the refetch, and surfaces the real errors
// (422 blocked-with-count / 400 no-op / 409 dup) inline via the `error` prop.
//
// The FE gate is UX only (P7): the backend re-checks the role floor on every call.

import { useState, type FormEvent, type ReactNode } from 'react'
import { Button, Input, useToast } from '../../../components/ui'
import { useOrgMutations, type OrgType } from '../../../hooks/useOrgMutations'
import styles from './OrgStructureDialogs.module.css'

const ORG_TYPE_LABEL: Record<OrgType, string> = {
  MAO: 'Ministerområde',
  ORGANISATION: 'Organisation',
}

const orgTypeLower = (t: OrgType): string => ORG_TYPE_LABEL[t].toLowerCase()

// ── shared centred dialog shell (scrim + square card) ───────────────────────────
function DialogShell({
  title,
  onClose,
  children,
}: {
  title: string
  onClose: () => void
  children: ReactNode
}) {
  return (
    <div className={styles.scrim} role="presentation" onClick={onClose} data-testid="org-dialog-scrim">
      <div
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <div className={styles.dialogHead}>{title}</div>
        <div className={styles.dialogBody}>{children}</div>
      </div>
    </div>
  )
}

// ── create (name-only; MAO root or Organisation under a MAO) ─────────────────────
interface OrgCreateDialogProps {
  orgType: OrgType
  /** The parent MAO's display name (ORGANISATION create); null for a MAO root. */
  parentName: string | null
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (name: string) => void
}

export function OrgCreateDialog({ orgType, parentName, busy, error, onClose, onSubmit }: OrgCreateDialogProps) {
  const [name, setName] = useState('')
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (trimmed) onSubmit(trimmed)
  }
  return (
    <DialogShell title={`Nyt ${orgTypeLower(orgType)}`} onClose={onClose}>
      <form onSubmit={submit}>
        {parentName && (
          <p className={styles.dialogText}>
            Oprettes under <strong>{parentName}</strong>.
          </p>
        )}
        <label className={styles.dialogLabel} htmlFor="org-create-name">
          Navn
        </label>
        <Input
          id="org-create-name"
          data-testid="org-create-name"
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder={orgType === 'MAO' ? 'F.eks. Finansministeriet' : 'F.eks. Økonomistyrelsen'}
        />
        {error && (
          <div className={styles.error} role="alert" data-testid="org-create-error">
            {error}
          </div>
        )}
        <div className={styles.dialogFooter}>
          <Button type="button" variant="ghost" size="md" onClick={onClose}>
            Annuller
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="md"
            data-testid="org-create-submit"
            disabled={busy || name.trim().length === 0}
          >
            {busy ? 'Opretter…' : `Opret ${orgTypeLower(orgType)}`}
          </Button>
        </div>
      </form>
    </DialogShell>
  )
}

// ── rename (warning + name) ──────────────────────────────────────────────────────
interface OrgRenameDialogProps {
  orgType: OrgType
  currentName: string
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (name: string) => void
}

export function OrgRenameDialog({ orgType, currentName, busy, error, onClose, onSubmit }: OrgRenameDialogProps) {
  const [name, setName] = useState(currentName)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (trimmed) onSubmit(trimmed)
  }
  return (
    <DialogShell title={`Omdøb ${orgTypeLower(orgType)}`} onClose={onClose}>
      <form onSubmit={submit}>
        <div className={styles.warnPanel} data-testid="org-rename-warning">
          {orgType === 'MAO'
            ? 'Et nyt navn slår igennem på rapporter, budgetansvar og historik — også på tidligere perioder.'
            : 'Et nyt navn slår igennem på lønsystemet (SLS), rapporter og historik — også på tidligere perioder.'}
        </div>
        <label className={styles.dialogLabel} htmlFor="org-rename-name">
          Nyt navn
        </label>
        <Input
          id="org-rename-name"
          data-testid="org-rename-name"
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        {error && (
          <div className={styles.error} role="alert" data-testid="org-rename-error">
            {error}
          </div>
        )}
        <div className={styles.dialogFooter}>
          <Button type="button" variant="ghost" size="md" onClick={onClose}>
            Annuller
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="md"
            data-testid="org-rename-submit"
            disabled={busy || name.trim().length === 0}
          >
            {busy ? 'Gemmer…' : 'Gem ændring'}
          </Button>
        </div>
      </form>
    </DialogShell>
  )
}

// ── move (re-parent an Organisation under another MAO) ───────────────────────────
export interface OrgMoveTarget {
  orgId: string
  orgName: string
}

interface OrgMoveDialogProps {
  orgName: string
  /** The candidate parent MAOs (the current parent already excluded). */
  targets: OrgMoveTarget[]
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (newParentOrgId: string) => void
}

export function OrgMoveDialog({ orgName, targets, busy, error, onClose, onSubmit }: OrgMoveDialogProps) {
  const [target, setTarget] = useState('')
  return (
    <DialogShell title="Flyt organisation" onClose={onClose}>
      <p className={styles.dialogText}>
        Vælg en ny placering for <strong>{orgName}</strong>. Medarbejdere følger med.
      </p>
      <label className={styles.dialogLabel} htmlFor="org-move-target">
        Ny placering
      </label>
      <select
        id="org-move-target"
        data-testid="org-move-target"
        className={styles.dialogSelect}
        value={target}
        onChange={(e) => setTarget(e.target.value)}
      >
        <option value="">Vælg ministerområde…</option>
        {targets.map((t) => (
          <option key={t.orgId} value={t.orgId} data-testid={`org-move-option-${t.orgId}`}>
            {t.orgName}
          </option>
        ))}
      </select>
      {error && (
        <div className={styles.error} role="alert" data-testid="org-move-error">
          {error}
        </div>
      )}
      <div className={styles.dialogFooter}>
        <Button type="button" variant="ghost" size="md" onClick={onClose}>
          Annuller
        </Button>
        <Button
          type="button"
          variant="primary"
          size="md"
          data-testid="org-move-submit"
          disabled={busy || !target}
          onClick={() => target && onSubmit(target)}
        >
          {busy ? 'Flytter…' : 'Flyt'}
        </Button>
      </div>
    </DialogShell>
  )
}

// ── delete (2-branch: blocked vs empty) ──────────────────────────────────────────
interface OrgDeleteDialogProps {
  orgType: OrgType
  name: string
  branch: 'blocked' | 'empty'
  employeeCount: number
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}

export function OrgDeleteDialog({
  orgType,
  name,
  branch,
  employeeCount,
  busy,
  error,
  onClose,
  onConfirm,
}: OrgDeleteDialogProps) {
  if (branch === 'blocked') {
    const noun = orgType === 'MAO' ? 'ministerområdet' : 'organisationen'
    return (
      <DialogShell title="Kan ikke slette" onClose={onClose}>
        <div className={styles.dangerPanel} data-testid="org-delete-blocked">
          <strong>{name}</strong> indeholder {employeeCount} medarbejdere og kan ikke slettes. Alle
          medarbejdere skal være tilknyttet en organisation — flyt dem til en anden organisation, før
          du sletter {noun}.
        </div>
        <div className={styles.dialogFooter}>
          <Button type="button" variant="primary" size="md" data-testid="org-delete-close" onClick={onClose}>
            Luk
          </Button>
        </div>
      </DialogShell>
    )
  }

  const title = orgType === 'MAO' ? 'Slet ministerområde?' : 'Slet organisation?'
  const confirmLabel = orgType === 'MAO' ? 'Slet ministerområde' : 'Slet organisation'
  return (
    <DialogShell title={title} onClose={onClose}>
      <div className={styles.dangerPanel} data-testid="org-delete-warning">
        Du er ved at slette <strong>{name}</strong>. Handlingen kan ikke fortrydes.
      </div>
      {error && (
        <div className={styles.error} role="alert" data-testid="org-delete-error">
          {error}
        </div>
      )}
      <div className={styles.dialogFooter}>
        <Button type="button" variant="ghost" size="md" onClick={onClose}>
          Annuller
        </Button>
        <Button
          type="button"
          variant="danger"
          size="md"
          data-testid="org-delete-confirm"
          disabled={busy}
          onClick={onConfirm}
        >
          {busy ? 'Sletter…' : confirmLabel}
        </Button>
      </div>
    </DialogShell>
  )
}

// ── MAO-create top-level action (button + dialog) ────────────────────────────────
// The "+ Ministerområde" affordance is NOT tied to a selected node (a MAO is a
// root, parent-less) → it lives in the page's tree header rather than the
// StrukturPanel title block. GlobalAdmin-gated by the page (the backend re-checks
// HasGlobalScope). Self-contained: owns its dialog state + the create mutation +
// the toast, then asks the page to refetch the forest via `onCreated`.
export function MaoCreateAction({ onCreated }: { onCreated: () => void | Promise<void> }) {
  const { createOrg } = useOrgMutations()
  const { toast } = useToast()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (name: string) => {
    setBusy(true)
    setError(null)
    const res = await createOrg({ orgName: name, orgType: 'MAO', parentOrgId: null })
    if (res.ok) {
      setBusy(false)
      setOpen(false)
      toast({ title: 'Oprettet', description: 'Ministerområde oprettet', variant: 'success' })
      await onCreated()
    } else {
      setBusy(false)
      setError(res.error)
    }
  }

  return (
    <>
      <Button
        variant="secondary"
        size="sm"
        data-testid="mao-create-button"
        onClick={() => {
          setError(null)
          setOpen(true)
        }}
      >
        + Ministerområde
      </Button>
      {open && (
        <OrgCreateDialog
          orgType="MAO"
          parentName={null}
          busy={busy}
          error={error}
          onClose={() => setOpen(false)}
          onSubmit={submit}
        />
      )}
    </>
  )
}
