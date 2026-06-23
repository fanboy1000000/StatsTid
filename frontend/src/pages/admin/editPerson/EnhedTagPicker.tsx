// S97 / TASK-9705 — the multi-select enhed tag picker for the EditPersonDrawer.
// Replaces the free-text `enhedLabel` field: a checkbox list of the PERSON's
// Organisation's ACTIVE enheder (fetched via GET /api/admin/enheder?organisationId
// =person.primaryOrgId). The selected `enhed_id`s are reported up to the drawer
// state; on save the drawer calls PUT /api/admin/users/{userId}/enheder (the
// single-save-path adds it as one step). An Enhed grants NO authority (ADR-035) —
// this is display metadata only.
//
// The person's CURRENT tag selection is seeded by NAME-matching the supplied
// `currentTagNames` (the roster/profile `enhed_tags` join, comma-joined names)
// against the fetched active enheder — there is no GET serving a user's current
// tag-id set, so the names are the available signal (a fallback org-name label
// won't match any enhed name, so it correctly seeds an empty selection). Once the
// user edits the selection it is authoritative.
import { useEffect, useRef, useState } from 'react'
import { Checkbox, Spinner } from '../../../components/ui'
import { useEnheder, type Enhed } from '../../../hooks/useEnheder'
import styles from '../EditPersonDrawer.module.css'

interface EnhedTagPickerProps {
  /** The person's primary Organisation — the enhed source. */
  organisationId: string
  /** The currently-selected enhed ids (controlled by the drawer). */
  selectedIds: string[]
  onChange: (ids: string[]) => void
  /** The person's current enhed tag NAMES (comma-joined display label) used to
      seed the initial selection ONCE the enheder list resolves. Optional. */
  currentTagNames?: string | null
  /** Fired ONCE when the picker seeds the initial selection from the current tag
      names, so the drawer can sync the save-dirtiness baseline (an un-touched
      save must NOT re-PUT the seeded tags). Receives the seeded id set. */
  onSeed?: (ids: string[]) => void
  /** The legacy free-text `enhed_label` shown read-only when the person has no
      structured tags (transitional display fallback). */
  legacyLabel?: string | null
  disabled?: boolean
}

/** Split a comma-joined enhed-names display string into trimmed, lower-cased
    tokens for case-insensitive matching against the active enhed names. */
function splitTagNames(joined: string | null | undefined): Set<string> {
  if (!joined) return new Set()
  return new Set(
    joined
      .split(',')
      .map((s) => s.trim().toLowerCase())
      .filter((s) => s.length > 0),
  )
}

export function EnhedTagPicker({
  organisationId,
  selectedIds,
  onChange,
  currentTagNames,
  onSeed,
  legacyLabel,
  disabled = false,
}: EnhedTagPickerProps) {
  const { fetchEnheder } = useEnheder()
  const [enheder, setEnheder] = useState<Enhed[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Seed the initial selection exactly once per (org, load) so subsequent user
  // edits are not clobbered by a re-render.
  const seededFor = useRef<string | null>(null)

  useEffect(() => {
    if (!organisationId) {
      setEnheder([])
      return
    }
    let cancelled = false
    setLoading(true)
    setError(null)
    void fetchEnheder(organisationId).then((result) => {
      if (cancelled) return
      if (result.ok) {
        setEnheder(result.data)
        // Seed the initial selection from the current tag names (once per org).
        // Always report the seed (even empty) so the drawer can set the
        // save-dirtiness baseline to the seeded set.
        if (seededFor.current !== organisationId) {
          seededFor.current = organisationId
          const wanted = splitTagNames(currentTagNames)
          const ids =
            wanted.size > 0
              ? result.data
                  .filter((e) => wanted.has(e.name.trim().toLowerCase()))
                  .map((e) => e.enhedId)
              : []
          if (ids.length > 0) onChange(ids)
          onSeed?.(ids)
        }
      } else {
        setEnheder([])
        // A MAO 400s (no enheder) — show the legacy fallback only, no error noise.
        setError(result.status === 400 ? null : result.error)
      }
      setLoading(false)
    })
    return () => {
      cancelled = true
    }
    // currentTagNames/onChange intentionally omitted — seed-once semantics.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [organisationId, fetchEnheder])

  const toggle = (id: string) => {
    if (selectedIds.includes(id)) {
      onChange(selectedIds.filter((x) => x !== id))
    } else {
      onChange([...selectedIds, id])
    }
  }

  const selectedSet = new Set(selectedIds)
  const hasStructuredTags = selectedIds.length > 0

  return (
    <div className={styles.formField}>
      <span className={styles.formLabel}>Enhed</span>

      {loading ? (
        <div className={styles.loading}>
          <Spinner size="sm" />
        </div>
      ) : error ? (
        <div className={styles.sectionError} role="alert" data-testid="ep-enheder-error">
          {error}
        </div>
      ) : enheder.length === 0 ? (
        <div className={styles.helperText} data-testid="ep-enheder-empty">
          Ingen enheder oprettet for denne organisation.
          {legacyLabel ? ` Tidligere etiket: ${legacyLabel}.` : ''}
        </div>
      ) : (
        <div className={styles.enhedList} data-testid="ep-enheder-picker">
          {enheder.map((enhed) => (
            <Checkbox
              key={enhed.enhedId}
              id={`ep-enhed-${enhed.enhedId}`}
              label={enhed.name}
              checked={selectedSet.has(enhed.enhedId)}
              onChange={() => toggle(enhed.enhedId)}
              disabled={disabled}
            />
          ))}
        </div>
      )}

      {/* Legacy read-only fallback: shown when no structured tags are selected
          AND a frozen free-text label exists (transitional display). */}
      {!loading && !hasStructuredTags && legacyLabel && enheder.length > 0 && (
        <div className={styles.helperText} data-testid="ep-enhed-legacy">
          Tidligere etiket (fri tekst): {legacyLabel}
        </div>
      )}

      <div className={styles.helperText}>
        Enheder er ren visningsmetadata (påvirker ikke regler, adgang eller løn).
      </div>
    </div>
  )
}
