// S148 / TASK-14812 (QUAL-236) — AC-S0-2h part (2): the Årsoversigt's "Barns sygedag" tile
// shows the allowance PER ILLNESS the server serves (whole days, plus the call-home day when the
// server says it applies), never a yearly remainder ("saldo"). The server half is TASK-14811-T's
// ChildSickAllowanceTileTests. Kinds per case: F for the first three (each fails on the old page, which
// showed "saldo"); R for the last (not opted in: the old page already omitted the tile).
// Red conditions (on the page before the change, which read the yearly remainder the server no longer sends):
//  - the three F cases fail their whole-text match, because the old tile read "Barns sygedag–saldo".
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { YearOverview } from '../../hooks/useYearOverview'
import { renderWithCalendar } from '../../test/renderWithCalendar'

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ user: { employeeId: 'emp001', role: 'Employee' }, role: 'Employee', orgId: 'STY01', agreementCode: 'HK' }),
}))
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return { ...actual, useNavigate: () => vi.fn() }
})
const mockUseYearOverview = vi.fn()
vi.mock('../../hooks/useYearOverview', () => ({
  useYearOverview: (...args: unknown[]) => mockUseYearOverview(...args),
}))

import { ArsoversigtPage } from '../ArsoversigtPage'

function overview(child: { eligible: boolean; days: number | null; callHome: boolean }): YearOverview {
  return {
    employeeId: 'emp001',
    year: 2026,
    today: '2026-03-15',
    header: { employeeName: 'Anna Berg', agreementCode: 'HK', okVersion: 'OK26', weeklyNormHours: 37, fullDayNormHours: 7.4 },
    tiles: {
      flexBalance: null,
      flexBalanceMonth: null,
      ferieRemaining: 22,
      careDayRemaining: 1,
      seniorDayRemaining: null,
      sickDaysYtd: 0,
      childSickDaysPerIllness: child.days,
      childSickCallHomeDay: child.callHome,
      childSickEligible: child.eligible,
      seniorDayEligible: false,
    },
    months: [],
    categories: [],
  }
}

function renderTile(child: Parameters<typeof overview>[0]) {
  mockUseYearOverview.mockReturnValue({ data: overview(child), loading: false, error: null, refetch: vi.fn() })
  renderWithCalendar('2026-03-15', <MemoryRouter><ArsoversigtPage /></MemoryRouter>)
}

/** The tile (label, value and sub-line are siblings in one element). */
function childTile(): HTMLElement {
  return screen.getByText('Barns sygedag').parentElement as HTMLElement
}

beforeEach(() => mockUseYearOverview.mockReset())

describe('ArsoversigtPage — Barns sygedag tile shows the allowance per illness', () => {
  // The whole tile text is pinned (anchored), so a tile kind that adds an hours figure
  // ("22,2 t (3 dage)") or a "saldo" sub-line cannot pass.
  it('F: OK26 shape (3 days + call-home day) reads exactly label, "3 dage", call-home sub-line', () => {
    renderTile({ eligible: true, days: 3, callHome: true })
    expect(childTile().textContent).toMatch(/^Barns sygedag3 dagepr\. sygdomsforløb \+ hjemkaldelsesdag$/)
  })

  it('F: OK24 shape (2 days, no call-home day) reads exactly label, "2 dage", "pr. sygdomsforløb"', () => {
    renderTile({ eligible: true, days: 2, callHome: false })
    expect(childTile().textContent).toMatch(/^Barns sygedag2 dagepr\. sygdomsforløb$/)
  })

  it('F: opted in but no day configured today (null): the dash with "pr. sygdomsforløb"', () => {
    renderTile({ eligible: true, days: null, callHome: false })
    expect(childTile().textContent).toMatch(/^Barns sygedag–pr\. sygdomsforløb$/)
  })

  it('R: not opted in: no tile at all (owner OQ-1; also pinned in ArsoversigtPage.test.tsx)', () => {
    renderTile({ eligible: false, days: null, callHome: false })
    expect(screen.queryByText('Barns sygedag')).not.toBeInTheDocument()
  })
})
