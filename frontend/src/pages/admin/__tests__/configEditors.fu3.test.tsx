// S148 T23 FU-3 (cycle-3 ruling C3-R1). Kind F, one section per fact.
// Red conditions on bf994ddf: a refused create shows its error above the table and keeps it after a
// successful retry; one page-wide counter drops a refusal or a success when the user typed in another
// field or form; a late answer for row A closes row B's step; a create floor is lost when an unrelated
// row edit opens; a BEFORE_PREDECESSOR answer does not read the list again; the publish dialog's date
// can change while a publish is pending and a late success replaces the page with the spinner.
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

/** A request that stays pending until `release(...)` answers it. */
function held() {
  let release: (v: unknown) => void = () => {}
  const promise = new Promise((resolve) => { release = resolve })
  return { promise, release: (v: unknown) => release(v) }
}

beforeEach(() => {
  mockFetch.mockReset()
})

// ---------------------------------------------------------------------------------- override page

const row = (overrideId: string, positionCode = 'DEPARTMENT_HEAD'): PositionOverrideConfig => ({
  overrideId,
  agreementCode: 'AC',
  okVersion: 'OK24',
  positionCode,
  status: 'ACTIVE',
  version: 1,
  maxFlexBalance: null,
  normPeriodWeeks: 4,
  weeklyNormHours: 40,
  createdBy: 'admin',
  createdAt: '2026-03-08T00:00:00Z',
  updatedAt: '2026-03-08T00:00:00Z',
  appliesFrom: '2026-11-02',
  description: null,
})

function mountOverrides(
  rows: PositionOverrideConfig[],
  calls: Call[],
  onWrite: (call: Call) => unknown = () => jsonResponse(200, rows[0]),
) {
  const reads = { list: 0 }
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'PUT') {
      const call = { url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined }
      calls.push(call)
      return onWrite(call)
    }
    reads.list += 1
    return jsonResponse(200, rows)
  })
  renderWithCalendar(TODAY, <PositionOverrideManagement />)
  return reads
}

const dateOf = (label: string) => screen.getByLabelText(label) as HTMLInputElement
const button = (name: string) => screen.getByRole('button', { name }) as HTMLButtonElement

function fillCreateKey(position: string) {
  fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
  fireEvent.change(screen.getByLabelText(/^OK Version/), { target: { value: 'OK24' } })
  fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: position } })
}

describe('(a) a refused create keeps its error in the create form', () => {
  it('a 409 shows inside the form and is gone after a successful retry', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1')], calls, () =>
      calls.length === 1 ? failure(409, 'Findes allerede') : jsonResponse(201, row('o9', 'NEW')),
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Opret ny'))
    fillCreateKey('NEW')
    fireEvent.click(button('Opret'))
    const message = await screen.findByText(/Findes allerede/)
    expect(message.closest('form')).not.toBeNull()

    fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: 'NEW2' } })
    fireEvent.click(button('Opret'))
    await waitFor(() => expect(screen.queryByText('Opret positionstilpasning')).toBeNull())
    expect(screen.queryByText(/Findes allerede/)).toBeNull()
  })
})

describe('(b) an answer stays valid when only other things changed', () => {
  it('typing in the same row\'s description during a pending date refusal still shows the refusal', async () => {
    const calls: Call[] = []
    const write = held()
    mountOverrides([row('o1')], calls, () => write.promise)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-11' } })
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(calls).toHaveLength(1))

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'ny tekst' } })
    write.release(notMonday(['2026-11-09', '2026-11-16']))
    expect((await screen.findByRole('alert')).textContent).toContain('mandag')
    expect(screen.getByRole('button', { name: 'Brug 9. november 2026' })).toBeDefined()
  })

  it('typing in the open create form during a pending switch still shows the switch\'s refusal', async () => {
    const calls: Call[] = []
    const write = held()
    mountOverrides([row('o1')], calls, () => write.promise)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Deaktiver'))
    fireEvent.click(button('Deaktiver'))
    await waitFor(() => expect(calls).toHaveLength(1))

    fireEvent.click(button('Opret ny'))
    fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
    write.release(notMonday(['2026-11-09', '2026-11-16']))
    expect((await screen.findByRole('alert')).textContent).toContain('mandag')
  })

  it('a switch on row A that succeeds after "Annuller" and a switch on row B keeps B\'s step', async () => {
    const calls: Call[] = []
    const write = held()
    mountOverrides([row('o1'), row('o2', 'OTHER')], calls, () => write.promise)
    await screen.findByText('OTHER')
    fireEvent.click(screen.getAllByRole('button', { name: 'Deaktiver' })[0])
    fireEvent.click(screen.getAllByRole('button', { name: 'Deaktiver' })[0])
    await waitFor(() => expect(calls).toHaveLength(1))

    fireEvent.click(button('Annuller'))
    fireEvent.click(screen.getAllByRole('button', { name: 'Deaktiver' })[1])
    expect(screen.getByText('Deaktiveres fra')).toBeDefined()

    write.release(jsonResponse(200, row('o1')))
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Annuller' })).toHaveLength(1))
    // The list was read again after the write; B's step is still open.
    await screen.findByText('OTHER')
    expect(screen.getByText('Deaktiveres fra')).toBeDefined()
    expect((screen.getAllByRole('button', { name: 'Deaktiver' })[0] as HTMLButtonElement).disabled).toBe(false)
  })
})

