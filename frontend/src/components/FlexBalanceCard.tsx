import type { FlexBalanceInfo } from '../types'

interface Props {
  flexBalance: FlexBalanceInfo | null
  loading: boolean
}

export function FlexBalanceCard({ flexBalance, loading }: Props) {
  if (loading) return <div style={cardStyle}>Henter flex-saldo...</div>

  if (!flexBalance) return <div style={cardStyle}>Ingen flex-data fundet</div>

  const isPositive = flexBalance.balance >= 0

  return (
    <div style={cardStyle}>
      <h3 style={{ margin: '0 0 8px 0' }}>Flex-saldo</h3>
      <div style={{ fontSize: '2em', fontWeight: 'bold', color: isPositive ? '#2e7d32' : '#c62828' }}>
        {flexBalance.balance.toFixed(1)}t
      </div>
      {flexBalance.delta !== undefined && (
        <div style={{ color: '#666', marginTop: 4 }}>
          Denne periode: {flexBalance.delta > 0 ? '+' : ''}{flexBalance.delta.toFixed(1)}t
        </div>
      )}
      {flexBalance.previousBalance !== undefined && (
        <div style={{ color: '#999', fontSize: '0.9em' }}>
          Forrige: {flexBalance.previousBalance.toFixed(1)}t
        </div>
      )}
    </div>
  )
}

const cardStyle: React.CSSProperties = {
  padding: 16,
  border: '1px solid #ddd',
  borderRadius: 8,
  backgroundColor: '#fafafa',
  minWidth: 200,
  display: 'inline-block',
}
