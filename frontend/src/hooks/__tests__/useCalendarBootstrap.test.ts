// S143 / TASK-14301 — the calendar gate's MECHANISM pins: the bootstrap read, the
// midnight-rollover refresh (performance.now()-timed), the stale-in-transit re-read, the
// visibility-regain re-read, and the deliberate asymmetry that a REFRESH failure keeps serving the
// last known day rather than re-gating.
//
// Every mocked response below is a HAND-WRITTEN literal, and every case whose point is "how many
// times was the endpoint read" uses a STEPPING mock (a queue of distinct responses, one per call) —
// never a frozen value — per the S143 spec's own warning: a frozen fixture cannot distinguish "read
// once" from "read many," which is exactly the class of bug this hook must not have (the
// stale-in-transit re-read and the midnight refresh both depend on a SECOND read actually happening).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { useCalendarBootstrap } from '../useCalendarBootstrap'

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
vi.stubGlobal('localStorage', {
  getItem: () => null,
  setItem: () => {},
  removeItem: () => {},
})
// `apiClient`'s shared 401 handler (`lib/api.ts`) hard-reloads the page on any 401 — including the
// bootstrap read's own 401 test below. Stub it to a no-op so jsdom's "not implemented: navigation"
// noise doesn't leak into the test output (mirrors `AuthContext.login.test.tsx`'s own stub).
Object.defineProperty(window, 'location', { value: { reload: vi.fn() }, writable: true })

beforeEach(() => {
  mockFetch.mockReset()
  vi.useRealTimers()
})

describe('useCalendarBootstrap — the bootstrap read', () => {
  it('fetches GET /api/calendar/today exactly once and reaches ready with the served day', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))

    expect(result.current.phase).toEqual({ kind: 'loading' })
    await waitFor(() => expect(result.current.phase.kind).toBe('ready'))
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
    expect(mockFetch).toHaveBeenCalledTimes(1)
    expect(mockFetch.mock.calls[0][0]).toBe('/api/calendar/today')
  })

  it('a 401 reaches "unauthorized", NOT "error" — the gate must route this to login, not show a system-fault screen', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}, 401))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'unauthorized' }))
  })

  it('a non-401 failure (5xx) reaches "error", distinct from "unauthorized"', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a network failure (rejected fetch) also reaches "error"', async () => {
    mockFetch.mockRejectedValueOnce(new Error('network down'))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('retry() re-runs the bootstrap from a clean "loading" phase and can recover', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))

    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 7200 }),
    )
    act(() => result.current.retry())
    expect(result.current.phase).toEqual({ kind: 'loading' })
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' }))
    expect(mockFetch).toHaveBeenCalledTimes(2)
  })

  it('enabled=false (unauthenticated) never fetches — an unauthenticated visit must not fire the authenticated-only read', async () => {
    const { result, rerender } = renderHook(({ enabled }) => useCalendarBootstrap(enabled), {
      initialProps: { enabled: false },
    })
    await act(async () => {
      await Promise.resolve()
    })
    expect(mockFetch).not.toHaveBeenCalled()
    expect(result.current.phase).toEqual({ kind: 'loading' })

    // Flipping to enabled (login completes) fires the read for the first time.
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    rerender({ enabled: true })
    await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1))
  })
})

describe('useCalendarBootstrap — the stale-in-transit correction (PINS scenario 3)', () => {
  it('a response whose duration already reads as gone (< 5s) triggers exactly ONE extra re-read before committing', async () => {
    // Call 1: the ORIGINAL server response, computed a heartbeat before midnight — already stale
    // by the time it is used. Call 2: the re-read, a fresh response with a full day ahead. These
    // are deliberately DIFFERENT literals so the assertion can tell which one actually got committed.
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 2 }))
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))

    const { result } = renderHook(() => useCalendarBootstrap(true))

    // Before the second (corrective) read resolves, nothing stale is ever committed — the phase
    // stays "loading", not a transient "ready" with the aged-out day.
    await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2))
    expect(result.current.phase).not.toEqual({ kind: 'ready', today: '2026-03-15' })

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' }))
    expect(mockFetch).toHaveBeenCalledTimes(2)
  })

  it('a re-read that is STILL under the threshold is accepted as-is (bounded to one extra read, not a loop)', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 1 }))
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3 }))

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' }))
    expect(mockFetch).toHaveBeenCalledTimes(2)
  })

  it('a response comfortably above the threshold commits on the FIRST read (no wasted re-read)', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' }))
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })
})

describe('useCalendarBootstrap — the midnight-rollover refresh + the AC-7 mount pins', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('PINS-1: a mount while the scheduled refresh is still PENDING must not read yesterday — it keeps serving the confirmed day', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // Half way to the rollover — the refresh has not fired yet.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(50_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(1)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
  })

  it('the refresh fires at exactly the served duration and moves the day forward by one confirmed read', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }))
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  it('PINS-4: a mount during retry backoff after a REFRESH failure sees the last known day, not an error — the deliberate asymmetry with the startup gate', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }))
      .mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // The scheduled refresh fires and FAILS.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    // Deliberately UNCHANGED — a refresh failure does not gate a running app (module doc). A
    // consumer "mounting" right now (i.e. reading `phase`) sees the last known day, not an error.
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // During the backoff window (before the retry fires) the day is still deliberately unchanged.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(29_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // The retry fires and recovers.
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(3)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  it('a visibility regain re-reads unconditionally, even though the scheduled refresh has not fired yet — no timer reliably survives a sleeping laptop', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }))
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  it('a visibility change to "hidden" does NOT trigger a re-read', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true })
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })
})
