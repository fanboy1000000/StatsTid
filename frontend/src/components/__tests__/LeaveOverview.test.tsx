import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LeaveOverview } from '../LeaveOverview'
import type { EntitlementInfo } from '../../hooks/useBalanceSummary'
import type { AccrualSeriesEntitlement } from '../../hooks/useAccrualSeries'

const vacation: EntitlementInfo = {
  type: 'VACATION',
  label: 'Ferie',
  totalQuota: 25,
  used: 5,
  planned: 2,
  carryoverIn: 3,
  remaining: 21,
  earned: 18.5,
  entitlementYear: 2025,
}

const careDay: EntitlementInfo = {
  type: 'CARE_DAY',
  label: 'Omsorgsdage',
  totalQuota: 2,
  used: 1,
  planned: 0,
  carryoverIn: 0,
  remaining: 1,
  earned: 2,
  entitlementYear: 2026,
}

const vacationSeries: AccrualSeriesEntitlement = {
  type: 'VACATION',
  label: 'Ferie',
  annualQuota: 25,
  entitlementYear: 2025,
  ferieaarStart: '2025-09-01',
  points: [],
}

describe('LeaveOverview', () => {
  it('frames VACATION as earned-of-annual ("optjent X af Y dage")', () => {
    render(<LeaveOverview entitlements={[vacation]} series={[vacationSeries]} loading={false} />)
    expect(screen.getByText('optjent 18,5 af 25 dage')).toBeDefined()
  })

  it('labels VACATION with a ferieår derived from the series ferieaarStart', () => {
    render(<LeaveOverview entitlements={[vacation]} series={[vacationSeries]} loading={false} />)
    expect(screen.getByText('Ferieår 2025/26')).toBeDefined()
  })

  it('shows Rest verbatim from the server', () => {
    render(<LeaveOverview entitlements={[vacation]} series={[vacationSeries]} loading={false} />)
    expect(screen.getByText('21 dage')).toBeDefined()
  })

  it('uses a used/total framing and a calendar year for non-accrual types', () => {
    render(<LeaveOverview entitlements={[careDay]} series={[]} loading={false} />)
    expect(screen.getByText('1 af 2 dage brugt')).toBeDefined()
    expect(screen.getByText('2026')).toBeDefined()
  })

  it('renders an informative empty state when there are no entitlements', () => {
    render(<LeaveOverview entitlements={[]} series={[]} loading={false} />)
    expect(screen.getByText(/Ingen ferie- eller fraværsrettigheder/)).toBeDefined()
  })
})
