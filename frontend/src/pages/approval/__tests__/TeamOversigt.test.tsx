// S87 / TASK-8702 — vitest + @testing-library/react tests for the leader
// Teamoversigt page (the team-overview aggregate at /godkend/oversigt).
//
// Coverage: contract-row render; the 4-status display mapping; KPI band;
// filter chips + full-team counts; live search; column sort; bulk-select +
// "Godkend N valgte" (sequential single-shot approve loop with a 409 lost-race
// row); the reject dialog (kit Radix Dialog, optional reason);
// reopen visibility (S89 Phase 1: shown to leader+, was LocalHR+); and the nav
// redirect (godkend/godkendelser → godkend/oversigt).
//
// PAT-007: the useAuth mock returns a referentially-stable object so the page's
// memoised derivations don't thrash. fetch is mocked at the network boundary.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Routes, Route, Navigate } from 'react-router-dom'
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

// ── Fixtures: the team-overview contract rows ────────────────────────────────
function row(over: Partial<Record<string, unknown>> = {}) {
  return {
    periodId: 'p-1',
    employeeId: 'emp001',
    displayName: 'Jesper Andersen',
    agreement: 'AC',
    status: 'SUBMITTED',
    submittedAt: '2026-03-29T10:00:00Z',
    decisionAt: null,
    rejectionReason: null,
    normExpected: 147,
    normRegistered: 140,
    flexBalance: 3.5,
    overtime: 0,
    ferieUsed: 5,
    ferieTotal: 25,
    awayToday: false,
    hasWarning: false,
    payrollExported: false,
    payrollExportedAt: null,
    ...over,
  }
}

