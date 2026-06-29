// SPRINT-107 / TASK-10704 (Enhedsspor Phase 3b-1) — the SEARCH overlay for the
// merged "Organisation & medarbejdere" admin page.
//
// A full-screen scrim + centered palette (the design's command-palette): an
// autofocused input feeding the debounced server search (useSearch), the two
// collapsible sections ENHEDER + MEDARBEJDERE (bold header + fold caret + green
// count pill), each row carrying its full path. Opens via the page's Søg button
// or the `/` shortcut; closes on Esc or a scrim click.
//
// READ + NAVIGATE ONLY (S91 dead-button discipline): selecting a result
// NAVIGATES the tree/panel (sets the page's selected node) and closes the
// overlay. The design opens the person's EDIT DRAWER on click — S107 must NOT
// (drawers are S108). A person result navigates to their Organisation (the id the
// result carries — PersonSearchResult has no unitId; the Organisation is the
// person's container the panel then renders).
//
// [Step-0b — respects the Afgrænsning]: the results are filtered client-side to
// the selected org set via each result's `organisationId` (NOT the fragile path
// text), and the footer note shows when the view is actively scoped. The search
// itself is server-scoped (D5); the Afgrænsning only narrows further.
//
// Styling is tokens-not-hardcoded (per-type accent/tint via the inherited
// --unit-accent-<type> / --unit-tint-<type> page-root vars); square corners;
// Danish copy verbatim.

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearch } from '../../../hooks/useSearch'
import { isScoped } from './afgraensning'
import type { SelectedNode } from './OrgStructureTree'
import { LABEL, type UnitType } from './typeMaps'
import styles from './SearchOverlay.module.css'

interface SearchOverlayProps {
  open: boolean
  onClose: () => void
  /** Navigate the page to a node (selecting it drives the tree + Struktur). */
  onNavigate: (node: SelectedNode) => void
  /** The applied Afgrænsning (null = all) — filters the results client-side. */
  selected: Set<string> | null
  allOrgIds: string[]
}

function initials(name: string): string {
  const parts = (name || '').trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[parts.length - 1]?.[0] ?? '')).toUpperCase()
}

