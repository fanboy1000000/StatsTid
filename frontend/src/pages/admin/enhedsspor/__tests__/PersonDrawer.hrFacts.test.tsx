// S148 / TASK-14837 — AC-C11g: the HR section of the edit-person drawer (the overtime regime and
// flex membership). Every fact renders PersonDrawer in edit mode with the HR-facts routes mocked.
// Presentation only: the server stays the authority on the values and dates.
//
// Kinds (docs/AGENTS.md "RED evidence"). On code without the section, the drawer has no HR-facts
// heading and sends no HR-facts request, so the first assertion of each F fact fails:
//  (1) F  Red conditions: no "Overarbejde, merarbejde og flex" heading.
//  (2) F  Red conditions: no heading; no empty-option select to read.
//  (3) F  Red conditions: no select, so no option list and no GET for the changed date.
//  (4) F  Red conditions: no select to choose from, so no PUT request.
//  (5) F  Red conditions: no date input.
//  (6) F  Red conditions: no section inputs to be disabled.
//  (7) F  Red conditions: no select and no date input to carry a refusal.
//  (6b) F Red conditions: no section inputs to be enabled.
//  (8) R  Regression pin: a non-HR actor sees no section and no HR-facts request is sent.
// Every case first waits until `ep-email` is enabled (each input is disabled while the drawer loads).

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

const auth = vi.hoisted(() => ({ employeeId: 'SOMEONE_ELSE', role: 'LocalHR' }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role, user: { employeeId: auth.employeeId, role: auth.role } }),
}))

const rlMock = vi.hoisted(() => ({
  searchPeople: vi.fn(),
  assignManager: vi.fn(),
  removeManager: vi.fn(),
  createVikar: vi.fn(),
  endVikar: vi.fn(),
  fetchActiveVikar: vi.fn(),
  fetchEmployeeLines: vi.fn(),
  fetchDirectReports: vi.fn(),
  deletePersonWithReassignment: vi.fn(),
}))
vi.mock('../../../../hooks/useReportingLines', async (importActual) => ({
  ...(await importActual<typeof import('../../../../hooks/useReportingLines')>()),
  useReportingLines: () => rlMock,
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 't' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => {
    mockStorage[k] = v
  },
  removeItem: (k: string) => {
    delete mockStorage[k]
  },
})

interface Recorded {
  url: string
  method: string
  headers: Record<string, string>
  body: Record<string, unknown> | null
}
let calls: Recorded[]

function res(status: number, json: unknown, etag?: string) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  } as unknown as Response
}

const TODAY = '2026-11-10'
const MISSING = 'Mangler – ikke registreret'
const PAY_NOTE = 'Gemmes nu, men bruges endnu ikke i nogen beregning og påvirker endnu ikke lønberegningen.'
const HEADING = 'Overarbejde, merarbejde og flex'
const NO_AGREEMENT = 'Medarbejderen har ingen overenskomst på den valgte dato, så en ordning kan ikke vælges.'

const JONAS = {
  employeeId: 'EMP1',
  asOf: TODAY,
  agreementCode: 'HK',
  allowedOvertimeRegimes: ['OVERTIME', 'EXEMPT'],
  overtimeRegime: 'OVERTIME',
  onFlexScheme: true,
  effectiveFrom: '2026-03-02',
  liveVersion: 1,
  liveEffectiveFrom: '2026-03-02',
}
const SEEDED = {
  ...JONAS,
  agreementCode: 'AC',
  allowedOvertimeRegimes: ['CONTRACTUAL', 'DISCRETIONARY', 'NONE'],
  overtimeRegime: null,
  effectiveFrom: '0001-01-01',
  liveEffectiveFrom: '0001-01-01',
}
const NO_ROW = { ...SEEDED, overtimeRegime: null, onFlexScheme: null, effectiveFrom: null, liveVersion: null, liveEffectiveFrom: null }

let facts: Record<string, unknown>
let factsForDate: Record<string, Record<string, unknown>>
let putResult: Response | null

