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

// S124 / TASK-12403 - a served month with hours on SPECIFIC DAYS. The whole point of the grid is
// day-level evidence, so the fixture places hours on distinguishable dates, not a lump total.
const cleanSkemaMonth = {
  employeeId: 'emp001',
  year: 2026,
  month: 3,
  entries: [
    { date: '2026-03-02', projectCode: 'ALFA', hours: 7.4, activityType: 'NORMAL', taskId: 'ALFA' },
    { date: '2026-03-03', projectCode: 'BETA', hours: 5, activityType: 'NORMAL', taskId: 'BETA' },
  ],
  absences: [
    { date: '2026-03-04', absenceType: 'VACATION', hours: 7.4, feriedage: 1 },
  ],
  workTime: [
    { date: '2026-03-02', intervals: [{ start: '08:00', end: '15:24' }], manualHours: 0 },
  ],
  dailyNorm: [
    { date: '2026-03-02', hours: 7.4 },
    { date: '2026-03-03', hours: 7.4 },
    { date: '2026-03-04', hours: 7.4 },
  ],
  consumptionBasis: [],
  // Step-7a Codex: these MUST match the generated contract (projectCode/projectName, type/label).
  // The earlier {code,name} shape rendered NO project rows, and the assertion below was silently
  // satisfied by the identically-named strings in `cleanBreakdown` — a vacuous test.
  projects: [
    { projectId: 'p-alfa', projectCode: 'ALFA', projectName: 'Projekt Alfa', sortOrder: 1 },
    { projectId: 'p-beta', projectCode: 'BETA', projectName: 'Projekt Beta', sortOrder: 2 },
  ],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }],
  catalogs: {
    projects: [
      { projectId: 'p-alfa', projectCode: 'ALFA', projectName: 'Projekt Alfa', sortOrder: 1 },
      { projectId: 'p-beta', projectCode: 'BETA', projectName: 'Projekt Beta', sortOrder: 2 },
    ],
    absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }],
  },
  rowPreferences: null,
  approval: {
    status: 'SUBMITTED', periodId: 'p-1', submittedAt: '2026-03-29T10:00:00Z',
    approvedAt: null, rejectionReason: null, payrollExported: false,
  },
  entitlementEligibility: null,
  seniorDayMinAge: null,
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
  /** S124 / TASK-12403 - the served skema month for the read-only grid. */
  skema?: unknown
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
    // S124 / TASK-12403 - the leader's read-only month-grid read.
    if (url.includes('/api/skema/') && url.includes('/month')) {
      return jsonResponse(opts.skema ?? cleanSkemaMonth)
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
    // S124 / TASK-12403 — the inline grid renders the SAME project names, so these are scoped to
    // the Fordeling column rather than the whole panel (an unscoped query is now ambiguous).
    await waitFor(() => expect(screen.getAllByText('Projekt Alfa').length).toBeGreaterThan(0))
    expect(screen.getAllByText('Projekt Beta').length).toBeGreaterThan(0)
    // Header sum {allocated} / {worked} t.
    expect(screen.getByText('140,0 / 140,0 t')).toBeInTheDocument()
  })

  it('a failed breakdown fetch → soft "Kunne ikke hente fordeling"; the rest still renders', async () => {
    const user = userEvent.setup()
    mockRoutes({ breakdownStatus: 500 })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByText('Kunne ikke hente fordeling')).toBeInTheDocument())
    // S125 / TASK-12500 — the summary section is titled "Overblik" (was "SALDI"); the row figures
    // inside it still render, which is the fault-isolation point of this test.
    expect(screen.getByText('Overblik')).toBeInTheDocument()
    expect(screen.getByText('Normtimer')).toBeInTheDocument()
  })
})