const team = [
  row({ periodId: 'p-1', employeeId: 'emp001', displayName: 'Anna Berg', status: 'SUBMITTED', flexBalance: 5.0, normRegistered: 147, hasWarning: false }),
  row({ periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl', status: 'APPROVED', flexBalance: -2.0, normRegistered: 120, awayToday: true, hasWarning: true }),
  row({ periodId: 'p-3', employeeId: 'emp003', displayName: 'Carla Eng', status: 'REJECTED', flexBalance: 0, normRegistered: 100, rejectionReason: 'Mangler fordeling' }),
  row({ periodId: null, employeeId: 'emp004', displayName: 'David Friis', status: 'DRAFT', flexBalance: 1.0, normRegistered: 0 }),
  row({ periodId: 'p-5', employeeId: 'emp005', displayName: 'Emil Holm', status: 'EMPLOYEE_APPROVED', flexBalance: 8.0, normRegistered: 130, hasWarning: true }),
]

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/** Route the team-overview GET to the given roster; everything else → {}. */
function mockOverview(rows = team) {
  mockFetch.mockImplementation(async (url: string) => {
    if (typeof url === 'string' && url.includes('/api/approval/team-overview')) {
      return jsonResponse({ employees: rows })
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

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  authState.role = 'LocalLeader'
})

describe('TeamOversigt — render + status mapping', () => {
  it('fetches the team-overview aggregate and renders one row per employee', async () => {
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    expect(screen.getByText('Bo Dahl')).toBeInTheDocument()
    expect(screen.getByText('David Friis')).toBeInTheDocument()
    // The aggregate endpoint was called.
    const calls = mockFetch.mock.calls.filter((c: unknown[]) =>
      typeof c[0] === 'string' && (c[0] as string).includes('/api/approval/team-overview'))
    expect(calls.length).toBeGreaterThanOrEqual(1)
  })

  it('maps the 5 backend statuses to the 4 display badges', async () => {
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // Scope to the status BADGES (class `badge`) — "Godkendt"/"Afvist" also appear
    // as filter-chip / KPI labels elsewhere on the page.
    expect(screen.getAllByText('Indsendt', { selector: '.badge' })).toHaveLength(2) // SUBMITTED + EMPLOYEE_APPROVED
    expect(screen.getByText('Godkendt', { selector: '.badge' })).toBeInTheDocument() // APPROVED
    expect(screen.getByText('Afvist', { selector: '.badge' })).toBeInTheDocument()   // REJECTED
    expect(screen.getByText('Kladde', { selector: '.badge' })).toBeInTheDocument()   // DRAFT
  })

  it('the no-period DRAFT row shows "Ikke indsendt" (no handling actions)', async () => {
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('David Friis')).toBeInTheDocument())
    const draftRow = screen.getByTestId('team-row-emp004')
    expect(within(draftRow).getByText('Ikke indsendt')).toBeInTheDocument()
    expect(within(draftRow).queryByRole('button', { name: 'Godkend' })).toBeNull()
    // The checkbox is disabled for a non-pending / no-period row.
    expect(within(draftRow).getByRole('checkbox')).toBeDisabled()
  })
})

describe('TeamOversigt — KPI band (full team)', () => {
  /** The `.kpiValue` paragraph text inside the card whose label is `label`. */
  function kpiValueText(label: string): string {
    const card = screen.getByText(label, { selector: '.kpiLabel' }).closest('.kpiCard') as HTMLElement
    return (card.querySelector('.kpiValue')!.textContent ?? '').trim()
  }

  it('computes the 5 KPIs over the FULL team', async () => {
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // Afventer = Indsendt count = 2 (SUBMITTED + EMPLOYEE_APPROVED).
    expect(kpiValueText('Afventer din godkendelse')).toBe('2')
    // Advarsler = hasWarning count = 2.
    expect(kpiValueText('Advarsler')).toBe('2')
    // Fravær i dag = awayToday count = 1.
    expect(kpiValueText('Fravær i dag')).toBe('1')
    // Godkendt = APPROVED count = "1 / 5" (value + "/ N" suffix).
    expect(kpiValueText('Godkendt')).toContain('1')
    expect(kpiValueText('Godkendt')).toContain('/ 5')
  })
})

describe('TeamOversigt — filter chips + counts (full team)', () => {
  it('chip counts reflect the FULL team, not the filtered view', async () => {
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    // Alle 5 / Afventer 2 / Godkendt 1 / Advarsel 2 — counts on the chips.
    const alleChip = screen.getByRole('button', { name: /Alle/ })
    expect(within(alleChip).getByText('5')).toBeInTheDocument()
    const afventerChip = screen.getByRole('button', { name: /Afventer/ })
    expect(within(afventerChip).getByText('2')).toBeInTheDocument()
  })

  it('filtering by Afventer hides non-pending rows but keeps full-team chip counts', async () => {
    const user = userEvent.setup()
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: /Afventer/ }))
    // Only the two pending rows remain (Anna SUBMITTED, Emil EMPLOYEE_APPROVED).
    expect(screen.getByText('Anna Berg')).toBeInTheDocument()
    expect(screen.getByText('Emil Holm')).toBeInTheDocument()
    expect(screen.queryByText('Bo Dahl')).toBeNull()      // APPROVED filtered out
    expect(screen.queryByText('Carla Eng')).toBeNull()    // REJECTED filtered out
    // The "Alle" chip still shows the full-team count 5.
    const alleChip = screen.getByRole('button', { name: /Alle/ })
    expect(within(alleChip).getByText('5')).toBeInTheDocument()
  })

  it('filtering by Advarsel keeps only rows with hasWarning', async () => {
    const user = userEvent.setup()
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: /Advarsel/ }))
    expect(screen.getByText('Bo Dahl')).toBeInTheDocument()
    expect(screen.getByText('Emil Holm')).toBeInTheDocument()
    expect(screen.queryByText('Anna Berg')).toBeNull()
  })
})

describe('TeamOversigt — search', () => {
  it('lives-filters on name and employeeId (case-insensitive)', async () => {
    const user = userEvent.setup()
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    const box = screen.getByLabelText('Søg medarbejder')
    await user.type(box, 'carla')
    expect(screen.getByText('Carla Eng')).toBeInTheDocument()
    expect(screen.queryByText('Anna Berg')).toBeNull()
    // Clear → search by employeeId.
    await user.clear(box)
    await user.type(box, 'emp002')
    expect(screen.getByText('Bo Dahl')).toBeInTheDocument()
    expect(screen.queryByText('Carla Eng')).toBeNull()
  })

  it('shows the empty-state when nothing matches', async () => {
    const user = userEvent.setup()
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.type(screen.getByLabelText('Søg medarbejder'), 'zzzz')
    expect(screen.getByText('Ingen medarbejdere matcher søgningen.')).toBeInTheDocument()
  })
})

