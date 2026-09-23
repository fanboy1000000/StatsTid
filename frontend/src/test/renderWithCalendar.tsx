import type { ReactElement, ReactNode } from 'react'
import { render, type RenderOptions, type RenderResult } from '@testing-library/react'
import { CalendarContext } from '../contexts/CalendarContext'

/**
 * S143 / TASK-14301 — the shared test seam for `useCalendarToday`
 * (`contexts/CalendarContext.tsx`).
 *
 * The real app only ever supplies this context from `RequireAuth` once the
 * calendar bootstrap read (`GET /api/calendar/today`) has succeeded — see
 * `hooks/useCalendarBootstrap.ts`. A page test has no interest in that
 * network/timer machinery; it wants a FIXED, hand-written business day. This
 * is that fixed value, supplied directly — no fetch mock, no fake timers, no
 * gate. Any component under test that calls `useCalendarToday()` without
 * being wrapped by this (or `CalendarTestProvider` below) will throw
 * (`CalendarContext.tsx`'s own throw-outside-provider guard) — that throw is
 * the intended signal that a test forgot this wrapper, not a bug to silence.
 *
 * Sibling tasks migrating a screen off the browser clock: wrap your render
 * with this instead of inventing a local provider stub, so every screen's
 * tests pick the "today" up the same way.
 */
export function CalendarTestProvider({
  today,
  children,
}: {
  today: string
  children: ReactNode
}) {
  return <CalendarContext.Provider value={{ today }}>{children}</CalendarContext.Provider>
}

/**
 * `render`, but with `today` fixed via {@link CalendarTestProvider}. Use this in place of
 * `@testing-library/react`'s `render` for any component that (directly or via a child) reads
 * `useCalendarToday()` — `SkemaPage`, `SkemaPageParamInit`, `SkemaGrid`, `ManagerSkemaGrid`,
 * `TeamOversigt`, `TeamRowDetail`, `ArsoversigtPage`, and any future consumer.
 *
 * `today` is always a HAND-WRITTEN literal you choose, never a value derived from `new Date()` or
 * from the code under test — the point of fixing it is to know, independent of when the suite runs,
 * exactly what day the component believes it is.
 */
export function renderWithCalendar(
  today: string,
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'>,
): RenderResult {
  return render(<CalendarTestProvider today={today}>{ui}</CalendarTestProvider>, options)
}
