// S65 / TASK-6503 — page-level vitest for ArsoversigtPage (Direction E
// Årsoversigt). Mocks useYearOverview with a contract-shaped payload (incl. the
// explicit SPECIAL_HOLIDAY / "Særlige feriedage" matrix entry) + useAuth + the
// router's useNavigate, so no AuthProvider or network is required.
//
// CRITICAL: every past/current/future + "Nu" classification is asserted against
// the MOCKED server `today` — never the client clock (server-today authority).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { YearOverview } from '../../hooks/useYearOverview'

// ── useAuth: a fixed logged-in employee ──
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

// ── useNavigate spy (drill-in target assertions) ──
const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return { ...actual, useNavigate: () => mockNavigate }
})

// ── useYearOverview: mocked so each test injects an exact server-shape payload ──
const mockUseYearOverview = vi.fn()
vi.mock('../../hooks/useYearOverview', () => ({
  useYearOverview: (...args: unknown[]) => mockUseYearOverview(...args),
}))

// Imported AFTER the mocks are registered.
import { ArsoversigtPage } from '../ArsoversigtPage'

// ── Fixture: a 2026 overview with today = 2026-03-15 (March = current month) ──
// Jan/Feb = past (worked + signed diff), Mar = current (Nu, worked + diff),
// Apr..Dec = future (norm projected + diff "–"). Feb has a 0,5-day VACATION.
function makeOverview(overrides: Partial<YearOverview> = {}): YearOverview {
  const months = Array.from({ length: 12 }, (_, i) => {
    const monthOneBased = i + 1
    if (i <= 2) {
      // Jan, Feb, Mar — faktisk
      return { month: monthOneBased, workedHours: 150.2, normHours: 147.9, diff: i === 1 ? -3.5 : 2.3 }
    }
    // Apr..Dec — future: diff null
    return { month: monthOneBased, workedHours: 0, normHours: 140, diff: null }
  })

  return {
    employeeId: 'emp001',
    year: 2026,
    today: '2026-03-15',
    header: {
      employeeName: 'Anna Berg',
      agreementCode: 'AC',
      okVersion: 'OK26',
      weeklyNormHours: 37,
    },
    tiles: {
      flexBalance: 22.5,
      ferieRemaining: 22,
      careDayRemaining: 1,
      seniorDayRemaining: 3,
      sickDaysYtd: 4,
      childSickRemaining: 1,
      childSickEligible: true,
      seniorDayEligible: true,
    },
    months,
    categories: [
      {
        type: 'VACATION',
        label: 'Ferie',
        // Feb afholdt = 0,5 day-equivalent; saldo drops by 0,5.
        saldo: [25, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5, 24.5],
        afholdt: [0, 0.5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        // Disposition (ADR-030 D9 as amended): amount expiring beyond the cap at Dec. > 0 → "Til udløb".
        expiring: 5,
        boundaryMonth: 12,
        // S120 mock re-anchoring: the spec category REQUIRES the nullable
        // `settlement` member (owner ruling #2 made even the empty-config
        // branch emit it) — null = unsettled; display-only this pass.
        settlement: null,
      },
      {
        type: 'SPECIAL_HOLIDAY',
        label: 'Særlige feriedage',
        saldo: [5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5],
        afholdt: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        // cap-0 type: untaken særlige feriedage convert to godtgørelse → a POSITIVE expiring that
        // SHOWS at Dec under the "Til udbetaling" label (inverts the pre-amendment cap-0 ⇒ em-dash).
        expiring: 3,
        boundaryMonth: 12,
        settlement: null,
      },
      {
        type: 'CARE_DAY',
        label: 'Omsorgsdage',
        saldo: [2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
        afholdt: [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        expiring: 0,
        boundaryMonth: 12,
        settlement: null,
      },
      {
        type: 'SENIOR_DAY',
        label: 'Seniordage',
        saldo: [3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3],
        afholdt: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        expiring: 0,
        boundaryMonth: 12,
        settlement: null,
      },
    ],
    ...overrides,
  }
}

function overviewHook(data: YearOverview | null, loading = false, error: string | null = null) {
  return { data, loading, error, refetch: vi.fn() }
}

/** Locate a data row by its rowheader (<th scope="row">) label and return its
 * 12 month <td> cells (Jan..Dec). `occurrence` picks among duplicate labels
 * (e.g. the 4 "Saldo (rest)" rows). */
function rowCells(label: string, occurrence = 0): HTMLElement[] {
  const headers = screen.getAllByRole('rowheader', { name: label })
  const tr = headers[occurrence].closest('tr') as HTMLElement
  return within(tr).getAllByRole('cell')
}

function renderPage() {
  return render(
    <MemoryRouter>
      <ArsoversigtPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  mockUseYearOverview.mockReset()
  mockNavigate.mockReset()
})

describe('ArsoversigtPage — header + tiles', () => {
  it('renders the title with the server year and the identity/norm sub-line', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Årsoversigt 2026')
    expect(screen.getByText('Anna Berg · AC · Norm: 37 t/uge')).toBeInTheDocument()
  })

  it('renders all 6 designed balance tiles with da-DK values, units and sub-lines', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // Tile labels (some — Ferie/Omsorgsdage/Seniordage — also appear as matrix
    // group headers, so assert presence rather than uniqueness).
    const labels = ['Flex saldo', 'Ferie', 'Omsorgsdage', 'Seniordage', 'Sygedage', 'Barns sygedag']
    for (const l of labels) expect(screen.getAllByText(l).length).toBeGreaterThanOrEqual(1)
    // Tile-only labels are unique.
    expect(screen.getByText('Flex saldo')).toBeInTheDocument()
    expect(screen.getByText('Sygedage')).toBeInTheDocument()
    // da-DK decimal comma + trimmed integers.
    expect(screen.getByText('22,5')).toBeInTheDocument() // Flex saldo
    expect(screen.getByText('optjent overtid')).toBeInTheDocument()
    // No 7th tile — Særlige feriedage appears ONLY as a matrix group header, never as
    // a tile label. (It still appears once, in the matrix.)
    expect(screen.getAllByText('Særlige feriedage')).toHaveLength(1)
    // Tile "rest" sub-line appears for Omsorgsdage + Seniordage + Barns sygedag.
    expect(screen.getAllByText('rest')).toHaveLength(3)
  })

  it('renders an em-dash (ineligible) for senior + child-sick tiles, layout unchanged', () => {
    const ineligible = makeOverview()
    ineligible.tiles = {
      ...ineligible.tiles,
      seniorDayEligible: false,
      seniorDayRemaining: null,
      childSickEligible: false,
      childSickRemaining: null,
    }
    mockUseYearOverview.mockReturnValue(overviewHook(ineligible))
    renderPage()
    // Tiles still present (labels render); their value is an em-dash.
    // (Seniordage also appears as a matrix group header → assert ≥1.)
    expect(screen.getAllByText('Seniordage').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Barns sygedag')).toBeInTheDocument()
    // Two em-dash tile values (senior + child-sick) — at minimum the dashes exist.
    const dashes = screen.getAllByText('–')
    expect(dashes.length).toBeGreaterThanOrEqual(2)
  })
})

describe('ArsoversigtPage — matrix structure', () => {
  it('renders 5 category groups and a 13-column header (label + 12 months)', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // Group header rows (Arbejdstid/Ferie/Omsorgsdage/Seniordage also appear as
    // tile labels; Særlige feriedage is matrix-only → all assert presence).
    for (const g of ['Arbejdstid', 'Ferie', 'Særlige feriedage', 'Omsorgsdage', 'Seniordage']) {
      expect(screen.getAllByText(g).length).toBeGreaterThanOrEqual(1)
    }
    // Header: the year label cell + 12 month columns = 13 <th scope="col">.
    const headerCells = screen.getAllByRole('columnheader')
    expect(headerCells).toHaveLength(13)
    // Sub-rows present for a leave group.
    expect(screen.getAllByText('Saldo (rest)').length).toBe(4) // Ferie, Særlige feriedage, Oms, Senior
    expect(screen.getAllByText('Afholdt').length).toBe(4)
    // Disposition row label keys off type: SPECIAL_HOLIDAY → "Til udbetaling" (godtgørelse),
    // every other leave type → "Til udløb" (lapses). The old "Kan overføres" is gone.
    expect(screen.getAllByText('Til udløb').length).toBe(3) // Ferie, Oms, Senior
    expect(screen.getAllByText('Til udbetaling').length).toBe(1) // Særlige feriedage
    expect(screen.queryByText('Kan overføres')).not.toBeInTheDocument()
    expect(screen.getByText('Diff. fra norm')).toBeInTheDocument()
  })

  it('highlights the current month ("Nu") derived from the MOCKED server today (March)', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // "Nu" tag is rendered exactly once, above the March header.
    const nuTags = screen.getAllByText('Nu')
    expect(nuTags).toHaveLength(1)
    // The March column header carries it.
    const marHeader = screen.getByRole('columnheader', { name: /Mar/ })
    expect(within(marHeader).getByText('Nu')).toBeInTheDocument()
  })

  it('shows NO highlight when viewing a year other than the server-today year', () => {
    // today is 2026-03-15 but the payload is for 2025 → no current-month in view.
    const otherYear = makeOverview({ year: 2025 })
    mockUseYearOverview.mockReturnValue(overviewHook(otherYear))
    renderPage()
    expect(screen.queryByText('Nu')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Årsoversigt 2025')
  })
})

describe('ArsoversigtPage — cell rules (server-today authority)', () => {
  it('Arbejdstid shows workedHours for past/current and projected normHours for future; diff "–" for future', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    const arbCells = rowCells('Arbejdstid')
    // Jan (past) + Mar (current) → workedHours 150,2.
    expect(arbCells[0]).toHaveTextContent('150,2')
    expect(arbCells[2]).toHaveTextContent('150,2')
    // Apr (future) → projected norm 140 (NOT worked 0).
    expect(arbCells[3]).toHaveTextContent('140')

    const diffCells = rowCells('Diff. fra norm')
    // Jan diff +2,3 (positive), Feb -3,5 (negative), Apr (future) → em-dash.
    expect(diffCells[0]).toHaveTextContent('+2,3')
    expect(diffCells[1]).toHaveTextContent('-3,5')
    expect(diffCells[3]).toHaveTextContent('–')
  })

  it('renders fractional afholdt ("0,5") and the saldo drop for the half-day VACATION', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // The Ferie group's Afholdt row, Feb column = 0,5.
    expect(screen.getByText('0,5')).toBeInTheDocument()
    // Saldo 24,5 appears (post half-day).
    expect(screen.getAllByText('24,5').length).toBeGreaterThanOrEqual(1)
  })

  it('renders normHours: null months as an em-dash', () => {
    const ov = makeOverview()
    // Make Apr a null-norm future month.
    ov.months[3] = { month: 4, workedHours: 0, normHours: null, diff: null }
    mockUseYearOverview.mockReturnValue(overviewHook(ov))
    renderPage()
    const arbCells = rowCells('Arbejdstid')
    expect(arbCells[3]).toHaveTextContent('–')
  })

  it('renders the disposition ("Til udløb") ONLY at the boundaryMonth (Dec) in info styling; em-dash elsewhere', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // The Ferie disposition row is labelled "Til udløb" (VACATION lapses); 1st such row
    // (occurrence 0): Dec cell = 5, others em-dash.
    const cells = rowCells('Til udløb', 0)
    expect(cells[11]).toHaveTextContent('5') // Dec (index 11)
    // info styling class applied to the Dec cell.
    expect(cells[11].className).toMatch(/keep/)
    // Jan..Nov are em-dashes (disposition shown only in December).
    expect(cells[0]).toHaveTextContent('–')
    expect(cells[5]).toHaveTextContent('–')
  })

  it('SHOWS a cap-0 godtgørelse type (Særlige feriedage) at December under "Til udbetaling" (D9 amended inversion)', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // Særlige feriedage's disposition row is labelled "Til udbetaling" (untaken særlige
    // feriedage convert to the 2½% godtgørelse — money, not loss). Pre-amendment a cap-0
    // type rendered an em-dash even at December; now a positive expiring (3) SHOWS.
    const cells = rowCells('Til udbetaling', 0)
    expect(cells[11]).toHaveTextContent('3') // Dec shows the godtgørelse-bound days
    expect(cells[11].className).toMatch(/keep/)
    // Other months remain em-dashes (disposition shown only in December).
    expect(cells[0]).toHaveTextContent('–')
  })

  it('renders an all-null saldo (no-config graceful row) as em-dashes without crashing, rest intact', () => {
    // The endpoint's graceful empty-config branch (no entitlement config under the
    // employee's agreement/OK — e.g. AC_RESEARCH/AC_TEACHING) emits saldo as an
    // ALL-null 12-element array (C# `new decimal?[12]`), afholdt all-zero,
    // expiring 0. A null saldo cell must render the em-dash (NOT crash on
    // formatDanishNumber(null)).
    const ov = makeOverview()
    // Make the FIRST leave group (Ferie / VACATION, occurrence 0) the graceful shape.
    ov.categories[0] = {
      type: 'VACATION',
      label: 'Ferie',
      saldo: Array.from({ length: 12 }, () => null),
      afholdt: Array.from({ length: 12 }, () => 0),
      expiring: 0,
      boundaryMonth: 12,
      settlement: null,
    }
    mockUseYearOverview.mockReturnValue(overviewHook(ov))
    // Must not throw while rendering.
    expect(() => renderPage()).not.toThrow()
    // The page rendered (heading present).
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Årsoversigt 2026')
    // The graceful group's Saldo (rest) row (occurrence 0): all 12 cells are em-dashes.
    const nullSaldoCells = rowCells('Saldo (rest)', 0)
    expect(nullSaldoCells).toHaveLength(12)
    for (const cell of nullSaldoCells) expect(cell).toHaveTextContent('–')
    // Rest of the matrix intact: the CARE_DAY group (occurrence 2) still shows a
    // real saldo value (2 in Jan/Feb per the fixture).
    const careSaldoCells = rowCells('Saldo (rest)', 2)
    expect(careSaldoCells[0]).toHaveTextContent('2')
    // Arbejdstid row still renders its worked hours (matrix structure preserved).
    const arbCells = rowCells('Arbejdstid')
    expect(arbCells[0]).toHaveTextContent('150,2')
  })
})

