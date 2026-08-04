// S126 / F2 — the stale-response guard, tested rather than assumed.
//
// The reported finding was "StrictMode double-fetch". That framing is a non-defect: React 18
// double-invokes effects in DEV builds only, production is unaffected, and removing StrictMode would
// be strictly negative. What the double-invoke is actually SIGNALLING is that effect-driven fetches
// had no cancellation discipline: `frontend/src` contained ZERO `AbortController` uses, and only
// useSearch and useYearOverview carried any stale-response guard at all.
//
// The reachable defect is a stale WRITE, not a wasted request: when an effect's inputs change while a
// request is in flight (month navigation, switching employee, typing a filter), two responses race and
// the LAST one to arrive wins — which may be the one for inputs the user has already left. This
// project has shipped and fixed that class before (S123 TASK-12301).
//
// These tests exercise useBalanceSummary as the representative site — [employeeId, year, month], the
// exact shape month navigation drives. The guard is the same `latestRequestId` ref pattern in every
// patched hook, so proving it here proves the pattern; the per-hook risk is mis-application, which
// tsc + the suite cover.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'

const { mockGet } = vi.hoisted(() => ({ mockGet: vi.fn() }))

vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

// Imported AFTER the mock is registered.
const { useBalanceSummary } = await import('../useBalanceSummary')

/** A response whose resolution we control, so two requests can be made to finish out of order. */
function deferred<T>() {
  let resolve!: (v: T) => void
  const promise = new Promise<T>(r => { resolve = r })
  return { promise, resolve }
}

const summaryFor = (month: number) => ({
  ok: true as const,
  data: { month, entitlements: [], overtimeBalance: null } as never,
})

beforeEach(() => { mockGet.mockReset() })

describe('stale-response guard (S126 / F2)', () => {
  it('drops an OLDER response that resolves AFTER a newer one — the month-navigation race', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    mockGet
      .mockImplementationOnce(() => first.promise)   // month 3
      .mockImplementationOnce(() => second.promise)  // month 4

    const { result, rerender } = renderHook(
      ({ month }) => useBalanceSummary('emp1', 2026, month),
      { initialProps: { month: 3 } },
    )

    // Navigate to month 4 while month 3 is still in flight.
    rerender({ month: 4 })
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(2))

    // The NEWER request resolves FIRST, then the older one lands late — the out-of-order case.
    await act(async () => { second.resolve(summaryFor(4)) })
    await waitFor(() => expect(result.current.data).not.toBeNull())
    expect((result.current.data as { month: number }).month).toBe(4)

    await act(async () => { first.resolve(summaryFor(3)) })

    // Without the guard, month 3's late response would overwrite month 4's — the user would be
    // looking at April and reading March's balance.
    expect((result.current.data as { month: number }).month).toBe(4)
  })

  it('does not settle loading from a superseded request', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    mockGet
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)

    const { result, rerender } = renderHook(
      ({ month }) => useBalanceSummary('emp1', 2026, month),
      { initialProps: { month: 3 } },
    )
    rerender({ month: 4 })
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(2))

    // The SUPERSEDED request resolves while the current one is still in flight. Loading must stay
    // true: reporting "done" here would flash resolved content for a request nobody is waiting on.
    await act(async () => { first.resolve(summaryFor(3)) })
    expect(result.current.loading).toBe(true)
    expect(result.current.data).toBeNull()

    await act(async () => { second.resolve(summaryFor(4)) })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect((result.current.data as { month: number }).month).toBe(4)
  })

  it('does not commit an error from a superseded request', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    mockGet
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)

    const { result, rerender } = renderHook(
      ({ month }) => useBalanceSummary('emp1', 2026, month),
      { initialProps: { month: 3 } },
    )
    rerender({ month: 4 })
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(2))

    // An abandoned request failing must not surface an error against the month the user is on —
    // the error direction of the same race, and the one that would look like a real outage.
    await act(async () => { first.resolve({ ok: false, error: 'stale failure' }) })
    await act(async () => { second.resolve(summaryFor(4)) })

    await waitFor(() => expect(result.current.data).not.toBeNull())
    expect(result.current.error).toBeNull()
  })

  // ⚠ NON-DISCRIMINATING, and labelled so deliberately. The three tests above were verified to FAIL
  // with the guard removed; this one PASSES either way. React 18 made setState-after-unmount a silent
  // no-op (the old warning was withdrawn), so there is nothing observable for it to assert. It is kept
  // as a crash-regression guard — resolving after unmount must not throw — and NOT as evidence that
  // the stale-response guard works. Reading it as the latter is exactly the "assertion that looks like
  // evidence" failure this sprint exists to drain.
  it('unmounting mid-flight does not throw (crash guard only — does NOT exercise the stale guard)', async () => {
    const inFlight = deferred<unknown>()
    mockGet.mockImplementationOnce(() => inFlight.promise)

    const { unmount } = renderHook(() => useBalanceSummary('emp1', 2026, 3))
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(1))

    unmount()
    await act(async () => { inFlight.resolve(summaryFor(3)) })
  })
})
