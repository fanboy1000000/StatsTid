// S141 / TASK-14109 (refinement C2) — the employment history SCREEN.
//
// `useSearch` is mocked directly (the SearchOverlay.test.tsx idiom: it has its own dedicated
// hook test, so re-driving its debounce here would only add flakiness for no new coverage). The
// history endpoint (`GET /api/hr/employees/{employeeId}/history`) is driven through a REAL global
// `fetch` mock (the OpfoelgningPage.test.tsx idiom) so the interesting behaviour — the three
// statuses, the two-tracks-interleaved-without-inventing-a-boundary rule, the empty state, the
// 422 refusal, the 403 access-denied — is exercised through the real hook, not a stand-in.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import type { SearchResponse } from '../../../../hooks/useSearch'
import { EmploymentHistoryPage } from '../EmploymentHistoryPage'

// ── useSearch: mutable holder the mock reads, like SearchOverlay.test.tsx ──────────────────────
const h = vi.hoisted(() => ({
  query: '',
  results: { units: [], people: [], unitsTotal: 0, peopleTotal: 0 } as SearchResponse,
  loading: false,
  error: null as string | null,
}))
vi.mock('../../../../hooks/useSearch', () => ({
  useSearch: () => ({ query: h.query, setQuery: vi.fn(), results: h.results, loading: h.loading, error: h.error }),
}))

