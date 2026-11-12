// S148 / TASK-14841 — AC-CL-28: the day panel's work-time save. A day whose period rows are all
// removed (the panel always keeps one row, so: all rows blank) is posted as `cleared.workTime`; a
// day whose rows are all invalid mid-typing posts NOTHING (the server refuses an empty posted day,
// W-a: 400 skema_work_time_day_empty, and a pause while typing is not an intent to remove). Both
// hold on the debounced path and on month navigation (`firePendingNow` -> `fireWorkTimeSave`).
//
// Kind F. Red conditions: before this task the removed day was posted as `{date, intervals: []}`
// (workTime non-empty with an empty interval list, no `cleared`), and so was the all-invalid day:
// the `cleared.workTime` toEqual and the "nothing posted" / "no empty posted day" assertions fail.
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

async function openDay(date: string) {
  fireEvent.click(await screen.findByRole('button', { name: `Registrér arbejdstid ${date}` }))
  return screen.findByRole('dialog')
}

const noEmptyPostedDay = () =>
  expect(saveBodies.every((b) => (b.workTime ?? []).every((d) => d.intervals.length > 0))).toBe(true)

async function settle() {
  await act(async () => { await new Promise((r) => setTimeout(r, 60)) })
}

/** Hold every save POST until the test answers it: `held[i](status)` answers the i-th. */
function holdSaves() {
  const held: Array<(status: number) => void> = []
  const base = mockFetch.getMockImplementation()!
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (String(url).includes('/api/skema/emp001/save') && (init?.method ?? 'GET') === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      const status = await new Promise<number>((r) => held.push(r))
      return json(status === 200 ? {} : { error: 'boom' }, status)
    }
    return base(url, init)
  })
  return held
}

/** Close the open panel and reopen the day: the rows are seeded from the grid's own day. */
async function reopen(date: string) {
  fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Luk' }))
  return openDay(date)
}

const times = (d: HTMLElement) => [
  (within(d).getByLabelText('Fra') as HTMLInputElement).value,
  (within(d).getByLabelText('Til') as HTMLInputElement).value,
]

/** Send one valid edit (Fra -> 09:00) and leave its POST held; returns the open drawer. */
async function sendNineToSixteenHeld(held: unknown[]) {
  const drawer = await openDay('2026-03-02')
  vi.useFakeTimers()
  fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
  vi.useRealTimers()
  await waitFor(() => expect(held).toHaveLength(1))
  return drawer
}

// The grid must match the server once in-flight saves settle (kind F; red on the code that
// restored a half-typed day to server truth only and never reconciled on the answer).
describe('SkemaPage — the grid follows the newest unanswered work-time save (AC-CL-28, F)', () => {
  it('edit sent and held, second edit, then half-typed: the 200 leaves the grid at the sent times and nothing more is posted', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await sendNineToSixteenHeld(held)
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    held[0](200)
    await settle()
    const reopened = await reopen('2026-03-02')
    expect(times(reopened)).toEqual(['09:00', '16:00'])
    await act(async () => { await new Promise((r) => setTimeout(r, 1200)) })
    expect(saveBodies).toHaveLength(1)
  })

  it('edit sent and held, both fields blanked, Fra half-typed before the debounce: the grid ends at the sent times', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await sendNineToSixteenHeld(held)
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '10' } })
    held[0](200)
    await settle()
    const reopened = await reopen('2026-03-02')
    expect(times(reopened)).toEqual(['09:00', '16:00'])
    await act(async () => { await new Promise((r) => setTimeout(r, 1200)) })
    expect(saveBodies).toHaveLength(1)
  })

  it('removal sent and held, a full period typed, then half-typed: the 200 leaves the grid empty', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(held).toHaveLength(1))
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '16:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    held[0](200)
    await settle()
    const reopened = await reopen('2026-03-02')
    expect(times(reopened)).toEqual(['', ''])
    await act(async () => { await new Promise((r) => setTimeout(r, 1200)) })
    expect(saveBodies).toHaveLength(1)
  })

  // Deleting the `!halfTyped.includes(d)` filter in the failure branch re-queues the day, and this
  // navigation then posts the restored 08:00-16:00 (a second POST): the test goes red.
  it('a 5xx on a day whose panel is half-typed is not re-queued: navigation, panel still half-typed, posts nothing', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await sendNineToSixteenHeld(held)
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    held[0](500)
    await screen.findByRole('alert')
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await settle()
    expect(saveBodies).toHaveLength(1)
  })

  it('a 5xx on a half-typed day restores the grid to the stored day', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await sendNineToSixteenHeld(held)
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    held[0](500)
    await screen.findByRole('alert')
    expect(times(await reopen('2026-03-02'))).toEqual(['08:00', '16:00'])
  })

  it('a removal that failed after the user half-typed and closed the panel is retried as a removal', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(held).toHaveLength(1))
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '16:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))
    held[0](500)
    await screen.findByRole('alert')
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await waitFor(() => expect(saveBodies).toHaveLength(2))
    expect(saveBodies[1].cleared?.workTime).toEqual([{ date: '2026-03-02' }])
    expect(saveBodies[1].workTime).toBeNull()
  })

  it('overlapping saves: the newer is refused (422) first, then the older answers 200: the grid ends at the older save', async () => {
    const held = holdSaves()
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // A leaves and is held
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '10:00' } })
    // B waits behind A for the queue's bound, then leaves too.
    await act(async () => { await vi.advanceTimersByTimeAsync(17000) })
    vi.useRealTimers()
    await waitFor(() => expect(held).toHaveLength(2))
    held[1](422) // B refused first
    await settle()
    held[0](200) // then A answers
    await settle()
    expect(times(await reopen('2026-03-02'))).toEqual(['09:00', '16:00'])
  })
})

