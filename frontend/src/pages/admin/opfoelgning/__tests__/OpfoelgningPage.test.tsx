// SPRINT-140 / TASK-14006 (refinement B4) — the HR follow-up landing page.
// Pins, each independently falsifiable:
//  - all TEN tiles render;
//  - the §21 tile shows its closed state at an "October" anchor and its open
//    state (count + days-to-deadline) at a "November" anchor;
//  - colour appears on exactly the four decision-ready tiles (§21, leaver's
//    final month, past-deadline, approved-not-exported) and on NONE of the
//    other six, even though every tile in this fixture has a non-zero count;
//  - each tile opens its own list on the same page;
//  - a read-only list states its action is handled via the API (or points at
//    an existing screen) and never renders a form;
//  - the non-additive caveat (leaver's final month vs. past-deadline) is
//    visible on the page;
//  - the rolling lookback floor renders on the three history tiles;
//  - the eventual-consistency lag note renders on the two event-backed lists;
//  - the §21 list surfaces `cannotCompute` and `projectionNote`.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { StrictMode } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { OpfoelgningPage } from '../OpfoelgningPage'

vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'LocalHR' }),
}))
vi.mock('../../../../components/ui/Toast', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../components/ui/Toast')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function jsonResponse(body: unknown) {
  return {
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

const TODAY = '2025-09-09'
const LOOKBACK_FLOOR = '2024-09-09'

const worklistRows = [
  {
    worklistId: 'w1', employeeId: 'emp1', kind: 'EXPORTED_MONTH', year: 2025, month: 8,
    exportId: 'exp1', entitlementType: null, entitlementYear: null,
    triggers: [{ kind: 'PROFILE_CHANGE', eventId: 'ev1', effectiveFrom: '2025-08-01', appendedAt: '2025-08-02T10:00:00Z', actorId: 'admin1', baselineContentHash: null, baselineSettlementSequence: null, baselineSettlementState: null, recalculatedSince: null, reversedSince: null, recalcBlockedBy: null }],
    recalcBlockedBy: [], recalculatedSince: false, reversedSince: null,
    createdAt: '2025-08-02T10:00:00Z', createdBy: 'admin1',
    resolvedAt: null, resolvedBy: null, resolution: null, resolutionReason: null, version: 1,
  },
]

// past-deadline and leaver-final-month are the two "enumeration" reads the
// page asks for with `?summary=true` on first paint (counts only) and
// re-fetches WITHOUT it, once, the first time their tile opens (the real
// contract the server pins in `Summary_PastDeadline_CountsMatchFullMode_
// ListsNull`: `employeeLate`/`approverLate`/`items` are null in summary mode,
// counts are identical either way). The mock below MIRRORS that split rather
// than being more generous than the API — see `installFetchMock`.
const pastDeadlineCounts = {
  today: TODAY, lookbackFloor: LOOKBACK_FLOOR,
  employeeLateCount: 3, oldestEmployeeLateAnchor: '2025-09-01',
  approverLateCount: 2, oldestApproverLateAnchor: '2025-09-04',
}
const pastDeadlineSummaryResponse = { ...pastDeadlineCounts, employeeLate: null, approverLate: null }
const pastDeadlineFullResponse = {
  ...pastDeadlineCounts,
  employeeLate: [{ employeeId: 'e1', displayName: 'Anna And', orgId: 'org1', year: 2025, month: 8, periodStatus: 'DRAFT', periodId: null, ageAnchor: '2025-09-01', daysPastAnchor: 8, deadlineSource: 'stored' }],
  approverLate: [{ employeeId: 'e2', displayName: 'Bo Berg', orgId: 'org1', year: 2025, month: 8, periodStatus: 'SUBMITTED', periodId: 'p1', ageAnchor: '2025-09-04', daysPastAnchor: 5, deadlineSource: 'computed' }],
}

const leaverCounts = { today: TODAY, lookbackFloor: LOOKBACK_FLOOR, count: 1, oldestAnchor: '2025-08-20' }
const leaverSummaryResponse = { ...leaverCounts, items: null }
const leaverFullResponse = {
  ...leaverCounts,
  items: [{ employeeId: 'e3', displayName: 'Carl Christ', orgId: 'org1', year: 2025, month: 8, periodStatus: 'DRAFT', periodId: null, ageAnchor: '2025-08-20', daysPastAnchor: 20, deadlineSource: 'stored' }],
}

const notExportedResponse = {
  today: TODAY, lookbackFloor: LOOKBACK_FLOOR, count: 1, oldestAnchor: '2025-09-05',
  items: [{ employeeId: 'e4', displayName: 'Dora Dam', orgId: 'org1', year: 2025, month: 8, periodStatus: 'APPROVED', periodId: 'p2', ageAnchor: '2025-09-05', daysPastAnchor: 4, deadlineSource: 'stored' }],
}

const uncoveredResponse = {
  today: TODAY, orphanCount: 2,
  orphans: [{ employeeId: 'e5', displayName: 'Erik Elm', orgId: 'org1', unitName: 'Vejledning' }],
  expiredDelegationCount: 1, expiredWithoutActiveCoverCount: 1, oldestExpiry: '2025-08-25',
  expiredDelegations: [{ vikarId: 'v1', absentApproverId: 'a1', absentApproverName: 'Fie Falk', vikarUserId: 'v1u', vikarUserName: 'Gitte Grib', orgId: 'org1', unitName: 'Vejledning', reason: 'FERIE', untilDate: '2025-08-20', expiredOn: '2025-08-25', expiredAt: '2025-08-25', daysSinceExpiry: 15, approverHasActiveCover: false }],
  windowDays: 30, eventSourceLagNote: 'Opdateres ca. et publikationsinterval efter hændelsen.',
}

const cannotRegisterResponse = {
  today: TODAY, count: 1, oldestGapSince: '2025-09-02',
  items: [{ employeeId: 'e6', displayName: 'Hans Holm', orgId: 'org1', unitName: 'Vejledning', gapSince: '2025-09-02', daysSinceGapStart: 7 }],
}

const settlementReviewsResponse = {
  items: [{ employeeId: 'e7', entitlementType: 'VACATION', entitlementYear: 2024, settlementSequence: 1, source: 'event', settlementState: 'SETTLED', trigger: 'TERMINATION', reviewDisposition: null, flaggedDays: 2, version: 1, ageAnchor: '2025-08-30T00:00:00Z', ageDays: 10, primaryOrgId: 'org1' }],
  count: 1, today: TODAY, eventSourceLagNote: 'Opdateres ca. et publikationsinterval efter hændelsen.',
}

const terminationPayoutsResponse = {
  items: [{ employeeId: 'e8', entitlementType: 'VACATION', entitlementYear: 2024, settlementSequence: 1, crystallizedDays: 4, version: 1, ageAnchor: '2025-09-03T00:00:00Z', ageDays: 6, primaryOrgId: 'org1' }],
  count: 1, today: TODAY,
}

const payoutPendingResponse = {
  items: [{ employeeId: 'e9', entitlementType: 'VACATION', entitlementYear: 2024, sequence: 1, payoutDays: 2.5, version: 1, settledAt: '2025-08-15T10:00:00Z', primaryOrgId: 'org1' }],
  count: 1,
}

const transferAgreementsOpen = {
  items: [{ employeeId: 'e10', primaryOrgId: 'org1', entitlementYear: 2024, underCapDays: 3, carryoverMax: 5, deadline: '2025-12-31', daysToDeadline: 20, ageAnchorDate: '2025-11-01' }],
  count: 1, entitlementYear: 2024, windowOpen: true, windowOpensOn: '2025-11-01', deadline: '2025-12-31', daysToDeadline: 20,
  cannotCompute: [{ employeeId: 'e11', primaryOrgId: 'org1', reason: 'VALUATION_FAILED' }], cannotComputeCount: 1,
  today: '2025-11-11', projectionNote: 'Listen er en foreløbig beregning indtil årsafslutningen gør den endelig.',
}

const transferAgreementsClosed = {
  items: [], count: 0, entitlementYear: 2024, windowOpen: false, windowOpensOn: '2025-11-01', deadline: '2025-12-31', daysToDeadline: 0,
  cannotCompute: [], cannotComputeCount: 0, today: '2025-10-15', projectionNote: 'Listen er en foreløbig beregning indtil årsafslutningen gør den endelig.',
}

function installFetchMock(
  transferAgreements: unknown = transferAgreementsOpen,
  overrides: {
    leaverSummary?: unknown
    leaverFull?: unknown
  } = {},
) {
  const leaverSummary = overrides.leaverSummary ?? leaverSummaryResponse
  const leaverFull = overrides.leaverFull ?? leaverFullResponse
  mockFetch.mockImplementation(async (url: string) => {
    if (url.includes('/api/hr/backdate-worklist')) return jsonResponse(worklistRows)
    // W-3 fix (Step-7a review): the mock must MIRROR the summary/full contract,
    // not be more generous than the API — a mock that returns the full body for
    // both requests would let a page that reuses the summary response for its
    // list pass this suite by accident. `summary=true` -> counts-only; anything
    // else (including an explicit `summary=false`, what the lazy full fetch
    // sends) -> the full item list.
    if (url.includes('/api/hr/follow-up/past-deadline')) {
      return jsonResponse(url.includes('summary=true') ? pastDeadlineSummaryResponse : pastDeadlineFullResponse)
    }
    if (url.includes('/api/hr/follow-up/leaver-final-month')) {
      return jsonResponse(url.includes('summary=true') ? leaverSummary : leaverFull)
    }
    if (url.includes('/api/hr/follow-up/approved-not-exported')) return jsonResponse(notExportedResponse)
    if (url.includes('/api/hr/follow-up/uncovered-approvers')) return jsonResponse(uncoveredResponse)
    if (url.includes('/api/hr/follow-up/cannot-register')) return jsonResponse(cannotRegisterResponse)
    if (url.includes('/api/hr/follow-up/settlement-reviews')) return jsonResponse(settlementReviewsResponse)
    if (url.includes('/api/hr/follow-up/termination-payouts-unrequested')) return jsonResponse(terminationPayoutsResponse)
    if (url.includes('/api/hr/follow-up/transfer-agreements-needed')) return jsonResponse(transferAgreements)
    if (url.includes('/api/vacation-settlements/payout-pending')) return jsonResponse(payoutPendingResponse)
    throw new Error(`Unmocked fetch in OpfoelgningPage test: ${url}`)
  })
}

function renderPage(initialTile?: string, strict = false) {
  const path = initialTile ? `/admin/opfoelgning/${initialTile}` : '/admin/opfoelgning'
  const tree = (
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/opfoelgning" element={<OpfoelgningPage />} />
        <Route path="/admin/opfoelgning/:tile" element={<OpfoelgningPage />} />
      </Routes>
    </MemoryRouter>
  )
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree)
}

const READY_TILE_TESTIDS = ['tile-past-deadline', 'tile-leaver-final-month', 'tile-approved-not-exported', 'tile-transfer-agreements']
const NOT_READY_TILE_TESTIDS = ['tile-worklist', 'tile-uncovered-approvers', 'tile-cannot-register', 'tile-settlement-reviews', 'tile-termination-payouts', 'tile-payout-pending']

beforeEach(() => {
  mockFetch.mockReset()
})

describe('OpfoelgningPage — the ten tiles', () => {
  it('renders all ten tiles', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-worklist-count')).toBeInTheDocument())
    for (const id of [...READY_TILE_TESTIDS, ...NOT_READY_TILE_TESTIDS]) {
      expect(screen.getByTestId(id)).toBeInTheDocument()
    }
  })

  it('colour appears on exactly the four decision-ready tiles, and on none of the other six — despite every tile here having a non-zero count', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-worklist-count').textContent).toBe('1'))
    // Sanity: every "not ready" tile DOES have a non-zero count in this fixture,
    // so an un-gated colour rule would light all ten up, not just four.
    expect(screen.getByTestId('tile-uncovered-approvers-count').textContent).toBe('2')
    expect(screen.getByTestId('tile-settlement-reviews-count').textContent).toBe('1')

    for (const id of READY_TILE_TESTIDS) {
      expect(screen.getByTestId(`${id}-tile`).dataset.coloured).toBe('true')
    }
    for (const id of NOT_READY_TILE_TESTIDS) {
      expect(screen.getByTestId(`${id}-tile`).dataset.coloured).toBe('false')
    }
  })

  it('the non-additive caveat (leaver vs. past-deadline) is visible on the page', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-overlap-caveat')).toBeInTheDocument())
    expect(screen.getByTestId('tile-overlap-caveat').textContent).toContain('må ikke lægges sammen')
  })

  it('the rolling lookback floor renders on the three history tiles', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-past-deadline-footnote')).toBeInTheDocument())
    expect(screen.getByTestId('tile-past-deadline-footnote').textContent).toContain(LOOKBACK_FLOOR)
    expect(screen.getByTestId('tile-leaver-final-month-footnote').textContent).toContain(LOOKBACK_FLOOR)
    expect(screen.getByTestId('tile-approved-not-exported-footnote').textContent).toContain(LOOKBACK_FLOOR)
    // None of the six non-history tiles carries this footnote.
    expect(screen.queryByTestId('tile-payout-pending-footnote')).toBeNull()
  })
})