function setupRouter() {
  calls = []
  putResult = null
  factsForDate = {}
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const rec: Recorded = {
      url,
      method,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: init?.body ? JSON.parse(init.body as string) : null,
    }
    calls.push(rec)

    if (url.includes('/hr-facts')) {
      if (method === 'PUT') return putResult ?? res(200, { employeeId: 'EMP1', version: 2 }, '"2"')
      const asOf = new URL(url, 'http://x').searchParams.get('asOf') ?? ''
      return res(200, factsForDate[asOf] ?? facts, facts.liveVersion != null ? `"${facts.liveVersion}"` : undefined)
    }
    if (url.includes('/employee-profiles/') && method === 'GET') {
      return res(
        200,
        { employeeId: 'EMP1', partTimeFraction: 1.0, position: 'Old Title', isPartTime: false, version: 5, scheduled: null },
        '"5"',
      )
    }
    if (url.includes('/birth-date')) return res(200, { employeeId: 'EMP1', birthDate: null, version: 5 }, '"5"')
    if (url.includes('/employment-start-date'))
      return res(200, { employeeId: 'EMP1', employmentStartDate: null, version: 5 }, '"5"')
    if (url.includes('/entitlement-eligibility/'))
      return res(200, { employeeId: 'EMP1', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false })
    throw new Error(`no route for ${method} ${url}`)
  })
}

function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01', orgName: 'Finansministeriet', orgType: 'MAO', parentOrgId: null, materializedPath: '/MIN01/', memberCount: 0,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24', memberCount: 0, directMemberCount: 0,
          units: [
            { unitId: 'U1', organisationId: 'STY02', parentUnitId: null, type: 'kontor', name: 'Kontoret', level: 1, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
          ],
        },
      ],
    },
  ]
}

function renderEdit() {
  const forest = makeForest()
  return renderWithCalendar(
    TODAY,
    <MemoryRouter>
      <ToastProvider>
        <PersonDrawer
          open
          user={{
            userId: 'EMP1', username: 'emp1', displayName: 'Karen Nielsen', email: 'k@x.dk', primaryOrgId: 'STY02',
            agreementCode: 'HK', version: 5, etag: '"5"', scheduledAgreementCode: null,
          }}
          organizations={orgsFromForest(forest)}
          forest={forest}
          currentUnitId="U1"
          onClose={vi.fn()}
          onSaved={vi.fn()}
        />
      </ToastProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.employeeId = 'SOMEONE_ELSE'
  auth.role = 'LocalHR'
  facts = JONAS
  mockFetch.mockReset()
  rlMock.searchPeople.mockResolvedValue({ ok: true, data: { items: [], total: 0, limit: 60, offset: 0 } })
  rlMock.fetchEmployeeLines.mockResolvedValue({ ok: true, data: { active: [], history: [] } })
  rlMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [] })
  rlMock.fetchActiveVikar.mockResolvedValue({ ok: true, data: { activeVikar: null } })
  setupRouter()
})

async function waitForLoaded() {
  await waitFor(() => expect(screen.getByTestId('ep-email')).toBeEnabled())
  await waitFor(() => expect(screen.queryByTestId('person-drawer-loading')).toBeNull())
}
async function waitForSection() {
  await waitForLoaded()
  await screen.findByRole('heading', { name: HEADING })
  await waitFor(() => expect(screen.getByTestId('pd-hrf-regime')).toBeEnabled())
}
const hrCalls = (method?: string) => calls.filter((c) => c.url.includes('/hr-facts') && (!method || c.method === method))
const optionTexts = () => within(screen.getByTestId('pd-hrf-regime')).queryAllByRole('option').map((o) => o.textContent)