export function SearchOverlay({ open, onClose, onNavigate, selected, allOrgIds }: SearchOverlayProps) {
  const { query, setQuery, results, loading } = useSearch()
  const inputRef = useRef<HTMLInputElement>(null)

  // Esc closes; focus the input on open.
  useEffect(() => {
    if (!open) return
    inputRef.current?.focus()
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onClose()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [open, onClose])

  const scoped = isScoped(selected, allOrgIds)

  // Filter to the selected org set via organisationId (NOT path text). When not
  // scoped (all / null) every server-admitted row is kept.
  const inScope = (orgId: string): boolean => !scoped || (selected?.has(orgId) ?? false)
  const units = useMemo(() => results.units.filter((u) => inScope(u.organisationId)), [results, scoped, selected])
  const people = useMemo(() => results.people.filter((p) => inScope(p.organisationId)), [results, scoped, selected])

  if (!open) return null

  const trimmed = query.trim()
  const hasResults = units.length > 0 || people.length > 0
  const idle = trimmed.length === 0
  const noResults = !idle && !loading && !hasResults

  const goUnit = (unitId: string, name: string, type: UnitType) => {
    onNavigate({ id: unitId, kind: 'unit', name, type })
    onClose()
  }
  const goPerson = (organisationId: string, orgName: string) => {
    onNavigate({ id: organisationId, kind: 'organisation', name: orgName, type: 'organisation' })
    onClose()
  }

  const chipStyle = (type: UnitType) => ({
    color: `var(--unit-accent-${type})`,
    background: `var(--unit-tint-${type})`,
  })

  return (
    <div
      className={styles.scrim}
      data-testid="search-overlay"
      onClick={onClose}
    >
      <div className={styles.palette} role="dialog" aria-label="Søg" onClick={(e) => e.stopPropagation()}>
        <div className={styles.inputRow}>
          <span className={styles.inputGlyph} aria-hidden="true">⌕</span>
          <input
            ref={inputRef}
            type="text"
            className={styles.input}
            data-testid="search-input"
            placeholder="Søg efter enhed eller medarbejder…"
            defaultValue={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          <button type="button" className={styles.escBtn} data-testid="search-esc" onClick={onClose}>
            Esc
          </button>
        </div>

        <div className={styles.body}>
          {hasResults && (
            <>
              {units.length > 0 && (
                <SearchSection
                  testid="search-section-enheder"
                  label="Enheder"
                  count={units.length}
                >
                  {units.map((u) => (
                    <button
                      key={u.unitId}
                      type="button"
                      className={styles.resultRow}
                      data-testid={`search-unit-${u.unitId}`}
                      onClick={() => goUnit(u.unitId, u.name, u.type)}
                    >
                      <span className={styles.unitDot} aria-hidden="true" style={{ background: `var(--unit-accent-${u.type})` }} />
                      <span className={styles.resultBody}>
                        <span className={styles.resultNameRow}>
                          <span className={styles.resultName}>{u.name}</span>
                          <span className={styles.typeChip} style={chipStyle(u.type)}>{LABEL[u.type]}</span>
                        </span>
                        <span className={styles.resultPath}>{u.path.join(' › ')}</span>
                      </span>
                      <span className={styles.chevron} aria-hidden="true">›</span>
                    </button>
                  ))}
                </SearchSection>
              )}

              {people.length > 0 && (
                <SearchSection
                  testid="search-section-medarbejdere"
                  label="Medarbejdere"
                  count={people.length}
                >
                  {people.map((p) => (
                    <button
                      key={p.userId}
                      type="button"
                      className={styles.resultRow}
                      data-testid={`search-person-${p.userId}`}
                      onClick={() => goPerson(p.organisationId, p.path[0] ?? p.organisationId)}
                    >
                      <span className={styles.personAvatar} aria-hidden="true">{initials(p.displayName)}</span>
                      <span className={styles.resultBody}>
                        <span className={styles.resultName}>{p.displayName}</span>
                        {p.position && <span className={styles.personTitle}>{p.position}</span>}
                        <span className={styles.resultPath}>{p.path.join(' › ')}</span>
                      </span>
                      <span className={styles.chevron} aria-hidden="true">›</span>
                    </button>
                  ))}
                </SearchSection>
              )}
            </>
          )}

          {noResults && (
            <div className={styles.message} data-testid="search-no-results">
              Ingen enheder eller medarbejdere matcher “{query}”.
            </div>
          )}
          {idle && (
            <div className={styles.hint} data-testid="search-idle">
              Skriv for at søge i enheder og medarbejdere på tværs af hele organisationen.
            </div>
          )}
        </div>

        {scoped && (
          <div className={styles.scopeNote} data-testid="search-scope-note">
            Søgningen er begrænset til den valgte afgrænsning.
          </div>
        )}
      </div>
    </div>
  )
}

interface SearchSectionProps {
  testid: string
  label: string
  count: number
  children: ReactNode
}

/** A collapsible search section (bold header + green fold caret + green count
    pill). Folding is local UI state; the body is hidden while folded. */
function SearchSection({ testid, label, count, children }: SearchSectionProps) {
  const [folded, setFolded] = useState(false)
  return (
    <>
      <button
        type="button"
        className={styles.sectionHeader}
        data-testid={testid}
        onClick={() => setFolded((v) => !v)}
      >
        <span className={styles.foldCaret} aria-hidden="true">{folded ? '▸' : '▾'}</span>
        <span className={styles.sectionLabel}>{label}</span>
        <span className={styles.countPill} data-testid={`${testid}-count`}>{count}</span>
      </button>
      {!folded && children}
    </>
  )
}
