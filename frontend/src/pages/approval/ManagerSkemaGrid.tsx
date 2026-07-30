// S124 / TASK-12403 — the leader's READ-ONLY view of an employee's full month skema, rendered
// INLINE in the Teamoversigt detail panel.
//
// WHY INLINE (owner ruling 2026-07-30): "Skema needs to be the default view. It is fine with the
// summary at top, but the skema should always be shown." Behind a "Vis skema" button the evidence was
// one click further away than the decision — you could approve a month without ever seeing which days
// carried the hours. Expanding a row now shows the summary AND the grid, in that order.
//
// STILL LAZY: this component only mounts when its row is expanded (the accordion keeps one row open),
// so the per-employee month read fires on expand — never once per row on table render.
//
// READ-ONLY BY CONSTRUCTION, NOT BY TRUST:
//   • `SkemaGrid readOnly` renders every cell as data and ignores `onOpenDay` — no input surface, no
//     day panel.
//   • `onCellChange` is a hard no-op that cannot reach a save path.
//   • This component NEVER calls the mutation helpers `useSkema` exposes (saveMonth /
//     employeeApprove / submitAndApprove / reopenPeriod); it destructures only { data, loading,
//     error }, so none is even bound in this scope. Approve/reject stay in the panel footer — no
//     second mutation path.
// Since S124 / TASK-12404 a leader can no longer write another employee's registrations at all
// (HR-or-above floor on both write endpoints), so the component discipline and the API now agree —
// but the discipline is kept as the inner layer rather than relying on the outer one.
import { useMemo } from 'react'
import { SkemaGrid } from '../../components/SkemaGrid'
import { useSkema, deriveSkemaRowBasis } from '../../hooks/useSkema'
import styles from './ManagerSkemaGrid.module.css'

interface ManagerSkemaGridProps {
  employeeId: string
  year: number
  month: number
}

export function ManagerSkemaGrid({ employeeId, year, month }: ManagerSkemaGridProps) {
  const { data, loading, error } = useSkema(employeeId, year, month)

  // The row/arithmetic basis, derived exactly as the employee's own page derives it, so the leader
  // sees the same rows the employee filled in.
  // FAULT-ISOLATED like the sibling compliance fetch in this panel: a partial or malformed month
  // payload degrades THIS block to empty, it never throws and takes the leader's whole review panel
  // (summary, warnings, the approve/reject buttons) down with it.
  const rows = useMemo(() => {
    if (!data) return []
    try {
      return deriveSkemaRowBasis(data).rows
    } catch {
      return []
    }
  }, [data])

  // Server truth, read straight through — no local edit state exists on this surface.
  const cellValues = useMemo(() => {
    const cells = new Map<string, number>()
    if (!data) return cells
    for (const entry of data.entries ?? []) {
      // `projectCode` is nullable on the wire (S120); the degenerate null key normalizes to '',
      // matching deriveSkemaRowBasis, so those hours stay inside the grid's arithmetic.
      if (entry.hours !== 0) cells.set(`${entry.projectCode ?? ''}:${entry.date}`, entry.hours)
    }
    for (const absence of data.absences ?? []) {
      if (absence.hours !== 0) cells.set(`${absence.absenceType}:${absence.date}`, absence.hours)
    }
    return cells
  }, [data])

  const workIntervals = useMemo(() => {
    const map = new Map<string, { start: string; end: string }[]>()
    for (const wt of data?.workTime ?? []) {
      if (wt.intervals && wt.intervals.length > 0) {
        map.set(wt.date, wt.intervals.map(iv => ({ start: iv.start, end: iv.end })))
      }
    }
    return map
  }, [data])

  const manualHours = useMemo(() => {
    const map = new Map<string, number>()
    for (const wt of data?.workTime ?? []) {
      if (wt.manualHours && wt.manualHours !== 0) map.set(wt.date, wt.manualHours)
    }
    return map
  }, [data])

  const dailyNorm = useMemo(() => {
    const map = new Map<string, number | null>()
    for (const dn of data?.dailyNorm ?? []) map.set(dn.date, dn.hours)
    return map
  }, [data])

  return (
    // S125 / TASK-12500 — no heading or top border here: the panel renders the collapsible
    // "Skema" section header around this component, so owning one too would double the chrome.
    <div className={styles.wrap} data-testid={`manager-skema-${employeeId}`}>
      {loading && <div className={styles.status} data-testid="manager-skema-loading">Henter skema…</div>}
      {error && (
        <div className={styles.statusError} role="alert" data-testid="manager-skema-error">
          Kunne ikke hente skemaet.
        </div>
      )}
      {!loading && !error && data && (
        // A month grid is ~31 columns: it scrolls inside THIS container, never the page.
        <div className={styles.scroll}>
          {/* NO `rowPreferences`: those are the EMPLOYEE's own row-visibility choices, which hide
              rows on their page. A reviewer needs the unfiltered served set — per SkemaGrid's own
              contract, omitting the prop renders ALL served rows. Do not "fix" this by passing them. */}
          <SkemaGrid
            year={year}
            month={month}
            rows={rows}
            cellValues={cellValues}
            readOnly
            onCellChange={NO_EDITS}
            workIntervals={workIntervals}
            manualHours={manualHours}
            dailyNorm={dailyNorm}
            // The Arbejdstid row must show HOURS WORKED, not the allocation glyph. Without this a
            // correctly-allocated month renders `✓` on every day and the registered work time is
            // invisible — on the one surface whose purpose is to show it.
            showWorkedHours
          />
        </div>
      )}
    </div>
  )
}

/** A leader cannot edit an employee's registrations from this surface. Required by the grid's prop
    contract; unreachable under `readOnly`, and inert if that ever regresses. */
const NO_EDITS = () => {}