describe('OpfoelgningPage — the "oldest" number never goes negative (N-2, Step-7a review)', () => {
  it('clamps the leaver-final-month tile at 0 when its oldest anchor is still in the future', async () => {
    // Leaver-final-month is "open = all" (S139 ruling): unlike past-deadline
    // (only ever listed once overdue), it lists a leaver's final month whether
    // or not its deadline has passed — so `oldestAnchor` can be AFTER `today`
    // (a leaver whose final month is the current one). Unclamped, this would
    // render "Ældste: -11 dage".
    const futureAnchorLeaver = { today: TODAY, lookbackFloor: LOOKBACK_FLOOR, count: 1, oldestAnchor: '2025-09-20' }
    installFetchMock(transferAgreementsOpen, {
      leaverSummary: { ...futureAnchorLeaver, items: null },
      leaverFull: { ...futureAnchorLeaver, items: [] },
    })
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-leaver-final-month-oldest')).toBeInTheDocument())
    expect(screen.getByTestId('tile-leaver-final-month-oldest').textContent).toBe('Ældste: 0 dage')
  })

  it('clamps the approved-not-exported tile the same way', async () => {
    installFetchMock(transferAgreementsOpen)
    // approved-not-exported is likewise "open = all" — reuse the standard
    // fixture's endpoint but override just this one response inline.
    const baseImpl = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string) => {
      if (url.includes('/api/hr/follow-up/approved-not-exported')) {
        return jsonResponse({ today: TODAY, lookbackFloor: LOOKBACK_FLOOR, count: 1, oldestAnchor: '2025-09-15', items: [] })
      }
      return baseImpl(url)
    })
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-approved-not-exported-oldest')).toBeInTheDocument())
    expect(screen.getByTestId('tile-approved-not-exported-oldest').textContent).toBe('Ældste: 0 dage')
  })
})

