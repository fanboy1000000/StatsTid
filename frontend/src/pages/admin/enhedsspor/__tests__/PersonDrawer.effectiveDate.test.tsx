// SPRINT-141 / TASK-14111 — the effective-date picker: the feature every
// other task in this sprint's Increment 4 exists to support. The backend
// (waves 1–2) already accepts ANY effective date on the profile and
// agreement-code writes; until this task the drawer never offered HR a way
// to choose one — `useEditPerson.saveEdit` always sent today's date,
// hardcoded (see `useEditPerson.test.tsx`'s own new suite for the plumbing
// proof). This suite proves the CONTROL: it defaults to today, it warns
// before a future-dated save that nothing changes yet, it still allows a
// past date (backdating pre-dates this sprint and must not be narrowed by
// adding the future half), and it composes with the sibling B0/OQ-6 notice
// (TASK-14107) rather than contradicting it once a save is no longer "now".

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../../../../components/ui/Toast'
import { todayIsoUtc } from '../../../../hooks/useEditPerson'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { WithEtag, User } from '../../../../hooks/useAdmin'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'

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

const SCHEDULED_FROM = '2026-12-01'

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
          // A change already scheduled for LATER than the dates this suite
          // picks (so the two notices' texts are both exercisable at once).
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
  return render(
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

function renderCreate() {
  const forest = makeForest()
  return render(
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

describe('PersonDrawer — S141 / TASK-14111 the effective-date picker: presence + default', () => {
  it('renders in edit mode, defaulting to today (requirement 1 — the default stays today)', async () => {
    renderEdit()
    await waitForHydrated()

    const input = screen.getByTestId('pd-effective-from') as HTMLInputElement
    expect(input.value).toBe(todayIsoUtc())
  })

  it('does NOT render at create — a brand-new person has no existing value for a scheduled change to apply against', () => {
    renderCreate()
    expect(screen.queryByTestId('pd-effective-from')).toBeNull()
  })

  it('shows no "nothing changes yet" banner while the date is left at its default (today)', async () => {
    renderEdit()
    await waitForHydrated()
    expect(screen.queryByTestId('pd-effective-future-notice')).toBeNull()
  })
})

describe('PersonDrawer — S141 / TASK-14111 requirement 2: say what will happen, before it happens', () => {
  it('shows a "nothing changes today" banner once HR picks a date AFTER today', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-10-01' } })

    const banner = screen.getByTestId('pd-effective-future-notice')
    expect(banner.textContent).toContain('ændres ikke i dag')
    expect(banner.textContent).toContain('oktober 2026')
    // The banner must not overclaim: name/email/organisation are plain
    // mutable columns (no temporal concept in the domain), and the backend
    // applies them immediately regardless of this date — see
    // `AdminEndpoints.cs`'s own `UpdateUserRequest` handler. A banner saying
    // "nothing changes" without this caveat would be false the moment HR
    // also edits, say, the display name in the same save.
    expect(banner.textContent).toMatch(/gemmes straks/)
  })

  it('removes the banner again if HR changes the date back to today', async () => {
    renderEdit()
    await waitForHydrated()

    const input = screen.getByTestId('pd-effective-from')
    fireEvent.change(input, { target: { value: '2026-10-01' } })
    expect(screen.getByTestId('pd-effective-future-notice')).toBeDefined()

    fireEvent.change(input, { target: { value: todayIsoUtc() } })
    expect(screen.queryByTestId('pd-effective-future-notice')).toBeNull()
  })

  it('does NOT show the future banner for a PAST date (requirement 4 — backdating is not "the future")', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2020-01-15' } })

    expect(screen.queryByTestId('pd-effective-future-notice')).toBeNull()
    // The value itself is accepted, unclamped — no `min` narrowed it away.
    expect((screen.getByTestId('pd-effective-from') as HTMLInputElement).value).toBe('2020-01-15')
  })
})

describe('PersonDrawer — S141 / TASK-14111: the picked date reaches BOTH dated writes on save', () => {
  it('sends the picked date as effectiveFrom on the employee-profiles PUT and the users PUT alike', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )

    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.effectiveFrom).toBe('2026-10-01')
    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.body?.effectiveFrom).toBe('2026-10-01')
  })

  it('still sends today when HR never touches the picker (the pre-existing behaviour, unchanged)', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )

    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.effectiveFrom).toBe(todayIsoUtc())
  })
})

