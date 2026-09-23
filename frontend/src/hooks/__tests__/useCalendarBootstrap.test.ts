// S143 / TASK-14301 — the calendar gate's MECHANISM pins: the bootstrap read, payload validation,
// the request deadline, the measured transit-staleness correction, the midnight-rollover refresh
// (performance.now()-timed), the visibility-regain re-read (including BEFORE any day is confirmed —
// Step-5a review bug B), and the deliberate asymmetry that a REFRESH failure keeps serving the last
// known day rather than re-gating — including that it does not reload the page either (bug B2).
//
// Every mocked response below is a HAND-WRITTEN literal, and every case whose point is "how many
// times was the endpoint read" uses a STEPPING mock (a queue of distinct responses, one per call) —
// never a frozen value — per the S143 spec's own warning: a frozen fixture cannot distinguish "read
// once" from "read many." Step-5a review additionally found that four of this file's ORIGINAL tests
// (labelled PINS-1..4) each caught something real but not the scenario their name promised, and a
// SECOND review pass (mutation-verified) found that even the rewritten transit-staleness tests
// checked the outcome only AFTER everything had already settled in one flush — a mutant that commits
// the stale day BEFORE recursing, then lets the corrective read silently overwrite it, passed
// undetected. Both classes are fixed the same way: hold BOTH the original and the corrective
// response pending (manually-released promises), and assert `phase` at the CHECKPOINT between them —
// the one moment a wrongly-committed value would be observable before a later read could paper over it.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
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
// `apiClient`'s shared 401 handler (`lib/api.ts`) would hard-reload the page on any 401 that does NOT
// pass `skipAuthReload` — this hook passes it for EVERY calendar read (bug B2 fix), so `mockReload`
// below is expected to stay uncalled by every test in this file; several tests assert that directly
// rather than merely stubbing it to silence jsdom's navigation noise (mirrors
// `AuthContext.login.test.tsx`'s own stub, but named so it is assertable).
const mockReload = vi.fn()
Object.defineProperty(window, 'location', { value: { reload: mockReload }, writable: true })

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
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

  it('a 401 reaches "unauthorized", NOT "error" — the gate must route this to login, not show a system-fault screen — and does NOT reload the page itself (bug B2/skipAuthReload)', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}, 401))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'unauthorized' }))
    // The hook reports the 401 back as `unauthorized` for `RequireAuth` to act on (`logout()` +
    // `<Navigate>`) — it must NOT also trigger `apiClient`'s shared reload underneath that.
    expect(mockReload).not.toHaveBeenCalled()
  })

  it('a 403 (forbidden, not expired) reaches "error", NOT "unauthorized" — only status 401 selects the login path', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}, 403))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a non-401 failure (5xx) reaches "error", distinct from "unauthorized"', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    const { result } = renderHook(() => useCalendarBootstrap(true))

    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a network failure (rejected fetch, surfaces as status 0) also reaches "error"', async () => {
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

describe('useCalendarBootstrap — payload validation (Step-5a review W)', () => {
  it('a malformed 200 body ({}) is treated as a bootstrap FAILURE ("error"), not destructured blindly into an undefined `today`', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}))
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a 204 (no body — `apiClient` resolves `data: undefined` for this status without calling `.json()`) is also a bootstrap failure, not a throw that strands `loading`', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204, headers: new Headers() })
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a non-numeric secondsUntilNextMidnight is rejected as malformed, same as a missing field', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 'soon' }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await waitFor(() => expect(result.current.phase).toEqual({ kind: 'error' }))
  })

  it('a huge (implausible-scale) duration is CLAMPED before scheduling, not handed raw to setTimeout — proving the schedule cannot overflow the signed 32-bit delay limit', async () => {
    vi.useFakeTimers()
    // 999,999,999 seconds (~31.7 YEARS) — if this were handed to `setTimeout` unclamped, the next
    // refresh would never fire within any test-observable horizon (and would overflow the browser's
    // signed 32-bit delay argument in production, per HTML timers). MAX_TIMEOUT_MS below is the
    // hand-written literal of `useCalendarBootstrap.ts`'s own `MAX_TIMEOUT_MS` (2^31-1 ms, ~24.8
    // days) — the platform's known ceiling, not a value derived from the code under test.
    mockFetch
      .mockResolvedValueOnce(
        jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 999_999_999 }),
      )
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
    expect(mockFetch).toHaveBeenCalledTimes(1)

    const MAX_TIMEOUT_MS = 2_147_483_647
    await act(async () => {
      await vi.advanceTimersByTimeAsync(MAX_TIMEOUT_MS)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
    vi.useRealTimers()
  })
})

