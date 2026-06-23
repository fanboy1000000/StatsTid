// S88 / TASK-8802 — vitest + @testing-library/react tests for the leader
// Teamoversigt EXPANDABLE DETAIL ROW (the accordion + the TeamRowDetail panel).
//
// Coverage: accordion expand/collapse (opening one closes another; chevron
// aria-expanded flips); the checkbox + Handling cells do NOT toggle the row
// (stopPropagation); the lazy breakdown + compliance fetches fire only on
// expand (loading → data; error states for BOTH); the over-allocation imbalance
// case (underAllocated=0, overAllocated>0, hasAllocationImbalance=true) renders
// the "Ikke fordelt" amber + the Overfordeling alert; the Merarbejde(AC) /
// Overarbejde(non-AC) label switch; compliance fault-isolation (error → soft
// message, rest renders); the footer REUSES the parent handlers (a 409 surfaces
// the conflict/refetch); Escape collapses + returns focus to the toggle; the
// rejection-reason display.
//
// PAT-007: the useAuth mock returns a referentially-stable object. fetch is
// mocked at the network boundary; the breakdown + compliance endpoints route by
// URL.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TeamOversigt } from '../TeamOversigt'

// ── Auth mock (PAT-007: stable role; flip via the module-level holder) ───────
const authState = { role: 'LocalLeader' as string }
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'MGR03', role: authState.role },
    role: authState.role,
    orgId: 'STY01',
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
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})
const mockReload = vi.fn()
Object.defineProperty(window, 'location', { value: { reload: mockReload }, writable: true })

// ── Fixtures ─────────────────────────────────────────────────────────────────
function row(over: Partial<Record<string, unknown>> = {}) {
  return {
    periodId: 'p-1',
    employeeId: 'emp001',
    displayName: 'Anna Berg',
    agreement: 'AC',
    status: 'SUBMITTED',
    submittedAt: '2026-03-29T10:00:00Z',
    decisionAt: null,
    rejectionReason: null,
    normExpected: 147,
    normRegistered: 140,
    flexBalance: 3.5,
    overtime: 2,
    ferieUsed: 5,
    ferieTotal: 25,
    awayToday: false,
    hasWarning: false,
    payrollExported: false,
    payrollExportedAt: null,
    ...over,
  }
}

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

function errResponse(status: number) {
  return {
    ok: false,
    status,
    headers: new Headers(),
    json: async () => ({ error: 'fail' }),
    text: async () => 'fail',
  }
}

const cleanBreakdown = {
  allocations: [
    { taskId: 'Projekt Alfa', hours: 90 },
    { taskId: 'Projekt Beta', hours: 50 },
  ],
  worked: 140,
  allocated: 140,
  underAllocated: 0,
  overAllocated: 0,
  hasAllocationImbalance: false,
}

const cleanCompliance = {
  ruleId: 'WT',
  employeeId: 'emp001',
  success: true,
  violations: [],
  warnings: [],
}

interface Routes {
  overview?: unknown[]
  breakdown?: unknown
  breakdownStatus?: number
  compliance?: unknown
  complianceStatus?: number
  /** Optional spy/override for /approve POSTs. */
  onApprove?: (url: string) => unknown
}

/** Wires fetch: team-overview, allocation-breakdown, compliance + a default. */
function mockRoutes(opts: Routes = {}) {
  const team = opts.overview ?? [row()]
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (typeof url !== 'string') return jsonResponse({})
    if (url.includes('/api/approval/team-overview')) {
      return jsonResponse({ employees: team })
    }
    if (url.includes('/allocation-breakdown')) {
      if (opts.breakdownStatus && opts.breakdownStatus >= 400) return errResponse(opts.breakdownStatus)
      return jsonResponse(opts.breakdown ?? cleanBreakdown)
    }
    if (url.includes('/api/compliance/')) {
      if (opts.complianceStatus && opts.complianceStatus >= 400) return errResponse(opts.complianceStatus)
      return jsonResponse(opts.compliance ?? cleanCompliance)
    }
    if (url.includes('/approve') && init?.method === 'POST') {
      return (opts.onApprove?.(url) as ReturnType<typeof jsonResponse>) ?? jsonResponse({ status: 'APPROVED' })
    }
    return jsonResponse({})
  })
}