describe('TeamRowDetail — Overblik balances (row figures, no extra fetch)', () => {
  it('renders the 4 balance cells with the Merarbejde label for AC', async () => {
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
        // S120 mock re-anchoring: the wire serves INTEGER enums (DAILY_REST=0, WARNING=0).
        warnings: [{ violationType: 0, date: '2026-03-10', actualValue: 9, thresholdValue: 11, severity: 0, isVoluntaryExempt: false, message: 'For kort hviletid den 10.' }],
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
    // Overblik + breakdown still render (fault isolated to the Advarsel arm).
    expect(screen.getByText('Overblik')).toBeInTheDocument()
    expect(screen.getAllByText('Projekt Alfa').length).toBeGreaterThan(0)
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

// ===================================================================================
// S124 / TASK-12403 - the leader's READ-ONLY month skema, INLINE and ALWAYS SHOWN.
//
// Owner ruling 2026-07-30: "Skema needs to be the default view ... the skema should always be
// shown." It was briefly behind a "Vis skema" button; that put the evidence one click further away
// than the decision. Expanding a row now renders summary -> grid -> decision buttons.
//
// The load-bearing assertion is still that this surface cannot WRITE: the grid is read-only by
// construction, and since TASK-12404 the backend also refuses a leader's write. Both layers are
// pinned - the component one here, the API one in S91TreePageHrAccessTests.
// ===================================================================================
describe("TeamRowDetail - the inline read-only employee skema (S124 / TASK-12403)", () => {
  it("shows the month grid IMMEDIATELY on expand - no extra click, per-DAY registrations visible", async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)

    // Present as soon as the panel opens; there is no affordance to press first.
    await waitFor(() => expect(screen.getByTestId("manager-skema-emp001")).toBeInTheDocument())
    expect(screen.queryByTestId("view-skema-emp001")).toBeNull()

    // The month read fired exactly once, on expand.
    const skemaCalls = mockFetch.mock.calls.filter((c: unknown[]) =>
      typeof c[0] === "string" && (c[0] as string).includes("/api/skema/emp001/month"))
    expect(skemaCalls.length).toBe(1)

    // DAY-LEVEL evidence, asserted INSIDE the grid — `cleanBreakdown` carries the same project
    // names, so an unscoped query would pass even with an empty grid (Step-7a Codex).
    const grid = screen.getByTestId("manager-skema-emp001")
    await waitFor(() => expect(within(grid).getByText("Projekt Alfa")).toBeInTheDocument())
    expect(within(grid).getByText("Projekt Beta")).toBeInTheDocument()
    // NOTE the grid renders the month the PAGE stepper is on (real "today"), not the fixture's
    // March, so a dated-cell assertion cannot be pinned here without freezing the clock. The
    // scoping above is what removes the vacuity: `cleanBreakdown` carries these same names OUTSIDE
    // the grid, so `within(grid)` is what proves the GRID itself rendered the served rows. Dated
    // per-day cells are pinned in SkemaGrid.test.tsx against an explicit year/month.
  })

  it("orders the panel summary -> skema -> decision buttons (evidence before verdict)", async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByTestId("manager-skema-emp001")).toBeInTheDocument())

    const panel = screen.getByTestId("team-detail-row-emp001")
    const text = panel.textContent ?? ""
    const summaryAt = text.indexOf("Overblik")
    const skemaAt = text.indexOf("Skema")
    const decideAt = text.indexOf("Godkend måned")
    expect(summaryAt).toBeGreaterThanOrEqual(0)
    expect(skemaAt).toBeGreaterThan(summaryAt)
    expect(decideAt).toBeGreaterThan(skemaAt)
  })

  it("is STILL lazy: no month read happens until a row is expanded", async () => {
    mockRoutes()
    renderPage()
    await waitFor(() => expect(screen.getByText("Anna Berg")).toBeInTheDocument())
    // The table rendered; nothing expanded yet, so no per-employee month read.
    const skemaCalls = mockFetch.mock.calls.filter((c: unknown[]) =>
      typeof c[0] === "string" && (c[0] as string).includes("/api/skema/"))
    expect(skemaCalls).toHaveLength(0)
  })

  it("THE READ-ONLY GUARD: no save request can be issued from the inline grid", async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByTestId("manager-skema-emp001")).toBeInTheDocument())

    const body = screen.getByTestId("manager-skema-emp001")
    expect(body.querySelectorAll("input")).toHaveLength(0)
    const writes = mockFetch.mock.calls.filter((c: unknown[]) => {
      const url = typeof c[0] === "string" ? (c[0] as string) : ""
      const init = c[1] as RequestInit | undefined
      return url.includes("/api/skema/") && init?.method === "POST"
    })
    expect(writes).toHaveLength(0)
  })

  it("an un-submitted row cannot reach the grid at all (no expander, so no panel)", async () => {
    const user = userEvent.setup()
    mockRoutes({
      overview: [row({
        periodId: null, employeeId: "emp009", displayName: "Withheld Person", status: "DRAFT",
        normRegistered: null, overtime: null, hasWarning: null, flexBalance: null, ferieUsed: null,
      })],
    })
    renderPage()
    await waitFor(() => expect(screen.getByText("Withheld Person")).toBeInTheDocument())
    // TASK-12402 removed the expander for un-submitted rows and the grid lives inside that panel, so
    // the two rules compose: nothing sent means nothing to inspect.
    expect(screen.queryByRole("button", { name: /detaljer for Withheld Person/ })).toBeNull()
    await user.click(screen.getByTestId("team-row-emp009"))
    expect(screen.queryByTestId("manager-skema-emp009")).toBeNull()
  })

  it("stays available AFTER a decision - a rejected month can be re-read", async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row({ status: "REJECTED", rejectionReason: "Mangler fordeling" })] })
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByTestId("manager-skema-emp001")).toBeInTheDocument())
  })
})

