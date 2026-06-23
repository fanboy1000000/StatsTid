// S97 / TASK-9705 — the org-scoped Enheder management panel for the
// medarbejder-administration page. Lists the ACTIVE enheder of ONE selected
// Organisation (the selector options = the actor's accessible Organisations,
// reusing the page's tree-root org list — MULTI-org, NOT single-org) and offers
// create / rename / delete with the If-Match version flow + 409 dup handling.
//
// LocalHR+ (the page is already LocalHR-gated). An Enhed is FLAT per-Organisation
// metadata with ZERO authority/scope/approval meaning (ADR-035) — this panel is
// pure CRUD over the `enheder` table; nothing here grants access.
import { useCallback, useEffect, useState } from 'react'
import { Card, Spinner, Input } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import { useEnheder, type Enhed, type EnhedMutationError } from '../../../hooks/useEnheder'
import type { Organization } from '../../../hooks/useAdmin'
import styles from './EnhederPanel.module.css'

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
  if (e?.status === 400) return 'Enheder kan kun oprettes under en organisation (ikke et ministerområde).'
  if (e?.status === 403) return 'Du har ikke adgang til denne organisations enheder.'
  return err instanceof Error ? err.message : String(err)
}

export function EnhederPanel({ organisations, selectedOrgId }: EnhederPanelProps) {
  const { fetchEnheder, createEnhed, renameEnhed, deleteEnhed } = useEnheder()
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
      await createEnhed(panelOrgId, name)
      toast({ title: 'Oprettet', description: `Enhed “${name}” oprettet`, variant: 'success' })
      setNewName('')
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
    if (!window.confirm(`Slet enheden “${enhed.name}”? Tags på medarbejdere fjernes fra visningen.`)) {
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
              {enheder.map((enhed) => (
                <li key={enhed.enhedId} className={styles.row}>
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
                  ) : (
                    <>
                      <span className={styles.rowName} data-testid={`enheder-name-${enhed.enhedId}`}>
                        {enhed.name}
                      </span>
                      <div className={styles.rowActions}>
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
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </Card>
  )
}
