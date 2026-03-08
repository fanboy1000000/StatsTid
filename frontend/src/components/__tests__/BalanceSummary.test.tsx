import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { BalanceSummary } from '../BalanceSummary'
import type { BalanceSummary as BalanceSummaryData } from '../../hooks/useBalanceSummary'

const mockData: BalanceSummaryData = {
  flexBalance: 12.5,
  flexDelta: 2.3,
  vacationDaysUsed: 5,
  vacationDaysEntitlement: 25,
  normHoursExpected: 162.8,
  normHoursActual: 155.0,
  overtimeHours: 0,
  agreementCode: 'HK',
  hasMerarbejde: false,
}

describe('BalanceSummary', () => {
  it('renders all 4 balance cards when data is provided', () => {
    render(<BalanceSummary data={mockData} loading={false} />)
    expect(screen.getByText('Flex saldo')).toBeDefined()
    expect(screen.getByText('Ferie')).toBeDefined()
    expect(screen.getByText('Normtimer')).toBeDefined()
    expect(screen.getByText('Overarbejde')).toBeDefined()  // HK uses "Overarbejde"
  })

  it('shows "Merarbejde" for AC agreements', () => {
    const acData: BalanceSummaryData = { ...mockData, hasMerarbejde: true, agreementCode: 'AC' }
    render(<BalanceSummary data={acData} loading={false} />)
    expect(screen.getByText('Merarbejde')).toBeDefined()
  })

  it('renders nothing when no data and not loading', () => {
    const { container } = render(<BalanceSummary data={null} loading={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('shows vacation days as used/entitled', () => {
    render(<BalanceSummary data={mockData} loading={false} />)
    expect(screen.getByText('5 / 25 dage')).toBeDefined()
  })

  it('shows norm hours as actual/expected', () => {
    render(<BalanceSummary data={mockData} loading={false} />)
    expect(screen.getByText('155.0 / 162.8 t')).toBeDefined()
  })
})
