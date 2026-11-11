// S72 / TASK-7205 — INTEGRATION tests for the rewritten SkemaPage (SPRINT-72
// R13 layering: the end-to-end pins live HERE, over the REAL SkemaGrid (7202),
// SkemaDayPanel (7203), SkemaProjectManager (7204) and BalanceSummary, with the
// network mocked at the fetch level (the ApprovalDashboard.test pattern).
//
// Named pins (each its own test):
//   R2  — the Flex card's "Denne måned" equals the grid's Diff trailing total,
//         and BOTH surfaces consume the ONE useSkema computation (W1 — no copy).
//   R3  — hiding a populated project via the modal flow leaves the Diff / I alt /
//         ✓ arithmetic unchanged, shows the hidden-rows affordance, and the
//         approval-gate 422 surface still renders. S72 Step-7a B1: the basis is
//         the UNION of catalogs ∪ served entry keys — incl. a DEACTIVATED
//         project absent from the catalogs (labeled by code).
//   R5  — locked month: readOnly grid, unreachable panel, REACHABLE modal.
//   R6  — S72 Step-7a W2: the §J analysis sees locally-edited NEIGHBOR days
//         (edit day N locally, open day N+1 → the warning reflects N's state).
//   R16 — a panel period edit round-trips through the hook's
//         buildWorkTimePayload as { date, intervals } (clock time only).
//   R10 — the D-A card pins (hours-first arithmetic; the null-scalar em-dash
//         fail-soft; the null-feriedage skip in the Afholdt sub-line).
//   R11 — the modal opens from BOTH entry points; a modal action live-updates
//         the grid with the R16 FLUSH → PUT → refetch sequence in order.
//   R16 — the page owns no private buildWorkTimePayload (source assertion);
//         S72 Step-7a B2: the flush is in-flight-safe (an unresolved save POST
//         blocks the PUT) and failure-safe (a failed flush save ABORTS the
//         preference write — no PUT, no refetch, the error surfaces).
//
// FIXTURE CONTRACT (S72 Step-7a Codex N — the pre-fix fixture concealed B1):
// per the 7201 month-GET, a CONFIGURED user's legacy `projects`/`absenceTypes`
// fields serve ONLY the VISIBLE selection (catalog ∩ selections); the full
// active catalog lives in `catalogs`; served `entries` may carry keys outside
// both (deactivated projects with historical hours).
//
// March 2026 reference: Mar 1 = Sunday, Mar 2 = Monday. Fixture arithmetic:
// Mar 2 worked = one 08:00–17:30 interval = 9,5; allocated 7,4 + 2,1 = 9,5 (✓);
// Diff +2,1. Mar 3 full VACATION 7,4 → Diff 0,0. Mar 4 VACATION 3,7 (feriedage
// NULL) → Diff −3,7. Diff total = −1,6. I alt total = 20,6.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { screen, waitFor, fireEvent, within, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { renderWithCalendar } from '../../test/renderWithCalendar'
import { forceTestTimeZone, restoreTestTimeZone } from '../../lib/__tests__/testTimeZone'
// The R16/W1 source assertions read modules as text via Vite's ?raw suffix
// (typed by vite/client; keeps the FE tsconfig free of node types).
import skemaPageSource from '../SkemaPage.tsx?raw'
import skemaGridSource from '../../components/SkemaGrid.tsx?raw'
import useBalanceSummarySource from '../../hooks/useBalanceSummary.ts?raw'
import { SkemaPage } from '../SkemaPage'
import {
  computeDayDiffs,
  computeMonthDiffTotal,
  deriveSkemaRowBasis,
} from '../../hooks/useSkema'
import type { SkemaMonthData } from '../../types'
import type { BalanceSummary as BalanceSummaryData } from '../../hooks/useBalanceSummary'

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({
    user: { employeeId: 'emp001', role: 'Employee' },
    role: 'Employee',
    orgId: 'STY01',
    agreementCode: 'AC',
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// ── Fixtures ──

function buildDailyNorm(): { date: string; hours: number | null }[] {
  const out: { date: string; hours: number | null }[] = []
  for (let d = 1; d <= 31; d++) {
    const date = `2026-03-${String(d).padStart(2, '0')}`
    const dow = new Date(date + 'T00:00:00').getDay()
    out.push({ date, hours: dow === 0 || dow === 6 ? 0 : 7.4 })
  }
  return out
}

// S120 / TASK-12001 mock re-anchoring — the fixture mirrors the SPEC
// `SkemaMonthResponse`: `fullDayOnly` is REQUIRED on every absence-type row
// (previously optional-omitted), and the always-served top-level
// `employeeDeadline`/`managerDeadline` + `consumptionBasis` members are
// present (empty basis = no snap data, byte-equivalent to the pre-S120
// absent-member behavior). No behavior pin changed.
function makeMonthData(overrides: Partial<SkemaMonthData> = {}): SkemaMonthData {
  return {
    year: 2026,
    month: 3,
    daysInMonth: 31,
    // The 7201 contract: this user is CONFIGURED, so `projects` is the VISIBLE
    // selection ONLY (= rowPreferences.projects here); the full catalog (incl.
    // the un-selected EXTRA) lives in `catalogs.projects` below. Tests that
    // override rowPreferences MUST override `projects` to match the selection.
    projects: [
      { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 },
      { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
    ],
    absenceTypes: [
      { type: 'VACATION', label: 'Ferie', fullDayOnly: false },
      { type: 'CARE_DAY', label: 'Omsorgsdage', fullDayOnly: false },
      { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', fullDayOnly: false },
    ],
    entries: [
      { date: '2026-03-02', projectCode: 'DRIFT', hours: 7.4 },
      { date: '2026-03-02', projectCode: 'UDV', hours: 2.1 },
    ],
    absences: [
      { date: '2026-03-03', absenceType: 'VACATION', hours: 7.4, feriedage: 1 },
      // ADR-032 zero-norm-style row: hours count, the NULL feriedage is skipped (R10)
      { date: '2026-03-04', absenceType: 'VACATION', hours: 3.7, feriedage: null },
    ],
    approval: null,
    employeeDeadline: '2026-04-05',
    managerDeadline: '2026-04-10',
    workTime: [
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '17:30' }] },
    ],
    dailyNorm: buildDailyNorm(),
    rowPreferences: {
      configured: true,
      projects: [
        { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 },
        { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
      ],
      absenceTypes: [
        { type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 },
        { type: 'CARE_DAY', label: 'Omsorgsdage', fullDayOnly: false, sortOrder: 1 },
        { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', fullDayOnly: false, sortOrder: 2 },
      ],
    },
    catalogs: {
      projects: [
        { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 },
        { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
        { projectId: 'p-extra', projectCode: 'EXTRA', projectName: 'Ekstra projekt', sortOrder: 2 },
      ],
      absenceTypes: [
        { type: 'VACATION', label: 'Ferie', fullDayOnly: false },
        { type: 'CARE_DAY', label: 'Omsorgsdage', fullDayOnly: false },
        { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', fullDayOnly: false },
      ],
    },
    boundaryWorkTime: [],
    fullDayNormAtMonthEnd: 7.4,
    consumptionBasis: [],
    ...overrides,
  }
}

// S120 mock re-anchoring — the spec `BalanceSummaryResponse`: + the served
// employeeId/year/month scalars, the REQUIRED null-valued `overtimeBalance`
// and the REQUIRED nullable `settlement` per entitlement row (all display-only
// shape growth; no behavior pin changed).
function makeSummaryData(): BalanceSummaryData {
  return {
    employeeId: 'emp001',
    year: 2026,
    month: 3,
    flexBalance: 4.2,
    flexBalanceMonth: '2026-02',
    flexDelta: 99.9, // the LAST-event delta — must never reach the strip (R10)
    vacationDaysUsed: 8,
    vacationDaysEntitlement: 25,
    normHoursExpected: 162.8,
    normHoursActual: 155.0,
    overtimeHours: 0,
    agreementCode: 'AC',
    hasMerarbejde: true,
    entitlements: [
      { type: 'VACATION', label: 'Ferie', totalQuota: 25, used: 8, planned: 0, carryoverIn: 3, remaining: 17, earned: 20.8, entitlementYear: 2025, settlement: null },
      { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', totalQuota: 5, used: 1, planned: 0, carryoverIn: 0, remaining: 4, earned: 5, entitlementYear: 2025, settlement: null },
      { type: 'CARE_DAY', label: 'Omsorgsdage', totalQuota: 2, used: 0, planned: 0, carryoverIn: 0, remaining: 2, earned: 2, entitlementYear: 2026, settlement: null },
    ],
    overtimeBalance: null,
  }
}

// S120 mock re-anchoring — `violationType`/`severity` are INTEGERS on the wire
// (the CLR enums serialize numerically; the old string-valued mock mirrored
// the deleted lying union): DAILY_REST=0, WARNING=0.
const COMPLIANCE_WITH_WARNING = {
  ruleId: 'EU_WTD',
  employeeId: 'emp001',
  success: true,
  violations: [],
  warnings: [
    {
      violationType: 0,
      date: '2026-03-02',
      actualValue: 10,
      thresholdValue: 11,
      severity: 0,
      isVoluntaryExempt: false,
      message: 'Hviletid under 11 timer',
    },
  ],
}

// ── Mutable per-test routing state ──
let monthData: SkemaMonthData
let summaryData: BalanceSummaryData
let fetchLog: string[]
let saveBodies: Array<{
  year: number
  month: number
  entries: { date: string; projectCode: string; hours: number }[] | null
  absences: { date: string; absenceType: string; hours: number }[] | null
  workTime: { date: string; intervals: { start: string; end: string }[] }[] | null
}>
let putBodies: Array<{
  projects: { projectId: string; sortOrder: number }[]
  absenceTypes: { absenceType: string; sortOrder: number }[]
}>
let approveResponder: (() => unknown) | null
/** B2 hook: when set, the save POST resolves through this (deferred/failing
    responses for the in-flight and flush-failure pins). */
let saveResponder: (() => unknown | Promise<unknown>) | null

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/** Apply a row-preferences PUT to the served fixture (the 7201 server's
    full-replacement semantics) so the post-PUT refetch returns the new state.
    S72 Step-7a Codex N (the fixture-contract correction): the refetched month
    serves the legacy `projects`/`absenceTypes` fields as the new VISIBLE
    selection — NOT the full set — exactly like the real container-aware
    backend; `entries`/`absences` keep serving the historical hours verbatim. */
function applyPrefsPut(body: (typeof putBodies)[number]) {
  const byId = new Map(monthData.catalogs!.projects.map((p) => [p.projectId, p]))
  const byType = new Map(monthData.catalogs!.absenceTypes.map((a) => [a.type, a]))
  const visibleProjects = body.projects.map((e) => byId.get(e.projectId)!)
  const visibleAbsenceTypes = body.absenceTypes.map((e) => byType.get(e.absenceType)!)
  monthData = {
    ...monthData,
    projects: visibleProjects,
    absenceTypes: visibleAbsenceTypes,
    rowPreferences: {
      configured: true,
      projects: visibleProjects.map((p, i) => ({
        projectId: p.projectId,
        projectCode: p.projectCode,
        projectName: p.projectName,
        sortOrder: i,
      })),
      absenceTypes: visibleAbsenceTypes.map((a, i) => ({
        type: a.type,
        label: a.label,
        fullDayOnly: a.fullDayOnly,
        sortOrder: i,
      })),
    },
  }
  return monthData.rowPreferences
}

function installFetchMock() {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    fetchLog.push(`${method} ${url}`)
    if (url.includes('/api/skema/emp001/month')) {
      return jsonResponse(monthData)
    }
    if (url.includes('/api/balance/emp001/summary')) {
      return jsonResponse(summaryData)
    }
    if (url.includes('/api/compliance/')) {
      return jsonResponse(COMPLIANCE_WITH_WARNING)
    }
    if (url.includes('/api/skema/emp001/save') && method === 'POST') {
      saveBodies.push(JSON.parse(String(init?.body)))
      return saveResponder ? saveResponder() : jsonResponse({})
    }
    if (url.includes('/api/skema/emp001/row-preferences') && method === 'PUT') {
      const body = JSON.parse(String(init?.body))
      putBodies.push(body)
      return jsonResponse(applyPrefsPut(body))
    }
    // S127 / TASK-12707 — `POST /api/approval/submit` is RETIRED. The month with no period sends
    // via `POST /api/approval/send` in ONE call, so `approveResponder` (the 422 injector) must hang
    // off THIS route as well: on the old two-call flow the gate rejection arrived on the second leg.
    if (url.includes('/api/approval/send') && method === 'POST') {
      return approveResponder ? approveResponder() : jsonResponse({ periodId: 'per-1', status: 'EMPLOYEE_APPROVED' })
    }
    if (url.includes('/employee-approve') && method === 'POST') {
      return approveResponder ? approveResponder() : jsonResponse({})
    }
    return jsonResponse({})
  })
}

beforeEach(() => {
  monthData = makeMonthData()
  summaryData = makeSummaryData()
  fetchLog = []
  saveBodies = []
  putBodies = []
  approveResponder = null
  saveResponder = null
  mockFetch.mockReset()
  installFetchMock()
})

afterEach(() => {
  vi.useRealTimers()
  document.body.style.pointerEvents = ''
  document.body.style.overflow = ''
})

// S143 / TASK-14303 — SkemaPage (and the REAL SkemaGrid this file renders under it) now read
// "today" via `useCalendarToday()` (`contexts/CalendarContext.tsx`), never `new Date()`. Every
// test below already sends an explicit `?year=2026&month=3`, which wins over the default (see
// `SkemaPageParamInit.test.tsx` for that precedence proof) — so which literal `DEFAULT_TODAY`
// carries is inert for all of them except the one AC-5 test that renders with NO url params.
const DEFAULT_TODAY = '2026-03-15'

function renderPage(url = '/tid/registrering?year=2026&month=3', today: string = DEFAULT_TODAY) {
  return renderWithCalendar(
    today,
    <MemoryRouter initialEntries={[url]}>
      <SkemaPage />
    </MemoryRouter>,
  )
}

async function renderLoaded(url?: string, today?: string) {
  const result = renderPage(url, today)
  await screen.findByText('Drift & support')
  // Settle await. History: the project-row LABELS render in the first commit (straight from
  // useSkema's `data`) while the diff total and every day-derived cell are computed from the
  // page's editable local cells/intervals. Until S147 / QUAL-241 those were seeded
  // by effects and landed in a SECOND commit, so a synchronous read right after the label
  // appeared raced it (the recurring R2/W1 flake). The page now copies `data` into local state
  // DURING RENDER, so the first commit already holds the hydrated state and no second commit
  // exists to race. This await is kept as a cheap settle guard (the other first-load effects —
  // balance and compliance fetches — still land after the label) and for the original
  // caveat: the default fixture's diff is -1,6; an empty/blank diff renders '' and a
  // blank-diff fixture must use renderPage directly rather than this helper.
  await waitFor(() =>
    expect(lastCell(gridRow(result.container, 'Diff. fra normtid')).textContent).not.toBe(''),
  )
  return result
}

/** Find a grid tbody row by the exact start of its label cell (the modal/drawer
    are body portals — `container` only holds the page tree, so these helpers
    never collide with overlay content). */
function gridRow(container: HTMLElement, label: string): HTMLElement {
  const row = gridRowOrNull(container, label)
  if (!row) throw new Error(`grid row "${label}" not found`)
  return row
}

function gridRowOrNull(container: HTMLElement, label: string): HTMLElement | null {
  const rows = Array.from(container.querySelectorAll('tbody tr'))
  return (rows.find((r) => r.querySelector('td')?.textContent?.startsWith(label)) as HTMLElement) ?? null
}

/** Row tds: [0] = label, [day] = day-of-month, [last] = trailing Sum/total. */
function dayCell(row: HTMLElement, day: number): HTMLElement {
  return row.querySelectorAll('td')[day] as HTMLElement
}

function lastCell(row: HTMLElement): HTMLElement {
  const cells = row.querySelectorAll('td')
  return cells[cells.length - 1] as HTMLElement
}

// "Ferie"/"Omsorgsdage"/… also appear as grid row labels — scope card queries to
// the strip's label elements (non-scoped CSS-module class names per vite.config).
function balanceCard(label: string): HTMLElement {
  const cardLabels = screen
    .getAllByText(label)
    .filter((el) => el.classList.contains('label'))
  expect(cardLabels).toHaveLength(1)
  return cardLabels[0].closest('[class*="card"]') as HTMLElement
}

describe('SkemaPage — page chrome (handoff §1)', () => {
  it('renders month nav, Administrer projekter, EXACTLY 4 balance cards, the kept ComplianceWarnings, the grid and the footer — with the legacy surfaces gone (R9)', async () => {
    const { container } = await renderLoaded()
    // Header row
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')
    expect(screen.getByRole('button', { name: /Forrige/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Næste/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Administrer projekter' })).toBeInTheDocument()
    // The 4-card strip — EXACTLY these four, in the strip (the grid also renders
    // absence-type labels, so card assertions go through the scoped helper)
    expect(balanceCard('Flex saldo')).toBeInTheDocument()
    expect(balanceCard('Ferie')).toBeInTheDocument()
    expect(balanceCard('Særlige feriedage')).toBeInTheDocument()
    expect(balanceCard('Omsorgsdage')).toBeInTheDocument()
    expect(container.querySelectorAll('[class*="card"] > [class*="label"]')).toHaveLength(4)
    expect(screen.queryByText('Normtimer')).toBeNull()
    expect(screen.queryByText('Merarbejde')).toBeNull()
    // ComplianceWarnings KEPT (R9)
    expect(screen.getByText('Arbejdstidskontrol')).toBeInTheDocument()
    // R9: the retired AllocationSummary heading is gone
    expect(screen.queryByText('Fordeling af arbejdstid')).toBeNull()
    // Grid + footer
    expect(gridRow(container, 'Diff. fra normtid')).toBeInTheDocument()
    expect(gridRow(container, 'Reg. Arbejdstid')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeInTheDocument()
  })

  it('navigates months and refetches (Næste → April 2026)', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('April 2026')
    await waitFor(() => {
      expect(fetchLog.some((e) => e.includes('/api/skema/emp001/month?year=2026&month=4'))).toBe(true)
    })
  })
})

describe('SkemaPage — R2 reconciliation', () => {
  it('R2/W1: the Flex card "Denne måned" equals the grid Diff trailing total, and BOTH render the ONE useSkema computation (no copy to drift)', async () => {
    const { container } = await renderLoaded()
    const diffTotal = lastCell(gridRow(container, 'Diff. fra normtid')).textContent
    expect(diffTotal).toBe('-1,6') // +2,1 (Mar 2) + 0,0 (Mar 3 full absence) − 3,7 (Mar 4)
    expect(balanceCard('Flex saldo').textContent).toContain(`Denne måned ${diffTotal} t`)
    // and never /summary's flexDelta (99,9 — the last-event delta)
    expect(balanceCard('Flex saldo').textContent).not.toContain('99,9')

    // W1 — the single source: the helper output over THIS fixture is what both
    // surfaces rendered (there is no second implementation to disagree with).
    const basis = deriveSkemaRowBasis(monthData)
    const cells = new Map<string, number>()
    for (const e of monthData.entries) cells.set(`${e.projectCode}:${e.date}`, e.hours)
    for (const a of monthData.absences) cells.set(`${a.absenceType}:${a.date}`, a.hours)
    const helperTotal = computeMonthDiffTotal(
      computeDayDiffs({
        year: 2026,
        month: 3,
        cellValues: cells,
        projectKeys: basis.projectKeys,
        absenceKeys: basis.absenceKeys,
        workIntervals: new Map(monthData.workTime.map((wt) => [wt.date, wt.intervals])),
        dailyNorm: new Map(monthData.dailyNorm.map((dn) => [dn.date, dn.hours])),
      }),
    )
    expect(helperTotal).toBe(-1.6)

    // W1 — the source pins: the grid consumes the useSkema diff owner, and the
    // card path (computeMonthFlexDelta) DELEGATES to the same helpers — neither
    // file carries its own copy of the arithmetic.
    expect(skemaGridSource).toMatch(/computeDayDiffs\(/)
    expect(skemaGridSource).toMatch(/computeMonthDiffTotal\(/)
    expect(useBalanceSummarySource).toMatch(/computeMonthDiffTotal\(computeDayDiffs\(inputs\)\)/)
  })
})

describe('SkemaPage — R10 / D-A balance cards', () => {
  it('R10: hours-first headlines = served days × fullDayNormAtMonthEnd; the Afholdt sub-line skips null-feriedage rows', async () => {
    await renderLoaded()
    const ferie = balanceCard('Ferie')
    expect(ferie.textContent).toContain('125,8') // 17 × 7,4
    expect(ferie.textContent).toContain('t tilbage')
    expect(ferie.textContent).toContain('17 dage')
    // hours sum BOTH served rows (7,4 + 3,7 = 11,1); days skip the null row → 1
    expect(ferie.textContent).toContain('Afholdt i marts 11,1 t · 1 dage')
    expect(balanceCard('Omsorgsdage').textContent).toContain('14,8') // 2 × 7,4
    expect(balanceCard('Flex saldo').textContent).toContain('Norm 7,4 t')
  })

  it('R10 fail-soft: a null fullDayNormAtMonthEnd em-dashes the hours headline while the days value still shows', async () => {
    monthData = makeMonthData({ fullDayNormAtMonthEnd: null })
    await renderLoaded()
    const ferie = balanceCard('Ferie')
    const headline = ferie.querySelector('[class*="num"]') as HTMLElement
    expect(headline.textContent).toBe('—')
    expect(ferie.textContent).toContain('17 dage')
    expect(ferie.textContent).not.toContain('125,8')
  })
})

describe('SkemaPage — R11/R16 manager modal wiring', () => {
  it('R11: the modal opens from the page header button', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    expect(await screen.findByText('Administrer rækker')).toBeInTheDocument()
  })

  it('R11: the modal opens from the day panel step-2 link', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')
    fireEvent.click(within(drawer).getByRole('button', { name: 'Administrer projekter' }))
    expect(await screen.findByText('Administrer rækker')).toBeInTheDocument()
  })

  it('R11/R16: a modal action FLUSHES the pending debounced save, PUTs, then refetches — in that order — and the grid updates live', async () => {
    const { container } = await renderLoaded()
    // A pending debounced cell edit (the 1s timer must NOT have fired)
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    // Modal action: remove the populated Drift row
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(putBodies.length).toBe(1))

    // Order: POST save (the FLUSH) → PUT row-preferences → GET month (refetch)
    const saveIdx = fetchLog.findIndex((e) => e.startsWith('POST') && e.includes('/save'))
    const putIdx = fetchLog.findIndex((e) => e.startsWith('PUT') && e.includes('/row-preferences'))
    expect(saveIdx).toBeGreaterThan(-1)
    expect(putIdx).toBeGreaterThan(saveIdx)
    await waitFor(() => {
      const refetchAfterPut = fetchLog.some(
        (e, i) => i > putIdx && e.startsWith('GET') && e.includes('/api/skema/emp001/month'),
      )
      expect(refetchAfterPut).toBe(true)
    })
    // The flush carried the pending cell — nothing was lost to the refetch
    expect(saveBodies[0].entries).toEqual([{ date: '2026-03-03', projectCode: 'UDV', hours: 2 }])
    // The PUT body is the dense full replacement minus Drift
    expect(putBodies[0].projects).toEqual([{ projectId: 'p-udv', sortOrder: 0 }])
    // Live grid update: the Drift row is gone, the hidden-rows affordance shows
    await waitFor(() => expect(gridRowOrNull(container, 'Drift & support')).toBeNull())
    expect(screen.getByText('1 skjulte rækker har timer i denne måned')).toBeInTheDocument()
  })

  it('R16/B2 in-flight: a FIRED debounce save whose POST is unresolved blocks the PUT until it resolves', async () => {
    await renderLoaded()
    // Hold the save POST open: the debounce will fire, the POST will leave, and
    // its promise stays unresolved until we release it.
    let releaseSave!: () => void
    saveResponder = () =>
      new Promise((resolve) => {
        releaseSave = () => resolve(jsonResponse({}))
      })

    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // the debounce FIRES — the POST leaves
    })
    vi.useRealTimers()
    expect(fetchLog.filter((e) => e.startsWith('POST') && e.includes('/save'))).toHaveLength(1)

    // Modal action while the POST is still in flight
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))

    // The PUT must NOT start while the save POST is unresolved
    await act(async () => {
      await new Promise((r) => setTimeout(r, 50))
    })
    expect(fetchLog.some((e) => e.startsWith('PUT'))).toBe(false)
    expect(putBodies).toHaveLength(0)

    // Release the POST → the PUT (and then the refetch) proceed, in order
    releaseSave()
    await waitFor(() => expect(putBodies).toHaveLength(1))
    const saveIdx = fetchLog.findIndex((e) => e.startsWith('POST') && e.includes('/save'))
    const putIdx = fetchLog.findIndex((e) => e.startsWith('PUT') && e.includes('/row-preferences'))
    expect(putIdx).toBeGreaterThan(saveIdx)
    await waitFor(() => {
      expect(
        fetchLog.some((e, i) => i > putIdx && e.startsWith('GET') && e.includes('/month')),
      ).toBe(true)
    })
  })

  it('R16/B2 flush-failure: a FAILED flush save ABORTS the preference write — NO PUT, NO refetch, the error surfaces, local edits intact', async () => {
    const { container } = await renderLoaded()
    const monthGets = () =>
      fetchLog.filter((e) => e.startsWith('GET') && e.includes('/month')).length
    const getsBefore = monthGets()
    saveResponder = () => jsonResponse({ error: 'boom' }, 500)

    // A pending debounced cell edit (the flush will fire — and fail)
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))

    // The error surfaces (the page's prefs-save alert) — no silent loss
    expect(
      await screen.findByText(/Kunne ikke gemme rækkeindstillingerne/),
    ).toBeInTheDocument()
    // NO PUT, NO refetch (a refetch would clobber the unsaved local edit)
    expect(fetchLog.some((e) => e.startsWith('PUT'))).toBe(false)
    expect(putBodies).toHaveLength(0)
    expect(monthGets()).toBe(getsBefore)
    // The local edit is intact, and the optimistic row change reverted
    expect(screen.getByLabelText('Udvikling dag 3')).toHaveValue('2')
    expect(gridRowOrNull(container, 'Drift & support')).not.toBeNull()
  })

  it('R16/B2 settled-failure (Step-7a c2): a save that FAILED and SETTLED before any flush is RETRIED by the flush — still-failing means NO PUT; recovered means the PUT proceeds', async () => {
    await renderLoaded()
    const savePosts = () =>
      fetchLog.filter((e) => e.startsWith('POST') && e.includes('/save')).length

    // 1) A debounced save fires, FAILS, and fully SETTLES before any flush —
    //    pre-fix this outcome was forgotten (no in-flight promise, no latch) and
    //    a later flush reported success over the never-persisted edit.
    saveResponder = () => jsonResponse({ error: 'boom' }, 500)
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    await waitFor(() => expect(savePosts()).toBe(1))

    // 2a) Modal action while the backend STILL fails: the flush retries the
    //     re-queued delta (a 2nd POST), fails again → NO PUT, the error surfaces.
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    expect(
      await screen.findByText(/Kunne ikke gemme rækkeindstillingerne/),
    ).toBeInTheDocument()
    expect(savePosts()).toBeGreaterThanOrEqual(2)
    expect(fetchLog.some((e) => e.startsWith('PUT'))).toBe(false)
    expect(putBodies).toHaveLength(0)

    // 2b) The backend recovers: the next action's flush retry SUCCEEDS → the
    //     PUT proceeds AFTER the successful save (order pinned).
    saveResponder = null
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Udvikling' }))
    await waitFor(() => expect(putBodies).toHaveLength(1))
    const lastSaveIdx = fetchLog.reduce(
      (acc, e, i) => (e.startsWith('POST') && e.includes('/save') ? i : acc),
      -1,
    )
    const putIdx = fetchLog.findIndex((e) => e.startsWith('PUT') && e.includes('/row-preferences'))
    expect(putIdx).toBeGreaterThan(lastSaveIdx)
  })

  it('R16/B2 overlapping-saves (Step-7a c3, rewritten for the serial queue): an OLDER save that FAILS while a NEWER edit is queued behind it retries from LIVE state — the stale value never resurrects', async () => {
    // Since fix-up 2 saves go one at a time, so the original "B succeeded BEFORE A failed" order
    // can no longer happen; what remains is: A (2) in flight, B (3) queued behind it, A fails,
    // B then leaves — and no retry may carry the stale 2.
    // Red condition (for the rewrite): a retry that re-queues A's CAPTURED delta instead of the
    // live value would send {UDV, 2026-03-03, 2} after B's 3 — the assertion at the end fails.
    await renderLoaded()
    const saveBodies = (): { hours: number }[][] =>
      mockFetch.mock.calls
        .filter(([url]) => String(url).includes('/save'))
        .map(([, init]) => JSON.parse(String((init as RequestInit).body)).entries ?? [])

    // 1) Save A leaves with the OLD value (2) and is HELD unresolved.
    let releaseAAsFailure!: () => void
    saveResponder = () =>
      new Promise((resolve) => {
        releaseAAsFailure = () => resolve(jsonResponse({ error: 'boom' }, 500))
      })
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })

    // 2) Save B (the NEW value, 3) is QUEUED behind A (one save at a time) and leaves when A settles.
    saveResponder = null
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '3' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()

    // 3) A settles as FAILURE (B now leaves) — its retry must rebuild from LIVE state (3),
    //    never re-queue the stale captured 2 (pre-fix it did).
    releaseAAsFailure()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 20))
    })

    // 4) A modal action flushes: the retry (if fired) carries the LIVE value.
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(putBodies).toHaveLength(1))

    // No save fired AFTER the second edit may carry the stale 2 for that cell.
    const bodiesAfterB = saveBodies().slice(2)
    for (const entries of bodiesAfterB) {
      for (const e of entries as { projectCode?: string; date?: string; hours: number }[]) {
        if (e.projectCode === 'UDV' && e.date === '2026-03-03') {
          expect(e.hours).toBe(3)
        }
      }
    }
  })
})

