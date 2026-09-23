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
 * read only ever runs once a token exists. Auth restore itself is synchronous from `localStorage`
 * (`AuthContext.tsx:49-94` — no network round trip), so this ordering cannot race it.
 *
 * <b>Why a 401 here is NOT a system error.</b> A 401 on this read means the token that made it past
 * `isAuthenticated` has since expired server-side — a session problem, not a calendar problem.
 * Rendering the generic error screen for that would strand a user with an expired session behind a
 * "try again" button that can never succeed (retrying with the same expired token gets the same
 * 401). Routing it to `/login` instead — the same outcome as the `isAuthenticated` check just above
 * — is what actually recovers. `apiClient`'s shared 401 handler (`lib/api.ts`) ALSO clears the token
 * and hard-reloads on any 401, including this one; the `<Navigate>` below is not standing in for
 * that (it still happens), it is what renders correctly in the brief window before it does, and what
 * a test asserts against without stubbing `window.location`.
 *
 * <b>The mechanism</b> (fetch, the midnight-rollover refresh, tab-visibility re-read, the
 * stale-in-transit correction, and why a REFRESH failure does not re-trigger this gate) lives in
 * `useCalendarBootstrap` (`hooks/useCalendarBootstrap.ts`) — this component only renders off its
 * `phase`.
 */
export function RequireAuth() {
  const { isAuthenticated } = useAuth()
  // Called on every render regardless of `isAuthenticated` (Rules of Hooks); the hook itself only
  // fires the network read while `enabled` is true, so an unauthenticated visit to a protected route
  // never issues the authenticated-only calendar call.
  const { phase, retry } = useCalendarBootstrap(isAuthenticated)

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (phase.kind === 'loading') {
    return <CalendarLoadingScreen />
  }

  if (phase.kind === 'unauthorized') {
    return <Navigate to="/login" replace />
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
