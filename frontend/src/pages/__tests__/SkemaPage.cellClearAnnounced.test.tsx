// S148 / TASK-14841 — AC-CL-30: a successful removal of something that was really saved is
// announced once in the polite live region ("Registreringen er fjernet"); a removal of something
// the screen's server truth never held (N-3) is not. The announcement has its own region: the
// existing status region speaks "Gemmer…" while a save runs and would overwrite it.
//
// Kind F. Red conditions: before this task no region carries the text, so every positive
// assertion (the region reads "Registreringen er fjernet") fails.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { screen, waitFor, fireEvent, act, within } from '@testing-library/react'
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

/** The always-mounted removal announcement region (not the "Indlæser…/Gemmer…" status region). */
function announcement(): HTMLElement {
  return screen.getByTestId('removal-announcement')
}

async function openDay(date: string) {
  fireEvent.click(await screen.findByRole('button', { name: `Registrér arbejdstid ${date}` }))
  return screen.findByRole('dialog')
}

const TEXT = 'Registreringen er fjernet'
const spoken = () => announcement().textContent?.replace(/\s+/g, ' ').trim()

describe('SkemaPage — the removal is announced once (AC-CL-30, F)', () => {
  it('the region is mounted and silent before any removal', async () => {
    await renderLoaded()
    expect(announcement().textContent).toBe('')
  })

  it('removing a stored cell is announced after the 200', async () => {
    await renderLoaded()
    await editCell('Drift & support dag 5', '')
    await waitFor(() => expect(spoken()).toBe(TEXT))
  })

  it('two removals in one save are announced once, not twice', async () => {
    await renderLoaded()
    const a = screen.getByLabelText('Drift & support dag 5') as HTMLInputElement
    const b = screen.getByLabelText('Ferie dag 6') as HTMLInputElement
    vi.useFakeTimers()
    fireEvent.change(a, { target: { value: '' } })
    fireEvent.change(b, { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(spoken()).toBe(TEXT))
    expect(saveBodies).toHaveLength(1)
    // One announcement = the text once, not doubled.
    expect(announcement().textContent?.match(/Registreringen er fjernet/g)).toHaveLength(1)
  })

  it('removing a stored work-time day is announced', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(spoken()).toBe(TEXT))
  })

  it('a removal of a cell the server never held (typed and emptied before the save) is not announced', async () => {
    await renderLoaded()
    const input = screen.getByLabelText('Drift & support dag 9') as HTMLInputElement
    vi.useFakeTimers()
    fireEvent.change(input, { target: { value: '4' } })
    fireEvent.change(input, { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    expect(saveBodies[0].cleared?.entries).toEqual([{ date: '2026-03-09', projectCode: 'DRIFT' }])
    await act(async () => { await new Promise((r) => setTimeout(r, 60)) })
    expect(announcement().textContent).toBe('')
  })

  it('a refused (422) removal is not announced', async () => {
    saveAnswer = () => ({ status: 422, body: { error: 'absence_type_not_eligible', absenceType: 'VACATION', date: '2026-03-06', message: 'Nej.' } })
    await renderLoaded()
    await editCell('Ferie dag 6', '')
    await screen.findByText(/Nej\./)
    expect(announcement().textContent).toBe('')
  })
})
