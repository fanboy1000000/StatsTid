import { useState, useMemo, useCallback, useEffect, type FormEvent } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import { useOrganizationTree } from '../../hooks/useOrganizationTree'
import {
  useOrganizationStructure,
  type OrgStructureError,
} from '../../hooks/useOrganizationStructure'
import { Badge, Spinner } from '../../components/ui'
import { useToast } from '../../components/ui/Toast'
import {
  visibleRows,
  searchRows,
  expandedForLevel,
  moveTargets,
  TYPE_LABEL,
  type OrgRow,
  type Level,
  type NodeType,
} from './organisationTree'
import styles from './OrganisationPage.module.css'

// S99 / TASK-9902–9904 — the redesigned Global administration → Organisation
// page (design_handoff_organisation). One indented, expandable tree-table of the
// hierarchy (MAO → Organisation) with a level control, search, the rolled-up
// "Medarb." count, and guarded create / rename / move / delete flows.
// GlobalAdmin-gated (route + nav); the backend re-checks every mutation. No
// leader / overenskomst column (handoff rule).
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the Enhed tier is REMOVED from this
// page. The tree is MAO → Organisation; Tilføj creates an Organisation under a
// MAO; Flyt re-homes an Organisation under another MAO.

const TITLE_SUB =
  'Forvalt organisationshierarkiet. Et ministeransvarsområde samler organisationer; ' +
  'organisationer er medarbejdernes obligatoriske tilknytning.'

const LEVEL_OPTIONS: { value: Level; label: string }[] = [
  { value: 'MAO', label: 'Ministeransvarsområde' },
  { value: 'ORGANISATION', label: 'Organisation' },
]

const TYPE_BADGE_VARIANT: Record<NodeType, 'info' | 'success'> = {
  MAO: 'info',
  ORGANISATION: 'success',
}

// ── Dialog state shapes ──
type CreateState = {
  parentId: string | null
  parentName: string | null
  type: NodeType
  value: string
}
type RenameState = { id: string; type: 'MAO' | 'ORGANISATION'; value: string }
type MoveState = { id: string; name: string; parentId: string | null; to: string }
type DeleteState = {
  row: OrgRow
  // resolved branch + data
  branch: 'blocked' | 'empty'
  employeeCount?: number
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      className={`${styles.chevron} ${open ? styles.chevronOpen : ''}`}
      width={11}
      height={11}
      viewBox="0 0 11 11"
      fill="none"
      aria-hidden="true"
    >
      <path
        d="M4 2.5L7 5.5L4 8.5"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="square"
      />
    </svg>
  )
}

