// S97 / TASK-9705 + S100 / TASK-10004 — the org-scoped Enheder management panel
// for the medarbejder-administration page. Lists the ACTIVE enheder of ONE
// selected Organisation (the selector options = the actor's accessible
// Organisations, reusing the page's tree-root org list — MULTI-org) and offers
// create / rename / delete with the If-Match version flow + 409 dup handling.
//
// S100: the enheder are HIERARCHICAL (`parentEnhedId` + a derived `level`). The
// flat list rows are indented by level; each row offers Tilføj (create a CHILD
// under it) + Flyt (re-parent within the Organisation, excluding self +
// descendants, or → root). Proportionate to the showcase Organisation page.
//
// LocalHR+ (the page is already LocalHR-gated). An Enhed is PURE display
// metadata with ZERO authority/scope/approval meaning (ADR-036) — this panel is
// pure CRUD over the `enheder` table; nothing here grants access.
import { useCallback, useEffect, useState } from 'react'
import { Card, Spinner, Input } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import { useEnheder, type Enhed, type EnhedMutationError } from '../../../hooks/useEnheder'
import type { Organization } from '../../../hooks/useAdmin'
import styles from './EnhederPanel.module.css'

/** The set of enhedIds that are `enhedId` ITSELF or any descendant (computed
    from the FLAT list's `parentEnhedId` edges) — an invalid move target. */
function selfAndDescendantIds(enheder: Enhed[], enhedId: string): Set<string> {
  const childrenOf = new Map<string, string[]>()
  for (const e of enheder) {
    const p = e.parentEnhedId ?? '__ROOT__'
    if (!childrenOf.has(p)) childrenOf.set(p, [])
    childrenOf.get(p)!.push(e.enhedId)
  }
  const out = new Set<string>()
  const stack = [enhedId]
  while (stack.length > 0) {
    const id = stack.pop()!
    if (out.has(id)) continue
    out.add(id)
    for (const c of childrenOf.get(id) ?? []) stack.push(c)
  }
  return out
}

/** Order the flat rows depth-first (parents immediately before their children)
    so the level-indent reads as a tree. Falls back to input order for any row
    whose parent is missing (defensive — a filtered/dead parent). */
function orderByTree(enheder: Enhed[]): Enhed[] {
  const childrenOf = new Map<string, Enhed[]>()
  for (const e of enheder) {
    const p = e.parentEnhedId ?? '__ROOT__'
    if (!childrenOf.has(p)) childrenOf.set(p, [])
    childrenOf.get(p)!.push(e)
  }
  const seen = new Set<string>()
  const out: Enhed[] = []
  const walk = (parentKey: string) => {
    for (const e of childrenOf.get(parentKey) ?? []) {
      if (seen.has(e.enhedId)) continue
      seen.add(e.enhedId)
      out.push(e)
      walk(e.enhedId)
    }
  }
  walk('__ROOT__')
  // Append any orphans (parent not in the list) preserving order.
  for (const e of enheder) if (!seen.has(e.enhedId)) out.push(e)
  return out
}

const ROOT_OPTION = '__ROOT__'

interface EnhederPanelProps {
  /** The actor's accessible Organisations (the page's tree-root org list). The
      panel's own selector picks ONE of these to manage. */
  organisations: Organization[]
  /** The org currently selected on the page — used as the panel's initial org so
      the two stay in sync on first render (the user may then switch the panel's
      own selector independently). */
  selectedOrgId: string
}

/** Map an enhed mutation error status → an honest Danish message. */
function enhedErrorMessage(err: unknown): string {
  const e = err as EnhedMutationError
  if (e?.status === 409) return 'Der findes allerede en aktiv enhed med dette navn i organisationen.'
  if (e?.status === 412) return 'Enheden er ændret af en anden administrator. Genindlæs og prøv igen.'
  if (e?.status === 422) return 'Ugyldig flytning: målet må ikke være enheden selv eller en af dens underenheder.'
  if (e?.status === 400) return 'Enheder kan kun oprettes under en organisation (ikke et ministerområde).'
  if (e?.status === 403) return 'Du har ikke adgang til denne organisations enheder.'
  return err instanceof Error ? err.message : String(err)
}

