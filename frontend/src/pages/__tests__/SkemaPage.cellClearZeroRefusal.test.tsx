// S148 / TASK-14841 fix-up 1 — a cell edited to 0 is an emptied cell: the page posts it under
// `cleared`, and when the server refuses that removal (422) the stored value comes back.
//
// Why a stand-in grid: the real grid and the day panel both turn 0 into null before the page sees
// it, so only a direct `onCellChange(row, date, 0)` reaches the page's 0 handling.
//
// Kind F. Red conditions: before the fix the page kept a literal 0 in its local cells; the 422
// revert's overlap guard compared that 0 with the sent "absent" and skipped the cell, so the
// stored 3 never came back (the "3" assertion fails).
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { screen, waitFor, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { renderWithCalendar } from '../../test/renderWithCalendar'
import { SkemaPage } from '../SkemaPage'
import type { SkemaMonthData } from '../../types'

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

vi.mock('../../components/SkemaGrid', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../components/SkemaGrid')>()
  return {
    ...actual,
    SkemaGrid: (props: {
      cellValues: Map<string, number>
      onCellChange: (rowKey: string, date: string, hours: number | null) => void
    }) => (
      <div>
        <button type="button" onClick={() => props.onCellChange('DRIFT', '2026-03-05', 0)}>
          zero
        </button>
        <span data-testid="cell">{String(props.cellValues.get('DRIFT:2026-03-05') ?? '')}</span>
      </div>
    ),
  }
})

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const drift = { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 }
const month: SkemaMonthData = {
  year: 2026,
  month: 3,
  daysInMonth: 31,
  projects: [drift],
  absenceTypes: [],
  entries: [{ date: '2026-03-05', projectCode: 'DRIFT', hours: 3 }],
  absences: [],
  approval: null,
  employeeDeadline: '2026-04-05',
  managerDeadline: '2026-04-10',
  workTime: [],
  dailyNorm: [],
  rowPreferences: { configured: true, projects: [drift], absenceTypes: [] },
  catalogs: { projects: [drift], absenceTypes: [] },
  boundaryWorkTime: [],
  fullDayNormAtMonthEnd: 7.4,
  consumptionBasis: [],
}
const summary = {
  employeeId: 'emp001', year: 2026, month: 3, flexBalance: 0, flexBalanceMonth: '2026-02', flexDelta: 0,
  vacationDaysUsed: 0, vacationDaysEntitlement: 25, normHoursExpected: 0, normHoursActual: 0,
  overtimeHours: 0, agreementCode: 'AC', hasMerarbejde: true, entitlements: [], overtimeBalance: null,
}

function json(body: unknown, status = 200) {
  return { ok: status < 300, status, headers: new Headers(), json: async () => body, text: async () => JSON.stringify(body) }
}

let saveBodies: Array<{ cleared?: { entries: { date: string; projectCode: string }[] | null } }>

beforeEach(() => {
  saveBodies = []
  mockFetch.mockReset()
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('/api/skema/emp001/month')) return json(month)
    if (url.includes('/api/balance/emp001/summary')) return json(summary)
    if (url.includes('/api/compliance/')) return json({ ruleId: 'EU_WTD', employeeId: 'emp001', success: true, violations: [], warnings: [] })
    if (url.includes('/api/skema/emp001/save') && (init?.method ?? 'GET') === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      return json({ error: 'absence_type_not_eligible', absenceType: 'DRIFT', date: '2026-03-05', message: 'Nej, den kan ikke fjernes.' }, 422)
    }
    return json({})
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('SkemaPage — a 0-edit removal refused with 422 restores the stored value (F)', () => {
  it('posts cleared.entries for the 0, then puts the stored 3 back', async () => {
    renderWithCalendar(
      '2026-03-15',
      <MemoryRouter initialEntries={['/tid/registrering?year=2026&month=3']}>
        <SkemaPage />
      </MemoryRouter>,
    )
    await waitFor(() => expect(screen.getByTestId('cell').textContent).toBe('3'))
    fireEvent.click(screen.getByRole('button', { name: 'zero' }))
    await waitFor(() => expect(screen.getByTestId('cell').textContent).toBe(''))
    await waitFor(() => expect(saveBodies).toHaveLength(1), { timeout: 3000 })
    expect(saveBodies[0].cleared?.entries).toEqual([{ date: '2026-03-05', projectCode: 'DRIFT' }])
    await screen.findByText(/Nej, den kan ikke fjernes/)
    await waitFor(() => expect(screen.getByTestId('cell').textContent).toBe('3'))
  })
})
