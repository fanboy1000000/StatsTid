// S143 / TASK-14301 — the shell gate itself: `RequireAuth` renders NOTHING past the
// `isAuthenticated` check until the calendar bootstrap read succeeds (owner ruling OQ-1d), a 401
// ends the session for real (not just a redirect while still "authenticated") rather than showing a
// system-fault screen, and any OTHER failure shows the error screen with a working retry.
//
// Also covers the two AC-7 pins that are about the CONTEXT/consumer relationship rather than the
// hook's own timing, so they belong at THIS level (a real React context with real consumers) rather
// than in `useCalendarBootstrap.test.ts`:
//
//  - "the context updates live without remounting" (renamed from the original "PINS-2" — Step-5a
//    review found its `SnapshottingProbe` proves the SEAM's own guarantee, which is real and worth
//    keeping, but cannot detect "a real screen moving its open month," since that behavior lives in
//    each sibling screen's OWN future implementation and test, not in this seam);
//  - "a mount during retry backoff sees the last known day" (the genuine PINS-4 claim — Step-5a
//    review found the hook-level test never actually mounted a second consumer mid-backoff; this one
//    does, via `rerender` on the SAME `RequireAuth` instance so a real route-transition-style mount
//    is exercised without disturbing the gate's own timers/state).
//
// A SECOND Step-5a review pass (bug B1) found the 401 test below passed for the WRONG reason: its
// route table rendered `/login` UNCONDITIONALLY, omitting the App-level bounce-back
// (`isAuthenticated ? <Navigate to="/tid/registrering"/> : <LoginContent/>`, `App.tsx`) that — in
// production, because `apiClient`'s `handle401` only ever cleared `localStorage` and never the React
// auth STATE — closed a loop: `/login` saw `isAuthenticated` still `true` and bounced straight back
// to the protected route, remounting `RequireAuth`, firing a fresh calendar read, hitting 401 again.
// Measured at 26 calendar reads and 25 page reloads in under five seconds before a probe stopped it.
// The mock `useAuth` below and the shared `Tree` route table are now BOTH faithful to that mechanism:
// `logout()` genuinely flips `isAuthenticated`, and `/login` bounces back when it is still `true` —
// so this suite can actually prove the loop terminates, not merely that a login page's TEXT appears.
import type { ReactNode } from 'react'
import { useState } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route, Navigate } from 'react-router-dom'
import { RequireAuth } from '../RequireAuth'
import { useAuth } from '../../../contexts/AuthContext'
import { useCalendarToday } from '../../../contexts/CalendarContext'

const auth = vi.hoisted(() => ({ isAuthenticated: true }))
vi.mock('../../../contexts/AuthContext', () => ({
  // A REAL stateful hook (not a plain function returning a fixed snapshot): `logout` must actually
  // trigger a re-render for this suite to prove anything about the B1 loop, exactly as the real
  // `AuthContext.logout` (`setToken(null)`, etc.) does. The shared `auth` control object is what a
  // FRESH mount (e.g. `/login`, mounted only after `RequireAuth` unmounts) reads its OWN initial
  // state from — `logout` writes to both so a subsequent fresh mount sees the ended session too.
  useAuth: () => {
    const [isAuthenticated, setIsAuthenticated] = useState(auth.isAuthenticated)
    return {
      isAuthenticated,
      logout: () => {
        auth.isAuthenticated = false
        setIsAuthenticated(false)
      },
    }
  },
}))

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

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

beforeEach(() => {
  auth.isAuthenticated = true
  mockFetch.mockReset()
  mockReload.mockReset()
})

/** A protected-route probe that renders the day it received via context. */
function TodayProbe() {
  const today = useCalendarToday()
  return <div data-testid="today">{today}</div>
}

/** A SECOND, independently-identified probe — used to prove that a consumer mounted LATER (a fresh
    route/component, not present at gate-mount time) reads the same live context a consumer mounted
    from the start would. */
