// S72 / TASK-7203 — the Skema day panel (design_handoff_skema README §4, prototype
// solA.jsx DayPanel) rendered inside the kit Drawer. The panel is fully CONTROLLED
// (SPRINT-72 R16): all data arrives via props and every mutation leaves via a
// callback — NO fetching, NO save plumbing (the page owns day-state + the debounced
// save; the useSkema module owns the payload builder and the R7 manualHours pin).
//
// Pinned rules implemented here:
//   R5  — the "Alt fordelt ✓" Resterende mirror uses the backend allocation gate's
//         EXACT tolerance via lib/allocation (round-2dp, |Δ| < 0.005). The handoff's
//         0,1-rounding is DISPLAY-only: per-row "= X,X t", Arbejdstid i alt and the
//         Resterende value render at 1 decimal while ALL comparisons stay exact.
//   R6  — BOTH warning triggers, WARNING-ONLY (no VoluntaryUnsocialHours toggle —
//         recorded follow-up): (i) worked > 9 t → the amber merarbejde/hvile Alert
//         in the meta area; (ii) the §J adjacent-day 11-hour analysis over the
//         served month + boundary workTime (pure analyzeRestPeriods), evaluated
//         LIVE against the panel's current periods.
//   R11 — step 2 DISABLED until step 1 has hours; Færdig closes; "ugyldig" inline
//         on invalid/reversed/overlapping periods; NO absence section, NO
//         clock-in/out; the step-2 header carries the "Administrer projekter" link
//         (exposed as onOpenManager — wired by 7205).
//   D-B — the manual lump-hours ENTRY UI is dropped; an existing manualHours value
//         renders read-only ("Manuelt registreret: X t") and keeps counting in the
//         day's worked total.
//   S58 — the relocated validation mirrors (7202 deleted the old dialog): overlap
//         flagging + the 24h day cap, ADVISORY only — the backend 422 stays
//         authoritative (R5/R17: the save contract is frozen).
import { useMemo, useState } from 'react'
import { Drawer } from './ui/Drawer'
import { Alert } from './ui/Alert'
import { Button } from './ui/Button'
import { Input } from './ui/Input'
import {
  analyzeRestPeriods,
  dayWorkedHours,
  overlappingPeriodIndices,
  periodHours,
  LONG_DAY_WARNING_HOURS,
  MAX_DAY_HOURS,
  type ClockPeriod,
} from '../hooks/useSkema'
import { classifyAllocation, unallocated } from '../lib/allocation'
import { DANISH_MONTHS, formatDanishNumber, parseDanishNumber } from '../lib/locale'
import type { WorkTimeDay } from '../types'
import styles from './SkemaDayPanel.module.css'

/** One step-1 period row — raw typed text (the id is a stable React key only). */
export interface DayPanelPeriod extends ClockPeriod {
  id: string
  from: string
  to: string
}

/** One VISIBLE project row for step 2 (key = projectCode), in preference order. */
export interface DayPanelProjectRow {
  key: string
  label: string
}

interface SkemaDayPanelProps {
  open: boolean
  /** The day, "YYYY-MM-DD". */
  date: string
  /** The day's clock periods (step 1), raw text. Empty → one blank row renders. */
  periods: DayPanelPeriod[]
  /** The day's EXISTING manual hours (D-B: read-only line, no entry UI). */
  manualHours: number
  /** The VISIBLE project rows (R4 preference order) — step 2 renders one FIXED
      row per entry (no add/remove; membership is the manager modal's job). */
  projectRows: DayPanelProjectRow[]
  /** The day's per-project allocated hours over ALL SERVED projects — not just
      the visible rows (R3: the Resterende indicator must agree with the grid's
      ✓/amber state, which computes over all served data). */
  allocations: ReadonlyMap<string, number>
  /** The day's SERVED norm (null = academic ANNUAL_ACTIVITY → blank, R1). */
  dailyNorm: number | null
  /** The month's served workTime — adjacent-day input for the R6 analysis. */
  monthWorkTime: readonly WorkTimeDay[]
  /** The served 0..2 boundary days (prev-month last / next-month first) — R6. */
  boundaryWorkTime?: readonly WorkTimeDay[]
  onPeriodsChange: (date: string, periods: DayPanelPeriod[]) => void
  /** hours: parsed exact value; null = cleared (the save path drops null/0 —
      recorded inherited limitation, R17). */
  onAllocationChange: (date: string, projectKey: string, hours: number | null) => void
  onClose: () => void
  /** The step-2 header's "Administrer projekter" link (wired by 7205). */
  onOpenManager: () => void
}

