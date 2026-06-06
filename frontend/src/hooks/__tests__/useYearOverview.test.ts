// S65 / Step-7a fix (Codex P2) — stale year-switch response-race guard for
// useYearOverview. Rapid ←/→ year switches fire overlapping GETs; an older
// year's response must NOT overwrite a newer one. The page-level test
// (ArsoversigtPage.test.tsx) stubs this hook wholesale, so the race lives
// nowhere there — this focused renderHook test pins the invariant directly via
// controllable (deferred) apiClient promises resolved out of order.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import type { ApiResult } from '../../lib/api'
import type { YearOverview } from '../useYearOverview'

// ── apiClient.get mocked directly so we control resolution order. PAT-007: the
//    resolved payloads are referentially-stable hoisted constants. ──
const { mockGet, OVERVIEW_2025, OVERVIEW_2026 } = vi.hoisted(() => {
  const base = {
    employeeId: 'emp001',
    today: '2026-03-15',
    header: { employeeName: 'Anna Berg', agreementCode: 'AC', okVersion: 'OK26', weeklyNormHours: 37 },
    tiles: {
      flexBalance: 0, ferieRemaining: 0, careDayRemaining: 0, seniorDayRemaining: null,
      sickDaysYtd: 0, childSickRemaining: null, childSickEligible: false, seniorDayEligible: false,
    },
    months: [],
    categories: [],
  }
  return {
    mockGet: vi.fn(),
    OVERVIEW_2025: { ...base, year: 2025 } as YearOverview,
    OVERVIEW_2026: { ...base, year: 2026 } as YearOverview,
  }
})

vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

// Imported AFTER the mock is registered.
import { useYearOverview } from '../useYearOverview'

/** A manually-resolvable ApiResult<YearOverview> promise. */
function deferred() {
  let resolve!: (r: ApiResult<YearOverview>) => void
  const promise = new Promise<ApiResult<YearOverview>>((res) => { resolve = res })
  return { promise, resolve }
}

beforeEach(() => {
  mockGet.mockReset()
})

describe('useYearOverview — stale year-switch response guard', () => {
  it('drops an older request that resolves AFTER a newer one (latest year wins)', async () => {
    // First render fetches year 2025 (request 1, deferred); rerender to 2026
    // fetches request 2 (deferred). We then resolve request 2 FIRST, then
    // request 1 — the stale request 1 must NOT overwrite the 2026 data.
    const d1 = deferred()
    const d2 = deferred()
    mockGet
      .mockReturnValueOnce(d1.promise) // year=2025 (older)
      .mockReturnValueOnce(d2.promise) // year=2026 (newer)

    const { result, rerender } = renderHook(
      ({ year }) => useYearOverview('emp001', year),
      { initialProps: { year: 2025 } },
    )

    // Switch to the newer year before the first response lands.
    rerender({ year: 2026 })
    expect(mockGet).toHaveBeenCalledTimes(2)
    expect(mockGet.mock.calls[0][0]).toContain('year=2025')
    expect(mockGet.mock.calls[1][0]).toContain('year=2026')

    // Resolve the NEWER request first…
    await act(async () => {
      d2.resolve({ ok: true, data: OVERVIEW_2026 })
    })
    await waitFor(() => expect(result.current.data?.year).toBe(2026))

    // …then the OLDER request resolves late — it must be dropped, not applied.
    await act(async () => {
      d1.resolve({ ok: true, data: OVERVIEW_2025 })
    })

    // The rendered data is still the LATER (2026) request's.
    expect(result.current.data?.year).toBe(2026)
    expect(result.current.loading).toBe(false)
  })

  it('applies an in-order resolution normally (newest still wins)', async () => {
    // Sanity counterpart: requests resolve in order → final data is the latest.
    const d1 = deferred()
    const d2 = deferred()
    mockGet
      .mockReturnValueOnce(d1.promise)
      .mockReturnValueOnce(d2.promise)

    const { result, rerender } = renderHook(
      ({ year }) => useYearOverview('emp001', year),
      { initialProps: { year: 2025 } },
    )
    rerender({ year: 2026 })

    await act(async () => { d1.resolve({ ok: true, data: OVERVIEW_2025 }) })
    await act(async () => { d2.resolve({ ok: true, data: OVERVIEW_2026 }) })

    await waitFor(() => expect(result.current.data?.year).toBe(2026))
    expect(result.current.loading).toBe(false)
  })
})
