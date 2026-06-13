// S72 / TASK-7204 — the "Administrer projekter" manager modal (design_handoff_skema
// README §5; prototype projects-manager.jsx — the wired-in tabbed `ProjectManager`
// with `DualPaneBody`, PM_DualPane being the chosen pane layout). Composed on the
// EXISTING kit Dialog + Tabs (SPRINT-72 R8): Radix owns the scrim, focus trap,
// Escape-to-close and aria wiring; this component adds no scratch overlay.
//
// INTERACTION MODEL — CALLBACK-PER-ACTION (prototype-faithful, NOT staged): the
// prototype's ProjectManager is fully CONTROLLED (`tab.mine` comes from the
// parent) and every add / remove / reorder calls `tab.onApply(nextFullList)`
// IMMEDIATELY (projects-manager.jsx:205/215 via MineList `setMine={onApply}`);
// the README pins "Changes apply live to the grid." (Interactions) and R11 pins
// modal-live-updates. This component therefore stages NOTHING: each action emits
// the FULL next ordered selection (sortOrder dense 0..n-1) via
// `onProjectsChange` / `onAbsenceTypesChange`, and the PAGE (7205) persists per
// action or batches — `Færdig` only closes. The PUT body derives via
// `toRowPreferencesPutBody` (lib/api — the dense-renumbering owner). R16: the
// page must flush pending debounced saves BEFORE the preferences refetch; this
// component never fetches and owns no save plumbing.
//
// R4 semantics surfaced here: the LEFT pane renders the current VISIBLE
// selection (rowPreferences — server-effective order); the RIGHT pane renders
// the selection-INDEPENDENT catalog minus the current selection, so removed
// rows stay re-addable. An EMPTY selection is legal (configured-empty) — no
// forced minimum. R5: preferences are month-independent view state, so the
// modal stays fully usable on locked months (no readOnly prop by design).
import { useMemo, useState } from 'react'
import { Dialog } from './ui/Dialog'
import { Tabs } from './ui/Tabs'
import { Alert } from './ui/Alert'
import { Button } from './ui/Button'
import { Input } from './ui/Input'
import type { SkemaRowPreferencesInvalidPayload } from '../lib/api'
import type {
  SkemaCatalogs,
  SkemaRowPreferenceAbsenceType,
  SkemaRowPreferenceProject,
  SkemaRowPreferences,
} from '../types'
import styles from './SkemaProjectManager.module.css'

interface SkemaProjectManagerProps {
  open: boolean
  /** The CURRENT effective visible rows (R4: catalog ∩ selections, server-dense
      positions). The component is CONTROLLED — after each emitted action the
      parent applies/persists and re-renders with the next value. */
  rowPreferences: SkemaRowPreferences
  /** The ADDABLE catalogs, selection-independent (R4). */
  catalogs: SkemaCatalogs
  /** Per-action callback (the prototype's `onApply`): the FULL next ordered
      project selection, sortOrder dense 0..n-1. */
  onProjectsChange: (next: SkemaRowPreferenceProject[]) => void
  /** Per-action callback for the Ferie og fravær tab — same contract. */
  onAbsenceTypesChange: (next: SkemaRowPreferenceAbsenceType[]) => void
  onClose: () => void
  /** The PUT 422 `row_preferences_invalid` payload — rendered as an error Alert
      listing the offenders. The page owns the PUT and passes this in (7205). */
  saveError?: SkemaRowPreferencesInvalidPayload | null
}

/** Tab-agnostic row shape shared by both panes. For projects: name=projectName,
    code=projectCode. For absence types: name=label, code=the type key (the
    catalog carries no separate code; the type key is the stable mono-rendered
    identifier, matching the prototype's "nøgler matcher lønart-mapping" note). */
interface PaneEntry {
  key: string
  name: string
  code: string
  /** S73 R5 — the served full-day-only flag (absence entries only); renders the
      "hele dage" note next to the row. From the served absence-type DTOs — never
      a hardcoded type list. */
  fullDayOnly?: boolean
}

/** Prototype matchq verbatim: name OR code, case-insensitive. */
function matchesQuery(entry: PaneEntry, q: string): boolean {
  return !q || `${entry.name} ${entry.code}`.toLowerCase().includes(q.toLowerCase())
}

// ── Inline 16×16 line icons in the kit style (handoff Assets: stroke
// currentColor, width 2, square caps) ──
function IconPlus({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d="M8 3V13M3 8H13" stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
    </svg>
  )
}

function IconArrow({ dir }: { dir: 'up' | 'down' }) {
  const d = dir === 'up' ? 'M4 10L8 6L12 10' : 'M4 6L8 10L12 6'
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d={d} stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
    </svg>
  )
}

