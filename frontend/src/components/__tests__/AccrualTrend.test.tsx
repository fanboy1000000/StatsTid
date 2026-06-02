import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { AccrualTrend } from '../AccrualTrend'
import type { AccrualSeriesEntitlement } from '../../hooks/useAccrualSeries'

const ferie: AccrualSeriesEntitlement = {
  type: 'VACATION',
  label: 'Ferie',
  annualQuota: 25,
  entitlementYear: 2025,
  ferieaarStart: '2025-09-01',
  points: [
    { monthEnd: '2025-09-30', earned: 2.08, isSelected: false },
    { monthEnd: '2025-10-31', earned: 4.16, isSelected: true },
    { monthEnd: '2025-11-30', earned: 6.25, isSelected: false },
  ],
}

describe('AccrualTrend', () => {
  it('renders one labelled chart per accrual entitlement with an accessible aria-label', () => {
    render(<AccrualTrend series={[ferie]} loading={false} />)
    expect(screen.getByText('Ferie')).toBeDefined()
    // role="img" with a descriptive label (not color-only).
    expect(screen.getByRole('img', { name: /Optjeningsudvikling for Ferie/ })).toBeDefined()
  })

  it('marks the selected point as "nu"', () => {
    render(<AccrualTrend series={[ferie]} loading={false} />)
    expect(screen.getByText(/\(nu\)/)).toBeDefined()
  })

  it('handles an empty series (new hire / no data) without crashing', () => {
    render(<AccrualTrend series={[]} loading={false} />)
    expect(screen.getByText(/Ingen optjeningskurver tilgængelige/)).toBeDefined()
  })

  it('handles an entitlement with no points', () => {
    const empty: AccrualSeriesEntitlement = { ...ferie, points: [] }
    render(<AccrualTrend series={[empty]} loading={false} />)
    expect(screen.getByText(/Ingen optjeningsdata endnu/)).toBeDefined()
  })
})
