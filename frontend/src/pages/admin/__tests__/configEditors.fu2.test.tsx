// S148 T23 FU-2 (cycle-2 ruling C2-R1). Kind F, one section per fact.
// Red conditions: on a10cd057 a date with a five-digit year passes the guard and is sent; the answer
// to an earlier submission closes or fills whatever is on screen by then; a BEFORE_PREDECESSOR
// answer is forgotten at once, so the next Monday offer goes below it again; the publish date's
// aria-describedby points at an element that does not exist yet, and "Prøv igen" leaves focus on
// <body>; the seeded anchor date prints as a long date.
//
// Today is Thursday 12 November 2026 throughout.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor, fireEvent, within } from '@testing-library/react'
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
const FIVE_DIGIT_YEAR = '12026-11-11'

interface Call {
  url: string
  method: string
  body: unknown
}

const jsonResponse = (status: number, body: unknown) => ({
  ok: true,
  status,
  headers: new Headers({ ETag: '"3"' }),
  json: async () => body,
})

const failure = (status: number, text: string) => ({
  ok: false,
  status,
  headers: new Headers(),
  text: async () => text,
})

const refusal = (code: string, nearestValid: string[]) =>
  failure(
    400,
    JSON.stringify({
      error: 'Applies-from date validation failed',
      fields: [{ field: 'AppliesFrom', code, nearestValid }],
    }),
  )

const beforePredecessor = (date: string) => refusal('BEFORE_PREDECESSOR', [date])
const notMonday = (nearestValid: string[]) => refusal('NOT_MONDAY_ALIGNED', nearestValid)

beforeEach(() => {
  mockFetch.mockReset()
})

// ---------------------------------------------------------------------------------- override page

const row = (
  overrideId: string,
  appliesFrom: string,
  positionCode = 'DEPARTMENT_HEAD',
  status: 'ACTIVE' | 'INACTIVE' = 'ACTIVE',
): PositionOverrideConfig => ({
  overrideId,
  agreementCode: 'AC',
  okVersion: 'OK24',
  positionCode,
  status,
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

/** A write that stays pending until `release(...)` answers it. */
function held() {
  let release: (v: unknown) => void = () => {}
  const promise = new Promise((resolve) => { release = resolve })
  return { promise, release: (v: unknown) => release(v) }
}

function mountOverrides(
  rows: PositionOverrideConfig[],
  calls: Call[],
  onWrite: (call: Call) => unknown = () => jsonResponse(200, rows[0]),
) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'PUT') {
      const call = { url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined }
      calls.push(call)
      return onWrite(call)
    }
    return jsonResponse(200, rows)
  })
  renderWithCalendar(TODAY, <PositionOverrideManagement />)
}

const dateOf = (label: string) => screen.getByLabelText(label) as HTMLInputElement
const button = (name: string) => screen.getByRole('button', { name }) as HTMLButtonElement

describe('(a) a date that is not exactly yyyy-MM-dd sends nothing', () => {
  it('override edit (a seeded row, lower bound 0001-01-01): a five-digit year disables "Gem", no request', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '0001-01-01')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: FIVE_DIGIT_YEAR } })
    expect(button('Gem').disabled).toBe(true)
    fireEvent.click(button('Gem'))
    expect(calls).toHaveLength(0)
    // The ordinary in-bounds date still works.
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-10' } })
    expect(button('Gem').disabled).toBe(false)
  })

  it('override create: a five-digit year disables "Opret" and a submit sends no request', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Opret ny'))
    fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
    fireEvent.change(screen.getByLabelText(/^OK Version/), { target: { value: 'OK24' } })
    fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: 'NEW' } })
    const date = dateOf('Gælder fra')
    fireEvent.change(date, { target: { value: FIVE_DIGIT_YEAR } })
    expect(button('Opret').disabled).toBe(true)
    fireEvent.submit(date.closest('form') as HTMLFormElement)
    expect(calls).toHaveLength(0)
  })
})