describe('OpfoelgningPage — the §21 tile window states', () => {
  it('shows the CLOSED state at an "October" anchor — never a "0" count', async () => {
    installFetchMock(transferAgreementsClosed)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-transfer-agreements-state')).toBeInTheDocument())
    expect(screen.getByTestId('tile-transfer-agreements-state').textContent).toContain('1. november')
    expect(screen.queryByTestId('tile-transfer-agreements-count')).toBeNull()
  })

  it('shows the OPEN state (count + days-to-deadline) at a "November" anchor', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-transfer-agreements-count')).toBeInTheDocument())
    expect(screen.getByTestId('tile-transfer-agreements-count').textContent).toBe('1')
    expect(screen.getByTestId('tile-transfer-agreements-footnote').textContent).toContain('20 dage')
  })
})

describe('OpfoelgningPage — each tile opens its list', () => {
  it('opens the worklist list', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-worklist')).toBeInTheDocument())
    await user.click(screen.getByTestId('tile-worklist'))
    await waitFor(() => expect(screen.getByText('Bagudrettede rettelser', { selector: 'h2' })).toBeInTheDocument())
  })

  it('opens the past-deadline list (first paint asks for `summary=true`; the list only renders once the lazy FULL fetch lands)', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-past-deadline')).toBeInTheDocument())

    // The first-paint request(s) for this endpoint must carry `summary=true` —
    // the mechanism that stops the landing page pulling ten full lists just to
    // render ten tile counts.
    const pastDeadlineCalls = () => mockFetch.mock.calls.map(([url]) => String(url)).filter((u) => u.includes('/api/hr/follow-up/past-deadline'))
    expect(pastDeadlineCalls().length).toBeGreaterThan(0)
    expect(pastDeadlineCalls().every((u) => u.includes('summary=true'))).toBe(true)

    await user.click(screen.getByTestId('tile-past-deadline'))
    // This assertion is only meaningful because the mock's summary response
    // carries `employeeLate: null` (mirroring the real API): if the page wrongly
    // reused the already-loaded summary data instead of fetching the full
    // response, this row would be ABSENT, not merely stale.
    await waitFor(() => expect(screen.getByTestId('list-past-deadline')).toBeInTheDocument())
    expect(within(screen.getByTestId('list-past-deadline')).getByText('Anna And')).toBeInTheDocument()

    // And the fetch that made it possible was a NON-summary (full) request.
    expect(pastDeadlineCalls().some((u) => !u.includes('summary=true'))).toBe(true)
  })

  it('opens the §21 list directly via the route param', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage('transfer-agreements')
    await waitFor(() => expect(screen.getByTestId('list-transfer-agreements')).toBeInTheDocument())
  })
})