describe('SkemaPage — R3 visibility-independence end-to-end', () => {
  it('R3: hiding a populated project leaves Diff / I alt / the ✓ state unchanged, shows the affordance, and the approval-gate 422 surface still renders', async () => {
    const { container } = await renderLoaded()
    const diffBefore = lastCell(gridRow(container, 'Diff. fra normtid')).textContent
    const totalBefore = lastCell(gridRow(container, 'I alt')).textContent
    expect(totalBefore).toBe('20,6')
    expect(dayCell(gridRow(container, 'Reg. Arbejdstid'), 2).textContent).toBe('✓')

    // Hide Drift (it carries 7,4 h on Mar 2) via the modal flow
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))

    // Wait past the OPTIMISTIC frame: the PUT must have landed AND the refetch
    // must have served the post-PUT month (where the legacy `projects` field is
    // the shrunken VISIBLE selection — the 7201 contract; the Step-7a B1 pin
    // breaks pre-fix exactly here), then settle the state into React.
    await waitFor(() => expect(putBodies.length).toBe(1))
    const putIdx = fetchLog.findIndex((e) => e.startsWith('PUT'))
    await waitFor(() =>
      expect(
        fetchLog.some((e, i) => i > putIdx && e.startsWith('GET') && e.includes('/month')),
      ).toBe(true),
    )
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0))
    })
    expect(gridRowOrNull(container, 'Drift & support')).toBeNull()

    // ALL computed values are visibility-independent (rendering filter only) —
    // asserted against the SETTLED server-truth state, not the optimistic frame
    expect(lastCell(gridRow(container, 'Diff. fra normtid')).textContent).toBe(diffBefore)
    expect(lastCell(gridRow(container, 'I alt')).textContent).toBe(totalBefore)
    expect(dayCell(gridRow(container, 'Reg. Arbejdstid'), 2).textContent).toBe('✓')
    expect(screen.getByText('1 skjulte rækker har timer i denne måned')).toBeInTheDocument()

    // Close the modal, then the employee-approve gate's 422 surface still renders
    fireEvent.click(screen.getByRole('button', { name: 'Færdig' }))
    approveResponder = () =>
      jsonResponse(
        {
          kind: 'allocation',
          unbalancedDays: [{ date: '2026-03-05', worked: 5, allocated: 3, direction: 'under' }],
        },
        422,
      )
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    expect(
      await screen.findByText(/Fordel de resterende 2 t på projekter/),
    ).toBeInTheDocument()
  })

  it('R3/B1: a DEACTIVATED project with historical hours (in entries, absent from catalogs AND the visible selection) still feeds Diff / I alt / the work row, and counts as a hidden row', async () => {
    const base = makeMonthData()
    monthData = makeMonthData({
      // GAMMEL: 4 h on Mar 5 — served history for a project no catalog knows
      entries: [...base.entries, { date: '2026-03-05', projectCode: 'GAMMEL', hours: 4 }],
    })
    const { container } = await renderLoaded()

    // Not rendered (not in the visible selection)…
    expect(gridRowOrNull(container, 'GAMMEL')).toBeNull()
    // …but the hidden-rows affordance counts it (it carries hours this month)
    expect(screen.getByText('1 skjulte rækker har timer i denne måned')).toBeInTheDocument()
    // I alt spans it: 20,6 + 4 = 24,6
    expect(lastCell(gridRow(container, 'I alt')).textContent).toBe('24,6')
    // The work row sees its allocation on Mar 5 (worked 0, allocated 4 → over)
    expect(dayCell(gridRow(container, 'Reg. Arbejdstid'), 5).textContent).toBe('+4,0')
    // Diff: Mar 5 now has a registration → 0 + 0 − 7,4 = −7,4; total −1,6 − 7,4 = −9,0
    expect(lastCell(gridRow(container, 'Diff. fra normtid')).textContent).toBe('-9,0')
    // …and the Flex card reconciles (R2 — the same single computation)
    expect(balanceCard('Flex saldo').textContent).toContain('Denne måned -9,0 t')
  })
})

