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
      {flexBalance.delta !== undefined && (
        <div className={styles.delta}>
          Denne periode: {flexBalance.delta > 0 ? '+' : ''}{flexBalance.delta.toFixed(1)}t
        </div>
      )}
      {flexBalance.previousBalance !== undefined && (
        <div className={styles.previous}>
          Forrige: {flexBalance.previousBalance.toFixed(1)}t
        </div>
      )}
    </Card>
  )
}
