import type { BalanceSummary as BalanceSummaryData } from '../hooks/useBalanceSummary'
import styles from './BalanceSummary.module.css'

interface BalanceSummaryProps {
  data: BalanceSummaryData | null
  loading: boolean
}

function formatHours(hours: number): string {
  return `${hours >= 0 ? '+' : ''}${hours.toFixed(1)} t`
}

function formatDelta(delta: number): string {
  if (delta > 0) return `\u25B2 +${delta.toFixed(1)}`
  if (delta < 0) return `\u25BC ${delta.toFixed(1)}`
  return `\u25CF ${delta.toFixed(1)}`
}

function getDeltaClass(delta: number): string {
  if (delta > 0) return styles.deltaPositive
  if (delta < 0) return styles.deltaNegative
  return styles.deltaNeutral
}

function SkeletonCard() {
  return (
    <div className={styles.skeleton}>
      <div className={styles.skeletonLabel} />
      <div className={styles.skeletonValue} />
    </div>
  )
}

export function BalanceSummary({ data, loading }: BalanceSummaryProps) {
  if (loading && !data) {
    return (
      <div className={styles.container}>
        <SkeletonCard />
        <SkeletonCard />
        <SkeletonCard />
        <SkeletonCard />
      </div>
    )
  }

  if (!data) {
    return null
  }

  const overtimeLabel = data.hasMerarbejde ? 'Merarbejde' : 'Overarbejde'

  return (
    <div className={styles.container}>
      <div className={styles.card}>
        <p className={styles.label}>Flex saldo</p>
        <p className={styles.value}>{formatHours(data.flexBalance)}</p>
        <p className={`${styles.delta} ${getDeltaClass(data.flexDelta)}`}>
          {formatDelta(data.flexDelta)}
        </p>
      </div>

      <div className={styles.card}>
        <p className={styles.label}>Ferie</p>
        <p className={styles.value}>
          {data.vacationDaysUsed} / {data.vacationDaysEntitlement} dage
        </p>
      </div>

      <div className={styles.card}>
        <p className={styles.label}>Normtimer</p>
        <p className={styles.value}>
          {data.normHoursActual.toFixed(1)} / {data.normHoursExpected.toFixed(1)} t
        </p>
      </div>

      <div className={styles.card}>
        <p className={styles.label}>{overtimeLabel}</p>
        <p className={styles.value}>{data.overtimeHours.toFixed(1)} t</p>
      </div>
    </div>
  )
}
