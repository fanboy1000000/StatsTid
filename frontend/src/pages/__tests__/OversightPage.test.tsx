// S61 / TASK-6106 — page-level vitest coverage for OversightPage (the read-only
// "Oversigt" landing surface added in TASK-6104). The 6104 sprint shipped
// component-level smoke tests for LeaveOverview + AccrualTrend; this suite adds the
// PAGE composition + edge cases, mocking the data hooks (useBalanceSummary /
// useAccrualSeries / useCompliance / useSkema) + useAuth in the SkemaPage/
// ApprovalDashboard vi.mock style so no AuthProvider or network is required.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { BalanceSummary } from '../../hooks/useBalanceSummary'
import type { AccrualSeries } from '../../hooks/useAccrualSeries'
import type { ComplianceCheckResult } from '../../hooks/useCompliance'

// ── useAuth: a fixed logged-in employee (no AuthProvider needed) ──
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'emp001', role: 'Employee' },
    role: 'Employee',
    orgId: 'STY01',
    agreementCode: 'AC',
    scopes: [],
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

// ── Data hooks: mocked so each test injects an exact server-shape payload ──
const mockUseBalanceSummary = vi.fn()
const mockUseAccrualSeries = vi.fn()
const mockUseCompliance = vi.fn()
const mockUseSkema = vi.fn()

vi.mock('../../hooks/useBalanceSummary', () => ({
  useBalanceSummary: (...args: unknown[]) => mockUseBalanceSummary(...args),
}))
vi.mock('../../hooks/useAccrualSeries', () => ({
  useAccrualSeries: (...args: unknown[]) => mockUseAccrualSeries(...args),
}))
vi.mock('../../hooks/useCompliance', () => ({
  useCompliance: (...args: unknown[]) => mockUseCompliance(...args),
}))
vi.mock('../../hooks/useSkema', () => ({
  useSkema: (...args: unknown[]) => mockUseSkema(...args),
}))

// Imported AFTER the mocks are registered.
import { OversightPage } from '../OversightPage'

// ── Fixtures ──

const fullBalance: BalanceSummary = {
  flexBalance: 3.5,
  flexDelta: 1.2,
  vacationDaysUsed: 5,
  vacationDaysEntitlement: 25,
  normHoursExpected: 148,
  normHoursActual: 150,
  overtimeHours: 2,
  agreementCode: 'AC',
  hasMerarbejde: false,
  entitlements: [
    {
      type: 'VACATION',
      label: 'Ferie',
      totalQuota: 25,
      used: 5,
      planned: 2,
      carryoverIn: 3,
      remaining: 14.5,
      earned: 18.5,
      entitlementYear: 2025,
    },
    {
      type: 'CARE_DAY',
      label: 'Omsorgsdage',
      totalQuota: 2,
      used: 1,
      planned: 0,
      carryoverIn: 0,
      remaining: 1,
      earned: 2,
      entitlementYear: 2026,
    },
  ],
  overtimeBalance: {
    accumulated: 12,
    paidOut: 4,
    afspadseringUsed: 3,
    remaining: 5,
    compensationModel: 'AFSPADSERING',
  },
}

const fullSeries: AccrualSeries = {
  employeeId: 'emp001',
  year: 2025,
  month: 10,
  series: [
    {
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
    },
  ],
}

const cleanCompliance: ComplianceCheckResult = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: true,
  violations: [],
  warnings: [],
}

function balanceHook(data: BalanceSummary | null, loading = false) {
  return { data, loading, error: null, refetch: vi.fn() }
}
function seriesHook(data: AccrualSeries | null, loading = false) {
  return { data, loading, error: null, refetch: vi.fn() }
}
function complianceHook(result: ComplianceCheckResult | null, loading = false) {
  return { result, loading, error: null, refetch: vi.fn() }
}
function skemaHook(approvalStatus: string | null) {
  const data = approvalStatus
    ? {
        approval: {
          status: approvalStatus,
          employeeApprovedAt: '2025-10-31T10:00:00Z',
          employeeDeadline: '2025-11-05',
          managerDeadline: '2025-11-10',
          rejectionReason: null,
        },
      }
    : { approval: null }
  // OversightPage only reads `data.approval` + `loading`; the rest of the rich
  // useSkema surface is unused on this read-only page.
  return { data, loading: false }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <OversightPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  mockUseBalanceSummary.mockReset()
  mockUseAccrualSeries.mockReset()
  mockUseCompliance.mockReset()
  mockUseSkema.mockReset()
})

