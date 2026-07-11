// S115 / TASK-11502 — WIRE-level pins for the typed reporting-lines slice.
//
// THE DELICATE ROW (the sprint charter's vitest-PIN): `assignManager`'s
// precondition branch —
//   • FIRST assign (no line yet)  → `If-None-Match: *` (create-only), no If-Match
//   • reassign (line ETag known)  → `If-Match: "<version>"`, no If-None-Match
// These pins exercise the REAL typed `apiFetchWithEtag` path (stubbed fetch, no
// hook mock), so a regression in the S115 `ifNoneMatch` option normalization or
// in the typed switch itself reds this file — the LifecycleSections tests pin
// the same row one level up, against a MOCKED hook.
//
// The remaining cases pin the byte-equivalence the typed switch promised:
// interpolated+encoded URLs, no phantom precondition on the vikar/remove
// routes, the `activeVikar` optional→null normalization, and the 409 gap
// protocol surviving the typed path.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useReportingLines } from '../useReportingLines'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function hook() {
  return renderHook(() => useReportingLines()).result.current
}

/** Capture each fetch call's (url, headers, parsed body). */
function captureCalls(respond: () => unknown) {
  const calls: Array<{ url: string; headers: Record<string, string>; body: unknown }> = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const headers: Record<string, string> = {}
    const h = init?.headers
    if (h) for (const [k, v] of Object.entries(h)) headers[k] = String(v)
    calls.push({
      url,
      headers,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    })
    return respond()
  })
  return calls
}

const lineJson = {
  reportingLineId: 'rl-1',
  employeeId: 'EMP001',
  managerId: 'MGR9',
  organisationId: 'ORG1',
  relationship: 'PRIMARY',
  effectiveFrom: '2026-07-03',
  effectiveTo: null,
  source: 'ADMIN',
  version: 1,
  createdBy: 'ADMIN1',
  createdAt: '2026-07-03T00:00:00Z',
}

beforeEach(() => {
  mockFetch.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

describe('assignManager — the precondition row (S115 PIN)', () => {
  it('FIRST assign (no ifMatch) sends If-None-Match: * and NO If-Match', async () => {
    const calls = captureCalls(() => ({
      ok: true, status: 201,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => lineJson,
    }))
    const result = await hook().assignManager({
      employeeId: 'EMP001',
      managerId: 'MGR9',
      effectiveFrom: '2026-07-03',
    })
    expect(calls[0].url).toBe('/api/admin/reporting-lines')
    expect(calls[0].headers['If-None-Match']).toBe('*')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].body).toEqual({
      employeeId: 'EMP001',
      managerId: 'MGR9',
      effectiveFrom: '2026-07-03',
    })
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.version).toBe(1)
  })

  it('REASSIGN (ifMatch supplied) sends If-Match and NO If-None-Match', async () => {
    const calls = captureCalls(() => ({
      ok: true, status: 200,
      headers: new Headers({ ETag: '"3"' }),
      json: async () => ({ ...lineJson, version: 3 }),
    }))
    const result = await hook().assignManager(
      { employeeId: 'EMP001', managerId: 'MGR9', effectiveFrom: '2026-07-03' },
      '"2"',
    )
    expect(calls[0].headers['If-Match']).toBe('"2"')
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.version).toBe(3)
  })
})

describe('the typed switch — byte-equivalent wire behavior', () => {
  it('removeManager: DELETE with strict If-Match, path param encoded', async () => {
    const calls = captureCalls(() => ({ ok: true, status: 204, headers: new Headers() }))
    const result = await hook().removeManager('EMP 1', '"2"')
    expect(calls[0].url).toBe('/api/admin/reporting-lines/EMP%201')
    expect(calls[0].headers['If-Match']).toBe('"2"')
    expect(result.ok).toBe(true)
  })

  it('createVikar: POST with NO precondition headers, body verbatim', async () => {
    const calls = captureCalls(() => ({
      ok: true, status: 200,
      headers: new Headers(),
      json: async () => ({
        vikarId: 'v1', managerId: 'MGR9', vikarUserId: 'BO1',
        effectiveFrom: '2026-07-03', effectiveTo: '2026-07-10', reason: 'FERIE',
      }),
    }))
    const result = await hook().createVikar('MGR9', {
      vikarUserId: 'BO1',
      effectiveTo: '2026-07-10',
      reason: 'FERIE',
    })
    expect(calls[0].url).toBe('/api/admin/reporting-lines/MGR9/vikar')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(calls[0].body).toEqual({ vikarUserId: 'BO1', effectiveTo: '2026-07-10', reason: 'FERIE' })
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.vikarId).toBe('v1')
  })

  it('endVikar: DELETE with NO precondition; the 200 body is discarded', async () => {
    const calls = captureCalls(() => ({
      ok: true, status: 200,
      headers: new Headers(),
      json: async () => ({ vikarId: 'v1', managerId: 'MGR9', vikarUserId: 'BO1', revoked: true }),
    }))
    const result = await hook().endVikar('MGR9')
    expect(calls[0].url).toBe('/api/admin/reporting-lines/MGR9/vikar')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data).toBeUndefined()
  })

  it('fetchActiveVikar: normalizes the always-emitted null wire value to activeVikar: null', async () => {
    captureCalls(() => ({
      ok: true, status: 200,
      headers: new Headers(),
      json: async () => ({ activeVikar: null }),
    }))
    const result = await hook().fetchActiveVikar('MGR9')
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.activeVikar).toBeNull()
  })

  it('fetchActiveVikar: passes an active vikar object through untouched', async () => {
    const vikar = { vikarUserId: 'BO1', vikarDisplayName: 'Bo Dahl', untilDate: '2026-07-10', reason: 'FERIE' }
    captureCalls(() => ({
      ok: true, status: 200,
      headers: new Headers(),
      json: async () => ({ activeVikar: vikar }),
    }))
    const result = await hook().fetchActiveVikar('MGR9')
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.activeVikar).toEqual(vikar)
  })

  it('fetchEmployeeLines / fetchDirectReports: typed spec-keyed URLs, params encoded', async () => {
    const calls = captureCalls(() => ({
      ok: true, status: 200,
      json: async () => ({ active: [], history: [] }),
    }))
    await hook().fetchEmployeeLines('EMP001')
    expect(calls[0].url).toBe('/api/admin/reporting-lines/EMP001')
    mockFetch.mockResolvedValueOnce({ ok: true, status: 200, json: async () => [] })
    await hook().fetchDirectReports('MGR9')
    expect(mockFetch.mock.calls[1][0]).toBe('/api/admin/reporting-lines/MGR9/reports')
  })

  it('deletePersonWithReassignment: the 409 gap protocol survives the typed path', async () => {
    const gapBody = {
      error: 'Manglende erstatningsgodkender for 2 medarbejdere.',
      reportsNeedingReassignment: ['CHILD1', 'CHILD2'],
      reportsNeedingReassignmentCount: 2,
    }
    captureCalls(() => ({
      ok: false, status: 409,
      headers: new Headers(),
      text: async () => JSON.stringify(gapBody),
    }))
    const result = await hook().deletePersonWithReassignment('EMP001', {})
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(409)
      expect(result.gap?.reportsNeedingReassignment).toEqual(['CHILD1', 'CHILD2'])
    }
    expect(mockFetch.mock.calls[0][0]).toBe('/api/admin/reporting-lines/EMP001/remove')
    const init = mockFetch.mock.calls[0][1] as RequestInit
    expect(JSON.parse(String(init.body))).toEqual({ replacements: {} })
  })
})