describe('(d) the floors belong to their own interaction', () => {
  it('a create floor survives an unrelated row edit; the create retry offers no 9 Nov', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1'), row('o2', 'OTHER')], calls, () =>
      calls.length === 1 ? beforePredecessor('2026-11-10') : notMonday(['2026-11-09', '2026-11-16']),
    )
    await screen.findByText('OTHER')
    fireEvent.click(button('Opret ny'))
    fillCreateKey('DEPARTMENT_HEAD')
    const createDate = dateOf('Gælder fra')
    fireEvent.change(createDate, { target: { value: '2026-11-05' } })
    fireEvent.click(button('Opret'))
    await screen.findByRole('button', { name: 'Brug 10. november 2026' })
    await waitFor(() => expect(createDate.getAttribute('min')).toBe('2026-11-10'))

    // An unrelated row's edit opens and the create floor is still there.
    fireEvent.click((await screen.findAllByRole('button', { name: 'Rediger' }))[1])
    expect(createDate.getAttribute('min')).toBe('2026-11-10')

    fireEvent.change(createDate, { target: { value: '2026-11-11' } })
    fireEvent.click(button('Opret'))
    await waitFor(() => expect(calls).toHaveLength(2))
    await waitFor(() =>
      expect(screen.getAllByRole('alert').some((a) => (a.textContent ?? '').includes('Ingen mandag'))).toBe(true),
    )
    expect(screen.queryByRole('button', { name: 'Brug 9. november 2026' })).toBeNull()
  })

  it('a BEFORE_PREDECESSOR answer reads the list again', async () => {
    const calls: Call[] = []
    const reads = mountOverrides([row('o1')], calls, () => beforePredecessor('2026-11-10'))
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-05' } })
    const before = reads.list
    fireEvent.click(button('Gem'))
    await screen.findByRole('button', { name: 'Brug 10. november 2026' })
    await waitFor(() => expect(reads.list).toBeGreaterThan(before))
  })
})

// ---------------------------------------------------------------------------------- agreement editor

const published = [
  { ...draftConfig, configId: 'v1', status: 'ACTIVE', publishedAt: '2026-11-01T08:00:00Z', appliesFrom: '2026-11-02' },
  draftConfig,
]

function mountEditor(posts: Call[], onPublish: () => unknown, configRead: () => unknown) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      posts.push({ url, method: 'POST', body: init.body ? JSON.parse(String(init.body)) : undefined })
      return onPublish()
    }
    if (url.endsWith('/AC/OK24')) return jsonResponse(200, published)
    return configRead()
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
}

async function openPublish() {
  await screen.findByText('AC / OK24')
  fireEvent.click(screen.getByRole('button', { name: 'Publicer' }))
  const dialog = within(screen.getByRole('dialog'))
  const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
  await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-02'))
  return { dialog, date }
}

describe('(c) the publish dialog cannot be disturbed by its own success', () => {
  it('while a publish is pending the date is disabled; after success the dialog is gone and nothing publishes twice', async () => {
    const posts: Call[] = []
    const write = held()
    mountEditor(posts, () => write.promise, () => jsonResponse(200, draftConfig))
    const { dialog, date } = await openPublish()
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(date.disabled).toBe(true)

    write.release(jsonResponse(200, {}))
    await screen.findByText('list')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(posts).toHaveLength(1)
  })

  it('a publish that succeeds after "Annuller" re-reads the config without the page spinner and the page is read-only', async () => {
    const posts: Call[] = []
    const write = held()
    const reread = held()
    let configReads = 0
    mountEditor(posts, () => write.promise, () => {
      configReads += 1
      return configReads === 1 ? jsonResponse(200, draftConfig) : reread.promise
    })
    const { dialog } = await openPublish()
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    await waitFor(() => expect(posts).toHaveLength(1))
    fireEvent.click(dialog.getByRole('button', { name: 'Annuller' }))

    write.release(jsonResponse(200, {}))
    await waitFor(() => expect(configReads).toBe(2))
    // The re-read is in flight: the page is still there, not replaced by the spinner.
    expect(screen.getByText('AC / OK24')).toBeDefined()
    reread.release(jsonResponse(200, { ...draftConfig, status: 'ACTIVE' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Publicer' })).toBeNull())
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(posts).toHaveLength(1)
  })
})