// ── fetch + localStorage stubs (the OpfoelgningPage.test.tsx idiom — apiClient reads a token) ──
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => {
    mockStorage[k] = v
  },
  removeItem: (k: string) => {
    delete mockStorage[k]
  },
})

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: true,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}
function jsonErrorResponse(body: unknown, status: number) {
  return {
    ok: false,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

const MIXED_HISTORY = {
  employeeId: 'emp1',
  today: '2026-09-14',
  windowFrom: null,
  windowTo: null,
  profileHistory: [
    {
      effectiveFrom: '2020-01-01',
      effectiveTo: '2024-01-01',
      status: 'PAST',
      isInitial: true,
      changedFields: [],
      partTimeFraction: 1,
      position: 'Konsulent',
      employmentCategory: 'FULL_TIME',
    },
    {
      effectiveFrom: '2024-01-01',
      effectiveTo: null,
      status: 'CURRENT',
      isInitial: false,
      changedFields: ['position'],
      partTimeFraction: 1,
      position: 'Seniorkonsulent',
      employmentCategory: 'FULL_TIME',
    },
  ],
  agreementCodeHistory: [
    {
      effectiveFrom: '2020-01-01',
      effectiveTo: '2026-12-01',
      status: 'CURRENT',
      isInitial: true,
      changedFields: [],
      agreementCode: 'AC-OLD',
    },
    {
      effectiveFrom: '2026-12-01',
      effectiveTo: null,
      status: 'SCHEDULED',
      isInitial: false,
      changedFields: ['agreementCode'],
      agreementCode: 'AC-NEW',
    },
  ],
}

const EMPTY_HISTORY = {
  employeeId: 'emp2',
  today: '2026-09-14',
  windowFrom: null,
  windowTo: null,
  profileHistory: [],
  agreementCodeHistory: [],
}

function installFetchMock(historyById: Record<string, unknown> = { emp1: MIXED_HISTORY }) {
  mockFetch.mockImplementation(async (url: string) => {
    const match = /\/api\/hr\/employees\/([^/]+)\/history(?:\?(.*))?$/.exec(url)
    if (match) {
      const [, employeeId, qs] = match
      const params = new URLSearchParams(qs ?? '')
      const from = params.get('from')
      const to = params.get('to')
      if (from && to && from >= to) {
        return jsonErrorResponse(
          { error: 'Invalid history window', reason: "'from' must be earlier than 'to' ('to' is end-exclusive, so from == to selects nothing)." },
          422,
        )
      }
      const body = historyById[employeeId]
      if (body === undefined) {
        return jsonErrorResponse({ error: 'Access denied', reason: 'not in scope' }, 403)
      }
      return jsonResponse(body)
    }
    throw new Error(`Unmocked fetch in EmploymentHistoryPage test: ${url}`)
  })
}

function renderPage(initialEmployeeId?: string) {
  const path = initialEmployeeId
    ? `/admin/ansaettelseshistorik/${initialEmployeeId}`
    : '/admin/ansaettelseshistorik'
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/ansaettelseshistorik" element={<EmploymentHistoryPage />} />
        <Route path="/admin/ansaettelseshistorik/:employeeId" element={<EmploymentHistoryPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  mockFetch.mockReset()
  h.query = ''
  h.results = { units: [], people: [], unitsTotal: 0, peopleTotal: 0 }
  h.loading = false
  h.error = null
})

describe('EmploymentHistoryPage — picking an employee', () => {
  it('the bare route shows a search panel, and selecting a match navigates to that employee\'s history', async () => {
    installFetchMock()
    h.query = 'Jens'
    h.results = {
      units: [],
      people: [{ userId: 'emp1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Konsulent', unitName: null, path: ['Statens IT'] }],
      unitsTotal: 0,
      peopleTotal: 1,
    }
    const user = userEvent.setup()
    renderPage()

    expect(screen.getByTestId('history-search-panel')).toBeInTheDocument()
    await user.click(screen.getByTestId('history-search-result-emp1'))

    await waitFor(() => expect(screen.getByTestId('history-employee-name')).toHaveTextContent('Jens Vej'))
    expect(screen.queryByTestId('history-search-panel')).toBeNull()
  })

  it('a direct link to a known employeeId (no picked name in hand) falls back to showing the id', async () => {
    installFetchMock()
    renderPage('emp1')
    await waitFor(() => expect(screen.getByTestId('history-employee-name')).toHaveTextContent('emp1'))
  })

  it('"Skift medarbejder" returns to the search panel', async () => {
    installFetchMock()
    renderPage('emp1')
    await waitFor(() => expect(screen.getByTestId('history-employee-name')).toBeInTheDocument())
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Skift medarbejder' }))
    expect(screen.getByTestId('history-search-panel')).toBeInTheDocument()
  })
})

describe('EmploymentHistoryPage — the three statuses', () => {
  it('renders PAST, CURRENT and SCHEDULED distinctly, and a SCHEDULED row is visibly not-yet-in-force', async () => {
    installFetchMock()
    renderPage('emp1')

    await waitFor(() => expect(screen.getByTestId('history-row-profile-0')).toBeInTheDocument())

    const pastRow = screen.getByTestId('history-row-profile-0')
    expect(within(pastRow).getByText('Tidligere')).toBeInTheDocument()
    expect(pastRow.className).not.toContain('scheduledRow')

    const currentRow = screen.getByTestId('history-row-profile-1')
    expect(within(currentRow).getByText('Gældende nu')).toBeInTheDocument()

    const scheduledRow = screen.getByTestId('history-row-agreement-1')
    expect(within(scheduledRow).getByText('Planlagt – endnu ikke i kraft')).toBeInTheDocument()
    // The wording alone is not the whole story — the row itself carries a distinct visual marker
    // (S141 B0: a scheduled change must never be mistaken for one already in force).
    expect(scheduledRow.className).toContain('scheduledRow')
  })

  it('interleaves both tracks by date WITHOUT inventing a boundary — each row keeps its own original interval', async () => {
    installFetchMock()
    renderPage('emp1')
    await waitFor(() => expect(screen.getByTestId('history-row-profile-0')).toBeInTheDocument())

    // Chronological read order across BOTH tracks: 2020 profile, 2020 agreement, 2024 profile,
    // 2026 agreement. Not "profile track then agreement track".
    const rows = screen.getAllByRole('row').slice(1) // drop the header row
    const testIds = rows.map((r) => r.getAttribute('data-testid'))
    expect(testIds).toEqual([
      'history-row-profile-0',
      'history-row-agreement-0',
      'history-row-profile-1',
      'history-row-agreement-1',
    ])

    // The agreement track's own 2020 interval (ending 2026-12-01) is rendered EXACTLY as the
    // server sent it — not truncated or merged against the profile track's 2024 boundary.
    // `da-DK` (jsdom/Node ICU) renders without zero-padding ("1.1.2020", not "01.01.2020").
    const firstAgreementRow = screen.getByTestId('history-row-agreement-0')
    expect(firstAgreementRow.textContent).toContain('1.1.2020')
    expect(firstAgreementRow.textContent).toContain('1.12.2026')
  })
})

describe('EmploymentHistoryPage — no history is not an error', () => {
  it('an employee with no records shows the empty state, not an error Alert', async () => {
    installFetchMock({ emp2: EMPTY_HISTORY })
    renderPage('emp2')
    await waitFor(() => expect(screen.getByTestId('history-empty')).toBeInTheDocument())
    expect(screen.getByTestId('history-empty').textContent).toContain('ingen registreret historik')
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('EmploymentHistoryPage — a malformed range is a refusal, not an empty result', () => {
  it('shows a refusal Alert when "from" is not earlier than "to", and never the empty-history message', async () => {
    installFetchMock()
    const user = userEvent.setup()
    renderPage('emp1')
    await waitFor(() => expect(screen.getByTestId('history-row-profile-0')).toBeInTheDocument())

    fireEvent.change(screen.getByLabelText('Fra dato'), { target: { value: '2025-06-01' } })
    fireEvent.change(screen.getByLabelText('Til dato'), { target: { value: '2025-01-01' } })
    await user.click(screen.getByRole('button', { name: 'Vis periode' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByRole('alert').textContent).toContain("'from' must be earlier than 'to'")
    expect(screen.queryByTestId('history-empty')).toBeNull()
  })
})

describe('EmploymentHistoryPage — access denied', () => {
  it('an out-of-scope or unknown employeeId shows an access-denied Alert that does not claim either way', async () => {
    installFetchMock({}) // no entry for emp-outside -> the mock answers 403
    renderPage('emp-outside')
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByRole('alert').textContent).toContain('ikke adgang')
  })
})