function renderPage() {
  return render(
    <MemoryRouter>
      <TeamOversigt />
    </MemoryRouter>,
  )
}

async function expandFirstRow(user: ReturnType<typeof userEvent.setup>, name = 'Anna Berg') {
  await waitFor(() => expect(screen.getByText(name)).toBeInTheDocument())
  const toggle = screen.getByRole('button', { name: new RegExp(`detaljer for ${name}`) })
  await user.click(toggle)
  return toggle
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  authState.role = 'LocalLeader'
})

describe('TeamRowDetail — accordion expand/collapse', () => {
  it('the toggle is a real button with aria-expanded that flips on click', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    const toggle = await expandFirstRow(user)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(toggle).toHaveAttribute('aria-controls', 'team-detail-emp001')
    // The detail row is rendered.
    expect(screen.getByTestId('team-detail-row-emp001')).toBeInTheDocument()
    // Collapse again.
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByTestId('team-detail-row-emp001')).toBeNull()
  })

  it('opening one row collapses another (accordion)', async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row(), row({ employeeId: 'emp002', displayName: 'Bo Dahl' })] })
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: /detaljer for Anna Berg/ }))
    expect(screen.getByTestId('team-detail-row-emp001')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /detaljer for Bo Dahl/ }))
    // emp002 open, emp001 closed.
    expect(screen.getByTestId('team-detail-row-emp002')).toBeInTheDocument()
    expect(screen.queryByTestId('team-detail-row-emp001')).toBeNull()
  })

  it('clicking the row BODY toggles expansion', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // Click a non-interactive cell (the agreement cell text).
    await user.click(screen.getByText('AC', { selector: 'td' }))
    expect(screen.getByTestId('team-detail-row-emp001')).toBeInTheDocument()
  })

  it('Escape collapses the open row and returns focus to its toggle', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    const toggle = await expandFirstRow(user)
    expect(screen.getByTestId('team-detail-row-emp001')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByTestId('team-detail-row-emp001')).toBeNull())
    expect(toggle).toHaveFocus()
  })
})

describe('TeamRowDetail — stopPropagation on checkbox + Handling cells', () => {
  it('toggling the checkbox does NOT expand the row', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('checkbox', { name: 'Vælg Anna Berg' }))
    expect(screen.queryByTestId('team-detail-row-emp001')).toBeNull()
  })

  it('clicking a Handling-cell button does NOT expand the row', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // The row-level "Afvis" opens the reject dialog (parent), must not expand.
    await user.click(screen.getByRole('button', { name: 'Afvis' }))
    expect(screen.queryByTestId('team-detail-row-emp001')).toBeNull()
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
  })
})

describe('TeamRowDetail — lazy fetch (breakdown + compliance)', () => {
  it('does NOT fetch breakdown/compliance until a row is expanded', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    const before = mockFetch.mock.calls.filter((c: unknown[]) =>
      typeof c[0] === 'string' && ((c[0] as string).includes('/allocation-breakdown') || (c[0] as string).includes('/api/compliance/')))
    expect(before).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: /detaljer for Anna Berg/ }))
    await waitFor(() => {
      const after = mockFetch.mock.calls.filter((c: unknown[]) =>
        typeof c[0] === 'string' && (c[0] as string).includes('/allocation-breakdown'))
      expect(after.length).toBeGreaterThanOrEqual(1)
    })
    expect(mockFetch.mock.calls.some((c: unknown[]) =>
      typeof c[0] === 'string' && (c[0] as string).includes('/api/compliance/emp001/period'))).toBe(true)
  })

  it('renders the breakdown bars + sum once loaded', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText('Projekt Alfa')).toBeInTheDocument())
    expect(screen.getByText('Projekt Beta')).toBeInTheDocument()
    // Header sum {allocated} / {worked} t.
    expect(screen.getByText('140,0 / 140,0 t')).toBeInTheDocument()
  })

  it('a failed breakdown fetch → soft "Kunne ikke hente fordeling"; the rest still renders', async () => {
    const user = userEvent.setup()
    mockRoutes({ breakdownStatus: 500 })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText('Kunne ikke hente fordeling')).toBeInTheDocument())
    // Saldi (from row data) still renders.
    expect(screen.getByText('Saldi')).toBeInTheDocument()
    expect(screen.getByText('Normtimer')).toBeInTheDocument()
  })
})

