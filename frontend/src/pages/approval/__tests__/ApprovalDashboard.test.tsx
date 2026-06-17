// S49 TASK-4901. Vitest + @testing-library/react tests for the
// ApprovalDashboard page — tabbed layout with "Mine medarbejdere" and
// "Alle i omraade" views. Mirrors ReportingLineTree.test.tsx pattern:
// mock globalThis.fetch, assert DOM state.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalDashboard } from '../ApprovalDashboard'

// Mock useAuth to provide role without needing AuthProvider
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'EMP_SELF', role: 'LocalLeader' },
    role: 'LocalLeader',
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

// --- Fixtures ---

const mockMyReportPeriods = [
  {
    periodId: 'p-1',
    employeeId: 'EMP001',
    orgId: 'ORG1',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T10:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'AC',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T16:00:00Z',
  },
  {
    periodId: 'p-2',
    employeeId: 'EMP002',
    orgId: 'ORG1',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T11:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'AC',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T17:00:00Z',
  },
]

const mockAllPeriods = [
  ...mockMyReportPeriods,
  {
    periodId: 'p-3',
    employeeId: 'EMP003',
    orgId: 'ORG2',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T12:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'HK',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T18:00:00Z',
  },
]

/**
 * Helper: build a mock Response object for the given JSON body.
 * Both hooks call apiClient.get which uses fetch internally, so we
 * intercept at the globalThis.fetch level.
 */
function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/**
 * Queue the two initial fetch calls that fire on mount:
 *   1. useApprovalsByMonth   -> GET /api/approval/by-month?year=X&month=Y
 *   2. useMyReportsByMonth   -> GET /api/approval/by-month?year=X&month=Y&my-reports=true
 *
 * The order these hooks fire is deterministic (both useEffect callbacks run
 * in declaration order during the same commit phase). We use mockImplementation
 * to route by URL to avoid ordering sensitivity.
 *
 * After these two, compliance fetches may fire per-period — we queue a
 * catch-all for those.
 */
function mockInitialFetches(
  myReportData = mockMyReportPeriods,
  allData = mockAllPeriods,
) {
  mockFetch.mockImplementation(async (url: string) => {
    if (typeof url === 'string' && url.includes('my-reports=true')) {
      return jsonResponse(myReportData)
    }
    if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
      return jsonResponse(allData)
    }
    // Compliance endpoint — return empty result
    if (typeof url === 'string' && url.includes('/api/compliance/')) {
      return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
    }
    // Fallback
    return jsonResponse({})
  })
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
})

