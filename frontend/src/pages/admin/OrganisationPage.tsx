import { useState, useMemo, useCallback, useEffect, type FormEvent } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import { useOrganizationTree } from '../../hooks/useOrganizationTree'
import {
  useOrganizationStructure,
  type OrgStructureError,
} from '../../hooks/useOrganizationStructure'
import { useEnheder, type EnhedMutationError } from '../../hooks/useEnheder'
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
// whole hierarchy (MAO → Organisation → Enhed leaves) with a level control,
// search, the rolled-up "Medarb." count, and guarded create / rename / move /
// delete flows on the FLAT S97 Enhed model. GlobalAdmin-gated (route + nav); the
// backend re-checks every mutation. No leader / overenskomst column (handoff rule).

const TITLE_SUB =
  'Forvalt organisationshierarkiet. Et ministeransvarsområde samler organisationer; ' +
  'organisationer er medarbejdernes obligatoriske tilknytning; enheder er fleksible ' +
  'undergrupper, der kan oprettes, omdøbes, flyttes og slettes.'

const LEVEL_OPTIONS: { value: Level; label: string }[] = [
  { value: 'MAO', label: 'Ministeransvarsområde' },
  { value: 'ORGANISATION', label: 'Organisation' },
  { value: 'ENHED', label: 'Enhed' },
]

const TYPE_BADGE_VARIANT: Record<NodeType, 'info' | 'success' | 'default'> = {
  MAO: 'info',
  ORGANISATION: 'success',
  ENHED: 'default',
}

