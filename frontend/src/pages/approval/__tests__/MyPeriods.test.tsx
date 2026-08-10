// MyPeriods — the employee's period list.
//
// ⚠ S127 / TASK-12707 (owner ruling R3) — THIS FILE WAS BUILT AROUND A PAGE
// FEATURE THAT NO LONGER EXISTS. Its original subject was the S82-8202 pin for
// the `POST /api/approval/submit` `orgId` bug: MyPeriods omitted the required
// `orgId` from the submit body, so every submission 400'd. That route is retired
// and the free-range "Indsend periode" form is removed, so both tests in the old
// `submit wire contract` describe — the orgId body pin and the missing-orgId
// guard — were deleted rather than adapted. There is nothing left of that bug to
// regress: the field, the form and the endpoint are all gone.
//
// What this file covers NOW: the mount read, the by-id re-send (the page's only
// remaining write), the ABSENCE of the send form, and the status vocabulary.
//
// Mirrors the SIBLING ApprovalDashboard.test.tsx harness: mock the AuthContext
// module (the useAuth re-export resolves through it), stub globalThis.fetch +
// localStorage, render, interact, assert the wire contract.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MyPeriods } from '../MyPeriods'

// --- Auth mock (per-test mutable so the missing-orgId guard test can null it) ---
// MyPeriods imports useAuth from '../../hooks/useAuth', which RE-EXPORTS from
// '../contexts/AuthContext' — so mocking the AuthContext module intercepts it.
const mockAuth: { user: { employeeId: string; role: string } | null; orgId: string | null } = {
  user: { employeeId: 'EMP_SELF', role: 'Employee' },
  orgId: 'STY01',
}

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: mockAuth.user,
    role: mockAuth.user?.role ?? null,
    orgId: mockAuth.orgId,
    agreementCode: 'AC',
    scopes: [],
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {
  statstid_token: 'test-token',
}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

/** Build a mock Response for the given JSON body. apiClient.post calls fetch
 *  internally, so we intercept at the globalThis.fetch boundary. */
function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/** One element of GET /api/approval/{employeeId} (the EmployeePeriodItem shape). */
const periodItem = {
  periodId: 'p-new-1',
  employeeId: 'EMP_SELF',
  orgId: 'STY01',
  periodStart: '2026-07-01',
  periodEnd: '2026-07-31',
  periodType: 'MONTHLY',
  status: 'EMPLOYEE_APPROVED',
  agreementCode: 'AC',
  okVersion: 'OK26',
  submittedAt: '2026-06-17T10:00:00Z',
  approvedBy: null,
  approvedAt: null,
  rejectionReason: null,
  createdAt: '2026-06-17T10:00:00Z',
}

/** Route the mount read (GET /api/approval/{employeeId}) to `rows`; everything else → {}. */
function mockFetches(rows: unknown[] = []) {
  mockFetch.mockImplementation(async (url: string) => {
    if (typeof url === 'string' && /\/api\/approval\/[^/]+$/.test(url)) {
      return jsonResponse(rows)
    }
    return jsonResponse({})
  })
}

/** Every recorded POST, whatever the route. */
function postCalls() {
  return mockFetch.mock.calls.filter((call: unknown[]) => (call[1] as RequestInit | undefined)?.method === 'POST')
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  // Restore the default authenticated employee with an org before each test.
  mockAuth.user = { employeeId: 'EMP_SELF', role: 'Employee' }
  mockAuth.orgId = 'STY01'
})

describe('MyPeriods — the free-range send form is GONE (S127 / TASK-12707, owner ruling R3)', () => {
  it('renders NO send form: no date inputs, no periodetype/overenskomst selects, no "Indsend periode" button', async () => {
    mockFetches()
    render(<MyPeriods />)
    // Mount read settled — the list card is up.
    await waitFor(() => expect(screen.getByText('Ingen perioder fundet.')).toBeInTheDocument())

    // Each of these existed and was interactive before this task.
    expect(screen.queryByRole('button', { name: /Indsend periode/ })).toBeNull()
    expect(screen.queryByLabelText('Startdato')).toBeNull()
    expect(screen.queryByLabelText('Slutdato')).toBeNull()
    expect(screen.queryByLabelText('Periodetype')).toBeNull()
    expect(screen.queryByLabelText('Overenskomst')).toBeNull()
    expect(screen.queryByLabelText('OK version')).toBeNull()
    // The banner the form raised on success is gone with it.
    expect(screen.queryByText('Periode indsendt.')).toBeNull()
    // And the page keeps its title + list card.
    expect(screen.getByText('Mine perioder')).toBeInTheDocument()
    expect(screen.getByText('Perioder')).toBeInTheDocument()
  })

  it('issues NO write on mount — the page is read-only until a re-send is pressed', async () => {
    mockFetches([{ ...periodItem, status: 'REJECTED', rejectionReason: 'Mangler' }])
    render(<MyPeriods />)
    await waitFor(() => expect(screen.getByText('Afvist', { selector: '.badge' })).toBeInTheDocument())
    expect(postCalls()).toHaveLength(0)
  })
})