describe('SkemaPage — R16 day-panel save paths', () => {
  it('R16: a panel period edit round-trips through the hook buildWorkTimePayload as { date, intervals }', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')

    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // the 1s debounce
    })
    vi.useRealTimers()

    expect(saveBodies.length).toBe(1)
    expect(saveBodies[0].entries).toBeNull()
    expect(saveBodies[0].absences).toBeNull()
    // The edited interval alone: manual hours are retired (ADR-043 D10)
    expect(saveBodies[0].workTime).toEqual([
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '17:00' }] },
    ])
  })

  it('a panel allocation edit rides the existing debounced cell-save path', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')

    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Drift & support'), { target: { value: '5' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()

    expect(saveBodies.length).toBe(1)
    expect(saveBodies[0].entries).toEqual([{ date: '2026-03-02', projectCode: 'DRIFT', hours: 5 }])
    expect(saveBodies[0].workTime).toBeNull()
  })

  it('the panel\'s allocations span ALL SERVED projects — a hidden populated row still counts toward Resterende (the recorded 7203 pin)', async () => {
    // Drift is HIDDEN by preferences from the start but carries 7,4 h on Mar 2.
    // Per the 7201 contract the legacy `projects` field serves ONLY the visible
    // selection — Drift reaches the page through `catalogs` ∪ entries (B1).
    monthData = makeMonthData({
      projects: [
        { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
      ],
      rowPreferences: {
        configured: true,
        projects: [{ projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 0 }],
        absenceTypes: [
          { type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 },
          { type: 'CARE_DAY', label: 'Omsorgsdage', fullDayOnly: false, sortOrder: 1 },
          { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', fullDayOnly: false, sortOrder: 2 },
        ],
      },
    })
    const { container } = renderPage()
    await screen.findByText('Udvikling')
    // S147 / QUAL-241 — the 'Udvikling' row appears in the FIRST commit with data. Before the
    // QUAL-241 fix the page's local state was copied from that data by effects a render LATER,
    // so clicking the day trigger on that first commit opened a panel with no periods
    // ("Arbejdstid i alt 1,5 t" against 9,5 t allocated → "Overfordelt"): this test's
    // "documented flake" (S121 flake-watch, 1 in 32 under parallel load) was that product race,
    // not test noise. The race is now closed in the page and pinned deterministically by the
    // QUAL-241 tests below. This test no longer relies on timing at all: the barrier is the
    // SERVED PERIOD ITSELF — the panel must show the day's served 08:00–17:30 before anything
    // is asserted. That is non-vacuous (an unseeded panel has no such field and the assertion
    // would time out), unlike a blank-Diff wait, which an allocation-only state also satisfies.
    await waitFor(() =>
      expect(lastCell(gridRow(container, 'Diff. fra normtid')).textContent).not.toBe(''),
    )
    expect(gridRowOrNull(container, 'Drift & support')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')

    // S125 / S147: await the SERVED PERIOD before asserting anything. History: this test carried
    // the "Alt fordelt ✓" async-settle flake (S121 flake-watch, red in CI 2026-07-30) — the dialog
    // role appears before the panel holds the day's data, so a synchronous query ran too early.
    // Neither the "Alt fordelt ✓" text (classifyAllocation(0, 0) is balanced, so it also renders
    // for the empty state — it could resolve vacuously) nor the 'Udvikling' row (served with the
    // first data commit, whatever the periods) proves the periods arrived. The served 08:00 /
    // 17:30 do: an unseeded panel has no such field and the wait would time out.
    expect(await within(drawer).findByDisplayValue('08:00')).toBeInTheDocument()
    expect(within(drawer).getByDisplayValue('17:30')).toBeInTheDocument()
    expect(await within(drawer).findByLabelText('Udvikling')).toBeInTheDocument()

    // Step 2 renders only the VISIBLE rows — now checked against a hydrated panel, where an
    // "is absent" assertion means something.
    expect(within(drawer).queryByLabelText('Drift & support')).toBeNull()

    // …and Resterende computes over ALL served allocations: 9,5 worked − (7,4 hidden Drift +
    // 2,1 visible Udvikling) = balanced. Asserted AFTER the barrier, so a balanced-looking zero
    // state cannot satisfy it.
    expect(within(drawer).getByText('Alt fordelt ✓')).toBeInTheDocument()
  })
})

describe('SkemaPage — QUAL-241: no day panel or edit starts from state not copied from the current data', () => {
  it('F: a day opened on the very FIRST commit that shows the grid still carries the served work periods', async () => {
    // Red condition: against the pre-QUAL-241 page the grid committed from `data` while the local
    // work-interval state was still empty (three effects copied it a render later), so a click
    // landing in that gap seeded the panel with no periods, and nothing ever refreshed them.
    //
    // Why this hits the gap reliably in practice rather than 1 in 32 (React 18.3, jsdom): the data
    // arrives from a fetch promise, so its setState is a DefaultLane update, rendered and
    // committed in one Scheduler task; the passive effects (and the second render they cause) are
    // queued for a LATER Scheduler task. A MutationObserver callback is a microtask queued by the
    // commit's DOM mutation, so it runs right after the commit. This is NOT a logical
    // guarantee: React's scheduler works in ~5 ms slices and could in principle drain the passive
    // effects within the same slice if the render and commit were that quick. The first data commit
    // of this page (the whole grid) takes far longer than a slice, which is why the pin is
    // dependable: it failed 6 of 6 runs against the pre-QUAL-241 page (96cd31e6) and is green on
    // every run of the fix. The act environment is switched OFF around the wait because this is
    // not an RTL async utility: with it on, React warns about updates outside act().
    const globals = globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }
    const previousActEnv = globals.IS_REACT_ACT_ENVIRONMENT
    globals.IS_REACT_ACT_ENVIRONMENT = false
    renderPage()
    try {
      await new Promise<void>((resolve) => {
        const observer = new MutationObserver(() => {
          const trigger = screen.queryByRole('button', { name: 'Registrér arbejdstid 2026-03-02' })
          if (!trigger) return
          observer.disconnect()
          fireEvent.click(trigger)
          resolve()
        })
        observer.observe(document.body, { childList: true, subtree: true })
      })
    } finally {
      globals.IS_REACT_ACT_ENVIRONMENT = previousActEnv
    }
    const drawer = await screen.findByRole('dialog')
    // The served 08:00–17:30 period must be in the panel (findBy: allow the settle, but an
    // unseeded panel never shows it, so this fails reliably before the fix).
    expect(await within(drawer).findByDisplayValue('08:00')).toBeInTheDocument()
    expect(within(drawer).getByDisplayValue('17:30')).toBeInTheDocument()
    expect(within(drawer).queryByText('Overfordelt')).toBeNull()
  })

  it('F: while a NEW month is loading, the previous month\'s data is not editable and its days open no panel; once it lands the panel seeds from the new month', async () => {
    // Red condition: pre-fix, useSkema keeps the old month's `data` (and the page its local
    // state) on screen under the new month's day columns until the response lands, and the
    // day triggers stayed live — a panel opened then saw no periods of the new month, and
    // an edit was overwritten (and its save dropped) when the new data arrived.
    await renderLoaded()
    const april = makeMonthData({
      month: 4,
      daysInMonth: 30,
      entries: [],
      absences: [],
      dailyNorm: [],
      workTime: [{ date: '2026-04-01', intervals: [{ start: '09:00', end: '12:00' }] }],
    })
    let release!: () => void
    const gate = new Promise<void>((r) => { release = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month') && String(url).includes('month=4')) {
        await gate
        return jsonResponse(april)
      }
      return base(url, init)
    })

    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    // In flight: April's columns are showing over March's data — not actionable.
    expect(await screen.findByText(/April 2026/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })).toBeNull()
    // …and the lock is announced (quietly): aria-busy on the page region + an "Indlæser…" status.
    expect(document.querySelector('[aria-busy="true"]')).not.toBeNull()
    expect(screen.getByRole('status')).toHaveTextContent('Indlæser…')

    await act(async () => { release(); await gate })
    const trigger = await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    fireEvent.click(trigger)
    const drawer = await screen.findByRole('dialog')
    expect(within(drawer).getByDisplayValue('09:00')).toBeInTheDocument()
    expect(within(drawer).getByDisplayValue('12:00')).toBeInTheDocument()
    expect(document.querySelector('[aria-busy="true"]')).toBeNull()
  })

  // ── fix-up 1 ──
  /** Serve an April fixture for every month request from now on (the default mock serves the
      March fixture whatever month is asked for, which would itself look like a month mismatch). */
  function serveApril(overrides: Partial<SkemaMonthData> = {}) {
    monthData = makeMonthData({
      month: 4,
      daysInMonth: 30,
      entries: [],
      absences: [],
      dailyNorm: [],
      workTime: [],
      ...overrides,
    })
  }

  /** Hold the April month response until released (everything else is the normal mock). */
  function holdAprilFetch(response?: () => unknown) {
    let release!: () => void
    const gate = new Promise<void>((r) => { release = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month') && String(url).includes('month=4')) {
        await gate
        return response ? response() : base(url, init)
      }
      return base(url, init)
    })
    return release
  }

  it('F: a period edit followed by "Næste" inside the 1 s debounce is SAVED for the month it was made in — the stored day is not replaced by an empty one', async () => {
    // Red condition: against e9195bcf the debounce timer fires after April has hydrated, builds
    // the payload from refs that no longer hold 2026-03-02 and sends
    // {date: 2026-03-02, intervals: []} — the whole-day replace wipes the stored
    // 08:00–17:30. (Against the original code the same race ends the same way.)
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    serveApril()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // inside the debounce
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' }) // April landed
    await waitFor(() => expect(saveBodies.length).toBeGreaterThan(0), { timeout: 3000 })
    await act(async () => { await new Promise((r) => setTimeout(r, 1300)) }) // let any late timer fire
    expect(saveBodies).toHaveLength(1)
    expect(saveBodies[0].month).toBe(3)
    expect(saveBodies[0].workTime).toEqual([
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '17:00' }] },
    ])
  })

  it('F: a typed cell value followed by "Næste" inside the debounce survives as a save for its own month', async () => {
    // Red condition: against e9195bcf the hydration effect cleared the pending cell delta, so
    // the typed 2 h was silently dropped — no save is ever sent (the waitFor times out).
    await renderLoaded()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    serveApril()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    await waitFor(() => expect(saveBodies.length).toBeGreaterThan(0), { timeout: 3000 })
    expect(saveBodies[0].month).toBe(3)
    expect(saveBodies[0].entries).toEqual([{ date: '2026-03-03', projectCode: 'UDV', hours: 2 }])
  })

  it('F: a FAILED month fetch leaves the old month locked (no editable grid under the new month) and says so', async () => {
    // Red condition: against e9195bcf the lock required `loading`, which drops to false on a
    // failed fetch while the old month's data stays — the grid unlocked under April's columns,
    // and the error was shown only when there was no data at all.
    await renderLoaded()
    const release = holdAprilFetch(() => jsonResponse({ error: 'boom' }, 500))
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await act(async () => { release() })
    expect(await screen.findByText(/Kunne ikke indlæse skema/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })).toBeNull()
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull()
    expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeDisabled()
  })

  it('F: while the previous month is still held, approve is disabled', async () => {
    // Red condition: against e9195bcf "Godkend måned" stayed enabled and silently did nothing.
    await renderLoaded()
    const release = holdAprilFetch()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeDisabled()
    serveApril()
    await act(async () => { release() })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeEnabled())
  })

  it('F: while the previous month is still held, "Genåbn" is disabled and posts nothing', async () => {
    // Red condition: against e9195bcf "Genåbn" stayed enabled and reopened the PREVIOUS
    // month's period (its periodId) while the heading showed the next month.
    monthData = makeMonthData({
      approval: {
        periodId: 'per-1',
        status: 'EMPLOYEE_APPROVED',
        employeeDeadline: null,
        managerDeadline: null,
        employeeApprovedAt: '2026-04-01T08:00:00Z',
        rejectionReason: null,
      },
    })
    await renderLoaded()
    const release = holdAprilFetch()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    const reopen = screen.getByRole('button', { name: 'Genåbn' })
    expect(reopen).toBeDisabled()
    fireEvent.click(reopen)
    // Barrier: let the held April load complete (and hydrate), THEN count — anything the click
    // could have queued has had every chance to go out by then.
    serveApril()
    await act(async () => { release() })
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    expect(fetchLog.some((e) => e.includes('POST') && e.includes('reopen'))).toBe(false)
  })

  it('F: edits are locked for the whole row-preference sequence (flush → PUT → refetch), so the refetch cannot overwrite them', async () => {
    // Red condition: against e9195bcf the grid stayed editable while the PUT/refetch was in
    // flight, and the refetched month then overwrote whatever was typed in that window.
    await renderLoaded()
    let releasePut!: () => void
    const putGate = new Promise<void>((r) => { releasePut = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/row-preferences') && init?.method === 'PUT') await putGate
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull()) // locked
    await act(async () => { releasePut() })
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument())
  })

  it('F: saves go one at a time — a 422 on a NEWER cell edit reverts to the value the OLDER save established, and the newer POST waits for the older one', async () => {
    // Red condition (fix-up 2 root fix; the fix-up 1 form of this pin was inherited QUAL, Codex
    // WARNING 2 / Opus NOTE 1): against ed884d6f both POSTs leave at once, so held has length 2
    // after the second debounce — and a newer save's 422 could be processed before the older
    // save's success, resetting the screen to the pre-save value while the server holds A's.
    await renderLoaded()
    const held: Array<(r: unknown) => void> = []
    saveResponder = () => new Promise((resolve) => { held.push(resolve) })
    vi.useFakeTimers()
    const input = screen.getByLabelText('Udvikling dag 3')
    fireEvent.change(input, { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // save A leaves
    fireEvent.change(input, { target: { value: '4' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // save B is QUEUED
    vi.useRealTimers()
    expect(held).toHaveLength(1) // B has not left: one save in flight at a time
    await act(async () => { held[0](jsonResponse({})) }) // A ok → B leaves
    await waitFor(() => expect(held).toHaveLength(2))
    await act(async () => {
      held[1](jsonResponse({
        error: 'absence_full_day_only', absenceType: 'CARE_DAY', date: '2026-03-03', requiredHours: 7.4, message: 'x',
      }, 422))
    }) // B rejected
    fireEvent.blur(input)
    await waitFor(() => expect((screen.getByLabelText('Udvikling dag 3') as HTMLInputElement).value).toBe('2'))
  })

  it('F: the same for a work-time day — the newer period save waits, and its 422 reverts to the OLDER save\'s period', async () => {
    // Red condition: against ed884d6f held has length 2 (both POSTs in flight at once).
    await renderLoaded()
    const held: Array<(r: unknown) => void> = []
    saveResponder = () => new Promise((resolve) => { held.push(resolve) })
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    let drawer = await screen.findByRole('dialog')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // A leaves
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '18:00' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // B queued
    vi.useRealTimers()
    expect(held).toHaveLength(1)
    await act(async () => { held[0](jsonResponse({})) })
    await waitFor(() => expect(held).toHaveLength(2))
    await act(async () => {
      held[1](jsonResponse({ error: 'absence_full_day_only', message: 'work-time rejected' }, 422))
    })
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    drawer = await screen.findByRole('dialog')
    await waitFor(() => expect((within(drawer).getByLabelText('Til') as HTMLInputElement).value).toBe('17:00'))
  })

  it('F: two overlapping row-preference actions run one after the other and keep edits locked until the LAST one has finished', async () => {
    // Red conditions: against ed884d6f the lock was a boolean released when the FIRST sequence
    // finished; against a5a84093 both PUTs were on the wire at once (putGates has length 2 before
    // anything is released), so the final row set depended on arrival order.
    await renderLoaded()
    const putGates: Array<() => void> = []
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/row-preferences') && init?.method === 'PUT') {
        await new Promise<void>((r) => { putGates.push(r) })
      }
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Udvikling' }))
    await waitFor(() => expect(putGates.length).toBeGreaterThanOrEqual(1))
    await act(async () => { await new Promise((r) => setTimeout(r, 20)) })
    expect(putGates).toHaveLength(1) // the second sequence waits for its turn
    expect(screen.queryByLabelText('Ferie dag 5')).toBeNull() // locked
    await act(async () => { putGates[0]() }) // the FIRST PUT answers…
    // …barrier: the second PUT only leaves after the first sequence (PUT + refetch) has finished.
    await waitFor(() => expect(putGates).toHaveLength(2))
    expect(screen.queryByLabelText('Ferie dag 5')).toBeNull() // still locked: one sequence remains
    await act(async () => { putGates[1]() })
    await waitFor(() => expect(screen.getByLabelText('Ferie dag 5')).toBeInTheDocument())
  })

  it('F: approving March and then navigating before the send returns — the reload lands on APRIL and the page is not left locked', async () => {
    // Red condition: against ed884d6f the post-send reload targeted the month the click was made
    // in (March), superseded April's own load, and left March's data under "April 2026" with the
    // page silently locked (no busy cue, no error).
    await renderLoaded()
    const marchData = monthData
    const aprilData = makeMonthData({ month: 4, daysInMonth: 30, entries: [], absences: [], dailyNorm: [], workTime: [] })
    const base = mockFetch.getMockImplementation()!
    let releaseSend!: () => void
    const sendGate = new Promise<void>((r) => { releaseSend = r })
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month')) {
        fetchLog.push(`${init?.method ?? 'GET'} ${url}`)
        return jsonResponse(String(url).includes('month=4') ? aprilData : marchData)
      }
      if (String(url).includes('/api/approval/send')) {
        fetchLog.push(`POST ${url}`)
        await sendGate
        return jsonResponse({ periodId: 'per-1', status: 'EMPLOYEE_APPROVED' })
      }
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    await waitFor(() => expect(fetchLog.some((e) => e.includes('/api/approval/send'))).toBe(true))
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await act(async () => { releaseSend() })
    expect(await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('April 2026')
    expect(document.querySelector('[aria-busy="true"]')).toBeNull()
  })

  it('F: if a stray load leaves the wrong month under the title, the page loads the month on screen (once)', async () => {
    // Red condition: against ed884d6f nothing recovers — March's data stays under April, locked.
    await renderLoaded()
    const aprilData = makeMonthData({ month: 4, daysInMonth: 30, entries: [], absences: [], dailyNorm: [], workTime: [] })
    const marchData = monthData
    const base = mockFetch.getMockImplementation()!
    let aprilRequests = 0
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month') && String(url).includes('month=4')) {
        aprilRequests += 1
        return jsonResponse(aprilRequests === 1 ? marchData : aprilData) // the first answer is the wrong month
      }
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    expect(await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })).toBeInTheDocument()
    expect(aprilRequests).toBe(2)
  })

  it('F: approve / send lock editing for the whole flush → request → reload sequence', async () => {
    // Red condition: against ed884d6f the grid stayed editable while the send was in flight, and
    // the reload then overwrote whatever was typed.
    await renderLoaded()
    let releaseSend!: () => void
    const sendGate = new Promise<void>((r) => { releaseSend = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/approval/send')) {
        fetchLog.push(`POST ${url}`)
        await sendGate
        return jsonResponse({ periodId: 'per-1', status: 'EMPLOYEE_APPROVED' })
      }
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    await waitFor(() => expect(fetchLog.some((e) => e.includes('/api/approval/send'))).toBe(true))
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull()
    await act(async () => { releaseSend() })
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument())
  })

  it('F: leaving the page sends the pending edits instead of dropping them', async () => {
    // Red condition: against ed884d6f the unmount cleanup only cleared the timers, so a value
    // typed less than a second before leaving was never saved.
    const { unmount } = await renderLoaded()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    unmount()
    await waitFor(() => expect(saveBodies.length).toBe(1), { timeout: 500 })
    expect(saveBodies[0].entries).toEqual([{ date: '2026-03-03', projectCode: 'UDV', hours: 2 }])
  })

  it('F: a failed SAVE shows a save alert (not "Kunne ikke indlæse skema"), and the next successful save clears it', async () => {
    // Red condition: against ed884d6f the save failure was stored in the load-error field, so it
    // rendered as "Kunne ikke indlæse skema: …" with no way to dismiss it.
    await renderLoaded()
    saveResponder = () => jsonResponse({ error: 'boom' }, 500)
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    expect(await screen.findByText(/Kunne ikke gemme \(marts 2026\):/)).toBeInTheDocument()
    expect(screen.queryByText(/Kunne ikke indlæse skema/)).toBeNull()
    saveResponder = null
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '3' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) })
    vi.useRealTimers()
    await waitFor(() => expect(screen.queryByText(/Kunne ikke gemme/)).toBeNull())
  })

  it('F: a failed reopen shows its own dismissible alert in Danish', async () => {
    // Red condition: against ed884d6f the reopen failure was stored in the load-error field and
    // rendered as "Kunne ikke indlæse skema: …" (undismissible); against a5a84093 the alert lacks
    // the month it belongs to ("(marts 2026)").
    monthData = makeMonthData({
      approval: {
        periodId: 'per-1',
        status: 'EMPLOYEE_APPROVED',
        employeeDeadline: null,
        managerDeadline: null,
        employeeApprovedAt: '2026-04-01T08:00:00Z',
        rejectionReason: null,
      },
    })
    await renderLoaded()
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/reopen')) return jsonResponse({ error: 'nej' }, 500)
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Genåbn' }))
    expect(await screen.findByText(/Kunne ikke genåbne måneden \(marts 2026\):/)).toBeInTheDocument()
    expect(screen.queryByText(/Kunne ikke indlæse skema/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Luk besked' }))
    expect(screen.queryByText(/Kunne ikke genåbne måneden/)).toBeNull()
  })

  it('F: the status region is mounted from the start (empty), gets its text while a month loads, and sits outside the aria-busy area', async () => {
    // Red condition: against ed884d6f the status element was inserted already filled and sat
    // INSIDE the aria-busy region.
    await renderLoaded()
    const status = screen.getByRole('status')
    expect(status).toHaveTextContent('')
    const release = holdAprilFetch()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    expect(screen.getByRole('status')).toBe(status) // same element, text set
    expect(status).toHaveTextContent('Indlæser…')
    const busy = document.querySelector('[aria-busy="true"]')
    expect(busy).not.toBeNull()
    expect(busy!.contains(status)).toBe(false)
    serveApril()
    await act(async () => { release() })
    await waitFor(() => expect(status).toHaveTextContent(''))
  })
})