describe('PersonDrawer — S141 / TASK-14111 requirement 3: composes with the B0/OQ-6 notice (TASK-14107) instead of contradicting it', () => {
  it('keeps the "Gemmer du nu" wording when the picker is left at today (no regression on the sibling task)', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })
    const notice = screen.getByTestId('pd-profile-scheduled')
    expect(notice.textContent).toContain('Gemmer du nu')
    expect(notice.textContent).not.toContain('virkning fra')
  })

  it('replaces "Gemmer du nu" with the real save date once the picker is dated ahead of today', async () => {
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'New Title' } })

    const notice = screen.getByTestId('pd-profile-scheduled')
    expect(notice.textContent).not.toContain('Gemmer du nu')
    expect(notice.textContent).toContain('Gemmer du med virkning fra')
    expect(notice.textContent).toContain('oktober 2026')
    // The EXISTING scheduled change's own date (December) is untouched —
    // the two dates in the composed sentence must not collapse into one.
    expect(notice.textContent).toContain('december 2026')
  })

  it('the same composition holds for the AGREEMENT-CODE notice (the second dated field)', async () => {
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByTestId('ep-agreement'), { target: { value: 'HK' } })

    const notice = screen.getByTestId('pd-agreement-scheduled')
    expect(notice.textContent).not.toContain('Gemmer du nu')
    expect(notice.textContent).toContain('Gemmer du med virkning fra')
  })
})

