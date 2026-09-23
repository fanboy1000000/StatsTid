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
 *                  failure, timeout, 5xx, malformed response). The shell shows an error screen with
 *                  a retry.
 *  - `unauthorized` — the bootstrap read got a 401: the token is expired, not the calendar's fault.
 *                  `RequireAuth` routes this to `/login` instead of showing a system-error screen.
 */
export type CalendarBootstrapPhase =
  | { kind: 'loading' }
  | { kind: 'ready'; today: string }
  | { kind: 'error' }
  | { kind: 'unauthorized' }

// A REFRESH (not the startup gate) that fails is retried on a fixed cadence rather than gating the
// running app — see the asymmetry doc below. 30s balances "notice a recovered network reasonably
// soon" against "don't hammer a dependency that is down"; there is no SLA here, only a same-session
// UX preference, so a fixed interval (rather than exponential backoff) keeps the one number legible.
const REFRESH_RETRY_DELAY_MS = 30_000

// A stalled fetch (a dropped connection, a proxy that never responds, a body that never finishes
// streaming) would otherwise leave the STARTUP gate on the loading screen until the browser's own
// connection timeout gives up — which can be minutes, with no error screen and no retry button in
// the meantime. This is deliberately far above any realistic same-origin round trip for a
// no-argument, no-body read, so it never misfires on a slow-but-alive network.
const REQUEST_TIMEOUT_MS = 10_000

// `window.setTimeout`'s delay is a signed 32-bit millisecond count; a value above ~2^31-1 ms
// (~24.8 days) overflows and browsers coerce it to fire almost immediately instead of waiting —
// the OPPOSITE of "wait longer." Real server values are far below this (the backend's own contract
// caps the served duration at the 25-hour day), but a malformed or future response is not something
// this hook should trust blindly before a millisecond conversion; clamping is a two-line guarantee
// regardless of what the server ever actually sends.
const MAX_TIMEOUT_MS = 2_147_483_647

const ISO_CALENDAR_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

/**
 * Whether `value` is a real ISO calendar date (`YYYY-MM-DD`) — not merely a non-empty string that
 * happens to look like one. A prior version of {@link parseCalendarTodayResponse} accepted ANY
 * non-empty string, which let straight through both `"garbage"` and — the one that will actually
 * happen — `"2026-02-31"`: syntactically a date, but not a day that exists (found in Step-5a
 * review; the whole product's notion of "today" flows through this value).
 *
 * The check: parse the three components, then reconstruct a `Date` from them and read the
 * components back. `new Date(year, month - 1, day)` (a THREE-ARGUMENT constructor — not the
 * zero-argument, ambient-clock-reading form this repo's clock guard forbids) NORMALIZES an
 * out-of-range day into the following month (`new Date(2026, 1, 31)` becomes 3 March 2026, month
 * index 2) rather than throwing, so a mismatch between what was asked for and what comes back is
 * exactly a real-date failure. This reuses `Date`'s own well-tested calendar/leap-year arithmetic
 * (the same idiom `useSkema.ts`'s `daysInMonth` already relies on) rather than hand-rolling a
 * days-per-month table here, which would just relocate the risk of an off-by-one bug into new code
 * written specifically to catch off-by-one bugs.
 */
function isValidIsoCalendarDate(value: string): boolean {
  const match = ISO_CALENDAR_DATE_PATTERN.exec(value)
  if (!match) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(year, month - 1, day)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
}

/**
 * Runtime shape-check for the ONE thing this hook receives over the wire: the compile-time type
 * (`CalendarTodayResponse`, generated from the OpenAPI spec) describes what the server is CONTRACTED
 * to send, not what necessarily arrives — a `null`/`204` body, a malformed `{}`, a `today` that is a
 * string but not a real calendar date, or any other TypeScript-can't-see-it wire surprise would
 * otherwise be destructured blindly (throwing OUTSIDE `apiClient`'s own try/catch, or silently
 * publishing an invalid "today" / scheduling a `NaN`-delay timer that fires immediately in a tight
 * loop). This is the one place that distrust is spent, so every OTHER line in this file can treat a
 * parsed response as trustworthy.
 */
