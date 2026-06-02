import type { EntitlementInfo } from '../hooks/useBalanceSummary'
import type { AccrualSeriesEntitlement } from '../hooks/useAccrualSeries'
import { formatDanishNumber } from '../lib/locale'
import styles from './LeaveOverview.module.css'

interface LeaveOverviewProps {
  entitlements: EntitlementInfo[] | undefined
  /** Accrual series, used to derive the ferieår label for accrual types. */
  series: AccrualSeriesEntitlement[] | undefined
  loading: boolean
}

/**
 * Entitlement types whose primary framing is "optjent {earned} af {totalQuota}"
 * (earned-of-annual accrual) and whose year is a ferieår, not a calendar year.
 */
const ACCRUAL_TYPES = new Set(['VACATION', 'SPECIAL_HOLIDAY'])

/** Danish ferieår label, e.g. ferieaarStart `2025-09-01` -> "Ferieår 2025/26". */
function ferieaarLabel(ferieaarStart: string | undefined): string | null {
  if (!ferieaarStart || ferieaarStart.length < 4) return null
  const startYear = Number(ferieaarStart.slice(0, 4))
  if (Number.isNaN(startYear)) return null
  const endYY = (startYear + 1) % 100
  return `Ferieår ${startYear}/${String(endYY).padStart(2, '0')}`
}

interface Stat {
  label: string
  value: string
}

function StatRow({ stats }: { stats: Stat[] }) {
  return (
    <dl className={styles.stats}>
      {stats.map((s) => (
        <div key={s.label} className={styles.stat}>
          <dt className={styles.statLabel}>{s.label}</dt>
          <dd className={styles.statValue}>{s.value}</dd>
        </div>
      ))}
    </dl>
  )
}

function EntitlementBlock({
  entitlement,
  seriesByType,
}: {
  entitlement: EntitlementInfo
  seriesByType: Map<string, AccrualSeriesEntitlement>
}) {
  const isAccrual = ACCRUAL_TYPES.has(entitlement.type)
  const seriesEntry = seriesByType.get(entitlement.type)

  // Year label: ferieår for accrual types (prefer the series ferieaarStart),
  // calendar entitlementYear for the rest.
  const yearLabel = isAccrual
    ? ferieaarLabel(seriesEntry?.ferieaarStart) ?? `${entitlement.entitlementYear}`
    : `${entitlement.entitlementYear}`

  // Rest is displayed VERBATIM from the server — never recomputed.
  const rest = formatDanishNumber(entitlement.remaining)

  const detailStats: Stat[] = [
    { label: 'Brugt', value: `${formatDanishNumber(entitlement.used)} dage` },
    { label: 'Planlagt', value: `${formatDanishNumber(entitlement.planned)} dage` },
    { label: 'Overført', value: `${formatDanishNumber(entitlement.carryoverIn)} dage` },
    { label: 'Årlig kvote', value: `${formatDanishNumber(entitlement.totalQuota)} dage` },
  ]

  // Earned-of-annual progress for accrual types (NOT used/total).
  const earnedPct =
    entitlement.totalQuota > 0
      ? Math.max(0, Math.min(100, (entitlement.earned / entitlement.totalQuota) * 100))
      : 0
  // Used-of-total progress for the non-accrual (calendar-quota) types.
  const usedPct =
    entitlement.totalQuota > 0
      ? Math.max(0, Math.min(100, (entitlement.used / entitlement.totalQuota) * 100))
      : 0

  return (
    <div className={styles.block}>
      <div className={styles.blockHead}>
        <span className={styles.blockTitle}>{entitlement.label}</span>
        <span className={styles.blockYear}>{yearLabel}</span>
      </div>

      {isAccrual ? (
        <>
          <p className={styles.primary}>
            optjent {formatDanishNumber(entitlement.earned)} af {formatDanishNumber(entitlement.totalQuota)} dage
          </p>
          <div className={styles.progressBar} aria-hidden="true">
            <div className={styles.progressFill} style={{ width: `${earnedPct}%` }} />
          </div>
        </>
      ) : (
        <>
          <p className={styles.primary}>
            {formatDanishNumber(entitlement.used)} af {formatDanishNumber(entitlement.totalQuota)} dage brugt
          </p>
          <div className={styles.progressBar} aria-hidden="true">
            <div className={styles.progressFill} style={{ width: `${usedPct}%` }} />
          </div>
        </>
      )}

      <p className={styles.rest}>
        <span className={styles.restLabel}>Rest</span>
        <span className={styles.restValue}>{rest} dage</span>
      </p>

      <StatRow stats={detailStats} />
    </div>
  )
}

export function LeaveOverview({ entitlements, series, loading }: LeaveOverviewProps) {
  const seriesByType = new Map<string, AccrualSeriesEntitlement>(
    (series ?? []).map((s) => [s.type, s])
  )

  if (loading && !entitlements) {
    return <p className={styles.empty}>Indlæser ferie & fravær…</p>
  }

  const items = entitlements ?? []
  if (items.length === 0) {
    return <p className={styles.empty}>Ingen ferie- eller fraværsrettigheder registreret.</p>
  }

  return (
    <div className={styles.container}>
      {items.map((e) => (
        <EntitlementBlock key={e.type} entitlement={e} seriesByType={seriesByType} />
      ))}
    </div>
  )
}