export function EnhederPanel({ organisations, selectedOrgId }: EnhederPanelProps) {
  const { fetchEnheder, createEnhed, renameEnhed, moveEnhed, deleteEnhed } = useEnheder()
  const { toast } = useToast()

  // Only ORGANISATION-typed orgs can hold enheder; the page's tree-root list
  // includes MAOs (which hold none) — keep them selectable so the panel can show
  // the "MAO holds no enheder" honest empty state rather than hiding the org.
  const [panelOrgId, setPanelOrgId] = useState(selectedOrgId)
  const [enheder, setEnheder] = useState<Enhed[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [newName, setNewName] = useState('')
  const [busy, setBusy] = useState(false)

  // Per-row rename draft (keyed by enhedId; null = not renaming that row).
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const [renameDraft, setRenameDraft] = useState('')

  // S100 — per-row child-create draft (keyed by the PARENT enhedId; null = none).
  const [childParentId, setChildParentId] = useState<string | null>(null)
  const [childName, setChildName] = useState('')

  // S100 — per-row move draft: which enhed is being moved + the chosen target
  // ('' = unselected, ROOT_OPTION = make root, else the parent enhedId).
  const [movingId, setMovingId] = useState<string | null>(null)
  const [moveTarget, setMoveTarget] = useState('')

  const selectedOrg = organisations.find((o) => o.orgId === panelOrgId) ?? null
  const isMao = selectedOrg?.orgType === 'MAO'

  // Re-sync the panel's org when the page's selection changes (e.g. the styrelse
  // switch). The user may still override it afterwards.
  useEffect(() => {
    setPanelOrgId(selectedOrgId)
  }, [selectedOrgId])

  const load = useCallback(
    async (orgId: string) => {
      if (!orgId) {
        setEnheder([])
        return
      }
      setLoading(true)
      setError(null)
      const result = await fetchEnheder(orgId)
      if (result.ok) {
        setEnheder(result.data)
      } else {
        setEnheder([])
        // A MAO 400s the list (an Enhed belongs to an ORGANISATION) — surface the
        // honest empty state, not a scary error.
        setError(result.status === 400 ? null : result.error)
      }
      setLoading(false)
    },
    [fetchEnheder],
  )

  useEffect(() => {
    void load(panelOrgId)
  }, [panelOrgId, load])

  const handleCreate = async () => {
    const name = newName.trim()
    if (!name || busy) return
    setBusy(true)
    try {
      // A root enhed (no parent) — the top create row.
      await createEnhed(panelOrgId, name, null)
      toast({ title: 'Oprettet', description: `Enhed “${name}” oprettet`, variant: 'success' })
      setNewName('')
      await load(panelOrgId)
    } catch (err) {
      toast({ title: 'Fejl', description: enhedErrorMessage(err), variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  const startChild = (parentId: string) => {
    setChildParentId(parentId)
    setChildName('')
    setMovingId(null)
  }
  const cancelChild = () => {
    setChildParentId(null)
    setChildName('')
  }

  // S100 — create a CHILD enhed under `childParentId`.
  const handleCreateChild = async (parentId: string) => {
    const name = childName.trim()
    if (!name || busy) return
    setBusy(true)
    try {
      await createEnhed(panelOrgId, name, parentId)
      toast({ title: 'Oprettet', description: `Underenhed “${name}” oprettet`, variant: 'success' })
      cancelChild()
      await load(panelOrgId)
    } catch (err) {
      toast({ title: 'Fejl', description: enhedErrorMessage(err), variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  const startMove = (enhed: Enhed) => {
    setMovingId(enhed.enhedId)
    setMoveTarget('')
    setChildParentId(null)
  }
  const cancelMove = () => {
    setMovingId(null)
    setMoveTarget('')
  }

  // S100 — re-parent `enhed` to the chosen target (ROOT_OPTION → null).
  const handleMove = async (enhed: Enhed) => {
    if (busy || !moveTarget) return
    const newParent = moveTarget === ROOT_OPTION ? null : moveTarget
    setBusy(true)
    try {
      await moveEnhed(enhed.enhedId, newParent, enhed.etag)
      toast({ title: 'Flyttet', description: `Enhed “${enhed.name}” flyttet`, variant: 'success' })
      cancelMove()
      await load(panelOrgId)
    } catch (err) {
      toast({ title: 'Fejl', description: enhedErrorMessage(err), variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  const startRename = (enhed: Enhed) => {
    setRenamingId(enhed.enhedId)
    setRenameDraft(enhed.name)
  }

  const cancelRename = () => {
    setRenamingId(null)
    setRenameDraft('')
  }

  const handleRename = async (enhed: Enhed) => {
    const name = renameDraft.trim()
    if (!name || busy) return
    if (name === enhed.name) {
      cancelRename()
      return
    }
    setBusy(true)
    try {
      await renameEnhed(enhed.enhedId, name, enhed.etag)
      toast({ title: 'Gemt', description: 'Enhed omdøbt', variant: 'success' })
      cancelRename()
      await load(panelOrgId)
    } catch (err) {
      toast({ title: 'Fejl', description: enhedErrorMessage(err), variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  const handleDelete = async (enhed: Enhed) => {
    if (busy) return
    // eslint-disable-next-line no-alert
    if (!window.confirm(`Slet enheden “${enhed.name}”? Tags på medarbejdere fjernes fra visningen. Eventuelle underenheder flyttes op til den overordnede enhed.`)) {
      return
    }
    setBusy(true)
    try {
      await deleteEnhed(enhed.enhedId, enhed.etag)
      toast({ title: 'Slettet', description: `Enhed “${enhed.name}” slettet`, variant: 'success' })
      await load(panelOrgId)
    } catch (err) {
      toast({ title: 'Fejl', description: enhedErrorMessage(err), variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card
      className={styles.panel}
      header={<span className={styles.panelTitle}>Enheder</span>}
    >
      <div className={styles.controls}>
        <div className={styles.orgpick}>
          <label className={styles.rolesK} htmlFor="enhederOrg">
            Organisation
          </label>
          <select
            id="enhederOrg"
            className={styles.select}
            value={panelOrgId}
            onChange={(e) => setPanelOrgId(e.target.value)}
            data-testid="enheder-org-select"
          >
            {organisations.map((org) => (
              <option key={org.orgId} value={org.orgId}>
                {org.orgName}
              </option>
            ))}
          </select>
        </div>
      </div>

      {isMao && (
        <p className={styles.muted} data-testid="enheder-mao-note">
          Et ministerområde har ingen enheder — vælg en organisation.
        </p>
      )}

      {error && <div className={styles.alert} role="alert">{error}</div>}

      {!isMao && (
        <>
          <div className={styles.createRow}>
            <Input
              id="enhederNewName"
              type="text"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  void handleCreate()
                }
              }}
              placeholder="Ny enhed, f.eks. Netværk"
              aria-label="Ny enheds navn"
              disabled={busy}
              data-testid="enheder-new-name"
            />
            <button
              type="button"
              className={styles.addBtn}
              onClick={() => void handleCreate()}
              disabled={busy || newName.trim() === ''}
              data-testid="enheder-create"
            >
              Tilføj enhed
            </button>
          </div>

          {loading ? (
            <div className={styles.spinner}>
              <Spinner size="md" />
            </div>
          ) : enheder.length === 0 ? (
            <p className={styles.muted} data-testid="enheder-empty">
              Ingen enheder oprettet for denne organisation endnu.
            </p>
          ) : (
            <ul className={styles.list} data-testid="enheder-list">
              {orderByTree(enheder).map((enhed) => {
                // Indent by depth (root level 1 → no indent). Each level adds ~18px.
                const indent = Math.max(0, (enhed.level ?? 1) - 1) * 18
                const excluded = selfAndDescendantIds(enheder, enhed.enhedId)
                const moveOptions = enheder.filter(
                  (e) => !excluded.has(e.enhedId) && e.enhedId !== (enhed.parentEnhedId ?? null),
                )
                return (
                  <li key={enhed.enhedId} className={styles.row} style={{ flexWrap: 'wrap' }}>
                    {renamingId === enhed.enhedId ? (
                      <>
                        <Input
                          id={`enheder-rename-${enhed.enhedId}`}
                          type="text"
                          value={renameDraft}
                          onChange={(e) => setRenameDraft(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') {
                              e.preventDefault()
                              void handleRename(enhed)
                            } else if (e.key === 'Escape') {
                              cancelRename()
                            }
                          }}
                          aria-label={`Omdøb ${enhed.name}`}
                          disabled={busy}
                          data-testid={`enheder-rename-input-${enhed.enhedId}`}
                        />
                        <div className={styles.rowActions}>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={() => void handleRename(enhed)}
                            disabled={busy}
                            data-testid={`enheder-rename-save-${enhed.enhedId}`}
                          >
                            Gem
                          </button>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={cancelRename}
                            disabled={busy}
                          >
                            Annullér
                          </button>
                        </div>
                      </>
                    ) : movingId === enhed.enhedId ? (
                      <>
                        <span
                          className={styles.rowName}
                          style={{ paddingLeft: indent }}
                          data-testid={`enheder-name-${enhed.enhedId}`}
                        >
                          {enhed.name}
                        </span>
                        <div className={styles.rowActions}>
                          <select
                            className={styles.select}
                            value={moveTarget}
                            onChange={(e) => setMoveTarget(e.target.value)}
                            aria-label={`Ny placering for ${enhed.name}`}
                            disabled={busy}
                            data-testid={`enheder-move-select-${enhed.enhedId}`}
                          >
                            <option value="">Vælg ny placering…</option>
                            <option value={ROOT_OPTION}>→ Rod (ingen overordnet)</option>
                            {moveOptions.map((t) => (
                              <option key={t.enhedId} value={t.enhedId}>
                                {`${'  '.repeat(Math.max(0, (t.level ?? 1) - 1))}${t.name}`}
                              </option>
                            ))}
                          </select>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={() => void handleMove(enhed)}
                            disabled={busy || !moveTarget}
                            data-testid={`enheder-move-save-${enhed.enhedId}`}
                          >
                            Flyt
                          </button>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={cancelMove}
                            disabled={busy}
                          >
                            Annullér
                          </button>
                        </div>
                      </>
                    ) : (
                      <>
                        <span
                          className={styles.rowName}
                          style={{ paddingLeft: indent }}
                          data-testid={`enheder-name-${enhed.enhedId}`}
                        >
                          {enhed.name}
                        </span>
                        <div className={styles.rowActions}>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={() => startChild(enhed.enhedId)}
                            disabled={busy}
                            data-testid={`enheder-add-child-${enhed.enhedId}`}
                          >
                            Tilføj
                          </button>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={() => startMove(enhed)}
                            disabled={busy}
                            data-testid={`enheder-move-${enhed.enhedId}`}
                          >
                            Flyt
                          </button>
                          <button
                            type="button"
                            className={styles.link}
                            onClick={() => startRename(enhed)}
                            disabled={busy}
                            data-testid={`enheder-rename-${enhed.enhedId}`}
                          >
                            Omdøb
                          </button>
                          <button
                            type="button"
                            className={`${styles.link} ${styles.danger}`}
                            onClick={() => void handleDelete(enhed)}
                            disabled={busy}
                            data-testid={`enheder-delete-${enhed.enhedId}`}
                          >
                            Slet
                          </button>
                        </div>
                      </>
                    )}
                    {childParentId === enhed.enhedId && (
                      <div className={styles.createRow} style={{ width: '100%' }}>
                        <Input
                          id={`enheder-child-name-${enhed.enhedId}`}
                          type="text"
                          value={childName}
                          onChange={(e) => setChildName(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') {
                              e.preventDefault()
                              void handleCreateChild(enhed.enhedId)
                            } else if (e.key === 'Escape') {
                              cancelChild()
                            }
                          }}
                          placeholder={`Underenhed under “${enhed.name}”`}
                          aria-label={`Ny underenhed under ${enhed.name}`}
                          disabled={busy}
                          data-testid={`enheder-child-name-${enhed.enhedId}`}
                        />
                        <button
                          type="button"
                          className={styles.addBtn}
                          onClick={() => void handleCreateChild(enhed.enhedId)}
                          disabled={busy || childName.trim() === ''}
                          data-testid={`enheder-child-create-${enhed.enhedId}`}
                        >
                          Tilføj underenhed
                        </button>
                        <button
                          type="button"
                          className={styles.link}
                          onClick={cancelChild}
                          disabled={busy}
                        >
                          Annullér
                        </button>
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </>
      )}
    </Card>
  )
}