describe('ArsoversigtPage — interactions', () => {
  it('year switcher refetches by re-rendering the hook with the new year', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    // Capture the seed year from the initial hook invocation (client-clock seed).
    const initialCalls = mockUseYearOverview.mock.calls
    const seedYear = initialCalls[initialCalls.length - 1]?.[1] as number
    mockUseYearOverview.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Forrige år' }))
    // After clicking ←, the hook is re-invoked with exactly seedYear − 1.
    const calls = mockUseYearOverview.mock.calls
    const lastArgs = calls[calls.length - 1]
    expect(lastArgs?.[0]).toBe('emp001')
    expect(lastArgs?.[1]).toBe(seedYear - 1)
    // And → moves forward again: back to the seed year.
    fireEvent.click(screen.getByRole('button', { name: 'Næste år' }))
    const callsAfterNext = mockUseYearOverview.mock.calls
    expect(callsAfterNext[callsAfterNext.length - 1]?.[1]).toBe(seedYear)
  })

  it('drills into a month: clicking a month header navigates to /tid/registrering?year&month', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview()))
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Gå til Mar 2026/ }))
    expect(mockNavigate).toHaveBeenCalledWith('/tid/registrering?year=2026&month=3')
  })

  it('drill-in anchors to the DISPLAYED year (data.year), not the switched `year` state, after a failed year switch', () => {
    // Stale-guard semantics: a year switch that FAILS keeps the last good `data`
    // (still 2026) while the `year` state advances. The referentially-stable mock
    // models exactly that — it returns the SAME 2026 payload regardless of the
    // year argument, now WITH an error set (the failed fetch). The displayed
    // matrix is therefore 2026's; clicking a month must drill into 2026, NOT the
    // newly-selected 2025, so the row the user sees matches the row they land on.
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview(), false, 'HTTP 500'))
    renderPage()
    // Switch the year backwards (the `year` state becomes seed − 1); the mock
    // keeps serving the 2026 payload, so data.year stays 2026.
    fireEvent.click(screen.getByRole('button', { name: 'Forrige år' }))
    // The displayed year is still 2026 (heading + label both read data.year).
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Årsoversigt 2026')
    // Click March → must navigate to year=2026 (data.year), NOT the switched state.
    fireEvent.click(screen.getByRole('button', { name: /Gå til Mar 2026/ }))
    expect(mockNavigate).toHaveBeenCalledWith('/tid/registrering?year=2026&month=3')
    expect(mockNavigate).not.toHaveBeenCalledWith(
      expect.stringContaining('year=2025'),
    )
  })
})

