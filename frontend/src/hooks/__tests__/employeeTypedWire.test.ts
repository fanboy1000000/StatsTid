// S120 / TASK-12001 — WIRE-level pins for the Pass-7 bucket-C typed switch
// (PAT-012): the final employee-facing surface. These exercise the REAL typed
// apiClient path (stubbed fetch, no hook mock), pinning:
//
//  • URLs byte-identical to the legacy template strings for every switched
//    read (time-entries, flex, summary, year-overview, compliance ×2,
//    compensation-choice, skema month);
//  • the FE-called mutations are UNCONDITIONED — no If-Match and no
//    If-None-Match on ANY of them (time-entries POST, skema save POST,
//    compensation-choice PUT, row-preferences PUT — the S119 doubly-pinned
//    precedent);
//  • request key sets byte-unchanged (the time-entries register body passes
//    through verbatim; the skema save body keeps the exact
//    year/month/entries/absences/workTime key set with the empty→null
//    collapsing; the choice PUT sends exactly { periodYear, compensationModel });
//  • the time-entries create consumes the declared 201 receipt
//    ({ eventId, streamId } — streamId surfaced additively);
//  • the flex BOTH-branch handling (ruling #1 delta-pinned FE-side): the
//    normalized no-history shape (nulls, NO `message`) and the with-history
//    shape both flow through to state unchanged;
//  • the compliance integer enums flow to state as INTEGERS (the masked
//    prod-bug class — see ComplianceWarnings.test for the render pins).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { useTimeEntries } from '../useTimeEntries'
import { useFlexBalance } from '../useFlexBalance'
import { useBalanceSummary } from '../useBalanceSummary'
import { useYearOverview } from '../useYearOverview'
import { useCompliance, useCompensatoryRest } from '../useCompliance'
import { useCompensationChoice } from '../useCompensationChoice'
import { useSkema } from '../useSkema'
import { putSkemaRowPreferences } from '../../lib/api'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

type Captured = {
  url: string
  method: string
  body: unknown
  headers: Record<string, string>
}

function toHeaderRecord(headers: HeadersInit | undefined): Record<string, string> {
  const record: Record<string, string> = {}
  if (!headers) return record
  if (headers instanceof Headers) {
    headers.forEach((v, k) => { record[k] = v })
  } else if (Array.isArray(headers)) {
    for (const [k, v] of headers) record[k] = v
  } else {
    for (const [k, v] of Object.entries(headers)) record[k] = v
  }
  return record
}

function captureCalls(
  respond: (url: string, method: string) => { status?: number; body?: unknown } = () => ({}),
) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      headers: toHeaderRecord(init?.headers),
    })
    const { status = 200, body = {} } = respond(url, init?.method ?? 'GET')
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: new Headers(),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
  return calls
}

function expectUnconditioned(call: Captured) {
  expect(call.headers['If-Match']).toBeUndefined()
  expect(call.headers['If-None-Match']).toBeUndefined()
}

beforeEach(() => {
  mockFetch.mockReset()
})

// ── time-entries ─────────────────────────────────────────────────────────────

const TIME_ENTRY_ROW = {
  employeeId: 'emp001',
  date: '2026-03-02',
  hours: 7.4,
  startTime: null,
  endTime: null,
  taskId: null,
  activityType: null,
  agreementCode: 'AC',
  okVersion: 'OK26',
  registeredAt: '2026-03-02T16:00:00Z',
  voluntaryUnsocialHours: false,
}

describe('useTimeEntries — typed GET + the 201 receipt POST', () => {
  it('list → GET /api/time-entries/{employeeId} (exact legacy URL); the SharedKernel rows flow through incl. voluntaryUnsocialHours', async () => {
    const calls = captureCalls((_url, method) =>
      method === 'GET' ? { body: [TIME_ENTRY_ROW] } : {},
    )
    const { result } = renderHook(() => useTimeEntries('emp001'))
    await waitFor(() => expect(result.current.entries).toHaveLength(1))
    expect(calls[0].url).toBe('/api/time-entries/emp001')
    expect(calls[0].method).toBe('GET')
    expect(result.current.entries[0]).toEqual(TIME_ENTRY_ROW)
  })

  it('register → POST /api/time-entries, UNCONDITIONED, body key set byte-unchanged, 201 receipt {eventId, streamId} returned', async () => {
    const receipt = { eventId: '11111111-1111-1111-1111-111111111111', streamId: 'time-emp001' }
    const calls = captureCalls((_url, method) =>
      method === 'POST' ? { status: 201, body: receipt } : { body: [] },
    )
    const { result } = renderHook(() => useTimeEntries('emp001'))
    await waitFor(() => expect(result.current.loading).toBe(false))
    const entry = {
      employeeId: 'emp001',
      date: '2026-03-02',
      hours: 7.4,
      startTime: '08:00',
      endTime: '16:00',
      taskId: 'DRIFT',
      activityType: 'NORMAL',
      agreementCode: 'AC',
      okVersion: 'OK24',
    }
    let returned: { eventId: string; streamId: string } | undefined
    await act(async () => {
      returned = await result.current.registerEntry(entry)
    })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/time-entries')
    expectUnconditioned(post)
    // The register body passes through VERBATIM (byte-unchanged key set incl.
    // the spec-deprecated okVersion the form still sends).
    expect(post.body).toEqual(entry)
    expect(Object.keys(post.body as Record<string, unknown>)).toEqual(Object.keys(entry))
    expect(returned).toEqual(receipt)
  })
})

