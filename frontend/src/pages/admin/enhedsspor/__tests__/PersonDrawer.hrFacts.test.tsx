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
// FU-1 (C1-R1, Step 5a cycle 1) facts, each with its kind and its red condition on 59bee5dd's section:
//  (a1) F Red conditions: a future date leaves Save enabled and shows no text.
//  (a2) F Red conditions: a date before the live row's start leaves Save enabled and shows no text.
//  (a3) F Red conditions: the native min on the date input blocks the drawer's own submit.
//  (b1) F Red conditions: Save and the select stay enabled while the as-of read is pending.
//  (b2) F Red conditions: a failed as-of read shows no text and leaves Save enabled.
//  (b3) R Regression pin: a deferred answer for an earlier date never changes a form saved since.
//  (c)  F Red conditions: "Mangler" is offered and OVERTIME is not preselected at 2026-03-16.
//  (d)  F Red conditions: the select shows the first allowed option, not the placeholder.
//  (e)  F Red conditions: no placeholder text and no (e2) sentence exist.
//  (f)  F Red conditions: the "reloaded" sentence shows although the re-read answered 500.
//  (g)  F Red conditions: focus stays on the Save button.
//  (h)  F Red conditions: no HR inputs are rendered without a resolved today.
//  (i1)-(i3), (i5), (i6) R Pins for the request bodies, the refreshed version header and the refusal texts.
//  (i4) F Red conditions: focus stays on the Save button (red on 59bee5dd).
// FU-3 (C2-R1, Step 5a cycle 2) facts, each with its kind, red on 83496e8a's section where F:
//  (a-gen) F Red conditions: a stale re-read of person A unlocks Save and clears "Gemmer..." for person B's pending save.
//  (b2-ph) F Red conditions: a failed read shows the first allowed option, not "Ingen ordning kan vælges".
//  (c-flex) F Red conditions: focus stays on the document, because the old target id pd-hrf-flex-yes does not exist.
//  (d-e3) R Pin: no agreement and nothing recorded keeps "Mangler"; a flex-only save is enabled and sends a null regime.
//  (e-msg) F Red conditions: the "gemt" message stays after a date change.
// Every case first waits until `ep-email` is enabled (each input is disabled while the drawer loads).

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor, within, render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { HrFactsSection } from '../../editPerson/HrFactsSection'
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
let readOverride: Response | null
let gates: Record<string, Promise<void> | undefined>

