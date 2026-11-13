// S148 / TASK-14814 — AC-14b: the "Hviletid ikke kontrolleret" notice is information, not a breach,
// so it uses the informational style (`--color-info`), not the amber warning style. CSS-module
// classes render under their own names in vitest (`vite.config.ts` classNameStrategy 'non-scoped').
//
// Kind F. Red conditions: before this task the notice's element has class `warning` and not `info`,
// so `toHaveClass('info')` and `not.toHaveClass('warning')` fail. The warning-item contrast holds
// today and after (kind R).
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ComplianceWarnings } from '../ComplianceWarnings'
import {
  COMPLIANCE_SEVERITY,
  COMPLIANCE_VIOLATION_TYPE,
  type ComplianceCheckResult,
} from '../../hooks/useCompliance'

const result: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: false,
  violations: [],
  warnings: [
    {
      violationType: COMPLIANCE_VIOLATION_TYPE.DAILY_REST,
      date: '2026-03-10',
      actualValue: 9,
      thresholdValue: 11,
      severity: COMPLIANCE_SEVERITY.WARNING,
      isVoluntaryExempt: false,
      message: 'En advarsel.',
    },
  ],
  restNotCheckedDays: ['2026-03-11'],
}

describe('ComplianceWarnings — the not-checked notice is informational (AC-14b)', () => {
  it('the notice item has the info style and not the warning style', () => {
    render(<ComplianceWarnings result={result} loading={false} />)
    const notice = screen.getByText('Hviletid ikke kontrolleret for 1 dag').closest('div')!
    expect(notice).toHaveClass('item')
    expect(notice).toHaveClass('info')
    expect(notice).not.toHaveClass('warning')
  })

  it('a warning item keeps the warning style (contrast, kind R)', () => {
    render(<ComplianceWarnings result={result} loading={false} />)
    const warning = screen.getByText('En advarsel.').closest('div')!
    expect(warning).toHaveClass('warning')
    expect(warning).not.toHaveClass('info')
  })
})
