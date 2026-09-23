// S143 / TASK-14301 — the shell gate itself: `RequireAuth` renders NOTHING past the
// `isAuthenticated` check until the calendar bootstrap read succeeds (owner ruling OQ-1d), a 401
// routes to `/login` rather than showing a system-fault screen, and any OTHER failure shows the
// error screen with a working retry. Also covers PINS scenario 2 (an already-open "month" — modeled
// here as a value a consumer snapshots on mount — does not move when a later refresh completes),
// the one AC-7 pin that is about the CONTEXT/consumer relationship rather than the hook's own
// timing, so it belongs at this level rather than in `useCalendarBootstrap.test.ts`.
import type { ReactNode } from 'react'
import { useState } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { RequireAuth } from '../RequireAuth'
import { useCalendarToday } from '../../../contexts/CalendarContext'

const auth = vi.hoisted(() => ({ isAuthenticated: true }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ isAuthenticated: auth.isAuthenticated }),
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

/** PINS-2's probe: SNAPSHOTS `today` once on mount (the discipline every sibling screen is told to
    follow — see `CalendarContext.tsx`'s own doc), rather than reading it reactively. */
function SnapshottingProbe() {
  const today = useCalendarToday()
  const [openedOn] = useState(today)
  return <div data-testid="opened-on">{openedOn}</div>
}

function renderGated(child: ReactNode) {
  return render(
    <MemoryRouter initialEntries={['/protected']}>
      <Routes>
        <Route path="login" element={<div>LOGIN PAGE</div>} />
        <Route element={<RequireAuth />}>
          <Route path="protected" element={child} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
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

  it('authenticated + a 401 on the read: routes to /login, NOT the error screen — an expired token is not a calendar fault', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}, 401))
    renderGated(<TodayProbe />)
    await waitFor(() => expect(screen.getByText('LOGIN PAGE')).toBeInTheDocument())
    expect(screen.queryByText('Dags dato kunne ikke hentes')).toBeNull()
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

  it('PINS-2: an already-open "month" (a value a consumer snapshotted on mount) does not move when a later refresh completes, even though the raw context value does', async () => {
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
    expect(screen.getByTestId('today')).toHaveTextContent('2026-03-16')

    // The snapshot a consumer took on mount is UNCHANGED — this is the discipline
    // `CalendarContext.tsx` documents: read once for "which month do I open on," don't react to
    // every later update. The raw context (TodayProbe, above) DID move — the plumbing is live; it is
    // the consumer's own choice to snapshot that protects an already-open month.
    expect(screen.getByTestId('opened-on')).toHaveTextContent('2026-03-15')

    vi.useRealTimers()
  })
})
