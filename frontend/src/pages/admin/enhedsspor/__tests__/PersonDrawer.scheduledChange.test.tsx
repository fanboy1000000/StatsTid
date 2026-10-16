// SPRINT-141 / TASK-14107 — B0 (the owner's visibility requirement: "should
// it not be visible to an HR employee looking at a page, that another has
// scheduled a change?") + OQ-6 (a) (the edit prompt: apply-until vs
// carry-forward), on the edit drawer's PROFILE and AGREEMENT-CODE fields, and
// the DangerSection awareness notice.
//
// The read payloads ALREADY carry the scheduled change (`scheduled` on the
// profile GET, `scheduledAgreementCode` on the `user` prop the host supplies
// — see PersonDrawer's own comment) — this suite proves the drawer actually
// SHOWS it, and that a same-values edit does not fabricate a stale carry-
// forward choice.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { WithEtag, User } from '../../../../hooks/useAdmin'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

// S143 / TASK-14313 — none of this file's assertions depend on the actual VALUE of "today" (they
// exercise scheduled-change visibility, not the effective-date default), so a single fixed
// AUTHORITY literal is enough here — unlike the sibling `PersonDrawer.effectiveDate.test.tsx` /
// `PersonDrawer.hireDate.test.tsx` files, which pin one to match a pre-existing asserted default.
const TEST_TODAY = '2026-07-16'

const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
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

const SCHEDULED_FROM = '2026-11-01'

function setupRouter() {
  calls = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const rec: Recorded = {
      url,
      method,
      body: init?.body ? JSON.parse(init.body as string) : null,
    }
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
          isPartTime: true,
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
          email: 'k@x.dk',
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
          memberCount: 0, directMemberCount: 0, units: [],
        },
      ],
    },
  ]
}

function renderEdit(user: Partial<WithEtag<User>> = {}) {
  const forest = makeForest()
  return renderWithCalendar(
    TEST_TODAY,
    // The drawer's edit-mode "Fratrædelse" link (react-router-dom `Link`)
    // needs a Router ancestor.
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
          currentUnitId={null}
          onClose={vi.fn()}
          onSaved={vi.fn()}
        />
      </ToastProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.role = 'LocalHR'
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
}

describe('PersonDrawer — B0 visibility: a scheduled PROFILE change', () => {
  it('shows the scheduled change with its date and values, read straight off the profile GET (no second call)', async () => {
    renderEdit()
    await waitForHydrated()

    const notice = screen.getByTestId('pd-profile-scheduled')
    expect(notice.textContent).toContain('november 2026')
    expect(notice.textContent).toContain('0,600')
    expect(notice.textContent).toContain('Future Title')
    // No second GET beyond the one hydrate call per endpoint.
    expect(calls.filter((c) => c.url.includes('/employee-profiles/') && c.method === 'GET')).toHaveLength(1)
  })

  it('shows NO carry-forward choice until the profile field is actually edited', async () => {
    renderEdit()
    await waitForHydrated()
    expect(screen.queryByTestId('pd-profile-scheduled-carry-forward')).toBeNull()
  })

  it('offers the carry-forward choice once the profile field is edited, and sends the choice on save', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })
    const carryForward = screen.getByTestId('pd-profile-scheduled-carry-forward') as HTMLInputElement
    expect(carryForward.checked).toBe(false) // default = "apply until"
    fireEvent.click(carryForward)
    expect(carryForward.checked).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )
    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.carryForwardToScheduledChange).toBe(true)
    expect(profilePut.body?.position).toBe('New Title')
  })

  it('sends carryForwardToScheduledChange: false (the default) when the choice is left unchecked', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )
    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.carryForwardToScheduledChange).toBe(false)
  })
})

describe('PersonDrawer — B0 visibility: a scheduled AGREEMENT-CODE change (the second dated field)', () => {
  it('shows the scheduled agreement-code change from the `user` prop — no second call needed', async () => {
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForHydrated()

    const notice = screen.getByTestId('pd-agreement-scheduled')
    expect(notice.textContent).toContain('november 2026')
    expect(notice.textContent).toContain('PROSA')
  })

  it('offers the carry-forward choice once the agreement code is edited, and sends the choice on save', async () => {
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForHydrated()

    expect(screen.queryByTestId('pd-agreement-scheduled-carry-forward')).toBeNull()
    fireEvent.change(screen.getByTestId('ep-agreement'), { target: { value: 'HK' } })
    const carryForward = screen.getByTestId('pd-agreement-scheduled-carry-forward') as HTMLInputElement
    fireEvent.click(carryForward)

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))
    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.body?.carryForwardToScheduledChange).toBe(true)
    expect(usersPut.body?.agreementCode).toBe('HK')
  })

  it('renders nothing when nothing is scheduled', async () => {
    renderEdit({ scheduledAgreementCode: null })
    await waitForHydrated()
    expect(screen.queryByTestId('pd-agreement-scheduled')).toBeNull()
  })
})

