// S148 T23 FU-1 (adjudication c1, rulings 1-5). Kind F, one section per fact.
// Red conditions: on f474897f the override lower bound is the clicked row's own date (none on
// create), the five writes send whatever date is typed, a cleared override date is sent as today,
// the version read is null for loading / failed / none, a row action's error is shown only inside
// the open create form, a switch confirm can be clicked twice, a manual date change keeps the
// refusal, and a "Brug …" click leaves focus on <body>.
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
const LEAD = 'Ændringen påvirker ugeberegningen og skal derfor gælde fra en mandag.'
const AFTER_TODAY = 'Datoen kan ikke ligge efter i dag.'
const BEFORE_2_NOV = 'Datoen kan tidligst være 2. november 2026, hvor den seneste version gælder fra.'

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

const notMonday = (nearestValid: string[]) =>
  failure(
    400,
    JSON.stringify({
      error: 'Applies-from date validation failed',
      fields: [{ field: 'WeeklyNormHours', code: 'NOT_MONDAY_ALIGNED', nearestValid }],
    }),
  )

beforeEach(() => {
  mockFetch.mockReset()
})

// ---------------------------------------------------------------------------------- override page

const row = (
  overrideId: string,
  appliesFrom: string,
  status: 'ACTIVE' | 'INACTIVE' = 'ACTIVE',
  positionCode = 'DEPARTMENT_HEAD',
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

/** Mounts the override page; `onWrite` answers a POST or PUT (default: success with the first row). */
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

async function fillCreateKey(position: string) {
  fireEvent.click(await screen.findByRole('button', { name: 'Opret ny' }))
  fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
  fireEvent.change(screen.getByLabelText(/^OK Version/), { target: { value: 'OK24' } })
  fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: position } })
}