describe('SkemaPage — QUAL-241 fix-up 3: envelopes, ordering, locks', () => {
  /** A month-aware mock: March answers from `march` (mutable), April from `april`. */
  function monthAwareMock(march: () => SkemaMonthData, april: () => SkemaMonthData) {
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month')) {
        fetchLog.push(`${init?.method ?? 'GET'} ${url}`)
        return jsonResponse(JSON.parse(JSON.stringify(String(url).includes('month=4') ? april() : march())))
      }
      return base(url, init)
    })
  }
  const aprilFixture = (overrides: Partial<SkemaMonthData> = {}) =>
    makeMonthData({ month: 4, daysInMonth: 30, entries: [], absences: [], dailyNorm: [], workTime: [], ...overrides })

  it('F: a save that FAILS after its month has left the screen is NOT resent, the alert names the month and day, and returning shows the server value', async () => {
    // Red condition: against 0974b286 the failed row was RETAINED and replayed at the next
    // replay point — the "Forrige" navigation back to March below, which sent the retained row —
    // so a second POST left (saveBodies would have length 2), resurrecting a value the server had
    // not accepted and overwriting what the server held for that month meanwhile. The later
    // preference action's flush is a second check that nothing is replayed there either.
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    let n = 0
    saveResponder = () => (++n === 1 ? jsonResponse({ error: 'offline' }, 500) : jsonResponse({}))
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // fires the save (fails)
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    const alert = await screen.findByText(/Kunne ikke gemme \(marts 2026\):/)
    expect(alert).toHaveTextContent('Ændringerne er ikke gemt')
    expect(alert).toHaveTextContent('3. marts')
    expect(saveBodies).toHaveLength(1)
    // Return to March: the server's value (nothing saved), not the dropped edit.
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ }))
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()) // hydrated
    const shown = () => {
      const el = screen.getByLabelText('Udvikling dag 3') as HTMLInputElement
      fireEvent.blur(el)
      return (screen.getByLabelText('Udvikling dag 3') as HTMLInputElement).value
    }
    expect(shown()).toBe('')
    // A later flush point (a preference action) must not replay it. Barrier: the grid unlocks
    // again only after the whole sequence (flush → PUT → refetch) has finished.
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(putBodies).toHaveLength(1))
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument())
    expect(saveBodies).toHaveLength(1)
    expect(shown()).toBe('')
  })

  it('F: a failed-save alert for one month is NOT cleared by a success in another month — only by a success in the SAME month', async () => {
    // Red condition: against 0974b286 any successful save cleared it, while its month's edit
    // was still unsaved.
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    let n = 0
    saveResponder = () => (++n === 1 ? jsonResponse({ error: 'offline' }, 500) : jsonResponse({}))
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    await screen.findByText(/Kunne ikke gemme \(marts 2026\):/)
    // A successful save in APRIL…
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ })) // fires April's save, then loads March
    await waitFor(() => expect(saveBodies.length).toBe(2))
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()) // March hydrated: April's save has settled (loads wait for the queue)
    expect(saveBodies[1].month).toBe(4)
    // …leaves March's alert in place.
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toBeInTheDocument()
    // A successful save in MARCH clears it.
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '4' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await waitFor(() => expect(saveBodies.length).toBe(3))
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    expect(screen.queryByText(/Kunne ikke gemme \(marts 2026\):/)).toBeNull()
  })

  it('F: a save that never answers does not hold Godkend forever — the flush gives up after the bound, releases the lock and says so', async () => {
    // Red condition: against 0974b286 the flush awaited the hung POST with no bound, so the busy
    // lock (and Godkend) stayed held for the life of the page.
    await renderLoaded()
    saveResponder = () => new Promise(() => {}) // never settles
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // the save leaves and hangs
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    await act(async () => { await vi.advanceTimersByTimeAsync(100) })
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull() // locked while the flush waits
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) })
    vi.useRealTimers()
    expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument() // lock released
    expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeEnabled()
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toHaveTextContent('Det er uvist, om ændringerne blev gemt')
    expect(fetchLog.some((e) => e.startsWith('POST') && e.includes('/api/approval/send'))).toBe(false)
  })

  it('F: edit and click Godkend BEFORE the debounce, the POST hangs — the flush gives up after the bound, releases the lock and says "uvist" (the whole flush is bounded)', async () => {
    // Red condition: against 782e2285 the bound covered only the in-flight set taken BEFORE the
    // flush fired its own save; `await fireCellSave(pending)` never returned, so the page stayed
    // locked on "Gemmer…" with no alert (this test then fails on the lock/alert assertions).
    await renderLoaded()
    saveResponder = () => new Promise(() => {}) // never settles
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' })) // before the 1 s debounce
    await act(async () => { await vi.advanceTimersByTimeAsync(100) })
    expect(saveBodies).toHaveLength(1) // the flush itself fired the save, and it hangs
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull() // locked while the flush waits
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) })
    vi.useRealTimers()
    expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument() // lock released
    expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeEnabled()
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toHaveTextContent('Det er uvist, om ændringerne blev gemt')
    expect(fetchLog.some((e) => e.startsWith('POST') && e.includes('/api/approval/send'))).toBe(false)
    expect(saveBodies).toHaveLength(1) // nothing replayed
  })

  it('F: a month load that gives up waiting for a hung save says "uvist" (here the hung save\'s month and the month on screen coincide, so the naming itself is pinned by the flush test below)', async () => {
    // Red condition: against 782e2285 the load's wait gave up silently: April simply appeared and
    // nothing told the person that March's last save may not be on the server (no alert at all).
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    saveResponder = () => new Promise(() => {}) // March's save never answers
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // the save leaves and hangs
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) }) // the load gives up waiting
    vi.useRealTimers()
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' }) // April loaded
    const alert = screen.getByText(/Kunne ikke gemme \(marts 2026\):/)
    expect(alert).toHaveTextContent('Serveren svarede ikke inden for 15 sekunder')
    expect(alert).toHaveTextContent('Det er uvist, om ændringerne blev gemt')
  })

  it('F: a retry that HANGS after an in-flight save failed is bounded too — "uvist" after the bound, the lock released', async () => {
    // Red condition: against c51a2012 `settledOk && (await fired)` skipped awaiting the retry
    // when the in-flight save had failed, so the flush returned at once: no "uvist", and the
    // hung retry's tracking was never released (the first assertion after Godkend's wait fails:
    // the grid is already unlocked and the alert says "Ændringerne er ikke gemt").
    // Red condition (fix-up 2): against cb05d2dc `reportActionError` overwrote the slot, so the
    // "uvist" notice replaced S1's "ikke gemt" and the two-notice assertion fails.
    await renderLoaded()
    const held: Array<(r: unknown) => void> = []
    let n = 0
    saveResponder = () => (++n === 1 ? new Promise((resolve) => { held.push(resolve) }) : new Promise(() => {}))
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // S1 leaves (held)
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' })) // flush waits for S1
    await act(async () => { await vi.advanceTimersByTimeAsync(100) })
    await act(async () => { held[0](jsonResponse({ error: 'offline' }, 500)) }) // S1 fails → the flush retries; the retry hangs
    await act(async () => { await vi.advanceTimersByTimeAsync(100) })
    expect(saveBodies).toHaveLength(2)
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull() // still locked, waiting for the retry
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) })
    vi.useRealTimers()
    expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()
    // Two notices: "uvist" (the hung retry) and S1's own "ikke gemt", which the "uvist" report
    // moved out of the single slot instead of overwriting it.
    const texts = screen.getAllByText(/Kunne ikke gemme \(marts 2026\):/).map((el) => el.textContent ?? '')
    expect(texts.some((t) => t.includes('Det er uvist, om ændringerne blev gemt'))).toBe(true)
    expect(texts.some((t) => t.includes('Ændringerne er ikke gemt'))).toBe(true)
    expect(fetchLog.some((e) => e.startsWith('POST') && e.includes('/api/approval/send'))).toBe(false)
  })

  it('F: a hung save is reported by the FIRST month load only — a second load, even after dismissal, does not report it again', async () => {
    // Red condition: against c51a2012 the load's own bound lapsed before the queue's, the notice
    // was given without marking the save, and the queue then registered it as hung — so the NEXT
    // load reported the same save again (the final assertion fails).
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    saveResponder = () => new Promise(() => {}) // never answers
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // sends the pending edit now; April's load waits
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) })
    vi.useRealTimers()
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    const alert = screen.getByText(/Kunne ikke gemme \(marts 2026\):/)
    expect(alert).toHaveTextContent('Det er uvist, om ændringerne blev gemt')
    fireEvent.click(within(alert.closest('[role="alert"]') as HTMLElement).getByRole('button', { name: 'Luk besked' }))
    expect(screen.queryByText(/Kunne ikke gemme \(marts 2026\):/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ })) // a second load
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()) // March loaded
    await act(async () => { await new Promise((r) => setTimeout(r, 50)) })
    expect(screen.queryByText(/Kunne ikke gemme \(marts 2026\):/)).toBeNull()
  })

  it('F: a save that fails with its month back on screen (March → April → March before it answered) leaves a PERSISTENT notice — a later save of another March day does not clear it', async () => {
    // Red condition: against c51a2012 the failure sat in the one clearable slot (March was on
    // screen when it failed); the March reload dropped its re-queued edit, and the later save of
    // another day cleared the warning, so the dropped day was lost without a trace.
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    const held: Array<(r: unknown) => void> = []
    let n = 0
    saveResponder = () => (++n === 1 ? new Promise((resolve) => { held.push(resolve) }) : jsonResponse({}))
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // S1 leaves (held)
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ })) // back to March before S1 answers
    await waitFor(() => expect(held).toHaveLength(1))
    await act(async () => { held[0](jsonResponse({ error: 'offline' }, 500)) })
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()) // March reloaded
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toHaveTextContent('3. marts')
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 4'), { target: { value: '1' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // a save of ANOTHER March day succeeds
    vi.useRealTimers()
    expect(saveBodies).toHaveLength(2)
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toHaveTextContent('3. marts')
  })

  it('F: a save that fails LATE — after March was reloaded without waiting for it — leaves a persistent notice, not a clearable one', async () => {
    // Red condition: against cb05d2dc the failure was filed by which month was on screen (March,
    // again), so it sat in the one clearable slot after the hydration-time move had already run;
    // the later save of another March day cleared it (the final assertion fails).
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    const held: Array<(r: unknown) => void> = []
    let n = 0
    saveResponder = () => (++n === 1 ? new Promise((resolve) => { held.push(resolve) }) : jsonResponse({}))
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // S1 leaves (held)
    await act(async () => { await vi.advanceTimersByTimeAsync(50) })
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ })) // back to March
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) }) // the bound expires; March reloads without S1
    vi.useRealTimers()
    await waitFor(() => expect(screen.getByLabelText('Udvikling dag 3')).toBeInTheDocument()) // March hydrated
    expect(held).toHaveLength(1)
    await act(async () => { held[0](jsonResponse({ error: 'late failure' }, 500)) }) // S1 fails now
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):.*late failure/)).toHaveTextContent('Ændringerne er ikke gemt')
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 4'), { target: { value: '1' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // a save of ANOTHER March day succeeds
    vi.useRealTimers()
    expect(saveBodies).toHaveLength(2)
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):.*late failure/)).toHaveTextContent('3. marts')
  })

  it('B: the "uvist" notice names the month of the save that HUNG, not the month on screen', async () => {
    // Kind B (it passes against c51a2012 too; it pins the naming that the load pin cannot).
    // Red conditions (mutation, run in S147 fix-up 1): reportUnanswered(null) in flushBounded
    // makes the alert say "april 2026" and this test fails.
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    let n = 0
    saveResponder = () => (++n === 1 ? new Promise(() => {}) : jsonResponse({})) // March's save hangs; April's answers
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // March's save leaves and hangs
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) }) // April loads; the load reported March
    vi.useRealTimers()
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    fireEvent.click(within(screen.getByText(/Kunne ikke gemme \(marts 2026\):/).closest('[role="alert"]') as HTMLElement).getByRole('button', { name: 'Luk besked' }))
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' })) // the flush still waits for March's hung save
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000) })
    vi.useRealTimers()
    const alert = screen.getByText(/Kunne ikke gemme \(marts 2026\):/)
    expect(alert).toHaveTextContent('Det er uvist, om ændringerne blev gemt')
    expect(screen.queryByText(/Kunne ikke gemme \(april 2026\):/)).toBeNull()
  })

  it('F: March\'s dropped-save notice survives a later April failure and an April success (the reviewer\'s reproduction)', async () => {
    // Red condition: against 782e2285 April's failure REPLACED March's alert in the one slot, so
    // the March notice was gone before April's success (the first March assertion below fails).
    await renderLoaded()
    const marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    let n = 0
    saveResponder = () => (++n <= 2 ? jsonResponse({ error: 'offline' }, 500) : jsonResponse({}))
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ })) // March's save fails (dropped)
    await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })
    await screen.findByText(/Kunne ikke gemme \(marts 2026\):/)
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '1' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // April's save fails
    expect(saveBodies).toHaveLength(2)
    expect(screen.getByText(/Kunne ikke gemme \(april 2026\):/)).toBeInTheDocument()
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '3' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // April's save succeeds
    vi.useRealTimers()
    expect(saveBodies).toHaveLength(3)
    expect(screen.queryByText(/Kunne ikke gemme \(april 2026\):/)).toBeNull()
    expect(screen.getByText(/Kunne ikke gemme \(marts 2026\):/)).toBeInTheDocument()
    // Dismissing removes it.
    const marchAlert = screen.getByText(/Kunne ikke gemme \(marts 2026\):/).closest('[role="alert"]') as HTMLElement
    fireEvent.click(within(marchAlert).getByRole('button', { name: 'Luk besked' }))
    expect(screen.queryByText(/Kunne ikke gemme \(marts 2026\):/)).toBeNull()
  })

  it('F: each queue link\'s bound starts when the save AHEAD of it starts — a third save waits for the second, not for the clock', async () => {
    // Red condition: against 0974b286 every waiting save's 15 s timer started when it was QUEUED,
    // so after one hang the third save left at ~18 s, overlapping the still-hung second one.
    await renderLoaded()
    saveResponder = () => new Promise(() => {}) // every save hangs
    vi.useFakeTimers()
    const input = () => screen.getByLabelText('Udvikling dag 3')
    fireEvent.change(input(), { target: { value: '1' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // t≈1.1 s: A leaves
    fireEvent.change(input(), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // t≈2.2 s: B queued
    fireEvent.change(input(), { target: { value: '3' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // t≈3.3 s: C queued
    expect(saveBodies).toHaveLength(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(16_700) }) // t≈20 s
    expect(saveBodies).toHaveLength(2) // B left at A.start + 15 s; C still waits for B
    await act(async () => { await vi.advanceTimersByTimeAsync(12_000) }) // t≈32 s (> B.start + 15 s)
    expect(saveBodies).toHaveLength(3)
    vi.useRealTimers()
  })

  it('F: a queued absence edit is sent as an ABSENCE even if the classification list changed before it left', async () => {
    // Red condition: against a5a84093 the cell was classified when the POST was built, i.e. after
    // April (whose absence-type list lacks VACATION) had loaded — it went out as a PROJECT entry.
    // NOTE: this is the END-TO-END pin (it passes today partly because the April load now waits
    // for the save queue). The classification-at-queue-time contract is isolated in
    // hooks/__tests__/skemaSaveBatch.test.ts.
    await renderLoaded()
    const held: Array<(r: unknown) => void> = []
    saveResponder = () => new Promise((resolve) => { held.push(resolve) })
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // A leaves (held)
    fireEvent.change(screen.getByLabelText('Ferie dag 5'), { target: { value: '3' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // B queued behind A
    vi.useRealTimers()
    monthData = aprilFixture({ absenceTypes: [] })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    await act(async () => { await new Promise((r) => setTimeout(r, 20)) })
    await act(async () => { held[0](jsonResponse({})) }) // A ok → B leaves
    await waitFor(() => expect(held).toHaveLength(2))
    expect(saveBodies[1].absences).toEqual([{ date: '2026-03-05', absenceType: 'VACATION', hours: 3 }])
    expect(saveBodies[1].entries).toBeNull()
    await act(async () => { held[1](jsonResponse({})) })
  })

  it('F: March → April → March shows the value saved in the meantime (the load waits for the queued save)', async () => {
    // Red condition: against a5a84093 the March reload went out while the save was still in
    // flight, hydrated the pre-save value, and the save's later success was ignored.
    await renderLoaded()
    let marchServer = monthData
    monthAwareMock(() => marchServer, () => aprilFixture())
    let releaseSave!: () => void
    saveResponder = () => new Promise((resolve) => {
      releaseSave = () => {
        marchServer = makeMonthData({
          entries: [...monthData.entries, { date: '2026-03-03', projectCode: 'UDV', hours: 2 }],
        })
        resolve(jsonResponse({}))
      }
    })
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // the save leaves (held)
    vi.useRealTimers()
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    fireEvent.click(screen.getByRole('button', { name: /Forrige/ }))
    // Back on March, whose reload is still waiting for the queued save: NO input is accepted
    // until March has hydrated (red against 0974b286: data was March's, so the grid was editable).
    expect(screen.queryByLabelText('Udvikling dag 3')).toBeNull()
    const monthGets = () => fetchLog.filter((e) => e.includes('/api/skema/emp001/month')).length
    const getsBefore = monthGets()
    await act(async () => { releaseSave() })
    // Blur first: the cell input keeps its typed draft while focused/edited, so only a committed
    // read shows what the page actually holds.
    const shown = () => {
      const el = screen.getByLabelText('Udvikling dag 3') as HTMLInputElement
      fireEvent.blur(el)
      return (screen.getByLabelText('Udvikling dag 3') as HTMLInputElement).value
    }
    await waitFor(() => expect(shown()).toBe('2'))
    // Barrier (instead of a sleep): the reload really went out after the save, and nothing
    // reloads again behind our back.
    expect(monthGets()).toBeGreaterThan(getsBefore)
    expect(shown()).toBe('2')
  })

  it('F: a double click on "Godkend måned" sends once', async () => {
    // Red condition: against a5a84093 the footer was only locked for a month mismatch and
    // `approving` was set after the flush, so a second click ran a second flush and send.
    await renderLoaded()
    const approve = screen.getByRole('button', { name: 'Godkend måned' })
    fireEvent.click(approve)
    fireEvent.click(approve)
    // Barrier: the whole sequence (flush → send → reload) has completed — the busy lock is
    // released, i.e. the approve button is enabled again — before anything is counted.
    await waitFor(() => expect(fetchLog.some((e) => e.includes('/api/approval/send'))).toBe(true))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Godkend måned' })).toBeEnabled())
    expect(fetchLog.filter((e) => e.startsWith('POST') && e.includes('/api/approval/send'))).toHaveLength(1)
  })

  it('F: a hung save does not stall later saves forever — the next one leaves after the bound', async () => {
    // Red condition: against a5a84093 the second save waited for the hung first one indefinitely.
    await renderLoaded()
    saveResponder = () => new Promise(() => {}) // never settles
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // A leaves, hangs
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '3' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(1100) }) // B queued
    await act(async () => { await vi.advanceTimersByTimeAsync(10_000) })
    expect(saveBodies).toHaveLength(1) // still inside the bound
    await act(async () => { await vi.advanceTimersByTimeAsync(6_000) })
    expect(saveBodies).toHaveLength(2) // the bound passed: B proceeds (A is neither aborted nor replayed)
    expect(saveBodies[1].entries).toEqual([{ date: '2026-03-03', projectCode: 'UDV', hours: 3 }])
    vi.useRealTimers()
  })

  it('F: a month that keeps coming back as the wrong month shows a recoverable message, and "Prøv igen" reloads it', async () => {
    // Red condition: against a5a84093 the page stayed silently locked (no message, no action).
    await renderLoaded()
    const marchFixture = monthData
    let answerRight = false
    monthAwareMock(() => marchFixture, () => (answerRight ? aprilFixture() : marchFixture))
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    expect(await screen.findByText(/Kunne ikke indlæse den valgte måned/)).toBeInTheDocument()
    answerRight = true
    fireEvent.click(screen.getByRole('button', { name: 'Prøv igen' }))
    expect(await screen.findByRole('button', { name: 'Registrér arbejdstid 2026-04-01' })).toBeInTheDocument()
    expect(screen.queryByText(/Kunne ikke indlæse den valgte måned/)).toBeNull()
  })

  it('F: the aria-busy area is a real box (a class, not an inline display: contents)', async () => {
    // Red condition: against a5a84093 the wrapper was `style="display: contents"`.
    await renderLoaded()
    let release!: () => void
    const gate = new Promise<void>((r) => { release = r })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/skema/emp001/month') && String(url).includes('month=4')) {
        await gate
        return jsonResponse(aprilFixture())
      }
      return base(url, init)
    })
    fireEvent.click(screen.getByRole('button', { name: /Næste/ }))
    const busyEl = document.querySelector('[aria-busy="true"]') as HTMLElement
    expect(busyEl).not.toBeNull()
    expect(busyEl.getAttribute('style') ?? '').not.toContain('contents')
    expect(busyEl.className).toMatch(/busyRegion/)
    await act(async () => { release() })
  })
})

describe('SkemaPage — W2 §J rest analysis over LOCAL edits', () => {
  it('W2/R6: editing day N\'s periods locally (no save round-trip) and opening day N+1 warns from N\'s LOCAL state', async () => {
    await renderLoaded()

    // Day N (Mar 2): extend the served 08:00–17:30 to end 23:00 — LOCAL only
    // (no refetch happens; the served data still says 17:30).
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    let drawer = await screen.findByRole('dialog')
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '23:00' } })
    fireEvent.click(within(drawer).getByRole('button', { name: 'Færdig' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    // Day N+1 (Mar 3): 06:00 start → 7 h rest against Mar 2's LOCAL 23:00 end
    // (the served 17:30 end would give 12,5 h — no warning — so this pin fails
    // when the panel is fed raw data.workTime instead of the local overlay).
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-03' }))
    drawer = await screen.findByRole('dialog')
    fireEvent.change(within(drawer).getByLabelText('Fra'), { target: { value: '06:00' } })
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '12:00' } })
    expect(within(drawer).getByText(/giver kun 7 timers hvile/)).toBeInTheDocument()
    expect(within(drawer).getByText(/mandag 2\. marts og tirsdag 3\. marts/)).toBeInTheDocument()
  })
})

