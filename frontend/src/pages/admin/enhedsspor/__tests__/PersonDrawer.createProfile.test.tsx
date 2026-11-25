// S148 / TASK-14852 — AC-Q268l: when a person has no employment profile the edit drawer says so in
// Danish and, only when the server's 404 said a create is possible, offers "Opret ny profil": a small
// form with its own date, pre-filled from the last profile, with its own save and Danish refusals.
// Presentation only: the server stays the authority. Today is Friday 10 April 2026.
//
// Kinds (docs/AGENTS.md "RED evidence"):
//  (1) F  Red conditions: no "Opret ny profil" button exists on the base (the fields are disabled with
//         the hover title), so `findByTestId('ep-profile-create')` fails.
//  (2) F  Red conditions: no form on the base, so the date input does not exist.
//  (3) F  Red conditions: no button on the base.
//  (4) F  Red conditions: the scheduled sentence does not exist on the base.
//  (5) F  Red conditions: no form on the base.
//  (6) R  Regression pin: a covered profile shows its two enabled fields and exactly one profile GET.
//  (7) F  Red conditions: the base shows the hover title "Profil ikke fundet for denne medarbejder" and
//         no sentence.
// FU-2 (the QUAL-268 adjudication, rulings 3-6), all F on 8d16d3e2 unless marked R:
//  (8)  F  the dead submit says why (fraction, date), nothing clamped.
//  (9)  F  a 404 body lacking any of the five members offers nothing; explicit nulls (R) offer it, and a "bad" position (F: the old 'bad' marker rejected it) is pre-filled.
//  (10) F  "Gem ændringer" waits while a create is pending.
//  (11) F  after a create the fraction input has focus.
//  (12) R/F  the date limits' other branches, the refusals, and a late answer after switching person.
// Every case first waits until `ep-email` is enabled: each input is disabled while the drawer loads.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { CalendarTestProvider, renderWithCalendar } from '../../../../test/renderWithCalendar'

const auth = vi.hoisted(() => ({ employeeId: 'SOMEONE_ELSE' }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'LocalHR', user: { employeeId: auth.employeeId, role: 'LocalHR' } }),
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

const TODAY = '2026-04-10'
const NONE_404 = {
  error: 'Employee profile not found',
  canCreate: true,
  uncoveredSince: '2026-03-16',
  scheduledFrom: null,
  lastPartTimeFraction: 1.0,
  lastPosition: null,
}
const COVERED_200 = {
  employeeId: 'EMP1',
  partTimeFraction: 1.0,
  position: 'Old Title',
  isPartTime: false,
  version: 5,
  scheduled: null,
}

let getProfile: () => Response
let putProfile: (rec: Recorded) => Response | Promise<Response>
let employmentStartDate: string | null

function setupRouter() {
  calls = []
  employmentStartDate = null
  getProfile = () => res(404, NONE_404)
  putProfile = (rec) =>
    res(
      200,
      {
        employeeId: 'EMP1',
        partTimeFraction: rec.body!.partTimeFraction,
        position: rec.body!.position,
        isPartTime: true,
        version: 8,
        scheduled: null,
      },
      '"8"',
    )
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const rec: Recorded = {
      url,
      method,
      headers: { ...(init?.headers as Record<string, string> | undefined) },
      body: init?.body ? JSON.parse(init.body as string) : null,
    }
    calls.push(rec)
    if (url.includes('/employee-profiles/') && method === 'GET') return getProfile()
    if (url.includes('/employee-profiles/') && method === 'PUT') return putProfile(rec)
    if (url.endsWith('/api/admin/users/EMP1') && method === 'PUT') {
      return res(
        200,
        {
          userId: 'EMP1',
          displayName: 'Karen Nielsen',
          email: rec.body!.email,
          primaryOrgId: 'STY02',
          agreementCode: rec.body!.agreementCode,
          version: 9,
        },
        '"9"',
      )
    }
    if (url.includes('/birth-date')) return res(200, { employeeId: 'EMP1', birthDate: null, version: 5 }, '"5"')
    if (url.includes('/employment-start-date')) {
      return res(200, { employeeId: 'EMP1', employmentStartDate, version: 5 }, '"5"')
    }
    if (url.includes('/entitlement-eligibility/')) {
      return res(200, { employeeId: 'EMP1', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false })
    }
    throw new Error(`no route for ${method} ${url}`)
  })
}