// S147 TIDY-4 item 23b + fix-up 1 (Fable W1, N6). The leaver list's send / approve / reject must
// refresh the landing tiles AND leave no stale cached full list behind.
describe('OpfoelgningPage — a resolved leaver month refreshes the tiles and the cached lists', () => {
  const PERIOD = 'p-leaver'
  const submittedLeaver = {
    ...leaverFullResponse,
    items: [{ employeeId: 'e3', displayName: 'Carl Christ', orgId: 'org1', year: 2025, month: 8, periodStatus: 'SUBMITTED', periodId: PERIOD, ageAnchor: '2025-08-20', daysPastAnchor: 20, deadlineSource: 'stored', lastManagerDay: '2025-08-18', firstHrOnlyDay: '2025-08-19' }],
  }
  const approvedLeaver = {
    ...submittedLeaver,
    items: [{ ...submittedLeaver.items[0], periodStatus: 'APPROVED' }],
  }
  const calls = (needle: string, summary: boolean | null = null) =>
    mockFetch.mock.calls
      .map(([url]) => String(url))
      .filter((u) => u.includes(needle) && (summary === null || u.includes('summary=true') === summary))

  function installLeaverMock(approvedRef: { done: boolean }) {
    installFetchMock(transferAgreementsOpen, { leaverFull: submittedLeaver })
    const base = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string) => {
      if (url.includes('/api/approval/') && url.endsWith('/approve')) {
        approvedRef.done = true
        return jsonResponse({ periodId: PERIOD, status: 'APPROVED' })
      }
      if (approvedRef.done && url.includes('/api/hr/follow-up/leaver-final-month') && !url.includes('summary=true')) {
        return jsonResponse(approvedLeaver)
      }
      return base(url)
    })
  }

  // N6, kind F against 94bd32be. Red conditions: before the change the leaver list had no onResolved,
  // so after an approve each of the three summaries was fetched exactly once (on mount); the
  // second-call assertions below fail with a count of 1.
  it('an approve reloads the leaver, approved-not-exported and past-deadline summaries', async () => {
    installLeaverMock({ done: false })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-leaver-final-month'))
    await user.click(await screen.findByTestId('list-leaver-final-month-approve-e3-2025-8'))
    await waitFor(() => expect(calls('/api/hr/follow-up/leaver-final-month', true).length).toBe(2))
    await waitFor(() => expect(calls('/api/hr/follow-up/approved-not-exported').length).toBe(2))
    await waitFor(() => expect(calls('/api/hr/follow-up/past-deadline', true).length).toBe(2))
  })

  // W1, kind F against 94bd32be. Red conditions: before the fix the full list stayed cached, so
  // coming back to the tile showed the old SUBMITTED row (status "Indsendt", Godkend offered) and
  // no second non-summary leaver fetch happened. The approved row's outcome sentence must survive
  // while the list is on screen (no reload of an open list).
  it('after an approve, leaving the tile and coming back refetches the list (no stale "Indsendt" row)', async () => {
    installLeaverMock({ done: false })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-leaver-final-month'))
    await user.click(await screen.findByTestId('list-leaver-final-month-approve-e3-2025-8'))
    await waitFor(() => expect(screen.getByTestId('list-leaver-final-month-status-e3-2025-8')).toHaveTextContent('Godkendt'))
    // The open list was not reloaded: the row's own outcome sentence is still there.
    expect(screen.getByTestId('list-leaver-final-month-outcome-e3-2025-8')).toBeInTheDocument()
    expect(calls('/api/hr/follow-up/leaver-final-month', false).length).toBe(1)

    await user.click(screen.getByTestId('tile-approved-not-exported'))
    await user.click(await screen.findByTestId('tile-leaver-final-month'))
    await waitFor(() => expect(calls('/api/hr/follow-up/leaver-final-month', false).length).toBe(2))
    await waitFor(() => expect(screen.getByTestId('list-leaver-final-month-status-e3-2025-8')).toHaveTextContent('Godkendt'))
    expect(screen.queryByTestId('list-leaver-final-month-approve-e3-2025-8')).not.toBeInTheDocument()
  })
})

