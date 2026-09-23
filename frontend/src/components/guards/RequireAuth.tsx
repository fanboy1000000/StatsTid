import { useEffect } from 'react'
import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { useCalendarBootstrap } from '../../hooks/useCalendarBootstrap'
import { CalendarContext } from '../../contexts/CalendarContext'
import { CalendarLoadingScreen } from './CalendarLoadingScreen'
import { CalendarErrorScreen } from './CalendarErrorScreen'

/**
 * S143 / TASK-14301 — `RequireAuth` now does TWO gates, in order: authentication, then the
 * calendar bootstrap read. Both must pass before anything past this point (the whole app shell,
 * `AppLayout` and every route it wraps) renders.
 *
 * <b>Why the calendar gate lives HERE and not in `main.tsx` (owner ruling OQ-1d).</b> Gating `App`
 * itself would also block `/login`: an unauthenticated visitor would hit a gate that calls an
 * AUTHENTICATED-only endpoint (`GET /api/calendar/today` requires the "Authenticated" policy —
 * `CalendarEndpoints.cs`) before they have any token to send, with no way to ever obtain one — an
 * unrecoverable boot loop. Placing the gate AFTER the `isAuthenticated` check means the calendar
 * read only ever runs once a token EXISTS in storage.
 *
 * <b>"Exists" is not "valid" — corrected (Step-5a review W1).</b> An EARLIER version of this doc
 * claimed auth restore is synchronous, "so this ordering cannot race it." That overstated things:
 * `AuthProvider`'s TOKEN READ is synchronous (`useState(getStoredToken)`, `AuthContext.tsx`), so
 * `isAuthenticated` is `true` on the very first render whenever a token STRING is present in
 * `localStorage` — expired or not. The EXPIRY check that would clear a stale token lives in
 * `AuthProvider`'s OWN `useEffect` (`AuthContext.tsx`'s mount effect), which — because React fires
 * child effects before parent effects — runs AFTER this gate's `useCalendarBootstrap` effect has
 * already started the network read. So a stale morning session (a token whose `exp` passed overnight)
 * CAN start the calendar read carrying an expired `Authorization` header. This does not race the
 * OUTCOME, though: `AuthProvider`'s synchronous-relative-to-network expiry check clears the token
 * and re-renders (flipping `isAuthenticated` to `false`, which — via the `enabled` gate on
 * `useCalendarBootstrap` — tears down that in-flight read's `mountedRef`/request id) well before any
 * REAL network round trip can return, and even in the residual case where the stale request's 401
 * response wins that race, the `unauthorized`-phase effect below reaches the identical outcome
 * (`logout()`, then `/login`). Either path ends the session cleanly — see bug B1 below for why that
 * did NOT used to hold.
 *
 * <b>Why a 401 here is NOT a system error, and why it must actually END the session (Step-5a review
 * bug B1, fixed).</b> A 401 on this read means the token that made it past `isAuthenticated` has
 * since expired server-side — a session problem, not a calendar problem. Rendering the generic error
 * screen for that would strand a user behind a "try again" button that can never succeed (retrying
 * with the same expired token gets the same 401). An EARLIER version of this component rendered
 * `<Navigate to="/login" replace />` for `unauthorized` while `isAuthenticated` was STILL `true` —
 * `handle401()` (`lib/api.ts`) only ever cleared `localStorage`, never the React auth STATE, so
 * `/login`'s own route (`App.tsx`: `isAuthenticated ? <Navigate to="/tid/registrering"/> : ...`)
 * immediately bounced back to a protected route, remounting this gate, firing a fresh read with the
 * SAME still-expired token, hitting 401 again — measured at 26 calendar reads and 25 page reloads in
 * under five seconds before a review probe stopped it deliberately. The fix below actually ends the
 * session (`useAuth().logout()`), so `isAuthenticated` is genuinely `false` by the time `/login` is
 * reached, and the bounce-back never fires.
 *
 * <b>The mechanism</b> (fetch, the midnight-rollover refresh, tab-visibility re-read, the
 * transit-staleness correction, the request deadline, payload validation, and why a REFRESH failure
 * does not re-trigger this gate NOR reload the page on its own 401) lives in `useCalendarBootstrap`
 * (`hooks/useCalendarBootstrap.ts`) — this component renders off its `phase` and additionally owns
 * ENDING THE SESSION when that phase is `unauthorized` (the one piece of session-lifecycle policy
 * that belongs here, not in the hook, since only this component holds `useAuth()`).
 */
export function RequireAuth() {
  const { isAuthenticated, logout } = useAuth()
  // Called on every render regardless of `isAuthenticated` (Rules of Hooks); the hook itself only
  // fires the network read while `enabled` is true, so an unauthenticated visit to a protected route
  // never issues the authenticated-only calendar call.
  const { phase, retry } = useCalendarBootstrap(isAuthenticated)

  // Bug B1 fix: an `unauthorized` phase means the token is expired — actually END the session
  // (clears storage AND the React auth state) rather than merely rendering a redirect while
  // `isAuthenticated` stays `true`. Runs in an effect (a render must not call another context's
  // state setter); the dependency on `phase.kind` alone (not the whole `phase` object) means this
  // fires exactly once per transition INTO `unauthorized`, not on every render while it persists —
  // `logout()` is idempotent regardless, but this avoids a repeated no-op call.
  useEffect(() => {
    if (phase.kind === 'unauthorized') {
      logout()
    }
  }, [phase.kind, logout])

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  // `unauthorized` renders the SAME screen as `loading`: it is a transient state by construction —
  // the effect above fires `logout()` essentially immediately, which flips `isAuthenticated` to
  // `false` on the NEXT render and the branch above takes over. There is nothing useful to show in
  // between (not an error — the session is ending correctly), so it borrows the loading screen
  // rather than introducing a new one for a state no user meaningfully dwells in.
  if (phase.kind === 'loading' || phase.kind === 'unauthorized') {
    return <CalendarLoadingScreen />
  }

  if (phase.kind === 'error') {
    return <CalendarErrorScreen onRetry={retry} />
  }

  return (
    <CalendarContext.Provider value={{ today: phase.today }}>
      <Outlet />
    </CalendarContext.Provider>
  )
}
