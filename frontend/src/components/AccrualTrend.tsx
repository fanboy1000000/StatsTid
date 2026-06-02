import type { AccrualSeriesEntitlement, AccrualSeriesPoint } from '../hooks/useAccrualSeries'
import { formatDanishNumber, DANISH_MONTHS } from '../lib/locale'
import styles from './AccrualTrend.module.css'

interface AccrualTrendProps {
  series: AccrualSeriesEntitlement[] | undefined
  loading: boolean
}

/** Short Danish month label (3 letters) from a `YYYY-MM-DD` month-end date. */
function shortMonth(monthEnd: string): string {
  const m = Number(monthEnd.slice(5, 7))
  const name = DANISH_MONTHS[m - 1] ?? monthEnd
  return name.slice(0, 3)
}

/**
 * One lightweight CSS bar-chart per MONTHLY_ACCRUAL entitlement (Ferie, Feriefridage).
 * x = the 12 ferieår months (labelled by month), y = cumulative `earned`.
 * The `isSelected` point ("nu") is highlighted. No charting library — plain
 * flex columns + percentage heights; AA-safe tokens; accessible (aria-label per bar).
 */
function MiniBarChart({ entitlement }: { entitlement: AccrualSeriesEntitlement }) {
  const points = entitlement.points ?? []
  // Scale to the annual quota when known, else to the max earned value so the
  // curve still reads. Guard against a zero denominator (new hire / empty).
  const maxEarned = points.reduce((m, p) => Math.max(m, p.earned), 0)
  const scaleMax = Math.max(entitlement.annualQuota, maxEarned, 0.0001)

  const ferieaarLabel = entitlement.ferieaarStart
    ? `Ferieår ${Number(entitlement.ferieaarStart.slice(0, 4))}/${(Number(entitlement.ferieaarStart.slice(0, 4)) + 1) % 100}`
    : `${entitlement.entitlementYear}`

  return (
    <div className={styles.chart}>
      <div className={styles.chartHead}>
        <span className={styles.chartTitle}>{entitlement.label}</span>
        <span className={styles.chartMeta}>{ferieaarLabel}</span>
      </div>
      {points.length === 0 ? (
        <p className={styles.empty}>Ingen optjeningsdata endnu.</p>
      ) : (
        <div
          className={styles.bars}
          role="img"
          aria-label={`Optjeningsudvikling for ${entitlement.label}, ${ferieaarLabel}. ${points
            .map((p) => `${shortMonth(p.monthEnd)}: ${formatDanishNumber(p.earned)} dage`)
            .join('. ')}`}
        >
          {points.map((p: AccrualSeriesPoint) => {
            const heightPct = Math.max(0, Math.min(100, (p.earned / scaleMax) * 100))
            return (
              <div key={p.monthEnd} className={styles.barCol} title={`${shortMonth(p.monthEnd)}: ${formatDanishNumber(p.earned)} af ${formatDanishNumber(entitlement.annualQuota)} dage`}>
                <div className={styles.barTrack}>
                  <div
                    className={`${styles.barFill} ${p.isSelected ? styles.barFillSelected : ''}`}
                    style={{ height: `${heightPct}%` }}
                  />
                </div>
                <span className={`${styles.barLabel} ${p.isSelected ? styles.barLabelSelected : ''}`}>
                  {shortMonth(p.monthEnd)}
                  {p.isSelected && <span className={styles.nowTag}> (nu)</span>}
                </span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

export function AccrualTrend({ series, loading }: AccrualTrendProps) {
  if (loading && !series) {
    return <p className={styles.empty}>Indlæser udvikling…</p>
  }
  const charts = series ?? []
  if (charts.length === 0) {
    return <p className={styles.empty}>Ingen optjeningskurver tilgængelige.</p>
  }
  return (
    <div className={styles.container}>
      {charts.map((e) => (
        <MiniBarChart key={e.type} entitlement={e} />
      ))}
    </div>
  )
}