describe('PersonDrawer — TASK-14837 the HR-facts section (AC-C11g)', () => {
  it("(1) F: shows today's facts and the pay note, and reads them as of the drawer's today", async () => {
    renderEdit()
    await waitForSection()

    expect(screen.getByTestId('pd-hrf-current-regime').textContent).toBe('Overarbejde: ret til overarbejdsbetaling')
    expect(screen.getByTestId('pd-hrf-current').textContent).toContain('fra 2. marts 2026')
    expect(screen.getByTestId('pd-hrf-current-flex').textContent).toContain('Ja')
    expect(screen.getByText(PAY_NOTE)).toBeInTheDocument()
    expect(hrCalls('GET')[0].url).toContain('/api/admin/employees/EMP1/hr-facts?asOf=2026-11-10')
  })

  it('(2) F: a seeded person shows missing with no "fra" text, offers the empty option, and saves flex alone', async () => {
    facts = SEEDED
    renderEdit()
    await waitForSection()

    expect(screen.getByTestId('pd-hrf-current-regime').textContent).toBe(MISSING)
    expect(screen.getByTestId('pd-hrf-current').textContent).not.toContain('fra')
    expect(screen.getByTestId('pd-hrf-current').textContent).not.toContain('0001')
    expect(optionTexts()[0]).toBe(MISSING)

    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    expect(screen.getByTestId('pd-hrf-save')).toBeEnabled()
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].headers['If-Match']).toBe('"1"')
    expect(hrCalls('PUT')[0].body).toEqual({ effectiveFrom: TODAY, overtimeRegime: null, onFlexScheme: false })
  })

  it('(2b) F: with a regime recorded the select has no empty option', async () => {
    renderEdit()
    await waitForSection()
    expect(optionTexts()).toEqual(['Overarbejde: ret til overarbejdsbetaling', 'Undtaget fra reglerne om overarbejde'])
  })

  it("(3) F: the options are the served set for the form's date, and none without an agreement", async () => {
    factsForDate['2026-04-06'] = { ...SEEDED, asOf: '2026-04-06' }
    renderEdit()
    await waitForSection()
    expect(optionTexts()).toEqual(['Overarbejde: ret til overarbejdsbetaling', 'Undtaget fra reglerne om overarbejde'])

    fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: '2026-04-06' } })
    await waitFor(() => expect(hrCalls('GET').some((c) => c.url.includes('asOf=2026-04-06'))).toBe(true))
    await waitFor(() =>
      expect(optionTexts()).toEqual([
        'Merarbejde: krav på godtgørelse',
        'Merarbejde: godtgørelse efter skøn',
        'Ingen godtgørelse for merarbejde',
      ]),
    )

    factsForDate['2026-01-05'] = { ...JONAS, asOf: '2026-01-05', agreementCode: null, allowedOvertimeRegimes: [] }
    fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: '2026-01-05' } })
    expect(await screen.findByText(NO_AGREEMENT)).toBeInTheDocument()
    expect(optionTexts()).toEqual([])
  })

  it('(4) F: save sends the exact request (If-Match, the chosen values and date)', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.change(screen.getByTestId('pd-hrf-regime'), { target: { value: 'EXEMPT' } })
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: '2026-04-06' } })
    await waitFor(() => expect(hrCalls('GET').some((c) => c.url.includes('asOf=2026-04-06'))).toBe(true))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].url).toContain('/api/admin/employees/EMP1/hr-facts')
    expect(hrCalls('PUT')[0].headers['If-Match']).toBe('"1"')
    expect(hrCalls('PUT')[0].body).toEqual({ effectiveFrom: '2026-04-06', overtimeRegime: 'EXEMPT', onFlexScheme: false })
    expect(await screen.findByText('Oplysningerne om overarbejde og flex er gemt.')).toBeInTheDocument()
  })

  it('(4b) F: no row sends If-None-Match, and the drawer save sends no HR-facts request', async () => {
    facts = NO_ROW
    renderEdit()
    await waitForSection()
    const before = hrCalls().length
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].headers['If-None-Match']).toBe('*')
    expect(hrCalls('PUT')[0].headers['If-Match']).toBeUndefined()

    const putsBefore = hrCalls('PUT').length
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.url.includes('/users/EMP1'))).toBe(true))
    expect(hrCalls('PUT')).toHaveLength(putsBefore)
    expect(before).toBeGreaterThan(0)
  })

  it("(5) F: the date's max is today, its min the live row's start; no min for a seeded row", async () => {
    renderEdit()
    await waitForSection()
    const date = screen.getByTestId('pd-hrf-date')
    expect(date.getAttribute('max')).toBe(TODAY)
    expect(date.getAttribute('min')).toBe('2026-03-02')
    expect(screen.getByText(/ikke før 2\. marts 2026, hvor den nuværende registrering begynder\./)).toBeInTheDocument()
  })

  it('(5b) F: a seeded row sets no min and shows the helper without a lower limit', async () => {
    facts = SEEDED
    renderEdit()
    await waitForSection()
    const date = screen.getByTestId('pd-hrf-date')
    expect(date.getAttribute('max')).toBe(TODAY)
    expect(date.hasAttribute('min')).toBe(false)
    expect(screen.getByText('Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.')).toBeInTheDocument()
  })

  it("(6) F: on one's own record the inputs and the save button are disabled and point at the note", async () => {
    auth.employeeId = 'EMP1'
    renderEdit()
    await waitForLoaded()
    await screen.findByRole('heading', { name: HEADING })
    await waitFor(() => expect(screen.getByTestId('pd-hrf-current')).toBeInTheDocument())

    for (const id of ['pd-hrf-regime', 'pd-hrf-flex-yes', 'pd-hrf-flex-no', 'pd-hrf-date', 'pd-hrf-save']) {
      expect(screen.getByTestId(id), id).toBeDisabled()
      expect(screen.getByTestId(id).getAttribute('aria-describedby') ?? '', id).toContain('pd-self-lock-note')
    }
    expect(hrCalls('GET')).toHaveLength(1)
    expect(hrCalls('PUT')).toHaveLength(0)
  })

  it("(6b) F: on another person's record none is disabled by the lock", async () => {
    renderEdit()
    await waitForSection()
    for (const id of ['pd-hrf-regime', 'pd-hrf-flex-yes', 'pd-hrf-flex-no', 'pd-hrf-date']) {
      expect(screen.getByTestId(id), id).toBeEnabled()
    }
  })

  it('(7) F: refusals show Danish text on the field; no English from a body appears', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))

    putResult = res(400, {
      error: 'HR facts validation failed',
      fields: [{ field: 'OvertimeRegime', code: 'NOT_ALLOWED_FOR_AGREEMENT', nearestValid: [] }],
    })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    const text = 'Værdien kan ikke vælges for den overenskomst, medarbejderen har på den valgte dato.'
    await waitFor(() => expect(screen.getByTestId('pd-hrf-regime').getAttribute('aria-invalid')).toBe('true'))
    expect(screen.getByTestId('pd-hrf-regime').getAttribute('aria-describedby')).toContain('pd-hrf-regime-error')
    expect(document.getElementById('pd-hrf-regime-error')?.textContent).toBe(text)
    expect(screen.queryByText(/validation failed/i)).toBeNull()

    putResult = res(412, { error: 'Version mismatch' })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(
      await screen.findByText('Oplysningerne er ændret i mellemtiden. De er indlæst igen; kontrollér dem, og gem igen.'),
    ).toBeInTheDocument()

    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    putResult = res(409, { error: "The user's organisation changed while this request was being processed; refresh and retry." })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(
      await screen.findByText('Medarbejderen er flyttet til en anden institution i mellemtiden. Genindlæs siden.'),
    ).toBeInTheDocument()
    expect(screen.queryByText(/organisation changed/)).toBeNull()
  })

  it('(8) R: a non-HR actor sees no HR-facts section and no HR-facts request is sent', async () => {
    auth.role = 'LocalLeader'
    renderEdit()
    await waitForLoaded()
    expect(screen.queryByRole('heading', { name: HEADING })).toBeNull()
    expect(hrCalls()).toHaveLength(0)
  })
})