describe('ApprovalDashboard', () => {
  it('renders tab bar with two tabs', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    await waitFor(() => {
      expect(screen.getByText(/Mine medarbejdere/)).toBeDefined()
    })
    expect(screen.getByText(/Alle i omraade/)).toBeDefined()
  })

  it('"Mine medarbejdere" tab fetches with my-reports=true', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Wait for hooks to fire
    await waitFor(() => {
      const myReportsCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const url = call[0] as string
          return url.includes('my-reports=true')
        },
      )
      expect(myReportsCalls.length).toBeGreaterThanOrEqual(1)
    })
  })

  it('"Alle i omraade" tab fetches regular pending', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Wait for initial data to load (mine medarbejdere tab is default)
    await waitFor(() => {
      expect(screen.getByText(/Mine medarbejdere/)).toBeDefined()
    })

    // Click the "Alle i omraade" tab
    const alleTab = screen.getByText(/Alle i omraade/)
    fireEvent.click(alleTab)

    // Both hooks fire on mount, so /api/approval/by-month (without my-reports)
    // should have been called
    await waitFor(() => {
      const pendingCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const url = call[0] as string
          return url.includes('/api/approval/by-month') && !url.includes('my-reports')
        },
      )
      expect(pendingCalls.length).toBeGreaterThanOrEqual(1)
    })
  })

  it('shows pending periods in table', async () => {
    const user = userEvent.setup()
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Default tab is "Mine medarbejdere" — should show EMP001 and EMP002
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    expect(screen.getByText('EMP002')).toBeDefined()

    // Switch to "Alle i omraade" tab using userEvent (dispatches the full
    // pointer + mouse + click sequence that Radix Tabs requires in jsdom)
    const allTab = screen.getByRole('tab', { name: /Alle i omraade/ })
    await user.click(allTab)

    // After clicking, the "all" panel becomes active and EMP003 should
    // be visible. EMP001+EMP002 also appear in allPeriods.
    await waitFor(() => {
      expect(screen.getByText('EMP003')).toBeDefined()
    })
  })

  it('shows enforcement dialog on 428 response', async () => {
    const user = userEvent.setup()

    // Route initial fetches as usual, but intercept approve POST with 428
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        return {
          ok: false,
          status: 428,
          headers: new Headers(),
          json: async () => ({ designatedApproverId: 'MGR01' }),
          text: async () => JSON.stringify({ designatedApproverId: 'MGR01' }),
        }
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })

    render(<ApprovalDashboard />)

    // Wait for the "Mine medarbejdere" tab data to load
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })

    // Click the first "Godkend" button
    const approveButtons = screen.getAllByText('Godkend')
    await user.click(approveButtons[0])

    // Assert the enforcement confirmation dialog appears
    await waitFor(() => {
      expect(screen.getByText('Haandhaevelse aktiv')).toBeDefined()
    })
    // Verify the designated approver is shown
    expect(screen.getByText(/MGR01/)).toBeDefined()
  })

  it('re-submits with confirmFallback on dialog confirm', async () => {
    const user = userEvent.setup()

    let approveCallCount = 0
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        approveCallCount++
        if (!url.includes('confirmFallback=true')) {
          // First call: return 428
          return {
            ok: false,
            status: 428,
            headers: new Headers(),
            json: async () => ({ designatedApproverId: 'MGR01' }),
            text: async () => JSON.stringify({ designatedApproverId: 'MGR01' }),
          }
        }
        // Second call with confirmFallback: return success
        return jsonResponse({ periodId: 'p-1', status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })

    render(<ApprovalDashboard />)

    // Wait for data to load
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })

    // Click the first "Godkend" button — triggers the 428
    const approveButtons = screen.getAllByText('Godkend')
    await user.click(approveButtons[0])

    // Wait for the enforcement dialog to appear
    await waitFor(() => {
      expect(screen.getByText('Haandhaevelse aktiv')).toBeDefined()
    })

    // Click the "Godkend alligevel" button inside the dialog
    const confirmButton = screen.getByText('Godkend alligevel')
    await user.click(confirmButton)

    // Assert that fetch was called again with confirmFallback=true
    await waitFor(() => {
      const fallbackCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const u = call[0] as string
          return u.includes('confirmFallback=true')
        },
      )
      expect(fallbackCalls.length).toBeGreaterThanOrEqual(1)
    })
  })
})

// ════════════════════════════════════════════════════════════════════════════
// S77 TASK-7700 / R1 — the headline FE CROSS-FLOW test (Godkend tid).
//
// HONEST FRAMING (do NOT over-claim): this proves the UI CROSS-FLOW for a
// Leader-level (vikar-)actor — i.e. that the actor's *my-reports* read surfaces
// an away-manager's report, and that the Godkend / Afvis buttons issue the right
// HTTP calls (method + URL + payload) against the approve/reject endpoint
// contracts. It does NOT assert that a designated/vikar edge GRANTS authority —
// that authorization is the BACKEND's job and is covered server-side (S74). The
// actor here is a LocalLeader (NOT LocalHR), so the role-gated "Genåbn" (reopen)
// button stays hidden and never short-circuits the flow; REOPEN is deliberately
// NOT exercised here (proven backend-side).
//
// The dashboard READS via the real approval hook but the approve/reject
// MUTATIONS call apiClient.post directly (ApprovalDashboard.tsx:228). We mock at
// the fetch network boundary and assert the wire contract for each verb.
// ════════════════════════════════════════════════════════════════════════════

/** An away-manager's report routed to THIS leader's my-reports surface. The
    Leader-actor sees it because it's in the my-reports payload — NOT because the
    FE re-derived edge authority (the server already scoped this read). */
