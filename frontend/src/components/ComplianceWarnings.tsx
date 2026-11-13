import {
  COMPLIANCE_SEVERITY,
  COMPLIANCE_VIOLATION_TYPE,
  type ComplianceCheckResult,
  type ComplianceViolation,
} from '../hooks/useCompliance'
import styles from './ComplianceWarnings.module.css'

interface ComplianceWarningsProps {
  result: ComplianceCheckResult | null
  loading: boolean
  /**
   * Suppress the internal "Arbejdstidskontrol" <h3> for callers that render
   * their own section heading, so the title appears exactly once. Defaults to
   * false → SkemaPage keeps the heading here. (Original consumer was the S61
   * OversightPage, deleted in S65 — currently no caller sets this; kept as the
   * documented opt-out.)
   */
  hideTitle?: boolean
}

// S120 / TASK-12001 — THE INTEGER-ENUM REPAIR (a masked LIVE prod bug):
// `violationType`/`severity` are INTEGERS on the wire (the CLR enums serialize
// numerically; the spec declares `type: integer`), but this component compared
// them against STRINGS. On every real response:
//  - `severity === 'VIOLATION'` NEVER matched → every item (hard VIOLATIONs
//    included) rendered with the WARNING styling and the "Advarsel" badge;
//  - the string-keyed label map NEVER hit → the type column rendered the raw
//    integer (e.g. "0", "3") instead of the Danish label.
// The lists themselves still grouped correctly (violations/warnings are
// separate server arrays) and the message/date rendered — the lie showed as a
// permanently-yellow, integer-labeled panel. Comparisons now key on the
// CLR-order constants from useCompliance (spec-union-typed), and the map
// covers ALL 6 CLR violation types (the deleted union was false-exhaustive at
// 4 of 6 — OVERTIME_EXCEEDED/OVERTIME_UNAPPROVED were missing).
const VIOLATION_TYPE_LABELS: Record<number, string> = {
  [COMPLIANCE_VIOLATION_TYPE.DAILY_REST]: 'Daglig hvile',
  [COMPLIANCE_VIOLATION_TYPE.WEEKLY_REST]: 'Ugentlig hviledag',
  [COMPLIANCE_VIOLATION_TYPE.MAX_DAILY_HOURS]: 'Maks daglig arbejdstid',
  [COMPLIANCE_VIOLATION_TYPE.WEEKLY_MAX_HOURS]: 'Ugentligt timemaksimum (48t)',
  [COMPLIANCE_VIOLATION_TYPE.OVERTIME_EXCEEDED]: 'Merarbejde over det godkendte maksimum',
  [COMPLIANCE_VIOLATION_TYPE.OVERTIME_UNAPPROVED]: 'Merarbejde uden forhåndsgodkendelse',
}

/**
 * The one month-level notice for days the 11-hour rest check could not measure (days whose
 * hours are not all on the clock; a fully clocked day is checked). Returns null when the list
 * is empty or absent. Shared with Teamoversigt so both screens say exactly the same thing.
 * It stays as information, not a breach (owner ruling R-22; QUAL-234). For a manager the month's
 * first day can also be listed while the previous month is not yet sent, because the server then
 * withholds that month's last day from the rest check (S148, SEC-059).
 */
export function restNotCheckedNotice(result: ComplianceCheckResult | null | undefined): string | null {
  const n = result?.restNotCheckedDays?.length ?? 0
  if (n === 0) return null
  return `Hviletid ikke kontrolleret for ${n} ${n === 1 ? 'dag' : 'dage'}`
}

function formatDate(dateStr: string): string {
  try {
    return new Date(dateStr).toLocaleDateString('da-DK')
  } catch {
    return dateStr
  }
}

function ViolationItem({ item }: { item: ComplianceViolation }) {
  const isViolation = item.severity === COMPLIANCE_SEVERITY.VIOLATION
  return (
    <div className={`${styles.item} ${isViolation ? styles.violation : styles.warning}`}>
      <div className={styles.itemHeader}>
        <span className={`${styles.badge} ${isViolation ? styles.badgeViolation : styles.badgeWarning}`}>
          {isViolation ? 'Overtraedelse' : 'Advarsel'}
        </span>
        <span className={styles.type}>
          {VIOLATION_TYPE_LABELS[item.violationType] ?? String(item.violationType)}
        </span>
        <span className={styles.date}>{formatDate(item.date)}</span>
      </div>
      <p className={styles.message}>{item.message}</p>
    </div>
  )
}

/**
 * One polite live region: role="status" on the outermost element, none on items. It is always
 * mounted and empty while loading or when there is nothing to show, because a region that mounts
 * with its content is often not announced; the month's fetch fills it in place. While empty it is
 * taken out of the page's flow (`.region:empty`) so it adds no flex-gap space.
 */
export function ComplianceWarnings(props: ComplianceWarningsProps) {
  return <div role="status" className={styles.region}>{!props.loading && props.result && <WarningsPanel {...props} result={props.result} />}</div>
}

function WarningsPanel({
  result,
  hideTitle = false,
}: ComplianceWarningsProps & { result: ComplianceCheckResult }) {
  const hasIssues = result.violations.length > 0 || result.warnings.length > 0
  const notCheckedNotice = restNotCheckedNotice(result)
  if (!hasIssues && !notCheckedNotice) return null

  return (
    <div className={styles.container}>
      {!hideTitle && <h3 className={styles.title}>Arbejdstidskontrol</h3>}
      {result.violations.length > 0 && (
        <div className={styles.section}>
          {result.violations.map((v, i) => (
            <ViolationItem key={`v-${i}`} item={v} />
          ))}
        </div>
      )}
      {result.warnings.length > 0 && (
        <div className={styles.section}>
          {result.warnings.map((w, i) => (
            <ViolationItem key={`w-${i}`} item={w} />
          ))}
        </div>
      )}
      {notCheckedNotice && (
        <div className={styles.section}>
          <div className={`${styles.item} ${styles.info}`}>
            <p className={styles.message}>{notCheckedNotice}</p>
          </div>
        </div>
      )}
    </div>
  )
}