describe('TeamOversigt — column sort', () => {
  it('sorts by name ascending then descending', async () => {
    const user = userEvent.setup()
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())

    const namesInOrder = () =>
      screen.getAllByTestId(/^team-row-/).map(r => within(r).getByText(/Berg|Dahl|Eng|Friis|Holm/).textContent)

    await user.click(screen.getByText(/Medarbejder/))
    expect(namesInOrder()).toEqual(['Anna Berg', 'Bo Dahl', 'Carla Eng', 'David Friis', 'Emil Holm'])
    // Flip to descending.
    await user.click(screen.getByText(/Medarbejder/))
    expect(namesInOrder()).toEqual(['Emil Holm', 'David Friis', 'Carla Eng', 'Bo Dahl', 'Anna Berg'])
  })
})

describe('TeamOversigt — bulk approve (sequential, status-aware)', () => {
  it('"Godkend N valgte" fires N sequential approves and clears the succeeded', async () => {
    const user = userEvent.setup()
    const approveUrls: string[] = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        approveUrls.push(url)
        return jsonResponse({ status: 'APPROVED' })
      }
      if (typeof url === 'string' && url.includes('/api/approval/team-overview')) {
        return jsonResponse({ employees: team })
      }
      return jsonResponse({})
    })
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())

    // Select the two pending rows via "Vælg alle".
    await user.click(screen.getByRole('checkbox', { name: 'Vælg alle' }))
    const bulkBtn = await screen.findByRole('button', { name: /Godkend 2 valgte/ })
    await user.click(bulkBtn)

    // Two approve POSTs fired (one per pending row), to the right period ids.
    await waitFor(() => expect(approveUrls.length).toBe(2))
    expect(approveUrls.some(u => u.includes('/api/approval/p-1/approve'))).toBe(true)
    expect(approveUrls.some(u => u.includes('/api/approval/p-5/approve'))).toBe(true)
  })

  it('a 409 lost-race row is surfaced (not silently dropped)', async () => {
    const user = userEvent.setup()
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/approve') && init?.method === 'POST') {
        return {
          ok: false, status: 409, headers: new Headers(),
          json: async () => ({ error: 'conflict' }),
          text: async () => JSON.stringify({ error: 'conflict' }),
        }
      }
      if (typeof url === 'string' && url.includes('/api/approval/team-overview')) {
        return jsonResponse({ employees: [team[0]] })
      }
      return jsonResponse({})
    })
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('checkbox', { name: 'Vælg Anna Berg' }))
    await user.click(await screen.findByRole('button', { name: /Godkend 1 valgte/ }))
    // The 409 is surfaced as a toast ("sprang over").
    await waitFor(() => expect(screen.getByText(/sprang over/)).toBeInTheDocument())
  })
})

describe('TeamOversigt — reject dialog (kit Radix Dialog)', () => {
  it('Afvis opens a role=dialog; reason is OPTIONAL; confirm POSTs the reason', async () => {
    const user = userEvent.setup()
    let rejectBody: unknown = null
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/reject') && init?.method === 'POST') {
        rejectBody = JSON.parse(init.body as string)
        return jsonResponse({ status: 'REJECTED' })
      }
      if (typeof url === 'string' && url.includes('/api/approval/team-overview')) {
        return jsonResponse({ employees: [team[0]] })
      }
      return jsonResponse({})
    })
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Afvis' }))
    const dialog = await screen.findByRole('dialog')
    // Confirm is enabled even with no reason (the reason is OPTIONAL per the hifi).
    const confirm = within(dialog).getByRole('button', { name: 'Afvis måned' })
    expect(confirm).toBeEnabled()
    const textarea = within(dialog).getByPlaceholderText('Skriv en kort begrundelse til medarbejderen…')
    await user.type(textarea, 'Mangler hviletid')
    await user.click(confirm)

    await waitFor(() => expect(rejectBody).toEqual({ reason: 'Mangler hviletid' }))
  })

  it('Escape closes the reject dialog WITHOUT firing a reject POST', async () => {
    const user = userEvent.setup()
    mockOverview([team[0]])
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Afvis' }))
    await screen.findByRole('dialog')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const rejectCalls = mockFetch.mock.calls.filter((c: unknown[]) =>
      typeof c[0] === 'string' && (c[0] as string).includes('/reject'))
    expect(rejectCalls).toHaveLength(0)
  })
})