describe('SkemaPage — R5 locked months', () => {
  it('R5: EMPLOYEE_APPROVED = readOnly grid, unreachable panel, disabled saves — but the manager modal STAYS reachable', async () => {
    monthData = makeMonthData({
      approval: {
        periodId: 'per-1',
        status: 'EMPLOYEE_APPROVED',
        employeeDeadline: null,
        managerDeadline: null,
        employeeApprovedAt: '2026-04-01T08:00:00Z',
        rejectionReason: null,
      },
    })
    const { container } = await renderLoaded()
    // readOnly grid: no editable inputs anywhere on the page
    expect(container.querySelectorAll('input').length).toBe(0)
    // the interactive row renders as DATA — no panel triggers
    expect(gridRowOrNull(container, 'Reg. Arbejdstid')).toBeNull()
    const workRow = gridRow(container, 'Arbejdstid')
    expect(workRow.querySelectorAll('button').length).toBe(0)
    expect(screen.queryByRole('dialog')).toBeNull()
    // the existing footer logic is preserved (status badge + Genåbn)
    expect(screen.getByText('Indsendt')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Genåbn' })).toBeInTheDocument()
    // …and the modal stays reachable (view preferences are month-independent)
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    expect(await screen.findByText('Administrer rækker')).toBeInTheDocument()
  })
})

describe('SkemaPage — R16 ownership', () => {
  it('R16: the page owns NO private buildWorkTimePayload — it imports the hook\'s single owner', () => {
    expect(skemaPageSource).not.toMatch(/const buildWorkTimePayload|function buildWorkTimePayload/)
    expect(skemaPageSource).toMatch(/import \{[^}]*buildWorkTimePayload[^}]*\} from '\.\.\/hooks\/useSkema'/)
  })
})

