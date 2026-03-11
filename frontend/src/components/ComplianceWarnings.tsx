import type { ComplianceCheckResult, ComplianceViolation } from '../hooks/useCompliance'
import styles from './ComplianceWarnings.module.css'

interface ComplianceWarningsProps {
  result: ComplianceCheckResult | null
  loading: boolean
}

const VIOLATION_TYPE_LABELS: Record<string, string> = {
  DAILY_REST: 'Daglig hvile',
  WEEKLY_REST: 'Ugentlig hviledag',
  MAX_DAILY_HOURS: 'Maks daglig arbejdstid',
  WEEKLY_MAX_HOURS: 'Ugentligt timemaksimum (48t)',
}

function formatDate(dateStr: string): string {
  try {
    return new Date(dateStr).toLocaleDateString('da-DK')
  } catch {
    return dateStr
  }
}

function ViolationItem({ item }: { item: ComplianceViolation }) {
  const isViolation = item.severity === 'VIOLATION'
  return (
    <div className={`${styles.item} ${isViolation ? styles.violation : styles.warning}`}>
      <div className={styles.itemHeader}>
        <span className={`${styles.badge} ${isViolation ? styles.badgeViolation : styles.badgeWarning}`}>
          {isViolation ? 'Overtraedelse' : 'Advarsel'}
        </span>
        <span className={styles.type}>{VIOLATION_TYPE_LABELS[item.violationType] ?? item.violationType}</span>
        <span className={styles.date}>{formatDate(item.date)}</span>
      </div>
      <p className={styles.message}>{item.message}</p>
    </div>
  )
}

export function ComplianceWarnings({ result, loading }: ComplianceWarningsProps) {
  if (loading || !result) return null

  const hasIssues = result.violations.length > 0 || result.warnings.length > 0
  if (!hasIssues) return null

  return (
    <div className={styles.container}>
      <h3 className={styles.title}>Arbejdstidskontrol</h3>
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
    </div>
  )
}