describe('TeamOversigt — reopen visibility (S89 Phase 1: leader+)', () => {
  it('a Leader SEES the Genåbn control on a decided row (was LocalHR+ pre-S89)', async () => {
    authState.role = 'LocalLeader'
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    const approvedRow = screen.getByTestId('team-row-emp002') // APPROVED
    expect(within(approvedRow).getByRole('button', { name: 'Genåbn' })).toBeInTheDocument()
  })

  it('LocalHR also sees the Genåbn control on a decided row', async () => {
    authState.role = 'LocalHR'
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    const approvedRow = screen.getByTestId('team-row-emp002')
    expect(within(approvedRow).getByRole('button', { name: 'Genåbn' })).toBeInTheDocument()
  })

  it('a REJECTED row shows NO Genåbn (not reopenable; the employee re-submits) — S91 dead-button fix', async () => {
    authState.role = 'LocalLeader'
    mockOverview()
    renderPage()
    await waitFor(() => expect(screen.getByText('Carla Eng')).toBeInTheDocument())
    const rejectedRow = screen.getByTestId('team-row-emp003') // REJECTED
    // RED on the old code: a REJECTED (isDecided) row rendered a dead Genåbn that 409s.
    expect(within(rejectedRow).queryByRole('button', { name: 'Genåbn' })).toBeNull()
    expect(within(rejectedRow).getByText('Afventer ny indsendelse')).toBeInTheDocument()
  })
})

// ── S90 / TASK-9005 — payroll-export lock surfacing ──────────────────────────
// Once a month is sent to lønkørsel (payrollExported=true) the reopen control
// disappears (corrections-only, ADR-034) and a non-actionable "Sendt til
// lønkørsel" indicator shows instead. A non-exported decided row keeps the S89
// leader-reopen behavior.
describe('TeamOversigt — payroll-export lock (S90)', () => {
  it('an EXPORTED decided row hides Genåbn and shows "Sendt til lønkørsel"', async () => {
    mockOverview([
      row({
        periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl',
        status: 'APPROVED', payrollExported: true, payrollExportedAt: '2026-04-12T08:00:00Z',
      }),
    ])
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    const exportedRow = screen.getByTestId('team-row-emp002')
    // No reopen control — the month is locked.
    expect(within(exportedRow).queryByRole('button', { name: 'Genåbn' })).toBeNull()
    // The non-actionable lock indicator is shown instead.
    expect(within(exportedRow).getByText('Sendt til lønkørsel')).toBeInTheDocument()
  })

  it('a NON-exported decided row still shows Genåbn (S89 preserved)', async () => {
    mockOverview([
      row({
        periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl',
        status: 'APPROVED', payrollExported: false,
      }),
    ])
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    const decidedRow = screen.getByTestId('team-row-emp002')
    expect(within(decidedRow).getByRole('button', { name: 'Genåbn' })).toBeInTheDocument()
    expect(within(decidedRow).queryByText('Sendt til lønkørsel')).toBeNull()
  })

  it('the detail footer hides "Genåbn måned" and shows the lock indicator for an exported row', async () => {
    const user = userEvent.setup()
    mockOverview([
      row({
        periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl',
        status: 'APPROVED', payrollExported: true, payrollExportedAt: '2026-04-12T08:00:00Z',
      }),
    ])
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    // Expand the detail panel.
    await user.click(screen.getByRole('button', { name: /detaljer for Bo Dahl/ }))
    const detail = await screen.findByTestId('team-detail-row-emp002')
    expect(within(detail).queryByRole('button', { name: 'Genåbn måned' })).toBeNull()
    expect(within(detail).getByText('Sendt til lønkørsel')).toBeInTheDocument()
  })

  it('the detail footer keeps "Genåbn måned" for a NON-exported decided row (S89 preserved)', async () => {
    const user = userEvent.setup()
    mockOverview([
      row({
        periodId: 'p-2', employeeId: 'emp002', displayName: 'Bo Dahl',
        status: 'APPROVED', payrollExported: false,
      }),
    ])
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: /detaljer for Bo Dahl/ }))
    const detail = await screen.findByTestId('team-detail-row-emp002')
    expect(within(detail).getByRole('button', { name: 'Genåbn måned' })).toBeInTheDocument()
    expect(within(detail).queryByText('Sendt til lønkørsel')).toBeNull()
  })
})