// ── Dialog state shapes ──
type CreateState = { parentId: string | null; parentName: string | null; type: NodeType; value: string }
type RenameState = { id: string; type: 'MAO' | 'ORGANISATION'; value: string }
type MoveState = { id: string; name: string; parentId: string | null; to: string }
type DeleteState = {
  row: OrgRow
  // resolved branch + data
  branch: 'blocked' | 'enhed' | 'empty' | 'loading'
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
  const { fetchEnheder, createEnhed, renameEnhed, deleteEnhed } = useEnheder()
  const { toast } = useToast()

  // ── view state ──
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [level, setLevel] = useState<Level | null>('ORGANISATION')
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)

  // ── inline Enhed rename ──
  const [editEnhed, setEditEnhed] = useState<{ id: string; orgId: string; value: string } | null>(null)

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

  // ── inline Enhed rename: resolve the version, then PUT ──
  const commitEnhedRename = async () => {
    if (!editEnhed) return
    const name = editEnhed.value.trim()
    if (!name) {
      setEditEnhed(null) // empty → keep old (handoff)
      return
    }
    try {
      const list = await fetchEnheder(editEnhed.orgId)
      if (!list.ok) throw new Error(list.error)
      const found = list.data.find((e) => e.enhedId === editEnhed.id)
      if (!found) throw new Error('Enheden findes ikke længere.')
      if (found.name === name) {
        setEditEnhed(null)
        return
      }
      await renameEnhed(editEnhed.id, name, found.etag)
      setEditEnhed(null)
      toast({ title: 'Gemt', description: 'Enhed omdøbt', variant: 'success' })
      await reload()
    } catch (err) {
      toast({
        title: 'Fejl',
        description: messageFor(err),
        variant: 'error',
      })
      setEditEnhed(null)
    }
  }

  // ── action dispatch from a row's "Handling" cell ──
  const onOmdoeb = (row: OrgRow) => {
    setSelected(row.id)
    if (row.type === 'ENHED') {
      setEditEnhed({ id: row.id, orgId: row.organisationId!, value: row.name })
    } else {
      setRenameModal({ id: row.id, type: row.type, value: row.name })
      setDialogError(null)
    }
  }

  const onTilfoej = (row: OrgRow) => {
    setSelected(row.id)
    // MAO → create Organisation; Organisation → create Enhed. Enhed has NO Tilføj.
    if (row.type === 'MAO') {
      setCreateModal({ parentId: row.id, parentName: row.name, type: 'ORGANISATION', value: '' })
    } else if (row.type === 'ORGANISATION') {
      setCreateModal({ parentId: row.id, parentName: row.name, type: 'ENHED', value: '' })
    }
    setDialogError(null)
  }

  const onFlyt = (row: OrgRow) => {
    setSelected(row.id)
    // Organisation only (MAO + Enhed hide Flyt).
    if (row.type !== 'ORGANISATION') return
    setMoveModal({ id: row.id, name: row.name, parentId: row.parentId, to: '' })
    setDialogError(null)
  }

  const onSlet = async (row: OrgRow) => {
    setSelected(row.id)
    setDialogError(null)
    if (row.type === 'ENHED') {
      // The S97 flat soft-delete = untag; no count probe, no re-parent promise.
      setDeleteModal({ row, branch: 'enhed' })
      return
    }
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
      if (createModal.type === 'ENHED') {
        await createEnhed(createModal.parentId!, name)
      } else {
        // MAO (no parent) or ORGANISATION (parentOrgId = the MAO). Name-only.
        await createOrganization({
          orgName: name,
          orgType: createModal.type,
          parentOrgId: createModal.parentId ?? null,
        })
      }
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
    try {
      await moveOrganization(moveModal.id, moveModal.to)
      toast({ title: 'Flyttet', description: 'Organisation flyttet', variant: 'success' })
      setMoveModal(null)
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
      if (row.type === 'ENHED') {
        // Resolve the version (the tree's enhed leaf carries none), then DELETE.
        const list = await fetchEnheder(row.organisationId!)
        if (!list.ok) throw new Error(list.error)
        const found = list.data.find((en) => en.enhedId === row.id)
        if (!found) throw new Error('Enheden findes ikke længere.')
        await deleteEnhed(row.id, found.etag)
      } else {
        await deleteOrganization(row.id)
      }
      toast({ title: 'Slettet', description: `${TYPE_LABEL[row.type]} slettet`, variant: 'success' })
      // Fallback selection after a delete: the parent (README:104 selectedB).
      setSelected(row.parentId)
      setDeleteModal(null)
      await reload()
    } catch (err) {
      const e2 = err as OrgStructureError
      // The server is the gate: a 422 flips the dialog to its BLOCKED branch with
      // the authoritative count (the optimistic count may have been stale).
      if (row.type !== 'ENHED' && e2.status === 422) {
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
            placeholder="Søg enhed…"
            aria-label="Søg enhed"
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
              <th className={styles.colName}>Enhed</th>
              <th className={styles.colType}>Type</th>
              <th className={styles.colCount}>Medarb.</th>
              <th className={styles.colAction}>Handling</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const open = expanded.has(row.id)
              const indent = searching ? 14 : 12 + row.depth * 22
              const isEditing = editEnhed?.id === row.id
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
                      {isEditing ? (
                        <input
                          className={styles.inlineInput}
                          autoFocus
                          value={editEnhed!.value}
                          onChange={(e) =>
                            setEditEnhed((s) => (s ? { ...s, value: e.target.value } : s))
                          }
                          onClick={(e) => e.stopPropagation()}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') void commitEnhedRename()
                            if (e.key === 'Escape') setEditEnhed(null)
                          }}
                          onBlur={() => void commitEnhedRename()}
                          aria-label="Nyt enhedsnavn"
                          data-testid={`enhed-edit-${row.id}`}
                        />
                      ) : (
                        <span
                          className={row.depth === 0 ? styles.nameStrong : styles.name}
                        >
                          {row.name}
                        </span>
                      )}
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
                      {row.type !== 'ENHED' && (
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
                          void onSlet(row)
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
                    ? 'Ingen enheder matcher søgningen.'
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
              Vælg en ny placering for <strong>{moveModal.name}</strong>. Medarbejdere og
              enheder følger med.
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

// ── Delete dialog (3 branches) ──
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
          til en anden enhed, før du sletter {noun}.
        </div>
        <div className={styles.dialogFooter}>
          <button type="button" className={styles.primaryBtn} onClick={onClose}>
            Luk
          </button>
        </div>
      </DialogShell>
    )
  }

  // Enhed delete (untag) OR empty Organisation/MAO delete — same red shell.
  const title =
    row.type === 'ENHED'
      ? 'Slet enhed?'
      : row.type === 'MAO'
        ? 'Slet ministeransvarsområde?'
        : 'Slet organisation?'
  const confirmLabel =
    row.type === 'ENHED'
      ? 'Slet enhed'
      : row.type === 'MAO'
        ? 'Slet ministeransvarsområde'
        : 'Slet organisation'

  return (
    <DialogShell title={title} onClose={onClose}>
      <div className={styles.dangerPanel}>
        Du er ved at slette <strong>{row.name}</strong>. Handlingen kan ikke fortrydes.
      </div>
      {row.type === 'ENHED' && (
        // Flat model: members keep their organisation home (no re-parent promise,
        // NO "Underenheder, der slettes" line — the S91 dead-copy guard).
        <p className={styles.dialogText}>
          Medarbejdere beholder deres organisation; kun enhedsmærket fjernes.
        </p>
      )}
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
  if (type === 'ORGANISATION') return 'Ny organisation'
  return 'Ny enhed'
}

function createPlaceholder(type: NodeType): string {
  if (type === 'MAO') return 'F.eks. Finansministeriet'
  if (type === 'ORGANISATION') return 'F.eks. Økonomistyrelsen'
  return 'F.eks. Team Drift'
}

function messageFor(err: unknown): string {
  if (err instanceof Error) return err.message
  return String(err)
}

/** A 409 (active-name dup) → a friendly inline message; else the raw message. */
function dupOrMessage(err: unknown): string {
  const e = err as EnhedMutationError | OrgStructureError | Error
  if ('status' in e && e.status === 409) {
    return 'Der findes allerede en aktiv enhed/organisation med dette navn.'
  }
  return messageFor(err)
}