describe('(b) a late answer never touches a newer interaction (override page)', () => {
  it('a success after "Annuller" and a new edit leaves the new edit open with its date', async () => {
    const calls: Call[] = []
    const write = held()
    mountOverrides(
      [row('o1', '2026-11-02'), row('o2', '2026-11-02', 'OTHER')],
      calls,
      () => write.promise,
    )
    await screen.findByText('OTHER')
    fireEvent.click(screen.getAllByRole('button', { name: 'Rediger' })[0])
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(calls).toHaveLength(1))

    fireEvent.click(button('Annuller'))
    fireEvent.click(screen.getAllByRole('button', { name: 'Rediger' })[1])
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-10' } })

    const listReads = () => mockFetch.mock.calls.filter(([, init]) => init?.method !== 'PUT' && init?.method !== 'POST').length
    const before = listReads()
    write.release(jsonResponse(200, row('o1', TODAY)))
    // The write refreshed the list and has finished; the new edit is untouched.
    await waitFor(() => expect(listReads()).toBeGreaterThan(before))
    await waitFor(() => expect(button('Gem').disabled).toBe(false))
    expect(dateOf('Ændringen gælder fra').value).toBe('2026-11-10')
  })

  it('a refusal for a date the user has since changed shows no refusal', async () => {
    const calls: Call[] = []
    const write = held()
    mountOverrides([row('o1', '2026-11-02')], calls, () => write.promise)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-11' } })
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(calls).toHaveLength(1))

    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-10' } })
    write.release(notMonday(['2026-11-09', '2026-11-16']))
    await waitFor(() => expect(button('Gem').disabled).toBe(false))
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('(c) the override page remembers the server floor', () => {
  it('after BEFORE_PREDECESSOR 10 Nov, a Monday answer of 9 and 16 Nov offers no 9 Nov', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls, () =>
      calls.length === 1 ? beforePredecessor('2026-11-10') : notMonday(['2026-11-09', '2026-11-16']),
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-05' } })
    fireEvent.click(button('Gem'))
    await screen.findByRole('button', { name: 'Brug 10. november 2026' })
    expect(dateOf('Ændringen gælder fra').getAttribute('min')).toBe('2026-11-10')

    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-11' } })
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(calls).toHaveLength(2))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Ingen mandag'))
    expect(screen.getByRole('alert').textContent).toContain('mandag den 16. november 2026')
    expect(screen.queryByRole('button', { name: 'Brug 9. november 2026' })).toBeNull()
  })
})

describe('(f) the seeded anchor date', () => {
  it('shows 0001-01-01 as stored; other dates keep the long form', async () => {
    mountOverrides([row('o1', '0001-01-01'), row('o2', '2026-11-02', 'OTHER')], [])
    await screen.findByText('OTHER')
    expect(screen.getByText('0001-01-01')).toBeDefined()
    expect(screen.getByText('2. november 2026')).toBeDefined()
  })
})

// ---------------------------------------------------------------------------------- agreement editor

function mountEditor(
  versions: () => unknown,
  posts: Call[],
  onPublish: (call: Call) => unknown = () => jsonResponse(200, {}),
) {
  const reads = { versions: 0, config: 0 }
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      const call = { url, method: 'POST', body: init.body ? JSON.parse(String(init.body)) : undefined }
      posts.push(call)
      return onPublish(call)
    }
    if (url.endsWith('/AC/OK24')) {
      reads.versions += 1
      return versions()
    }
    reads.config += 1
    return jsonResponse(200, draftConfig)
  })
  renderWithCalendar(
    TODAY,
    <MemoryRouter initialEntries={[`/global/overenskomster/${CONFIG_ID}`]}>
      <Routes>
        <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
        <Route path="/global/overenskomster" element={<div>list</div>} />
      </Routes>
    </MemoryRouter>,
  )
  return reads
}

const published = (appliesFrom: string) => [
  { ...draftConfig, configId: 'v1', status: 'ACTIVE', publishedAt: '2026-11-01T08:00:00Z', appliesFrom },
  draftConfig,
]

async function openPublish() {
  await screen.findByText('AC / OK24')
  fireEvent.click(screen.getByRole('button', { name: 'Publicer' }))
  return within(screen.getByRole('dialog'))
}

describe('(a) the publish dialog', () => {
  it('a five-digit year with no earlier version disables "Publicer" and sends no request', async () => {
    const posts: Call[] = []
    mountEditor(() => jsonResponse(200, [draftConfig]), posts)
    const dialog = await openPublish()
    await dialog.findByText('Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.')
    fireEvent.change(dialog.getByLabelText('Gælder fra'), { target: { value: FIVE_DIGIT_YEAR } })
    const confirm = dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement
    expect(confirm.disabled).toBe(true)
    fireEvent.click(confirm)
    expect(posts).toHaveLength(0)
  })
})