function FreshMountProbe() {
  const today = useCalendarToday()
  return <div data-testid="fresh-mount-today">{today}</div>
}

/** SNAPSHOTS `today` once on mount (the discipline every sibling screen is told to follow — see
    `CalendarContext.tsx`'s own doc), rather than reading it reactively. */
function SnapshottingProbe() {
  const today = useCalendarToday()
  const [openedOn] = useState(today)
  return <div data-testid="opened-on">{openedOn}</div>
}

/** Mirrors `App.tsx`'s ACTUAL `/login` route (`isAuthenticated ? <Navigate .../> : <LoginContent/>`)
    — the piece the ORIGINAL version of this test omitted, which is exactly what let the B1 loop's
    bounce-back go unmodeled. A stub that always rendered "LOGIN PAGE" regardless of auth state could
    not distinguish "the redirect worked" from "the redirect is about to bounce right back." */
function LoginRouteElement() {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <Navigate to="/protected" replace /> : <div>LOGIN PAGE</div>
}

function Tree({ children }: { children: ReactNode }) {
  return (
    <MemoryRouter initialEntries={['/protected']}>
      <Routes>
        <Route path="login" element={<LoginRouteElement />} />
        <Route element={<RequireAuth />}>
          <Route path="protected" element={children} />
        </Route>
      </Routes>
    </MemoryRouter>
  )
}

function renderGated(child: ReactNode) {
  return render(<Tree>{child}</Tree>)
}