describe('OversightPage', () => {
  it('renders the optjent leave overview with earned-of-annual framing (not used/total)', () => {
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    renderPage()

    // VACATION (an accrual type) frames as "optjent X af Y dage" — NOT "X af Y dage brugt".
    expect(screen.getByText('optjent 18,5 af 25 dage')).toBeInTheDocument()
    // The non-accrual CARE_DAY keeps the used/total framing alongside it.
    expect(screen.getByText('1 af 2 dage brugt')).toBeInTheDocument()
  })

  it('renders the overtime (overtidssaldo) card with its value', () => {
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    renderPage()

    expect(screen.getByText('Overtidssaldo')).toBeInTheDocument()
    // remaining = 5 t (formatDanishNumber trims the trailing ,0).
    expect(screen.getByText('5 t')).toBeInTheDocument()
    expect(screen.getByText('AFSPADSERING')).toBeInTheDocument()
  })

  it('renders the overtime card null fallback ("—") when overtimeBalance is null', () => {
    const noOvertime: BalanceSummary = { ...fullBalance, overtimeBalance: null }
    mockUseBalanceSummary.mockReturnValue(balanceHook(noOvertime))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    renderPage()

    expect(screen.getByText('Overtidssaldo')).toBeInTheDocument()
    // The null-balance branch renders an em-dash placeholder instead of the stats list.
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.queryByText('AFSPADSERING')).not.toBeInTheDocument()
  })

  it('renders the accrual trend chart for the MONTHLY_ACCRUAL series', () => {
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    renderPage()

    // AccrualTrend renders an accessible (role=img) chart with a descriptive label.
    expect(
      screen.getByRole('img', { name: /Optjeningsudvikling for Ferie/ }),
    ).toBeInTheDocument()
    // The "now" point (isSelected) is tagged.
    expect(screen.getByText(/\(nu\)/)).toBeInTheDocument()
  })

  it('shows the "Ingen advarsler" clean-compliance banner when there are no violations/warnings', () => {
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    renderPage()

    expect(screen.getByText('Ingen advarsler')).toBeInTheDocument()
    expect(
      screen.getByText('Ingen overtrædelser eller advarsler for perioden.'),
    ).toBeInTheDocument()
  })

  it('renders the "Arbejdstidskontrol" heading exactly once in both clean and issues states', () => {
    // Clean state: ComplianceWarnings returns null → only the page <h3> shows.
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('DRAFT'))

    const { unmount } = renderPage()
    expect(screen.getAllByRole('heading', { name: 'Arbejdstidskontrol' })).toHaveLength(1)
    unmount()

    // Issues state: hideTitle suppresses the component's own <h3>, so the page
    // <h3> remains the single heading (regression guard for the S61 double-heading).
    const withIssues: ComplianceCheckResult = {
      ...cleanCompliance,
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
    mockUseCompliance.mockReturnValue(complianceHook(withIssues))

    renderPage()
    expect(screen.getAllByRole('heading', { name: 'Arbejdstidskontrol' })).toHaveLength(1)
    // The violation itself still renders under the single heading.
    expect(screen.getByText('Ugentligt timemaksimum overskredet.')).toBeInTheDocument()
  })

  it('is read-only: shows the approval status but renders no submit/approve buttons', () => {
    mockUseBalanceSummary.mockReturnValue(balanceHook(fullBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(fullSeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook('SUBMITTED'))

    renderPage()

    // The status section header + the SUBMITTED → "Indsendt" badge are present.
    expect(screen.getByText('Godkendelsesstatus')).toBeInTheDocument()
    expect(screen.getAllByText('Indsendt').length).toBeGreaterThanOrEqual(1)

    // No write affordances on this read-only page (no submit/approve/reject buttons).
    expect(screen.queryByRole('button', { name: /Godkend/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Indsend/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Afvis/ })).not.toBeInTheDocument()
  })

  it('renders an empty/new-hire state (earned ≈ 0, empty series) without crashing', () => {
    const newHireBalance: BalanceSummary = {
      ...fullBalance,
      entitlements: [
        {
          type: 'VACATION',
          label: 'Ferie',
          totalQuota: 25,
          used: 0,
          planned: 0,
          carryoverIn: 0,
          remaining: 0,
          earned: 0,
          entitlementYear: 2025,
        },
      ],
      overtimeBalance: null,
    }
    const emptySeries: AccrualSeries = { employeeId: 'emp001', year: 2025, month: 10, series: [] }

    mockUseBalanceSummary.mockReturnValue(balanceHook(newHireBalance))
    mockUseAccrualSeries.mockReturnValue(seriesHook(emptySeries))
    mockUseCompliance.mockReturnValue(complianceHook(cleanCompliance))
    mockUseSkema.mockReturnValue(skemaHook(null)) // no period created yet

    renderPage()

    // Earned 0 still uses the optjent framing.
    expect(screen.getByText('optjent 0 af 25 dage')).toBeInTheDocument()
    // Empty accrual series → the AccrualTrend empty state (no crash, no chart).
    expect(screen.getByText('Ingen optjeningskurver tilgængelige.')).toBeInTheDocument()
    // No period yet → the "Kladde / Ingen periode oprettet" status fallback.
    expect(screen.getByText('Ingen periode oprettet for måneden endnu.')).toBeInTheDocument()
  })
})
