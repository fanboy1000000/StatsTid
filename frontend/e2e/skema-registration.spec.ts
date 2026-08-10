import { test, expect, type Page } from '@playwright/test'
import { login } from './helpers/auth'
import { runNonce, targetMonth, nonBoundaryWeekday, nonceWeekdayIndex } from './helpers/dates'

/**
 * SPRINT-82 journey 2 (R2b): Skema absence registration → persists → visible
 * after a real reload.
 *
 * emp001 (AC / STY01) registers a Sygedag (SICK_DAY) absence on a runtime-derived
 * NON-boundary weekday (R4: mid-month, Mon–Fri, UTC, never an edge), the debounced
 * save POSTs to /api/skema/emp001/save, and the registration survives a full page
 * reload (server-truth, not an in-memory check).
 *
 * SICK_DAY is the robust absence type: always eligible for emp001/AC, not
 * fullDayOnly (no consumption-basis snap to reason about), partial-day legal.
 *
 * Re-run tolerance: the per-run nonce targets a UNIQUE future (month, day) slot, so
 * a re-run registers into a fresh slot and can never go green-but-weak against state
 * left in the persistent local Postgres volume. The approval journey runs in PARALLEL
 * against the SAME employee and locks the whole month it sends, so the two must not
 * share a month — S127 made that separation structural (see SKEMA_MONTH_OFFSET below).
 * The save-side lock is `SkemaEndpoints.IsPeriodLockedForSave`, located by name: the
 * line number this comment used to carry (`:604`) had drifted onto the route
 * registration.
 */

const ABSENCE_LABEL = 'Sygedag' // SICK_DAY — the cell aria-label is `${label} dag ${dayOfMonth}`
// A half-day SICK_DAY value (valid: SICK_DAY is not fullDayOnly, partial-day legal).
// Deliberately NOT the weekday norm (7,4): the empty absence cell's focus-prefill
// seeds the norm, so a distinct value guarantees our typed entry is a REAL change
// (a fresh onChange → a scheduled debounced save) instead of a no-op over the prefill.
const HOURS_DISPLAY = '3,7'

// Per-SPEC month offset. The skema + approval journeys both mutate emp001 and run in
// PARALLEL (Playwright runs files across workers).
//
// S127 / TASK-12709 — THE OLD SAFETY ARGUMENT HERE IS VOID AND WAS REPLACED. It read:
// "same-month is benign, because the approval journey only approves SINGLE-DAY periods
// and the skema period-lock keys on the exact period span, so the two never collide."
// The approval journey now sends a WHOLE MONTH — which is exactly the span this page's
// save looks up — and sending locks it (the backend locks EMPLOYEE_APPROVED; it never
// locked SUBMITTED), so a shared month WOULD make this save fail.
//
// The separation is now structural rather than argued: approval.spec.ts takes its month
// from its OWN window starting 19 months out and never re-enters `targetMonth`'s [1, 18]
// slots, which this spec keeps to itself. See approval.spec.ts's MONTH_WINDOW_START.
const SKEMA_MONTH_OFFSET = 0

/** Drive the SkemaPage month-nav (internal year/month state, no URL param) FORWARD
 *  `clicks` whole months from the current UTC month onto the target. Each click is
 *  followed by a web-first assertion on the month title so navigation can't race. */
async function navigateSkemaToMonth(page: Page, clicks: number, monthLabel: string): Promise<void> {
  const next = page.getByRole('button', { name: /Næste/ })
  for (let i = 0; i < clicks; i++) {
    await next.click()
  }
  await expect(page.getByRole('heading', { name: monthLabel })).toBeVisible()
}

test('emp001 registers a Sygedag absence on a non-boundary weekday and it persists across reload', async ({
  page,
}) => {
  const nonce = runNonce()
  const { year, month, forwardClicks } = targetMonth(nonce, SKEMA_MONTH_OFFSET)
  // Nonce-rotated mid-month weekday: re-runs land on a fresh (month, day) slot, so
  // even when the bounded month window recycles, this never reuses a day a prior run
  // already wrote (idempotent registration would still pass, but this keeps it clean).
  const isoDay = nonBoundaryWeekday(year, month, nonceWeekdayIndex(nonce, year, month))
  const dayOfMonth = Number(isoDay.slice(-2))
  // The SkemaGrid month title is `${DANISH_MONTHS[month-1]} ${year}` (formatMonthLabel).
  const DANISH_MONTHS = [
    'januar', 'februar', 'marts', 'april', 'maj', 'juni',
    'juli', 'august', 'september', 'oktober', 'november', 'december',
  ]
  const monthLabel = `${DANISH_MONTHS[month - 1]} ${year}`
  // The absence cell input is selected by its stable aria-label: `${label} dag ${dayOfMonth}`.
  const cellLabel = `${ABSENCE_LABEL} dag ${dayOfMonth}`

  await login(page, 'emp001')

  // Land on the registration page (the index redirect already put us there).
  await expect(page).toHaveURL(/\/tid\/registrering$/)

  // Navigate to the unique target month.
  await navigateSkemaToMonth(page, forwardClicks, monthLabel)

  // The "Ferie og fravær" disclosure band defaults open; the Sygedag row + its
  // per-day inputs are therefore present. Locate this day's SICK_DAY cell.
  const cell = page.getByRole('textbox', { name: cellLabel })
  await expect(cell).toBeVisible()

  // Listen for the save POST BEFORE the interaction (no race window). We match ANY
  // status (not just 200) so a non-2xx fails FAST with the real status rather than
  // hanging until the test timeout — and assert the 200 afterwards.
  const savePromise = page.waitForResponse(
    (resp) =>
      resp.url().includes('/api/skema/emp001/save') && resp.request().method() === 'POST',
  )

  // Register the absence DETERMINISTICALLY. Focusing an empty absence cell prefills
  // the served norm (onCellChange only — NOT the debounced save). To guarantee a real
  // change event (→ a scheduled debounced save, 1s) regardless of any pre-existing
  // value, we drive the controlled input via keyboard: focus, select-all + delete to
  // clear, then type the distinct half-day value character-by-character. Web-first
  // assertions between steps force React to settle (no controlled-input batching race).
  await cell.click()
  await cell.press('ControlOrMeta+a')
  await cell.press('Delete')
  await expect(cell).toHaveValue('')
  await cell.pressSequentially(HOURS_DISPLAY)
  await expect(cell).toHaveValue(HOURS_DISPLAY)
  // Blur to commit the edit; the debounce fires ~1s after the last change.
  await cell.blur()
  const saveResp = await savePromise
  expect(saveResp.status(), `skema save returned ${saveResp.status()}`).toBe(200)

  // PERSISTENCE: a full page reload re-fetches the month from the server. Because
  // SkemaPage initialises its month from "today", re-navigate to the target month
  // and assert the cell still carries the registered value (server-truth, not the
  // pre-reload in-memory state).
  await page.reload()
  await expect(page).toHaveURL(/\/tid\/registrering$/)
  await navigateSkemaToMonth(page, forwardClicks, monthLabel)

  const reloadedCell = page.getByRole('textbox', { name: cellLabel })
  await expect(reloadedCell).toBeVisible()
  await expect(reloadedCell).toHaveValue(HOURS_DISPLAY)
})