// ── S127 / TASK-12707 — the status vocabulary ────────────────────────────────
// `statusBadgeClass` and `statusLabel` had NO `EMPLOYEE_APPROVED` case and both
// fell through to `default: return status`, so the employee saw the raw enum
// string. Every period sent through `/api/approval/send` lands in that state, so
// this is the state the page will show most often.
describe('MyPeriods — status vocabulary', () => {
  // NOTE the `{ selector: '.badge' }` scoping throughout: the table's own column
  // header is also the word "Indsendt", so an unscoped query matches two nodes.
  it('EMPLOYEE_APPROVED renders the Danish label, never the raw enum', async () => {
    mockFetches([periodItem])
    render(<MyPeriods />)
    await waitFor(() => expect(screen.getByText('Indsendt', { selector: '.badge' })).toBeInTheDocument())
    // The discriminating half: before the fix this cell read "EMPLOYEE_APPROVED".
    expect(screen.queryByText('EMPLOYEE_APPROVED')).toBeNull()
    // `statusBadgeClass` too — its `default` arm also returned a class, so assert the
    // SPECIFIC one, not merely that some badge class is present.
    expect(screen.getByText('Indsendt', { selector: '.badge' }).className).toMatch(/badgeWarning/)
  })

  it('the other four states keep their labels', async () => {
    mockFetches([
      { ...periodItem, periodId: 'p-d', status: 'DRAFT' },
      { ...periodItem, periodId: 'p-s', status: 'SUBMITTED' },
      { ...periodItem, periodId: 'p-a', status: 'APPROVED' },
      { ...periodItem, periodId: 'p-r', status: 'REJECTED' },
    ])
    render(<MyPeriods />)
    await waitFor(() => expect(screen.getByText('Kladde', { selector: '.badge' })).toBeInTheDocument())
    expect(screen.getByText('Indsendt', { selector: '.badge' })).toBeInTheDocument()
    expect(screen.getByText('Godkendt', { selector: '.badge' })).toBeInTheDocument()
    expect(screen.getByText('Afvist', { selector: '.badge' })).toBeInTheDocument()
  })
})

// ── S116 / TASK-11602 — the typed-switch wire pins ───────────────────────────
// The mount read + the resubmit (employee-approve) switched to the typed
// spec-keyed forms; these pins assert the exact URLs and that the resubmit
// carries NO body (that call never sent one — no request delta; the L1 fix
// also stripped the `<ApprovalPeriod>` response overclaim, invisible on the
// wire because the backend serves `{periodId, status}` either way).
describe('MyPeriods — S116 typed-switch wire pins', () => {
  it('the mount read hits GET /api/approval/{employeeId} (interpolated, exact)', async () => {
    mockFetches()
    render(<MyPeriods />)
    // S127: the form button this used to wait on is gone; the empty-state marks the
    // same moment (the mount read has resolved and the list card has rendered).
    await waitFor(() => {
      expect(screen.getByText('Ingen perioder fundet.')).toBeInTheDocument()
    })
    const mountRead = mockFetch.mock.calls.find((call: unknown[]) => {
      const init = call[1] as RequestInit | undefined
      return (init?.method ?? 'GET') === 'GET'
    })
    expect(mountRead?.[0]).toBe('/api/approval/EMP_SELF')
  })

  it('resubmit (Indsend on a REJECTED row) → POST /api/approval/{periodId}/employee-approve with NO body', async () => {
    const user = userEvent.setup()
    const captured: Array<{ url: string; method: string; body: unknown }> = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      captured.push({
        url,
        method: init?.method ?? 'GET',
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      })
      if (typeof url === 'string' && url.includes('/employee-approve')) {
        return jsonResponse({ periodId: 'p-rej-1', status: 'EMPLOYEE_APPROVED' })
      }
      if (typeof url === 'string' && /\/api\/approval\/[^/]+$/.test(url)) {
        return jsonResponse([{ ...periodItem, periodId: 'p-rej-1', status: 'REJECTED', rejectionReason: 'Mangler' }])
      }
      return jsonResponse({})
    })

    render(<MyPeriods />)
    // The REJECTED row renders an "Indsend" row action.
    const resubmitBtn = await screen.findByRole('button', { name: 'Indsend' })
    await user.click(resubmitBtn)

    await waitFor(() => {
      const post = captured.find(c => c.method === 'POST')
      expect(post?.url).toBe('/api/approval/p-rej-1/employee-approve')
      expect(post?.body).toBeUndefined()
    })
    // The success banner confirms the 200 path ran end-to-end.
    await waitFor(() => {
      expect(screen.getByText('Periode genindsendt.')).toBeDefined()
    })
  })
})