// ── S73 / TASK-7302 — the rejected-save honesty split (R4) ──
describe('SkemaPage — R4 rejected-save honesty', () => {
  it('R4: a 422 REVERTS the affected cell to server truth (a cell with no server value clears) and surfaces the alert', async () => {
    await renderLoaded()
    // The save 422s with the full-day-only body.
    saveResponder = () =>
      jsonResponse(
        {
          error: 'absence_full_day_only',
          absenceType: 'CARE_DAY',
          date: '2026-03-03',
          requiredHours: 7.4,
          message: 'Omsorgsdag kan kun registreres som hel dag (7,4 timer) den 03-03-2026.',
        },
        422,
      )
    vi.useFakeTimers()
    // Register a partial CARE_DAY on Mar 3 (server truth has none), then blur so
    // the cell is committed (the displayed value reflects cellValues, not the
    // raw editing text). S123 note: this fixture keeps CARE_DAY fullDayOnly=FALSE,
    // so the on-input fill does not intercept — in production CARE_DAY is full-day-
    // only and this partial is UI-unreachable (the fill snaps it to the whole day).
    // The test is retained as a SERVER-AUTHORITY pin: the 422 revert works whatever
    // the FE config, and the backend full-day 422 stays the source of truth.
    const careInput = screen.getByLabelText('Omsorgsdage dag 3')
    fireEvent.change(careInput, { target: { value: '3' } })
    fireEvent.blur(careInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    // Reverted to server truth (no CARE_DAY on Mar 3 → blank).
    await waitFor(() =>
      expect((screen.getByLabelText('Omsorgsdage dag 3') as HTMLInputElement).value).toBe(''),
    )
    // …and the 422 body's own server-composed Danish sentence is shown, plus the page's rollback sentence.
    // S147 QUAL-231: kind F. Fails on the pre-change formatAbsenceRuleError, which ignored `message`
    // and rendered a label-table sentence ("Omsorgsdage skal registreres som en hel dag (7,4 t). ...").
    // Red conditions: the getByText below finds nothing (that sentence is rendered instead). Was
    // /Omsorgsdage skal registreres som en hel dag/. The mock's decimal comma is what the server sends
    // since S147 tidy item 8 (da-DK formatting; a full-timer's served value keeps two decimals, "7,40").
    expect(
      screen.getByText('Omsorgsdag kan kun registreres som hel dag (7,4 timer) den 03-03-2026. Registreringen blev derfor sat tilbage.'),
    ).toBeInTheDocument()
  })

  // S147 C-1 (owner ruling 2026-11-04, option a), kind R (regression pin, boundary - holds today): the
  // quota alert names the entitlement with the PLURAL balance name the server serves, now read from
  // the one shared map (entitlementBalanceLabel). Red conditions: the alert text below changes if the
  // map drifts from BalanceEndpoints.DanishLabels (e.g. the singular "Omsorgsdag") or if the page
  // goes back to a private table that disagrees.
  it('C-1: a quota 422 names the entitlement with the plural balance name', async () => {
    await renderLoaded()
    saveResponder = () =>
      jsonResponse(
        { error: 'Quota exceeded', absenceType: 'CARE_DAY', remaining: 1, requested: 3, message: 'Quota exceeded for CARE_DAY' },
        422,
      )
    vi.useFakeTimers()
    const careInput = screen.getByLabelText('Omsorgsdage dag 3')
    fireEvent.change(careInput, { target: { value: '3' } })
    fireEvent.blur(careInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    expect(
      await screen.findByText('Du har overskredet din kvote for Omsorgsdage. Du har 1,0 dage tilbage, men forsøgte at registrere 3,0 dage.'),
    ).toBeInTheDocument()
  })

  it('S123: a daily-norm ABSENCE cap 422 surfaces the dedicated cap alert (shape-discriminated, NOT the generic page-level swallow)', async () => {
    await renderLoaded()
    // The cap 422 body shape: {error, date, totalHours, maxHours} — NO absenceType.
    // Pre-S123 this fell through to setError, which the page swallows once data is
    // loaded (no alert). The shape-keyed branch now surfaces a Danish cap alert.
    saveResponder = () =>
      jsonResponse(
        { error: 'Total absence hours exceed norm day', date: '2026-03-04', totalHours: 9, maxHours: 7.4 },
        422,
      )
    vi.useFakeTimers()
    // Ferie is hours-based (fullDayOnly=false) → the typed value reaches save; the
    // server rejects it as over the day's absence cap.
    const ferieInput = screen.getByLabelText('Ferie dag 4')
    fireEvent.change(ferieInput, { target: { value: '9' } })
    fireEvent.blur(ferieInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    expect(await screen.findByText(/overstiger normtiden/)).toBeInTheDocument()
  })

  // AC-S0-7g, kind F (vitest). Red conditions: before the kind-aware formatter the same bodies
  // render "…overstiger normtiden (0 t)…" / "…overstiger normtiden (4,4 t)…", so the exact-text
  // assertions and the absence of "overstiger normtiden" both fail. Expected texts are the
  // ruled copy (TASK-14810 contract item 3(d), sentences 2 and 3), never read from the code.
  it("S0-7: the paid-absence bound's 422 (kind paid-absence-day-bound) names the planned and worked hours, not the norm", async () => {
    await renderLoaded()
    const typeFerie = async (value: string) => {
      vi.useFakeTimers()
      const ferieInput = screen.getByLabelText('Ferie dag 4')
      fireEvent.change(ferieInput, { target: { value } })
      fireEvent.blur(ferieInput)
      await act(async () => {
        await vi.advanceTimersByTimeAsync(1100)
      })
      vi.useRealTimers()
    }

    // (i) no room: planned 7,4, worked 7,4 → maxHours 0 (sentence 2)
    saveResponder = () =>
      jsonResponse(
        {
          error: 'Total absence hours exceed norm day', date: '2026-03-04', totalHours: 7.4, maxHours: 0,
          kind: 'paid-absence-day-bound', plannedHours: 7.4, workedHours: 7.4,
        },
        422,
      )
    await typeFerie('7.4')
    expect(
      await screen.findByText(
        'Dagen er allerede fyldt: planlagt 7,4 t, registreret arbejde 7,4 t, så der er ikke plads til betalt fravær (7,4 t). Registreringen blev derfor sat tilbage.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText(/overstiger normtiden/)).not.toBeInTheDocument()

    // (ii) some room: planned 7,4, worked 3 → room for at most 4,4 (sentence 3; AC-S0-7c's figures)
    saveResponder = () =>
      jsonResponse(
        {
          error: 'Total absence hours exceed norm day', date: '2026-03-04', totalHours: 4.5, maxHours: 4.4,
          kind: 'paid-absence-day-bound', plannedHours: 7.4, workedHours: 3,
        },
        422,
      )
    await typeFerie('4.5')
    expect(
      await screen.findByText(
        'Dagens betalte fravær (4,5 t) er mere end dagen kan rumme: planlagt 7,4 t, registreret arbejde 3 t, så der er plads til højst 4,4 t. Registreringen blev derfor sat tilbage.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText(/overstiger normtiden/)).not.toBeInTheDocument()
  })

  // Kind R-style companion to AC-S0-7g: sentence 1 (planned 0). Red condition: the pre-change
  // formatter shows "…overstiger normtiden (0 t)…" instead.
  it("S0-7: the paid-absence bound's 422 on a day with no planned hours says so (sentence 1)", async () => {
    await renderLoaded()
    saveResponder = () =>
      jsonResponse(
        {
          error: 'Total absence hours exceed norm day', date: '2026-03-04', totalHours: 7.4, maxHours: 0,
          kind: 'paid-absence-day-bound', plannedHours: 0, workedHours: 0,
        },
        422,
      )
    vi.useFakeTimers()
    const ferieInput = screen.getByLabelText('Ferie dag 4')
    fireEvent.change(ferieInput, { target: { value: '7.4' } })
    fireEvent.blur(ferieInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    expect(
      await screen.findByText(
        'Der er ingen planlagte timer denne dag, så der kan ikke registreres betalt fravær (7,4 t). Registreringen blev derfor sat tilbage.',
      ),
    ).toBeInTheDocument()
  })

  it('S123 (Step-7a WARNING): a >24h work-time 422 does NOT surface the absence-cap alert — the two 422s share a shape, so the branch anchors on the specific error string', async () => {
    await renderLoaded()
    // `work_time_exceeds_day` carries the SAME body shape as the absence cap
    // ({date, totalHours, maxHours}, no absenceType). Under a shape-ONLY match it
    // was mis-surfaced as "…fravær… overstiger normtiden 24 t" (nonsense — it's
    // work time). The error-string anchor sends it to the raw-text branch instead.
    saveResponder = () =>
      jsonResponse(
        {
          error: 'work_time_exceeds_day',
          date: '2026-03-04',
          totalHours: 25,
          maxHours: 24,
          message: 'Arbejdstid for 04-03-2026 må ikke overstige 24 timer.',
        },
        422,
      )
    const ferieInput = screen.getByLabelText('Ferie dag 4') as HTMLInputElement
    const serverTruth = ferieInput.value // capture before editing (revert target)
    vi.useFakeTimers()
    fireEvent.change(ferieInput, { target: { value: '5' } })
    fireEvent.blur(ferieInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    // The 422 is still a rejection (the cell reverts to server truth)…
    await waitFor(() =>
      expect((screen.getByLabelText('Ferie dag 4') as HTMLInputElement).value).toBe(serverTruth),
    )
    // …but the absence-cap alert must NOT appear (it would, mis-keyed, pre-fix).
    expect(screen.queryByText(/overstiger normtiden/)).not.toBeInTheDocument()
  })

  it('R4: a 422 reverts the cell to its PRE-EXISTING server value (not blank) when the server had one', async () => {
    // Server truth: VACATION 7,4 on Mar 3 (from the fixture). Edit it to 5, get a
    // 422 → the cell reverts to the served 7,4, not to blank.
    await renderLoaded()
    saveResponder = () => jsonResponse({ error: 'absence_full_day_only', absenceType: 'VACATION' }, 422)
    vi.useFakeTimers()
    const ferieInput = screen.getByLabelText('Ferie dag 3')
    fireEvent.change(ferieInput, { target: { value: '5' } })
    fireEvent.blur(ferieInput) // commit (ferie = hours-based, no snap)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    await waitFor(() =>
      expect((screen.getByLabelText('Ferie dag 3') as HTMLInputElement).value).toBe('7,4'),
    )
  })

  it('R4 overlap guard: a 422 on the OLD value does NOT revert a cell a NEWER edit already owns', async () => {
    await renderLoaded()
    // Save A leaves with value 2 and is HELD; it will 422.
    let releaseAAs422!: () => void
    saveResponder = () =>
      new Promise((resolve) => {
        releaseAAs422 = () =>
          resolve(jsonResponse({ error: 'absence_full_day_only', absenceType: 'UDV' }, 422))
      })
    vi.useFakeTimers()
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // save A leaves
    })
    // A NEWER edit takes the cell to 5 (succeeds), then commit.
    saveResponder = null
    const udvInput = screen.getByLabelText('Udvikling dag 3')
    fireEvent.change(udvInput, { target: { value: '5' } })
    fireEvent.blur(udvInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    // Now save A settles as 422 — its revert must SKIP (live 5 ≠ sent 2).
    releaseAAs422()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })
    expect((screen.getByLabelText('Udvikling dag 3') as HTMLInputElement).value).toBe('5')
  })

  it('R4: a 403 (credential-shaped) KEEPS the local edit — a mid-edit auth failure never discards typed cells, no revert', async () => {
    // 403 is the credential-shaped non-2xx that does NOT trigger the api.ts 401
    // reload path; it must keep the local edit (the B2 retry posture), never
    // revert to server truth (revert is 422-ONLY).
    await renderLoaded()
    saveResponder = () => jsonResponse({ error: 'forbidden' }, 403)
    vi.useFakeTimers()
    const udvInput = screen.getByLabelText('Udvikling dag 3')
    fireEvent.change(udvInput, { target: { value: '2' } })
    fireEvent.blur(udvInput)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()
    // The local edit is intact (NOT reverted) — the failed/retry posture holds.
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })
    expect((screen.getByLabelText('Udvikling dag 3') as HTMLInputElement).value).toBe('2')
  })

  it('R4 flush contract: a 422-reverted delta counts RESOLVED — a following preference PUT PROCEEDS', async () => {
    await renderLoaded()
    // A pending debounced edit that the FLUSH will fire — and the server 422s.
    saveResponder = () => jsonResponse({ error: 'absence_full_day_only', absenceType: 'UDV' }, 422)
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    // Modal action triggers the flush → the 422 resolves (revert) → the PUT proceeds.
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(putBodies).toHaveLength(1))
    // No prefs-save-abort error surfaced (the flush did NOT return false).
    expect(screen.queryByText(/Kunne ikke gemme rækkeindstillingerne/)).toBeNull()
  })

  it('R4 contrast: a 500 (non-422) flush failure still ABORTS the preference PUT (the S72 B2 posture is unchanged)', async () => {
    await renderLoaded()
    saveResponder = () => jsonResponse({ error: 'boom' }, 500)
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    expect(
      await screen.findByText(/Kunne ikke gemme rækkeindstillingerne/),
    ).toBeInTheDocument()
    expect(fetchLog.some((e) => e.startsWith('PUT'))).toBe(false)
    expect(putBodies).toHaveLength(0)
  })

  // ── S73 Step-7a B2 — the work-time 422 mirror of fireCellSave ──
  it('B2: a work-time 422 REVERTS the day\'s intervals to server truth + surfaces the alert', async () => {
    await renderLoaded()
    // The work-time save 422s (a work-time-shaped or generic 422 body).
    saveResponder = () =>
      jsonResponse({ error: 'absence_full_day_only', message: 'work-time rejected' }, 422)

    // Open Mar 2 (server truth: 08:00–17:30) and push the end to 17:00.
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    let drawer = await screen.findByRole('dialog')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // the 1s work-time debounce fires → 422
    })
    vi.useRealTimers()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30)) // let the 422 revert settle
    })

    // The 422 alert surfaced (saveMonth maps the body into the alert surface).
    expect(screen.getByText(/work-time rejected|hel dag/i)).toBeInTheDocument()

    // Re-open the panel: it re-initialises from the (reverted) local work-time —
    // the end is back to server truth 17:30, NOT the rejected 17:00.
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    drawer = await screen.findByRole('dialog')
    expect((within(drawer).getByLabelText('Til') as HTMLInputElement).value).toBe('17:30')
  })

  it('B2: a work-time 422 counts RESOLVED — a following preference PUT is NOT silently aborted', async () => {
    await renderLoaded()
    saveResponder = () =>
      jsonResponse({ error: 'absence_full_day_only', message: 'work-time rejected' }, 422)

    // Push an UNFLUSHED work-time edit (no fake timers → the debounce has not
    // fired), then open the manager modal and remove a row: the flush fires the
    // pending work-time save → 422 → reverts → counts RESOLVED → the PUT proceeds.
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))

    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    await screen.findByText('Administrer rækker')
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Drift & support' }))
    await waitFor(() => expect(putBodies).toHaveLength(1))
    // No prefs-save-abort error surfaced (the work-time 422 did NOT return false).
    expect(screen.queryByText(/Kunne ikke gemme rækkeindstillingerne/)).toBeNull()
  })

  // ── S73 Step-7a cycle-2 B2 — a 422 reverts to the LAST KNOWN-GOOD save, not
  //    the stale original-fetched value (serverWorkIntervalsRef advances on a
  //    SUCCESSFUL save). ──
  it('B2 (c2): a work-time 422 AFTER a successful save reverts to the SAVED value, not the original', async () => {
    await renderLoaded()
    // First edit SUCCEEDS (server truth must advance to 18:00).
    saveResponder = () => jsonResponse({})

    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    let drawer = await screen.findByRole('dialog')
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '18:00' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // debounce fires → 200 (server truth → 18:00)
    })
    vi.useRealTimers()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })

    // Now a SECOND edit on the SAME day 422s.
    saveResponder = () =>
      jsonResponse({ error: 'absence_full_day_only', message: 'work-time rejected' }, 422)
    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // debounce fires → 422 → revert
    })
    vi.useRealTimers()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })

    // The revert restores 18:00 (the LAST KNOWN-GOOD save), NOT the original 17:30.
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    drawer = await screen.findByRole('dialog')
    expect((within(drawer).getByLabelText('Til') as HTMLInputElement).value).toBe('18:00')
  })

  // ── S73 Step-7a cycle-2 B2 (cell mirror) — the cell path shared the bug:
  //    serverCellsRef advances on a successful cell save, so a later 422 reverts
  //    to the saved value, not the original. ──
  it('B2 (c2 cell): a cell 422 AFTER a successful cell save reverts to the SAVED value, not the original', async () => {
    await renderLoaded()
    // The fixture seeds Mar 2 Drift & support at 7,4 (allocated). First edit to
    // 5 SUCCEEDS → server truth advances to 5.
    saveResponder = () => jsonResponse({})
    const input1 = screen.getByLabelText('Drift & support dag 2') as HTMLInputElement

    vi.useFakeTimers()
    fireEvent.focus(input1)
    fireEvent.change(input1, { target: { value: '5' } })
    fireEvent.blur(input1)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // cell debounce fires → 200 (server truth → 5)
    })
    vi.useRealTimers()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })

    // Second edit on the SAME cell 422s → revert to the last known-good (5), not 7,4.
    saveResponder = () => jsonResponse({ error: 'boom' }, 422)
    const input2 = screen.getByLabelText('Drift & support dag 2') as HTMLInputElement
    vi.useFakeTimers()
    fireEvent.focus(input2)
    fireEvent.change(input2, { target: { value: '3' } })
    fireEvent.blur(input2)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // cell debounce fires → 422 → revert
    })
    vi.useRealTimers()
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30))
    })

    expect((screen.getByLabelText('Drift & support dag 2') as HTMLInputElement).value).toBe('5')
  })
})

