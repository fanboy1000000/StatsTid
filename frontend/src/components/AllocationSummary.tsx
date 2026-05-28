import { formatDanishNumber } from '../lib/locale'
import { unallocated } from '../lib/allocation'
import styles from './AllocationSummary.module.css'

export interface AllocationProjectBreakdown {
  projectCode: string
  projectName: string
  hours: number
}

interface AllocationSummaryProps {
  workedHours: number
  allocatedHours: number
  projects: AllocationProjectBreakdown[]
}

/**
 * "Fordeling af arbejdstid" — monthly summary of how registered work time is
 * distributed across projects, with a progress bar (allocated-of-worked %),
 * a per-project breakdown, and an "Ikke fordelt" line.
 */
export function AllocationSummary({ workedHours, allocatedHours, projects }: AllocationSummaryProps) {
  if (workedHours <= 0 && allocatedHours <= 0) {
    return null
  }

  const pct = workedHours > 0 ? Math.min(100, (allocatedHours / workedHours) * 100) : 0
  const remaining = unallocated(workedHours, allocatedHours)

  return (
    <div className={styles.card}>
      <div className={styles.header}>
        <p className={styles.title}>Fordeling af arbejdstid</p>
        <span className={styles.summaryValue}>
          {formatDanishNumber(allocatedHours, 1)} / {formatDanishNumber(workedHours, 1)} t
        </span>
      </div>

      <div className={styles.progressBar}>
        <div className={styles.progressFill} style={{ width: `${pct}%` }} />
      </div>

      <ul className={styles.breakdown}>
        {projects
          .filter(p => p.hours > 0)
          .map(p => {
            const projPct = workedHours > 0 ? (p.hours / workedHours) * 100 : 0
            return (
              <li key={p.projectCode} className={styles.row}>
                <span className={styles.projectName}>{p.projectName}</span>
                <span className={styles.projectHours}>
                  {formatDanishNumber(p.hours, 1)} t
                  <span className={styles.projectPct}>({formatDanishNumber(projPct, 0)}%)</span>
                </span>
              </li>
            )
          })}
        <li className={`${styles.row} ${styles.unallocatedRow}`}>
          <span className={styles.projectName}>Ikke fordelt</span>
          <span className={styles.projectHours}>{formatDanishNumber(remaining, 1)} t</span>
        </li>
      </ul>
    </div>
  )
}