// ===================================================================================
// S125 / TASK-12500 — the two collapsible panel sections (Overblik / Skema).
//
// SESSION-STICKY by design (owner ruling): the fold lives on the PAGE, not the row, because the
// panel unmounts when a row collapses — per-row state would reset on every expand. So a fold made
// while reviewing one employee must survive opening the next, and reset only on reload.
// ===================================================================================
describe('TeamRowDetail — collapsible Overblik / Skema sections (S125 / TASK-12500)', () => {
  it('both sections are OPEN by default when an employee is pressed', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)

    expect(screen.getByTestId('toggle-overblik-emp001')).toHaveAttribute('aria-expanded', 'true')
    await waitFor(() => expect(screen.getByTestId('toggle-skema-emp001')).toHaveAttribute('aria-expanded', 'true'))
    // And their content is present.
    expect(screen.getByText('Flex saldo')).toBeInTheDocument()
    expect(screen.getByTestId('manager-skema-emp001')).toBeInTheDocument()
  })

  it('folds each section INDEPENDENTLY — collapsing Overblik leaves Skema open', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)
    await waitFor(() => expect(screen.getByTestId('manager-skema-emp001')).toBeInTheDocument())

    await user.click(screen.getByTestId('toggle-overblik-emp001'))
    expect(screen.getByTestId('toggle-overblik-emp001')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Flex saldo')).toBeNull()
    // Skema untouched.
    expect(screen.getByTestId('manager-skema-emp001')).toBeInTheDocument()

    // And the other direction: fold Skema, Overblik comes back on its own.
    await user.click(screen.getByTestId('toggle-overblik-emp001'))
    await user.click(screen.getByTestId('toggle-skema-emp001'))
    expect(screen.getByText('Flex saldo')).toBeInTheDocument()
    expect(screen.queryByTestId('manager-skema-emp001')).toBeNull()
  })

  it('SESSION-STICKY: a fold survives switching to another employee', async () => {
    const user = userEvent.setup()
    mockRoutes({ overview: [row(), row({ periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl' })] })
    renderPage()
    await expandFirstRow(user)

    // Fold Overblik on Anna.
    await user.click(screen.getByTestId('toggle-overblik-emp001'))
    expect(screen.queryByText('Flex saldo')).toBeNull()

    // Open Bo (the accordion closes Anna and UNMOUNTS her panel — the exact case per-row state
    // would have got wrong).
    await user.click(screen.getByRole('button', { name: /detaljer for Bo Dahl/ }))
    await waitFor(() => expect(screen.getByTestId('toggle-overblik-emp002')).toBeInTheDocument())

    expect(screen.getByTestId('toggle-overblik-emp002')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Flex saldo')).toBeNull()
    // Skema was never folded, so it stays open for Bo too.
    expect(screen.getByTestId('toggle-skema-emp002')).toHaveAttribute('aria-expanded', 'true')
  })

  it('the section headers are real buttons (keyboard-operable), not clickable divs', async () => {
    const user = userEvent.setup()
    mockRoutes()
    renderPage()
    await expandFirstRow(user)

    const head = screen.getByTestId('toggle-overblik-emp001')
    expect(head.tagName).toBe('BUTTON')
    head.focus()
    await user.keyboard('{Enter}')
    expect(head).toHaveAttribute('aria-expanded', 'false')
  })
})