// ── S116 / TASK-11602 — the typed-switch WIRE pins for the mutation trio ─────
// The approve/reject/reopen call sites switched from `post<unknown>(templateUrl)`
// to the typed spec-keyed forms. These pins assert the EXACT wire behavior of
// each (URL byte-identical, approve carries NO body — the op binds no request
// DTO — and reject/reopen carry the identical bodies), so a regression in the
// typed client's URL interpolation or body threading reds this file. The call
// FORM itself (typed vs bare-legacy) is additionally audited + lint-tiered —
// a bare `post(url)` legacy call would pass these wire pins, which is why the
// call-form audit is a separate acceptance criterion (Reviewer N1).
describe('TeamOversigt — S116 typed-switch wire pins (approve/reject/reopen)', () => {
  type Captured = { url: string; method: string; body: unknown }

  function captureAll(roster = team) {
    const calls: Captured[] = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      calls.push({
        url,
        method: init?.method ?? 'GET',
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      })
      if (typeof url === 'string' && url.includes('/api/approval/team-overview')) {
        return jsonResponse({ employees: roster })
      }
      return jsonResponse({ periodId: 'p-x', status: 'APPROVED' })
    })
    return calls
  }

  it('approve: POST /api/approval/{periodId}/approve — exact URL, NO body', async () => {
    const user = userEvent.setup()
    const calls = captureAll([team[0]]) // Anna, SUBMITTED, p-1
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Godkend' }))
    await waitFor(() => {
      const post = calls.find(c => c.method === 'POST')
      expect(post?.url).toBe('/api/approval/p-1/approve')
      expect(post?.body).toBeUndefined()
    })
  })

  it('reject: POST /api/approval/{periodId}/reject — exact URL, {reason} body', async () => {
    const user = userEvent.setup()
    const calls = captureAll([team[0]])
    renderPage()
    await waitFor(() => expect(screen.getByText('Anna Berg')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Afvis' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByPlaceholderText('Skriv en kort begrundelse til medarbejderen…'),
      'Mangler hviletid',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Afvis måned' }))
    await waitFor(() => {
      const post = calls.find(c => c.method === 'POST')
      expect(post?.url).toBe('/api/approval/p-1/reject')
      expect(post?.body).toEqual({ reason: 'Mangler hviletid' })
    })
  })

  it('reopen: POST /api/approval/{periodId}/reopen — exact URL, the fixed leader reason body', async () => {
    const user = userEvent.setup()
    const calls = captureAll([team[1]]) // Bo, APPROVED, p-2 → Genåbn visible
    renderPage()
    await waitFor(() => expect(screen.getByText('Bo Dahl')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Genåbn' }))
    await waitFor(() => {
      const post = calls.find(c => c.method === 'POST')
      expect(post?.url).toBe('/api/approval/p-2/reopen')
      expect(post?.body).toEqual({ reason: 'Genåbnet af leder' })
    })
  })
})

// ── Nav redirect: godkend/godkendelser → godkend/oversigt (OQ-3) ─────────────
// A standalone routing check mirroring App.tsx's <Navigate>. The TeamOversigt
// page must mount under /godkend/oversigt after the redirect.
describe('TeamOversigt — nav redirect', () => {
  it('godkend/godkendelser redirects to godkend/oversigt (renders Teamoversigt)', async () => {
    mockOverview([team[0]])
    render(
      <MemoryRouter initialEntries={['/godkend/godkendelser']}>
        <Routes>
          <Route path="/godkend/oversigt" element={<TeamOversigt />} />
          <Route path="/godkend/godkendelser" element={<Navigate to="/godkend/oversigt" replace />} />
        </Routes>
      </MemoryRouter>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Teamoversigt' })).toBeInTheDocument())
  })
})
