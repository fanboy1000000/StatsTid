// S116 / TASK-11602 — the REPAIRED OvertimePreApprovalManagement page.
//
// Before this sprint the list read called `GET /api/overtime/pre-approvals`,
// a route that did NOT exist — every page load 404'd (the S116 L3 pre-existing
// defect). TASK-11601 created the endpoint (typed from birth, scope-bounded,
// 11-field element incl. non-null `employeeName`); the page now rides the
// typed form. The list-read pin below is therefore the page's FIRST
// working-read proof: exact NEW route + rows rendered from a stubbed 11-field
// response. The two PUT pins verify the typed no-body call forms.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OvertimePreApprovalManagement } from '../OvertimePreApprovalManagement'

// The page consumes useToast; render it standalone by mocking the module.
const toastSpy = vi.fn()
vi.mock('../../../components/ui/Toast', () => ({
  useToast: () => ({ toast: toastSpy }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})
const mockReload = vi.fn()
Object.defineProperty(window, 'location', { value: { reload: mockReload }, writable: true })

/** A full 11-field spec element (OvertimePreApprovalAdminListItem). */
function listItem(over: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'pa-1',
    employeeId: 'EMP001',
    periodStart: '2026-07-01',
    periodEnd: '2026-07-05',
    maxHours: 10,
    approvedBy: null,
    approvedAt: null,
    status: 'PENDING',
    reason: 'Spidsbelastning',
    createdAt: '2026-06-28T09:00:00Z',
    employeeName: 'Anna Berg',
    ...over,
  }
}

const rows = [
  listItem(),
  listItem({
    id: 'pa-2',
    employeeId: 'EMP002',
    employeeName: 'Bo Dahl',
    status: 'APPROVED',
    approvedBy: 'MGR01',
    approvedAt: '2026-06-29T10:00:00Z',
    reason: null,
    maxHours: 4.5,
  }),
]

type Captured = { url: string; method: string; body: unknown }

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/** Capture calls; GET list → `rows`, PUTs → a minimal action response. */
function captureCalls(listRows: unknown[] = rows) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    })
    if ((init?.method ?? 'GET') === 'GET') return jsonResponse(listRows)
    return jsonResponse({ id: 'pa-1', status: 'APPROVED', approvedBy: 'MGR01', reason: null })
  })
  return calls
}

beforeEach(() => {
  mockFetch.mockReset()
  toastSpy.mockReset()
})

describe('OvertimePreApprovalManagement — the repaired list read (S116 L3)', () => {
  it('reads the NEW admin list route (exact URL) and renders rows from the 11-field response', async () => {
    const calls = captureCalls()
    render(<OvertimePreApprovalManagement />)
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // The exact NEW route — NOT the per-employee /{employeeId}/pre-approvals one.
    expect(calls[0].url).toBe('/api/overtime/pre-approvals')
    expect(calls[0].method).toBe('GET')
    // Rendered from the spec shape: non-null employeeName, hours, badges, reason.
    expect(screen.getByText('Bo Dahl')).toBeInTheDocument()
    expect(screen.getByText('10.0 t')).toBeInTheDocument()
    expect(screen.getByText('4.5 t')).toBeInTheDocument()
    expect(screen.getByText('Spidsbelastning')).toBeInTheDocument()
    expect(screen.getByText('Afventer')).toBeInTheDocument()
    expect(screen.getByText('Godkendt')).toBeInTheDocument()
  })

  it('renders the empty state from an empty (non-404) list', async () => {
    captureCalls([])
    render(<OvertimePreApprovalManagement />)
    await waitFor(() =>
      expect(screen.getByText('Ingen ventende godkendelser')).toBeInTheDocument(),
    )
  })
})

describe('OvertimePreApprovalManagement — the typed PUT pair', () => {
  it('Godkend → PUT /api/overtime/pre-approval/{id}/approve with NO body, then refetches', async () => {
    const user = userEvent.setup()
    const calls = captureCalls()
    render(<OvertimePreApprovalManagement />)
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // Only the PENDING row has actions.
    const pendingRow = screen.getByText('Anna Berg').closest('tr')!
    await user.click(within(pendingRow as HTMLElement).getByRole('button', { name: 'Godkend' }))
    await waitFor(() => {
      const put = calls.find(c => c.method === 'PUT')
      expect(put?.url).toBe('/api/overtime/pre-approval/pa-1/approve')
      expect(put?.body).toBeUndefined()
    })
    // The list refetched after the action (GET, PUT, GET).
    await waitFor(() => expect(calls.filter(c => c.method === 'GET').length).toBe(2))
    expect(toastSpy).toHaveBeenCalled()
  })

  it('Afvis → PUT /api/overtime/pre-approval/{id}/reject with NO body', async () => {
    const user = userEvent.setup()
    const calls = captureCalls()
    render(<OvertimePreApprovalManagement />)
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    const pendingRow = screen.getByText('Anna Berg').closest('tr')!
    await user.click(within(pendingRow as HTMLElement).getByRole('button', { name: 'Afvis' }))
    await waitFor(() => {
      const put = calls.find(c => c.method === 'PUT')
      expect(put?.url).toBe('/api/overtime/pre-approval/pa-1/reject')
      expect(put?.body).toBeUndefined()
    })
  })
})