describe('(b) a late answer never touches a newer interaction (publish dialog)', () => {
  it('a publish that succeeds after "Annuller" does not navigate; the config is read again', async () => {
    const posts: Call[] = []
    const write = held()
    const reads = mountEditor(() => jsonResponse(200, published('2026-11-02')), posts, () => write.promise)
    const dialog = await openPublish()
    const confirm = dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement
    await waitFor(() => expect(confirm.disabled).toBe(false))
    fireEvent.click(confirm)
    await waitFor(() => expect(posts).toHaveLength(1))

    fireEvent.click(dialog.getByRole('button', { name: 'Annuller' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    const before = reads.config
    write.release(jsonResponse(200, {}))
    await waitFor(() => expect(reads.config).toBeGreaterThan(before))
    expect(screen.queryByText('list')).toBeNull()
  })

  it('a refusal for a date the user has since changed shows no refusal', async () => {
    const posts: Call[] = []
    const write = held()
    mountEditor(() => jsonResponse(200, published('2026-11-02')), posts, () => write.promise)
    const dialog = await openPublish()
    const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
    await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-02'))
    fireEvent.change(date, { target: { value: '2026-11-11' } })
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    await waitFor(() => expect(posts).toHaveLength(1))

    fireEvent.change(date, { target: { value: '2026-11-10' } })
    write.release(notMonday(['2026-11-09', '2026-11-16']))
    await waitFor(() =>
      expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(false),
    )
    expect(dialog.queryByRole('alert')).toBeNull()
  })
})

describe('(c) the publish dialog remembers the server floor', () => {
  it('BEFORE_PREDECESSOR 10 Nov reads the versions again and a later 9 and 16 Nov offers no 9 Nov', async () => {
    const posts: Call[] = []
    const reads = mountEditor(
      () => jsonResponse(200, published('2026-11-02')),
      posts,
      () => (posts.length === 1 ? beforePredecessor('2026-11-10') : notMonday(['2026-11-09', '2026-11-16'])),
    )
    const dialog = await openPublish()
    const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
    await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-02'))
    expect(reads.versions).toBe(1)

    fireEvent.change(date, { target: { value: '2026-11-05' } })
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    await dialog.findByRole('button', { name: 'Brug 10. november 2026' })
    await waitFor(() => expect(reads.versions).toBe(2))
    await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-10'))

    fireEvent.change(date, { target: { value: '2026-11-11' } })
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    await waitFor(() => expect(posts).toHaveLength(2))
    await waitFor(() => expect(dialog.getByRole('alert').textContent).toContain('Ingen mandag'))
    expect(dialog.getByRole('alert').textContent).toContain('mandag den 16. november 2026')
    expect(dialog.queryByRole('button', { name: 'Brug 9. november 2026' })).toBeNull()
  })
})

describe('(e) accessibility of the publish date', () => {
  it('aria-describedby names only an element that exists; focus returns to the date after "Prøv igen"', async () => {
    let phase: 'pending' | 'failed' | 'ok' = 'pending'
    const first = held()
    mountEditor(
      () => {
        if (phase === 'pending') return first.promise
        if (phase === 'failed') return failure(500, 'boom')
        return jsonResponse(200, published('2026-11-02'))
      },
      [],
    )
    const dialog = await openPublish()
    const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
    // While the read is in flight there is no helper sentence, so nothing is referenced.
    expect(date.getAttribute('aria-describedby')).toBeNull()

    phase = 'failed'
    first.release(failure(500, 'boom'))
    await dialog.findByText('Datoen for den seneste publicerede version kunne ikke hentes.')
    expect(date.getAttribute('aria-describedby')).toBeNull()

    phase = 'ok'
    date.blur()
    fireEvent.click(dialog.getByRole('button', { name: 'Prøv igen' }))
    expect(document.activeElement).toBe(date)
    const helper = await waitFor(() => {
      const id = date.getAttribute('aria-describedby')
      expect(id).not.toBeNull()
      return document.getElementById(id as string)
    })
    expect(helper).not.toBeNull()
  })
})
