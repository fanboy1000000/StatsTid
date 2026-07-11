// S82 TASK-8202 dual-lens follow-up — the unit-tier REGRESSION PIN for the
// MyPeriods /api/approval/submit `orgId` bug.
//
// The bug (fixed in S82-8202): MyPeriods.tsx omitted the server-`required` `orgId`
// from its POST /api/approval/submit body, so EVERY new-period submission 400'd
// ("OrgId is required", SubmitPeriodRequest.OrgId). The fix sources `orgId` from
// useAuth() and guards on a missing value (MyPeriods.tsx:60/127/134-142).
//
// The load-bearing test below is DISCRIMINATING for that fix: it asserts the
// captured submit body carries `orgId` with the auth-context value (proven
// RED-on-old: dropping orgId from the payload makes the assertion fail).
//
// Mirrors the SIBLING ApprovalDashboard.test.tsx harness: mock the AuthContext
// module (the useAuth re-export resolves through it), stub globalThis.fetch +
// localStorage, render, interact, assert the wire contract.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
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

/** A successfully-submitted period the /submit POST returns on 200. */
const submittedPeriod = {
  periodId: 'p-new-1',
  employeeId: 'EMP_SELF',
  orgId: 'STY01',
  periodStart: '2026-07-01',
  periodEnd: '2026-07-07',
  periodType: 'WEEKLY',
  status: 'SUBMITTED',
  agreementCode: 'AC',
  okVersion: 'OK24',
  submittedAt: '2026-06-17T10:00:00Z',
  approvedBy: null,
  approvedAt: null,
  rejectionReason: null,
  createdAt: '2026-06-17T10:00:00Z',
}

/**
 * Route the mount read (GET /api/approval/{employeeId}) to an empty list and
 * the submit POST (POST /api/approval/submit) to a 200. Records all calls so
 * the test can inspect the submit body.
 */
function mockFetches() {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (typeof url === 'string' && url.includes('/api/approval/submit') && init?.method === 'POST') {
      return jsonResponse(submittedPeriod)
    }
    // The mount read of the employee's existing periods.
    if (typeof url === 'string' && /\/api\/approval\/[^/]+$/.test(url)) {
      return jsonResponse([])
    }
    return jsonResponse({})
  })
}

/** Find the recorded POST /api/approval/submit call (if any). */
function findSubmitCall() {
  return mockFetch.mock.calls.find((call: unknown[]) => {
    const u = call[0] as string
    const init = call[1] as RequestInit | undefined
    return typeof u === 'string' && u.includes('/api/approval/submit') && init?.method === 'POST'
  })
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  // Restore the default authenticated employee with an org before each test.
  mockAuth.user = { employeeId: 'EMP_SELF', role: 'Employee' }
  mockAuth.orgId = 'STY01'
})

describe('MyPeriods — submit wire contract (S82-8202 orgId regression pin)', () => {
  it('POST /api/approval/submit carries orgId from the auth context (plus employeeId + dates)', async () => {
    const user = userEvent.setup()
    mockFetches()

    render(<MyPeriods />)

    // Wait for the form to be present (mount read resolves to an empty list).
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Indsend periode/ })).toBeDefined()
    })

    // Fill the required dates (periodType + agreementCode + okVersion default to
    // WEEKLY / AC / OK24).
    fireEvent.change(screen.getByLabelText('Startdato'), { target: { value: '2026-07-01' } })
    fireEvent.change(screen.getByLabelText('Slutdato'), { target: { value: '2026-07-07' } })

    await user.click(screen.getByRole('button', { name: /Indsend periode/ }))

    // The submit POST fired with the full body.
    await waitFor(() => {
      expect(findSubmitCall()).toBeDefined()
    })
    const submitCall = findSubmitCall()!
    const init = submitCall[1] as RequestInit
    const body = JSON.parse(init.body as string)

    // The DISCRIMINATING assertion: orgId is present and equals the auth-context
    // value (this is what the S82-8202 fix added; dropping it makes the test RED).
    expect(body.orgId).toBe('STY01')
    // ...alongside the rest of the SubmitPeriodRequest contract.
    expect(body.employeeId).toBe('EMP_SELF')
    expect(body.periodStart).toBe('2026-07-01')
    expect(body.periodEnd).toBe('2026-07-07')
    expect(body.periodType).toBe('WEEKLY')
    expect(body.agreementCode).toBe('AC')

    // The success banner confirms the 200 path ran end-to-end.
    await waitFor(() => {
      expect(screen.getByText('Periode indsendt.')).toBeDefined()
    })
  })

  it('blocks submit with a user-facing error when orgId is missing (the auth guard)', async () => {
    const user = userEvent.setup()
    mockFetches()
    // Simulate a logged-in employee whose auth context lacks an org (the guard
    // at MyPeriods.tsx:127 must short-circuit BEFORE any POST fires).
    mockAuth.orgId = null

    render(<MyPeriods />)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Indsend periode/ })).toBeDefined()
    })

    fireEvent.change(screen.getByLabelText('Startdato'), { target: { value: '2026-07-01' } })
    fireEvent.change(screen.getByLabelText('Slutdato'), { target: { value: '2026-07-07' } })

    await user.click(screen.getByRole('button', { name: /Indsend periode/ }))

    // The guard surfaces a Danish error and NO submit POST is issued.
    await waitFor(() => {
      expect(screen.getByText(/Kunne ikke fastslaa organisation/)).toBeDefined()
    })
    expect(findSubmitCall()).toBeUndefined()
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
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Indsend periode/ })).toBeDefined()
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
        return jsonResponse([{ ...submittedPeriod, periodId: 'p-rej-1', status: 'REJECTED', rejectionReason: 'Mangler' }])
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
