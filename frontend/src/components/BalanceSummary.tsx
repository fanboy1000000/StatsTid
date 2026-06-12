// S72 / TASK-7205 — the 4-card balance strip (design_handoff_skema README §2 +
// solA.jsx BalancePanel). EXACTLY four cards — Flex saldo / Ferie / Særlige
// feriedage / Omsorgsdage — per owner ruling D-A (HOURS-FIRST display over the
// day-denominated authoritative record). The old Normtimer / Merarbejde /
// dynamic additional-entitlement cards are REMOVED (SPRINT-72 R10 — recorded
// information relocation; those balances remain on Årsoversigt).
//
// Sourcing is HYBRID (R10 / Reviewer W3): the headline saldi come from
// `/summary` (the `data` prop); everything month-scoped — the Flex card's
// "Denne måned", the norm scalar, and the "Afholdt i <måned>" usage — arrives
// as props the PAGE derives from the month GET (one source; the Flex value uses
// the R2 arithmetic so it reconciles with the grid's Diff total by
// construction). This component performs NO fetching and NO norm math beyond
// the D-A display conversion days × fullDayNormAtMonthEnd.
//
// D-A fail-soft pin: when `fullDayNormAtMonthEnd` is null (no dated profile /
// ANNUAL_ACTIVITY) the hours headline renders an em-dash while the DAYS value
// still shows — the cards never invent a norm client-side (Step-0b B1).
import type { BalanceSummary as BalanceSummaryData } from '../hooks/useBalanceSummary'
import type { MonthAbsenceUsage } from '../hooks/useBalanceSummary'
import { DANISH_MONTHS, formatDanishNumber } from '../lib/locale'
import styles from './BalanceSummary.module.css'

interface BalanceSummaryProps {
  /** `/summary` — the headline saldi (R10 HYBRID). */
  data: BalanceSummaryData | null
  loading: boolean
  /** The viewed month (1–12) — names the "Afholdt i <måned>" sub-line. */
  month: number
  /** Month-GET-derived flex delta (R2 arithmetic — equals the grid's Diff
      total by construction). Null while month data is unavailable. */
  monthFlexDelta: number | null
  /** The served D-A norm scalar (month GET). Null → hours headline em-dash. */
  fullDayNormAtMonthEnd: number | null
  /** Per-absence-type served month usage (hours Σ; days Σ skipping null
      feriedage rows — R10). */
  monthAbsenceUsage: ReadonlyMap<string, MonthAbsenceUsage>
}

const EM_DASH = '—'

/** Signed 1-decimal Danish hours value ("+4,2" / "-0,1" / "0,0") — matches the
    grid Diff row's formatSignedDiff so the R2 reconciliation is textual too. */
function formatSigned1(v: number): string {
  const s = v.toFixed(1).replace('.', ',')
  return v > 0 ? `+${s}` : s
}

function round1(n: number): number {
  return Math.round(n * 10) / 10
}

const NO_USAGE: MonthAbsenceUsage = { hours: 0, days: 0 }

interface DayCardProps {
  label: string
  /** Available days from `/summary` (entitlement.remaining); null = unknown. */
  remainingDays: number | null
  norm: number | null
  usage: MonthAbsenceUsage
  monthName: string
}

/** A D-A hours-first day card: `<days × norm> t tilbage` headline, `<days> dage`
    sub, `Afholdt i <måned> <X,X> t · <Y> dage` month line. */
function DayCard({ label, remainingDays, norm, usage, monthName }: DayCardProps) {
  const hours =
    remainingDays !== null && norm !== null ? round1(remainingDays * norm) : null
  return (
    <div className={styles.card}>
      <p className={styles.label}>{label}</p>
      <p className={styles.value}>
        <span className={styles.num}>
          {hours !== null ? formatDanishNumber(hours, 1) : EM_DASH}
        </span>
        <span className={styles.unit}>t tilbage</span>
      </p>
      <p className={styles.sub}>
        {remainingDays !== null
          ? `${formatDanishNumber(remainingDays, 1)} dage`
          : `${EM_DASH} dage`}
      </p>
      <p className={styles.month}>
        Afholdt i {monthName} <b>{formatDanishNumber(usage.hours, 1)} t</b>
        {' · '}
        {formatDanishNumber(usage.days, 1)} dage
      </p>
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

export function BalanceSummary({
  data,
  loading,
  month,
  monthFlexDelta,
  fullDayNormAtMonthEnd,
  monthAbsenceUsage,
}: BalanceSummaryProps) {
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

  const monthName = DANISH_MONTHS[month - 1].toLowerCase()

  const remainingFor = (type: string): number | null => {
    const entitlement = data.entitlements?.find((e) => e.type === type)
    if (entitlement) return entitlement.remaining
    // Legacy `/summary` shapes (pre-entitlements) carried vacation as two flat
    // fields — keep the Ferie card alive on them; the other types fail soft.
    if (type === 'VACATION') {
      return data.vacationDaysEntitlement - data.vacationDaysUsed
    }
    return null
  }

  return (
    <div className={styles.container}>
      {/* 1 — Flex saldo: headline from /summary; sub-line LEFT = the month-GET-
          derived delta (R10 HYBRID, R2); sub-line RIGHT = the D-A norm scalar. */}
      <div className={styles.card}>
        <p className={styles.label}>Flex saldo</p>
        <p className={styles.value}>
          <span
            className={`${styles.num} ${data.flexBalance >= 0 ? styles.pos : styles.neg}`}
          >
            {formatSigned1(data.flexBalance)}
          </span>
          <span className={styles.unit}>t</span>
        </p>
        <p className={`${styles.month} ${styles.monthRow}`}>
          <span>
            Denne måned{' '}
            <b
              className={
                monthFlexDelta !== null && monthFlexDelta < 0 ? styles.neg : styles.pos
              }
            >
              {monthFlexDelta !== null ? `${formatSigned1(monthFlexDelta)} t` : EM_DASH}
            </b>
          </span>
          <span className={styles.norm}>
            Norm{' '}
            <b>
              {fullDayNormAtMonthEnd !== null
                ? `${formatDanishNumber(fullDayNormAtMonthEnd, 1)} t`
                : EM_DASH}
            </b>
          </span>
        </p>
      </div>

      {/* 2–4 — the D-A hours-first day cards */}
      <DayCard
        label="Ferie"
        remainingDays={remainingFor('VACATION')}
        norm={fullDayNormAtMonthEnd}
        usage={monthAbsenceUsage.get('VACATION') ?? NO_USAGE}
        monthName={monthName}
      />
      <DayCard
        label="Særlige feriedage"
        remainingDays={remainingFor('SPECIAL_HOLIDAY')}
        norm={fullDayNormAtMonthEnd}
        usage={monthAbsenceUsage.get('SPECIAL_HOLIDAY') ?? NO_USAGE}
        monthName={monthName}
      />
      <DayCard
        label="Omsorgsdage"
        remainingDays={remainingFor('CARE_DAY')}
        norm={fullDayNormAtMonthEnd}
        usage={monthAbsenceUsage.get('CARE_DAY') ?? NO_USAGE}
        monthName={monthName}
      />
    </div>
  )
}
