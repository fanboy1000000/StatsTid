// S148 / TASK-14812 (QUAL-236) — AC-S0-2h part (2): the Årsoversigt's "Barns sygedag" tile
// shows the allowance PER ILLNESS the server serves (whole days, plus the call-home day when the
// server says it applies), never a yearly remainder ("saldo"). The server half is TASK-14811-T's
// ChildSickAllowanceTileTests. Kind F (vitest).
// Red conditions (on the page before the change, which read the yearly remainder the server no longer sends):
//  - the OK26 and OK24 cases fail at "3 dage" / "2 dage" (the tile showed an em dash) and at
//    the absence of "saldo" on the child tile (it carried that sub-line).
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
  it('OK26 shape (3 days + call-home day): "3 dage" and the call-home sub-line, no "saldo"', () => {
    renderTile({ eligible: true, days: 3, callHome: true })
    const tile = childTile()
    expect(tile).toHaveTextContent('3 dage')
    expect(tile).toHaveTextContent('pr. sygdomsforløb + hjemkaldelsesdag')
    expect(tile).not.toHaveTextContent('saldo')
  })

  it('OK24 shape (2 days, no call-home day): "2 dage" and "pr. sygdomsforløb" alone', () => {
    renderTile({ eligible: true, days: 2, callHome: false })
    const tile = childTile()
    expect(tile).toHaveTextContent('2 dage')
    expect(tile).toHaveTextContent('pr. sygdomsforløb')
    expect(tile).not.toHaveTextContent('hjemkaldelsesdag')
    expect(tile).not.toHaveTextContent('saldo')
  })

  it('not opted in: no tile at all (owner OQ-1; also pinned in ArsoversigtPage.test.tsx)', () => {
    renderTile({ eligible: false, days: null, callHome: false })
    expect(screen.queryByText('Barns sygedag')).not.toBeInTheDocument()
  })
})
