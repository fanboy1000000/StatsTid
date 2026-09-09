// SPRINT-140 / TASK-14007 (HRP-016) — the admin CREATE form's hire date.
// Separate file from `PersonDrawer.test.tsx` (that suite never submits the
// create form; this one exists specifically to prove the wire-up end to end:
// the field is pre-filled, editable, and its value reaches the create POST
// body as `employmentStartDate` — the ONE thing an undated create silently
// gets wrong (S137: omitted ⇒ "hired today" server-side, blocking any
// back-filled registration from before the record existed).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
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

const todayIso = () => new Date().toISOString().slice(0, 10)

describe('PersonDrawer — the create-mode hire date (HRP-016)', () => {
  it('pre-fills the field with today', () => {
    renderCreate()
    expect((screen.getByTestId('pd-employment-start') as HTMLInputElement).value).toBe(todayIso())
  })

  it('sends the pre-filled (today) date on the create POST when left untouched', async () => {
    mockFetch.mockImplementation(async () => jsonResponse({ userId: 'EMP010', version: 1 }))
    renderCreate()

    fireEvent.change(screen.getByTestId('pd-create-user-id'), { target: { value: 'EMP010' } })
    fireEvent.change(screen.getByTestId('pd-create-username'), { target: { value: 'emp010' } })
    fireEvent.change(screen.getByTestId('pd-create-password'), { target: { value: 'password123' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ny Person' } })

    fireEvent.click(screen.getByRole('button', { name: 'Opret medarbejder' }))

    await waitFor(() => expect(mockFetch).toHaveBeenCalled())
    expect(createRequestBody().employmentStartDate).toBe(todayIso())
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
