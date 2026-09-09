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

const pastDeadlineResponse = {
  today: TODAY, lookbackFloor: LOOKBACK_FLOOR,
  employeeLateCount: 3, oldestEmployeeLateAnchor: '2025-09-01',
  approverLateCount: 2, oldestApproverLateAnchor: '2025-09-04',
  employeeLate: [{ employeeId: 'e1', displayName: 'Anna And', orgId: 'org1', year: 2025, month: 8, periodStatus: 'DRAFT', periodId: null, ageAnchor: '2025-09-01', daysPastAnchor: 8, deadlineSource: 'stored' }],
  approverLate: [{ employeeId: 'e2', displayName: 'Bo Berg', orgId: 'org1', year: 2025, month: 8, periodStatus: 'SUBMITTED', periodId: 'p1', ageAnchor: '2025-09-04', daysPastAnchor: 5, deadlineSource: 'computed' }],
}

const leaverResponse = {
  today: TODAY, lookbackFloor: LOOKBACK_FLOOR, count: 1, oldestAnchor: '2025-08-20',
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

function installFetchMock(transferAgreements: unknown) {
  mockFetch.mockImplementation(async (url: string) => {
    if (url.includes('/api/hr/backdate-worklist')) return jsonResponse(worklistRows)
    if (url.includes('/api/hr/follow-up/past-deadline')) return jsonResponse(pastDeadlineResponse)
    if (url.includes('/api/hr/follow-up/leaver-final-month')) return jsonResponse(leaverResponse)
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

function renderPage(initialTile?: string) {
  const path = initialTile ? `/admin/opfoelgning/${initialTile}` : '/admin/opfoelgning'
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/opfoelgning" element={<OpfoelgningPage />} />
        <Route path="/admin/opfoelgning/:tile" element={<OpfoelgningPage />} />
      </Routes>
    </MemoryRouter>,
  )
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

  it('opens the past-deadline list (lazily fetching the full, non-summary response)', async () => {
    installFetchMock(transferAgreementsOpen)
    const user = userEvent.setup()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('tile-past-deadline')).toBeInTheDocument())
    await user.click(screen.getByTestId('tile-past-deadline'))
    await waitFor(() => expect(screen.getByTestId('list-past-deadline')).toBeInTheDocument())
    expect(within(screen.getByTestId('list-past-deadline')).getByText('Anna And')).toBeInTheDocument()
  })

  it('opens the §21 list directly via the route param', async () => {
    installFetchMock(transferAgreementsOpen)
    renderPage('transfer-agreements')
    await waitFor(() => expect(screen.getByTestId('list-transfer-agreements')).toBeInTheDocument())
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
