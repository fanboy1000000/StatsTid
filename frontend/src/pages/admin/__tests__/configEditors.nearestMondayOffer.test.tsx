// AC-C2b (2) (NR-P14, NR-P17; rulings 29, 32, 33). Kind F.
// Red conditions: before the change neither page sends a date or reads the refusal's `fields`. The
// agreement editor's publish dialog has no date input (getByLabelText('Gælder fra') throws), and the
// override page's edit row has no date input (getByLabelText('Ændringen gælder fra') throws) and its
// Deaktiver is one click with no "Deaktiveres fra" input, so every case below fails at its first
// step and, past that, at the offer assertions (no Monday sentence, no "Brug …" button).
//
// Today is Thursday 12 November 2026 throughout. Each case runs on BOTH pages:
//   (a) newest version Monday 2 Nov, the write dated Wed 11 Nov, the server names Mon 9 and Mon 16:
//       Monday 9 November is offered (its button sets the date field), 16 November never.
//   (b) newest version Tue 10 Nov, same refusal: 9 Nov is before the version, 16 Nov is after today,
//       so no date is offered and the text names Monday 16 November 2026 as the earliest.
//   (c) newest version Monday 2 Nov, the write dated Tue 3 Nov, the server names Mon 2 and Mon 9:
//       both are admissible, both are offered, Monday 2 November first.
// The override page also runs the deactivate arm (a norm-setting row): the date is asked and sent.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgreementConfigEditor } from '../AgreementConfigEditor'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import type { PositionOverrideConfig } from '../../../hooks/usePositionOverrides'
import { CONFIG_ID, draftConfig } from './agreementConfigFixture'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'GlobalAdmin' }),
}))

const TODAY = '2026-11-12'
const LEAD = 'Ændringen påvirker ugeberegningen og skal derfor gælde fra en mandag.'

interface Case {
  name: string
  newest: string
  typed: string
  nearestValid: string[]
  text: string
  buttons: { label: string; date: string }[]
}

const CASES: Case[] = [
  {
    name: '(a) one admissible Monday is offered, the later one never',
    newest: '2026-11-02',
    typed: '2026-11-11',
    nearestValid: ['2026-11-09', '2026-11-16'],
    text: `${LEAD} Du kan bruge mandag den 9. november 2026.`,
    buttons: [{ label: 'Brug 9. november 2026', date: '2026-11-09' }],
  },
  {
    name: '(b) no admissible Monday: no date offered, the earliest is named',
    newest: '2026-11-10',
    typed: '2026-11-11',
    nearestValid: ['2026-11-09', '2026-11-16'],
    text: `${LEAD} Ingen mandag kan bruges i dag: den tidligst mulige dato er mandag den 16. november 2026, og ændringen kan først gemmes den dag.`,
    buttons: [],
  },
  {
    name: '(c) both Mondays are admissible: both offered, in date order',
    newest: '2026-11-02',
    typed: '2026-11-03',
    nearestValid: ['2026-11-02', '2026-11-09'],
    text: `${LEAD} Du kan bruge mandag den 2. november 2026 eller mandag den 9. november 2026.`,
    buttons: [
      { label: 'Brug 2. november 2026', date: '2026-11-02' },
      { label: 'Brug 9. november 2026', date: '2026-11-09' },
    ],
  },
]

function refusal(nearestValid: string[]) {
  return {
    ok: false,
    status: 400,
    headers: new Headers(),
    text: async () =>
      JSON.stringify({
        error: 'Applies-from date validation failed',
        fields: [{ field: 'WeeklyNormHours', code: 'NOT_MONDAY_ALIGNED', nearestValid }],
      }),
  }
}

function okJson(body: unknown) {
  return { ok: true, status: 200, headers: new Headers({ ETag: '"3"' }), json: async () => body }
}

