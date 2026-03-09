import type { BalanceSummary as BalanceSummaryData, EntitlementInfo } from '../hooks/useBalanceSummary'
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

function EntitlementCard({ entitlement }: { entitlement: EntitlementInfo }) {
  const pct = entitlement.totalQuota > 0
    ? Math.min(100, (entitlement.used / entitlement.totalQuota) * 100)
    : 0

  return (
    <div className={styles.card}>
      <p className={styles.label}>{entitlement.label}</p>
      <p className={styles.value}>
        {entitlement.used} / {entitlement.totalQuota} dage
      </p>
      <div className={styles.progressBar}>
        <div className={styles.progressFill} style={{ width: `${pct}%` }} />
      </div>
      {entitlement.carryoverIn > 0 && (
        <p className={styles.entitlementDetail}>
          (heraf {entitlement.carryoverIn} overført)
        </p>
      )}
    </div>
  )
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

  const vacationEntitlement = data.entitlements?.find(e => e.type === 'VACATION')
  const vacationUsed = vacationEntitlement ? vacationEntitlement.used : data.vacationDaysUsed
  const vacationTotal = vacationEntitlement ? vacationEntitlement.totalQuota : data.vacationDaysEntitlement
  const vacationCarryover = vacationEntitlement?.carryoverIn ?? 0
  const vacationPct = vacationTotal > 0 ? Math.min(100, (vacationUsed / vacationTotal) * 100) : 0

  const additionalEntitlements = data.entitlements
    ?.filter(e => e.type !== 'VACATION' && e.totalQuota > 0) ?? []

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
          {vacationUsed} / {vacationTotal} dage
        </p>
        {vacationEntitlement && (
          <div className={styles.progressBar}>
            <div className={styles.progressFill} style={{ width: `${vacationPct}%` }} />
          </div>
        )}
        {vacationCarryover > 0 && (
          <p className={styles.entitlementDetail}>
            (heraf {vacationCarryover} overført)
          </p>
        )}
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

      {additionalEntitlements.map(entitlement => (
        <EntitlementCard key={entitlement.type} entitlement={entitlement} />
      ))}
    </div>
  )
}
