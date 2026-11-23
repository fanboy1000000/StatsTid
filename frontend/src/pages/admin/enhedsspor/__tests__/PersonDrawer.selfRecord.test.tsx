// S148 / TASK-14846 — AC-C13r: when the drawer's person is the logged-in user, every input the
// server refuses a self actor is locked (and no other), one Danish `info` note says why, and a
// self save still sends what it sends today. Presentation only: the server stays the authority.
//
// Kinds (docs/AGENTS.md "RED evidence"):
//  (1) F  Red conditions: the seven refused inputs and the picker are enabled and no note exists on
//         code without the lock.
//  (2) R  Regression pin: another person's record locks nothing and shows no note (a lock applied
//         to everyone fails it).
//  (3) R  Regression pin: a self save of the e-mail alone still sends the users and profile PUTs
//         with unchanged values, and no birth-date / start-date / eligibility PUT.
//  (4) F  Red conditions: both carry-forward checkboxes are `disabled: busy`, so enabled once the
//         drawer has loaded, on code without the self lock.
// Every case first waits until `ep-email` is enabled: each input is disabled while the drawer
// loads, so an earlier "disabled" assertion would pass on code without the lock.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { WithEtag, User } from '../../../../hooks/useAdmin'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

const SELF_NOTE =
  'Du kan ikke selv ændre de låste felter, fordi de har betydning for din egen løn eller dine rettigheder. Bed en anden HR-administrator om at gøre dette.'

const auth = vi.hoisted(() => ({ employeeId: 'EMP1' }))
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

// A scheduled change later than the picked date, so the carry-forward checkboxes can appear.
const SCHEDULED_FROM = '2026-12-01'
const TODAY = '2026-07-16'

function setupRouter() {
  calls = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const rec: Recorded = { url, method, body: init?.body ? JSON.parse(init.body as string) : null }
    calls.push(rec)

    if (url.includes('/employee-profiles/') && method === 'GET') {
      return res(
        200,
        {
          employeeId: 'EMP1',
          partTimeFraction: 1.0,
          position: 'Old Title',
          isPartTime: false,
          version: 5,
          scheduled: {
            effectiveFrom: SCHEDULED_FROM,
            effectiveTo: null,
            partTimeFraction: 0.6,
            position: 'Future Title',
            employmentCategory: null,
          },
        },
        '"5"',
      )
    }
    if (url.includes('/employee-profiles/') && method === 'PUT') {
      return res(
        200,
        {
          employeeId: 'EMP1',
          partTimeFraction: rec.body!.partTimeFraction,
          position: rec.body!.position,
          isPartTime: false,
          version: 6,
          scheduled: null,
        },
        '"6"',
      )
    }
    if (url.endsWith('/api/admin/users/EMP1') && method === 'PUT') {
      return res(
        200,
        {
          userId: 'EMP1',
          displayName: 'Karen Nielsen',
          email: rec.body!.email,
          primaryOrgId: 'STY02',
          agreementCode: rec.body!.agreementCode,
          version: 6,
        },
        '"6"',
      )
    }
    if (url.includes('/birth-date')) {
      return res(200, { employeeId: 'EMP1', birthDate: null, version: 5 }, '"5"')
    }
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

function renderEdit(user: Partial<WithEtag<User>> = {}) {
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
            ...user,
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
  auth.employeeId = 'EMP1'
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

const REFUSED = [
  'ep-primary-org',
  'ep-agreement',
  'ep-part-time',
  'ep-position',
  'ep-employment-start',
  'ep-birth-date',
  'ep-child-sick',
  'pd-effective-from',
]

describe('PersonDrawer — TASK-14846 the self lock (AC-C13r)', () => {
  it("(1) F: on the logged-in user's own record the refused inputs and the picker are disabled, the ruled note shows, the rest stays enabled", async () => {
    renderEdit()
    await waitForLoaded()

    for (const id of REFUSED) expect(screen.getByTestId(id), id).toBeDisabled()
    expect(screen.getByTestId('pd-self-lock-note').textContent).toBe(SELF_NOTE)
    expect(screen.getByTestId('ep-primary-org').getAttribute('aria-describedby')).toBe('pd-self-lock-note')

    for (const id of ['ep-display-name', 'ep-email', 'pd-placement', 'pd-promote', 'danger-open']) {
      expect(screen.getByTestId(id), id).toBeEnabled()
    }
    expect(screen.getByTestId('pd-termination-link')).toBeInTheDocument()
  })

  it("(2) R: on another person's record nothing is locked by this rule and no note shows", async () => {
    auth.employeeId = 'SOMEONE_ELSE'
    renderEdit()
    await waitForLoaded()

    for (const id of REFUSED) expect(screen.getByTestId(id), id).toBeEnabled()
    expect(screen.queryByTestId('pd-self-lock-note')).toBeNull()
    expect(screen.queryByText(SELF_NOTE)).toBeNull()
  })

  it('(3) R: a self save of the e-mail alone still sends the users and profile PUTs, and no birth-date, start-date or eligibility PUT', async () => {
    renderEdit()
    await waitForLoaded()

    fireEvent.change(screen.getByTestId('ep-email'), { target: { value: 'new@x.dk' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )

    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.body?.email).toBe('new@x.dk')
    expect(usersPut.body?.primaryOrgId).toBe('STY02')
    expect(usersPut.body?.agreementCode).toBe('AC')
    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.partTimeFraction).toBe(1)
    expect(profilePut.body?.position).toBe('Old Title')
    const forbidden = calls.filter(
      (c) =>
        c.method === 'PUT' &&
        (c.url.includes('/birth-date') ||
          c.url.includes('/employment-start-date') ||
          c.url.includes('/entitlement-eligibility/')),
    )
    expect(forbidden).toEqual([])
  })

  it("(4) F: on one's own record both carry-forward checkboxes are disabled", async () => {
    // The locked inputs cannot be edited through the UI, so the fields are made dirty with
    // fireEvent.change (which bypasses `disabled`) to make each checkbox render.
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForLoaded()

    fireEvent.change(screen.getByTestId('ep-agreement'), { target: { value: 'HK' } })
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })

    expect(screen.getByTestId('pd-agreement-scheduled-carry-forward')).toBeDisabled()
    expect(screen.getByTestId('pd-profile-scheduled-carry-forward')).toBeDisabled()
  })
})
