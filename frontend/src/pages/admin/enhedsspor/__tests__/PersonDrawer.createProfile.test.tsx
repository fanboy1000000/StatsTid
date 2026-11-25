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
// Every case first waits until `ep-email` is enabled: each input is disabled while the drawer loads.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

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
let putProfile: (rec: Recorded) => Response

function setupRouter() {
  calls = []
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
      return res(200, { employeeId: 'EMP1', employmentStartDate: null, version: 5 }, '"5"')
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

function renderEdit() {
  const forest = makeForest()
  return renderWithCalendar(
    TODAY,
    <MemoryRouter>
      <ToastProvider>
        <PersonDrawer
          open
          user={{
            userId: 'EMP1',
            username: 'emp1',
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
    </MemoryRouter>,
  )
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
