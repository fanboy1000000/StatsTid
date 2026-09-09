// SPRINT-140 / TASK-14006 — the HR follow-up landing page's routing pin,
// mirroring `App.overtimeRoute.test.tsx` (QUAL-162): a LocalHR user reaches
// `/admin/opfoelgning`; a LocalLeader (below the LocalHR floor) gets the
// `RequireRole` guard's 403 `ForbiddenPage` card — NOT a redirect
// (`RequireRole.tsx:13-14`) — and the "Opfølgning" sidebar entry never
// renders for them either (`Sidebar.tsx`'s own `minRole` filter).
import type { ReactNode } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { App } from '../App'

const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../contexts/AuthContext', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => children,
  useAuth: () => ({
    role: auth.role,
    isAuthenticated: true,
    user: { employeeId: 'TESTUSER', role: auth.role },
    orgId: null,
    agreementCode: null,
    scopes: [],
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

beforeEach(() => {
  auth.role = 'LocalHR'
  mockFetch.mockReset()
  // Every GET this page fires resolves to an empty, well-formed 200 so the
  // page settles without a load-error state; this test cares about ROUTING,
  // not tile content (covered by OpfoelgningPage.test.tsx).
  mockFetch.mockImplementation(async (url: string) => {
    const empty = url.includes('transfer-agreements-needed')
      ? { items: [], count: 0, entitlementYear: 2025, windowOpen: false, windowOpensOn: '2025-11-01', deadline: '2025-12-31', daysToDeadline: 0, cannotCompute: [], cannotComputeCount: 0, today: '2025-10-15', projectionNote: '' }
      : { items: [], count: 0, today: '2025-10-15' }
    return {
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => empty,
      text: async () => JSON.stringify(empty),
    }
  })
})

describe('the HR follow-up landing page route (TASK-14006)', () => {
  it('resolves for a LocalHR user at /admin/opfoelgning', async () => {
    auth.role = 'LocalHR'
    window.history.pushState({}, '', '/admin/opfoelgning')
    render(<App />)

    // Both the page heading AND the sidebar entry render (the entry is present
    // for a role that clears the LocalHR floor) — the lazy page chunk can
    // resolve after the sidebar's synchronous render, so wait for both.
    await waitFor(() => expect(screen.getAllByText('Opfølgning').length).toBe(2))
    expect(screen.queryByText('403')).toBeNull()
  })

  it('is refused for a LocalLeader (the RequireRole 403 card, not a redirect) with no sidebar entry', async () => {
    auth.role = 'LocalLeader'
    window.history.pushState({}, '', '/admin/opfoelgning')
    render(<App />)

    await waitFor(() => expect(screen.getByText('403')).toBeInTheDocument())
    expect(screen.getByText('Adgang naegtet')).toBeInTheDocument()
    // Still on the same path — a guard renders a card in place, it does not navigate away.
    expect(window.location.pathname).toBe('/admin/opfoelgning')
    // No sidebar entry at all for a role below the LocalHR floor.
    expect(screen.queryByText('Opfølgning')).toBeNull()
  })
})
