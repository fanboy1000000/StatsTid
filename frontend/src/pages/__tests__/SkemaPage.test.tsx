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
//   R7  — a panel period edit round-trips through the hook's
//         buildWorkTimePayload, preserving the day's existing manualHours.
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
// Mar 2 worked = 8h interval + 1,5 manual = 9,5; allocated 7,4 + 2,1 = 9,5 (✓);
// Diff +2,1. Mar 3 full VACATION 7,4 → Diff 0,0. Mar 4 VACATION 3,7 (feriedage
// NULL) → Diff −3,7. Diff total = −1,6. I alt total = 20,6.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
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
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '16:00' }], manualHours: 1.5 },
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
  workTime: { date: string; intervals: { start: string; end: string }[]; manualHours: number }[] | null
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
    if (url.includes('/api/approval/submit') && method === 'POST') {
      return jsonResponse({ periodId: 'per-1' })
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

function renderPage(url = '/tid/registrering?year=2026&month=3') {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <SkemaPage />
    </MemoryRouter>,
  )
}

async function renderLoaded(url?: string) {
  const result = renderPage(url)
  await screen.findByText('Drift & support')
  // PASS-2 await (fixes the recurring R2/W1 timing flake). The project-row LABELS
  // render in the first commit (straight from useSkema's `data`), but the diff
  // total + every day-derived cell are computed from SkemaPage's editable
  // `localCells`/`localWorkIntervals`/`localManualHours` — which start empty and
  // are SEEDED from `data` via effects (SkemaPage.tsx:212/239/320/324/327), so they
  // land in a SECOND commit. A synchronous read of a day-derived cell right after
  // the label appears therefore races that second commit (intermittently '' under
  // CI timing). Await the diff total becoming non-blank so every renderLoaded-based
  // test reads the settled Pass-2 state. (The default fixture's diff is -1,6; an
  // empty/blank diff renders '' in BOTH passes, so a blank-diff fixture must use
  // renderPage directly rather than this helper.)
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
    expect(gridRow(container, 'Registrér arbejdstid')).toBeInTheDocument()
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
        manualHours: new Map(monthData.workTime.map((wt) => [wt.date, wt.manualHours])),
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

  it('R16/B2 overlapping-saves (Step-7a c3): an OLDER save failing AFTER a newer one succeeded retries from LIVE state — the stale value never resurrects', async () => {
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

    // 2) Save B leaves with the NEW value (3) and SUCCEEDS first.
    saveResponder = null
    fireEvent.change(screen.getByLabelText('Udvikling dag 3'), { target: { value: '3' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100)
    })
    vi.useRealTimers()

    // 3) A settles as FAILURE — its retry must rebuild from LIVE state (3),
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
    expect(dayCell(gridRow(container, 'Registrér arbejdstid'), 2).textContent).toBe('✓')

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
    expect(dayCell(gridRow(container, 'Registrér arbejdstid'), 2).textContent).toBe('✓')
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
    expect(dayCell(gridRow(container, 'Registrér arbejdstid'), 5).textContent).toBe('+4,0')
    // Diff: Mar 5 now has a registration → 0 + 0 − 7,4 = −7,4; total −1,6 − 7,4 = −9,0
    expect(lastCell(gridRow(container, 'Diff. fra normtid')).textContent).toBe('-9,0')
    // …and the Flex card reconciles (R2 — the same single computation)
    expect(balanceCard('Flex saldo').textContent).toContain('Denne måned -9,0 t')
  })
})

describe('SkemaPage — R7/R16 day-panel save paths', () => {
  it('R7: a panel period edit round-trips through the hook buildWorkTimePayload, preserving the day\'s existing manualHours', async () => {
    await renderLoaded()
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')
    // D-B: the existing manual hours render read-only in the panel
    expect(within(drawer).getByText('Manuelt registreret: 1,5 t')).toBeInTheDocument()

    vi.useFakeTimers()
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '17:00' } })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1100) // the 1s debounce
    })
    vi.useRealTimers()

    expect(saveBodies.length).toBe(1)
    expect(saveBodies[0].entries).toBeNull()
    expect(saveBodies[0].absences).toBeNull()
    // The R7 pin: the edited interval AND the day's EXISTING manualHours (1,5)
    expect(saveBodies[0].workTime).toEqual([
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '17:00' }], manualHours: 1.5 },
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
    expect(gridRowOrNull(container, 'Drift & support')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    const drawer = await screen.findByRole('dialog')
    // Step 2 renders only the VISIBLE rows…
    expect(within(drawer).queryByLabelText('Drift & support')).toBeNull()
    expect(within(drawer).getByLabelText('Udvikling')).toBeInTheDocument()
    // …but Resterende computes over ALL served allocations: 9,5 worked −
    // (7,4 hidden Drift + 2,1 visible Udvikling) = balanced.
    expect(within(drawer).getByText('Alt fordelt ✓')).toBeInTheDocument()
  })
})

describe('SkemaPage — W2 §J rest analysis over LOCAL edits', () => {
  it('W2/R6: editing day N\'s periods locally (no save round-trip) and opening day N+1 warns from N\'s LOCAL state', async () => {
    await renderLoaded()

    // Day N (Mar 2): extend the served 08:00–16:00 to end 23:00 — LOCAL only
    // (no refetch happens; the served data still says 16:00).
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    let drawer = await screen.findByRole('dialog')
    fireEvent.change(within(drawer).getByLabelText('Til'), { target: { value: '23:00' } })
    fireEvent.click(within(drawer).getByRole('button', { name: 'Færdig' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    // Day N+1 (Mar 3): 06:00 start → 7 h rest against Mar 2's LOCAL 23:00 end
    // (the served 16:00 end would give 14 h — no warning — so this pin fails
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
    expect(gridRowOrNull(container, 'Registrér arbejdstid')).toBeNull()
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
          message: 'full day only',
        },
        422,
      )
    vi.useFakeTimers()
    // Register a partial CARE_DAY on Mar 3 (server truth has none), then blur so
    // the cell is committed (the displayed value reflects cellValues, not the
    // raw editing text).
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
    // …and the new 422 body surfaces a comprehensible Danish alert.
    expect(screen.getByText(/Omsorgsdage skal registreres som en hel dag/)).toBeInTheDocument()
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

    // Open Mar 2 (server truth: 08:00–16:00) and push the end to 17:00.
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
    // the end is back to server truth 16:00, NOT the rejected 17:00.
    fireEvent.click(within(drawer).getByRole('button', { name: 'Luk' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    drawer = await screen.findByRole('dialog')
    expect((within(drawer).getByLabelText('Til') as HTMLInputElement).value).toBe('16:00')
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

    // The revert restores 18:00 (the LAST KNOWN-GOOD save), NOT the original 16:00.
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

  it('R5: a partial entry in the full-day CARE_DAY cell SNAPS to the served consumption basis on commit', async () => {
    monthData = withFullDayCare()
    const { container } = await renderLoaded()
    void container
    const input = screen.getByLabelText('Omsorgsdage dag 5') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '3' } })
    fireEvent.blur(input)
    expect(input.value).toBe('7,4') // snapped to the basis (Mar 5 weekday)
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
    fireEvent.focus(input) // ADR-032 D3 prefill seeds 7,4
    fireEvent.change(input, { target: { value: '3,7' } })
    fireEvent.blur(input)
    expect(input.value).toBe('3,7') // ferie keeps the partial — no snap
  })
})