const awayManagerReport = {
  periodId: 'p-away-1',
  employeeId: 'EMP_AWAY_MGR',
  orgId: 'ORG1',
  periodStart: '2026-05-01',
  periodEnd: '2026-05-07',
  periodType: 'WEEKLY',
  status: 'SUBMITTED',
  submittedAt: '2026-05-08T10:00:00Z',
  approvedBy: null,
  approvedAt: null,
  rejectionReason: null,
  agreementCode: 'AC',
  okVersion: '1',
  createdAt: '2026-05-01T00:00:00Z',
  employeeApprovedAt: '2026-05-07T16:00:00Z',
}

// An ALREADY-APPROVED period in the same my-reports payload — used to prove the
// role-gated Genåbn button stays hidden for a Leader (it only renders for an
// APPROVED period AND hasMinRole(LocalHR)).
const approvedReport = {
  ...awayManagerReport,
  periodId: 'p-approved-1',
  employeeId: 'EMP_DONE',
  status: 'APPROVED',
  approvedBy: 'LEADER_X',
  approvedAt: '2026-05-09T09:00:00Z',
}

// A period that exists ONLY in the unrestricted "Alle" (by-month, no my-reports)
// read — deliberately ABSENT from the scoped my-reports payload. This makes the
// default-tab assertions DISCRIMINATING: the my-reports tab must surface
// EMP_AWAY_MGR (from the scoped read) and must NOT surface EMP_OTHER (the
// unrestricted read's record). If the dashboard wrongly fed the my-reports tab
// from the unrestricted read, EMP_AWAY_MGR would be missing AND EMP_OTHER present.
const unrelatedReport = {
  ...awayManagerReport,
  periodId: 'p-other-1',
  employeeId: 'EMP_OTHER',
  status: 'SUBMITTED',
}

