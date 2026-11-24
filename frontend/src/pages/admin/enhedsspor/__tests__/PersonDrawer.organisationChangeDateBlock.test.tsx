// S148 / TASK-14850 — AC-C13j (NR-P4): while the organisation field is changed, the edit drawer
// offers no date after today and says why in Danish (C-13: the server refuses a future transfer
// date); with the organisation unchanged, future dates stay offered (S141's scheduling).
//
// Today is Tuesday 10 November 2026; the picked date is Tuesday 1 December 2026.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'LocalHR' }),
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

function setupRouter() {
  calls = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const rec: Recorded = { url, method, body: init?.body ? JSON.parse(init.body as string) : null }
    calls.push(rec)
    if (url.includes('/employee-profiles/') && method === 'GET') {
      return res(
        200,
        { employeeId: 'EMP1', partTimeFraction: 1.0, position: 'Old Title', isPartTime: false, version: 5, scheduled: null },
        '"5"',
      )
    }
    if (url.endsWith('/api/admin/users/EMP1') && method === 'PUT') {
      return res(
        200,
        {
          userId: 'EMP1',
          displayName: 'Karen Nielsen',
          email: 'k@x.dk',
          primaryOrgId: rec.body!.primaryOrgId,
          agreementCode: rec.body!.agreementCode,
          version: 6,
        },
        '"6"',
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
  const org = (orgId: string, orgName: string) => ({
    orgId, orgName, orgType: 'ORGANISATION' as const, parentOrgId: 'MIN01',
    materializedPath: `/MIN01/${orgId}/`, agreementCode: 'HK', okVersion: 'OK24',
    memberCount: 0, directMemberCount: 0, units: [],
  })
  return [
    {
      orgId: 'MIN01', orgName: 'Finansministeriet', orgType: 'MAO', parentOrgId: null,
      materializedPath: '/MIN01/', memberCount: 0,
      organisations: [org('STY02', 'Statens IT'), org('STY03', 'Statens Indkøb')],
    },
  ]
}

function renderEdit() {
  const forest = makeForest()
  return renderWithCalendar(
    '2026-11-10',
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
          currentUnitId={null}
          onClose={vi.fn()}
          onSaved={vi.fn()}
        />
      </ToastProvider>
    </MemoryRouter>,
  )
}

const BLOCK_TEXT =
  'En flytning til en anden organisation kan kun gælde fra i dag eller en tidligere dato. Vælg dags dato eller en tidligere dato.'

beforeEach(() => {
  mockFetch.mockReset()
  rlMock.searchPeople.mockReset()
  rlMock.searchPeople.mockResolvedValue({ ok: true, data: { items: [], total: 0, limit: 60, offset: 0 } })
  rlMock.fetchEmployeeLines.mockResolvedValue({ ok: true, data: { active: [], history: [] } })
  rlMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [] })
  rlMock.fetchActiveVikar.mockResolvedValue({ ok: true, data: { activeVikar: null } })
  setupRouter()
})

async function waitForHydrated() {
  await waitFor(() => expect(screen.queryByTestId('person-drawer-loading')).toBeNull())
  await waitFor(() => expect((screen.getByTestId('ep-email') as HTMLInputElement).disabled).toBe(false))
}

describe('PersonDrawer — S148 / TASK-14850 NR-P4: no future date while the organisation changes', () => {
  /** F (vitest), AC-C13j (1). Red conditions: the block's text assertion, the disabled save and the
      input's `max` all fail on the base, where nothing caps or blocks a future date. */
  it('(1) F: organisation changed + Tuesday 1 December 2026 blocks the save, shows the Danish sentence, caps the input at today', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })
    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-01' } })

    expect(screen.getByTestId('pd-effective-blocked').textContent).toBe(BLOCK_TEXT)
    expect((screen.getByTestId('pd-effective-from') as HTMLInputElement).max).toBe('2026-11-10')
    expect(screen.getByTestId('pd-effective-from').getAttribute('aria-describedby')).toBe('pd-effective-blocked')
    expect((screen.getByRole('button', { name: 'Gem ændringer' }) as HTMLButtonElement).disabled).toBe(true)
  })

  /** R (vitest), AC-C13j (2). Pins S141's scheduling: no block, no `max`, the save sends the date. */
  it('(2) R: organisation unchanged + Tuesday 1 December 2026 is offered and saved as effectiveFrom', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-01' } })

    expect(screen.queryByTestId('pd-effective-blocked')).toBeNull()
    expect((screen.getByTestId('pd-effective-from') as HTMLInputElement).max).toBe('')

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!.body?.effectiveFrom).toBe('2026-12-01')
  })
})
