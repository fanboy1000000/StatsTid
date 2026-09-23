import { createContext, useContext } from 'react'

/**
 * S143 / TASK-14301 — the calendar seam's CONSUMER-FACING context.
 *
 * Plain-language why: several screens used to ask the BROWSER's clock which
 * month to open on. That month is not decoration — it is sent as the period
 * envelope of the skema save and the approval send, so it decides which month
 * a person's real work hours are filed under. A device in the wrong time zone
 * (or simply with a wrong clock) filed work into the wrong period, silently.
 * Owner ruling OQ-1a/1b (2026-09-23): the SERVER is the authority on "today,"
 * delivered once at app start via `GET /api/calendar/today`
 * (`StatsTid.Backend.Api/Endpoints/CalendarEndpoints.cs`). This context is how
 * that value reaches every screen — never `new Date()`.
 *
 * This module is deliberately SEPARATE from `useCalendarBootstrap` (the hook
 * that performs the read, the midnight refresh and the retry/backoff): the
 * hook contains the network/scheduling MECHANISM; this file is the small,
 * stable SHAPE that consumers (and tests) depend on. Splitting them means a
 * test can supply a fixed `today` (via `renderWithCalendar`,
 * `src/test/renderWithCalendar.tsx`) without dragging in fetch mocking, fake
 * timers or visibility-event plumbing.
 */
export interface CalendarContextValue {
  /**
   * The Europe/Copenhagen BUSINESS DAY (`YYYY-MM-DD`), as last confirmed by
   * the server — never derived from the browser's clock. ADR-041 draws the
   * line this value lives on: every BUSINESS DATE in StatsTid is this
   * Copenhagen calendar day, while INSTANTS (audit timestamps, outbox
   * ordering) stay UTC and are unaffected by this context.
   *
   * This value CHANGES during a session — at the Copenhagen day rollover, and
   * whenever the tab regains visibility (see `useCalendarBootstrap`). A
   * screen that opens on "today's" month must snapshot this value ONCE
   * (e.g. `useState(() => today)`), not read it reactively on every render —
   * otherwise an already-open month would jump when the day rolls over under
   * the user (PINS scenario 2 in the S143 spec). This context intentionally
   * does not decide that for consumers; it only ever reports the CURRENT
   * known day.
   */
  today: string
}

export const CalendarContext = createContext<CalendarContextValue | null>(null)

/**
 * The day every screen must use in place of `new Date()` / `Date.now()` for
 * "which month/day do I open on." Throws when read outside a mounted gate —
 * mirrors `useAuth`'s own throw-outside-provider discipline
 * (`AuthContext.tsx`), so a missing wrapper is a loud test failure rather
 * than a silent `undefined` reaching a date computation.
 *
 * In the running app the provider is mounted by `RequireAuth`
 * (`components/guards/RequireAuth.tsx`) ONLY once the calendar gate reaches
 * its `ready` phase — so by construction, any component that can render this
 * hook's call already has a server-confirmed day. In tests, use
 * `renderWithCalendar` (`src/test/renderWithCalendar.tsx`) rather than
 * reaching for this context directly.
 */
export function useCalendarToday(): string {
  const ctx = useContext(CalendarContext)
  if (ctx === null) {
    throw new Error(
      'useCalendarToday must be used within a mounted calendar gate (RequireAuth) or ' +
        "renderWithCalendar in tests — it is not available before the app's calendar " +
        'bootstrap read has succeeded.',
    )
  }
  return ctx.today
}
