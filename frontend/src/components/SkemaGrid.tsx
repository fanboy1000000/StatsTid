// S72 / TASK-7202 — Skema grid restructured to the design_handoff_skema layout
// (README §3 "Month grid"). Row order top→bottom: Diff. fra normtid (read-only) →
// Registrér arbejdstid (the ONE interactive row) → divider → ▾ Projekter band →
// project rows → ▾ Ferie og fravær band → absence rows → I alt. The grid is
// PRESENTATION-ONLY (SPRINT-72 R16): it owns no fetching and no save plumbing —
// the day panel opens via `onOpenDay(date)`, the manager modal via `onOpenManager`.
//
// Pinned rules implemented here:
//   R1  — norms are SERVED, never constant (no 7,4 literal anywhere); null norm →
//         blank rendering; ADR-032 D3 absence-cell prefill ships UNCHANGED.
//   R2  — Diff(day) = (workTime periods + manualHours + absence hours) − served norm.
//         A full-absence day shows 0,0 GREEN (the shipped grid showed −7,4 red); a day
//         with NO registration at all is BLANK (the shipped grid deliberately showed
//         −norm). Trailing total sums rendered values.
//   R3  — ALL arithmetic (Diff, unallocated remainder, I alt, Sum, ✓/amber) computes
//         over ALL served data INCLUDING rows hidden by preferences; `rowPreferences`
//         filters RENDERING only. Hidden rows carrying hours surface the
//         "N skjulte rækker har timer i denne måned" affordance.
//   R5  — the ✓/amber classification mirrors the backend allocation gate's exact
//         tolerance via lib/allocation (round-2dp, |Δ| < 0.005).
//   R12 — read-only/review mode: cells render as text, the interactive row renders as
//         data (no panel trigger), no prefill; when `rowPreferences` is absent the
//         grid falls back to rendering ALL served rows (the approval surface's
//         leader-sees-the-full-record contract).
//   R17 — cell-clearing is a RECORDED inherited limitation: the save path drops
//         null/0 cells, so the 0→null propagation semantics here are kept verbatim
//         from the shipped grid (do NOT add delete semantics).
import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import type { SkemaRow, SkemaRowPreferences } from '../types'
import { parseDanishNumber, formatDanishNumber } from '../lib/locale'
import { classifyAllocation, unallocated } from '../lib/allocation'
import { computeDayDiffs, computeMonthDiffTotal } from '../hooks/useSkema'
import styles from './SkemaGrid.module.css'

export interface WorkInterval {
  start: string  // "HH:mm" or "HH:mm:ss"
  end: string    // "HH:mm" or "HH:mm:ss"
}

export type WorkIntervalsMap = Map<string, WorkInterval[]>  // dateKey -> intervals
export type ManualHoursMap = Map<string, number>            // dateKey -> manual hours
export type DailyNormMap = Map<string, number | null>       // dateKey -> norm hours (null = blank)
// S73 / TASK-7302 — dateKey -> the served ADR-032 consumption basis (R3/R5).
// `null` = no dated profile covers the day → NO full-day snap (the typed value
// stands; the server rejects via the anchor-422 family — fail-closed).
export type ConsumptionBasisMap = Map<string, number | null>

