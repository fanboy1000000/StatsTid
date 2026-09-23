import { useCallback, useEffect, useRef, useState } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

type CalendarTodayResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.CalendarTodayResponse']

/**
 * The calendar gate's phase. `RequireAuth` (`components/guards/RequireAuth.tsx`) renders directly
 * off this union — it is the single source of truth for "what does the shell show right now."
 *
 *  - `loading`   — the bootstrap read (or a retry of it) is in flight; nothing renders yet
 *                  (owner ruling OQ-1d: the app is unusable, even for reading, rather than guessing).
 *  - `ready`     — a day is confirmed; `today` is always populated. Once reached, the phase NEVER
 *                  reverts to `loading`/`error` for the life of this gate instance — see the
 *                  "refresh failure does not gate a running app" doc on `useCalendarBootstrap` below.
 *  - `error`     — the bootstrap read failed for a reason OTHER than an expired token (network
 *                  failure, 5xx, malformed response). The shell shows an error screen with a retry.
 *  - `unauthorized` — the bootstrap read got a 401: the token is expired, not the calendar's fault.
 *                  `RequireAuth` routes this to `/login` instead of showing a system-error screen.
 */
export type CalendarBootstrapPhase =
  | { kind: 'loading' }
  | { kind: 'ready'; today: string }
  | { kind: 'error' }
  | { kind: 'unauthorized' }

// A response computed a heartbeat before Danish midnight can arrive at the client AFTER midnight
// has actually passed — the network round trip plus React's own scheduling is easily a few hundred
// milliseconds, and a backgrounded/throttled tab can add much more. 5 seconds is comfortably above
// realistic transit jitter (the failure mode this guards against is measured in tens to low hundreds
// of milliseconds) while being comfortably below "the user would notice the extra round trip" — and
// it only ever fires in the ~5-second sliver of a 24-hour day, so it costs nothing the rest of the
// time. PINS scenario 3 (S143 spec) is exactly this: a response whose duration already reads as
// "already gone" by the time it is used.
const STALE_IN_TRANSIT_THRESHOLD_SECONDS = 5

// A REFRESH (not the startup gate) that fails is retried on a fixed cadence rather than gating the
// running app — see the asymmetry doc below. 30s balances "notice a recovered network reasonably
// soon" against "don't hammer a dependency that is down"; there is no SLA here, only a same-session
// UX preference, so a fixed interval (rather than exponential backoff) keeps the one number legible.
const REFRESH_RETRY_DELAY_MS = 30_000

/**
 * S143 / TASK-14301 — the calendar gate's MECHANISM: one bootstrap read of `GET
 * /api/calendar/today`, a scheduled refresh at the Copenhagen day rollover, a re-read on tab
 * visibility regain, and a retry-with-backoff on a failed (non-bootstrap) refresh.
 *
 * <b>The asymmetry this hook exists to implement (owner ruling OQ-1a/1b/1d + the S143 spec's
 * "trap in the refresh").</b> The BOOTSTRAP read gates the whole app shell — `RequireAuth` shows
 * nothing at all until it succeeds — because at that point nothing is at stake yet: the user has
 * not started anything a failure could destroy. A REFRESH failure is different: the app is already
 * running, quite possibly with unsaved work on screen, and the ONLY thing at risk from a stale day
 * is that the calendar is wrong by at most a few hours (the gap since the last confirmed read) —
 * far cheaper than discarding whatever the user was doing. So a refresh failure does NOT change
 * `phase` at all: the last known `today` keeps being served, and this hook quietly retries in the
 * background. Conflating the two — e.g. re-gating on every refresh failure — would trade a rare,
 * small, recoverable staleness for a much larger and more frequent cost (destroyed in-progress work
 * on every network hiccup), which is exactly backwards.
 *
 * This includes a 401 ON A REFRESH: unlike the bootstrap 401 (routed to `/login` — see
 * `RequireAuth.tsx`), a refresh that gets a 401 is just another refresh failure and keeps serving
 * the last known day. A token that expires mid-session is already handled the moment the user's
 * NEXT real action hits any OTHER endpoint — `apiClient`'s shared 401 handler (`lib/api.ts`) clears
 * the session and reloads then. Having the calendar's own background refresh race that on a timer
 * would not get the user to `/login` any sooner; it would only add a second, redundant path to the
 * same outcome.
 *

 * <b>Why `enabled` rather than an unconditional effect.</b> `RequireAuth` must call this hook on
 * EVERY render (Rules of Hooks — it cannot be called only when authenticated), including the render
 * where the user is not yet logged in. Without a gate, an unauthenticated visit to a protected route
 * would fire an authenticated-only network call with no token, receive a 401 that has nothing to do
 * with an expired session, and feed `apiClient`'s shared 401 handler (`lib/api.ts`) needlessly. The
 * caller passes `enabled = isAuthenticated`; the internal effect only runs the bootstrap (and only
 * arms the visibility listener) while `true`, and tears everything down when it flips back to
 * `false` (logout) so no stale timer survives into the next session.
 *
 * <b>The stale-in-transit correction (PINS scenario 3).</b> Every successful response (bootstrap OR
 * refresh) is checked before being trusted: if `secondsUntilNextMidnight` is already below
 * {@link STALE_IN_TRANSIT_THRESHOLD_SECONDS}, the response is treated as having aged past the
 * rollover in transit, and this hook re-reads ONCE more before committing anything to `phase` or
 * scheduling off it. It is bounded to one extra read (not a loop): a second response that is STILL
 * under the threshold is accepted as-is — the sliver of a day this could still be wrong by is
 * immaterial next to the complexity of guarding against it further.
 *
 * <b>Timed from elapsed-since-receipt, never the wall clock.</b> The refresh timer's delay is the
 * server's DURATION (`secondsUntilNextMidnight`), handed directly to `window.setTimeout`. A timer's
 * delay is resolved against the platform's MONOTONIC clock (the same clock `performance.now()` reads)
 * — never `Date.now()` — so a user (or an NTP sync) moving the device's wall clock during the wait
 * cannot move the fire time. The form this rules out is computing a wall-clock TARGET
 * (`Date.now() + delayMs`) and polling `Date.now()` against it later: that IS corrupted by a clock
 * change, because the target and the check are both wall-clock reads taken at different moments.
 * Handing `setTimeout` the duration once, at receipt, is what keeps this immune.
 *
 * <b>Superseded-response guard.</b> A visibility-regain re-read and an already-scheduled refresh
 * timer can both be in flight at once (e.g. the tab becomes visible a moment before the timer was
 * due). Each call is tagged with a monotonically increasing request id (the same pattern as
 * `useBalanceSummary`'s stale-response guard, S126/F2); a response is only committed if its id is
 * still the newest one issued, so an out-of-order resolution can never overwrite a newer day with an
 * older one.
 */