// ── flex balance (ruling #1 — the FE-side both-branch delta pin) ─────────────

describe('useFlexBalance — the ruled ONE shape, both branches', () => {
  it('GET /api/flex-balance/{employeeId} (exact legacy URL); the NO-HISTORY branch (nulls, NO message) flows to state unchanged', async () => {
    const noHistory = {
      employeeId: 'emp001',
      balance: 0,
      previousBalance: null,
      delta: null,
      reason: null,
    }
    const calls = captureCalls(() => ({ body: noHistory }))
    const { result } = renderHook(() => useFlexBalance('emp001'))
    await waitFor(() => expect(result.current.flexBalance).not.toBeNull())
    expect(calls[0].url).toBe('/api/flex-balance/emp001')
    expect(result.current.flexBalance).toEqual(noHistory)
    // The ruled normalization: null-valued history, and NO vestigial `message`.
    expect(result.current.flexBalance).not.toHaveProperty('message')
  })

  it('the WITH-HISTORY branch flows through byte-faithful', async () => {
    const withHistory = {
      employeeId: 'emp001',
      balance: 4.2,
      previousBalance: 1.7,
      delta: 2.5,
      reason: 'WEEKLY_CALCULATION',
    }
    captureCalls(() => ({ body: withHistory }))
    const { result } = renderHook(() => useFlexBalance('emp001'))
    await waitFor(() => expect(result.current.flexBalance).not.toBeNull())
    expect(result.current.flexBalance).toEqual(withHistory)
  })
})

// ── balance summary + year overview (the additive settlement surfacing) ──────