export function OrganisationPage() {
  const { tree, loading, error, fetchTree } = useOrganizationTree()
  const { createOrganization, updateOrganization } = useOrganizations()
  const { deleteOrganization, moveOrganization } = useOrganizationStructure()
  const { toast } = useToast()

  // ── view state ──
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [level, setLevel] = useState<Level | null>('ORGANISATION')
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)

  // ── dialog state (one open at a time) ──
  const [createModal, setCreateModal] = useState<CreateState | null>(null)
  const [renameModal, setRenameModal] = useState<RenameState | null>(null)
  const [moveModal, setMoveModal] = useState<MoveState | null>(null)
  const [deleteModal, setDeleteModal] = useState<DeleteState | null>(null)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Default-expand to Organisation level once the tree arrives (and the user has
  // not manually toggled — i.e. the active level is still ORGANISATION).
  useEffect(() => {
    if (tree.length > 0 && level === 'ORGANISATION' && expanded.size === 0) {
      setExpanded(expandedForLevel(tree, 'ORGANISATION'))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tree])

  const searching = query.trim().length > 0
  const rows = useMemo<OrgRow[]>(
    () => (searching ? searchRows(tree, query) : visibleRows(tree, expanded)),
    [tree, expanded, query, searching],
  )

  const applyLevel = (lvl: Level) => {
    setLevel(lvl)
    setExpanded(expandedForLevel(tree, lvl))
  }

  const toggle = (id: string) => {
    // A manual chevron toggle clears the active level (the tree no longer matches
    // a clean depth) — the handoff Interactions rule.
    setLevel(null)
    setExpanded((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })
  }

  const reload = useCallback(async () => {
    await fetchTree()
  }, [fetchTree])

  // ── action dispatch from a row's "Handling" cell ──
  const onOmdoeb = (row: OrgRow) => {
    setSelected(row.id)
    setRenameModal({ id: row.id, type: row.type, value: row.name })
    setDialogError(null)
  }

  const onTilfoej = (row: OrgRow) => {
    setSelected(row.id)
    // MAO → create an Organisation under it. (Organisations are leaves.)
    setCreateModal({ parentId: row.id, parentName: row.name, type: 'ORGANISATION', value: '' })
    setDialogError(null)
  }

  const onFlyt = (row: OrgRow) => {
    setSelected(row.id)
    setDialogError(null)
    // Organisation → move under another MAO. MAO hides Flyt.
    if (row.type === 'ORGANISATION') {
      setMoveModal({ id: row.id, name: row.name, parentId: row.parentId, to: '' })
    }
  }

  const onSlet = (row: OrgRow) => {
    setSelected(row.id)
    setDialogError(null)
    // MAO / Organisation: an EMPTY one (count 0) deletes; one WITH employees is
    // blocked. The aggregated count is authoritative, but the backend is the
    // gate — we open optimistically on the count, then the DELETE confirms.
    setDeleteModal({ row, branch: row.count > 0 ? 'blocked' : 'empty', employeeCount: row.count })
  }

  // ── create submit ──
  const submitCreate = async (e: FormEvent) => {
    e.preventDefault()
    if (!createModal) return
    const name = createModal.value.trim()
    if (!name) return
    setBusy(true)
    setDialogError(null)
    try {
      // MAO (no parent) or ORGANISATION (parentOrgId = the MAO). Name-only.
      await createOrganization({
        orgName: name,
        orgType: createModal.type,
        parentOrgId: createModal.parentId ?? null,
      })
      toast({ title: 'Oprettet', description: `${TYPE_LABEL[createModal.type]} oprettet`, variant: 'success' })
      setCreateModal(null)
      // Reveal the new child: ensure the parent is expanded.
      if (createModal.parentId) {
        setExpanded((s) => new Set(s).add(createModal.parentId!))
        setLevel(null)
      }
      await reload()
    } catch (err) {
      setDialogError(dupOrMessage(err))
    } finally {
      setBusy(false)
    }
  }

  // ── rename submit (MAO / Organisation, name-only) ──
  const submitRename = async (e: FormEvent) => {
    e.preventDefault()
    if (!renameModal) return
    const name = renameModal.value.trim()
    if (!name) return
    setBusy(true)
    setDialogError(null)
    try {
      await updateOrganization(renameModal.id, { orgName: name })
      toast({ title: 'Gemt', description: 'Navn opdateret', variant: 'success' })
      setRenameModal(null)
      await reload()
    } catch (err) {
      setDialogError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  // ── move submit ──
  const submitMove = async (e: FormEvent) => {
    e.preventDefault()
    if (!moveModal || !moveModal.to) return
    setBusy(true)
    setDialogError(null)
    const newParent = moveModal.to
    try {
      await moveOrganization(moveModal.id, newParent)
      toast({ title: 'Flyttet', description: 'Organisation flyttet', variant: 'success' })
      setMoveModal(null)
      // Reveal the moved org under its new parent (mirror the create-child reveal) — else a
      // move into a collapsed MAO hides the org. Manual reveal → clear the active level.
      setExpanded((s) => new Set(s).add(newParent))
      setLevel(null)
      await reload()
    } catch (err) {
      // Map 400 (shape) / 422 (semantic) to an inline dialog error.
      const e2 = err as OrgStructureError
      if (e2.status === 422) {
        setDialogError('Flytningen blev afvist: målet skal være et aktivt ministeransvarsområde.')
      } else if (e2.status === 400) {
        setDialogError('Ugyldig placering. Vælg et andet ministeransvarsområde.')
      } else {
        setDialogError(messageFor(err))
      }
    } finally {
      setBusy(false)
    }
  }

  // ── delete confirm ──
  const confirmDelete = async () => {
    if (!deleteModal) return
    const { row } = deleteModal
    setBusy(true)
    setDialogError(null)
    try {
      await deleteOrganization(row.id)
      toast({ title: 'Slettet', description: `${TYPE_LABEL[row.type]} slettet`, variant: 'success' })
      // Fallback selection after a delete: the parent (README:104 selectedB).
      setSelected(row.parentId)
      setDeleteModal(null)
      await reload()
    } catch (err) {
      const e2 = err as OrgStructureError
      // The server is the gate: a 422 flips the dialog to its BLOCKED branch with
      // the authoritative count (the optimistic count may have been stale).
      if (e2.status === 422) {
        setDeleteModal({ row, branch: 'blocked', employeeCount: e2.employeeCount ?? row.count })
      } else {
        setDialogError(messageFor(err))
      }
    } finally {
      setBusy(false)
    }
  }

  const closeDialogs = () => {
    setCreateModal(null)
    setRenameModal(null)
    setMoveModal(null)
    setDeleteModal(null)
    setDialogError(null)
  }

  return (
    <div className={styles.page}>
      {/* Title block */}
      <div className={styles.titleBlock}>
        <h2 className={styles.h2}>Organisation</h2>
        <p className={styles.sub}>{TITLE_SUB}</p>
      </div>

      {/* Toolbar */}
      <div className={styles.toolbar}>
        <div className={styles.toolbarLeft}>
          <input
            type="search"
            className={styles.search}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Søg organisation…"
            aria-label="Søg organisation"
          />
        </div>
        <div className={styles.toolbarRight}>
          <span className={styles.levelLabel} id="orgLevelLabel">
            Vis til niveau
          </span>
          <div className={styles.seg} role="group" aria-labelledby="orgLevelLabel">
            {LEVEL_OPTIONS.map((opt) => (
              <button
                key={opt.value}
                type="button"
                className={`${styles.segBtn} ${level === opt.value ? styles.segBtnActive : ''}`}
                onClick={() => applyLevel(opt.value)}
                aria-pressed={level === opt.value}
              >
                {opt.label}
              </button>
            ))}
          </div>
          <button
            type="button"
            className={styles.primaryBtn}
            onClick={() => {
              setCreateModal({ parentId: null, parentName: null, type: 'MAO', value: '' })
              setDialogError(null)
            }}
            data-testid="new-mao"
          >
            Nyt ministeransvarsområde
          </button>
        </div>
      </div>

      {error && <div className={styles.alert}>{error}</div>}

      {loading && (
        <div className={styles.spinner}>
          <Spinner size="lg" />
        </div>
      )}

      {!loading && !error && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.colName}>Navn</th>
              <th className={styles.colType}>Type</th>
              <th className={styles.colCount}>Medarb.</th>
              <th className={styles.colAction}>Handling</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const open = expanded.has(row.id)
              const indent = searching ? 14 : 12 + row.depth * 22
              return (
                <tr
                  key={`${row.type}-${row.id}`}
                  className={`${styles.row} ${selected === row.id ? styles.rowSelected : ''}`}
                  onClick={() => setSelected(row.id)}
                  data-testid={`org-row-${row.id}`}
                >
                  <td className={styles.colName}>
                    <span className={styles.nameCell} style={{ paddingLeft: indent }}>
                      {!searching && row.hasChildren ? (
                        <button
                          type="button"
                          className={styles.chevronBtn}
                          onClick={(e) => {
                            e.stopPropagation()
                            toggle(row.id)
                          }}
                          aria-label={open ? 'Skjul' : 'Vis'}
                          aria-expanded={open}
                          data-testid={`chevron-${row.id}`}
                        >
                          <Chevron open={open} />
                        </button>
                      ) : (
                        <span className={styles.chevronSpacer} aria-hidden="true" />
                      )}
                      <span
                        className={row.depth === 0 ? styles.nameStrong : styles.name}
                      >
                        {row.name}
                      </span>
                    </span>
                  </td>
                  <td className={styles.colType}>
                    <Badge variant={TYPE_BADGE_VARIANT[row.type]}>
                      {TYPE_LABEL[row.type]}
                    </Badge>
                  </td>
                  <td className={`${styles.colCount} ${styles.count}`}>{row.count}</td>
                  <td className={styles.colAction}>
                    <div className={styles.actions}>
                      <button
                        type="button"
                        className={styles.ghost}
                        onClick={(e) => {
                          e.stopPropagation()
                          onOmdoeb(row)
                        }}
                        data-testid={`action-omdoeb-${row.id}`}
                      >
                        Omdøb
                      </button>
                      {/* Tilføj on a MAO (creates an Organisation). */}
                      {row.type === 'MAO' && (
                        <button
                          type="button"
                          className={styles.ghost}
                          onClick={(e) => {
                            e.stopPropagation()
                            onTilfoej(row)
                          }}
                          data-testid={`action-tilfoej-${row.id}`}
                        >
                          Tilføj
                        </button>
                      )}
                      {/* Flyt on an Organisation (re-home under a MAO). MAO hides Flyt. */}
                      {row.type === 'ORGANISATION' && (
                        <button
                          type="button"
                          className={styles.ghost}
                          onClick={(e) => {
                            e.stopPropagation()
                            onFlyt(row)
                          }}
                          data-testid={`action-flyt-${row.id}`}
                        >
                          Flyt
                        </button>
                      )}
                      <button
                        type="button"
                        className={styles.ghostDanger}
                        onClick={(e) => {
                          e.stopPropagation()
                          onSlet(row)
                        }}
                        data-testid={`action-slet-${row.id}`}
                      >
                        Slet
                      </button>
                    </div>
                  </td>
                </tr>
              )
            })}
            {rows.length === 0 && (
              <tr>
                <td colSpan={4} className={styles.empty}>
                  {searching
                    ? 'Ingen organisationer matcher søgningen.'
                    : 'Ingen organisationer fundet.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      {/* ── Dialogs (one shared shell) ── */}
      {createModal && (
        <DialogShell title={createTitle(createModal.type)} onClose={closeDialogs}>
          <form onSubmit={submitCreate}>
            {createModal.parentName && (
              <p className={styles.dialogText}>
                Oprettes under <strong>{createModal.parentName}</strong>.
              </p>
            )}
            <label className={styles.dialogLabel} htmlFor="createName">
              Navn
            </label>
            <input
              id="createName"
              className={styles.dialogInput}
              autoFocus
              value={createModal.value}
              onChange={(e) =>
                setCreateModal((s) => (s ? { ...s, value: e.target.value } : s))
              }
              placeholder={createPlaceholder(createModal.type)}
              onKeyDown={(e) => {
                if (e.key === 'Escape') closeDialogs()
              }}
            />
            {dialogError && <div className={styles.dialogError}>{dialogError}</div>}
            <DialogFooter
              onCancel={closeDialogs}
              confirmLabel={createTitle(createModal.type)}
              confirmDisabled={busy || createModal.value.trim().length === 0}
            />
          </form>
        </DialogShell>
      )}

      {renameModal && (
        <DialogShell
          title={renameModal.type === 'MAO' ? 'Omdøb ministeransvarsområde' : 'Omdøb organisation'}
          onClose={closeDialogs}
        >
          <form onSubmit={submitRename}>
            <div className={styles.warnPanel}>
              {renameModal.type === 'MAO'
                ? 'Et nyt navn slår igennem på rapporter, budgetansvar og historik — også på tidligere perioder.'
                : 'Et nyt navn slår igennem på lønsystemet (SLS), rapporter og historik — også på tidligere perioder.'}
            </div>
            <label className={styles.dialogLabel} htmlFor="renameName">
              Nyt navn
            </label>
            <input
              id="renameName"
              className={styles.dialogInput}
              autoFocus
              value={renameModal.value}
              onChange={(e) =>
                setRenameModal((s) => (s ? { ...s, value: e.target.value } : s))
              }
              onKeyDown={(e) => {
                if (e.key === 'Escape') closeDialogs()
              }}
            />
            {dialogError && <div className={styles.dialogError}>{dialogError}</div>}
            <DialogFooter
              onCancel={closeDialogs}
              confirmLabel="Gem ændring"
              confirmDisabled={busy || renameModal.value.trim().length === 0}
            />
          </form>
        </DialogShell>
      )}

      {moveModal && (
        <DialogShell title="Flyt organisation" onClose={closeDialogs}>
          <form onSubmit={submitMove}>
            <p className={styles.dialogText}>
              Vælg en ny placering for <strong>{moveModal.name}</strong>. Medarbejdere
              følger med.
            </p>
            <label className={styles.dialogLabel} htmlFor="moveTarget">
              Ny placering
            </label>
            <select
              id="moveTarget"
              className={styles.dialogSelect}
              value={moveModal.to}
              onChange={(e) =>
                setMoveModal((s) => (s ? { ...s, to: e.target.value } : s))
              }
            >
              <option value="">Vælg ministeransvarsområde…</option>
              {moveTargets(tree, moveModal.parentId).map((t) => (
                <option key={t.orgId} value={t.orgId}>
                  {t.orgName}
                </option>
              ))}
            </select>
            {dialogError && <div className={styles.dialogError}>{dialogError}</div>}
            <DialogFooter
              onCancel={closeDialogs}
              confirmLabel="Flyt"
              confirmDisabled={busy || !moveModal.to}
            />
          </form>
        </DialogShell>
      )}

      {deleteModal && (
        <DeleteDialog
          state={deleteModal}
          busy={busy}
          error={dialogError}
          onClose={closeDialogs}
          onConfirm={confirmDelete}
        />
      )}
    </div>
  )
}

// ── Dialog shell (shared) ──
function DialogShell({
  title,
  onClose,
  children,
}: {
  title: string
  onClose: () => void
  children: React.ReactNode
}) {
  return (
    <div
      className={styles.scrim}
      onClick={onClose}
      role="presentation"
      data-testid="dialog-scrim"
    >
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

function DialogFooter({
  onCancel,
  confirmLabel,
  confirmDisabled,
  destructive,
}: {
  onCancel: () => void
  confirmLabel: string
  confirmDisabled?: boolean
  destructive?: boolean
}) {
  return (
    <div className={styles.dialogFooter}>
      <button type="button" className={styles.secondaryBtn} onClick={onCancel}>
        Annuller
      </button>
      <button
        type="submit"
        className={destructive ? styles.dangerBtn : styles.primaryBtn}
        disabled={confirmDisabled}
      >
        {confirmLabel}
      </button>
    </div>
  )
}

// ── Delete dialog (2 branches) ──
function DeleteDialog({
  state,
  busy,
  error,
  onClose,
  onConfirm,
}: {
  state: DeleteState
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}) {
  const { row, branch, employeeCount } = state

  if (branch === 'blocked') {
    const noun = row.type === 'MAO' ? 'ministeransvarsområdet' : 'organisationen'
    return (
      <DialogShell title="Kan ikke slette" onClose={onClose}>
        <div className={styles.warnPanel}>
          <strong>{row.name}</strong> indeholder {employeeCount ?? row.count} medarbejdere og
          kan ikke slettes. Alle medarbejdere skal være tilknyttet en organisation — flyt dem
          til en anden organisation, før du sletter {noun}.
        </div>
        <div className={styles.dialogFooter}>
          <button type="button" className={styles.primaryBtn} onClick={onClose}>
            Luk
          </button>
        </div>
      </DialogShell>
    )
  }

  // Empty Organisation / MAO delete — the red confirm shell.
  const title = row.type === 'MAO' ? 'Slet ministeransvarsområde?' : 'Slet organisation?'
  const confirmLabel = row.type === 'MAO' ? 'Slet ministeransvarsområde' : 'Slet organisation'

  return (
    <DialogShell title={title} onClose={onClose}>
      <div className={styles.dangerPanel}>
        Du er ved at slette <strong>{row.name}</strong>. Handlingen kan ikke fortrydes.
      </div>
      {error && <div className={styles.dialogError}>{error}</div>}
      <div className={styles.dialogFooter}>
        <button type="button" className={styles.secondaryBtn} onClick={onClose}>
          Annuller
        </button>
        <button
          type="button"
          className={styles.dangerBtn}
          onClick={onConfirm}
          disabled={busy}
          data-testid="confirm-delete"
        >
          {busy ? 'Sletter…' : confirmLabel}
        </button>
      </div>
    </DialogShell>
  )
}

// ── small helpers ──
function createTitle(type: NodeType): string {
  if (type === 'MAO') return 'Nyt ministeransvarsområde'
  return 'Ny organisation'
}

function createPlaceholder(type: NodeType): string {
  if (type === 'MAO') return 'F.eks. Finansministeriet'
  return 'F.eks. Økonomistyrelsen'
}

function messageFor(err: unknown): string {
  if (err instanceof Error) return err.message
  return String(err)
}

/** A 409 (active-name dup) → a friendly inline message; else the raw message. */
function dupOrMessage(err: unknown): string {
  const e = err as Error & { status?: number }
  if (e.status === 409) {
    return 'Der findes allerede en aktiv organisation med dette navn.'
  }
  return messageFor(err)
}