function parseCalendarTodayResponse(data: unknown): CalendarTodayResponse | null {
  if (data === null || typeof data !== 'object') return null
  const maybe = data as Record<string, unknown>
  const today = maybe.today
  const secondsUntilNextMidnight = maybe.secondsUntilNextMidnight
  if (typeof today !== 'string' || !isValidIsoCalendarDate(today)) return null
  if (typeof secondsUntilNextMidnight !== 'number' || !Number.isFinite(secondsUntilNextMidnight)) {
    return null
  }
  if (secondsUntilNextMidnight < 0) return null
  return { today, secondsUntilNextMidnight }
}

/** The sentinel `Promise.race` resolves to when {@link REQUEST_TIMEOUT_MS} elapses before the real
    request does. A distinct shape (no other branch of the raced union carries a `timedOut` key), so
    `'timedOut' in raced` is a safe, TypeScript-narrowable discriminant. */
const TIMED_OUT = { timedOut: true as const }

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
 * <b>The one door this asymmetry does NOT cover: a visibility regain BEFORE any day is confirmed.</b>
 * "Refresh failure keeps the last known day" presupposes a last known day EXISTS. Reaching tab
 * visibility while the very first bootstrap is still unresolved has none — so `handleVisibility`
 * (inside the hook, below) decides its mode from `hasConfirmedDayRef`, not from "which event fired,"
 * and a visibility regain before confirmation runs with BOOTSTRAP failure semantics (error screen /
 * `/login`), never refresh semantics (silent, unbounded retry with no escape hatch).
 *
 * <b>A 401 on a refresh does NOT reload the page (Step-5a review bug B2, fixed).</b> Unlike the
 * bootstrap 401 (routed to `/login` via `RequireAuth`'s own effect — see below), a refresh 401 is
 * treated by THIS hook's bookkeeping as just another refresh failure: `phase` is left alone and a
 * quiet retry is scheduled. An EARLIER version of this hook called `apiClient.get` without an
 * opt-out, so `apiClient`'s shared 401 handler (`lib/api.ts`) reacted to EVERY 401 — including a
 * refresh one — by clearing the token and hard-reloading the page UNCONDITIONALLY, before this
 * hook's own bookkeeping ever ran: a half-filled skema, a laptop asleep past token expiry, the tab
 * regaining visibility, and the page reloading with no action from the user — precisely the harm
 * this asymmetry exists to prevent, reintroduced by a DIFFERENT layer entirely. The fix is
 * `skipAuthReload: true` on the request (below): the shared handler's reload is bypassed for THIS
 * read, so a refresh 401 now genuinely keeps serving the last known day like any other refresh
 * failure — no page teardown, no lost work. Ending the SESSION for a bootstrap 401 is instead this
 * hook's own explicit responsibility, achieved via `RequireAuth`'s `logout()` effect, not via the
 * shared handler's side effect.
 *
 * <b>Why `enabled` rather than an unconditional effect.</b> `RequireAuth` must call this hook on
 * EVERY render (Rules of Hooks — it cannot be called only when authenticated), including the render
 * where the user is not yet logged in. Without a gate, an unauthenticated visit to a protected route
 * would fire an authenticated-only network call with no token, receive a 401 that has nothing to do
 * with an expired session, and feed `apiClient`'s shared 401 handler (`lib/api.ts`) needlessly. The
 * caller passes `enabled = isAuthenticated`; the internal effect only runs the bootstrap (and only
 * arms the visibility listener) while `true`, and tears everything down when it flips back to
 * `false` (logout) so no stale timer survives into the next session — and resets
 * `hasConfirmedDayRef` to `false`, so a NEW session's visibility regain (before ITS OWN bootstrap
 * has confirmed anything) cannot inherit the PREVIOUS session's "confirmed" flag.
 *
 * <b>The transit-staleness correction, measured rather than guessed.</b> Every successful response
 * (bootstrap OR refresh) has its ACTUAL transit time measured (`performance.now()` at request start,
 * subtracted from `performance.now()` at receipt) and subtracted from the server's reported
 * `secondsUntilNextMidnight`. If that leaves zero or less time remaining, the response aged past the
 * rollover in transit and this hook re-reads ONCE more before committing anything to `phase` or
 * scheduling off it. This replaced an earlier fixed "`< 5 seconds`" heuristic on the RAW reported
 * value, which was wrong in both directions: a response reporting exactly 5s that took 6s to arrive
 * was accepted as still-good (it was not — "yesterday" would have been momentarily schedulable off
 * it), while a response reporting 6s that took only 100ms was needlessly re-read. Measuring the
 * actual elapsed time answers the real question for every duration, not just ones near a guessed
 * cutoff.
 *
 * <b>Payload validation and the request deadline.</b> `parseCalendarTodayResponse` (module-level,
 * above) is the one place a successful response's SHAPE is distrusted before use — a `null`/`204`
 * body, a `{}`, or any other wire surprise TypeScript's compile-time contract cannot see is treated
 * as a failure (bootstrap: error screen; refresh: quiet retry) rather than destructured blindly,
 * which would otherwise throw OUTSIDE `apiClient`'s own try/catch and strand `phase` at `loading`
 * forever with nothing to catch it. `REQUEST_TIMEOUT_MS` (module-level, above) bounds how long a
 * stalled fetch can hold the gate: `Promise.race`d against the real request, it resolves into the
 * SAME failure path a non-ok response would, rather than leaving the loading screen up until the
 * browser's own connection timeout — which can be minutes — with no error screen and no retry button
 * in between.
 *
 * <b>Timed from elapsed-since-receipt, never the wall clock.</b> The refresh timer's delay is a
 * DURATION (the server's reported seconds, adjusted for measured transit above), handed directly to
 * `window.setTimeout`. A timer's delay is resolved against the platform's MONOTONIC clock (the same
 * clock `performance.now()` reads) — never `Date.now()` — so a user (or an NTP sync) moving the
 * device's wall clock during the wait cannot move the fire time. The form this rules out is
 * computing a wall-clock TARGET (`Date.now() + delayMs`) and polling `Date.now()` against it later:
 * that IS corrupted by a clock change, because the target and the check are both wall-clock reads
 * taken at different moments. Handing `setTimeout` a duration once, at receipt, is what keeps this
 * immune — and `MAX_TIMEOUT_MS` (module-level, above) additionally clamps that duration so an
 * implausible value cannot overflow `setTimeout`'s own signed 32-bit delay argument.
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
  // Tracks "has THIS gate instance ever successfully committed a day" — independent of `phase`,
  // because `phase` is what a RENDER reads, while this must be read inside an EVENT LISTENER
  // closure (`handleVisibility`, below) that was created once, at effect-setup time, and would
  // otherwise see a permanently stale snapshot of `phase` from that moment (see bug B in the S143
  // Step-5a review: `phase` inside that closure was always `loading`, because the effect that
  // creates the listener only re-runs on `enabled`, never on `phase`).
  const hasConfirmedDayRef = useRef(false)

  const clearScheduled = useCallback(() => {
    if (timerIdRef.current !== undefined) {
      window.clearTimeout(timerIdRef.current)
      timerIdRef.current = undefined
    }
  }, [])

  const load = useCallback(
    async (mode: 'bootstrap' | 'refresh', requestId: number, isStaleRetry = false): Promise<void> => {
      // Captured BEFORE the request leaves, so the gap between "the server computed this duration"
      // and "this code is deciding whether to trust it" is a measured monotonic elapsed time, not a
      // guess — see the transit-staleness check below.
      const requestStartPerf = performance.now()

      // `requestPromise` is left to INFER its type from the typed-client overload (matching the
      // literal path key) rather than supplying an explicit type argument — `apiClient.get<'/api/...'
      // >(...)` resolves to the untyped FALLBACK overload instead (its type parameter binds the
      // RESPONSE type, not the path key), silently erasing the real response shape.
      //
      // `skipAuthReload: true` (S143/TASK-14301, Step-5a review bug B2) — WITHOUT this, `apiClient`'s
      // shared 401 handler (`lib/api.ts`) hard-reloads the page on ANY 401, including one arriving on
      // a background REFRESH read, unconditionally and before this hook's own asymmetry bookkeeping
      // ever runs. That is precisely the harm the asymmetry exists to prevent: a half-filled skema, a
      // laptop that slept past token expiry, the tab regaining visibility, and the page reloading
      // with no action from the user. Bypassing the shared handler here means a 401 is reported back
      // as an ordinary `ApiResult` (`status: 401`) and THIS hook decides what happens — a bootstrap
      // 401 ends the session explicitly (`RequireAuth.tsx`'s `logout()` effect); a refresh 401 is
      // just another refresh failure (the `fail()` closure below does not distinguish it further).
      const requestPromise = apiClient.get('/api/calendar/today', { skipAuthReload: true })
      let deadlineTimerId: ReturnType<typeof window.setTimeout> | undefined
      const timeoutPromise = new Promise<typeof TIMED_OUT>((resolve) => {
        deadlineTimerId = window.setTimeout(() => resolve(TIMED_OUT), REQUEST_TIMEOUT_MS)
      })
      const raced = await Promise.race([requestPromise, timeoutPromise])
      // Whichever side won, the deadline timer's job is done — clear it so a request that resolved
      // well within budget does not leave a real (or fake-clock) timer ticking in the background for
      // the rest of REQUEST_TIMEOUT_MS for no purpose.
      if (deadlineTimerId !== undefined) window.clearTimeout(deadlineTimerId)

      if (!mountedRef.current) return
      // A newer trigger (visibility regain, another scheduled refresh, a manual retry) superseded
      // this call while it was in flight — drop the result rather than risk overwriting a fresher
      // day with a stale one.
      if (requestId !== requestIdRef.current) return

      // Every failure path (network/5xx, a request that timed out, or a payload that parsed to
      // nothing usable) converges here: bootstrap failures gate with an escape hatch (`unauthorized`
      // routes to `/login`, anything else shows the error screen's retry); refresh failures do
      // neither — they leave `phase` untouched and quietly retry (the asymmetry documented below).
      const fail = (isUnauthorized: boolean): void => {
        if (mode === 'bootstrap') {
          setPhase(isUnauthorized ? { kind: 'unauthorized' } : { kind: 'error' })
          return
        }
        clearScheduled()
        timerIdRef.current = window.setTimeout(() => {
          const nextId = ++requestIdRef.current
          void load('refresh', nextId)
        }, REFRESH_RETRY_DELAY_MS)
      }

      if ('timedOut' in raced) {
        fail(false)
        return
      }
      const result = raced
      if (!result.ok) {
        fail(result.status === 401)
        return
      }

      const parsed = parseCalendarTodayResponse(result.data)
      if (parsed === null) {
        fail(false)
        return
      }

      // The MEASURED transit delay: how long the round trip (plus this code's own scheduling) took,
      // read from the SAME monotonic clock the schedule below is timed from — never `Date.now()`, so
      // this cannot be corrupted by a device clock change happening mid-request. Subtracting it from
      // the server's reported duration answers "how much time is ACTUALLY left," not "how much was
      // left when the server computed the response" — the earlier `< 5 seconds` heuristic asked the
      // second question and got it right only by coincidence: a response reporting exactly 5s that
      // then took 6s in transit was accepted as still-good, while a response that took 4s in transit
      // (well within realistic jitter) but reported 6s was needlessly re-read. Measuring transit
      // directly answers the actual question for every duration, not just ones near the old cutoff.
      const transitMs = performance.now() - requestStartPerf
      const remainingMs = parsed.secondsUntilNextMidnight * 1000 - transitMs

      if (remainingMs <= 0 && !isStaleRetry) {
        // The response aged past the rollover in transit — bounded to ONE extra STALE-CHECK read
        // (not an unbounded loop): a second response that is STILL non-positive after ITS OWN
        // transit is accepted as-is rather than recursing again (below). It does NOT stay wrong,
        // though — see the delay clamp immediately below: a non-positive remaining duration produces
        // a `Math.max(0, remainingMs)` of exactly 0, which schedules its OWN follow-up refresh
        // immediately, as an ORDINARY refresh cycle (not a further stale-retry attempt). That is a
        // real, observable extra fetch — pinned by
        // `useCalendarBootstrap.test.ts`'s "self-heals via an IMMEDIATE follow-up refresh" case —
        // not something to imply away by calling this "bounded to one extra read" without qualifying
        // which mechanism bounds which part.
        await load(mode, requestId, true)
        return
      }

      setPhase({ kind: 'ready', today: parsed.today })
      hasConfirmedDayRef.current = true
      clearScheduled()
      // Clamped so a malformed-but-technically-parseable huge duration cannot overflow
      // `setTimeout`'s signed 32-bit delay (see MAX_TIMEOUT_MS) — belt-and-suspenders alongside the
      // payload validation above, which rejects negative/non-finite/non-numeric values but not
      // merely IMPLAUSIBLY LARGE ones (the backend's contract, not this hook, is what makes those
      // implausible; this hook does not re-derive or enforce that contract, only refuses to let a
      // number it cannot vouch for crash a browser API). The SAME `Math.max(0, …)` floor is also what
      // gives the self-heal above its immediate (0ms) follow-up refresh when `remainingMs` is negative.
      const delayMs = Math.min(Math.max(0, remainingMs), MAX_TIMEOUT_MS)
      timerIdRef.current = window.setTimeout(() => {
        const nextId = ++requestIdRef.current
        void load('refresh', nextId)
      }, delayMs)
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
    hasConfirmedDayRef.current = false
    setPhase({ kind: 'loading' })
    const bootstrapId = ++requestIdRef.current
    void load('bootstrap', bootstrapId)

    const handleVisibility = () => {
      if (document.visibilityState !== 'visible') return
      // No timer reliably survives a sleeping laptop — re-read unconditionally on tab regain.
      //
      // WHICH SEMANTICS apply is decided by whether a day has EVER been confirmed in this gate
      // instance (`hasConfirmedDayRef`) — NOT by "this happens to be a visibility event" and NOT by
      // reading `phase` from this closure (a stale snapshot from effect-setup time; see the ref's own
      // doc above). Before any day is confirmed, a visibility regain is functionally a SECOND
      // bootstrap attempt: `mode: 'bootstrap'` means a failure surfaces the error screen (or routes
      // to `/login`) instead of silently retrying forever with nothing on screen and no escape hatch
      // — which is exactly the bug this distinction fixes (S143 Step-5a review, bug B): a user who
      // tabs away and back while the FIRST bootstrap is still in flight used to get a `refresh`-mode
      // call, whose failure path leaves `phase` untouched — but `phase` was still `loading`, so
      // "untouched" meant "stuck on the loading screen forever," with no error screen and no retry
      // button. Once a day HAS been confirmed, this is a genuine refresh, and keeps its quiet-retry
      // semantics (the asymmetry documented on `load` above).
      const requestId = ++requestIdRef.current
      void load(hasConfirmedDayRef.current ? 'refresh' : 'bootstrap', requestId)
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