// S147 TIDY-4 fix-up 2 (Codex c2 W, Fable c2 N1): a lazy full-list fetch that is cancelled before it
// lands must not leave the list stuck on "loading".
describe('OpfoelgningPage — a cancelled lazy fetch is retried on the next activation', () => {
  // Kind F against f1af775b (the Codex sequence). Red conditions: after approve the cache is stale;
  // opening the tile starts refetch #2, leaving mid-fetch cancels it, and f1af775b had already consumed
  // the stale flag, so the third activation fetched nothing: the third non-summary leaver call never
  // happens and the list stays "loading" (waitFor on the third call times out).
  it('approve, leave, return, leave before the refetch lands, return: the list fetches again', async () => {
    installFetchMock(transferAgreementsOpen, {
      leaverFull: {
        ...leaverFullResponse,
        items: [{ employeeId: 'e3', displayName: 'Carl Christ', orgId: 'org1', year: 2025, month: 8, periodStatus: 'SUBMITTED', periodId: 'p-leaver', ageAnchor: '2025-08-20', daysPastAnchor: 20, deadlineSource: 'stored', lastManagerDay: '2025-08-18', firstHrOnlyDay: '2025-08-19' }],
      },
    })
    const base = mockFetch.getMockImplementation()!
    let approved = false
    let hold: Promise<void> | null = null
    const fullCalls = () => mockFetch.mock.calls.map(([u]) => String(u)).filter((u) => u.includes('/api/hr/follow-up/leaver-final-month') && !u.includes('summary=true')).length
    mockFetch.mockImplementation(async (url: string) => {
      if (url.includes('/api/approval/') && url.endsWith('/approve')) {
        approved = true
        return jsonResponse({ periodId: 'p-leaver', status: 'APPROVED' })
      }
      if (url.includes('/api/hr/follow-up/leaver-final-month') && !url.includes('summary=true')) {
        if (hold) await hold
        if (approved) {
          const r = await base(url)
          const body = await r.json()
          return jsonResponse({ ...body, items: body.items.map((i: object) => ({ ...i, periodStatus: 'APPROVED' })) })
        }
      }
      return base(url)
    })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-leaver-final-month'))
    await user.click(await screen.findByTestId('list-leaver-final-month-approve-e3-2025-8'))
    await waitFor(() => expect(screen.getByTestId('list-leaver-final-month-status-e3-2025-8')).toHaveTextContent('Godkendt'))
    await user.click(screen.getByTestId('tile-approved-not-exported'))

    // Refetch #2 is held, and the person leaves before it lands.
    let release: () => void = () => {}
    hold = new Promise<void>((r) => { release = r })
    await user.click(screen.getByTestId('tile-leaver-final-month'))
    await waitFor(() => expect(fullCalls()).toBe(2))
    await user.click(screen.getByTestId('tile-approved-not-exported'))

    // Back again, nothing held: the cancelled fetch must be retried.
    hold = null
    await user.click(screen.getByTestId('tile-leaver-final-month'))
    await waitFor(() => expect(fullCalls()).toBe(3))
    await waitFor(() => expect(screen.getByTestId('list-leaver-final-month-status-e3-2025-8')).toHaveTextContent('Godkendt'))
    release()
  })

  // Kind F against 94bd32be (pre-existing since S140, dev only). Red conditions: under React.StrictMode
  // the mount effect runs, cleans up and runs again; the first run set the started flag and its fetch
  // was cancelled by the cleanup, so the second run returned early and the list never left "loading".
  it('under StrictMode a direct load of a lazy tile leaves "loading"', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage('past-deadline', true)
    await waitFor(() => expect(screen.getByTestId('list-past-deadline')).toBeInTheDocument())
    expect(within(screen.getByTestId('list-past-deadline')).getByText('Anna And')).toBeInTheDocument()
  })

  // Boundary pin - holds today: a normal tile click fetches the full list exactly once.
  it('a normal tile click fetches the full list once (no double fetch)', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-past-deadline'))
    await waitFor(() => expect(screen.getByTestId('list-past-deadline')).toBeInTheDocument())
    const full = mockFetch.mock.calls.map(([u]) => String(u)).filter((u) => u.includes('/api/hr/follow-up/past-deadline') && !u.includes('summary=true'))
    expect(full.length).toBe(1)
  })
})

