// S76b / TASK-7603 — the searchable person picker (modal) for the approver +
// vikar pickers. Ported from the prototype `PersonPickerDialog`
// (ledelseslinjer-data.jsx) but searching SERVER-side (`GET /api/admin/users/search`)
// so it scales to 2000+ employees. The server scope-filters to the caller's RBAC
// org-scope AND excludes self + descendants when `excludeEmployeeId` is supplied
// (the cycle-prevention mirror); a client-side `forbidden` set is the additional
// defence-in-depth mirror (R4: forbidden = self + descendantsOf), filtering any
// stray hit before render.
import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Dialog } from '../../../components/ui'
import { useReportingLines, type PersonSearchHit } from '../../../hooks/useReportingLines'
import styles from './PersonPickerDialog.module.css'

interface PersonPickerDialogProps {
  open: boolean
  title: string
  subtitle?: string
  /** The currently-selected person id (shown as "Nuværende"). */
  currentId?: string | null
  /**
   * Client-side forbidden set (self + descendants) — the cycle-prevention mirror.
   * The server ALSO excludes via `excludeEmployeeId`; this is defence-in-depth so
   * a forbidden person never renders even if the caller cannot pass the exclude id.
   */
  forbidden?: Set<string>
  /** Threaded to the server search so self + descendants are excluded at source. */
  excludeEmployeeId?: string
  /** `displayName` is the picked person's name (for an immediate label). */
  onPick: (userId: string, displayName: string) => void
  onClose: () => void
}

export function PersonPickerDialog({
  open,
  title,
  subtitle,
  currentId,
  forbidden,
  excludeEmployeeId,
  onPick,
  onClose,
}: PersonPickerDialogProps) {
  const { searchPeople } = useReportingLines()
  const [q, setQ] = useState('')
  const [items, setItems] = useState<PersonSearchHit[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)

  // Reset on (re)open.
  useEffect(() => {
    if (open) {
      setQ('')
      setItems([])
      setTotal(0)
      setError(null)
    }
  }, [open])

  // Debounced server search (250ms). Re-runs on q change while open.
  useEffect(() => {
    if (!open) return
    let cancelled = false
    setLoading(true)
    setError(null)
    const handle = setTimeout(async () => {
      const result = await searchPeople({
        q: q.trim() || undefined,
        excludeEmployeeId,
        limit: 60,
      })
      if (cancelled) return
      if (result.ok) {
        setItems(result.data.items)
        setTotal(result.data.total)
      } else {
        setError('Kunne ikke søge efter personer.')
        setItems([])
        setTotal(0)
      }
      setLoading(false)
    }, 250)
    return () => {
      cancelled = true
      clearTimeout(handle)
    }
  }, [open, q, excludeEmployeeId, searchPeople])

  // Focus the search field when opened.
  useEffect(() => {
    if (open) {
      // Radix portals asynchronously; defer the focus a tick.
      const id = setTimeout(() => inputRef.current?.focus(), 0)
      return () => clearTimeout(id)
    }
  }, [open])

  const block = forbidden ?? new Set<string>()
  const shown = items.filter((p) => !block.has(p.userId))

  const handlePick = useCallback(
    (userId: string, displayName: string) => {
      onPick(userId, displayName)
    },
    [onPick],
  )

  // Hifi (README:258): Enter in the search field picks the FIRST visible result
  // (after the client-side forbidden filter), mirroring the prototype's
  // `pickFirst`. Guarded on a non-empty result list so an empty search is a no-op.
  const handleSearchKeyDown = useCallback(
    (e: KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter' && shown.length > 0) {
        e.preventDefault()
        handlePick(shown[0].userId, shown[0].displayName)
      }
    },
    [shown, handlePick],
  )

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose()
      }}
      title={title}
      description={subtitle ?? 'Søg og vælg en person.'}
      contentClassName={styles.pickerContent}
    >
      <div className={styles.search}>
        <input
          ref={inputRef}
          className={styles.searchInput}
          type="search"
          placeholder="Søg på navn eller enhed…"
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onKeyDown={handleSearchKeyDown}
          data-testid="picker-search"
        />
      </div>

      {error && (
        <div className={styles.error} role="alert" data-testid="picker-error">
          {error}
        </div>
      )}

      <div className={styles.list} role="listbox" aria-label={title}>
        {loading && shown.length === 0 && (
          <div className={styles.empty} data-testid="picker-loading">
            Søger…
          </div>
        )}
        {!loading && shown.length === 0 && !error && (
          <div className={styles.empty} data-testid="picker-empty">
            {q.trim() ? `Ingen match på "${q.trim()}".` : 'Ingen personer.'}
          </div>
        )}
        {shown.map((p) => (
          <button
            type="button"
            key={p.userId}
            className={styles.row}
            onClick={() => handlePick(p.userId, p.displayName)}
            data-testid={`picker-row-${p.userId}`}
          >
            <span className={styles.rowText}>
              <strong>{p.displayName}</strong>
              <span className={styles.rowSub}>
                {p.primaryOrgName || p.userId}
              </span>
            </span>
            {p.userId === currentId && <span className={styles.current}>Nuværende</span>}
          </button>
        ))}
      </div>

      <div className={styles.foot}>
        {total} {total === 1 ? 'resultat' : 'resultater'}
        {total > shown.length ? ' · søg for at indsnævre' : ''}
      </div>
    </Dialog>
  )
}