// QUAL-207 — the ordering race behind the one-off CI red on the "offers the carry-forward choice"
// fact above. `hydrate()` finishes (profile + `live` + `hydrating=false` commit in ONE render) and
// only LATER does the passive re-baseline effect run. An edit delivered in that gap used to meet
// `baselineRef === null` and be overwritten by the live value. A human cannot hit the gap; this
// test lands in it on purpose and deterministically: the scheduled-change notice is wrapped so that,
// in its LAYOUT effect (which React always runs after the hydrated commit and BEFORE any passive
// effect, whatever the render's priority), it fires the user's edit once. (A MutationObserver
// version of this was tried and rejected: whether the passive effects had already flushed by then
// depended on test warm-up, so it was red alone and green in the full file.)
// Raw DOM dispatch (not RTL's fireEvent): fireEvent wraps the event in act(), which tries to flush
// React work synchronously and cannot do that from inside a commit.
function rawChange(el: HTMLInputElement | HTMLSelectElement, value: string, type: 'input' | 'change') {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(proto, 'value')!.set!.call(el, value)
  el.dispatchEvent(new Event(type, { bubbles: true }))
}
const raceNotice = vi.hoisted(() => ({ armed: false, fired: new Set<string>() }))
vi.mock('../../editPerson/ScheduledChangeNotice', async (importActual) => {
  const actual = await importActual<typeof import('../../editPerson/ScheduledChangeNotice')>()
  const React = await import('react')
  const RTL = await import('@testing-library/react')
  return {
    ...actual,
    ScheduledChangeNotice: (props: Parameters<typeof actual.ScheduledChangeNotice>[0]) => {
      const id = (props as { testId?: string }).testId ?? ''
      React.useLayoutEffect(() => {
        if (!raceNotice.armed || raceNotice.fired.has(id)) return
        raceNotice.fired.add(id)
        if (id === 'pd-profile-scheduled') {
          rawChange(RTL.screen.getByTestId('ep-position') as HTMLInputElement, 'New Title', 'input')
        } else if (id === 'pd-agreement-scheduled') {
          rawChange(RTL.screen.getByTestId('ep-agreement') as HTMLSelectElement, 'HK', 'change')
        }
      })
      return React.createElement(actual.ScheduledChangeNotice, props)
    },
  }
})

describe('PersonDrawer — QUAL-207: an edit landing right after hydration is never overwritten', () => {
  it('keeps an edit fired between the hydrated commit and the re-baseline effects, and offers the carry-forward choice (profile + agreement)', async () => {
    raceNotice.armed = true
    raceNotice.fired.clear()
    try {
      renderEdit({
        scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
      })
      await waitForHydrated()

      expect(raceNotice.fired.has('pd-profile-scheduled')).toBe(true)
      expect(raceNotice.fired.has('pd-agreement-scheduled')).toBe(true)
      await waitFor(() => expect(screen.getByTestId('pd-profile-scheduled-carry-forward')).toBeDefined())
      expect((screen.getByTestId('ep-position') as HTMLInputElement).value).toBe('New Title')
      expect(screen.getByTestId('pd-agreement-scheduled-carry-forward')).toBeDefined()
      expect((screen.getByTestId('ep-agreement') as HTMLSelectElement).value).toBe('HK')
    } finally {
      raceNotice.armed = false
    }
  })
})

describe('PersonDrawer — B0 visibility: the danger section (awareness, not consequence)', () => {
  it('shows the scheduled profile AND agreement changes before HR confirms "Fjern medarbejder fra afgrænsning"', async () => {
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForHydrated()

    fireEvent.click(screen.getByTestId('danger-open'))
    expect(screen.getByTestId('danger-profile-scheduled').textContent).toContain('Future Title')
    expect(screen.getByTestId('danger-agreement-scheduled').textContent).toContain('PROSA')
  })
})

// SPRINT-141 / TASK-14108 (termination screen) + TASK-14107 follow-up — the
// screen the coordinator flagged as built but unreachable: the drawer is the
// ONLY place that can link to it (no sidebar entry by design). Termination
// does NOT cancel a scheduled change (only deleting the profile does, an
// unrelated action), so this link — and the copy around it — must never
// imply that saving/visiting it affects the scheduled-change notices above.
describe('PersonDrawer — the "Fratrædelse" link to the termination screen (TASK-14108 follow-up)', () => {
  it('links to the termination route for THIS employee, in edit mode', async () => {
    renderEdit()
    await waitForHydrated()

    const link = screen.getByTestId('pd-termination-link') as HTMLAnchorElement
    expect(link.getAttribute('href')).toBe('/admin/medarbejdere/EMP1/fratraedelse')
  })

  it('does NOT render in create mode (nobody to terminate yet)', () => {
    const forest = makeForest()
    renderWithCalendar(
      TEST_TODAY,
      <MemoryRouter>
        <ToastProvider>
          <PersonDrawer
            open
            organizations={orgsFromForest(forest)}
            forest={forest}
            defaultOrgId="STY02"
            defaultUnitId={null}
            onClose={vi.fn()}
            onSaved={vi.fn()}
          />
        </ToastProvider>
      </MemoryRouter>,
    )
    expect(screen.queryByTestId('pd-termination-link')).toBeNull()
  })

  it('renders alongside a scheduled change WITHOUT implying termination cancels it', async () => {
    renderEdit()
    await waitForHydrated()

    // Both are present — the point is that neither's copy references the other.
    expect(screen.getByTestId('pd-profile-scheduled')).toBeDefined()
    const section = screen.getByTestId('pd-termination-link').closest('section')!
    expect(section.textContent).not.toMatch(/planlagt|scheduled|annulle/i)
  })
})
