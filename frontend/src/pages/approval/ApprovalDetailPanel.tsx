// S72 / TASK-7205 — the approval (leader) surface adapted to the redesigned grid
// (SPRINT-72 R12, Step-0b B5). Pinned contract: leaders see the redesigned grid
// READ-ONLY with ALL rows visible (NO rowPreferences prop — the 7202 fallback
// renders every row it is given; a leader reviews the full record, and R3's
// all-data arithmetic makes that consistent), the interactive row rendered as
// DATA (no onOpenDay → no panel trigger), and NO manager-modal access (no
// onOpenManager). S72 Step-7a B1: "all rows" = deriveSkemaRowBasis's UNION of
// the served catalogs ∪ entry/absence keys — the legacy `projects` field is
// the employee's VISIBLE selection for configured users (7201) and would hide
// preference-hidden / deactivated rows from the review surface. The balance
// strip receives the same month-GET-derived values as the employee page,
// computed over the SERVED data via the shared pure helpers (one computation
// owner — useSkema's computeDayDiffs via useBalanceSummary).
import { useMemo } from 'react'
import { useSkema, deriveSkemaRowBasis, type SkemaRowBasis } from '../../hooks/useSkema'
import {
  useBalanceSummary,
  computeMonthFlexDelta,
  deriveMonthAbsenceUsage,
} from '../../hooks/useBalanceSummary'
import {
  SkemaGrid,
  type WorkIntervalsMap,
  type ManualHoursMap,
  type DailyNormMap,
} from '../../components/SkemaGrid'
import { BalanceSummary } from '../../components/BalanceSummary'
import { Spinner } from '../../components/ui/Spinner'
import { Alert } from '../../components/ui/Alert'
import styles from './ApprovalDashboard.module.css'

interface ApprovalDetailPanelProps {
  period: {
    periodId: string
    employeeId: string
    periodStart: string
    periodEnd: string
  }
}

function noop() {}

export function ApprovalDetailPanel({ period }: ApprovalDetailPanelProps) {
  const startDate = new Date(period.periodStart)
  const year = startDate.getFullYear()
  const month = startDate.getMonth() + 1

  const { data, loading, error } = useSkema(period.employeeId, year, month)
  const { data: balanceData, loading: balanceLoading } = useBalanceSummary(period.employeeId, year, month)

  // ALL rows — R12 + S72 Step-7a B1: the UNION basis (`catalogs` ∪ keys in the
  // served entries/absences). The legacy `projects`/`absenceTypes` fields are
  // the employee's VISIBLE selection for configured users (7201), and a leader
  // reviews the FULL record — including preference-hidden rows and deactivated
  // projects with historical hours (labeled by their code). The grid gets no
  // rowPreferences here, so it renders every basis row.
  const rowBasis = useMemo<SkemaRowBasis>(
    () =>
      data
        ? deriveSkemaRowBasis(data)
        : { rows: [], projectKeys: new Set<string>(), absenceKeys: new Set<string>() },
    [data],
  )
  const rows = rowBasis.rows

  const cells = useMemo(() => {
    if (!data) return new Map<string, number>()
    const m = new Map<string, number>()
    for (const entry of data.entries) {
      if (entry.hours !== 0) m.set(`${entry.projectCode}:${entry.date}`, entry.hours)
    }
    for (const absence of data.absences) {
      if (absence.hours !== 0) m.set(`${absence.absenceType}:${absence.date}`, absence.hours)
    }
    return m
  }, [data])

  // Served workTime + norms — the redesigned grid's Diff / Arbejdstid rows need
  // them on the review surface too (the full record).
  const workIntervals = useMemo<WorkIntervalsMap>(() => {
    const map: WorkIntervalsMap = new Map()
    for (const wt of data?.workTime ?? []) {
      if (wt.intervals && wt.intervals.length > 0) {
        map.set(wt.date, wt.intervals.map(iv => ({ start: iv.start, end: iv.end })))
      }
    }
    return map
  }, [data])

  const manualHours = useMemo<ManualHoursMap>(() => {
    const map: ManualHoursMap = new Map()
    for (const wt of data?.workTime ?? []) {
      if (wt.manualHours && wt.manualHours !== 0) {
        map.set(wt.date, wt.manualHours)
      }
    }
    return map
  }, [data])

  const dailyNorm = useMemo<DailyNormMap>(() => {
    const map: DailyNormMap = new Map()
    for (const dn of data?.dailyNorm ?? []) {
      map.set(dn.date, dn.hours)
    }
    return map
  }, [data])

  // Balance-strip month derivations over the SERVED data (the shared R10/R2
  // helpers — same computation owner as the employee page).
  const monthFlexDelta = useMemo<number | null>(() => {
    if (!data) return null
    return computeMonthFlexDelta({
      year,
      month,
      cellValues: cells,
      // B1: the union basis keys — visibility-independent (R3)
      projectKeys: rowBasis.projectKeys,
      absenceKeys: rowBasis.absenceKeys,
      workIntervals,
      manualHours,
      dailyNorm,
    })
  }, [data, year, month, cells, rowBasis, workIntervals, manualHours, dailyNorm])

  const monthAbsenceUsage = useMemo(() => deriveMonthAbsenceUsage(data?.absences), [data])

  if (loading && !data) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.detailLoading}>
          <Spinner size="sm" />
          Indlæser registreringer...
        </div>
      </div>
    )
  }

  if (error && !data) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.detailError}>
          <Alert variant="error">{error}</Alert>
        </div>
      </div>
    )
  }

  const hasRegistrations =
    !!data &&
    (data.entries.length > 0 || data.absences.length > 0 || (data.workTime ?? []).length > 0)

  if (!hasRegistrations) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.detailEmpty}>Ingen registreringer</div>
      </div>
    )
  }

  return (
    <div className={styles.detailPanel}>
      <BalanceSummary
        data={balanceData}
        loading={balanceLoading}
        month={month}
        monthFlexDelta={monthFlexDelta}
        fullDayNormAtMonthEnd={data?.fullDayNormAtMonthEnd ?? null}
        monthAbsenceUsage={monthAbsenceUsage}
      />
      <div className={styles.detailGrid}>
        {/* R12: readOnly, NO rowPreferences (all served rows render), NO
            onOpenDay (interactive row = data), NO onOpenManager (no modal). */}
        <SkemaGrid
          year={year}
          month={month}
          rows={rows}
          cellValues={cells}
          readOnly={true}
          onCellChange={noop}
          workIntervals={workIntervals}
          manualHours={manualHours}
          dailyNorm={dailyNorm}
        />
      </div>
    </div>
  )
}