describe('OpfoelgningPage — read-only lists (owner ruling OQ-4)', () => {
  it('the settlement-reviews list states the action is handled via API and offers no form', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-settlement-reviews')).toBeInTheDocument())
    await user.click(screen.getByTestId('tile-settlement-reviews'))
    const list = await screen.findByTestId('list-settlement-reviews')
    expect(within(list).getByTestId('list-settlement-reviews-action-note').textContent).toContain('S141')
    expect(list.querySelector('textarea')).toBeNull()
    expect(list.querySelector('input')).toBeNull()
    expect(list.querySelector('form')).toBeNull()
  })

  it('the settlement-reviews and uncovered-approvers lists carry the eventual-consistency lag note', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-settlement-reviews'))
    expect((await screen.findByTestId('list-settlement-reviews-lag-note')).textContent).toContain('publikationsinterval')

    await user.click(await screen.findByTestId('tile-uncovered-approvers'))
    expect((await screen.findByTestId('list-uncovered-approvers-lag-note')).textContent).toContain('publikationsinterval')
  })

  it('the §21 list surfaces cannotCompute and the projectionNote', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage('transfer-agreements')
    const list = await screen.findByTestId('list-transfer-agreements')
    expect(within(list).getByTestId('list-transfer-agreements-cannot-compute').textContent).toContain('1')
    expect(within(list).getByTestId('list-transfer-agreements-projection-note').textContent).toContain('foreløbig')
    expect(list.querySelector('form')).toBeNull()
  })

  it('the payout-pending list states its action is handled via the API and offers no form', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByTestId('tile-payout-pending'))
    const list = await screen.findByTestId('list-payout-pending')
    expect(within(list).getByTestId('list-payout-pending-action-note').textContent).toContain('reconcile')
    expect(list.querySelector('form')).toBeNull()
  })
})
