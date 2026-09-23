// Smoke tests for the Monday-only / past-or-today-only date picker (S21 / TASK-2109).
// Basic functional coverage only — Phase 5 owns calendar UX polish.
import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeAll, afterAll, beforeEach, afterEach } from 'vitest'
import { MondayDatePicker } from '../MondayDatePicker'
import { forceTestTimeZone, restoreTestTimeZone } from '../../../lib/__tests__/testTimeZone'
import { renderWithCalendar, CalendarTestProvider } from '../../../test/renderWithCalendar'

// S143 / TASK-14313 — `MondayDatePicker` now reads "today" via `useCalendarToday()` (ADR-042)
// UNCONDITIONALLY on every render (even when `pastOrTodayOnly` is false and the value goes
// unused), so every render in this file needs a mounted `CalendarContext.Provider` — EXCEPT the
// dedicated "unresolvable ... (OQ-12)" block at the end, which deliberately omits it to exercise
// the no-provider throw. Most tests here don't care what "today" actually is (Monday-only
// rejection, and the two dates from the S143-TASK-14306b comment below that sit far enough from
// any plausible "today" to never depend on it) — one fixed literal covers them.
const TEST_TODAY = '2026-07-16'

describe('MondayDatePicker', () => {
  it('rejects a non-Monday date when mondayOnly is true', () => {
    const onChange = vi.fn()
    renderWithCalendar(
      TEST_TODAY,
      <MondayDatePicker
        id="dp"
        value="2026-04-27"  // Monday
        onChange={onChange}
        mondayOnly={true}
        pastOrTodayOnly={false}
      />,
    )
    const input = screen.getByDisplayValue('2026-04-27') as HTMLInputElement
    // Try a Tuesday — must NOT propagate.
    fireEvent.change(input, { target: { value: '2026-04-28' } })
    expect(onChange).not.toHaveBeenCalled()
    // The warning text appears.
    expect(screen.getByRole('alert').textContent).toMatch(/mandag/i)
  })

  it('accepts a Monday date when mondayOnly is true', () => {
    const onChange = vi.fn()
    renderWithCalendar(
      TEST_TODAY,
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={true}
        pastOrTodayOnly={false}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    fireEvent.change(dateInput, { target: { value: '2026-04-27' } })  // Monday (UTC)
    expect(onChange).toHaveBeenCalledWith('2026-04-27')
  })

  // S143 TASK-14306b (test-clock hygiene census) — DECLARED COVERAGE GAP, left as-is.
  //
  // The two tests below reject '9999-01-01' and accept '2020-01-15' under pastOrTodayOnly. Both
  // dates sit so far from the real "today" (whatever day this suite happens to run on) that
  // NEITHER test can tell a correctly-computed "today" apart from a badly wrong one — a
  // regression that moved "today" by a day, a year, or onto the wrong side of the planet would
  // still pass both, because a distant future/past date stays future/past regardless. There is
  // nothing to fix IN these two tests: they are honest, correct smoke tests of the accept/reject
  // branches, and pinning them to the real day-of-run would not make them more correct, only more
  // fragile (see the "why literals, not copenhagenToday()" note at line ~116 below — the same
  // trap applies here in reverse).
  //
  // The boundary case that WOULD discriminate a "today"-computation regression already exists in
  // this same file: the 'Copenhagen "today" (S142)' describe block below (added by TASK-14201,
  // ahead of this task) pins the clock with `vi.setSystemTime` and asserts, against LITERAL
  // expected values, that the day exactly at "today" is accepted and the day right after it is
  // refused — see 'accepts the Danish today even though the browser is still on yesterday' and
  // 'still refuses the day AFTER the Danish today'. Adding a second, near-duplicate boundary case
  // here — necessarily reaching for the same fake-timer machinery, since a real, unmocked "today"
  // cannot be pinned to a literal without becoming flaky the next time this suite runs — would
  // contort this file's plain smoke-test section for no additional protection: the regression net
  // already exists, just lower in the file. So: option (a), documented here rather than silently
  // left implicit.
  it('rejects a future date when pastOrTodayOnly is true', () => {
    const onChange = vi.fn()
    renderWithCalendar(
      TEST_TODAY,
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={false}
        pastOrTodayOnly={true}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    // Year 9999 is unambiguously future.
    fireEvent.change(dateInput, { target: { value: '9999-01-01' } })
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toMatch(/fremtiden/i)
  })

  it('passes through past dates when pastOrTodayOnly is true', () => {
    const onChange = vi.fn()
    renderWithCalendar(
      TEST_TODAY,
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={false}
        pastOrTodayOnly={true}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    fireEvent.change(dateInput, { target: { value: '2020-01-15' } })
    expect(onChange).toHaveBeenCalledWith('2020-01-15')
  })

  // ────────────────────────────────────────────────────────────────────────────────────────
  // S142 / TASK-14201 (census row 59) — the picker's "today" is the EUROPE/COPENHAGEN day.
  //
  // WHAT THIS BLOCK PROVES, in plain terms: the date box offers, and accepts, the day it is in
  // Denmark — not the day it is on the user's laptop, and not the day it is at Greenwich. The
  // backend half of the same gate (ConfigEndpoints' EFFECTIVE_FROM_NOT_TODAY_OR_PAST check,
  // census row 14) moved in the SAME commit, so the two cannot disagree at any checkout.
  //
  // S143 / TASK-14313 (Step-7a review) — right calendar was not the whole story: this "today" was
  // still read from the DEVICE's clock (`copenhagenToday()`), which is the right ZONE but the
  // wrong AUTHORITY (ADR-042: the SERVER decides "today," not whichever browser asks). `today` now
  // comes from `useCalendarToday()`, supplied to every render below via `renderWithCalendar`
  // (`'2026-07-16'`, matching this block's own pre-existing literal).
  //
  // WHY THE ZONE/CLOCK FORCING BELOW IS KEPT ANYWAY, even though the picker no longer reads
  // either: it is now a REGRESSION GUARD per this task's PINS, not a load-bearing input. The
  // pinned instant, read under the forced America/New_York zone, is '2026-07-15' — one day BEHIND
  // the '2026-07-16' authority value supplied below. If this control ever regressed to reading the
  // device clock again, the facts below would compute '2026-07-15' and fail against the
  // '2026-07-16' they assert — exactly the discrimination a same-zone, same-day test cannot
  // provide.
  describe('Copenhagen "today" (S142)', () => {
    let restoreTz: string | undefined

    beforeAll(() => {
      restoreTz = forceTestTimeZone('America/New_York')
    })

    afterAll(() => {
      restoreTestTimeZone(restoreTz)
    })

    // THE GUARD ON THE GUARD: if the zone forcing ever stopped taking effect, the facts below
    // would not fail — they would quietly start passing against a browser-local picker, which is
    // the worst outcome available. Assert the precondition with LITERALS: at 22:30 UTC on 15 July
    // a host on America/New_York (EDT, UTC-04:00) reads 18:30 on the 15th.
    it('runs in a zone behind Copenhagen, which is what lets the facts below fail', () => {
      const pinned = new Date('2026-07-15T22:30:00Z')
      expect(pinned.getDate()).toBe(15)
      expect(pinned.getHours()).toBe(18)
    })

    beforeEach(() => {
      // 2026-07-15 22:30 UTC. In Copenhagen (CEST, UTC+02:00) it is already 00:30 on the 16th.
      // In New York (EDT, UTC-04:00) — the forced zone — it is 18:30 on the 15th. Since S143 this
      // pin no longer drives what the picker sees (`renderWithCalendar`'s '2026-07-16' below does)
      // — it is a regression guard, not a load-bearing input; see this block's own header.
      // Expected values below are LITERALS, never derived from a call to the code under test.
      vi.useFakeTimers()
      vi.setSystemTime(new Date('2026-07-15T22:30:00Z'))
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('caps the picker at the Danish day, not the browser day', () => {
      renderWithCalendar(
        '2026-07-16',
        <MondayDatePicker
          id="dp"
          value=""
          onChange={vi.fn()}
          mondayOnly={false}
          pastOrTodayOnly={true}
        />,
      )
      const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
      // The AUTHORITY's day, not the device's — which (per the pin above, under the forced zone)
      // would be '2026-07-15' if this ever regressed.
      expect(dateInput.max).toBe('2026-07-16')
    })

    it('accepts the Danish today even though the browser is still on yesterday', () => {
      const onChange = vi.fn()
      renderWithCalendar(
        '2026-07-16',
        <MondayDatePicker
          id="dp"
          value=""
          onChange={onChange}
          mondayOnly={false}
          pastOrTodayOnly={true}
        />,
      )
      const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
      fireEvent.change(dateInput, { target: { value: '2026-07-16' } })
      // Pre-S142 this was refused as "in the future" — the exact off-by-one the sprint removes.
      expect(onChange).toHaveBeenCalledWith('2026-07-16')
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('still refuses the day AFTER the Danish today', () => {
      const onChange = vi.fn()
      renderWithCalendar(
        '2026-07-16',
        <MondayDatePicker
          id="dp"
          value=""
          onChange={onChange}
          mondayOnly={false}
          pastOrTodayOnly={true}
        />,
      )
      const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
      fireEvent.change(dateInput, { target: { value: '2026-07-17' } })
      // The guard must still BE a guard — moving the boundary must not remove it.
      expect(onChange).not.toHaveBeenCalled()
      expect(screen.getByRole('alert').textContent).toMatch(/fremtiden/i)
    })

    it('clears a now-illegal future date when pastOrTodayOnly flips on, against the Danish day', () => {
      const onChange = vi.fn()
      // `rerender` swaps the ENTIRE rendered tree, not just the element `renderWithCalendar`
      // wrapped — so the re-rendered element must supply its own `CalendarTestProvider`, matching
      // the same authority day, or `useCalendarToday()` throws on the second render.
      const { rerender } = renderWithCalendar(
        '2026-07-16',
        <MondayDatePicker
          id="dp"
          value="2026-07-17"
          onChange={onChange}
          mondayOnly={false}
          pastOrTodayOnly={false}
        />,
      )
      rerender(
        <CalendarTestProvider today="2026-07-16">
          <MondayDatePicker
            id="dp"
            value="2026-07-17"
            onChange={onChange}
            mondayOnly={false}
            pastOrTodayOnly={true}
          />
        </CalendarTestProvider>,
      )
      expect(onChange).toHaveBeenCalledWith('')
    })

    it('does NOT clear the Danish today when pastOrTodayOnly flips on', () => {
      const onChange = vi.fn()
      const { rerender } = renderWithCalendar(
        '2026-07-16',
        <MondayDatePicker
          id="dp"
          value="2026-07-16"
          onChange={onChange}
          mondayOnly={false}
          pastOrTodayOnly={false}
        />,
      )
      rerender(
        <CalendarTestProvider today="2026-07-16">
          <MondayDatePicker
            id="dp"
            value="2026-07-16"
            onChange={onChange}
            mondayOnly={false}
            pastOrTodayOnly={true}
          />
        </CalendarTestProvider>,
      )
      // Pre-S142 the browser-local day was 2026-07-15, so the admin's legal choice was silently
      // wiped out of the form by the re-validation effect.
      expect(onChange).not.toHaveBeenCalled()
    })
  })
})

// ── S142 / owner ruling OQ-12 (2026-09-17) ────────────────────────────────────────────────
//
// S143 / TASK-14313 (Step-7a review) — the FAILURE MODE this block exercises changed; the UI
// CONTRACT it proves did not. Pre-S143, a runtime that could not resolve Europe/Copenhagen made
// `copenhagenToday()` throw (a per-call `Intl` lookup); the two tests below used to mock that
// module to force it. Post-S143 the picker reads `useCalendarToday()` instead, which throws for a
// DIFFERENT reason — no mounted `CalendarContext.Provider` — and is, if anything, LESS reachable
// in the running app: every production caller (`ProfileEditor`, `PersonDrawer`, `DelegationPage`)
// renders only inside `RequireAuth`'s post-`ready` subtree, which guarantees the provider
// (`MondayDatePicker.tsx`'s own comment). The STIMULUS that exercises the catch changed —
// from mocking `copenhagenToday()` to simply omitting `CalendarTestProvider` — but the OBSERVABLE
// CONTRACT below (disabled control, explanatory message, no guessed `max`) is unchanged, so these
// two tests keep proving it rather than being deleted as dead weight.
//
// The RULING is about where that failure is PRESENTED. An uncaught throw during render blanks the
// whole config editor: white screen, no explanation, every unrelated field on the page unreachable.
// OQ-12 requires the picker to catch it and disable THIS control with a message instead — still
// impossible to enter a wrong date, still loud, but the damage confined to the control that
// actually depends on the day.
//
// These facts fail if anyone "simplifies" the try/catch away (back to a white screen) OR replaces
// it with a browser-local fallback (silently wrong dates, the thing S142 removed).
describe('MondayDatePicker — unresolvable "today" (OQ-12)', () => {
  it('renders disabled with an explanation instead of blanking the page', () => {
    const onChange = vi.fn()
    // Deliberately NO `CalendarTestProvider` — `useCalendarToday()` throws exactly as it would if
    // this control were ever rendered outside `RequireAuth`'s post-`ready` subtree. The assertion
    // is that this RENDERS AT ALL — pre-OQ-12 (and still today) an uncaught throw here would
    // otherwise unmount the whole tree.
    render(
      <MondayDatePicker id="dp" value="" onChange={onChange} mondayOnly={false} pastOrTodayOnly={true} />,
    )

    const input = document.querySelector('input[type="date"]') as HTMLInputElement
    expect(input).not.toBeNull()
    expect(input.disabled).toBe(true)
    // The message must name the cause, not just fail silently: a disabled control with no
    // explanation is indistinguishable from a bug. (The text still describes the OLD failure —
    // unresolvable tz data — because rewriting it is a separate, non-clock-read decision; see
    // `MondayDatePicker.tsx`'s own comment.)
    expect(screen.getByRole('alert').textContent).toMatch(/dansk kalenderdag|Europe\/Copenhagen/i)
  })

  it('does not guess a day when "today" cannot be resolved', () => {
    render(
      <MondayDatePicker id="dp" value="" onChange={vi.fn()} mondayOnly={false} pastOrTodayOnly={true} />,
    )

    // `max` caps the picker at "today". With no resolvable today there is no honest value for it,
    // so it must be ABSENT rather than filled from a guess — guessing a business date is the one
    // thing this sprint refuses to do.
    const input = document.querySelector('input[type="date"]') as HTMLInputElement
    expect(input.getAttribute('max')).toBeNull()
  })
})
