// SPRINT-140 / TASK-14007 (HRP-016) — the admin CREATE form's hire date.
// Separate file from `PersonDrawer.test.tsx` (that suite never submits the
// create form; this one exists specifically to prove the wire-up end to end:
// the field is pre-filled, editable, and its value reaches the create POST
// body as `employmentStartDate` — the ONE thing an undated create silently
// gets wrong (S137: omitted ⇒ "hired today" server-side, blocking any
// back-filled registration from before the record existed).
import { describe, it, expect, vi, beforeEach, beforeAll, afterAll, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'
import { PersonDrawer } from '../PersonDrawer'
import { forceTestTimeZone, restoreTestTimeZone } from '../../../../lib/__tests__/testTimeZone'

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
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})
Object.defineProperty(window, 'location', { value: { reload: vi.fn() }, writable: true })

function jsonResponse(body: unknown, status = 201) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ ETag: '"1"' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
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
          units: [],
        },
      ],
    },
  ]
}

function renderCreate() {
  const forest = makeForest()
  return render(
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
    </ToastProvider>,
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
})

/** The exact request body of the (single) POST /api/admin/users call. */
function createRequestBody(): Record<string, unknown> {
  const call = mockFetch.mock.calls.find((c) => c[0] === '/api/admin/users')
  expect(call).toBeDefined()
  const init = call![1] as RequestInit
  return JSON.parse(init.body as string)
}

// S142 test-clock sweep (701f4b6) flagged this suite's OWN `todayIso()` as INERT: it was a LOCAL
// RE-IMPLEMENTATION of the raw UTC formula (`new Date().toISOString().slice(0, 10)`), not an
// import, so it computed the IDENTICAL expression the component (then also still on the raw
// formula) used — the two could never disagree, so this test would have passed whether or not
// `PersonDrawer.tsx` was ever fixed. That sweep's comment tracked the fix to land together with
// `PersonDrawer.tsx`'s own migration; TASK-14209 is that migration (census row 62 — the
// create-mode hire-date pre-fill now reads the Copenhagen day via `useEditPerson.ts`'s
// `todayIso()` / `copenhagenDate.ts`), so this file updates in the SAME change as promised.
//
// WHY THE ZONE MUST BE FORCED (not just the clock pinned). The developer machine this suite runs
// on is ALSO on Copenhagen time, so browser-local and Copenhagen would agree here by coincidence —
// forcing the zone away from Copenhagen is what makes the literals below actually discriminate a
// regression. See `testTimeZone.ts` and `MondayDatePicker.test.tsx` (the first site this pattern
// shipped for).
describe('PersonDrawer — the create-mode hire date (HRP-016)', () => {
  let restoreTz: string | undefined

  beforeAll(() => {
    restoreTz = forceTestTimeZone('America/New_York')
  })

  afterAll(() => {
    restoreTestTimeZone(restoreTz)
  })

  beforeEach(() => {
    // `toFake: ['Date']` only — `setTimeout` stays REAL, so the `waitFor` calls below keep
    // working normally; only `new Date()` (and therefore `todayIso()`) reads the pinned instant.
    vi.useFakeTimers({ toFake: ['Date'] })
    // 2026-07-15 22:30 UTC — the SAME pinned instant `MondayDatePicker.test.tsx` and
    // `useEditPerson.test.tsx`'s S142 guard use: Copenhagen (CEST, +02:00) already reads 00:30 on
    // the 16th; New York (EDT, -04:00) still reads 18:30 on the 15th; UTC itself reads the 15th.
    vi.setSystemTime(new Date('2026-07-15T22:30:00Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // THE GUARD ON THE GUARD: if the zone forcing ever stopped taking effect, the facts below would
  // not fail — they would quietly start passing again with browser-local and Copenhagen
  // coincidentally agreeing (this host IS Copenhagen), which is the worst outcome available.
  it('runs in a zone behind Copenhagen, which is what lets the facts below fail on a regression', () => {
    const pinned = new Date('2026-07-15T22:30:00Z')
    expect(pinned.getDate()).toBe(15)
    expect(pinned.getHours()).toBe(18)
  })

  it('pre-fills the field with the Copenhagen today', () => {
    renderCreate()
    // LITERAL, not a call to a formula the component also computes — see this block's header.
    // Pre-S142 (the raw UTC formula) this would have been '2026-07-15'.
    expect((screen.getByTestId('pd-employment-start') as HTMLInputElement).value).toBe('2026-07-16')
  })

  it('sends the pre-filled (Copenhagen today) date on the create POST when left untouched', async () => {
    mockFetch.mockImplementation(async () => jsonResponse({ userId: 'EMP010', version: 1 }))
    renderCreate()

    fireEvent.change(screen.getByTestId('pd-create-user-id'), { target: { value: 'EMP010' } })
    fireEvent.change(screen.getByTestId('pd-create-username'), { target: { value: 'emp010' } })
    fireEvent.change(screen.getByTestId('pd-create-password'), { target: { value: 'password123' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ny Person' } })

    fireEvent.click(screen.getByRole('button', { name: 'Opret medarbejder' }))

    await waitFor(() => expect(mockFetch).toHaveBeenCalled())
    expect(createRequestBody().employmentStartDate).toBe('2026-07-16')
  })

  it('is editable, and the EDITED value — not today — reaches the create POST body', async () => {
    mockFetch.mockImplementation(async () => jsonResponse({ userId: 'EMP011', version: 1 }))
    renderCreate()

    fireEvent.change(screen.getByTestId('pd-create-user-id'), { target: { value: 'EMP011' } })
    fireEvent.change(screen.getByTestId('pd-create-username'), { target: { value: 'emp011' } })
    fireEvent.change(screen.getByTestId('pd-create-password'), { target: { value: 'password123' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Bagudrettet Person' } })
    fireEvent.change(screen.getByTestId('pd-employment-start'), { target: { value: '2020-01-15' } })

    fireEvent.click(screen.getByRole('button', { name: 'Opret medarbejder' }))

    await waitFor(() => expect(mockFetch).toHaveBeenCalled())
    expect(createRequestBody().employmentStartDate).toBe('2020-01-15')
  })
})
