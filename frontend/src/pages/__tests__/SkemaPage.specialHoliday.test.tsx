// S147 / TASK-14702 — AC-235-6: the "Særlig feriedag" row renders in Skema from the SERVED catalogue.
//
// Why: TASK-14701 makes the server serve a new absence-type row, SPECIAL_HOLIDAY_ALLOWANCE
// (label "Særlig feriedag"). Skema draws its absence rows from what the server sends, so the row
// should appear and be bookable with NO frontend change. This test proves that.
//
// Fact kind: B — boundary pin — holds today. Skema renders served absence rows generically; which
// rows show is gated by the USER'S ROW PREFERENCES (their saved selection ∩ the served catalogue;
// an unconfigured user sees the served rows), with `deriveSkemaRowBasis` supplying the basis. No
// hard-coded type list gates the grid, so NO component change was needed. Not F: no assertion
// fails on today's code. Red conditions (what would break it): a Skema change that filters the
// grid rows or the save payload by a fixed list of absence codes (SPECIAL_HOLIDAY_ALLOWANCE
// would vanish or not save).
//
// A user's saved row selection is authoritative (ruling R4): someone who customised their rows
// BEFORE this type existed does not get the new row automatically — they add it through
// "Administrer rækker". Demo employees have no saved selection, so they see it at once.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import userEvent from '@testing-library/user-event'
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

// null = the default fixture (preferences include the special-holiday row).
let prefsOverride: SkemaMonthData['rowPreferences'] | null = null
let saveReject: { status: number; body: unknown } | null = null
let saveBodies: Array<{ absences: { date: string; absenceType: string; hours: number }[] | null }>

function json(body: unknown) {
  return { ok: true, status: 200, headers: new Headers(), json: async () => body, text: async () => JSON.stringify(body) }
}

beforeEach(() => {
  saveBodies = []
  saveReject = null
  prefsOverride = null
  mockFetch.mockReset()
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('/api/skema/emp001/month')) return json(prefsOverride ? { ...monthData, rowPreferences: prefsOverride } : monthData)
    if (url.includes('/api/balance/emp001/summary')) return json(summary)
    if (url.includes('/api/compliance/')) {
      return json({ ruleId: 'EU_WTD', employeeId: 'emp001', success: true, violations: [], warnings: [] })
    }
    if (url.includes('/api/skema/emp001/save') && (init?.method ?? 'GET') === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      if (saveReject) {
        const rej = saveReject
        return { ok: false, status: rej.status, headers: new Headers(), json: async () => rej.body, text: async () => JSON.stringify(rej.body) }
      }
      return json({})
    }
    return json({})
  })
})

afterEach(() => {
  vi.useRealTimers()
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
    fireEvent.change(input, { target: { value: '3,7' } })
    fireEvent.blur(input)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    await waitFor(() => expect(saveBodies).toHaveLength(1))
    // A part-day (3,7 h) is accepted: the frontend half of AC-235-7 (fractions of a day allowed).
    expect(saveBodies[0].absences).toEqual([
      { date: '2026-03-05', absenceType: 'SPECIAL_HOLIDAY_ALLOWANCE', hours: 3.7 },
    ])
  })

  // S147 TASK-14702 fix-up 2 - B, boundary pin — holds today: a user's saved row selection is
  // authoritative (R4). Preferences configured WITHOUT the special-holiday row hide it from the
  // grid even though the server serves it, and the row manager offers it as addable.
  // Red conditions: the grid showing the row despite the saved selection, or the manager not
  // listing it under "Tilføj fra katalog" (the user would have no way to add it).
  it('a user whose saved rows omit it sees no "Særlig feriedag" row, but can add it in "Administrer rækker"', async () => {
    prefsOverride = {
      configured: true,
      projects: [drift],
      absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 }],
    }
    const { container } = renderWithCalendar(
      '2026-03-15',
      <MemoryRouter initialEntries={['/tid/registrering?year=2026&month=3']}>
        <SkemaPage />
      </MemoryRouter>,
    )
    await screen.findByText('Drift & support')
    await screen.findByLabelText('Ferie dag 5')
    expect(gridRow(container, 'Særlig feriedag')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    await userEvent.setup().click(screen.getByRole('tab', { name: /Ferie og fravær/ }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Tilføj Særlig feriedag' })).toBeEnabled()
  })

  // This fact pins the NO-`message` FALLBACK (the served catalogue's label). Today's server never
  // triggers it: every rule 422 carries a `message`, and this mock deliberately sends none.
  // History (TASK-14702 fix-up, kind F, fails on that fix-up's pre-fix code): the rule alert fell back
  // to the raw code SPECIAL_HOLIDAY_ALLOWANCE. (The QUOTA alert already resolved, because the server
  // keys it on the entitlement type SPECIAL_HOLIDAY.)
  // S147 QUAL-231 (TASK-14709), kind F: the label map is gone from the rule alerts; with no `message`
  // the alert names the code by the SERVED CATALOGUE's label ("Særlig feriedag", the name the row
  // shows). Was: "Særlige feriedage".
  // Red conditions (QUAL-231): on the pre-change code the alert reads "Særlige feriedage skal
  // registreres som en hel dag..." (the label map), failing the toContain('Særlig feriedag skal...')
  // assertion. Also breaks if the fallback stops reading the catalogue (the raw code shows).
  it('names the absence type by its served catalogue label (not the raw code) in a full-day rule error with no message', async () => {
    saveReject = {
      status: 422,
      body: { error: 'absence_full_day_only', absenceType: 'SPECIAL_HOLIDAY_ALLOWANCE', date: '2026-03-05', requiredHours: 7.4 },
    }
    renderWithCalendar(
      '2026-03-15',
      <MemoryRouter initialEntries={['/tid/registrering?year=2026&month=3']}>
        <SkemaPage />
      </MemoryRouter>,
    )
    await screen.findByText('Drift & support')
    const input = (await screen.findByLabelText('Særlig feriedag dag 5')) as HTMLInputElement
    vi.useFakeTimers()
    fireEvent.change(input, { target: { value: '7,4' } })
    fireEvent.blur(input)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    const alert = await screen.findByText(/skal registreres som en hel dag/)
    // S147 QUAL-231: was toContain('Særlige feriedage').
    expect(alert.textContent).toContain('Særlig feriedag skal registreres som en hel dag')
    expect(alert.textContent).not.toContain('SPECIAL_HOLIDAY_ALLOWANCE')
  })
})