describe('ArsoversigtPage — states', () => {
  it('renders the loading spinner while the first fetch is in flight', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(null, true))
    renderPage()
    expect(screen.getByText('Indlæser årsoversigt…')).toBeInTheDocument()
  })

  it('renders an error card when the fetch fails with no data', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(null, false, 'HTTP 500'))
    renderPage()
    expect(screen.getByText(/Kunne ikke indlæse årsoversigt/)).toBeInTheDocument()
  })

  it('shows a stale-data banner naming BOTH years when a switch fails but data is present', () => {
    // error set AND data present → the page keeps the last good matrix (2026) and
    // surfaces a soft warning instead of swallowing the failure. The message must
    // name the FAILED year (the `year` state) and the DISPLAYED year (data.year).
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview(), false, 'HTTP 500'))
    renderPage()
    // Capture the client-seeded `year` from the hook's last call, then switch
    // backwards so the failed year (seed − 1) is deterministic and ≠ data.year.
    const calls = mockUseYearOverview.mock.calls
    const seedYear = calls[calls.length - 1]?.[1] as number
    fireEvent.click(screen.getByRole('button', { name: 'Forrige år' }))
    const failedYear = seedYear - 1
    // Banner present (role=alert) and names both years.
    const banner = screen.getByRole('alert')
    expect(banner).toHaveTextContent(`Kunne ikke indlæse ${failedYear}`)
    expect(banner).toHaveTextContent('viser 2026') // data.year, still displayed
    // The matrix below is intact (Arbejdstid worked-hours still rendered).
    expect(rowCells('Arbejdstid')[0]).toHaveTextContent('150,2')
  })

  it('shows NO stale-data banner when there is no error', () => {
    mockUseYearOverview.mockReturnValue(overviewHook(makeOverview(), false, null))
    renderPage()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText(/Kunne ikke indlæse 20/)).not.toBeInTheDocument()
  })
})