describe('useBalanceSummary / useYearOverview — typed reads, exact legacy URLs', () => {
  it('summary → GET /api/balance/{employeeId}/summary?year=&month=; the row-level settlement flows through (display-only surfacing)', async () => {
    const summary = {
      employeeId: 'emp001',
      year: 2026,
      month: 3,
      flexBalance: 4.2,
      flexDelta: 0.5,
      vacationDaysUsed: 8,
      vacationDaysEntitlement: 25,
      normHoursExpected: 162.8,
      normHoursActual: 155.0,
      overtimeHours: 0,
      agreementCode: 'AC',
      hasMerarbejde: true,
      entitlements: [
        {
          type: 'VACATION', label: 'Ferie', totalQuota: 25, earned: 20.8, used: 8,
          planned: 0, carryoverIn: 3, remaining: 17, entitlementYear: 2025,
          settlement: {
            state: 'PENDING_REVIEW', transferDays: 4, payoutDays: 0, forfeitDays: 1,
            forfeitPending: true, reviewDisposition: null, claimDispositionDays: null,
          },
        },
      ],
      overtimeBalance: null,
    }
    const calls = captureCalls(() => ({ body: summary }))
    const { result } = renderHook(() => useBalanceSummary('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data).not.toBeNull())
    expect(calls[0].url).toBe('/api/balance/emp001/summary?year=2026&month=3')
    expect(result.current.data?.entitlements[0].settlement?.state).toBe('PENDING_REVIEW')
    expect(result.current.data?.overtimeBalance).toBeNull()
  })

  it('year-overview → GET /api/balance/{employeeId}/year-overview?year=; null saldo cells + category settlement flow through', async () => {
    const overview = {
      employeeId: 'emp001',
      year: 2026,
      today: '2026-03-15',
      header: { employeeName: 'Anna Berg', agreementCode: 'AC', okVersion: 'OK26', weeklyNormHours: 37 },
      tiles: {
        flexBalance: 0, ferieRemaining: null, careDayRemaining: null, seniorDayRemaining: null,
        sickDaysYtd: 0, childSickRemaining: null, childSickEligible: false, seniorDayEligible: false,
      },
      months: [],
      categories: [
        {
          type: 'VACATION', label: 'Ferie',
          // The empty-config graceful branch: ALL-null saldo (decimal?[12] on
          // the wire — the declared spec array-item-nullability discrepancy) +
          // the ruling-#2 always-present settlement key.
          saldo: Array.from({ length: 12 }, () => null),
          afholdt: Array.from({ length: 12 }, () => 0),
          expiring: 0, boundaryMonth: 12, settlement: null,
        },
      ],
    }
    const calls = captureCalls(() => ({ body: overview }))
    const { result } = renderHook(() => useYearOverview('emp001', 2026))
    await waitFor(() => expect(result.current.data).not.toBeNull())
    expect(calls[0].url).toBe('/api/balance/emp001/year-overview?year=2026')
    expect(result.current.data?.categories[0].saldo[0]).toBeNull()
    expect(result.current.data?.categories[0].settlement).toBeNull()
  })
})

// ── compliance (the integer-enum wire truth) ─────────────────────────────────

describe('useCompliance / useCompensatoryRest — typed reads; INTEGER enums flow', () => {
  it('period → GET /api/compliance/{employeeId}/period?year=&month=; violationType/severity arrive as INTEGERS', async () => {
    const complianceBody = {
      ruleId: 'EU_WTD',
      employeeId: 'emp001',
      success: false,
      violations: [
        {
          violationType: 3, // WEEKLY_MAX_HOURS on the REAL wire — an integer
          date: '2026-03-02',
          actualValue: 50,
          thresholdValue: 48,
          severity: 1, // VIOLATION
          isVoluntaryExempt: false,
          message: 'Ugentligt timemaksimum overskredet.',
        },
      ],
      warnings: [],
    }
    const calls = captureCalls(() => ({ body: complianceBody }))
    const { result } = renderHook(() => useCompliance('emp001', 2026, 3))
    await waitFor(() => expect(result.current.result).not.toBeNull())
    expect(calls[0].url).toBe('/api/compliance/emp001/period?year=2026&month=3')
    // The wire truth reaches state as integers — the pre-S120 string union
    // could never have matched these (the masked prod bug).
    expect(result.current.result?.violations[0].violationType).toBe(3)
    expect(result.current.result?.violations[0].severity).toBe(1)
  })

  it('compensatory-rest → GET /api/compliance/{employeeId}/compensatory-rest (exact legacy URL)', async () => {
    const row = {
      id: '22222222-2222-2222-2222-222222222222',
      employeeId: 'emp001',
      sourceDate: '2026-03-02',
      compensatoryDate: null,
      hours: 2,
      status: 'PENDING',
      createdAt: '2026-03-02T16:00:00Z',
    }
    const calls = captureCalls(() => ({ body: [row] }))
    const { result } = renderHook(() => useCompensatoryRest('emp001'))
    await waitFor(() => expect(result.current.entries).toHaveLength(1))
    expect(calls[0].url).toBe('/api/compliance/emp001/compensatory-rest')
    expect(result.current.entries[0]).toEqual(row)
  })
})

// ── compensation choice ──────────────────────────────────────────────────────

describe('useCompensationChoice — typed GET + UNCONDITIONED PUT', () => {
  const choice = {
    employeeId: 'emp001',
    periodYear: 2026,
    compensationModel: 'AFSPADSERING',
    source: 'AGREEMENT_DEFAULT',
  }

  it('GET /api/overtime/{employeeId}/compensation-choice?periodYear= (exact legacy URL); the 4-member shape flows', async () => {
    const calls = captureCalls(() => ({ body: choice }))
    const { result } = renderHook(() => useCompensationChoice('emp001', 2026))
    await waitFor(() => expect(result.current.choice).not.toBeNull())
    expect(calls[0].url).toBe('/api/overtime/emp001/compensation-choice?periodYear=2026')
    expect(result.current.choice).toEqual(choice)
  })

  it('update → PUT, UNCONDITIONED, body EXACTLY { periodYear, compensationModel } (byte-unchanged key set)', async () => {
    const calls = captureCalls((_url, method) =>
      method === 'PUT'
        ? { body: { employeeId: 'emp001', periodYear: 2026, compensationModel: 'PAYOUT' } }
        : { body: choice },
    )
    const { result } = renderHook(() => useCompensationChoice('emp001', 2026))
    await waitFor(() => expect(result.current.choice).not.toBeNull())
    await act(async () => {
      await result.current.updateChoice(2026, 'PAYOUT')
    })
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url).toBe('/api/overtime/emp001/compensation-choice')
    expectUnconditioned(put)
    expect(put.body).toEqual({ periodYear: 2026, compensationModel: 'PAYOUT' })
    expect(Object.keys(put.body as Record<string, unknown>)).toEqual([
      'periodYear', 'compensationModel',
    ])
    expect(result.current.choice?.compensationModel).toBe('PAYOUT')
  })
})

// ── skema (the graduation) ───────────────────────────────────────────────────

/** A minimal-but-spec-shaped month body (the hook only threads it to state and
    reads `absenceTypes` for the save split). */
const SKEMA_MONTH = {
  year: 2026,
  month: 3,
  daysInMonth: 31,
  projects: [{ projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift', sortOrder: 0 }],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }],
  entries: [],
  absences: [],
  workTime: [],
  dailyNorm: [],
  approval: null,
  employeeDeadline: '2026-04-05',
  managerDeadline: '2026-04-10',
  rowPreferences: { configured: false, projects: [], absenceTypes: [] },
  catalogs: { projects: [], absenceTypes: [] },
  boundaryWorkTime: [],
  fullDayNormAtMonthEnd: 7.4,
  consumptionBasis: [],
}

describe('useSkema — THE GRADUATION: typed month GET + save POST', () => {
  function skemaCalls() {
    return captureCalls((url, method) => {
      if (method === 'POST' && url.includes('/save')) return { body: { saved: 2 } }
      return { body: SKEMA_MONTH }
    })
  }

  it('month → GET /api/skema/{employeeId}/month?year=&month= — byte-identical to the retired SKEMA_MONTH_PATH helper URL', async () => {
    const calls = skemaCalls()
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data).not.toBeNull())
    expect(calls[0].url).toBe('/api/skema/emp001/month?year=2026&month=3')
    expect(calls[0].method).toBe('GET')
    expect(result.current.data?.absenceTypes[0].type).toBe('VACATION')
  })

  it('save → POST /api/skema/{employeeId}/save, UNCONDITIONED; the body key set is byte-unchanged (year/month/entries/absences/workTime, empty→null) and the row split/order is preserved', async () => {
    const calls = skemaCalls()
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data).not.toBeNull())
    let outcome: { status: string } | undefined
    await act(async () => {
      outcome = await result.current.saveMonth([
        { rowKey: 'DRIFT', date: '2026-03-02', hours: 5 },
        { rowKey: 'VACATION', date: '2026-03-03', hours: 7.4 },
        { rowKey: 'DRIFT', date: '2026-03-04', hours: 0 }, // zero → dropped
        { rowKey: 'VACATION', date: '2026-03-05', hours: null }, // null → dropped
      ])
    })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/skema/emp001/save')
    expectUnconditioned(post)
    expect(post.body).toEqual({
      year: 2026,
      month: 3,
      entries: [{ date: '2026-03-02', projectCode: 'DRIFT', hours: 5 }],
      absences: [{ date: '2026-03-03', absenceType: 'VACATION', hours: 7.4 }],
      workTime: null,
    })
    // The wire key ORDER is the legacy order (byte-unchanged request).
    expect(Object.keys(post.body as Record<string, unknown>)).toEqual([
      'year', 'month', 'entries', 'absences', 'workTime',
    ])
    expect(outcome).toEqual({ status: 'ok' })
  })

  it('save with NO rows collapses every list to null (the legacy empty→null pin)', async () => {
    const calls = skemaCalls()
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data).not.toBeNull())
    await act(async () => {
      await result.current.saveMonth([])
    })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.body).toEqual({ year: 2026, month: 3, entries: null, absences: null, workTime: null })
  })
})

// ── row preferences (the api.ts switched PUT) ────────────────────────────────

describe('putSkemaRowPreferences — the typed structured PUT stays UNCONDITIONED', () => {
  it('PUT /api/skema/{employeeId}/row-preferences with NO precondition headers and the byte-unchanged body', async () => {
    const body = {
      projects: [{ projectId: 'p-drift', sortOrder: 0 }],
      absenceTypes: [{ absenceType: 'VACATION', sortOrder: 0 }],
    }
    const calls = captureCalls(() => ({
      body: { configured: true, projects: [], absenceTypes: [] },
    }))
    await putSkemaRowPreferences('emp001', body)
    expect(calls[0].url).toBe('/api/skema/emp001/row-preferences')
    expect(calls[0].method).toBe('PUT')
    expectUnconditioned(calls[0])
    expect(calls[0].body).toEqual(body)
  })
})