// SPRINT-END BLOCKER (2026-09-14, coordinator-verified) — this sprint's own
// defect class (a same-values write silently reverting a scheduled change)
// came back through the picker itself: picking a date AT OR AFTER an
// existing scheduled change's own start put the write INSIDE that change's
// interval, but the drawer still pre-filled the profile/agreement-code
// fields from TODAY's values — so an untouched field sent today's stale
// values dated into the scheduled interval, and the backend (correctly
// comparing against the row that actually covers that date) wrote them
// forward, reverting the colleague's scheduled decision. This is exactly the
// relationship the ORIGINAL suite above never tests: every prior test in
// this file picks '2026-10-01', strictly BEFORE `SCHEDULED_FROM`
// ('2026-12-01') — the one relationship that matters, picked-date >=
// scheduled-date, was excluded by construction.
function setupRouterWithProfileSchedule(profileEffectiveTo: string | null) {
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
            effectiveTo: profileEffectiveTo,
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
          displayName: rec.body!.displayName ?? 'Karen Nielsen',
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

describe('PersonDrawer — SPRINT-END BLOCKER FIX: a picked date AT OR AFTER a scheduled change re-baselines from the scheduled row', () => {
  it('THE MISSING TEST — sends the scheduled row untouched (not today\'s stale values) when the picked date covers it and HR edits an unrelated field', async () => {
    setupRouterWithProfileSchedule(null) // open-ended scheduled change
    renderEdit()
    await waitForHydrated()

    // AT the scheduled change's own start — "at or after", the boundary case.
    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: SCHEDULED_FROM } })
    // An edit that has NOTHING to do with the profile/agreement fields.
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'New Display Name' } })

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )

    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    // The SCHEDULED row's own values — NOT today's (1.0 / 'Old Title'), which
    // is exactly what would silently overwrite the colleague's decision.
    expect(profilePut.body?.partTimeFraction).toBe(0.6)
    expect(profilePut.body?.position).toBe('Future Title')
    expect(profilePut.body?.effectiveFrom).toBe(SCHEDULED_FROM)

    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.body?.displayName).toBe('New Display Name')
  })

  it('preserves per-field independence: an edited field keeps its new value while an untouched sibling field still follows the scheduled row', async () => {
    setupRouterWithProfileSchedule(null)
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-15' } }) // AFTER the start
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'Custom Title' } }) // edited
    // partTimeFraction (ep-part-time) is left untouched.

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() =>
      expect(calls.some((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')).toBe(true),
    )

    const profilePut = calls.find((c) => c.url.includes('/employee-profiles/') && c.method === 'PUT')!
    expect(profilePut.body?.position).toBe('Custom Title') // HR's deliberate edit, sent verbatim
    expect(profilePut.body?.partTimeFraction).toBe(0.6) // untouched — the SCHEDULED fraction, not today's 1.0
  })

  it('does the same for the AGREEMENT CODE: an untouched agreement code sends the scheduled code, not today\'s', async () => {
    setupRouterWithProfileSchedule(null)
    renderEdit({
      scheduledAgreementCode: { effectiveFrom: SCHEDULED_FROM, effectiveTo: null, agreementCode: 'PROSA' },
    })
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: SCHEDULED_FROM } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'New Display Name' } }) // agreement untouched

    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')).toBe(true))

    const usersPut = calls.find((c) => c.url.endsWith('/users/EMP1') && c.method === 'PUT')!
    expect(usersPut.body?.agreementCode).toBe('PROSA') // the SCHEDULED code, not today's 'AC'
  })

  it('does NOT clobber a manual edit made BEFORE the date was moved into the scheduled interval', async () => {
    setupRouterWithProfileSchedule(null)
    renderEdit()
    await waitForHydrated()

    // HR types a new title FIRST, while the picker is still at today ('before').
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'Manually Typed Title' } })
    // THEN moves the date into the scheduled interval.
    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: SCHEDULED_FROM } })

    // The deliberate edit survives the date change...
    expect((screen.getByTestId('ep-position') as HTMLInputElement).value).toBe('Manually Typed Title')
    // ...but the UNTOUCHED fraction re-baselines to the scheduled value (0.600), not today's (1.000).
    expect((screen.getByTestId('ep-part-time') as HTMLInputElement).value).toBe('0.600')
  })

  it('shows an advisory note naming which field(s) were re-baselined', async () => {
    setupRouterWithProfileSchedule(null)
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: SCHEDULED_FROM } })

    const note = screen.getByTestId('pd-effective-covers-notice')
    expect(note.textContent).toContain('deltid/stilling')
  })

  it('replaces the carry-forward checkbox with the "supersedes" wording — never the chronologically impossible "gælder kun indtil" sentence', async () => {
    setupRouterWithProfileSchedule(null)
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-15' } })
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'Custom Title' } })

    expect(screen.queryByTestId('pd-profile-scheduled-carry-forward')).toBeNull()
    const supersedesText = screen.getByTestId('pd-profile-scheduled-supersedes')
    expect(supersedesText.textContent).not.toContain('gælder ændringen kun indtil')
    expect(supersedesText.textContent).toContain('erstatter')
    expect(supersedesText.textContent).toContain('december 2026')
  })

  it('refuses the save entirely once the picked date is at or beyond the scheduled change\'s own END', async () => {
    setupRouterWithProfileSchedule('2026-12-20') // bounded: Dec 1 – Dec 20
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-20' } }) // AT the end

    const blocked = screen.getByTestId('pd-effective-blocked')
    expect(blocked.textContent).toMatch(/deltid\/stilling/)
    const submitBtn = screen.getByRole('button', { name: 'Gem ændringer' }) as HTMLButtonElement
    expect(submitBtn.disabled).toBe(true)

    // A disabled submit button cannot fire the form's submit handler —
    // belt-and-braces proof that no write was even attempted.
    fireEvent.click(submitBtn)
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('does NOT block a date still WITHIN the scheduled change even when it has a bounded end', async () => {
    setupRouterWithProfileSchedule('2026-12-20')
    renderEdit()
    await waitForHydrated()

    fireEvent.change(screen.getByTestId('pd-effective-from'), { target: { value: '2026-12-10' } }) // inside Dec1–Dec20

    expect(screen.queryByTestId('pd-effective-blocked')).toBeNull()
    const submitBtn = screen.getByRole('button', { name: 'Gem ændringer' }) as HTMLButtonElement
    expect(submitBtn.disabled).toBe(false)
  })
})