describe('SkemaPage — work-time removal vs half-typed day (AC-CL-28, F)', () => {
  it('blanking the only period row posts cleared.workTime and no empty day', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    expect(saveBodies[0].workTime).toBeNull()
    expect(saveBodies[0].cleared).toEqual({ entries: null, absences: null, workTime: [{ date: '2026-03-02' }] })
    noEmptyPostedDay()
  })

  it('a day whose only row is half-typed (invalid) posts nothing', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '1x' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1500) })
    vi.useRealTimers()
    await settle()
    expect(saveBodies).toHaveLength(0)
  })

  it('blank both times, then half-type within the debounce: the pending removal is withdrawn, nothing is posted', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(400) })
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1500) })
    vi.useRealTimers()
    await settle()
    expect(saveBodies).toHaveLength(0)
  })

  it('a pending valid edit is withdrawn when the day goes half-typed: nothing is posted and the grid keeps the stored day', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1500) })
    vi.useRealTimers()
    await settle()
    expect(saveBodies).toHaveLength(0)
    // Reopening seeds from the grid's local day: the stored 08:00-16:00, not 09:00-16:00.
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Luk' }))
    const reopened = await openDay('2026-03-02')
    expect((within(reopened).getByLabelText('Fra') as HTMLInputElement).value).toBe('08:00')
    expect((within(reopened).getByLabelText('Til') as HTMLInputElement).value).toBe('16:00')
  })

  it('a pending valid edit is withdrawn at month navigation too: nothing is posted', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await settle()
    expect(saveBodies).toHaveLength(0)
  })

  it('half-typing AFTER the removal was sent leaves the in-flight save to decide: the grid does not show the removed day', async () => {
    let posted = 0
    let release!: () => void
    const gate = new Promise<void>((r) => { release = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/save')) {
        posted++
        await gate
      }
      return base(url, init)
    })
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // the removal leaves; its answer is held
    vi.useRealTimers()
    await waitFor(() => expect(posted).toBe(1))
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '09' } })
    release()
    await waitFor(() => expect(screen.getByTestId('removal-announcement').textContent).toContain('Registreringen er fjernet'))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Luk' }))
    const reopened = await openDay('2026-03-02')
    expect((within(reopened).getByLabelText('Fra') as HTMLInputElement).value).toBe('')
    expect((within(reopened).getByLabelText('Til') as HTMLInputElement).value).toBe('')
  })

  it('month navigation before the autosave: a removed day posts cleared.workTime', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    expect(saveBodies[0].workTime).toBeNull()
    expect(saveBodies[0].cleared?.workTime).toEqual([{ date: '2026-03-02' }])
    noEmptyPostedDay()
  })

  it('month navigation before the autosave: an all-invalid day posts nothing', async () => {
    await renderLoaded()
    const drawer = await openDay('2026-03-02')
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '1x' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await settle()
    expect(saveBodies).toHaveLength(0)
  })
})
