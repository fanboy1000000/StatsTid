// S148 / TASK-14814 — AC-14a: the warnings panel is ONE polite live region. Its outermost element
// carries role="status" and is always mounted (a region that mounts with its content is often not
// announced); no item carries a role.
//
// Kind F. Red conditions: before this task the container has no role and the notice alone has
// role="status", so the one status element does not contain the heading; the clean and loading
// states render nothing (`getByRole('status')` throws); and rerendering from clean to a violation
// cannot keep a region element that never existed.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ComplianceWarnings } from '../ComplianceWarnings'
import {
  COMPLIANCE_SEVERITY,
  COMPLIANCE_VIOLATION_TYPE,
  type ComplianceCheckResult,
  type ComplianceViolation,
} from '../../hooks/useCompliance'

const item = (severity: ComplianceViolation['severity'], message: string): ComplianceViolation => ({
  violationType: COMPLIANCE_VIOLATION_TYPE.DAILY_REST,
  date: '2026-03-10',
  actualValue: 9,
  thresholdValue: 11,
  severity,
  isVoluntaryExempt: false,
  message,
})

const clean: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: true,
  violations: [],
  warnings: [],
  restNotCheckedDays: [],
}
const full: ComplianceCheckResult = {
  ...clean,
  success: false,
  violations: [item(COMPLIANCE_SEVERITY.VIOLATION, 'Et brud.')],
  warnings: [item(COMPLIANCE_SEVERITY.WARNING, 'En advarsel.')],
  restNotCheckedDays: ['2026-03-11'],
}

describe('ComplianceWarnings — one polite live region (AC-14a)', () => {
  it('a violation, a warning and the notice sit inside one status region; no item has a role', () => {
    const { container } = render(<ComplianceWarnings result={full} loading={false} />)
    const regions = screen.getAllByRole('status')
    expect(regions).toHaveLength(1)
    const region = regions[0]
    expect(region).toHaveTextContent('Arbejdstidskontrol')
    expect(region).toHaveTextContent('Et brud.')
    expect(region).toHaveTextContent('En advarsel.')
    expect(region).toHaveTextContent('Hviletid ikke kontrolleret for 1 dag')
    // The region is the only element with a role.
    expect(container.querySelectorAll('[role]')).toHaveLength(1)
    expect(container.firstElementChild).toBe(region)
  })

  it('the clean and the loading state keep the region mounted and empty', () => {
    const { rerender } = render(<ComplianceWarnings result={clean} loading={false} />)
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
    rerender(<ComplianceWarnings result={null} loading={true} />)
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
  })

  it('rerendering from clean to a violation keeps the same region element', () => {
    const { rerender } = render(<ComplianceWarnings result={clean} loading={false} />)
    const before = screen.getByRole('status')
    rerender(<ComplianceWarnings result={full} loading={false} />)
    expect(screen.getByRole('status')).toBe(before)
    expect(before).toHaveTextContent('Et brud.')
  })
})