interface DualPaneProps {
  /** Stable id prefix for the search Input (kit Input requires `id`). */
  idPrefix: string
  selected: PaneEntry[]
  catalog: PaneEntry[]
  searchPlaceholder: string
  hint?: string
  /** Left-pane empty-state copy (per-tab — see the deviation note in the
      component header). */
  emptyText: string
  /** The prototype's `onApply`: fired once per action with the full next list. */
  onApply: (next: PaneEntry[]) => void
}

/** The dual-pane body (prototype `DualPaneBody` + `MineList`, variant A —
    ▲▼ reorder buttons, no drag). */
function DualPane({
  idPrefix,
  selected,
  catalog,
  searchPlaceholder,
  hint,
  emptyText,
  onApply,
}: DualPaneProps) {
  const [q, setQ] = useState('')

  const selectedKeys = useMemo(() => new Set(selected.map((e) => e.key)), [selected])
  const available = catalog.filter((e) => !selectedKeys.has(e.key) && matchesQuery(e, q))

  // ▲▼ = adjacent swap; the dense renumbering happens at emission time (the
  // parent maps the array order to sortOrder 0..n-1).
  const move = (index: number, delta: -1 | 1) => {
    const j = index + delta
    if (j < 0 || j >= selected.length) return
    const next = [...selected]
    ;[next[index], next[j]] = [next[j], next[index]]
    onApply(next)
  }
  const remove = (key: string) => onApply(selected.filter((e) => e.key !== key))
  const add = (entry: PaneEntry) => onApply([...selected, entry])

  return (
    <div className={styles.dual}>
      <section className={styles.pane}>
        <h4 className={styles.paneTitle}>
          Valgt <span className={styles.count}>{selected.length}</span>
        </h4>
        {hint && <p className={styles.paneHint}>{hint}</p>}
        <ul className={styles.mine}>
          {selected.length === 0 && <li className={styles.empty}>{emptyText}</li>}
          {selected.map((entry, i) => (
            <li className={styles.row} key={entry.key}>
              <span className={styles.ord}>
                <button
                  type="button"
                  className={styles.iconBtn}
                  onClick={() => move(i, -1)}
                  disabled={i === 0}
                  aria-label={`Flyt ${entry.name} op`}
                >
                  <IconArrow dir="up" />
                </button>
                <button
                  type="button"
                  className={styles.iconBtn}
                  onClick={() => move(i, 1)}
                  disabled={i === selected.length - 1}
                  aria-label={`Flyt ${entry.name} ned`}
                >
                  <IconArrow dir="down" />
                </button>
              </span>
              <span className={styles.rowMain}>
                <span className={styles.rowName}>
                  {entry.name}
                  {entry.fullDayOnly && <span className={styles.fullDayNote}>hele dage</span>}
                </span>
                <span className={styles.rowCode}>{entry.code}</span>
              </span>
              <button
                type="button"
                className={styles.remove}
                onClick={() => remove(entry.key)}
                aria-label={`Fjern ${entry.name}`}
              >
                Fjern
              </button>
            </li>
          ))}
        </ul>
      </section>
      <section className={`${styles.pane} ${styles.paneCat}`}>
        <h4 className={styles.paneTitle}>Tilføj fra katalog</h4>
        <Input
          id={`${idPrefix}-search`}
          className={styles.search}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={searchPlaceholder}
          aria-label={searchPlaceholder}
        />
        <ul className={styles.cat}>
          {available.length === 0 && <li className={styles.empty}>Ingen matcher.</li>}
          {available.map((entry) => (
            <li className={styles.catRow} key={entry.key}>
              <span className={styles.rowMain}>
                <span className={styles.rowName}>
                  {entry.name}
                  {entry.fullDayOnly && <span className={styles.fullDayNote}>hele dage</span>}
                </span>
                <span className={styles.rowCode}>{entry.code}</span>
              </span>
              <button
                type="button"
                className={styles.add}
                onClick={() => add(entry)}
                aria-label={`Tilføj ${entry.name}`}
              >
                <IconPlus /> Tilføj
              </button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}

export function SkemaProjectManager({
  open,
  rowPreferences,
  catalogs,
  onProjectsChange,
  onAbsenceTypesChange,
  onClose,
  saveError,
}: SkemaProjectManagerProps) {
  // Defensive stable order: the server serves dense effective positions, but the
  // modal must never render a stale interleaving.
  const selectedProjects = useMemo(
    () => [...rowPreferences.projects].sort((a, b) => a.sortOrder - b.sortOrder),
    [rowPreferences.projects],
  )
  const selectedAbsence = useMemo(
    () => [...rowPreferences.absenceTypes].sort((a, b) => a.sortOrder - b.sortOrder),
    [rowPreferences.absenceTypes],
  )

  const projectEntries: PaneEntry[] = selectedProjects.map((p) => ({
    key: p.projectId,
    name: p.projectName,
    code: p.projectCode,
  }))
  const projectCatalog: PaneEntry[] = catalogs.projects.map((p) => ({
    key: p.projectId,
    name: p.projectName,
    code: p.projectCode,
  }))
  const absenceEntries: PaneEntry[] = selectedAbsence.map((a) => ({
    key: a.type,
    name: a.label,
    code: a.type,
    fullDayOnly: a.fullDayOnly ?? false,
  }))
  const absenceCatalog: PaneEntry[] = catalogs.absenceTypes.map((a) => ({
    key: a.type,
    name: a.label,
    code: a.type,
    fullDayOnly: a.fullDayOnly ?? false,
  }))

  // Emission: array order → dense sortOrder 0..n-1 (the same rule
  // toRowPreferencesPutBody applies to the wire body).
  const applyProjects = (next: PaneEntry[]) =>
    onProjectsChange(
      next.map((e, i) => ({
        projectId: e.key,
        projectCode: e.code,
        projectName: e.name,
        sortOrder: i,
      })),
    )
  const applyAbsence = (next: PaneEntry[]) =>
    onAbsenceTypesChange(
      // S73 R5 — carry the served fullDayOnly flag through so the optimistic
      // re-render keeps the "hele dage" note (the wire PUT body ignores it; the
      // server owns the flag). Only attach the flag when TRUE so the emitted shape
      // is byte-identical to the pre-S73 contract for ordinary types (S72 pins).
      next.map((e, i) =>
        e.fullDayOnly
          ? { type: e.key, label: e.name, sortOrder: i, fullDayOnly: true }
          : { type: e.key, label: e.name, sortOrder: i },
      ),
    )

  const tabs = [
    {
      value: 'proj',
      label: (
        <>
          Projekter <span className={styles.count}>{selectedProjects.length}</span>
        </>
      ),
      content: (
        <DualPane
          idPrefix="spm-proj"
          selected={projectEntries}
          catalog={projectCatalog}
          searchPlaceholder="Søg projekt eller kode…"
          hint="Rækkefølge bestemmer visningen i skemaet."
          emptyText="Ingen projekter valgt endnu."
          onApply={applyProjects}
        />
      ),
    },
    {
      value: 'frav',
      label: (
        <>
          Ferie og fravær <span className={styles.count}>{selectedAbsence.length}</span>
        </>
      ),
      content: (
        <>
          <div className={styles.note}>
            <Alert variant="info">
              Bemærk: kataloget over fraværstyper er ikke komplet i denne mock-up.
            </Alert>
          </div>
          <DualPane
            idPrefix="spm-frav"
            selected={absenceEntries}
            catalog={absenceCatalog}
            searchPlaceholder="Søg fraværstype…"
            emptyText="Ingen fraværstyper valgt endnu."
            onApply={applyAbsence}
          />
        </>
      ),
    },
  ]

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!o) onClose()
      }}
      title="Administrer rækker"
      description="Vælg hvilke projekter og fraværstyper der vises på dit skema."
      contentClassName={styles.dialog}
    >
      {saveError && (
        <div className={styles.note}>
          <Alert variant="error">
            <p className={styles.errLine}>{saveError.message}</p>
            {(saveError.invalidProjectIds?.length ?? 0) > 0 && (
              <p className={styles.errLine}>
                Ugyldige projekter: {saveError.invalidProjectIds.join(', ')}
              </p>
            )}
            {(saveError.duplicateProjectIds?.length ?? 0) > 0 && (
              <p className={styles.errLine}>
                Dublerede projekter: {saveError.duplicateProjectIds.join(', ')}
              </p>
            )}
            {(saveError.invalidAbsenceTypes?.length ?? 0) > 0 && (
              <p className={styles.errLine}>
                Ugyldige fraværstyper: {saveError.invalidAbsenceTypes.join(', ')}
              </p>
            )}
            {(saveError.duplicateAbsenceTypes?.length ?? 0) > 0 && (
              <p className={styles.errLine}>
                Dublerede fraværstyper: {saveError.duplicateAbsenceTypes.join(', ')}
              </p>
            )}
          </Alert>
        </div>
      )}
      <Tabs tabs={tabs} defaultValue="proj" />
      <div className={styles.foot}>
        <span className={styles.footNote}>
          Fjernede rækker beholder deres registreringer — de skjules blot fra skemaet.
        </span>
        <Button variant="primary" onClick={onClose}>
          Færdig
        </Button>
      </div>
    </Dialog>
  )
}
