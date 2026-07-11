// S116 / TASK-11602 — WIRE-level pins for the Pass-3 typed approval-bucket
// switch (PAT-012). These exercise the REAL typed apiClient path (stubbed
// fetch, no hook mock), pinning the byte-equivalence the switch promised:
//
//  • the pending/by-month reads hit the exact legacy URLs (query building
//    included — `my-reports=true` verbatim);
//  • the team-overview / allocation-breakdown reads interpolate + query
//    identically;
//  • the delegate trio: GET url; POST body verbatim; DELETE with NO body and
//    the typed `{revokedCount}` derivation (previously declared `<void>`);
//  • THE NAMED REQUEST DELTA (the S112 rule): employee-approve binds NO request
//    DTO — the legacy calls sent a literal `{}` body, the typed form sends NO
//    body (the backend handler takes only the periodId route param and never
//    reads a body — ApprovalEndpoints.cs:1344). Pinned as `body === undefined`.
//  • submit / reopen mutation bodies stay byte-identical.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import {
  usePendingApprovals,
  usePendingMyReports,
  useApprovalsByMonth,
  useMyReportsByMonth,
} from '../useApprovals'
import { useTeamOverview } from '../useTeamOverview'
import { useAllocationBreakdown } from '../useAllocationBreakdown'
import { useDelegation } from '../useDelegation'
import { useSkema } from '../useSkema'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

type Captured = { url: string; method: string; body: unknown }

/** Capture every fetch call's (url, method, parsed body); respond per-URL. */
function captureCalls(respond: (url: string) => unknown = () => []) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    })
    return {
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => respond(url),
      text: async () => JSON.stringify(respond(url)),
    }
  })
  return calls
}

beforeEach(() => {
  mockFetch.mockReset()
})

// ── the pending / by-month reads (useApprovals) ──────────────────────────────

describe('useApprovals — the 4 typed reads hit the exact legacy URLs', () => {
  it('usePendingApprovals → GET /api/approval/pending', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => usePendingApprovals())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/pending')
    expect(calls[0].method).toBe('GET')
  })

  it('usePendingMyReports → GET /api/approval/pending?my-reports=true', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => usePendingMyReports())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/pending?my-reports=true')
  })

  it('useApprovalsByMonth → GET /api/approval/by-month?year=&month=', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => useApprovalsByMonth(2026, 7))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/by-month?year=2026&month=7')
  })

  it('useMyReportsByMonth → GET /api/approval/by-month?year=&month=&my-reports=true', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => useMyReportsByMonth(2026, 7))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/by-month?year=2026&month=7&my-reports=true')
  })

  it('the spec-derived rows flow through (the 9-field ApprovalPeriodListItem)', async () => {
    const item = {
      periodId: 'p-1',
      employeeId: 'EMP001',
      orgId: 'STY01',
      periodStart: '2026-07-01',
      periodEnd: '2026-07-31',
      periodType: 'MONTHLY',
      status: 'SUBMITTED',
      submittedAt: '2026-07-05T08:00:00Z',
      agreementCode: 'AC',
    }
    captureCalls(() => [item])
    const { result } = renderHook(() => usePendingApprovals())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.periods).toEqual([item])
  })
})

// ── the team-overview read (useTeamOverview) ─────────────────────────────────

describe('useTeamOverview — typed read', () => {
  it('GET /api/approval/team-overview?year=&month= and unwraps {employees}', async () => {
    const overviewRow = {
      periodId: 'p-1',
      employeeId: 'emp001',
      displayName: 'Anna Berg',
      agreement: 'AC',
      status: 'SUBMITTED',
      submittedAt: '2026-07-05T08:00:00Z',
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
    }
    const calls = captureCalls((url) =>
      url.includes('team-overview') ? { employees: [overviewRow] } : [],
    )
    const { result } = renderHook(() => useTeamOverview(2026, 7))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/team-overview?year=2026&month=7')
    expect(result.current.rows).toEqual([overviewRow])
  })
})

// ── the allocation-breakdown read (useAllocationBreakdown) ───────────────────

describe('useAllocationBreakdown — typed read', () => {
  it('GET /api/approval/{employeeId}/allocation-breakdown?year=&month= (interpolated)', async () => {
    const breakdown = {
      allocations: [{ taskId: 'PRJ-1', hours: 12 }],
      worked: 12,
      allocated: 12,
      underAllocated: 0,
      overAllocated: 0,
      hasAllocationImbalance: false,
    }
    const calls = captureCalls(() => breakdown)
    const { result } = renderHook(() => useAllocationBreakdown('EMP001', 2026, 7))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/approval/EMP001/allocation-breakdown?year=2026&month=7')
    expect(result.current.data).toEqual(breakdown)
  })
})

// ── the delegate trio (useDelegation) ────────────────────────────────────────

