// S65 / TASK-6503 — pins the SkemaPage drill-in param init: the Årsoversigt
// year-matrix month header navigates to /tid/registrering?year=Y&month=M, and
// SkemaPage must seed its initial period from those search params (defaulting to
// today, clamping month to 1..12). Renders the REAL SkemaPage with all data
// hooks mocked to a stable idle state, asserting the month title <h2>.
//
// S143 / TASK-14303 — "today" no longer comes from `new Date()` (`SkemaPage.tsx:218`); it comes
// from the shared calendar context (`useCalendarToday()`, `contexts/CalendarContext.tsx`), fed
// once at app start by the server (`GET /api/calendar/today`). Every render below now supplies
// that value via `renderWithCalendar` — the shared test seam TASK-14301 shipped — instead of
// faking the system clock: SkemaPage has nothing left to fake, since it no longer reads it.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { renderWithCalendar } from '../../test/renderWithCalendar'

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

// S72/TASK-7205: the page imports the PURE helpers (buildWorkTimePayload /
// periodHours from useSkema; computeMonthFlexDelta / deriveMonthAbsenceUsage
// from useBalanceSummary) alongside the hooks — keep the real module and stub
// ONLY the hook so those exports stay callable.
vi.mock('../../hooks/useSkema', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useSkema')>()),
  useSkema: () => stable.skema,
}))
vi.mock('../../hooks/useBalanceSummary', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useBalanceSummary')>()),
  useBalanceSummary: () => stable.balance,
}))
vi.mock('../../hooks/useCompliance', () => ({ useCompliance: () => stable.compliance }))

// Stub the heavy presentational children so the param-init render stays light
// and deterministic (we only assert the month-title <h2> the page itself owns).
// (AllocationSummary + ProjectPicker stubs removed — retired in S72/R9.)
vi.mock('../../components/SkemaGrid', () => ({ SkemaGrid: () => null }))
vi.mock('../../components/BalanceSummary', () => ({ BalanceSummary: () => null }))
vi.mock('../../components/ComplianceWarnings', () => ({ ComplianceWarnings: () => null }))
vi.mock('../../components/SkemaDayPanel', () => ({ SkemaDayPanel: () => null }))
vi.mock('../../components/SkemaProjectManager', () => ({ SkemaProjectManager: () => null }))

import { SkemaPage } from '../SkemaPage'

function renderAt(url: string, today: string) {
  return renderWithCalendar(
    today,
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
    // "today" is deliberately far from the param (year 2099, not 2026) — proves the explicit
    // query param wins over the default on its own terms, not by coincidentally agreeing with it.
    renderAt('/tid/registrering?year=2026&month=3', '2099-01-01')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')
  })

  it('honors a different drilled-in month', () => {
    renderAt('/tid/registrering?year=2025&month=11', '2099-01-01')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('November 2025')
  })

  it('defaults to today when no params are present', () => {
    renderAt('/tid/registrering', '2026-07-04')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Juli 2026')
  })

  it('ignores an out-of-range month and falls back to today', () => {
    renderAt('/tid/registrering?year=2026&month=13', '2026-02-10')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Februar 2026')
  })

  it('ignores an out-of-range year (backend supports 2000–2100) and falls back to today', () => {
    // Step-7a cycle-4 Codex: an unclamped ?year=10000 propagates to the Skema APIs where
    // DateTime.DaysInMonth(10000, m) throws server-side — the seed must clamp like the
    // year-overview endpoint (2000–2100), not just require > 0. Params clamp per-field:
    // the invalid year falls back to today's YEAR while the valid month=1 is honored.
    renderAt('/tid/registrering?year=10000&month=1', '2026-02-10')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Januar 2026')
  })
})