describe('TeamRowDetail — Saldi (row figures, no extra fetch)', () => {
  it('renders the 4 Saldi cells with the Merarbejde label for AC', async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row({ agreement: 'AC' })] })
    renderPage()
    await expandFirstRow(user)
    expect(screen.getByText('Flex saldo')).toBeInTheDocument()
    expect(screen.getByText('Merarbejde')).toBeInTheDocument()
    expect(screen.queryByText('Overarbejde')).toBeNull()
  })

  it('renders the Overarbejde label for a non-AC agreement', async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row({ agreement: 'HK' })] })
    renderPage()
    await expandFirstRow(user)
    expect(screen.getByText('Overarbejde')).toBeInTheDocument()
    expect(screen.queryByText('Merarbejde')).toBeNull()
  })
})

describe('TeamRowDetail — imbalance UI (per-day contract, B1)', () => {
  it('the OVER-allocation case (underAllocated=0, hasAllocationImbalance=true) renders amber + the Overfordeling alert, NOT a clean panel', async () => {
    const user = userEvent.setup()
    mockRoutes({
      overview: [row({ hasWarning: true })],
      breakdown: {
        allocations: [{ taskId: 'Projekt Alfa', hours: 150 }],
        worked: 140,
        allocated: 150,
        underAllocated: 0,
        overAllocated: 10,
        hasAllocationImbalance: true,
      },
    })
    renderPage()
    await expandFirstRow(user)
    // The Overfordeling alert (NOT a clean panel) appears.
    await waitFor(() =>
      expect(screen.getByText(/er fordelt på projekter ud over den registrerede tid/)).toBeInTheDocument())
    // The Manglende-fordeling alert is NOT shown (underAllocated == 0).
    expect(screen.queryByText(/skal fordele hele sin registrerede tid/)).toBeNull()
    // The "Ikke fordelt" entry exists and is the amber/imbalance variant.
    const ikkeFordelt = screen.getByText('Ikke fordelt')
    expect(ikkeFordelt.closest('.allocImbalance')).not.toBeNull()
  })

  it('the under-allocation case renders the Manglende fordeling alert', async () => {
    const user = userEvent.setup()
    mockRoutes({
      overview: [row({ hasWarning: true })],
      breakdown: {
        allocations: [{ taskId: 'Projekt Alfa', hours: 120 }],
        worked: 140,
        allocated: 120,
        underAllocated: 20,
        overAllocated: 0,
        hasAllocationImbalance: true,
      },
    })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() =>
      expect(screen.getByText(/er ikke fordelt på projekter/)).toBeInTheDocument())
  })

  it('a clean fully-allocated month shows NO allocation alerts and a muted "Ikke fordelt"', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText('Ikke fordelt')).toBeInTheDocument())
    expect(screen.queryByText(/er ikke fordelt på projekter/)).toBeNull()
    expect(screen.queryByText(/ud over den registrerede tid/)).toBeNull()
    expect(screen.getByText('Ikke fordelt').closest('.allocImbalance')).toBeNull()
  })
})

describe('TeamRowDetail — compliance Advarsel + fault isolation', () => {
  it('renders each compliance warning/violation as an Advarsel', async () => {
    const user = userEvent.setup()
    mockRoutes({
      compliance: {
        ruleId: 'WT', employeeId: 'emp001', success: false, violations: [],
        warnings: [{ violationType: 'DAILY_REST', date: '2026-03-10', actualValue: 9, thresholdValue: 11, severity: 'WARNING', isVoluntaryExempt: false, message: 'For kort hviletid den 10.' }],
      },
    })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText(/For kort hviletid den 10\./)).toBeInTheDocument())
  })

  it('a failed compliance fetch → soft "Advarsler kunne ikke hentes"; the rest still renders', async () => {
    const user = userEvent.setup()
    mockRoutes({ complianceStatus: 503 })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText('Advarsler kunne ikke hentes')).toBeInTheDocument())
    // Saldi + breakdown still render (fault isolated to the Advarsel arm).
    expect(screen.getByText('Saldi')).toBeInTheDocument()
    expect(screen.getByText('Projekt Alfa')).toBeInTheDocument()
  })
})