describe('useDelegation — the typed GET/POST/DELETE trio', () => {
  it('fetchStatus → GET /api/reporting-lines/delegate', async () => {
    const status = {
      active: true,
      actingManagerId: 'MGR9',
      effectiveFrom: '2026-07-01',
      effectiveTo: '2026-07-31',
      delegatedEmployees: [{ employeeId: 'EMP001', displayName: null }],
    }
    const calls = captureCalls(() => status)
    const { result } = renderHook(() => useDelegation())
    const res = await result.current.fetchStatus()
    expect(calls[0].url).toBe('/api/reporting-lines/delegate')
    expect(calls[0].method).toBe('GET')
    expect(res.ok).toBe(true)
    // The spec-honest nullable displayName flows through (the hand-written
    // interface claimed non-null — deleted in S116).
    if (res.ok) expect(res.data.delegatedEmployees[0].displayName).toBeNull()
  })

  it('createDelegation → POST with the body verbatim', async () => {
    const calls = captureCalls(() => ({
      delegatedCount: 3,
      skippedCount: 0,
      actingManagerId: 'MGR9',
      effectiveFrom: '2026-07-11',
      effectiveTo: '2026-07-31',
    }))
    const { result } = renderHook(() => useDelegation())
    const res = await result.current.createDelegation({
      actingManagerId: 'MGR9',
      effectiveTo: '2026-07-31',
    })
    expect(calls[0].url).toBe('/api/reporting-lines/delegate')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].body).toEqual({ actingManagerId: 'MGR9', effectiveTo: '2026-07-31' })
    expect(res.ok).toBe(true)
    if (res.ok) expect(res.data.delegatedCount).toBe(3)
  })

  it('cancelDelegation → DELETE with NO body; the typed form derives {revokedCount} (previously <void>)', async () => {
    const calls = captureCalls(() => ({ revokedCount: 3 }))
    const { result } = renderHook(() => useDelegation())
    const res = await result.current.cancelDelegation()
    expect(calls[0].url).toBe('/api/reporting-lines/delegate')
    expect(calls[0].method).toBe('DELETE')
    expect(calls[0].body).toBeUndefined()
    expect(res.ok).toBe(true)
    // The genuine 200 body is now typed and available — consumers may keep
    // discarding it, but the derivation is proven here.
    if (res.ok) expect(res.data.revokedCount).toBe(3)
  })
})

// ── the useSkema approval slice ──────────────────────────────────────────────

describe('useSkema approval slice — typed mutations (incl. THE named no-body delta)', () => {
  /** Route the month GET to an empty month; capture everything. */
  function skemaCalls() {
    return captureCalls((url) => {
      if (url.includes('/month')) return {}
      if (url.endsWith('/api/approval/submit')) return { periodId: 'p-new', status: 'SUBMITTED' }
      return { periodId: 'p-1', status: 'EMPLOYEE_APPROVED' }
    })
  }

  async function mountSkema() {
    const rendered = renderHook(() => useSkema('EMP001', 2026, 7))
    await waitFor(() => expect(rendered.result.current.loading).toBe(false))
    return rendered
  }

  it('employeeApprove: POST /api/approval/{periodId}/employee-approve with NO body (was `{}` — the named delta)', async () => {
    const calls = skemaCalls()
    const { result } = await mountSkema()
    await act(async () => { await result.current.employeeApprove('p-1') })
    const post = calls.find(c => c.method === 'POST')!
    expect(post.url).toBe('/api/approval/p-1/employee-approve')
    expect(post.body).toBeUndefined() // legacy sent {}; the op binds no DTO
  })

  it('submitAndApprove: submit body byte-identical, then employee-approve on the returned periodId with NO body', async () => {
    const calls = skemaCalls()
    const { result } = await mountSkema()
    await act(async () => { await result.current.submitAndApprove('STY01', 'AC') })
    const posts = calls.filter(c => c.method === 'POST')
    expect(posts[0].url).toBe('/api/approval/submit')
    expect(posts[0].body).toEqual({
      employeeId: 'EMP001',
      orgId: 'STY01',
      periodStart: '2026-07-01',
      periodEnd: '2026-07-31',
      periodType: 'MONTHLY',
      agreementCode: 'AC',
      okVersion: 'OK26',
    })
    expect(posts[1].url).toBe('/api/approval/p-new/employee-approve')
    expect(posts[1].body).toBeUndefined()
  })

  it('reopenPeriod: POST /api/approval/{periodId}/reopen with the default reason body', async () => {
    const calls = skemaCalls()
    const { result } = await mountSkema()
    await act(async () => { await result.current.reopenPeriod('p-1') })
    const post = calls.find(c => c.method === 'POST')!
    expect(post.url).toBe('/api/approval/p-1/reopen')
    expect(post.body).toEqual({ reason: 'Genåbnet af medarbejder' })
  })
})
