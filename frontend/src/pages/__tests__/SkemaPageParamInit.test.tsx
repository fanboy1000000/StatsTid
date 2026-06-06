// S65 / TASK-6503 — pins the SkemaPage drill-in param init: the Årsoversigt
// year-matrix month header navigates to /tid/registrering?year=Y&month=M, and
// SkemaPage must seed its initial period from those search params (defaulting to
// today, clamping month to 1..12). Renders the REAL SkemaPage with all data
// hooks mocked to a stable idle state, asserting the month title <h2>.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({
    user: { employeeId: 'emp001', role: 'Employee' },
    role: 'Employee',
    orgId: 'STY01',
    agreementCode: 'AC',
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

// Stable idle data hooks — SkemaPage renders its month heading regardless.
// CRITICAL: the returned objects MUST be referentially STABLE across renders.
// SkemaPage has effects keyed on `data` identity that rehydrate local state; a
// fresh object literal each render would loop (setState→render→new data→…→OOM).
const stable = vi.hoisted(() => {
  const skemaData = {
    entries: [],
    absences: [],
    projects: [],
    absenceTypes: [],
    workTime: [],
    dailyNorm: [],
    approval: null,
  }
  return {
    skema: {
      data: skemaData,
      loading: false,
      error: null,
      quotaError: null,
      approvalValidationError: null,
      clearQuotaError: () => {},
      clearApprovalValidationError: () => {},
      refetch: () => {},
      saveMonth: () => {},
      employeeApprove: () => {},
      submitAndApprove: () => {},
      reopenPeriod: () => {},
    },
    balance: { data: null, loading: false, error: null, refetch: () => {} },
    compliance: { result: null, loading: false, error: null, refetch: () => {} },
  }
})

vi.mock('../../hooks/useSkema', () => ({ useSkema: () => stable.skema }))
vi.mock('../../hooks/useBalanceSummary', () => ({ useBalanceSummary: () => stable.balance }))
vi.mock('../../hooks/useCompliance', () => ({ useCompliance: () => stable.compliance }))

// Stub the heavy presentational children so the param-init render stays light
// and deterministic (we only assert the month-title <h2> the page itself owns).
vi.mock('../../components/SkemaGrid', () => ({ SkemaGrid: () => null }))
vi.mock('../../components/BalanceSummary', () => ({ BalanceSummary: () => null }))
vi.mock('../../components/AllocationSummary', () => ({ AllocationSummary: () => null }))
vi.mock('../../components/ComplianceWarnings', () => ({ ComplianceWarnings: () => null }))
vi.mock('../../components/ProjectPicker', () => ({ ProjectPicker: () => null }))

import { SkemaPage } from '../SkemaPage'

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <SkemaPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.useRealTimers()
})

describe('SkemaPage drill-in param init', () => {
  it('seeds the period from ?year=&month= (the Årsoversigt drill-in target)', () => {
    renderAt('/tid/registrering?year=2026&month=3')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')
  })

  it('honors a different drilled-in month', () => {
    renderAt('/tid/registrering?year=2025&month=11')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('November 2025')
  })

  it('defaults to today when no params are present', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-04T09:00:00Z'))
    renderAt('/tid/registrering')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Juli 2026')
    vi.useRealTimers()
  })

  it('ignores an out-of-range month and falls back to today', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-02-10T09:00:00Z'))
    renderAt('/tid/registrering?year=2026&month=13')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Februar 2026')
    vi.useRealTimers()
  })

  it('ignores an out-of-range year (backend supports 2000–2100) and falls back to today', () => {
    // Step-7a cycle-4 Codex: an unclamped ?year=10000 propagates to the Skema APIs where
    // DateTime.DaysInMonth(10000, m) throws server-side — the seed must clamp like the
    // year-overview endpoint (2000–2100), not just require > 0. Params clamp per-field:
    // the invalid year falls back to today's YEAR while the valid month=1 is honored.
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-02-10T09:00:00Z'))
    renderAt('/tid/registrering?year=10000&month=1')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Januar 2026')
    vi.useRealTimers()
  })
})