describe('TeamRowDetail — rejection reason', () => {
  it('shows "Begrundelse for afvisning" for a REJECTED row with a reason', async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row({ status: 'REJECTED', decisionAt: '2026-04-01T08:00:00Z', rejectionReason: 'Mangler fordeling' })] })
    renderPage()
    await expandFirstRow(user)
    expect(screen.getByText('Begrundelse for afvisning:')).toBeInTheDocument()
    expect(screen.getByText(/Mangler fordeling/)).toBeInTheDocument()
  })
})

describe('TeamRowDetail — footer reuses the parent handlers (no second save path)', () => {
  it('"Godkend måned" goes through the status-aware path: a 200 approves single-shot (no dialog)', async () => {
    const user = userEvent.setup()
    const approveCalls: string[] = []
    mockRoutes({
      onApprove: (url: string) => {
        approveCalls.push(url)
        return jsonResponse({ status: 'APPROVED' })
      },
    })
    renderPage()
    await expandFirstRow(user)
    // The detail footer's "Godkend måned" button reuses the parent handleApprove.
    await user.click(await screen.findByRole('button', { name: 'Godkend måned' }))
    // A single-shot approve POST fires (no confirm dialog, no second request).
    await waitFor(() => expect(approveCalls.length).toBe(1))
    expect(approveCalls[0]).toContain('/api/approval/p-1/approve')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('"Godkend måned" surfaces a 409 lost-race (refetch path), not a silent drop', async () => {
    const user = userEvent.setup()
    mockRoutes({
      onApprove: () => ({
        ok: false, status: 409, headers: new Headers(),
        json: async () => ({ error: 'conflict' }),
        text: async () => JSON.stringify({ error: 'conflict' }),
      }),
    })
    renderPage()
    await expandFirstRow(user)
    await user.click(await screen.findByRole('button', { name: 'Godkend måned' }))
    // The parent handleApprove surfaces the 409 via a toast ("ændret af en anden").
    await waitFor(() => expect(screen.getByText(/ændret af en anden/)).toBeInTheDocument())
  })

  it('"Afvis måned" in the footer opens the PARENT reject dialog (not a re-implemented mutation)', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await user.click(await screen.findByRole('button', { name: 'Afvis måned' }))
    const dialog = await screen.findByRole('dialog')
    // The parent reject dialog (kit Radix Dialog) — confirm button + textarea.
    expect(within(dialog).getByRole('button', { name: 'Afvis måned' })).toBeInTheDocument()
    expect(within(dialog).getByPlaceholderText('Skriv en kort begrundelse til medarbejderen…')).toBeInTheDocument()
  })

  it('a Leader SEES "Genåbn måned" in the footer of a decided row (S89 Phase 1; was LocalHR+)', async () => {
    const user = userEvent.setup()
    authState.role = 'LocalLeader'
    mockRoutes({ overview: [row({ status: 'APPROVED', decisionAt: '2026-04-01T08:00:00Z' })] })
    renderPage()
    await expandFirstRow(user)
    expect(screen.getByTestId('team-detail-row-emp001')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Genåbn måned' })).toBeInTheDocument()
  })

  it('a LocalHR also sees "Genåbn måned" in the footer of a decided row', async () => {
    const user = userEvent.setup()
    authState.role = 'LocalHR'
    mockRoutes({ overview: [row({ status: 'APPROVED', decisionAt: '2026-04-01T08:00:00Z' })] })
    renderPage()
    await expandFirstRow(user)
    expect(screen.getByRole('button', { name: 'Genåbn måned' })).toBeInTheDocument()
  })
})
