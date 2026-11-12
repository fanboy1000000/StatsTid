// S148 / TASK-14841 — AC-CL-29 (2): a clear answered 409, 5xx or a network failure keeps the cell
// empty, shows the existing not-saved alert, and is RE-QUEUED (not dropped): the next pending-save
// fire (here: month navigation) posts the same removal again.
//
// Kind F. Red conditions: the emptied cell was never sent before this task, so the first post
// (`saveBodies[0].cleared`) and the retried post (`saveBodies[1].cleared`) both fail; the old
// re-queue also skipped a cell whose live value was undefined (the dropped clear).
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
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

// Stored in March 2026: DRIFT 3 h on the 5th, VACATION 7,4 h on the 6th, work time 08:00-16:00 on
// the 2nd. (2026-03-05 and -06 are weekdays.)
const drift = { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 }
const VACATION = { type: 'VACATION', label: 'Ferie', fullDayOnly: false }

function dailyNorm() {
  const out: { date: string; hours: number | null }[] = []
  for (let d = 1; d <= 31; d++) {
    const date = `2026-03-${String(d).padStart(2, '0')}`
    const dow = new Date(date + 'T00:00:00').getDay()
    out.push({ date, hours: dow === 0 || dow === 6 ? 0 : 7.4 })
  }
  return out
}

function makeMonth(over: Partial<SkemaMonthData> = {}): SkemaMonthData {
  return {
    year: 2026,
    month: 3,
    daysInMonth: 31,
    projects: [drift],
    absenceTypes: [VACATION],
    entries: [{ date: '2026-03-05', projectCode: 'DRIFT', hours: 3 }],
    absences: [{ date: '2026-03-06', absenceType: 'VACATION', hours: 7.4, feriedage: 1 }],
    approval: null,
    employeeDeadline: '2026-04-05',
    managerDeadline: '2026-04-10',
    workTime: [{ date: '2026-03-02', intervals: [{ start: '08:00', end: '16:00' }] }],
    dailyNorm: dailyNorm(),
    rowPreferences: { configured: true, projects: [drift], absenceTypes: [{ ...VACATION, sortOrder: 0 }] },
    catalogs: { projects: [drift], absenceTypes: [VACATION] },
    boundaryWorkTime: [],
    fullDayNormAtMonthEnd: 7.4,
    consumptionBasis: [],
    ...over,
  }
}

const summary = {
  employeeId: 'emp001', year: 2026, month: 3, flexBalance: 0, flexBalanceMonth: '2026-02', flexDelta: 0,
  vacationDaysUsed: 0, vacationDaysEntitlement: 25, normHoursExpected: 162.8, normHoursActual: 0,
  overtimeHours: 0, agreementCode: 'AC', hasMerarbejde: true, entitlements: [], overtimeBalance: null,
}

type SaveBody = {
  entries: { date: string; projectCode: string; hours: number }[] | null
  absences: { date: string; absenceType: string; hours: number }[] | null
  workTime: { date: string; intervals: { start: string; end: string }[] }[] | null
  cleared?: {
    entries: { date: string; projectCode: string }[] | null
    absences: { date: string; absenceType: string }[] | null
    workTime: { date: string }[] | null
  }
}

let monthData: SkemaMonthData
let saveBodies: SaveBody[]
// What the next save answers; default 200. A function so a fact can change it per call.
let saveAnswer: (n: number) => { status: number; body: unknown } | 'network'

function json(body: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, headers: new Headers(), json: async () => body, text: async () => JSON.stringify(body) }
}

beforeEach(() => {
  monthData = makeMonth()
  saveBodies = []
  saveAnswer = () => ({ status: 200, body: {} })
  mockFetch.mockReset()
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('/api/skema/emp001/month')) return json(monthData)
    if (url.includes('/api/balance/emp001/summary')) return json(summary)
    if (url.includes('/api/compliance/')) return json({ ruleId: 'EU_WTD', employeeId: 'emp001', success: true, violations: [], warnings: [] })
    if (url.includes('/api/skema/emp001/save') && (init?.method ?? 'GET') === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      const a = saveAnswer(saveBodies.length)
      if (a === 'network') throw new TypeError('Failed to fetch')
      return json(a.body, a.status)
    }
    return json({})
  })
})

afterEach(() => {
  vi.useRealTimers()
})

async function renderLoaded() {
  const r = renderWithCalendar(
    '2026-03-15',
    <MemoryRouter initialEntries={['/tid/registrering?year=2026&month=3']}>
      <SkemaPage />
    </MemoryRouter>,
  )
  await screen.findByText('Drift & support')
  await screen.findByLabelText('Ferie dag 6')
  return r
}

/** Type into a grid cell and let the one-second autosave fire. */
async function editCell(label: string, value: string) {
  const input = screen.getByLabelText(label) as HTMLInputElement
  vi.useFakeTimers()
  fireEvent.change(input, { target: { value } })
  fireEvent.blur(input)
  await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
  vi.useRealTimers()
}

const answers: [string, { status: number; body: unknown } | 'network'][] = [
  ['409', { status: 409, body: { error: 'period_locked' } }],
  ['500', { status: 500, body: { error: 'boom' } }],
  ['network failure', 'network'],
]

describe('SkemaPage — a failed clear keeps the cell empty and retries (AC-CL-29 (2), F)', () => {
  it.each(answers)('%s: the cell stays empty, the not-saved alert shows, and the removal is re-queued', async (_name, answer) => {
    saveAnswer = (n) => (n === 1 ? answer : { status: 200, body: {} })
    await renderLoaded()
    await editCell('Drift & support dag 5', '')
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    expect(saveBodies[0].cleared?.entries).toEqual([{ date: '2026-03-05', projectCode: 'DRIFT' }])
    await screen.findByRole('alert')
    expect((screen.getByLabelText('Drift & support dag 5') as HTMLInputElement).value).toBe('')

    // The failure re-queued the removal from live state; the next fire posts it again.
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await waitFor(() => expect(saveBodies).toHaveLength(2))
    expect(saveBodies[1].cleared?.entries).toEqual([{ date: '2026-03-05', projectCode: 'DRIFT' }])
    expect(saveBodies[1].entries).toBeNull()
  })
})