function expectOffer(c: Case) {
  const alert = screen.getByRole('alert')
  expect(alert.textContent).toContain(c.text)
  if (c.nearestValid.includes('2026-11-16')) expect(alert.textContent).not.toContain('Du kan bruge mandag den 16.')
  const brugButtons = screen.queryAllByRole('button', { name: /^Brug / })
  expect(brugButtons.map((b) => b.textContent)).toEqual(c.buttons.map((b) => b.label))
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('the agreement editor: a mid-week refusal offers only what the server accepts', () => {
  it.each(CASES)('$name', async (c) => {
    const posts: { url: string; body: unknown }[] = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        posts.push({ url, body: init.body ? JSON.parse(String(init.body)) : undefined })
        return refusal(c.nearestValid)
      }
      if (url.endsWith('/AC/OK24')) {
        return okJson([
          { ...draftConfig, configId: 'v0', status: 'ARCHIVED', publishedAt: '2026-10-01T08:00:00Z', appliesFrom: '2026-10-01' },
          { ...draftConfig, configId: 'v1', status: 'ACTIVE', publishedAt: '2026-11-01T08:00:00Z', appliesFrom: c.newest },
          draftConfig,
        ])
      }
      return okJson(draftConfig)
    })
    renderWithCalendar(
      TODAY,
      <MemoryRouter initialEntries={[`/global/overenskomster/${CONFIG_ID}`]}>
        <Routes>
          <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
        </Routes>
      </MemoryRouter>,
    )
    await screen.findByText('AC / OK24')
    fireEvent.click(screen.getByRole('button', { name: 'Publicer' }))

    // The lower limit is the newest published version's date, read from the version list.
    const date = screen.getByLabelText('Gælder fra')
    await waitFor(() => expect(date.getAttribute('min')).toBe(c.newest))
    expect(date.getAttribute('max')).toBe(TODAY)
    expect((date as HTMLInputElement).value).toBe(TODAY)

    fireEvent.change(date, { target: { value: c.typed } })
    const publishButtons = screen.getAllByRole('button', { name: 'Publicer' })
    fireEvent.click(publishButtons[publishButtons.length - 1])

    await waitFor(() => expect(screen.getByRole('alert')).toBeDefined())
    expect(posts[0].url).toContain('/publish')
    expect(posts[0].body).toEqual({ appliesFrom: c.typed })
    expectOffer(c)

    for (const b of c.buttons) {
      fireEvent.click(screen.getByRole('button', { name: b.label }))
      expect((screen.getByLabelText('Gælder fra') as HTMLInputElement).value).toBe(b.date)
      expect(screen.queryByRole('alert')).toBeNull()
      break
    }
  })
})

const overrideRow = (appliesFrom: string): PositionOverrideConfig => ({
  overrideId: '11111111-1111-1111-1111-111111111111',
  agreementCode: 'AC',
  okVersion: 'OK24',
  positionCode: 'DEPARTMENT_HEAD',
  status: 'ACTIVE',
  version: 1,
  maxFlexBalance: null,
  normPeriodWeeks: 4,
  weeklyNormHours: 40,
  createdBy: 'admin',
  createdAt: '2026-03-08T00:00:00Z',
  updatedAt: '2026-03-08T00:00:00Z',
  appliesFrom,
  description: null,
})

function mountOverridePage(c: Case, posts: { url: string; method: string; body: unknown }[]) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'PUT') {
      posts.push({ url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined })
      return refusal(c.nearestValid)
    }
    return okJson([overrideRow(c.newest)])
  })
  renderWithCalendar(TODAY, <PositionOverrideManagement />)
}