// ── S73 / TASK-7302 — the full-day snap end-to-end (R5) ──
describe('SkemaPage — R5 full-day snap (served basis)', () => {
  function withFullDayCare(): SkemaMonthData {
    const base = makeMonthData()
    return {
      ...base,
      // Mark CARE_DAY full-day-only on both served DTO surfaces.
      absenceTypes: base.absenceTypes.map((a) =>
        a.type === 'CARE_DAY' ? { ...a, fullDayOnly: true } : a,
      ),
      rowPreferences: {
        ...base.rowPreferences!,
        absenceTypes: base.rowPreferences!.absenceTypes.map((a) =>
          a.type === 'CARE_DAY' ? { ...a, fullDayOnly: true } : a,
        ),
      },
      catalogs: {
        ...base.catalogs!,
        absenceTypes: base.catalogs!.absenceTypes.map((a) =>
          a.type === 'CARE_DAY' ? { ...a, fullDayOnly: true } : a,
        ),
      },
      // The per-day consumption basis (7,4 on Mar 5, a weekday).
      consumptionBasis: buildDailyNorm(),
    }
  }

  it('R5/S123: a partial entry in the full-day CARE_DAY cell fills to the served consumption basis (on input; the value is whole by commit)', async () => {
    monthData = withFullDayCare()
    const { container } = await renderLoaded()
    void container
    const input = screen.getByLabelText('Omsorgsdage dag 5') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '3' } }) // S123: fills to norm ON INPUT
    fireEvent.blur(input)
    expect(input.value).toBe('7,4') // whole day (Mar 5 weekday basis) — never the partial 3
  })

  it('R5: the CARE_DAY row carries the served "hele dage" note; the ferie row does not', async () => {
    monthData = withFullDayCare()
    const { container } = await renderLoaded()
    const care = gridRow(container, 'Omsorgsdage')
    expect(care.textContent).toContain('hele dage')
    const ferie = gridRow(container, 'Ferie')
    expect(ferie.textContent).not.toContain('hele dage')
  })

  it('R5: ferie (hours-based) partial-day entry is UNCHANGED — a below-norm value commits as typed (no snap)', async () => {
    monthData = withFullDayCare()
    await renderLoaded()
    const input = screen.getByLabelText('Ferie dag 5') as HTMLInputElement
    fireEvent.focus(input) // S123: no prefill — the cell starts empty
    fireEvent.change(input, { target: { value: '3,7' } })
    fireEvent.blur(input)
    expect(input.value).toBe('3,7') // ferie keeps the partial — no snap
  })
})

