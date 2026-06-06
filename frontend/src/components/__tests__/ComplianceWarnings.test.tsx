// S61 — guards the ComplianceWarnings heading contract after the Oversigt
// double-heading fix. SkemaPage relies on the component's own <h3> being the
// section title (default hideTitle=false); hideTitle lets a caller that renders
// its own section heading suppress this one so the title shows exactly once
// (original consumer: the S61 OversightPage, deleted S65 — prop kept as the
// documented opt-out).
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ComplianceWarnings } from '../ComplianceWarnings'
import type { ComplianceCheckResult } from '../../hooks/useCompliance'

const withViolation: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: false,
  violations: [
    {
      violationType: 'WEEKLY_MAX_HOURS',
      date: '2025-10-15',
      actualValue: 50,
      thresholdValue: 48,
      severity: 'VIOLATION',
      isVoluntaryExempt: false,
      message: 'Ugentligt timemaksimum overskredet.',
    },
  ],
  warnings: [],
}

const clean: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: true,
  violations: [],
  warnings: [],
}

describe('ComplianceWarnings', () => {
  it('renders its own "Arbejdstidskontrol" <h3> by default (SkemaPage contract)', () => {
    render(<ComplianceWarnings result={withViolation} loading={false} />)
    expect(screen.getByRole('heading', { name: 'Arbejdstidskontrol' })).toBeInTheDocument()
    expect(screen.getByText('Ugentligt timemaksimum overskredet.')).toBeInTheDocument()
  })

  it('suppresses its internal <h3> when hideTitle is set (own-heading-caller contract)', () => {
    render(<ComplianceWarnings result={withViolation} loading={false} hideTitle />)
    expect(screen.queryByRole('heading', { name: 'Arbejdstidskontrol' })).not.toBeInTheDocument()
    // The violations themselves still render — only the heading is suppressed.
    expect(screen.getByText('Ugentligt timemaksimum overskredet.')).toBeInTheDocument()
  })

  it('renders nothing (no heading) in the clean state, regardless of hideTitle', () => {
    const { rerender } = render(<ComplianceWarnings result={clean} loading={false} />)
    expect(screen.queryByRole('heading', { name: 'Arbejdstidskontrol' })).not.toBeInTheDocument()
    rerender(<ComplianceWarnings result={clean} loading={false} hideTitle />)
    expect(screen.queryByRole('heading', { name: 'Arbejdstidskontrol' })).not.toBeInTheDocument()
  })
})