const DA_DAYS_LONG = ['søndag', 'mandag', 'tirsdag', 'onsdag', 'torsdag', 'fredag', 'lørdag']

/** "mandag 9. marts" — built from Danish arrays (deterministic across ICU). */
function formatLongDanishDate(date: string): string {
  const d = new Date(date + 'T00:00:00')
  return `${DA_DAYS_LONG[d.getDay()]} ${d.getDate()}. ${DANISH_MONTHS[d.getMonth()].toLowerCase()}`
}

/** Signed 1-decimal Danish hours for the meta diff ("+0,6" / "-3,4" / "0,0"). */
function formatSignedHours1(v: number): string {
  const s = v.toFixed(1).replace('.', ',')
  return v > 0 ? `+${s}` : s
}

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

let nextPeriodId = 1
function mkPeriod(): DayPanelPeriod {
  return { id: `p${nextPeriodId++}`, from: '', to: '' }
}

function IconClose() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d="M4 4L12 12M12 4L4 12" stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
    </svg>
  )
}

function IconTrash() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path
        d="M3 4H13M6 4V2.5H10V4M5 4L5.5 13.5H10.5L11 4"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="square"
      />
    </svg>
  )
}

export function SkemaDayPanel({
  open,
  date,
  periods,
  manualHours,
  projectRows,
  allocations,
  dailyNorm,
  monthWorkTime,
  boundaryWorkTime,
  onPeriodsChange,
  onAllocationChange,
  onClose,
  onOpenManager,
}: SkemaDayPanelProps) {
  const dateLabel = formatLongDanishDate(date)

  // Empty day → render one blank period row (prototype behavior). The synthetic
  // row only enters the page's state once the user edits it; a new day gets a
  // fresh row (the memo keys on `date`).
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const emptyRow = useMemo(() => mkPeriod(), [date])
  const list = useMemo(
    () => (periods.length > 0 ? periods : [emptyRow]),
    [periods, emptyRow],
  )

  const setPeriod = (index: number, patch: Partial<ClockPeriod>) =>
    onPeriodsChange(date, list.map((p, i) => (i === index ? { ...p, ...patch } : p)))
  const addPeriod = () => onPeriodsChange(date, [...list, mkPeriod()])
  const removePeriod = (index: number) =>
    onPeriodsChange(date, list.filter((_, i) => i !== index))

  // ── Derived (exact 2-decimal arithmetic; display rounds to 0,1 — R5) ──
  const worked = dayWorkedHours(list, manualHours)
  const overlapping = overlappingPeriodIndices(list)

  // Allocated = Σ over ALL served allocations for the day (R3 — includes
  // projects hidden from the visible rows, so the panel agrees with the grid).
  let allocatedSum = 0
  for (const v of allocations.values()) allocatedSum += v
  const allocationState = classifyAllocation(worked, allocatedSum)
  const remaining = unallocated(worked, allocatedSum)

  const diffNorm = dailyNorm !== null ? round2(worked - dailyNorm) : null

  // R6 (i): intra-day long-day trigger.
  const longDay = worked > LONG_DAY_WARNING_HOURS

  // R6 (ii): adjacent-day rest analysis, LIVE — overlay the panel's CURRENT
  // periods onto the served month before running the pure helper.
  const restWarnings = useMemo(() => {
    const overlayDay: WorkTimeDay = {
      date,
      intervals: list.map((p) => ({ start: p.from, end: p.to })),
      manualHours,
    }
    const overlay = [...monthWorkTime.filter((wt) => wt.date !== date), overlayDay]
    return analyzeRestPeriods(overlay, boundaryWorkTime, date)
  }, [monthWorkTime, boundaryWorkTime, date, list, manualHours])

  // S58 advisory mirrors (backend 422 authoritative).
  const hasOverlap = overlapping.size > 0
  const over24 = worked > MAX_DAY_HOURS

  const stepTwoDisabled = worked <= 0

  // ── Step-2 raw-text editing overlay (decimal comma survives mid-typing;
  // committed values live in the page's state via onAllocationChange) ──
  const [editingAlloc, setEditingAlloc] = useState<{ key: string; raw: string } | null>(null)

  const allocDisplay = (key: string): string => {
    if (editingAlloc && editingAlloc.key === key) return editingAlloc.raw
    const v = allocations.get(key)
    return v != null && v !== 0 ? formatDanishNumber(v, 1) : ''
  }

  const handleAllocInput = (key: string, raw: string) => {
    setEditingAlloc({ key, raw })
    const num = raw === '' ? null : parseDanishNumber(raw)
    if (num !== null && isNaN(num)) return
    onAllocationChange(date, key, num === 0 ? null : num)
  }

  return (
    <Drawer open={open} onClose={onClose} ariaLabel={`Registrér tid — ${dateLabel}`}>
      <div className={styles.head}>
        <div>
          <p className={styles.eyebrow}>Registrér tid</p>
          <h3 className={styles.title}>{dateLabel}</h3>
        </div>
        <button type="button" className={styles.close} onClick={onClose} aria-label="Luk">
          <IconClose />
        </button>
      </div>

      <div className={styles.meta}>
        <span>Diff. fra normtid</span>
        <span
          className={`${styles.metaValue} ${
            diffNorm !== null && diffNorm > 0
              ? styles.diffPos
              : diffNorm !== null && diffNorm < 0
                ? styles.diffNeg
                : ''
          }`}
        >
          {worked > 0 && diffNorm !== null ? `${formatSignedHours1(diffNorm)} t` : '—'}
        </span>
        <span className={styles.metaHint}>
          norm {dailyNorm !== null ? `${formatDanishNumber(dailyNorm, 1)} t` : '—'}
        </span>
      </div>

      <div className={styles.body}>
        {/* R6 warnings — the panel meta area (both advisory; rule engine definitive) */}
        {(longDay || restWarnings) && (
          <div className={styles.warnings}>
            {longDay && (
              <Alert variant="warning">
                Mere end 9 timers arbejde på én dag kan udløse merarbejde. Husk 11 timers hvile
                til næste vagt.
              </Alert>
            )}
            {restWarnings?.map((w) => (
              <Alert key={`${w.fromDate}-${w.toDate}`} variant="warning">
                Denne registrering giver kun {formatDanishNumber(w.gapHours, 1)} timers hvile
                mellem {formatLongDanishDate(w.fromDate)} og {formatLongDanishDate(w.toDate)}.
                Hvile under 11 timer registreres som et hviletidsbrud, der er synligt for din
                leder og kan udløse kompenserende hvile.
              </Alert>
            ))}
          </div>
        )}

        {/* ── Step 1 — Registrér arbejdsperioder ── */}
        <div className={styles.section}>
          <p className={styles.sectionTitle}>
            <span className={styles.step}>1</span> Registrér arbejdsperioder
          </p>
          <div className={styles.form}>
            {list.map((p, i) => {
              const h = periodHours(p)
              const typed = p.from !== '' || p.to !== ''
              const invalid = (typed && h === null) || overlapping.has(i)
              return (
                <div className={styles.periodRow} key={p.id}>
                  <Input
                    id={`period-${p.id}-fra`}
                    className={styles.timeField}
                    value={p.from}
                    placeholder="10:27"
                    inputMode="numeric"
                    error={invalid}
                    onChange={(e) => setPeriod(i, { from: e.target.value })}
                    aria-label="Fra"
                  />
                  <span className={styles.dash} aria-hidden="true">
                    –
                  </span>
                  <Input
                    id={`period-${p.id}-til`}
                    className={styles.timeField}
                    value={p.to}
                    placeholder="13:03"
                    inputMode="numeric"
                    error={invalid}
                    onChange={(e) => setPeriod(i, { to: e.target.value })}
                    aria-label="Til"
                  />
                  <span className={`${styles.periodResult} ${invalid ? styles.periodResultBad : ''}`}>
                    {invalid ? 'ugyldig' : h !== null ? `= ${formatDanishNumber(h, 1)} t` : ''}
                  </span>
                  {list.length > 1 && (
                    <button
                      type="button"
                      className={styles.iconButton}
                      onClick={() => removePeriod(i)}
                      aria-label="Fjern periode"
                    >
                      <IconTrash />
                    </button>
                  )}
                </div>
              )
            })}
            <button type="button" className={styles.addRow} onClick={addPeriod}>
              + Tilføj periode
            </button>
            {manualHours > 0 && (
              <p className={styles.manualLine}>
                Manuelt registreret: {formatDanishNumber(manualHours, 1)} t
              </p>
            )}
            <div className={styles.workTotal}>
              <span>Arbejdstid i alt</span>
              <span className={styles.strong}>{formatDanishNumber(worked, 1)} t</span>
            </div>
            {hasOverlap && <p className={styles.advisory}>Arbejdsperioderne overlapper hinanden.</p>}
            {over24 && (
              <p className={styles.advisory}>
                Arbejdstid må ikke overstige 24 timer (i alt {formatDanishNumber(worked, 1)} t).
              </p>
            )}
          </div>
        </div>

        <div className={styles.divider} />

        {/* ── Step 2 — Fordel på projekter (dimmed/disabled until step 1 has hours) ── */}
        <div className={`${styles.section} ${stepTwoDisabled ? styles.sectionDim : ''}`}>
          <div className={styles.sectionHead}>
            <p className={styles.sectionTitle}>
              <span className={styles.step}>2</span> Fordel på projekter
            </p>
            <button type="button" className={styles.textLink} onClick={onOpenManager}>
              Administrer projekter
            </button>
          </div>
          <p className={styles.formLead}>
            Fordel dagens arbejdstid på de projekter, du har arbejdet på.
          </p>
          <div className={styles.formTight}>
            {projectRows.map((p) => (
              <div className={styles.allocRow} key={p.key}>
                <span className={styles.allocName}>{p.label}</span>
                <Input
                  id={`alloc-${p.key}`}
                  className={styles.allocField}
                  value={allocDisplay(p.key)}
                  placeholder="0"
                  inputMode="decimal"
                  disabled={stepTwoDisabled}
                  onFocus={() => setEditingAlloc({ key: p.key, raw: allocDisplay(p.key) })}
                  onChange={(e) => handleAllocInput(p.key, e.target.value)}
                  onBlur={() => setEditingAlloc(null)}
                  aria-label={p.label}
                />
                <span className={styles.unit}>t</span>
              </div>
            ))}
            <div
              className={`${styles.remaining} ${
                allocationState === 'balanced'
                  ? styles.remainingOk
                  : allocationState === 'under'
                    ? styles.remainingLeft
                    : styles.remainingOver
              }`}
            >
              <span>{allocationState === 'over' ? 'Overfordelt' : 'Resterende at fordele'}</span>
              <span className={styles.strong}>
                {allocationState === 'balanced'
                  ? 'Alt fordelt ✓'
                  : allocationState === 'under'
                    ? `${formatDanishNumber(remaining, 1)} t`
                    : `+${formatDanishNumber(Math.abs(remaining), 1)} t`}
              </span>
            </div>
          </div>
        </div>
      </div>

      <div className={styles.foot}>
        <Button variant="primary" onClick={onClose}>
          Færdig
        </Button>
      </div>
    </Drawer>
  )
}