describe('ApprovalDashboard — Leader vikar-actor UI cross-flow (R1)', () => {
  /** Route the two month reads to a my-reports payload that includes the
      away-manager report + an approved one; record approve/reject POSTs so the
      test can assert their method + URL + body. Returns the success Response for
      the mutations. */
  function mockCrossFlow() {
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (typeof url === 'string' && url.includes('/approve') && method === 'POST') {
        return jsonResponse({ periodId: 'p-away-1', status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('/reject') && method === 'POST') {
        return jsonResponse({ periodId: 'p-away-1', status: 'REJECTED' })
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse([awayManagerReport, approvedReport])
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        // The UNRESTRICTED "Alle" read returns a DISTINCT dataset (no away-manager
        // report) so the my-reports-tab assertions discriminate the scoped read.
        return jsonResponse([unrelatedReport])
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })
  }

  it('the my-reports surface shows the away-manager report and HIDES Genåbn for a Leader (not LocalHR)', async () => {
    mockCrossFlow()
    render(<ApprovalDashboard />)

    // The leader's my-reports read surfaced the away-manager's report.
    await waitFor(() => {
      expect(screen.getByText('EMP_AWAY_MGR')).toBeDefined()
    })
    // The already-approved row is present too...
    expect(screen.getByText('EMP_DONE')).toBeDefined()
    // DISCRIMINATING: the unrestricted "Alle" read returned EMP_OTHER, but the
    // default my-reports tab must NOT surface it — proving the visible rows come
    // from the SCOPED my-reports read, not the unrestricted by-month read (Radix
    // unmounts the inactive "Alle" tab, so EMP_OTHER is genuinely out of the DOM).
    expect(screen.queryByText('EMP_OTHER')).toBeNull()
    // ...but the reopen affordance is hidden: the actor is a LocalLeader, below
    // the LocalHR floor (ApprovalDashboard.tsx:132 gates it on hasMinRole LocalHR).
    expect(screen.queryByText('Genåbn')).toBeNull()
  })

  it('Godkend issues POST /api/approval/{id}/approve (method + URL, empty body)', async () => {
    const user = userEvent.setup()
    mockCrossFlow()
    render(<ApprovalDashboard />)

    await waitFor(() => {
      expect(screen.getByText('EMP_AWAY_MGR')).toBeDefined()
    })
    // The away-manager report (SUBMITTED) shows Godkend; the approved one does not.
    const approveButtons = screen.getAllByText('Godkend')
    expect(approveButtons).toHaveLength(1)
    await user.click(approveButtons[0])

    // Assert the WIRE contract: POST to the approve endpoint for THIS period id,
    // with no JSON body (the approve verb sends none).
    await waitFor(() => {
      const approveCall = mockFetch.mock.calls.find((call: unknown[]) => {
        const u = call[0] as string
        const init = call[1] as RequestInit | undefined
        return u === '/api/approval/p-away-1/approve' && (init?.method ?? 'GET') === 'POST'
      })
      expect(approveCall).toBeDefined()
    })
    const approveCall = mockFetch.mock.calls.find((call: unknown[]) => {
      const u = call[0] as string
      const init = call[1] as RequestInit | undefined
      return u === '/api/approval/p-away-1/approve' && (init?.method ?? 'GET') === 'POST'
    })!
    const approveInit = approveCall[1] as RequestInit
    // No confirmFallback in the happy path (the actor is on the my-reports surface).
    expect(approveCall[0]).not.toContain('confirmFallback')
    // The approve verb carries no request body.
    expect(approveInit.body).toBeUndefined()
  })

  it('Afvis issues POST /api/approval/{id}/reject with the {reason} payload', async () => {
    const user = userEvent.setup()
    mockCrossFlow()
    render(<ApprovalDashboard />)

    await waitFor(() => {
      expect(screen.getByText('EMP_AWAY_MGR')).toBeDefined()
    })
    // Open the reject dialog for the away-manager report.
    await user.click(screen.getByText('Afvis'))
    const textarea = await screen.findByPlaceholderText('Begrundelse for afvisning...')
    await user.type(textarea, 'Mangler hviletid')
    // Confirm the rejection ("Afvis periode" is both the kit Dialog title (a Radix
    // Dialog.Title, post-8203 migration) and the confirm <button> — scope to the button role).
    await user.click(screen.getByRole('button', { name: 'Afvis periode' }))

    // Assert the WIRE contract: POST to the reject endpoint for THIS period id,
    // with the reason threaded in the JSON body.
    await waitFor(() => {
      const rejectCall = mockFetch.mock.calls.find((call: unknown[]) => {
        const u = call[0] as string
        const init = call[1] as RequestInit | undefined
        return u === '/api/approval/p-away-1/reject' && (init?.method ?? 'GET') === 'POST'
      })
      expect(rejectCall).toBeDefined()
    })
    const rejectCall = mockFetch.mock.calls.find((call: unknown[]) => {
      const u = call[0] as string
      const init = call[1] as RequestInit | undefined
      return u === '/api/approval/p-away-1/reject' && (init?.method ?? 'GET') === 'POST'
    })!
    const rejectInit = rejectCall[1] as RequestInit
    expect(rejectCall[0]).not.toContain('confirmFallback')
    expect(JSON.parse(rejectInit.body as string)).toEqual({ reason: 'Mangler hviletid' })
  })
})

// S77 TASK-7700 / R5 — light a11y audit for the dashboard surface. Verifies the
// tab bar exposes tab roles with accessible names; the action buttons are
// reachable by name.
describe('ApprovalDashboard — a11y audit (R5)', () => {
  it('the tab bar exposes both tabs by role + accessible name', async () => {
    mockInitialFetches()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /Mine medarbejdere/ })).toBeDefined()
    })
    expect(screen.getByRole('tab', { name: /Alle i omraade/ })).toBeDefined()
  })

  it('the approve/reject row actions are buttons reachable by their accessible name', async () => {
    mockInitialFetches()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    // Both verbs render as buttons with text accessible names.
    expect(screen.getAllByRole('button', { name: 'Godkend' }).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByRole('button', { name: 'Afvis' }).length).toBeGreaterThanOrEqual(1)
  })
})