function setupRouter() {
  calls = []
  putResult = null
  readOverride = null
  gates = {}
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
      if (gates[asOf]) await gates[asOf]
      if (readOverride) return readOverride
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
        'Vælg en ordning',
        'Merarbejde: krav på godtgørelse',
        'Merarbejde: godtgørelse efter skøn',
        'Ingen godtgørelse for merarbejde',
      ]),
    )

    factsForDate['2026-01-05'] = { ...JONAS, asOf: '2026-01-05', agreementCode: null, allowedOvertimeRegimes: [] }
    fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: '2026-01-05' } })
    expect(await screen.findByText(NO_AGREEMENT)).toBeInTheDocument()
    expect(optionTexts()).toEqual(['Ingen ordning kan vælges'])
  })

  it('(4) F: save sends the exact request (If-Match, the chosen values and date)', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.change(screen.getByTestId('pd-hrf-regime'), { target: { value: 'EXEMPT' } })
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: '2026-04-06' } })
    await waitFor(() => expect(hrCalls('GET').some((c) => c.url.includes('asOf=2026-04-06'))).toBe(true))
    await waitFor(() => expect(screen.getByTestId('pd-hrf-save')).toBeEnabled())
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

  it("(5) F: the helper names today and the live row's start as the limits; the input carries no native limits", async () => {
    renderEdit()
    await waitForSection()
    const date = screen.getByTestId('pd-hrf-date')
    expect(date.hasAttribute('max')).toBe(false)
    expect(date.hasAttribute('min')).toBe(false)
    expect(screen.getByText(/ikke før 2\. marts 2026, hvor den nuværende registrering begynder\./)).toBeInTheDocument()
  })

  it('(5b) F: a seeded row shows the helper without a lower limit', async () => {
    facts = SEEDED
    renderEdit()
    await waitForSection()
    const date = screen.getByTestId('pd-hrf-date')
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

describe('PersonDrawer — TASK-14836 FU-1 (C1-R1): the section validates and follows its own form', () => {
  const setDate = (v: string) => fireEvent.change(screen.getByTestId('pd-hrf-date'), { target: { value: v } })
  const regimeSelect = () => screen.getByTestId('pd-hrf-regime') as HTMLSelectElement
  const shownOption = () => regimeSelect().selectedOptions[0]?.textContent
  const NO_AGREEMENT_DAY = { ...JONAS, asOf: '2026-01-05', agreementCode: null, allowedOvertimeRegimes: [], overtimeRegime: null }
  const AC_DAY = { ...SEEDED, asOf: '2026-04-06' }
  const READ_FAILED = 'Oplysningerne kunne ikke hentes. Prøv igen.'
  const CHANGED = 'Oplysningerne er ændret i mellemtiden. De er indlæst igen; kontrollér dem, og gem igen.'

  it('(a1) F: a date after today shows the ruled text, marks the field, disables Save and sends no PUT', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    setDate('2026-11-11')
    expect(await screen.findByText('Datoen kan ikke ligge efter i dag.')).toBeInTheDocument()
    expect(screen.getByTestId('pd-hrf-date').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByTestId('pd-hrf-date').getAttribute('aria-describedby')).toContain('pd-hrf-date-error')
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(hrCalls('PUT')).toHaveLength(0)
  })

  it("(a2) F: a date before the live row's start shows the ruled text, disables Save and sends no PUT", async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    setDate('2026-03-01')
    expect(await screen.findByText('Datoen kan tidligst være 2. marts 2026.')).toBeInTheDocument()
    expect(screen.getByTestId('pd-hrf-date').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(hrCalls('PUT')).toHaveLength(0)
  })

  it("(a3) F: with an invalid HR date in place, the drawer's own save still submits", async () => {
    renderEdit()
    await waitForSection()
    setDate('2026-03-01')
    expect(await screen.findByText('Datoen kan tidligst være 2. marts 2026.')).toBeInTheDocument()
    expect(screen.getByTestId('pd-hrf-date').getAttribute('form')).not.toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.url.includes('/users/EMP1'))).toBe(true))
  })

  it('(b1) F: while the as-of read is pending the select and Save are disabled, then enabled by the answer', async () => {
    let release!: () => void
    gates['2026-04-06'] = new Promise<void>((r) => (release = r))
    factsForDate['2026-04-06'] = { ...JONAS, asOf: '2026-04-06' }
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    expect(screen.getByTestId('pd-hrf-save')).toBeEnabled()
    setDate('2026-04-06')
    await waitFor(() => expect(hrCalls('GET').some((c) => c.url.includes('asOf=2026-04-06'))).toBe(true))
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    expect(regimeSelect()).toBeDisabled()
    release()
    await waitFor(() => expect(screen.getByTestId('pd-hrf-save')).toBeEnabled())
    expect(regimeSelect()).toBeEnabled()
  })

  it('(b2) F: a failed as-of read shows the ruled text and keeps Save and the select disabled', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    readOverride = res(500, { error: 'boom' })
    setDate('2026-04-06')
    await waitFor(() => expect(screen.getByTestId('pd-hrf-status').textContent).toContain(READ_FAILED))
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    expect(regimeSelect()).toBeDisabled()
  })

  it('(b2-ph) F: from the placeholder state, a failed read for a new date shows "Ingen ordning kan vælges" as the selected option', async () => {
    factsForDate['2026-04-06'] = { ...AC_DAY }
    renderEdit()
    await waitForSection()
    setDate('2026-04-06')
    await waitFor(() => expect(shownOption()).toBe('Vælg en ordning'))
    readOverride = res(500, { error: 'boom' })
    setDate('2026-05-04')
    await waitFor(() => expect(screen.getByTestId('pd-hrf-status').textContent).toContain(READ_FAILED))
    expect(regimeSelect()).toBeDisabled()
    expect(regimeSelect().value).toBe('')
    expect(shownOption()).toBe('Ingen ordning kan vælges')
    expect(regimeSelect().selectedOptions[0].disabled).toBe(true)
    expect(screen.queryByTestId('pd-hrf-regime-help')).toBeNull()
  })

  it('(b3) R: a deferred answer for an earlier date changes nothing in a form saved since', async () => {
    let release!: () => void
    gates['2026-04-06'] = new Promise<void>((r) => (release = r))
    factsForDate['2026-04-06'] = { ...AC_DAY }
    renderEdit()
    await waitForSection()
    setDate('2026-04-06')
    await waitFor(() => expect(hrCalls('GET').some((c) => c.url.includes('asOf=2026-04-06'))).toBe(true))
    setDate(TODAY)
    await waitFor(() => expect(screen.getByTestId('pd-hrf-save')).toBeEnabled())
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText('Oplysningerne om overarbejde og flex er gemt.')).toBeInTheDocument()
    release()
    await new Promise((r) => setTimeout(r, 30))
    expect(optionTexts()).toEqual(['Overarbejde: ret til overarbejdsbetaling', 'Undtaget fra reglerne om overarbejde'])
    expect(regimeSelect().value).toBe('OVERTIME')
  })

  it('(c) F: a regime valid at the form date is preselected there and "Mangler" is not offered', async () => {
    facts = { ...SEEDED, asOf: TODAY, overtimeRegime: null, effectiveFrom: '2026-03-02', liveEffectiveFrom: '2026-03-02' }
    factsForDate['2026-03-16'] = { ...JONAS, asOf: '2026-03-16' }
    renderEdit()
    await waitForSection()
    expect(optionTexts()[0]).toBe(MISSING)
    setDate('2026-03-16')
    await waitFor(() => expect(regimeSelect().value).toBe('OVERTIME'))
    expect(optionTexts()).not.toContain(MISSING)
    expect(shownOption()).toBe('Overarbejde: ret til overarbejdsbetaling')
  })

  it('(d) F: the select shows what the form holds: the placeholder and helper on an agreement that cannot hold the regime, the regime again on return', async () => {
    factsForDate['2026-04-06'] = { ...AC_DAY }
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    expect(regimeSelect().value).toBe('OVERTIME')
    expect(shownOption()).toBe('Overarbejde: ret til overarbejdsbetaling')
    expect(screen.getByTestId('pd-hrf-save')).toBeEnabled()

    setDate('2026-04-06')
    await waitFor(() => expect(shownOption()).toBe('Vælg en ordning'))
    expect(regimeSelect().value).toBe('')
    expect(regimeSelect().selectedOptions[0].disabled).toBe(true)
    expect(optionTexts()).not.toContain(MISSING)
    const help = screen.getByTestId('pd-hrf-regime-help')
    expect(help.textContent).toBe(
      'Den registrerede ordning kan ikke vælges for den overenskomst, medarbejderen har på den valgte dato. Vælg en anden ordning.',
    )
    expect(regimeSelect().getAttribute('aria-describedby')).toContain('pd-hrf-regime-help')
    expect(regimeSelect().getAttribute('aria-invalid')).toBeNull()
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()

    setDate(TODAY)
    await waitFor(() => expect(regimeSelect().value).toBe('OVERTIME'))
    expect(shownOption()).toBe('Overarbejde: ret til overarbejdsbetaling')
    expect(screen.queryByTestId('pd-hrf-regime-help')).toBeNull()
    expect(screen.getByTestId('pd-hrf-save')).toBeEnabled()
  })

  it('(d-e3) R: with no agreement and nothing recorded, "Mangler" stays selected and a flex-only save sends a null regime', async () => {
    facts = SEEDED
    factsForDate['2026-01-05'] = { ...SEEDED, asOf: '2026-01-05', agreementCode: null, allowedOvertimeRegimes: [] }
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    setDate('2026-01-05')
    await screen.findByTestId('pd-hrf-no-agreement')
    expect(regimeSelect()).toBeDisabled()
    expect(shownOption()).toBe(MISSING)
    expect(screen.getByTestId('pd-hrf-save')).toBeEnabled()
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].body).toEqual({ effectiveFrom: '2026-01-05', overtimeRegime: null, onFlexScheme: false })
  })

  it('(e-msg) F: a date change clears the saved message', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText('Oplysningerne om overarbejde og flex er gemt.')).toBeInTheDocument()
    setDate('2026-11-09')
    expect(screen.queryByTestId('pd-hrf-message')).toBeNull()
  })

  it('(e) F: nothing to choose shows "Ingen ordning kan vælges" (no agreement) or the no-regimes sentence, with Save disabled', async () => {
    factsForDate['2026-01-05'] = NO_AGREEMENT_DAY
    factsForDate['2026-02-02'] = { ...JONAS, asOf: '2026-02-02', agreementCode: 'ZZ', allowedOvertimeRegimes: [], overtimeRegime: null }
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))

    setDate('2026-01-05')
    await waitFor(() => expect(shownOption()).toBe('Ingen ordning kan vælges'))
    expect(regimeSelect()).toBeDisabled()
    expect(screen.getByTestId('pd-hrf-no-agreement')).toBeInTheDocument()
    expect(screen.queryByTestId('pd-hrf-regime-help')).toBeNull()
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()

    setDate('2026-02-02')
    expect((await screen.findByTestId('pd-hrf-no-regimes')).textContent).toBe(
      'Der findes ingen ordninger for den overenskomst, medarbejderen har på den valgte dato, så en ordning kan ikke vælges.',
    )
    expect(regimeSelect()).toBeDisabled()
    expect(shownOption()).toBe('Ingen ordning kan vælges')
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
  })

  it('(f) F: a 412 whose re-read fails shows the read-failed text, never the reloaded sentence', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    putResult = res(412, { error: 'Version mismatch' })
    readOverride = res(500, { error: 'boom' })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(screen.getByTestId('pd-hrf-status').textContent).toContain(READ_FAILED))
    expect(screen.queryByText(CHANGED)).toBeNull()
  })

  it('(g) F: after a refused save the first invalid field has focus', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    putResult = res(400, { fields: [{ field: 'OvertimeRegime', code: 'NOT_ALLOWED_FOR_AGREEMENT', nearestValid: [] }] })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(document.activeElement).toBe(regimeSelect()))
  })

  it('(c-flex) F: a refused flex field takes focus', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    putResult = res(400, { fields: [{ field: 'OnFlexScheme', code: 'UNKNOWN_VALUE' }] })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByTestId('pd-hrf-flex-yes')))
  })

  it('(a-gen) F: a stale re-read of person A cannot unlock the Save of person B', async () => {
    const getsOf: Record<string, number> = { A: 0, B: 0 }
    const putsOf: string[] = []
    let releaseARead!: () => void
    let releaseBPut!: () => void
    const aRead = new Promise<void>((r) => (releaseARead = r))
    const bPut = new Promise<void>((r) => (releaseBPut = r))
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const who = /employees\/([^/]+)\/hr-facts/.exec(url)![1]
      if ((init?.method ?? 'GET') === 'PUT') {
        putsOf.push(who)
        if (who === 'B') await bPut
        return res(200, { employeeId: who, version: 2 }, '"2"')
      }
      getsOf[who]++
      if (who === 'A' && getsOf.A === 2) await aRead
      return res(200, { ...JONAS, employeeId: who }, '"1"')
    })
    const { rerender } = render(<HrFactsSection employeeId="A" today={TODAY} />)
    await waitFor(() => expect(screen.getByTestId('pd-hrf-save')).toBeEnabled())
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(getsOf.A).toBe(2))
    rerender(<HrFactsSection employeeId="B" today={TODAY} />)
    await waitFor(() => expect(getsOf.B).toBe(1))
    await waitFor(() => expect(screen.getByTestId('pd-hrf-save')).toBeEnabled())
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(putsOf).toEqual(['A', 'B']))
    expect(screen.getByTestId('pd-hrf-save').textContent).toBe('Gemmer...')
    releaseARead()
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.getByTestId('pd-hrf-save')).toBeDisabled()
    expect(screen.getByTestId('pd-hrf-save').textContent).toBe('Gemmer...')
    expect(screen.queryByTestId('pd-hrf-message')).toBeNull()
    expect(putsOf).toEqual(['A', 'B'])
    releaseBPut()
    expect(await screen.findByText('Oplysningerne om overarbejde og flex er gemt.')).toBeInTheDocument()
  })

  it('(h) F: with no resolved today the inputs are rendered disabled', async () => {
    // The drawer itself cannot mount without a calendar (other sections need it), so the section is rendered alone.
    render(<HrFactsSection employeeId="EMP1" today={null} />)
    await screen.findByRole('heading', { name: HEADING })
    for (const id of ['pd-hrf-regime', 'pd-hrf-flex-yes', 'pd-hrf-flex-no', 'pd-hrf-date', 'pd-hrf-save']) {
      expect(screen.getByTestId(id), id).toBeDisabled()
    }
    expect(hrCalls('GET')).toHaveLength(0)
  })

  it('(i1) R: the no-row save sends exactly the date, a null regime and the flex choice', async () => {
    facts = NO_ROW
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].body).toEqual({ effectiveFrom: TODAY, overtimeRegime: null, onFlexScheme: true })
  })

  it('(i2) R: after a 412 the next save sends the refreshed version and shows the refreshed values', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    putResult = res(412, { error: 'Version mismatch' })
    facts = { ...JONAS, liveVersion: 2, onFlexScheme: false, overtimeRegime: 'EXEMPT' }
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText(CHANGED)).toBeInTheDocument()
    await waitFor(() => expect(regimeSelect().value).toBe('EXEMPT'))
    expect((screen.getByTestId('pd-hrf-flex-no') as HTMLInputElement).checked).toBe(true)
    putResult = null
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(2))
    expect(hrCalls('PUT')[1].headers['If-Match']).toBe('"2"')
    expect(hrCalls('PUT')[1].body).toEqual({ effectiveFrom: TODAY, overtimeRegime: 'EXEMPT', onFlexScheme: false })
  })

  it('(i3) R: saving unchanged values is sent and announced as saved', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(hrCalls('PUT')).toHaveLength(1))
    expect(hrCalls('PUT')[0].body).toEqual({ effectiveFrom: TODAY, overtimeRegime: 'OVERTIME', onFlexScheme: true })
    expect(await screen.findByText('Oplysningerne om overarbejde og flex er gemt.')).toBeInTheDocument()
  })

  it('(i4) R: BEFORE_CURRENT_ROW puts its text on the date field', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    putResult = res(400, { fields: [{ field: 'EffectiveFrom', code: 'BEFORE_CURRENT_ROW', nearestValid: ['2026-03-09'] }] })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    await waitFor(() => expect(document.getElementById('pd-hrf-date-error')?.textContent).toBe('Datoen kan tidligst være 9. marts 2026.'))
    expect(screen.getByTestId('pd-hrf-date').getAttribute('aria-invalid')).toBe('true')
    expect(document.activeElement).toBe(screen.getByTestId('pd-hrf-date'))
  })

  it('(i5) R: 403 and 404 on save show their Danish texts', async () => {
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-no'))
    putResult = res(403, { error: 'Forbidden' })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText('Du har ikke adgang til at ændre disse oplysninger.')).toBeInTheDocument()
    putResult = res(404, { error: 'Not found' })
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText('Medarbejderen findes ikke længere.')).toBeInTheDocument()
  })

  it('(i6) R: a create 409 carrying currentVersion reloads and says the facts changed', async () => {
    facts = NO_ROW
    renderEdit()
    await waitForSection()
    fireEvent.click(screen.getByTestId('pd-hrf-flex-yes'))
    putResult = res(409, { currentVersion: 3 })
    facts = { ...JONAS, liveVersion: 3 }
    fireEvent.click(screen.getByTestId('pd-hrf-save'))
    expect(await screen.findByText(CHANGED)).toBeInTheDocument()
    await waitFor(() => expect(regimeSelect().value).toBe('OVERTIME'))
  })
})