interface SkemaGridProps {
  year: number
  month: number
  /** ALL served rows (projects + absences) — the R3 arithmetic basis. Visibility
      filtering happens via `rowPreferences` and affects RENDERING only. */
  rows: SkemaRow[]
  cellValues: Map<string, number>
  readOnly: boolean
  onCellChange: (rowKey: string, date: string, hours: number | null) => void
  /** Served work-time intervals per day — feeds worked = periods + manual (R2). */
  workIntervals?: WorkIntervalsMap
  /** Legacy pre-S72 page wiring ("Tilføj periode" dialog) — accepted for prop
      compatibility until TASK-7205 swaps the page; no longer used (the day panel
      owns interval editing per R16). */
  onWorkIntervalsChange?: (date: string, intervals: WorkInterval[]) => void
  manualHours?: ManualHoursMap
  /** Legacy pre-S72 page wiring ("Tilføj timer" row) — accepted-but-unused
      (owner ruling D-B drops the lump-hours entry UI). */
  onManualHoursChange?: (date: string, hours: number | null) => void
  dailyNorm?: DailyNormMap
  /** S73 R5 — the served per-day ADR-032 consumption basis. A commit in a
      `fullDayOnly` absence cell SNAPS to `consumptionBasis.get(dateKey)`; a null
      basis (no dated profile) means NO snap (the typed value stands, fail-closed
      server-side). Absent map → no snap data (full-day cells behave like the
      pre-S73 prefill — the typed value stands). */
  consumptionBasis?: ConsumptionBasisMap
  /** S72 R4/R12 — the VISIBLE row sets + order. Absent → ALL served rows render
      (the approval surface / pre-S72 fallback). */
  rowPreferences?: SkemaRowPreferences
  /** S72 R16 — clicking a Registrér arbejdstid cell opens the day panel for that
      date ("YYYY-MM-DD"). Absent or readOnly → the row renders as data. */
  onOpenDay?: (date: string) => void
  /** S72 R3 — the hidden-rows affordance links to the manager modal. */
  onOpenManager?: () => void
}

// Danish weekday abbreviations, indexed by Date.getDay() (handoff prototype copy).
const DA_DAY_ABBREV = ['søn', 'man', 'tir', 'ons', 'tor', 'fre', 'lør']

function getDaysInMonth(year: number, month: number): Date[] {
  const days: Date[] = []
  const daysCount = new Date(year, month, 0).getDate()
  for (let d = 1; d <= daysCount; d++) {
    days.push(new Date(year, month - 1, d))
  }
  return days
}

