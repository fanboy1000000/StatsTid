// SPRINT-140 / TASK-14007 (QUAL-162) — the overtime pre-approval page was
// finished and unit-tested (`OvertimePreApprovalManagement.test.tsx`) but never
// routed: no role could reach it (`docs/FRONTEND.md:185`'s "not routed" list).
// This is the FIRST routing-level test in the repo (no `RequireRole`/App route
// test existed before), written specifically to prove the new wiring: a leader
// reaches the page at `/godkend/overtid`; an employee gets the `RequireRole`
// guard's 403 `ForbiddenPage` card (NOT a redirect — `RequireRole.tsx:13-14`).
//
// `useAuth` is mocked at the module boundary (the same technique the admin
// drawer suites use) rather than driven through a real JWT, so the role is a
// single controlled variable; `window.history.pushState` sets the initial
// location before `<App/>` mounts its own internal `BrowserRouter` (App.tsx
// does not expose a way to inject a router, unlike a testable route table).
import type { ReactNode } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { App } from '../App'

const auth = vi.hoisted(() => ({ role: 'LocalLeader' as string | null }))
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

beforeEach(() => {
  auth.role = 'LocalLeader'
})

describe('the overtime pre-approval route (QUAL-162)', () => {
  it('resolves for a leader at /godkend/overtid', async () => {
    auth.role = 'LocalLeader'
    window.history.pushState({}, '', '/godkend/overtid')
    render(<App />)

    await waitFor(() => expect(screen.getByText('Overtidsgodkendelse')).toBeInTheDocument())
    expect(screen.queryByText('403')).toBeNull()
  })

  it('is refused for an Employee (the RequireRole 403 card, not a redirect)', async () => {
    auth.role = 'Employee'
    window.history.pushState({}, '', '/godkend/overtid')
    render(<App />)

    await waitFor(() => expect(screen.getByText('403')).toBeInTheDocument())
    expect(screen.getByText('Adgang naegtet')).toBeInTheDocument()
    // Still on the same path — a guard renders a card in place, it does not navigate away.
    expect(window.location.pathname).toBe('/godkend/overtid')
    expect(screen.queryByText('Overtidsgodkendelse')).toBeNull()
  })
})