export function useCalendarBootstrap(enabled: boolean): {
  phase: CalendarBootstrapPhase
  /** Re-runs the BOOTSTRAP read from a clean `loading` phase — the error screen's retry action. */
  retry: () => void
} {
  const [phase, setPhase] = useState<CalendarBootstrapPhase>({ kind: 'loading' })
  const timerIdRef = useRef<ReturnType<typeof window.setTimeout> | undefined>(undefined)
  const mountedRef = useRef(false)
  const requestIdRef = useRef(0)

  const clearScheduled = useCallback(() => {
    if (timerIdRef.current !== undefined) {
      window.clearTimeout(timerIdRef.current)
      timerIdRef.current = undefined
    }
  }, [])

  const load = useCallback(
    async (mode: 'bootstrap' | 'refresh', requestId: number, isStaleRetry = false): Promise<void> => {
      const result = await apiClient.get('/api/calendar/today')
      if (!mountedRef.current) return
      // A newer trigger (visibility regain, another scheduled refresh, a manual retry) superseded
      // this call while it was in flight — drop the result rather than risk overwriting a fresher
      // day with a stale one.
      if (requestId !== requestIdRef.current) return

      if (result.ok) {
        const { today, secondsUntilNextMidnight } = result.data as CalendarTodayResponse
        if (secondsUntilNextMidnight < STALE_IN_TRANSIT_THRESHOLD_SECONDS && !isStaleRetry) {
          await load(mode, requestId, true)
          return
        }
        setPhase({ kind: 'ready', today })
        // `secondsUntilNextMidnight` is a DURATION measured at receipt, not a wall-clock target —
        // `window.setTimeout`'s delay argument is itself resolved against the platform's monotonic
        // clock (never `Date.now()`), so handing it this duration directly is what "timed from
        // elapsed-since-receipt, never the wall clock" means in practice. The naive alternative this
        // rules out is computing `Date.now() + delayMs` and polling `Date.now()` against it — THAT
        // form is what a device clock change (NTP sync, DST, a user moving the clock) can corrupt.
        clearScheduled()
        timerIdRef.current = window.setTimeout(() => {
          const nextId = ++requestIdRef.current
          void load('refresh', nextId)
        }, Math.max(0, secondsUntilNextMidnight * 1000))
        return
      }

      if (mode === 'bootstrap') {
        setPhase(result.status === 401 ? { kind: 'unauthorized' } : { kind: 'error' })
        return
      }

      // A REFRESH failure does NOT gate a running app (the asymmetry documented above): `phase` is
      // left untouched — the last known `today` keeps being served — and this schedules one retry.
      clearScheduled()
      timerIdRef.current = window.setTimeout(() => {
        const nextId = ++requestIdRef.current
        void load('refresh', nextId)
      }, REFRESH_RETRY_DELAY_MS)
    },
    [clearScheduled],
  )

  const retry = useCallback(() => {
    setPhase({ kind: 'loading' })
    const requestId = ++requestIdRef.current
    void load('bootstrap', requestId)
  }, [load])

  useEffect(() => {
    if (!enabled) return undefined

    mountedRef.current = true
    setPhase({ kind: 'loading' })
    const bootstrapId = ++requestIdRef.current
    void load('bootstrap', bootstrapId)

    const handleVisibility = () => {
      if (document.visibilityState === 'visible') {
        // No timer reliably survives a sleeping laptop — re-read unconditionally on tab regain
        // rather than trusting whatever schedule was armed before the sleep.
        const requestId = ++requestIdRef.current
        void load('refresh', requestId)
      }
    }
    document.addEventListener('visibilitychange', handleVisibility)

    return () => {
      mountedRef.current = false
      clearScheduled()
      document.removeEventListener('visibilitychange', handleVisibility)
    }
    // `load`/`clearScheduled` are stable (see their own useCallback deps) — `enabled` is the only
    // thing this effect should re-run on. Re-running per render would re-fetch and re-arm endlessly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled])

  return { phase, retry }
}