function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 0,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0,
          units: [
            { unitId: 'U1', organisationId: 'STY02', parentUnitId: null, type: 'kontor', name: 'Kontoret', level: 1, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
          ],
        },
      ],
    },
  ]
}

function drawerFor(userId: string) {
  const forest = makeForest()
  return (
    <MemoryRouter>
      <ToastProvider>
        <PersonDrawer
          open
          user={{
            userId,
            username: userId.toLowerCase(),
            displayName: 'Karen Nielsen',
            email: 'k@x.dk',
            primaryOrgId: 'STY02',
            agreementCode: 'AC',
            version: 5,
            etag: '"5"',
            scheduledAgreementCode: null,
          }}
          organizations={orgsFromForest(forest)}
          forest={forest}
          currentUnitId="U1"
          onClose={vi.fn()}
          onSaved={vi.fn()}
        />
      </ToastProvider>
    </MemoryRouter>
  )
}

function renderEdit() {
  return renderWithCalendar(TODAY, drawerFor('EMP1'))
}

beforeEach(() => {
  auth.employeeId = 'SOMEONE_ELSE'
  mockFetch.mockReset()
  rlMock.searchPeople.mockReset()
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

const profilePuts = () => calls.filter((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')
const profileGets = () => calls.filter((c) => c.url.includes('/employee-profiles/') && c.method === 'GET')

const NONE_NOTE =
  'Medarbejderen har ingen ansættelsesprofil fra og med 16. marts 2026. Uden en profil kan medarbejderen ikke registrere tid, og lønnen kan ikke beregnes for de dage.'
const UNREADABLE = 'Profilen kunne ikke hentes. Luk panelet, og åbn medarbejderen igen.'

async function openForm() {
  await waitForLoaded()
  const button = await screen.findByTestId('ep-profile-create')
  fireEvent.click(button)
  return screen.findByTestId('pd-cp-date')
}

describe('PersonDrawer — TASK-14852 create a missing profile (AC-Q268l)', () => {
  it('offersTheCreateOnlyWhenAllowed_prefillsAndSends', async () => {
    renderEdit()
    await waitForLoaded()
    expect(await screen.findByTestId('ep-profile-none')).toHaveTextContent(NONE_NOTE)
    expect(screen.queryByTitle('Profil ikke fundet for denne medarbejder')).toBeNull()
    expect(screen.queryByTestId('ep-part-time')).toBeNull()
    expect(screen.getByTestId('ep-profile-create')).toHaveTextContent('Opret ny profil')

    const date = await openForm()
    expect(date).toHaveValue('2026-03-16')
    expect(date).toHaveAttribute('min', '2026-03-16')
    expect(date).toHaveAttribute('max', '2026-04-10')
    expect(screen.getByTestId('pd-cp-part-time')).toHaveValue(1)
    expect((screen.getByTestId('pd-cp-part-time') as HTMLInputElement).value).toBe('1.000')
    expect(screen.getByTestId('pd-cp-position')).toHaveValue('')
    expect(screen.getByText('Ny ansættelsesprofil')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Forudfyldt med 16. marts 2026, den første dag uden profil, så der ikke opstår et hul. Datoen kan ikke ligge efter i dag.',
      ),
    ).toBeInTheDocument()

    fireEvent.change(screen.getByTestId('pd-cp-part-time'), { target: { value: '0.8' } })
    fireEvent.change(screen.getByTestId('pd-cp-position'), { target: { value: 'Fuldmægtig' } })
    fireEvent.change(date, { target: { value: '2026-03-25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))

    await waitFor(() => expect(screen.getByTestId('ep-part-time')).toBeEnabled())
    expect(profilePuts()).toHaveLength(1)
    const put = profilePuts()[0]
    expect(put.headers['If-None-Match']).toBe('*')
    expect(put.headers['If-Match']).toBeUndefined()
    expect(put.body).toEqual({ effectiveFrom: '2026-03-25', partTimeFraction: 0.8, position: 'Fuldmægtig' })
    expect((screen.getByTestId('ep-part-time') as HTMLInputElement).value).toBe('0.800')
    expect(screen.getByTestId('ep-position')).toHaveValue('Fuldmægtig')
    expect(screen.queryByTestId('ep-profile-create')).toBeNull()
    expect(await screen.findByText('Profilen er oprettet.')).toBeInTheDocument()

    fireEvent.change(screen.getByTestId('ep-email'), { target: { value: 'new@x.dk' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))
    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.headers['If-Match']).toBe('"8"')
  })

  it('aLaterDateNamesTheDaysWithoutAProfile', async () => {
    renderEdit()
    const date = await openForm()
    const describedBy = () => date.getAttribute('aria-describedby') ?? ''

    fireEvent.change(date, { target: { value: '2026-03-25' } })
    const notice = await screen.findByTestId('pd-cp-gap')
    expect(notice).toHaveTextContent(
      'Dagene fra og med 16. marts 2026 til og med 24. marts 2026 får ingen profil, og lønnen for de måneder kan ikke beregnes, før dagene har en profil. Vælg 16. marts 2026, hvis der ikke skal være et hul.',
    )
    expect(notice).toHaveAttribute('role', 'status')
    expect(describedBy().split(' ')).toContain(notice.id)
    expect(describedBy().split(' ')).toContain('pd-cp-date-help')

    fireEvent.change(date, { target: { value: '2026-03-16' } })
    expect(screen.queryByTestId('pd-cp-gap')).toBeNull()
    expect(describedBy().split(' ')).not.toContain('pd-cp-gap')
    expect(describedBy().split(' ')).toContain('pd-cp-date-help')
  })

  it('ownRecord_theCreateIsLocked', async () => {
    auth.employeeId = 'EMP1'
    renderEdit()
    await waitForLoaded()
    const button = await screen.findByTestId('ep-profile-create')
    expect(button).toBeDisabled()
    expect(screen.getByTestId('pd-self-lock-note')).toBeInTheDocument()
    expect((button.getAttribute('aria-describedby') ?? '').split(' ')).toContain('pd-self-lock-note')
    fireEvent.click(button)
    expect(screen.queryByTestId('pd-cp-form')).toBeNull()
    expect(profilePuts()).toHaveLength(0)
  })

  it('scheduledOrNotYet_noButton', async () => {
    getProfile = () => res(404, { ...NONE_404, canCreate: false, uncoveredSince: '2026-03-16', scheduledFrom: '2026-12-01' })
    renderEdit()
    await waitForLoaded()
    expect(await screen.findByTestId('ep-profile-scheduled')).toHaveTextContent(
      'En ansættelsesprofil er planlagt fra 1. december 2026. Den dækker medarbejderen fra den dag, og der skal ikke gøres noget nu.',
    )
    expect(screen.queryByTestId('ep-profile-create')).toBeNull()
  })

  it('refusalsShownInDanish', async () => {
    renderEdit()
    await openForm()
    const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))
    const refuse = (status: number, body: unknown) => {
      putProfile = () => res(status, body)
    }

    refuse(400, {
      error: 'Employee profile validation failed',
      fields: [{ field: 'EffectiveFrom', code: 'BEFORE_LAST_PROFILE_END', nearestValid: ['2026-03-16'] }],
    })
    submit()
    expect(await screen.findByTestId('pd-cp-error')).toHaveTextContent(
      'Den nye profil kan tidligst gælde fra 16. marts 2026, hvor den seneste profil ophørte.',
    )

    refuse(400, {
      error: 'Employee profile validation failed',
      fields: [{ field: 'PartTimeFraction', code: 'EXCEEDS_STORAGE_LIMIT', nearestValid: null }],
    })
    submit()
    await waitFor(() => expect(screen.getByTestId('pd-cp-error')).toHaveTextContent('Tallet er for stort til at blive gemt.'))
    expect(screen.getByTestId('pd-cp-error')).not.toHaveTextContent('Den nye profil kan tidligst')

    refuse(409, { error: 'Employee profile already exists', currentVersion: 2 })
    submit()
    await waitFor(() =>
      expect(screen.getByTestId('pd-cp-error')).toHaveTextContent(
        'Medarbejderen har allerede en ansættelsesprofil, eller en er planlagt. Luk panelet, og åbn medarbejderen igen for at se den.',
      ),
    )
    expect(screen.getByTestId('pd-cp-error')).toHaveAttribute('role', 'alert')
    expect(screen.queryByText(/Employee profile/)).toBeNull()
    expect(screen.getByTestId('pd-cp-form')).toBeInTheDocument()
  })

  it('fractionOutOfRange_saysWhy_andNothingIsClamped', async () => {
    getProfile = () => res(404, { ...NONE_404, lastPartTimeFraction: 1.2 })
    renderEdit()
    await openForm()
    const input = screen.getByTestId('pd-cp-part-time') as HTMLInputElement
    const sentence = 'Deltidsfraktionen skal være et tal mellem 0,1 og 1,0.'
    expect(input.value).toBe('1.200')
    const error = screen.getByTestId('pd-cp-part-time-error')
    expect(error).toHaveTextContent(sentence)
    expect(error.id).toBe('pd-cp-part-time-error')
    expect(error).toHaveAttribute('role', 'status')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAttribute('aria-describedby', 'pd-cp-part-time-error')
    expect(screen.getByTestId('pd-cp-submit')).toBeDisabled()

    fireEvent.change(input, { target: { value: '0.8' } })
    expect(screen.queryByTestId('pd-cp-part-time-error')).toBeNull()
    expect(input).not.toHaveAttribute('aria-invalid')
    expect(input).not.toHaveAttribute('aria-describedby')
    expect(screen.getByTestId('pd-cp-submit')).toBeEnabled()

    for (const bad of ['', '0.05']) {
      fireEvent.change(input, { target: { value: bad } })
      expect(screen.getByTestId('pd-cp-part-time-error')).toHaveTextContent(sentence)
      expect(screen.getByTestId('pd-cp-submit')).toBeDisabled()
    }
  })

  it('theDateLimits_eachSayWhy', async () => {
    renderEdit()
    const date = await openForm()
    const text = () => screen.getByTestId('pd-cp-date-error')
    expect(screen.queryByTestId('pd-cp-date-error')).toBeNull()

    fireEvent.change(date, { target: { value: '' } })
    expect(text()).toHaveTextContent('Vælg en dato.')
    expect(text().id).toBe('pd-cp-date-error')
    expect(text()).toHaveAttribute('role', 'status')
    expect(date).toHaveAttribute('aria-invalid', 'true')
    expect((date.getAttribute('aria-describedby') ?? '').split(' ')).toContain('pd-cp-date-error')
    expect(screen.getByTestId('pd-cp-submit')).toBeDisabled()

    fireEvent.change(date, { target: { value: '2026-04-11' } })
    expect(text()).toHaveTextContent('Datoen kan ikke ligge efter i dag.')

    fireEvent.change(date, { target: { value: '2026-03-15' } })
    expect(text()).toHaveTextContent('Den nye profil kan tidligst gælde fra 16. marts 2026, hvor den seneste profil ophørte.')

    fireEvent.change(date, { target: { value: '2026-03-16' } })
    expect(screen.queryByTestId('pd-cp-date-error')).toBeNull()
    expect(date).not.toHaveAttribute('aria-invalid')
    expect((date.getAttribute('aria-describedby') ?? '').split(' ')).not.toContain('pd-cp-date-error')
  })

  it('theEmploymentStartIsTheEarliestDay_withItsOwnHelperAndRefusal', async () => {
    employmentStartDate = '2026-03-20'
    getProfile = () => res(404, { ...NONE_404, uncoveredSince: null })
    renderEdit()
    const date = await openForm()
    expect(date).toHaveValue('2026-03-20')
    expect(date).toHaveAttribute('min', '2026-03-20')
    expect(
      screen.getByText(
        'Forudfyldt med 20. marts 2026, medarbejderens første ansættelsesdag. Datoen kan ikke ligge før den dag eller efter i dag.',
      ),
    ).toBeInTheDocument()
    fireEvent.change(date, { target: { value: '2026-03-19' } })
    expect(screen.getByTestId('pd-cp-date-error')).toHaveTextContent('Datoen ligger før medarbejderens første ansættelsesdag.')
  })

  it('noEarliestDay_todayIsChosen', async () => {
    getProfile = () =>
      res(404, { ...NONE_404, uncoveredSince: null, scheduledFrom: null, lastPartTimeFraction: null, lastPosition: null })
    renderEdit()
    await waitForLoaded()
    expect(await screen.findByTestId('ep-profile-none')).toHaveTextContent(
      'Medarbejderen har ingen ansættelsesprofil, der gælder i dag. Uden en profil kan medarbejderen ikke registrere tid, og lønnen kan ikke beregnes.',
    )
    fireEvent.click(screen.getByTestId('ep-profile-create'))
    const date = await screen.findByTestId('pd-cp-date')
    expect(date).toHaveValue(TODAY)
    expect(date).not.toHaveAttribute('min')
    expect(screen.getByText('Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.')).toBeInTheDocument()
    expect((screen.getByTestId('pd-cp-part-time') as HTMLInputElement).value).toBe('1.000')
  })

  it('aOneDayGap_namesThatDay', async () => {
    renderEdit()
    const date = await openForm()
    fireEvent.change(date, { target: { value: '2026-03-17' } })
    expect(await screen.findByTestId('pd-cp-gap')).toHaveTextContent(
      '16. marts 2026 får ingen profil, og lønnen for den måned kan ikke beregnes, før dagen har en profil. Vælg 16. marts 2026, hvis der ikke skal være et hul.',
    )
  })

  it('theOtherRefusals_403_422_drift_unmapped', async () => {
    renderEdit()
    await openForm()
    const arms: Array<[number, unknown, string]> = [
      [403, { error: 'Forbidden' }, 'Du har ikke rettigheder til at oprette en profil for denne medarbejder.'],
      [422, { error: 'before start' }, 'Datoen ligger før medarbejderens første ansættelsesdag.'],
      [
        409,
        { error: "The user's organisation changed while this request was being processed; refresh and retry." },
        'Medarbejderen er flyttet til en anden organisation, mens du gemte. Luk panelet, åbn medarbejderen igen, og prøv igen.',
      ],
      [500, { error: 'boom' }, 'Profilen kunne ikke oprettes: boom'],
    ]
    for (const [status, body, sentence] of arms) {
      putProfile = () => res(status, body)
      fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))
      await waitFor(() => expect(screen.getByTestId('pd-cp-error')).toHaveTextContent(sentence))
      await waitFor(() => expect(screen.getByTestId('pd-cp-submit')).toBeEnabled())
    }
    expect(profilePuts()).toHaveLength(4)
  })

  it('aLateAnswerAfterSwitchingPerson_changesNothing', async () => {
    let release: (r: Response) => void = () => {}
    putProfile = () => new Promise<Response>((resolve) => (release = resolve))
    const { rerender } = renderEdit()
    await openForm()
    fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))
    await waitFor(() => expect(profilePuts()).toHaveLength(1))

    rerender(<CalendarTestProvider today={TODAY}>{drawerFor('EMP2')}</CalendarTestProvider>)
    await waitFor(() => expect(profileGets().some((c) => c.url.includes('/EMP2'))).toBe(true))
    await waitForLoaded()
    expect(await screen.findByTestId('ep-profile-create')).toBeInTheDocument()

    release(
      res(
        200,
        { employeeId: 'EMP1', partTimeFraction: 1, position: null, isPartTime: false, version: 8, scheduled: null },
        '"8"',
      ),
    )
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.queryByText('Profilen er oprettet.')).toBeNull()
    expect(screen.queryByTestId('ep-part-time')).toBeNull()
    expect(screen.getByTestId('ep-profile-create')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Gem ændringer' })).toBeEnabled()
  })

  it('saveWaitsWhileACreateIsPending_thenNamesTheNewVersion', async () => {
    let release: (r: Response) => void = () => {}
    putProfile = () => new Promise<Response>((resolve) => (release = resolve))
    renderEdit()
    await openForm()
    expect(screen.getByRole('button', { name: 'Gem ændringer' })).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))
    await waitFor(() => expect(profilePuts()).toHaveLength(1))
    const save = screen.getByRole('button', { name: 'Gem ændringer' })
    expect(save).toBeDisabled()
    fireEvent.click(save)
    fireEvent.submit(save.closest('form')!)
    expect(calls.filter((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toHaveLength(0)

    release(
      res(
        200,
        { employeeId: 'EMP1', partTimeFraction: 1, position: null, isPartTime: false, version: 8, scheduled: null },
        '"8"',
      ),
    )
    await waitFor(() => expect(screen.getByRole('button', { name: 'Gem ændringer' })).toBeEnabled())
    fireEvent.change(screen.getByTestId('ep-email'), { target: { value: 'new@x.dk' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!.headers['If-Match']).toBe('"8"')
  })

  it('afterACreate_theFractionInputHasFocus', async () => {
    renderEdit()
    await openForm()
    fireEvent.click(screen.getByRole('button', { name: 'Opret profil' }))
    await waitFor(() => expect(screen.getByTestId('ep-part-time')).toBeEnabled())
    await waitFor(() => expect(screen.getByTestId('ep-part-time')).toHaveFocus())
  })

  it('aCoveredProfile_isUnchanged', async () => {
    getProfile = () => res(200, COVERED_200, '"5"')
    renderEdit()
    await waitForLoaded()
    await waitFor(() => expect(screen.getByTestId('ep-part-time')).toBeEnabled())
    expect(screen.getByTestId('ep-position')).toBeEnabled()
    expect(screen.queryByTestId('ep-profile-create')).toBeNull()
    expect(screen.queryByTestId('ep-profile-none')).toBeNull()
    expect(profileGets()).toHaveLength(1)
  })

  it('aPartialOrOddNotFoundBody_isReadFailClosed', async () => {
    const withoutUncovered: Record<string, unknown> = { ...NONE_404 }
    delete withoutUncovered.uncoveredSince
    const arms: unknown[] = [{ canCreate: true }, withoutUncovered, { ...NONE_404, lastPosition: 5 }]
    for (const body of arms) {
      getProfile = () => res(404, body)
      const { unmount } = renderEdit()
      await waitForLoaded()
      expect(await screen.findByTestId('ep-profile-unreadable')).toHaveTextContent(UNREADABLE)
      expect(screen.queryByTestId('ep-profile-create')).toBeNull()
      unmount()
    }
  })

  it('explicitNulls_offerTheCreate_andAPositionNamedBad_isPrefilled', async () => {
    getProfile = () =>
      res(404, { canCreate: true, uncoveredSince: null, scheduledFrom: null, lastPartTimeFraction: null, lastPosition: null })
    const first = renderEdit()
    await waitForLoaded()
    expect(await screen.findByTestId('ep-profile-none')).toHaveTextContent(
      'Medarbejderen har ingen ansættelsesprofil, der gælder i dag.',
    )
    expect(screen.getByTestId('ep-profile-create')).toBeInTheDocument()
    first.unmount()

    getProfile = () => res(404, { ...NONE_404, lastPosition: 'bad' })
    renderEdit()
    await openForm()
    expect(screen.getByTestId('pd-cp-position')).toHaveValue('bad')
  })

  it('anUnreadableProfile_offersNothing', async () => {
    const arms: Array<() => Response> = [() => res(500, { error: 'boom' }), () => res(404, { error: 'Employee profile not found' })]
    for (const arm of arms) {
      getProfile = arm
      const { unmount } = renderEdit()
      await waitForLoaded()
      expect(await screen.findByTestId('ep-profile-unreadable')).toHaveTextContent(UNREADABLE)
      expect(screen.queryByTitle('Profil ikke fundet for denne medarbejder')).toBeNull()
      expect(screen.queryByTestId('ep-profile-create')).toBeNull()
      expect(screen.getByTestId('ep-part-time')).toBeDisabled()
      unmount()
    }
  })
})
