// S61 — guards the ComplianceWarnings heading contract after the Oversigt
// double-heading fix. SkemaPage relies on the component's own <h3> being the
// section title (default hideTitle=false); hideTitle lets a caller that renders
// its own section heading suppress this one so the title shows exactly once
// (original consumer: the S61 OversightPage, deleted S65 — prop kept as the
// documented opt-out).
//
// S120 / TASK-12001 mock re-anchoring + THE INTEGER-ENUM REPAIR PINS: the
// fixtures now carry the WIRE truth — `violationType`/`severity` are INTEGERS
// (CLR enum order; the spec declares `type: integer`). The old string-valued
// mocks mirrored the deleted hand-written union and MASKED the prod bug where
// `severity === 'VIOLATION'` never matched a real response (everything
// rendered as a yellow "Advarsel" with a bare-integer type label). The new
// pins lock the repaired behavior against integer-valued bodies.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ComplianceWarnings } from '../ComplianceWarnings'
import {
  COMPLIANCE_SEVERITY,
  COMPLIANCE_VIOLATION_TYPE,
  type ComplianceCheckResult,
} from '../../hooks/useCompliance'

const withViolation: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: false,
  violations: [
    {
      violationType: COMPLIANCE_VIOLATION_TYPE.WEEKLY_MAX_HOURS, // 3 on the wire
      date: '2025-10-15',
      actualValue: 50,
      thresholdValue: 48,
      severity: COMPLIANCE_SEVERITY.VIOLATION, // 1 on the wire
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

  // ── S120 — the integer-enum repair pins (the masked prod bug) ──

  it('S120 repair: an integer severity=1 (VIOLATION) renders the "Overtraedelse" badge — the pre-fix string comparison could NEVER match the wire', () => {
    render(<ComplianceWarnings result={withViolation} loading={false} />)
    expect(screen.getByText('Overtraedelse')).toBeInTheDocument()
    expect(screen.queryByText('Advarsel')).not.toBeInTheDocument()
  })

  it('S120 repair: an integer severity=0 (WARNING) renders the "Advarsel" badge', () => {
    const withWarning: ComplianceCheckResult = {
      ...clean,
      warnings: [
        {
          violationType: COMPLIANCE_VIOLATION_TYPE.DAILY_REST, // 0
          date: '2025-10-16',
          actualValue: 9,
          thresholdValue: 11,
          severity: COMPLIANCE_SEVERITY.WARNING, // 0
          isVoluntaryExempt: false,
          message: 'Hviletid under 11 timer.',
        },
      ],
    }
    render(<ComplianceWarnings result={withWarning} loading={false} />)
    expect(screen.getByText('Advarsel')).toBeInTheDocument()
    expect(screen.queryByText('Overtraedelse')).not.toBeInTheDocument()
  })

  it('S120 repair: the integer-keyed Danish labels cover ALL 6 CLR violation types (the deleted union was false-exhaustive at 4 of 6)', () => {
    const labels: Record<number, string> = {
      [COMPLIANCE_VIOLATION_TYPE.DAILY_REST]: 'Daglig hvile',
      [COMPLIANCE_VIOLATION_TYPE.WEEKLY_REST]: 'Ugentlig hviledag',
      [COMPLIANCE_VIOLATION_TYPE.MAX_DAILY_HOURS]: 'Maks daglig arbejdstid',
      [COMPLIANCE_VIOLATION_TYPE.WEEKLY_MAX_HOURS]: 'Ugentligt timemaksimum (48t)',
      [COMPLIANCE_VIOLATION_TYPE.OVERTIME_EXCEEDED]: 'Merarbejde over det godkendte maksimum',
      [COMPLIANCE_VIOLATION_TYPE.OVERTIME_UNAPPROVED]: 'Merarbejde uden forhåndsgodkendelse',
    }
    const all: ComplianceCheckResult = {
      ...clean,
      success: false,
      violations: ([0, 1, 2, 3, 4, 5] as const).map((t) => ({
        violationType: t,
        date: '2025-10-15',
        actualValue: 1,
        thresholdValue: 0,
        severity: COMPLIANCE_SEVERITY.VIOLATION,
        isVoluntaryExempt: false,
        message: `violation-${t}`,
      })),
    }
    render(<ComplianceWarnings result={all} loading={false} />)
    for (const t of [0, 1, 2, 3, 4, 5]) {
      expect(screen.getByText(labels[t])).toBeInTheDocument()
    }
    // No bare-integer fallback label renders when the map covers the value.
    expect(screen.queryByText(/^[0-5]$/)).not.toBeInTheDocument()
  })

  it('S120: the CLR-order constants match the generated spec unions (WARNING=0/VIOLATION=1; DAILY_REST=0..OVERTIME_UNAPPROVED=5)', () => {
    expect(COMPLIANCE_SEVERITY).toEqual({ WARNING: 0, VIOLATION: 1 })
    expect(COMPLIANCE_VIOLATION_TYPE).toEqual({
      DAILY_REST: 0,
      WEEKLY_REST: 1,
      MAX_DAILY_HOURS: 2,
      WEEKLY_MAX_HOURS: 3,
      OVERTIME_EXCEEDED: 4,
      OVERTIME_UNAPPROVED: 5,
    })
  })
})
