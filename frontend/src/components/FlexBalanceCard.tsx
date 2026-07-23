import type { FlexBalanceInfo } from '../types'
import { Card, Badge } from './ui'
import styles from './FlexBalanceCard.module.css'

interface Props {
  flexBalance: FlexBalanceInfo | null
  loading: boolean
}

export function FlexBalanceCard({ flexBalance, loading }: Props) {
  if (loading) {
    return (
      <Card header="Flex-saldo">
        <span className={styles.loadingText}>Henter flex-saldo...</span>
      </Card>
    )
  }

  if (!flexBalance) {
    return (
      <Card header="Flex-saldo">
        <span className={styles.noData}>Ingen flex-data fundet</span>
      </Card>
    )
  }

  const isPositive = flexBalance.balance >= 0

  return (
    <Card header="Flex-saldo">
      <div className={`${styles.balance} ${isPositive ? styles.positive : styles.negative}`}>
        {flexBalance.balance.toFixed(1)}t
        {' '}
        <Badge variant={isPositive ? 'success' : 'error'}>
          {isPositive ? 'Positiv' : 'Negativ'}
        </Badge>
      </div>
      {/* S120 — THE RULED COMPANION EDIT (owner ruling #1, behavior-preserving):
          under the flex normalization the no-history branch serves the history
          members as NULL (never absent), so the presence-guards (!== undefined)
          became VALUE-guards (!= null). The no-history state renders the zero
          headline with neither sub-line — exactly as before the ruling. */}
      {flexBalance.delta != null && (
        <div className={styles.delta}>
          Denne periode: {flexBalance.delta > 0 ? '+' : ''}{flexBalance.delta.toFixed(1)}t
        </div>
      )}
      {flexBalance.previousBalance != null && (
        <div className={styles.previous}>
          Forrige: {flexBalance.previousBalance.toFixed(1)}t
        </div>
      )}
    </Card>
  )
}