describe('RequireAuth — the calendar shell gate', () => {
  it('unauthenticated: routes straight to /login and never fires the authenticated-only calendar read', async () => {
    auth.isAuthenticated = false
    renderGated(<TodayProbe />)
    await waitFor(() => expect(screen.getByText('LOGIN PAGE')).toBeInTheDocument())
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it('authenticated + read pending: shows the loading screen, not the route (nothing renders yet — OQ-1d)', () => {
    mockFetch.mockImplementation(() => new Promise(() => {})) // never resolves in this test
    renderGated(<TodayProbe />)
    expect(screen.getByText('Henter dags dato…')).toBeInTheDocument()
    expect(screen.queryByTestId('today')).toBeNull()
  })

  it('authenticated + read succeeds: renders the route, with the server day available via context', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    renderGated(<TodayProbe />)
    await waitFor(() => expect(screen.getByTestId('today')).toHaveTextContent('2026-03-15'))
  })

  // Step-5a review bug B1 (see the file-header note for the full mechanism). This is now checked
  // against a FAITHFUL router: `/login` bounces back to `/protected` whenever `isAuthenticated` is
  // still `true` — the exact mechanism that turned a naive redirect into a 26-read, 25-reload loop
  // in production. Reaching "LOGIN PAGE" here means the session was actually ended, not merely that
  // a `<Navigate>` was rendered once.
  it('authenticated + a 401 on the read: ENDS THE SESSION for real — reaches /login without looping back, and without needing a page reload to get there', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}, 401))
    renderGated(<TodayProbe />)

    await waitFor(() => expect(screen.getByText('LOGIN PAGE')).toBeInTheDocument())
    expect(screen.queryByText('Dags dato kunne ikke hentes')).toBeNull()
    // Exactly ONE calendar read — a surviving loop would keep climbing well past this.
    expect(mockFetch).toHaveBeenCalledTimes(1)
    // `skipAuthReload` (bug B2 fix, `useCalendarBootstrap.ts`) means `apiClient`'s shared handler
    // never fires for this read — `logout()` (this component's own effect) is what ends the
    // session, so no reload was needed to reach a stable, correct `/login`.
    expect(mockReload).not.toHaveBeenCalled()
  })

  it('authenticated + a non-401 failure: shows the error screen (not login), and "Prøv igen" recovers', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))
    renderGated(<TodayProbe />)
    await waitFor(() =>
      expect(screen.getByText('Dags dato kunne ikke hentes')).toBeInTheDocument(),
    )
    expect(screen.queryByTestId('today')).toBeNull()

    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 3600 }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Prøv igen' }))
    await waitFor(() => expect(screen.getByTestId('today')).toHaveTextContent('2026-03-15'))
  })

  it('a refresh updates the live context value WITHOUT remounting the Provider/Outlet subtree — the invariant a screen\'s own "snapshot today once" pattern relies on to keep an already-open month from moving', async () => {
    vi.useFakeTimers()
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }))
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }))

    renderGated(
      <>
        <SnapshottingProbe />
        <TodayProbe />
      </>,
    )
    // Fake timers are active, so this deliberately does NOT use testing-library's `waitFor` (its
    // polling loop cannot progress against a paused clock) — `act` + `advanceTimersByTimeAsync(0)`
    // flushes the pending bootstrap microtask/render synchronously instead (same pattern as
    // `useCalendarBootstrap.test.ts`).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(screen.getByTestId('opened-on')).toHaveTextContent('2026-03-15')
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-15')

    // The scheduled midnight refresh completes with a NEW day.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    // The LIVE context value moved — proving the Provider re-rendered with the new value rather
    // than, say, being torn down and only picked up on a later remount.
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-16')

    // The EARLIER snapshot is unchanged — `useState`'s initial-value-only semantics only hold if the
    // component was NOT remounted; had the Provider (or `Outlet`) remounted on this update, React
    // would re-evaluate `useState(today)`'s initial value against the NEW `today` and this would read
    // '2026-03-16' too. This is the primitive sibling screens are told to build their own "which
    // month do I open on" logic on (`CalendarContext.tsx`'s doc) — what EACH screen actually does
    // with a moved value is that screen's own concern and its own future test, not this seam's.
    expect(screen.getByTestId('opened-on')).toHaveTextContent('2026-03-15')

    vi.useRealTimers()
  })

  it('a mount during retry backoff after a refresh failure sees the last known day — a FRESH consumer, mounted mid-backoff (not present when the gate first opened), reads the identical value an original consumer sees', async () => {
    vi.useFakeTimers()
    mockFetch
      .mockResolvedValueOnce(jsonResponse({ today: '2026-03-15', secondsUntilNextMidnight: 100 }))
      .mockResolvedValueOnce(jsonResponse({ error: 'boom' }, 500))

    const { rerender } = render(<Tree><TodayProbe /></Tree>)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-15')

    // The scheduled refresh fires and FAILS — the last known day keeps being served (the asymmetry).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-15')

    // DURING the retry backoff window, a FRESH consumer mounts — e.g. a route transition rendering a
    // page that was not on screen when the gate first opened. `rerender` targets the SAME
    // `RequireAuth`/`CalendarContext.Provider` instance (same element position/type in the tree) —
    // it is NOT torn down and recreated by this — so this genuinely exercises "a consumer that did
    // not exist yet subscribes to the gate's CURRENT state," not merely re-reading a value already
    // held by a component that was there the whole time.
    rerender(
      <Tree>
        <>
          <TodayProbe />
          <FreshMountProbe />
        </>
      </Tree>,
    )
    expect(screen.getByTestId('fresh-mount-today')).toHaveTextContent('2026-03-15')
    // Mounting a new consumer does not itself trigger any network activity.
    expect(mockFetch).toHaveBeenCalledTimes(2)

    // Still within the backoff window — no change yet.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(29_000)
    })
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(screen.getByTestId('fresh-mount-today')).toHaveTextContent('2026-03-15')

    // The retry fires and recovers — the freshly-mounted consumer sees the update exactly like the
    // original one does, confirming it is reading the SAME live context, not a frozen copy.
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ today: '2026-03-16', secondsUntilNextMidnight: 86400 }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000)
    })
    expect(screen.getByTestId('fresh-mount-today')).toHaveTextContent('2026-03-16')
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-16')

    vi.useRealTimers()
  })
})
