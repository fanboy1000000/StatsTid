// S120 / TASK-12001 — component-test-pin for THE RULED FlexBalanceCard EDIT
// (owner ruling #1, the flex ONE-shape normalization): the card's two
// presence-guards (`!== undefined`) became VALUE-guards (`!= null`) because
// the no-history branch now serves the 3 history members as NULL (never
// absent). The pins lock the behavior-preservation claim: the no-history state
// renders the zero display with NEITHER history sub-line and never throws; the
// with-history state renders both sub-lines.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { FlexBalanceCard } from '../FlexBalanceCard'
import type { FlexBalanceInfo } from '../../types'

/** The ruled no-history wire shape: all 5 members present, history NULL. */
const noHistory: FlexBalanceInfo = {
  employeeId: 'emp001',
  balance: 0,
  previousBalance: null,
  delta: null,
  reason: null,
}

const withHistory: FlexBalanceInfo = {
  employeeId: 'emp001',
  balance: 4.2,
  previousBalance: 1.7,
  delta: 2.5,
  reason: 'WEEKLY_CALCULATION',
}

describe('FlexBalanceCard — the S120 ruled value-guards', () => {
  it('no-history (nulls, the ruled normalized branch): renders the zero display and NO history sub-lines — and never throws', () => {
    expect(() => render(<FlexBalanceCard flexBalance={noHistory} loading={false} />)).not.toThrow()
    expect(screen.getByText(/0\.0t/)).toBeInTheDocument()
    expect(screen.getByText('Positiv')).toBeInTheDocument()
    // The value-guards suppress both sub-lines on null (pre-ruling: on absent).
    expect(screen.queryByText(/Denne periode/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Forrige/)).not.toBeInTheDocument()
  })

  it('with-history: renders the delta and previous-balance sub-lines', () => {
    render(<FlexBalanceCard flexBalance={withHistory} loading={false} />)
    expect(screen.getByText(/4\.2t/)).toBeInTheDocument()
    expect(screen.getByText(/Denne periode: \+2\.5t/)).toBeInTheDocument()
    expect(screen.getByText(/Forrige: 1\.7t/)).toBeInTheDocument()
  })

  it('a zero delta still renders its sub-line (0 is a VALUE, not no-history — the != null guard keeps it)', () => {
    render(
      <FlexBalanceCard
        flexBalance={{ ...withHistory, delta: 0, previousBalance: 0 }}
        loading={false}
      />,
    )
    expect(screen.getByText(/Denne periode: 0\.0t/)).toBeInTheDocument()
    expect(screen.getByText(/Forrige: 0\.0t/)).toBeInTheDocument()
  })

  it('negative balance renders the Negativ badge', () => {
    render(<FlexBalanceCard flexBalance={{ ...noHistory, balance: -2.1 }} loading={false} />)
    expect(screen.getByText('Negativ')).toBeInTheDocument()
  })

  it('loading and no-data states are unchanged', () => {
    const { rerender } = render(<FlexBalanceCard flexBalance={null} loading={true} />)
    expect(screen.getByText('Henter flex-saldo...')).toBeInTheDocument()
    rerender(<FlexBalanceCard flexBalance={null} loading={false} />)
    expect(screen.getByText('Ingen flex-data fundet')).toBeInTheDocument()
  })
})