describe('useCalendarBootstrap — the request deadline (Step-5a review W)', () => {
  it('a stalled bootstrap request (never resolves) times out and reaches "error" — not an indefinite loading screen', async () => {
    vi.useFakeTimers()
    mockFetch.mockImplementationOnce(() => new Promise(() => {})) // never settles
    const { result } = renderHook(() => useCalendarBootstrap(true))

    // REQUEST_TIMEOUT_MS in `useCalendarBootstrap.ts` is 10_000 — a hand-written literal here, the
    // same reasoning as MAX_TIMEOUT_MS above.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9_999)
    })
    expect(result.current.phase).toEqual({ kind: 'loading' }) // still within budget

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(result.current.phase).toEqual({ kind: 'error' })
    vi.useRealTimers()
  })

  it('a stalled REFRESH request also times out, but — per the asymmetry — keeps the last known day rather than erroring', async () => {
    vi.useFakeTimers()
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    mockFetch.mockImplementationOnce(() => new Promise(() => {})) // the refresh call stalls forever
    // 100_000ms to fire the scheduled refresh, then 10_000ms more for ITS OWN deadline to expire.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(110_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
    vi.useRealTimers()
  })
})

describe('useCalendarBootstrap — the transit-staleness correction, measured not guessed (Step-5a review W)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it("the reviewer's exact example: a response reporting 5s that takes 6s of measured transit is caught as stale, and the stale day is never even TRANSIENTLY committed", async () => {
    // BOTH responses are held pending (manually released) — not just the first. A single-flush
    // version of this test (release the first, let the second resolve on its own in the same
    // `advanceTimersByTimeAsync(0)`) is NOT mutation-proof: a mutant that commits `today` from the
    // stale response BEFORE recursing to re-read would still show the CORRECT final value once the
    // corrective read's response — resolving near-instantly with no fake-timer gap of its own —
    // overwrites it milliseconds later, all within the same flush this test would otherwise check
    // after (Step-5a second-lens review, mutation-verified against exactly this shape). Holding the
    // SECOND response pending too creates an observable window — after the corrective re-read has
    // been ISSUED but before it RESOLVES — where a wrongly-committed stale value would still be
    // sitting in `phase` if the mutant existed, and isn't.
    let releaseFirst!: (value: unknown) => void
    let releaseSecond!: (value: unknown) => void
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFirst = resolve
        }),
    )
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseSecond = resolve
        }),
    )

    const { result } = renderHook(() => useCalendarBootstrap(true))
    // 6 seconds of REAL elapsed (monotonic-clock) transit time pass BEFORE the response arrives —
    // measured via the same `performance.now()` the hook itself reads, via fake timers that advance
    // `performance.now()` in lockstep with `setTimeout`/`Date`.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(6_000)
    })
    releaseFirst(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 5 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })

    // CHECKPOINT: the corrective re-read has been ISSUED but has NOT resolved yet. This is the exact
    // moment a mutant that commits-then-corrects would be caught — nothing else has had a chance to
    // overwrite an incorrectly-committed value yet.
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'loading' })
    expect(result.current.phase).not.toEqual({ kind: 'ready', today: '2026-03-15' })

    releaseSecond(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  it('a LARGER transit delay, on a response comfortably above the old fixed heuristic ceiling, is STILL caught — proving this no longer depends on any raw-value threshold', async () => {
    let releaseFirst!: (value: unknown) => void
    let releaseSecond!: (value: unknown) => void
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFirst = resolve
        }),
    )
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseSecond = resolve
        }),
    )

    const { result } = renderHook(() => useCalendarBootstrap(true))
    // The response reports 6s — above the OLD "< 5s" heuristic, which would have accepted it
    // outright with no re-read — but transit itself took 7s, so the TRUE remaining time is negative.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(7_000)
    })
    releaseFirst(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 6 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })

    // CHECKPOINT (same reasoning as the test above): caught mid-correction, before anything could
    // have silently overwritten a wrongly-committed value.
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).not.toEqual({ kind: 'ready', today: '2026-03-15' })

    releaseSecond(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  it('a small reported duration that arrives with NEGLIGIBLE transit is accepted on the FIRST read — measurement does not over-trigger the way a blanket threshold could', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 2 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it('a re-read that is STILL non-positive after its OWN measured transit is accepted as-is and self-heals via an IMMEDIATE follow-up refresh — bounded to one STALE-CHECK retry, not an infinite loop', async () => {
    let releaseFirst!: (value: unknown) => void
    let releaseSecond!: (value: unknown) => void
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFirst = resolve
        }),
    )
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseSecond = resolve
        }),
    )

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000)
    })
    releaseFirst(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 1 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    // The ONE bounded corrective re-read has been ISSUED (second fetch call) — not yet resolved.
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'loading' })

    // The re-read ALSO takes 2s of transit and ALSO reports only 1s remaining — STILL non-positive.
    // Because this is already the bounded retry (`isStaleRetry: true`), it is accepted as-is rather
    // than recursing again — but a non-positive remaining duration schedules its OWN follow-up
    // refresh IMMEDIATELY (a clamped `Math.max(0, remainingMs)` delay of 0ms), so a THIRD call
    // follows right away as an ORDINARY refresh, not a further stale-retry attempt. Queue a normal
    // future day for it so the sequence's actual termination (not a runaway loop) is provable.
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000)
    })
    releaseSecond(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 1 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })

    expect(mockFetch).toHaveBeenCalledTimes(3)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })

    // And THAT settles normally — no further fetch without another full day's wait, proving this
    // was a one-time self-heal, not an ongoing tight loop.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(50_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(3)
  })
})

