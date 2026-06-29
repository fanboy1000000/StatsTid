// SPRINT-108 / TASK-10801 (Enhedsspor Phase 3b-2a) — the UNIT structure-mutation
// surface for the merged "Organisation & medarbejdere" admin page:
//
//   • UnitDrawer        — create a child unit (name only) / edit a unit (rename +
//                          designate peer leaders from the unit's OWN members).
//   • UnitMoveDialog    — re-parent within the same Organisation (the picker
//                          already excludes self + descendants + same-or-deeper
//                          type-rank targets, and offers "→ Rod").
//   • UnitDeleteConfirm — a confirm-and-CASCADE dialog (NOT a guard): S104's
//                          DELETE soft-deletes + re-parents children UP / re-homes
//                          members UP / clears leaders, so the copy WARNS of that.
//
// These are presentational: StrukturPanel owns the action state, wires the S104
// mutations (useUnitMutations) and the refetch, and surfaces the real errors
// (412 stale / 409 dup-name / 422 parent- or member-validation) inline via the
// `error` prop. The Ledere checkboxes encode the member-invariant ("En leder skal
// være placeret i denne enhed"): only the unit's own members are listed.
//
// Square corners, hairline borders, IBM Plex Sans — tokens, no hardcoded hex.

import { useState, type FormEvent, type ReactNode } from 'react'
import { Button, Checkbox, Drawer, Input } from '../../../components/ui'
import { LABEL, type UnitType } from './typeMaps'
import styles from './UnitDrawer.module.css'

/** A leader-candidate row (the unit's own member). */
export interface UnitMemberOption {
  employeeId: string
  displayName: string
  position: string | null
}

interface UnitDrawerProps {
  mode: 'create' | 'edit'
  /** create: the derived child-unit type label (e.g. "Team"); edit: the unit's
      own type label. Drives the drawer title. */
  typeLabel: string
  /** edit: the current name (prefilled). create: ''. */
  initialName: string
  /** edit only: the unit's own members (the leader candidates). */
  members?: UnitMemberOption[]
  /** edit only: the currently-designated leader ids. */
  currentLeaderIds?: string[]
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmitCreate?: (name: string) => void
  onSubmitEdit?: (name: string, addLeaderIds: string[], removeLeaderIds: string[]) => void
}

export function UnitDrawer({
  mode,
  typeLabel,
  initialName,
  members = [],
  currentLeaderIds = [],
  busy,
  error,
  onClose,
  onSubmitCreate,
  onSubmitEdit,
}: UnitDrawerProps) {
  const [name, setName] = useState(initialName)
  const [leaders, setLeaders] = useState<Set<string>>(() => new Set(currentLeaderIds))

  const title =
    mode === 'create' ? `Opret ${typeLabel.toLowerCase()}` : `Rediger ${typeLabel.toLowerCase()}`

  const toggleLeader = (id: string) =>
    setLeaders((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) return
    if (mode === 'create') {
      onSubmitCreate?.(trimmed)
      return
    }
    const current = new Set(currentLeaderIds)
    const addLeaderIds = [...leaders].filter((id) => !current.has(id))
    const removeLeaderIds = currentLeaderIds.filter((id) => !leaders.has(id))
    onSubmitEdit?.(trimmed, addLeaderIds, removeLeaderIds)
  }

  return (
    <Drawer open onClose={onClose} ariaLabel={title}>
      <form className={styles.drawerForm} onSubmit={submit}>
        <div className={styles.drawerHead}>
          <h2 className={styles.drawerTitle} data-testid="unit-drawer-title">
            {title}
          </h2>
          <button type="button" className={styles.drawerClose} aria-label="Luk" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className={styles.drawerBody}>
          <label className={styles.fieldLabel} htmlFor="unit-drawer-name">
            Navn
          </label>
          <Input
            id="unit-drawer-name"
            data-testid="unit-drawer-name"
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Navn på enheden"
          />

          {mode === 'edit' && (
            <div className={styles.leaderSection}>
              <div className={styles.fieldLabel}>Ledere</div>
              <p className={styles.fieldHint}>
                En leder skal være placeret i denne enhed. Vælg en eller flere blandt enhedens
                egne medarbejdere.
              </p>
              {members.length === 0 ? (
                <p className={styles.emptyMembers} data-testid="unit-drawer-no-members">
                  Enheden har ingen medarbejdere endnu.
                </p>
              ) : (
                <div className={styles.leaderList}>
                  {members.map((m) => (
                    <Checkbox
                      key={m.employeeId}
                      id={`leader-checkbox-${m.employeeId}`}
                      checked={leaders.has(m.employeeId)}
                      onChange={() => toggleLeader(m.employeeId)}
                      label={m.position ? `${m.displayName} · ${m.position}` : m.displayName}
                    />
                  ))}
                </div>
              )}
            </div>
          )}

          {error && (
            <div className={styles.error} role="alert" data-testid="unit-drawer-error">
              {error}
            </div>
          )}
        </div>

        <div className={styles.drawerFooter}>
          <Button type="button" variant="ghost" size="md" onClick={onClose}>
            Annuller
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="md"
            data-testid="unit-drawer-submit"
            disabled={busy || name.trim().length === 0}
          >
            {busy ? 'Gemmer…' : 'Gem'}
          </Button>
        </div>
      </form>
    </Drawer>
  )
}