describe('override lower bound is the key newest date (ruling 1)', () => {
  // F1
  it('a create on a key whose only row is inactive from 10 Nov: min is 10 Nov, no date offered, 16 Nov named', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-10', 'INACTIVE')], calls, () => notMonday(['2026-11-09', '2026-11-16']))
    await screen.findByText('DEPARTMENT_HEAD')
    await fillCreateKey('DEPARTMENT_HEAD')

    const date = screen.getByLabelText('Gælder fra')
    expect(date.getAttribute('min')).toBe('2026-11-10')
    fireEvent.change(date, { target: { value: '2026-11-11' } })
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('mandag den 16. november 2026'))
    expect(screen.getByRole('alert').textContent).not.toContain('Du kan bruge')
    expect(screen.queryAllByRole('button', { name: /^Brug / })).toHaveLength(0)
  })

  // F2
  it('"Aktiver" on the older of two inactive rows of one key shows min 10 Nov', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02', 'INACTIVE'), row('o2', '2026-11-10', 'INACTIVE')], calls)
    await screen.findAllByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getAllByRole('button', { name: 'Aktiver' })[0])
    expect(screen.getByLabelText('Aktiveres fra').getAttribute('min')).toBe('2026-11-10')
  })

  // R: another key's rows do not set the bound
  it('a row of another position does not set the bound', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02'), row('o2', '2026-11-10', 'ACTIVE', 'OTHER')], calls)
    await screen.findByText('OTHER')
    fireEvent.click(screen.getAllByRole('button', { name: 'Rediger' })[0])
    expect(screen.getByLabelText('Ændringen gælder fra').getAttribute('min')).toBe('2026-11-02')
  })

  // R: "unknown" is not "none"
  it('"Opret" is disabled while the list is loading, and when its read failed', async () => {
    let release: (v: unknown) => void = () => {}
    mockFetch.mockImplementation(
      () => new Promise((resolve) => { release = resolve }),
    )
    renderWithCalendar(TODAY, <PositionOverrideManagement />)
    await fillCreateKey('NEW')
    expect((screen.getByRole('button', { name: 'Opret' }) as HTMLButtonElement).disabled).toBe(true)
    release(jsonResponse(200, []))
    await waitFor(() => expect((screen.getByRole('button', { name: 'Opret' }) as HTMLButtonElement).disabled).toBe(false))
  })

  it('"Opret" stays disabled when the list read failed', async () => {
    mockFetch.mockImplementation(async () => failure(500, 'The list could not be read'))
    renderWithCalendar(TODAY, <PositionOverrideManagement />)
    await fillCreateKey('NEW')
    await screen.findByText('The list could not be read')
    expect((screen.getByRole('button', { name: 'Opret' }) as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('override page: a date out of bounds sends nothing (ruling 2)', () => {
  // F3 (edit)
  it('edit: before the bound and after today show the ruled sentences; buttons set the date; nothing is sent', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    const date = screen.getByLabelText('Ændringen gælder fra') as HTMLInputElement

    fireEvent.change(date, { target: { value: '2026-11-01' } })
    expect(screen.getByRole('alert').textContent).toContain(BEFORE_2_NOV)
    expect((screen.getByRole('button', { name: 'Gem' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    fireEvent.click(screen.getByRole('button', { name: 'Brug 2. november 2026' }))
    expect(date.value).toBe('2026-11-02')

    fireEvent.change(date, { target: { value: '2026-11-20' } })
    expect(screen.getByRole('alert').textContent).toContain(AFTER_TODAY)
    expect((screen.getByRole('button', { name: 'Gem' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Brug dags dato' }))
    expect(date.value).toBe(TODAY)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(calls).toHaveLength(0)
  })

  // F3 (a switch)
  it('a switch: before the bound sends nothing, shows the sentence, the button sets the date', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    const date = screen.getByLabelText('Deaktiveres fra') as HTMLInputElement
    fireEvent.change(date, { target: { value: '2026-11-01' } })
    expect(screen.getByRole('alert').textContent).toContain(BEFORE_2_NOV)
    expect((screen.getByRole('button', { name: 'Deaktiver' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    fireEvent.click(screen.getByRole('button', { name: 'Brug 2. november 2026' }))
    expect(date.value).toBe('2026-11-02')
    expect(calls).toHaveLength(0)
  })

  // F3 (create)
  it('create: before the key bound and after today send nothing, show the sentences, buttons set the date', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02', 'INACTIVE')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    await fillCreateKey('DEPARTMENT_HEAD')
    const date = screen.getByLabelText('Gælder fra') as HTMLInputElement

    fireEvent.change(date, { target: { value: '2026-11-01' } })
    expect(screen.getByRole('alert').textContent).toContain(BEFORE_2_NOV)
    expect((screen.getByRole('button', { name: 'Opret' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.submit(date.closest('form') as HTMLFormElement)
    fireEvent.click(screen.getByRole('button', { name: 'Brug 2. november 2026' }))
    expect(date.value).toBe('2026-11-02')

    fireEvent.change(date, { target: { value: '2026-11-20' } })
    expect(screen.getByRole('alert').textContent).toContain(AFTER_TODAY)
    fireEvent.submit(date.closest('form') as HTMLFormElement)
    fireEvent.click(screen.getByRole('button', { name: 'Brug dags dato' }))
    expect(date.value).toBe(TODAY)
    expect(calls).toHaveLength(0)
  })

  // F4
  it('a cleared date disables the confirm and sends nothing (edit and switch)', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '' } })
    expect((screen.getByRole('button', { name: 'Gem' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    fireEvent.click(screen.getByRole('button', { name: 'Annuller' }))

    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    fireEvent.change(screen.getByLabelText('Deaktiveres fra'), { target: { value: '' } })
    expect((screen.getByRole('button', { name: 'Deaktiver' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    expect(calls).toHaveLength(0)
  })

  // F6
  it('a 409 on "Aktiver" with the create form closed is visible, with the server text', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02', 'INACTIVE')], calls, () =>
      failure(409, 'An active override already exists for this key'),
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Aktiver' }))
    fireEvent.click(screen.getByRole('button', { name: 'Aktiver' }))
    await waitFor(() => expect(calls).toHaveLength(1))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe('An active override already exists for this key')
    // Cancelling clears it.
    fireEvent.click(screen.getByRole('button', { name: 'Annuller' }))
    expect(screen.queryByRole('alert')).toBeNull()
  })

  // F7
  it('a double click on the switch confirm sends one POST', async () => {
    const calls: Call[] = []
    let release: (v: unknown) => void = () => {}
    mountOverrides([row('o1', '2026-11-02')], calls, () => new Promise((resolve) => { release = resolve }))
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    const confirm = screen.getByRole('button', { name: 'Deaktiver' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    await waitFor(() => expect(calls).toHaveLength(1))
    release(jsonResponse(200, row('o1', TODAY, 'INACTIVE')))
    await waitFor(() => expect(screen.queryByLabelText('Deaktiveres fra')).toBeNull())
    expect(calls).toHaveLength(1)
  })

  // F8
  it('a manual date change clears the refusal', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls, () => notMonday(['2026-11-09', '2026-11-16']))
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '2026-11-11' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(LEAD))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '2026-11-10' } })
    expect(screen.queryByRole('alert')).toBeNull()
  })

  // F9
  it('focus is on the date input after "Brug …"', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls, () => notMonday(['2026-11-09', '2026-11-16']))
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '2026-11-11' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Brug 9. november 2026' }))
    expect(document.activeElement).toBe(screen.getByLabelText('Ændringen gælder fra'))
  })
})

describe('override page: an untouched date sends today (ruling 2)', () => {
  it('create, edit and a switch', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls)
    await screen.findByText('DEPARTMENT_HEAD')

    await fillCreateKey('NEW')
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))
    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].body).toMatchObject({ positionCode: 'NEW', appliesFrom: TODAY })

    await waitFor(() => expect(screen.getByRole('button', { name: 'Rediger' })).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    await waitFor(() => expect(calls).toHaveLength(2))
    expect(calls[1].method).toBe('PUT')
    expect(calls[1].body).toMatchObject({ appliesFrom: TODAY })

    await waitFor(() => expect(screen.getByRole('button', { name: 'Deaktiver' })).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    fireEvent.click(screen.getByRole('button', { name: 'Deaktiver' }))
    await waitFor(() => expect(calls).toHaveLength(3))
    expect(calls[2].url).toContain('/deactivate')
    expect(calls[2].body).toEqual({ appliesFrom: TODAY })
  })

  // R: a stale list reaches BEFORE_PREDECESSOR, which keeps its recovery sentence
  it('a BEFORE_PREDECESSOR refusal (a stale list) shows its sentence and the button sets the date', async () => {
    const calls: Call[] = []
    mountOverrides([row('o1', '2026-11-02')], calls, () =>
      failure(
        400,
        JSON.stringify({
          error: 'Applies-from date validation failed',
          fields: [{ field: 'AppliesFrom', code: 'BEFORE_PREDECESSOR', nearestValid: ['2026-11-09'] }],
        }),
      ),
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    fireEvent.change(screen.getByLabelText('Ændringen gælder fra'), { target: { value: '2026-11-05' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toContain(
        'Datoen kan tidligst være 9. november 2026, hvor den seneste version gælder fra.',
      ),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Brug 9. november 2026' }))
    expect((screen.getByLabelText('Ændringen gælder fra') as HTMLInputElement).value).toBe('2026-11-09')
  })

  it('the date column shows the long date', async () => {
    mountOverrides([row('o1', '2026-11-02')], [])
    await screen.findByText('DEPARTMENT_HEAD')
    expect(screen.getByText('2. november 2026')).toBeDefined()
  })
})

// ---------------------------------------------------------------------------------- agreement editor

interface VersionList {
  respond: () => unknown
}

function mountEditor(version: VersionList, posts: Call[], onPublish: () => unknown = () => jsonResponse(200, {})) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      posts.push({ url, method: 'POST', body: init.body ? JSON.parse(String(init.body)) : undefined })
      return onPublish()
    }
    if (url.endsWith('/AC/OK24')) return version.respond()
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
}

const published = (appliesFrom: string) => [
  { ...draftConfig, configId: 'v1', status: 'ACTIVE', publishedAt: '2026-11-01T08:00:00Z', appliesFrom },
  draftConfig,
]

async function openPublish() {
  await screen.findByText('AC / OK24')
  fireEvent.click(screen.getByRole('button', { name: 'Publicer' }))
  // The dialog container is the date field's grandparent (found without its role, so a fact does
  // not fail on the role before it reaches its own behaviour).
  return within(screen.getByLabelText('Gælder fra').parentElement?.parentElement as HTMLElement)
}

describe('the publish dialog (rulings 2, 3, 5)', () => {
  // F3 (publish)
  it('before the bound and after today send nothing, show the sentences, the buttons set the date', async () => {
    const posts: Call[] = []
    mountEditor({ respond: () => jsonResponse(200, published('2026-11-02')) }, posts)
    const dialog = await openPublish()
    const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
    await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-02'))

    fireEvent.change(date, { target: { value: '2026-11-01' } })
    expect(dialog.getByRole('alert').textContent).toContain(BEFORE_2_NOV)
    expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(dialog.getByRole('button', { name: 'Brug 2. november 2026' }))
    expect(date.value).toBe('2026-11-02')

    fireEvent.change(date, { target: { value: '2026-11-20' } })
    expect(dialog.getByRole('alert').textContent).toContain(AFTER_TODAY)
    expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(dialog.getByRole('button', { name: 'Brug dags dato' }))
    expect(date.value).toBe(TODAY)
    expect(posts).toHaveLength(0)
  })

  // F5
  it('"Publicer" is disabled while the version read is in flight and when it failed; retry enables it', async () => {
    const posts: Call[] = []
    let phase: 'pending' | 'failed' | 'ok' = 'pending'
    let release: (v: unknown) => void = () => {}
    mountEditor(
      {
        respond: () => {
          if (phase === 'pending') return new Promise((resolve) => { release = resolve })
          if (phase === 'failed') return failure(500, 'boom')
          return jsonResponse(200, published('2026-11-02'))
        },
      },
      posts,
    )
    const dialog = await openPublish()
    expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(true)

    phase = 'failed'
    release(failure(500, 'boom'))
    await dialog.findByText('Datoen for den seneste publicerede version kunne ikke hentes.')
    expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(true)
    expect(dialog.queryByText(/Datoen kan ikke ligge efter i dag\.$/)).toBeNull()

    phase = 'ok'
    fireEvent.click(dialog.getByRole('button', { name: 'Prøv igen' }))
    await waitFor(() =>
      expect((dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement).disabled).toBe(false),
    )
    expect(dialog.queryByText('Datoen for den seneste publicerede version kunne ikke hentes.')).toBeNull()
  })

  it('the "no lower limit" helper shows only when the read loaded and found no published version', async () => {
    mountEditor({ respond: () => jsonResponse(200, [draftConfig]) }, [])
    const dialog = await openPublish()
    expect(dialog.queryByText('Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.')).toBeNull()
    await dialog.findByText('Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.')
  })

  it('an untouched date sends today', async () => {
    const posts: Call[] = []
    mountEditor({ respond: () => jsonResponse(200, published('2026-11-02')) }, posts)
    const dialog = await openPublish()
    const confirm = dialog.getByRole('button', { name: 'Publicer' }) as HTMLButtonElement
    await waitFor(() => expect(confirm.disabled).toBe(false))
    fireEvent.click(confirm)
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0].url).toContain('/publish')
    expect(posts[0].body).toEqual({ appliesFrom: TODAY })
  })

  // F9 + the dialog semantics
  it('the dialog is labelled by its title, focus starts on the date input and returns to it after "Brug …"', async () => {
    const posts: Call[] = []
    mountEditor({ respond: () => jsonResponse(200, published('2026-11-02')) }, posts, () => notMonday(['2026-11-09', '2026-11-16']))
    const dialog = await openPublish()
    const date = dialog.getByLabelText('Gælder fra') as HTMLInputElement
    expect(screen.getByRole('dialog', { name: 'Publicer overenskomst' })).toBeDefined()
    expect(document.activeElement).toBe(date)

    await waitFor(() => expect(date.getAttribute('min')).toBe('2026-11-02'))
    fireEvent.change(date, { target: { value: '2026-11-11' } })
    fireEvent.click(dialog.getByRole('button', { name: 'Publicer' }))
    const brug = await dialog.findByRole('button', { name: 'Brug 9. november 2026' })
    ;(document.activeElement as HTMLElement).blur()
    fireEvent.click(brug)
    expect(document.activeElement).toBe(date)
  })
})