// ═══════════════════════════════════════════════════════════════════════════════
// S127 / TASK-12707 — AC-15: BOTH 422 shapes surface through the REAL Skema send.
//
// Why this describe exists as written: the pre-existing coverage of these two
// bodies (`hooks/__tests__/approvalValidationError.test.ts`) is a VERBATIM MIRROR
// of the private `formatApprovalValidationError`, feeding it already-parsed
// objects. It cannot see the wire, the hook, or the route — so it stayed green
// through every version of this flow, including the one where the create leg
// called `setError` with no parse at all and the allocation 422 never reached the
// alert. AC-15 names that exact failure mode ("Fails if: tested only against
// parseApprovalValidationError"), so these drive the button, over the real
// `POST /api/approval/send`, and assert the rendered Danish text.
//
// The fixture month has `approval: null`, which is what routes the click to
// `submitAndApprove` (the send arm) rather than `employeeApprove` (the by-id arm).
// ═══════════════════════════════════════════════════════════════════════════════
describe('SkemaPage — AC-15: the send 422s surface through the real flow', () => {
  it('the COVERAGE shape ({missingDays}) renders Danish guidance, not raw JSON', async () => {
    await renderLoaded()
    approveResponder = () =>
      jsonResponse(
        { missingDays: ['2026-03-05', '2026-03-06'], coveredDays: 20, totalWorkdays: 22 },
        422,
      )
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))

    expect(await screen.findByText(/Ikke alle arbejdsdage er dækket \(20 af 22\)/)).toBeInTheDocument()
    // The raw body must not leak — this is the half that goes red when the parse
    // is missing, because `setError` on the send leg renders the JSON verbatim.
    expect(screen.queryByText(/"missingDays"/)).toBeNull()
  })

  it('the ALLOCATION shape ({kind, unbalancedDays}) renders directional Danish guidance', async () => {
    await renderLoaded()
    approveResponder = () =>
      jsonResponse(
        {
          kind: 'allocation',
          unbalancedDays: [
            { date: '2026-03-05', worked: 7.4, allocated: 5, direction: 'under' },
            { date: '2026-03-06', worked: 3, allocated: 5, direction: 'over' },
          ],
        },
        422,
      )
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))

    // Both directions, from ONE response — the formatter's two arms.
    expect(await screen.findByText(/Fordel de resterende 2,4 t på projekter/)).toBeInTheDocument()
    expect(screen.getByText(/Registrér arbejdstid for de 2 t/)).toBeInTheDocument()
    expect(screen.queryByText(/"unbalancedDays"/)).toBeNull()
  })

  it('sends EXACTLY ONE POST, to /api/approval/send, with {employeeId, year, month}', async () => {
    await renderLoaded()
    const before = fetchLog.length
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    await waitFor(() => {
      expect(fetchLog.some((e, i) => i >= before && e.includes('/api/approval/send'))).toBe(true)
    })
    // The retired two-call flow would have produced a /submit POST followed by an
    // /employee-approve POST. Neither route may be touched.
    expect(fetchLog.some((e, i) => i >= before && e.includes('/api/approval/submit'))).toBe(false)
    expect(fetchLog.some((e, i) => i >= before && e.includes('/employee-approve'))).toBe(false)

    const sendCall = mockFetch.mock.calls.find((c: unknown[]) =>
      typeof c[0] === 'string' && (c[0] as string).includes('/api/approval/send'))
    expect(sendCall).toBeDefined()
    expect(JSON.parse(String((sendCall![1] as RequestInit).body)))
      .toEqual({ employeeId: 'emp001', year: 2026, month: 3 })
  })
})

// ═══════════════════════════════════════════════════════════════════════════════
// S143 / TASK-14303 — the month a person's time is FILED UNDER.
//
// PLAIN LANGUAGE. Which month the page opens on is not a display default: `useSkema`
// sends that year/month as the period envelope of BOTH `POST /api/skema/{employeeId}/save`
// AND `POST /api/approval/send` — the second of which CREATES the approval period. A device
// in the wrong time zone (or simply with a wrong clock) used to open, save into, and submit
// for approval, the WRONG month — silently, because the page seeded itself from `new Date()`
// (the browser's own clock) rather than asking the server what day it is.
//
// ONE COMBINED ASSERTION (AC-5), not four separate tests: the month the page opens on = the
// month sent to /save = the month sent to /approval/send = the day the grid highlights as
// "today". Splitting these into four tests would let a regression collapsing them back onto
// the device clock get "fixed" by editing whichever one test went red, one at a time, without
// ever re-establishing that all four still agree.
//
// THE MONTH-BOUNDARY PROOF. The device clock is forced to a zone AND an instant that, locally,
// falls on the LAST DAY OF FEBRUARY — while the server-confirmed "today" (delivered via the
// shared calendar context, `useCalendarToday()`) is 1 March. A page still reading the device
// clock would open on February; this one must open, save and submit under March regardless —
// the AUTHORITY's month, not the device's.
describe('SkemaPage — AC-5: opened month = /save month = /approval/send month = highlighted day', () => {
  let restoreTz: string | undefined

  beforeEach(() => {
    restoreTz = forceTestTimeZone('America/New_York')
    // `toFake: ['Date']` only — `setTimeout`/`waitFor` stay real (mirrors
    // `PersonDrawer.hireDate.test.tsx`'s S142 pattern).
    vi.useFakeTimers({ toFake: ['Date'] })
    // 2026-03-01 02:00 UTC is 2026-02-28 21:00 in New York (EST, UTC-05:00 — US DST does not
    // start until 2026-03-08, so New York is still on winter time here).
    vi.setSystemTime(new Date('2026-03-01T02:00:00Z'))
  })

  afterEach(() => {
    restoreTestTimeZone(restoreTz)
  })

  it('runs on a device day/month that DISAGREES with the server "today", which is what lets the facts below fail', () => {
    // THE GUARD ON THE GUARD: the SAME literal `vi.setSystemTime` pinned "now" to, above —
    // checked WITH its argument (never a bare `new Date()`, which the clock guard forbids
    // outside the approved Copenhagen helper, this file included). If the zone forcing ever
    // stopped taking effect, this would not fail — it would quietly start passing against a
    // device-clock-driven page, the worst outcome available.
    const pinned = new Date('2026-03-01T02:00:00Z')
    expect(pinned.getMonth()).toBe(1) // February (Date#getMonth is 0-indexed)
    expect(pinned.getDate()).toBe(28)
  })

  it('AC-5: the SERVER month opens, saves, sends and highlights — the device month does none of it', async () => {
    const { container } = await renderLoaded('/tid/registrering', '2026-03-01')

    // 1) THE MONTH THE PAGE OPENS ON. A device-clock-driven page would show "Februar 2026" (and
    //    the March fixture below would not even match what it asked the server for).
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')

    // 2) THE HIGHLIGHTED CELL — day 1 (the server's "today"), never day 28 (the device's).
    const headers = container.querySelectorAll('thead th')
    expect(headers[1].className).toContain('today')
    expect(headers[28].className).not.toContain('today')

    // 3) THE MONTH SENT TO /save. Edit a cell, then approve — approving flushes any pending
    //    debounced cell save IMMEDIATELY (`flushCellSave`, SkemaPage.tsx) rather than waiting
    //    out the 1s debounce, so no timer advance is needed here.
    const input = screen.getByLabelText('Udvikling dag 5') as HTMLInputElement
    fireEvent.change(input, { target: { value: '2' } })
    fireEvent.blur(input)

    const beforeApprove = fetchLog.length
    fireEvent.click(screen.getByRole('button', { name: 'Godkend måned' }))
    await waitFor(() => {
      expect(
        fetchLog.some((e, i) => i >= beforeApprove && e.includes('/api/approval/send')),
      ).toBe(true)
    })

    expect(saveBodies.length).toBeGreaterThan(0)
    const lastSave = saveBodies[saveBodies.length - 1]
    expect(lastSave.year).toBe(2026)
    expect(lastSave.month).toBe(3)

    // 4) THE MONTH SENT TO /approval/send — the SAME call that CREATES the approval period.
    const sendCall = mockFetch.mock.calls.find((c: unknown[]) =>
      typeof c[0] === 'string' && (c[0] as string).includes('/api/approval/send'))
    expect(sendCall).toBeDefined()
    const sendBody = JSON.parse(String((sendCall![1] as RequestInit).body))
    expect(sendBody.year).toBe(2026)
    expect(sendBody.month).toBe(3)
  })
})
