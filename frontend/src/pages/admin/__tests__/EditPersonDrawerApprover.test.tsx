// S76b / TASK-7603 — drawer-level integration test for the create-mode approver:
// the draft approver chosen via the PersonPickerDialog is threaded into the SAME
// `POST /api/admin/users` body (the S74 R9 atomic create+assign), and NO separate
// reporting-line POST is made at create time. Uses the REAL hooks + a fetch router
// (the picker's server-search + the create POST both flow through fetch).
import type { ComponentProps } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { EditPersonDrawer } from '../EditPersonDrawer'
import type { Organization } from '../../../hooks/useAdmin'

let mockRole = 'LocalAdmin'
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'ADMIN1', role: mockRole },
    role: mockRole,
    orgId: 'ORG1',
    agreementCode: 'AC',
    scopes: [],
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => {
    mockStorage[key] = val
  },
  removeItem: (key: string) => {
    delete mockStorage[key]
  },
})

const organizations: Organization[] = [
  { orgId: 'ORG1', orgName: 'Test Org', orgType: 'DEPARTMENT', parentOrgId: null, agreementCode: 'AC' },
]

function ok(json: unknown, etag?: string): Response {
  return {
    ok: true,
    status: 200,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  } as unknown as Response
}

function setupRouter() {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    if (url.includes('/api/admin/users/search')) {
      return ok({
        items: [
          { userId: 'MGR9', displayName: 'Mette Holm', primaryOrgName: 'Direktion', enhedLabel: null },
        ],
        total: 1,
        limit: 60,
        offset: 0,
      })
    }
    if (url.includes('/api/admin/users') && method === 'POST') {
      return ok({ userId: 'EMP010', username: 'emp010', displayName: 'Ny Bruger', email: null, primaryOrgId: 'ORG1', agreementCode: 'AC', version: 1 }, '"1"')
    }
    return ok({}, '"1"')
  })
}

function renderDrawer(props: Partial<ComponentProps<typeof EditPersonDrawer>> = {}) {
  return render(
    <ToastProvider>
      <EditPersonDrawer
        open
        organizations={organizations}
        defaultOrgId="ORG1"
        onClose={props.onClose ?? vi.fn()}
        onSaved={props.onSaved}
        user={props.user}
      />
    </ToastProvider>,
  )
}

beforeEach(() => {
  mockFetch.mockReset()
  mockRole = 'LocalAdmin'
  setupRouter()
})

function collect(substring: string) {
  const calls: Array<{ method: string; body: unknown }> = []
  const inner = mockFetch.getMockImplementation()!
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes(substring)) {
      calls.push({
        method: init?.method ?? 'GET',
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      })
    }
    return inner(url, init)
  })
  return calls
}

describe('EditPersonDrawer — create-mode approver threading', () => {
  it('threads the picked approverId into the create POST (atomic create+assign), no separate reporting-line POST', async () => {
    const reportingLineCalls = collect('/api/admin/reporting-lines')
    const userCalls = collect('/api/admin/users')
    const onSaved = vi.fn()
    renderDrawer({ onSaved })

    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })

    fireEvent.change(screen.getByTestId('ep-create-user-id'), { target: { value: 'EMP010' } })
    fireEvent.change(screen.getByTestId('ep-create-username'), { target: { value: 'emp010' } })
    fireEvent.change(screen.getByTestId('ep-create-password'), { target: { value: 'pw' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ny Bruger' } })

    // Pick the approver via the create-mode picker (sets the draft, no API call yet).
    fireEvent.click(screen.getByTestId('approver-assign'))
    await waitFor(() => expect(screen.getByTestId('picker-row-MGR9')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-MGR9'))
    await waitFor(() => expect(screen.getByTestId('approver-assigned').textContent).toContain('Mette Holm'))

    fireEvent.click(screen.getByText('Opret medarbejder', { selector: 'button[type="submit"]' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalled())

    const post = userCalls.find((c) => c.method === 'POST')
    expect(post).toBeDefined()
    expect((post!.body as { approverId?: string }).approverId).toBe('MGR9')
    // No standalone reporting-line POST/DELETE at create (the create tx does it).
    expect(reportingLineCalls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('create without an approver omits approverId', async () => {
    const userCalls = collect('/api/admin/users')
    const onSaved = vi.fn()
    renderDrawer({ onSaved })
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })
    fireEvent.change(screen.getByTestId('ep-create-user-id'), { target: { value: 'EMP011' } })
    fireEvent.change(screen.getByTestId('ep-create-username'), { target: { value: 'emp011' } })
    fireEvent.change(screen.getByTestId('ep-create-password'), { target: { value: 'pw' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ingen Leder' } })
    fireEvent.click(screen.getByText('Opret medarbejder', { selector: 'button[type="submit"]' }))
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    const post = userCalls.find((c) => c.method === 'POST')
    expect((post!.body as { approverId?: string }).approverId).toBeUndefined()
  })
})
