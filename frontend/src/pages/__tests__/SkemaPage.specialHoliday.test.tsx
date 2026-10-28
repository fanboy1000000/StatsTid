// S147 / TASK-14702 — AC-235-6: the "Særlig feriedag" row renders in Skema from the SERVED catalogue.
//
// Why: TASK-14701 makes the server serve a new absence-type row, SPECIAL_HOLIDAY_ALLOWANCE
// (label "Særlig feriedag"). Skema draws its absence rows from what the server sends, so the row
// should appear and be bookable with NO frontend change. This test proves that.
//
// Fact kind: B — boundary pin — holds today. Skema renders served absence rows generically
// (rows come from `deriveSkemaRowBasis` over the served `absenceTypes` / `catalogs.absenceTypes`;
// no hard-coded type list gates the grid), so NO component change was needed. Not F: no assertion
// fails on today's code. Red conditions (what would break it): a Skema change that filters the
// grid rows or the save payload by a fixed list of absence codes (SPECIAL_HOLIDAY_ALLOWANCE
// would vanish or not save).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor, fireEvent, act } from '@testing-library/react'
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

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const SPECIAL = { type: 'SPECIAL_HOLIDAY_ALLOWANCE', label: 'Særlig feriedag', fullDayOnly: false }

function dailyNorm() {
  const out: { date: string; hours: number | null }[] = []
  for (let d = 1; d <= 31; d++) {
    const date = `2026-03-${String(d).padStart(2, '0')}`
    const dow = new Date(date + 'T00:00:00').getDay()
    out.push({ date, hours: dow === 0 || dow === 6 ? 0 : 7.4 })
  }
  return out
}

const drift = { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 }

const monthData: SkemaMonthData = {
  year: 2026,
  month: 3,
  daysInMonth: 31,
  projects: [drift],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }, SPECIAL],
  entries: [],
  absences: [],
  approval: null,
  employeeDeadline: '2026-04-05',
  managerDeadline: '2026-04-10',
  workTime: [],
  dailyNorm: dailyNorm(),
  rowPreferences: {
    configured: true,
    projects: [drift],
    absenceTypes: [
      { type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 },
      { ...SPECIAL, sortOrder: 1 },
    ],
  },
  catalogs: {
    projects: [drift],
    absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }, SPECIAL],
  },
  boundaryWorkTime: [],
  fullDayNormAtMonthEnd: 7.4,
  consumptionBasis: [],
}

const summary = {
  employeeId: 'emp001',
  year: 2026,
  month: 3,
  flexBalance: 0,
  flexBalanceMonth: '2026-02',
  flexDelta: 0,
  vacationDaysUsed: 0,
  vacationDaysEntitlement: 25,
  normHoursExpected: 162.8,
  normHoursActual: 0,
  overtimeHours: 0,
  agreementCode: 'AC',
  hasMerarbejde: true,
  entitlements: [],
  overtimeBalance: null,
}

let saveBodies: Array<{ absences: { date: string; absenceType: string; hours: number }[] | null }>

function json(body: unknown) {
  return { ok: true, status: 200, headers: new Headers(), json: async () => body, text: async () => JSON.stringify(body) }
}

beforeEach(() => {
  saveBodies = []
  mockFetch.mockReset()
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('/api/skema/emp001/month')) return json(monthData)
    if (url.includes('/api/balance/emp001/summary')) return json(summary)
    if (url.includes('/api/compliance/')) {
      return json({ ruleId: 'EU_WTD', employeeId: 'emp001', success: true, violations: [], warnings: [] })
    }
    if (url.includes('/api/skema/emp001/save') && (init?.method ?? 'GET') === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      return json({})
    }
    return json({})
  })
})

function gridRow(container: HTMLElement, label: string): HTMLElement | null {
  const rows = Array.from(container.querySelectorAll('tbody tr'))
  return (rows.find((r) => r.querySelector('td')?.textContent?.startsWith(label)) as HTMLElement) ?? null
}

describe('SkemaPage — AC-235-6: the served SPECIAL_HOLIDAY_ALLOWANCE row (B, boundary pin — holds today)', () => {
  it('shows the "Særlig feriedag" row and books an absence on it', async () => {
    const { container } = renderWithCalendar(
      '2026-03-15',
      <MemoryRouter initialEntries={['/tid/registrering?year=2026&month=3']}>
        <SkemaPage />
      </MemoryRouter>,
    )
    await screen.findByText('Drift & support')

    // Visible: a grid row labelled by the served label.
    await waitFor(() => expect(gridRow(container, 'Særlig feriedag')).not.toBeNull())

    // Bookable: an editable day cell, whose edit is saved under the served type code.
    const input = (await screen.findByLabelText('Særlig feriedag dag 5')) as HTMLInputElement
    expect(input.disabled).toBe(false)
    vi.useFakeTimers()
    fireEvent.change(input, { target: { value: '7,4' } })
    fireEvent.blur(input)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    expect(saveBodies[0].absences).toEqual([
      { date: '2026-03-05', absenceType: 'SPECIAL_HOLIDAY_ALLOWANCE', hours: 7.4 },
    ])
  })
})