function formatDateKey(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function isToday(date: Date): boolean {
  const today = new Date()
  return (
    date.getFullYear() === today.getFullYear() &&
    date.getMonth() === today.getMonth() &&
    date.getDate() === today.getDate()
  )
}

function isWeekend(date: Date): boolean {
  const day = date.getDay()
  return day === 0 || day === 6
}

function calcIntervalHours(intervals: WorkInterval[]): number {
  let totalSec = 0
  for (const iv of intervals) {
    if (iv.start && iv.end) {
      const sp = iv.start.split(':').map(Number)
      const ep = iv.end.split(':').map(Number)
      const startSec = sp[0] * 3600 + sp[1] * 60 + (sp[2] ?? 0)
      const endSec = ep[0] * 3600 + ep[1] * 60 + (ep[2] ?? 0)
      const diff = endSec - startSec
      if (diff > 0) totalSec += diff
    }
  }
  return Math.round((totalSec / 3600) * 100) / 100
}

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/** Cell/total display: 1 decimal, Danish comma, trailing ",0" trimmed ("7,4" / "7"). */
function formatCell(v: number): string {
  return formatDanishNumber(v, 1)
}

/** Diff display: ALWAYS 1 decimal ("0,0", "+0,6", "-3,4") — R2 pins the explicit 0,0. */
function formatSignedDiff(v: number): string {
  const s = v.toFixed(1).replace('.', ',')
  return v > 0 ? `+${s}` : s
}

/** Remainder display: plain 1 decimal ("2,0"). */
function formatHours1(v: number): string {
  return v.toFixed(1).replace('.', ',')
}

export function SkemaGrid({
  year,
  month,
  rows,
  cellValues,
  readOnly,
  onCellChange,
  workIntervals,
  manualHours,
  dailyNorm,
  consumptionBasis,
  rowPreferences,
  onOpenDay,
  onOpenManager,
}: SkemaGridProps) {
  const days = useMemo(() => getDaysInMonth(year, month), [year, month])
  const dateKeys = useMemo(() => days.map(formatDateKey), [days])

  // ── R3 arithmetic basis: ALL served rows, independent of visibility ──
  const projectRowsAll = useMemo(() => rows.filter((r) => r.type === 'project'), [rows])
  const absenceRowsAll = useMemo(() => rows.filter((r) => r.type === 'absence'), [rows])

  // ── Visible rows (R4 preferences = RENDERING filter only; R12 fallback = all) ──
  const visibleProjectRows = useMemo<SkemaRow[]>(() => {
    if (!rowPreferences) return projectRowsAll
    const byKey = new Map(projectRowsAll.map((r) => [r.key, r]))
    return [...rowPreferences.projects]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((p) => byKey.get(p.projectCode) ?? { type: 'project' as const, key: p.projectCode, label: p.projectName })
  }, [rowPreferences, projectRowsAll])

  const visibleAbsenceRows = useMemo<SkemaRow[]>(() => {
    if (!rowPreferences) return absenceRowsAll
    const byKey = new Map(absenceRowsAll.map((r) => [r.key, r]))
    return [...rowPreferences.absenceTypes]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      // S73 R5 — the served fullDayOnly flag rides through: the basis row (from
      // deriveSkemaRowBasis) wins; the fallback carries the preference entry's
      // own served flag so the "hele dage" note + snap never drop on this path
      // (only attached when TRUE — falsy reads identically at the snap/note sites).
      .map(
        (a) =>
          byKey.get(a.type) ??
          (a.fullDayOnly
            ? { type: 'absence' as const, key: a.type, label: a.label, fullDayOnly: true }
            : { type: 'absence' as const, key: a.type, label: a.label }),
      )
  }, [rowPreferences, absenceRowsAll])

  // ── Hidden-rows affordance (R3): served keys carrying hours in the viewed month
  // that are NOT among the visible rows. Only meaningful when preferences filter. ──
  const hiddenRowsWithHours = useMemo(() => {
    if (!rowPreferences) return 0
    const visibleKeys = new Set([...visibleProjectRows, ...visibleAbsenceRows].map((r) => r.key))
    const monthPrefix = `${year}-${String(month).padStart(2, '0')}-`
    const hiddenKeys = new Set<string>()
    for (const [key, value] of cellValues) {
      if (value === 0) continue
      const dateKey = key.slice(-10)
      if (!dateKey.startsWith(monthPrefix)) continue
      const rowKey = key.slice(0, -11) // strip ":YYYY-MM-DD"
      if (!visibleKeys.has(rowKey)) hiddenKeys.add(rowKey)
    }
    return hiddenKeys.size
  }, [rowPreferences, visibleProjectRows, visibleAbsenceRows, cellValues, year, month])

  // ── Collapsible groups (the disclosure bands) ──
  const [collapsedGroups, setCollapsedGroups] = useState<{ projects: boolean; absences: boolean }>({
    projects: false,
    absences: false,
  })

  // ── Cell editing state: raw typed text while focused (decimal comma survives) ──
  const [editing, setEditing] = useState<{ key: string; raw: string; initial: string } | null>(null)

  // ── Recently-changed flash (--color-success-light → transparent, 1.2s) ──
  const [recentKey, setRecentKey] = useState<string | null>(null)
  const flashTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => {
    return () => {
      if (flashTimerRef.current) clearTimeout(flashTimerRef.current)
    }
  }, [])

  const triggerFlash = useCallback((cellKey: string) => {
    // Respect reduced motion (gov a11y, R15): skip the flash entirely. The CSS
    // animation also carries a prefers-reduced-motion media query as a belt.
    if (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      return
    }
    if (flashTimerRef.current) clearTimeout(flashTimerRef.current)
    setRecentKey(cellKey)
    flashTimerRef.current = setTimeout(() => setRecentKey(null), 1200)
  }, [])

  // ── Per-day worked = interval hours + manual hours (ADR-028 workTime; R2) ──
  const workedPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const dateKey of dateKeys) {
      const period = workIntervals ? calcIntervalHours(workIntervals.get(dateKey) ?? []) : 0
      const manual = manualHours?.get(dateKey) ?? 0
      map.set(dateKey, round2(period + manual))
    }
    return map
  }, [dateKeys, workIntervals, manualHours])

  // Allocated per day = Σ project cells over ALL served project rows (R3).
  const allocatedPerDay = useMemo(() => {
    const map = new Map<string, number>()
    for (const dateKey of dateKeys) {
      let total = 0
      for (const row of projectRowsAll) {
        total += cellValues.get(`${row.key}:${dateKey}`) ?? 0
      }
      map.set(dateKey, round2(total))
    }
    return map
  }, [dateKeys, projectRowsAll, cellValues])

  // ── Diff. fra normtid (R2) ──
  // S72 Step-7a W1: the diff arithmetic lives in useSkema's computeDayDiffs —
  // the ONE R2 computation owner shared with the Flex card's "Denne måned"
  // (useBalanceSummary.computeMonthFlexDelta delegates to the same helpers), so
  // the two surfaces reconcile BY CONSTRUCTION. Semantics (pinned):
  //   • norm null/missing (academic ANNUAL_ACTIVITY) → BLANK (R1), even with data;
  //   • a day with NO registration at all (no workTime, no absence, no allocation)
  //     → BLANK — the R2 pinned pro-handoff change (the shipped grid surfaced the
  //     full −norm shortfall on every empty norm-day);
  //   • a full-absence day → 0,0 GREEN (the shipped grid showed −7,4 red because
  //     absence hours were excluded from the diff basis).
  const diffPerDay = useMemo(
    () =>
      computeDayDiffs({
        year,
        month,
        cellValues,
        projectKeys: new Set(projectRowsAll.map((r) => r.key)),
        absenceKeys: new Set(absenceRowsAll.map((r) => r.key)),
        workIntervals: workIntervals ?? new Map(),
        manualHours: manualHours ?? new Map(),
        dailyNorm: dailyNorm ?? new Map(),
      }),
    [year, month, cellValues, projectRowsAll, absenceRowsAll, workIntervals, manualHours, dailyNorm],
  )

  // Trailing Diff total = Σ of RENDERED values (R2) — same single owner.
  const diffSum = useMemo(() => computeMonthDiffTotal(diffPerDay), [diffPerDay])

  // ── I alt: Σ ALL served cells per day (R3 — keys outside the served row list
  // still count; the served data is authoritative, not the rendering). ──
  const dayTotals = useMemo(() => {
    const map = new Map<string, number>()
    for (const [key, value] of cellValues) {
      const dateKey = key.slice(-10)
      map.set(dateKey, (map.get(dateKey) ?? 0) + value)
    }
    for (const [k, v] of map) map.set(k, round2(v))
    return map
  }, [cellValues])

  const grandTotal = useMemo(() => {
    let sum = 0
    for (const dateKey of dateKeys) sum += dayTotals.get(dateKey) ?? 0
    return round2(sum)
  }, [dateKeys, dayTotals])

  // Trailing Registrér arbejdstid total = Σ amber (under-allocated) hours only —
  // never nets against over-allocated days (the S56 per-day gate lesson holds).
  const amberTotal = useMemo(() => {
    let sum = 0
    for (const dateKey of dateKeys) {
      const worked = workedPerDay.get(dateKey) ?? 0
      const allocated = allocatedPerDay.get(dateKey) ?? 0
      if (worked === 0 && allocated === 0) continue
      if (classifyAllocation(worked, allocated) === 'under') {
        sum += unallocated(worked, allocated)
      }
    }
    return round2(sum)
  }, [dateKeys, workedPerDay, allocatedPerDay])

  // Per-row sums (Sum column) over the viewed month.
  const rowSums = useMemo(() => {
    const sums = new Map<string, number>()
    for (const row of rows) {
      let sum = 0
      for (const dateKey of dateKeys) {
        sum += cellValues.get(`${row.key}:${dateKey}`) ?? 0
      }
      sums.set(row.key, round2(sum))
    }
    return sums
  }, [rows, dateKeys, cellValues])

  // ── Handlers ──

  // ADR-032 D3 — norm prefill for absence-type cells, UNCHANGED behavior (R1).
  // Trigger = FIRST FOCUS of an EMPTY absence cell: seed that day's SERVED norm
  // (the same per-day value the Diff row reads). Never overwrites an existing
  // value; skipped entirely on zero/null-norm days; the seeded value stays fully
  // editable (partial days remain legal). No full-day snap (D-D owns that later).
  const handleCellFocus = useCallback(
    (row: SkemaRow, dateKey: string, currentValue: number | undefined) => {
      const cellKey = `${row.key}:${dateKey}`
      const fmt = currentValue != null ? formatCell(currentValue) : ''
      if (row.type === 'absence' && currentValue == null) {
        const norm = dailyNorm?.get(dateKey)
        if (norm !== null && norm !== undefined && norm > 0) {
          onCellChange(row.key, dateKey, norm)
          setEditing({ key: cellKey, raw: formatCell(norm), initial: fmt })
          return
        }
      }
      setEditing({ key: cellKey, raw: fmt, initial: fmt })
    },
    [dailyNorm, onCellChange]
  )

  // Keeps the RAW typed text in the focused input (decimal comma survives
  // mid-typing) while propagating the parsed value. Propagation semantics are
  // verbatim from the shipped grid (R17): '' → null, 0 → null (the save path
  // drops null/0 cells — clearing never persists; recorded inherited limitation),
  // unparsable input → no call.
  const handleCellInput = useCallback(
    (rowKey: string, cellKey: string, dateKey: string, value: string) => {
      setEditing((cur) =>
        cur && cur.key === cellKey ? { ...cur, raw: value } : { key: cellKey, raw: value, initial: '' }
      )
      const num = value === '' ? null : parseDanishNumber(value)
      if (num !== null && isNaN(num)) return
      onCellChange(rowKey, dateKey, num === 0 ? null : num)
    },
    [onCellChange]
  )

  // On blur the cell reformats to 1 decimal (the display falls back to the
  // formatted cellValues entry) and flashes when the value changed.
  //
  // S73 R5 — FULL-DAY SNAP (on commit): when the row is `fullDayOnly` and the
  // cell holds an ENTRY (the raw text parses to a non-null, non-zero value), the
  // value SNAPS to that day's served consumption basis. The null-basis case
  // (no dated profile covers the day) does NOT snap and invents no value — the
  // typed entry stands locally and the SERVER rejects it via the existing
  // anchor-422 family (fail-closed server-side). Blank stays blank (a cleared
  // cell propagated null already → nothing to snap). Non-full-day rows are
  // untouched here (ADR-032 D3 norm prefill stays the only seeding, unchanged).
  const handleCellBlur = useCallback(
    (row: SkemaRow, dateKey: string) => {
      if (row.type === 'absence' && row.fullDayOnly && editing) {
        const num = editing.raw === '' ? null : parseDanishNumber(editing.raw)
        const hasEntry = num !== null && !isNaN(num) && num !== 0
        if (hasEntry) {
          const basis = consumptionBasis?.get(dateKey)
          // null basis → no snap (typed value stands, server fail-closes); a
          // positive basis snaps the committed value to the day's full-day basis.
          if (basis !== null && basis !== undefined && basis > 0) {
            onCellChange(row.key, dateKey, basis)
          }
        }
      }
      if (editing && editing.raw !== editing.initial) {
        triggerFlash(editing.key)
      }
      setEditing(null)
    },
    [editing, triggerFlash, consumptionBasis, onCellChange],
  )

  const toggleGroup = useCallback((id: 'projects' | 'absences') => {
    setCollapsedGroups((g) => ({ ...g, [id]: !g[id] }))
  }, [])

  const interactive = !readOnly && !!onOpenDay

  // ── Renderers ──

  const renderEditableRow = (row: SkemaRow) => {
    const rowSum = rowSums.get(row.key) ?? 0
    // S73 R5 — the "hele dage" note renders from the SERVED fullDayOnly flag
    // (never a hardcoded type list); an explicit row.note still wins if present.
    const noteText = row.note ?? (row.fullDayOnly ? 'hele dage' : undefined)
    return (
      <tr key={row.key} className={row.type === 'absence' ? styles.absenceRow : undefined}>
        <td className={styles.rowLabel} title={noteText ? `${row.label} — ${noteText}` : row.label}>
          {row.label}
          {noteText && <span className={styles.rowNote}>{noteText}</span>}
        </td>
        {days.map((day) => {
          const dateKey = formatDateKey(day)
          const cellKey = `${row.key}:${dateKey}`
          const val = cellValues.get(cellKey)
          const tdClasses = [styles.cell]
          if (isWeekend(day)) tdClasses.push(styles.weekend)
          if (recentKey === cellKey) tdClasses.push(styles.recent)
          if (readOnly) {
            return (
              <td key={dateKey} className={tdClasses.join(' ')}>
                <span className={styles.cellDisplay}>{val != null ? formatCell(val) : ''}</span>
              </td>
            )
          }
          const fmt = val != null ? formatCell(val) : ''
          const display = editing && editing.key === cellKey ? editing.raw : fmt
          return (
            <td key={dateKey} className={tdClasses.join(' ')}>
              <input
                type="text"
                inputMode="decimal"
                className={styles.cellInput}
                value={display}
                onFocus={() => handleCellFocus(row, dateKey, val)}
                onChange={(e) => handleCellInput(row.key, cellKey, dateKey, e.target.value)}
                onBlur={() => handleCellBlur(row, dateKey)}
                aria-label={`${row.label} dag ${day.getDate()}`}
              />
            </td>
          )
        })}
        <td className={styles.sumCell}>{rowSum !== 0 ? formatCell(rowSum) : ''}</td>
      </tr>
    )
  }

  const renderBand = (id: 'projects' | 'absences', label: string, count: number) => {
    const isCollapsed = collapsedGroups[id]
    return (
      <tr className={styles.bandRow}>
        <td className={styles.bandCell}>
          <button
            type="button"
            className={styles.bandButton}
            aria-expanded={!isCollapsed}
            onClick={() => toggleGroup(id)}
          >
            <span className={styles.chevron} aria-hidden="true">
              {isCollapsed ? '▸' : '▾'}
            </span>
            {label} <span className={styles.bandCount}>({count})</span>
          </button>
        </td>
        <td className={styles.bandFill} colSpan={days.length + 1} />
      </tr>
    )
  }

  const hiddenNoteText = `${hiddenRowsWithHours} skjulte rækker har timer i denne måned`

  return (
    <div className={styles.gridWrapper}>
      <table className={styles.grid}>
        <thead>
          <tr>
            <th className={styles.labelHeader}>Dato</th>
            {days.map((day) => {
              const headClasses = [styles.dayHeader]
              if (isWeekend(day)) headClasses.push(styles.weekend)
              if (isToday(day)) headClasses.push(styles.today)
              return (
                <th key={formatDateKey(day)} className={headClasses.join(' ')}>
                  <div className={styles.dayAbbrev}>{DA_DAY_ABBREV[day.getDay()]}</div>
                  <div className={styles.dayNumber}>{day.getDate()}</div>
                </th>
              )
            })}
            <th className={styles.sumHeader}>Sum</th>
          </tr>
        </thead>
        <tbody>
          {/* 1 — Diff. fra normtid (read-only summary, grey; R2) */}
          <tr className={styles.diffRow}>
            <td className={styles.rowLabel}>Diff. fra normtid</td>
            {days.map((day) => {
              const dateKey = formatDateKey(day)
              const diff = diffPerDay.get(dateKey)
              return (
                <td key={dateKey}>
                  {diff !== undefined && (
                    <span className={diff < 0 ? styles.diffNeg : styles.diffPos}>
                      {formatSignedDiff(diff)}
                    </span>
                  )}
                </td>
              )
            })}
            <td className={`${styles.sumCell} ${styles.diffGrand}`}>
              {diffPerDay.size > 0 && (
                <span className={diffSum < 0 ? styles.diffNeg : styles.diffPos}>
                  {formatSignedDiff(diffSum)}
                </span>
              )}
            </td>
          </tr>

          {/* 2 — Registrér arbejdstid (the ONE interactive row; R12 read-only = data) */}
          <tr className={styles.workRow}>
            <td className={styles.rowLabel}>{readOnly ? 'Arbejdstid' : 'Registrér arbejdstid'}</td>
            {days.map((day) => {
              const dateKey = formatDateKey(day)
              const worked = workedPerDay.get(dateKey) ?? 0
              const allocated = allocatedPerDay.get(dateKey) ?? 0
              const show = worked > 0 || allocated > 0
              const state = show ? classifyAllocation(worked, allocated) : null
              const remainder = unallocated(worked, allocated)
              let content = ''
              let stateClass = ''
              if (state === 'balanced') {
                content = '✓'
                stateClass = styles.workBalanced
              } else if (state === 'under') {
                content = formatHours1(remainder)
                stateClass = styles.workUnder
              } else if (state === 'over') {
                content = `+${formatHours1(Math.abs(remainder))}`
                stateClass = styles.workOver
              }
              const tdClasses = [styles.workCell]
              if (interactive) tdClasses.push(styles.workClickable)
              if (isWeekend(day)) tdClasses.push(styles.weekend)
              return (
                <td key={dateKey} className={tdClasses.join(' ')}>
                  {interactive ? (
                    <button
                      type="button"
                      className={`${styles.workButton} ${stateClass}`}
                      onClick={() => onOpenDay?.(dateKey)}
                      title="Åbn dag — registrér perioder & fordel"
                      aria-label={`Registrér arbejdstid ${dateKey}`}
                    >
                      {content}
                    </button>
                  ) : (
                    <span className={`${styles.workDisplay} ${stateClass}`}>{content}</span>
                  )}
                </td>
              )
            })}
            <td className={styles.sumCell}>
              {amberTotal > 0 && <span className={styles.workUnder}>{formatHours1(amberTotal)}</span>}
            </td>
          </tr>

          {/* 3 — divider: a single full-width 2px rule, no label, no gap */}
          <tr className={styles.dividerRow}>
            <td colSpan={days.length + 2} />
          </tr>

          {/* 4 — ▾ Projekter band + visible project rows */}
          {renderBand('projects', 'Projekter', visibleProjectRows.length)}
          {!collapsedGroups.projects && visibleProjectRows.map(renderEditableRow)}

          {/* 5 — ▾ Ferie og fravær band + visible absence rows */}
          {renderBand('absences', 'Ferie og fravær', visibleAbsenceRows.length)}
          {!collapsedGroups.absences && visibleAbsenceRows.map(renderEditableRow)}

          {/* 6 — I alt (read-only bottom total, grey; R3 over ALL served data) */}
          <tr className={styles.totalRow}>
            <td className={styles.rowLabel}>I alt</td>
            {days.map((day) => {
              const dateKey = formatDateKey(day)
              const total = dayTotals.get(dateKey) ?? 0
              return <td key={dateKey}>{total !== 0 ? formatCell(total) : ''}</td>
            })}
            <td className={`${styles.sumCell} ${styles.grandTotal}`}>{formatCell(grandTotal)}</td>
          </tr>
        </tbody>
      </table>

      {/* Hidden-rows affordance (R3): only meaningful under preference filtering */}
      {hiddenRowsWithHours > 0 && (
        <div className={styles.hiddenNote}>
          {onOpenManager ? (
            <button type="button" className={styles.hiddenNoteButton} onClick={onOpenManager}>
              {hiddenNoteText}
            </button>
          ) : (
            <span>{hiddenNoteText}</span>
          )}
        </div>
      )}
    </div>
  )
}