// ── Move dialog ────────────────────────────────────────────────────────────────

export interface UnitMoveTarget {
  id: string
  name: string
  type: UnitType
}

interface UnitMoveDialogProps {
  unitName: string
  /** Valid parent targets — already filtered (no self / descendants / same-or-
      deeper type-rank). The "→ Rod" (top-level) option is always offered. */
  targets: UnitMoveTarget[]
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (newParentUnitId: string | null) => void
}

const ROOT_VALUE = '__ROOT__'

export function UnitMoveDialog({
  unitName,
  targets,
  busy,
  error,
  onClose,
  onSubmit,
}: UnitMoveDialogProps) {
  const [target, setTarget] = useState<string>(ROOT_VALUE)

  return (
    <DialogShell title="Flyt enhed" onClose={onClose}>
      <p className={styles.dialogText}>
        Vælg en ny placering for <strong>{unitName}</strong>. Medarbejdere følger med.
      </p>
      <label className={styles.fieldLabel} htmlFor="unit-move-target">
        Ny placering
      </label>
      <select
        id="unit-move-target"
        data-testid="unit-move-target"
        className={styles.dialogSelect}
        value={target}
        onChange={(e) => setTarget(e.target.value)}
      >
        <option value={ROOT_VALUE}>→ Rod (øverste niveau)</option>
        {targets.map((t) => (
          <option key={t.id} value={t.id} data-testid={`unit-move-option-${t.id}`}>
            {t.name} ({LABEL[t.type]})
          </option>
        ))}
      </select>
      {error && (
        <div className={styles.error} role="alert" data-testid="unit-move-error">
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
          data-testid="unit-move-submit"
          disabled={busy}
          onClick={() => onSubmit(target === ROOT_VALUE ? null : target)}
        >
          {busy ? 'Flytter…' : 'Flyt'}
        </Button>
      </div>
    </DialogShell>
  )
}

// ── Delete confirm (confirm-and-CASCADE) ────────────────────────────────────────

interface UnitDeleteConfirmProps {
  unitName: string
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}

export function UnitDeleteConfirm({
  unitName,
  busy,
  error,
  onClose,
  onConfirm,
}: UnitDeleteConfirmProps) {
  return (
    <DialogShell title="Slet enhed?" onClose={onClose}>
      <div className={styles.dangerPanel} data-testid="unit-delete-warning">
        Du er ved at slette <strong>{unitName}</strong>. Underenheder og medarbejdere flyttes ét
        niveau op, og ledertildelinger fjernes. Handlingen kan ikke fortrydes.
      </div>
      {error && (
        <div className={styles.error} role="alert" data-testid="unit-delete-error">
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
          data-testid="unit-delete-confirm"
          disabled={busy}
          onClick={onConfirm}
        >
          {busy ? 'Sletter…' : 'Slet enhed'}
        </Button>
      </div>
    </DialogShell>
  )
}

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
    <div className={styles.scrim} role="presentation" onClick={onClose} data-testid="unit-dialog-scrim">
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