describe('useCalendarBootstrap — a refresh in flight must not corrupt the currently-served day', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  // Strengthens the ORIGINAL "PINS-1" test, which only proved a premature read never happens BEFORE
  // the scheduled timer fires — it never actually started a pending refresh, so an implementation
  // that cleared `today` the moment a refresh fetch LEFT (rather than when it resolves) would have
  // passed it undetected (Step-5a review).
  it('while a refresh fetch has been issued but not yet resolved, `phase` keeps reporting the OLD confirmed day — never cleared, never blanked, never the new (unconfirmed) value early', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // The scheduled refresh fires — the fetch LEAVES but is held open (not yet resolved).
    let releaseRefresh!: (value: unknown) => void
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseRefresh = resolve
        }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2) // the refresh fetch has been ISSUED

    // While it is pending, the OLD day is still what `phase` reports.
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // Only once it resolves does the phase move — and to the NEW confirmed value, never a gap.
    releaseRefresh(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
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
})

describe('useCalendarBootstrap — a REFRESH failure: no lost ready state, the documented retry cadence, and recovery', () => {
  // Renamed from the original "PINS-4": that test asserted losing ready state, the retry cadence
  // and eventual recovery — all real — but its NAME also claimed "a mount during backoff sees the
  // last known day," which it never tested (no second consumer, no fresh mount during the backoff
  // window). This hook has no multi-consumer surface of its own to mount a second reader against —
  // that claim is what `RequireAuth.test.tsx`'s own "a mount during retry backoff" test now proves,
  // at the level (a shared React context with two independent consumers) where it is actually true.
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('a refresh failure does not revert "ready", retries after exactly REFRESH_RETRY_DELAY_MS (30s), and recovers on the retry', async () => {
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
    // Deliberately UNCHANGED — a refresh failure does not gate a running app (module doc).
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // Before the 30s retry cadence elapses, no further fetch and no phase change.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(29_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    // The retry fires (at exactly the 30s cadence) and recovers.
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(3)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-16' })
  })

  // Step-5a review bug B2: an EARLIER version of this hook called `apiClient.get` without
  // `skipAuthReload`, so `apiClient`'s shared 401 handler (`lib/api.ts`) hard-reloaded the page on a
  // REFRESH 401 — probe-measured as firing exactly once, undocumented, and precisely the harm this
  // asymmetry exists to prevent (a half-filled skema, a laptop asleep past token expiry, the tab
  // regaining visibility, the page reloading with no action from the user). This proves BOTH halves
  // together: the asymmetry (last known day kept) AND that it is not secretly overridden by a reload
  // happening beneath this hook.
  it('B2 regression: a REFRESH 401 does NOT reload the page — the last known day keeps being served, exactly like a 5xx (the asymmetry is REAL, not overridden underneath by apiClient\'s shared handler)', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }))
      .mockResolvedValueOnce(jsonResponse({}, 401))

    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
    expect(mockReload).not.toHaveBeenCalled()
  })
})

