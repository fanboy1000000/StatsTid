import { useMemo } from 'react'
import { useSkema } from '../../hooks/useSkema'
import { useBalanceSummary } from '../../hooks/useBalanceSummary'
import { SkemaGrid } from '../../components/SkemaGrid'
import { BalanceSummary } from '../../components/BalanceSummary'
import { Spinner } from '../../components/ui/Spinner'
import { Alert } from '../../components/ui/Alert'
import type { SkemaRow } from '../../types'
import styles from './ApprovalDashboard.module.css'

interface ApprovalDetailPanelProps {
  period: {
    periodId: string
    employeeId: string
    periodStart: string
    periodEnd: string
  }
}

export function ApprovalDetailPanel({ period }: ApprovalDetailPanelProps) {
  const startDate = new Date(period.periodStart)
  const year = startDate.getFullYear()
  const month = startDate.getMonth() + 1

  const { data, loading, error } = useSkema(period.employeeId, year, month)
  const { data: balanceData, loading: balanceLoading } = useBalanceSummary(period.employeeId, year, month)

  const rows: SkemaRow[] = useMemo(() => {
    if (!data) return []
    const projectRows: SkemaRow[] = (data.projects ?? []).map(p => ({
      type: 'project' as const,
      key: p.projectCode,
      label: p.projectName,
    }))
    const absenceRows: SkemaRow[] = (data.absenceTypes ?? []).map(a => ({
      type: 'absence' as const,
      key: a.type,
      label: a.label,
    }))
    return [...projectRows, ...absenceRows]
  }, [data])

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

  const filteredRows = useMemo(() => {
    const keys = [...cells.keys()]
    return rows.filter(row => keys.some(k => k.startsWith(row.key + ':')))
  }, [rows, cells])

  if (loading && !data) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.detailLoading}>
          <Spinner size="sm" />
          Indlaeser registreringer...
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

  if (filteredRows.length === 0) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.detailEmpty}>Ingen registreringer</div>
      </div>
    )
  }

  return (
    <div className={styles.detailPanel}>
      <BalanceSummary data={balanceData} loading={balanceLoading} />
      <div className={styles.detailGrid}>
        <SkemaGrid
          year={year}
          month={month}
          rows={filteredRows}
          cellValues={cells}
          readOnly={true}
          onCellChange={() => {}}
        />
      </div>
    </div>
  )
}