describe('the override page: a mid-week refusal offers only what the server accepts', () => {
  describe.each(CASES)('edit — $name', (c) => {
    it('asks the date, sends it, and offers the Mondays', async () => {
      const posts: { url: string; method: string; body: unknown }[] = []
      mountOverridePage(c, posts)
      await screen.findByText('DEPARTMENT_HEAD')
      fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))

      const date = screen.getByLabelText('Ændringen gælder fra')
      expect(date.getAttribute('min')).toBe(c.newest)
      expect(date.getAttribute('max')).toBe(TODAY)
      fireEvent.change(date, { target: { value: c.typed } })
      fireEvent.click(screen.getByRole('button', { name: 'Gem' }))

      await waitFor(() => expect(screen.getByRole('alert')).toBeDefined())
      expect(posts[0].method).toBe('PUT')
      expect(posts[0].body).toMatchObject({ appliesFrom: c.typed })
      expectOffer(c)

      if (c.buttons.length > 0) {
        fireEvent.click(screen.getByRole('button', { name: c.buttons[0].label }))
        expect((screen.getByLabelText('Ændringen gælder fra') as HTMLInputElement).value).toBe(c.buttons[0].date)
      }
    })
  })

  describe.each(CASES)('deactivate — $name', (c) => {
    it('asks the date, sends it, and offers the Mondays', async () => {
      const posts: { url: string; method: string; body: unknown }[] = []
      mountOverridePage(c, posts)
      await screen.findByText('DEPARTMENT_HEAD')
      fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))

      const date = screen.getByLabelText('Deaktiveres fra')
      expect(date.getAttribute('min')).toBe(c.newest)
      expect(date.getAttribute('max')).toBe(TODAY)
      fireEvent.change(date, { target: { value: c.typed } })
      fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))

      await waitFor(() => expect(screen.getByRole('alert')).toBeDefined())
      expect(posts[0].url).toContain('/deactivate')
      expect(posts[0].body).toEqual({ appliesFrom: c.typed })
      expectOffer(c)

      if (c.buttons.length > 0) {
        fireEvent.click(screen.getByRole('button', { name: c.buttons[0].label }))
        expect((screen.getByLabelText('Deaktiveres fra') as HTMLInputElement).value).toBe(c.buttons[0].date)
      }
    })
  })
})

describe('other refusals and other errors', () => {
  it('a future date answers with the not-after-today sentence and a button that sets today', async () => {
    const posts: { url: string; method: string; body: unknown }[] = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        posts.push({ url, method: init.method, body: undefined })
        return {
          ok: false,
          status: 400,
          headers: new Headers(),
          text: async () =>
            JSON.stringify({
              error: 'Applies-from date validation failed',
              fields: [{ field: 'AppliesFrom', code: 'EFFECTIVE_FROM_NOT_TODAY_OR_PAST', nearestValid: [TODAY] }],
            }),
        }
      }
      return okJson([overrideRow('2026-11-02')])
    })
    renderWithCalendar(TODAY, <PositionOverrideManagement />)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '2026-11-20' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Datoen kan ikke ligge efter i dag.'))
    fireEvent.click(screen.getByRole('button', { name: 'Brug dags dato' }))
    expect((screen.getByLabelText('Ændringen gælder fra') as HTMLInputElement).value).toBe(TODAY)
  })

  // R (ruling 33): an error that is not one of the three date codes shows as it did before.
  it('a 400 without a date code keeps the page\'s own message (no Monday sentence)', async () => {
    mockFetch.mockImplementation(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return {
          ok: false,
          status: 400,
          headers: new Headers(),
          text: async () => JSON.stringify({ error: 'Something else is wrong', fields: [{ field: 'X', code: 'OTHER' }] }),
        }
      }
      return okJson([overrideRow('2026-11-02')])
    })
    renderWithCalendar(TODAY, <PositionOverrideManagement />)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Opret ny' }))
    fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
    fireEvent.change(screen.getByLabelText(/^OK Version/), { target: { value: 'OK26' } })
    fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: 'NEW' } })
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))
    await waitFor(() => expect(screen.getByText(/Something else is wrong/)).toBeDefined())
    expect(screen.queryByText(/mandag/)).toBeNull()
  })
})