// ════════════════════════════════════════════════════════════════════════════
// S82 TASK-8203 / R5 — the dialog-a11y RETROFIT (replaces the old S77 follow-up
// placeholder). BOTH ApprovalDashboard dialogs (the reject dialog + the
// enforcement-confirmation dialog) were migrated from hand-rolled plain <div>s to
// the kit accessible `Dialog` (Radix: role=dialog, focus-trap, Escape, aria,
// built-in close-X). These tests assert the a11y guarantees AND are
// DISCRIMINATING: the Escape-closes-WITHOUT-firing assertions would FAIL if
// onOpenChange(false) were NOT wired to the React close handlers (the stale-state
// trap both review lenses flagged) — Escape would close visually but a subsequent
// re-render or the lingering target state would betray the leak. The
// payload-preservation assertions guard the behavior-preserving contract.
// ════════════════════════════════════════════════════════════════════════════
describe('ApprovalDashboard — dialog a11y retrofit (R5 / kit Dialog)', () => {
  /** Route the month reads + record approve/reject POSTs; the mutations succeed. */
  function mockDialogFlow() {
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (typeof url === 'string' && url.includes('/approve') && method === 'POST') {
        return jsonResponse({ periodId: 'p-1', status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('/reject') && method === 'POST') {
        return jsonResponse({ periodId: 'p-1', status: 'REJECTED' })
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })
  }

  // ── Reject dialog ──────────────────────────────────────────────────────────

  it('the reject dialog exposes role=dialog and manages focus into the panel', async () => {
    const user = userEvent.setup()
    mockDialogFlow()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })

    // No dialog before it's opened.
    expect(screen.queryByRole('dialog')).toBeNull()

    await user.click(screen.getAllByRole('button', { name: 'Afvis' })[0])

    // role=dialog present (Radix) — the plain-div had none.
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeDefined()
    // The reject-reason textarea is present (E2E selector parity: same placeholder).
    const textarea = screen.getByPlaceholderText('Begrundelse for afvisning...')
    expect(textarea).toBeDefined()
    // Focus is managed INTO the panel (Radix focus-trap): the active element is the
    // textarea (autoFocus + first focusable), i.e. focus is no longer on the trigger
    // button outside the dialog.
    expect(dialog.contains(document.activeElement)).toBe(true)
  })

  it('the reject confirm stays disabled until a non-empty TRIMMED reason', async () => {
    const user = userEvent.setup()
    mockDialogFlow()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByRole('button', { name: 'Afvis' })[0])

    const confirm = await screen.findByRole('button', { name: 'Afvis periode' })
    // Disabled with no reason.
    expect((confirm as HTMLButtonElement).disabled).toBe(true)

    // Whitespace-only reason → still disabled (trim()-gated).
    const textarea = screen.getByPlaceholderText('Begrundelse for afvisning...')
    await user.type(textarea, '   ')
    expect((confirm as HTMLButtonElement).disabled).toBe(true)

    // A real reason enables it.
    await user.type(textarea, 'Mangler hviletid')
    expect((confirm as HTMLButtonElement).disabled).toBe(false)
  })

  it('Escape CLOSES the reject dialog WITHOUT firing a reject POST', async () => {
    const user = userEvent.setup()
    mockDialogFlow()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByRole('button', { name: 'Afvis' })[0])

    // Type a reason so an accidental submit WOULD fire (discriminating: if Escape
    // wrongly triggered the confirm, the reject POST would go out).
    const textarea = await screen.findByPlaceholderText('Begrundelse for afvisning...')
    await user.type(textarea, 'Skulle ikke afvises')

    await user.keyboard('{Escape}')

    // The dialog is gone (React state cleared, not just a visual close).
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull()
    })
    // DISCRIMINATING: no reject POST fired.
    const rejectCalls = mockFetch.mock.calls.filter((call: unknown[]) => {
      const u = call[0] as string
      const init = call[1] as RequestInit | undefined
      return typeof u === 'string' && u.includes('/reject') && (init?.method ?? 'GET') === 'POST'
    })
    expect(rejectCalls).toHaveLength(0)

    // And the React state was truly cleared: re-opening starts with an EMPTY reason
    // (closeRejectDialog resets rejectReason). This fails if onOpenChange just hid
    // the panel visually without routing to the state-close handler.
    await user.click(screen.getAllByRole('button', { name: 'Afvis' })[0])
    const reopened = await screen.findByPlaceholderText('Begrundelse for afvisning...')
    expect((reopened as HTMLTextAreaElement).value).toBe('')
  })

  it('the reject confirm still POSTs the SAME {reason} payload after migration', async () => {
    const user = userEvent.setup()
    mockDialogFlow()
    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByRole('button', { name: 'Afvis' })[0])

    const textarea = await screen.findByPlaceholderText('Begrundelse for afvisning...')
    await user.type(textarea, 'Mangler hviletid')
    await user.click(screen.getByRole('button', { name: 'Afvis periode' }))

    await waitFor(() => {
      const rejectCall = mockFetch.mock.calls.find((call: unknown[]) => {
        const u = call[0] as string
        const init = call[1] as RequestInit | undefined
        return u === '/api/approval/p-1/reject' && (init?.method ?? 'GET') === 'POST'
      })
      expect(rejectCall).toBeDefined()
    })
    const rejectCall = mockFetch.mock.calls.find((call: unknown[]) => {
      const u = call[0] as string
      const init = call[1] as RequestInit | undefined
      return u === '/api/approval/p-1/reject' && (init?.method ?? 'GET') === 'POST'
    })!
    const rejectInit = rejectCall[1] as RequestInit
    expect(rejectCall[0]).not.toContain('confirmFallback')
    expect(JSON.parse(rejectInit.body as string)).toEqual({ reason: 'Mangler hviletid' })
  })

  // ── Enforcement-confirmation dialog ──────────────────────────────────────────

  it('the enforcement dialog (428) exposes role=dialog', async () => {
    const user = userEvent.setup()
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        return {
          ok: false,
          status: 428,
          headers: new Headers(),
          json: async () => ({ designatedApproverId: 'MGR01' }),
          text: async () => JSON.stringify({ designatedApproverId: 'MGR01' }),
        }
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })

    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByText('Godkend')[0])

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeDefined()
    expect(screen.getByText('Haandhaevelse aktiv')).toBeDefined()
    expect(screen.getByText(/MGR01/)).toBeDefined()
  })

  it('Escape CLOSES the enforcement dialog WITHOUT firing the confirm (no confirmFallback POST)', async () => {
    const user = userEvent.setup()
    let approveCallCount = 0
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        approveCallCount++
        if (!url.includes('confirmFallback=true')) {
          return {
            ok: false,
            status: 428,
            headers: new Headers(),
            json: async () => ({ designatedApproverId: 'MGR01' }),
            text: async () => JSON.stringify({ designatedApproverId: 'MGR01' }),
          }
        }
        return jsonResponse({ periodId: 'p-1', status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })

    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByText('Godkend')[0])

    // The enforcement dialog opened (the first approve fired + got the 428).
    await screen.findByRole('dialog')
    expect(approveCallCount).toBe(1)

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull()
    })
    // DISCRIMINATING: the confirm never fired, so NO confirmFallback POST went out —
    // and no further approve call happened at all (count still 1).
    expect(approveCallCount).toBe(1)
    const fallbackCalls = mockFetch.mock.calls.filter((call: unknown[]) => {
      const u = call[0] as string
      return typeof u === 'string' && u.includes('confirmFallback=true')
    })
    expect(fallbackCalls).toHaveLength(0)
  })

  it('the enforcement confirm still POSTs with confirmFallback=true after migration', async () => {
    const user = userEvent.setup()
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        if (!url.includes('confirmFallback=true')) {
          return {
            ok: false,
            status: 428,
            headers: new Headers(),
            json: async () => ({ designatedApproverId: 'MGR01' }),
            text: async () => JSON.stringify({ designatedApproverId: 'MGR01' }),
          }
        }
        return jsonResponse({ periodId: 'p-1', status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('my-reports=true')) {
        return jsonResponse(mockMyReportPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/approval/by-month')) {
        return jsonResponse(mockAllPeriods)
      }
      if (typeof url === 'string' && url.includes('/api/compliance/')) {
        return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
      }
      return jsonResponse({})
    })

    render(<ApprovalDashboard />)
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    await user.click(screen.getAllByText('Godkend')[0])
    await screen.findByRole('dialog')

    await user.click(screen.getByRole('button', { name: 'Godkend alligevel' }))

    await waitFor(() => {
      const fallbackCalls = mockFetch.mock.calls.filter((call: unknown[]) => {
        const u = call[0] as string
        return typeof u === 'string' && u.includes('confirmFallback=true')
      })
      expect(fallbackCalls.length).toBeGreaterThanOrEqual(1)
    })
  })
})