describe('useCalendarBootstrap — tab visibility regain', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('AFTER a day is confirmed: a visibility regain re-reads unconditionally, even though the scheduled refresh has not fired yet — no timer reliably survives a sleeping laptop', async () => {
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

  // Step-5a review BUG B: reaching tab visibility WHILE the very first bootstrap is still pending
  // used to be treated as a "refresh" — whose failure path deliberately leaves `phase` UNTOUCHED.
  // But `phase` was still `loading` at that point (no day had ever been confirmed), so "untouched"
  // meant "stuck on the loading screen forever," with no error screen and no retry button — exactly
  // the unrecoverable-boot class the gate's placement (`RequireAuth`, after `isAuthenticated`) was
  // designed to avoid, reached through a door neither the gate's placement nor the 401-vs-error
  // split had anything to do with.
  it('BEFORE any day is confirmed: a visibility regain while the FIRST bootstrap is still pending is treated as a SECOND BOOTSTRAP attempt — its failure reaches the error screen, not an indefinite loading screen', async () => {
    // The first attempt never resolves — simulating a slow start the user tabs away during.
    mockFetch.mockImplementationOnce(() => new Promise(() => {}))
    const { result } = renderHook(() => useCalendarBootstrap(true))
    expect(result.current.phase).toEqual({ kind: 'loading' })

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
    // The SUPERSEDING attempt (issued by the visibility regain) fails.
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })

    expect(result.current.phase).toEqual({ kind: 'error' })
  })

  it('BEFORE any day is confirmed: a visibility regain that SUCCEEDS reaches "ready" normally (the second-bootstrap path is not merely a failure path)', async () => {
    mockFetch.mockImplementationOnce(() => new Promise(() => {}))
    const { result } = renderHook(() => useCalendarBootstrap(true))
    expect(result.current.phase).toEqual({ kind: 'loading' })

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })

    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
  })

  it('AFTER a day is confirmed, a SUBSEQUENT visibility regain still uses the quiet REFRESH-failure semantics — the fix does not regress the ordinary case', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    const { result } = renderHook(() => useCalendarBootstrap(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })

    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })

    // Once confirmed, a visibility-triggered failure is a REFRESH failure — last known day kept.
    expect(result.current.phase).toEqual({ kind: 'ready', today: '2026-03-15' })
  })
})
