/**
 * SPRINT-82 task 8202 (R4 + the re-run-tolerance nonce) — the shared date/nonce
 * helper for the mutating journeys (Skema registration + approval).
 *
 * The running stack uses the REAL clock (TimeProvider.System; approval "today" =
 * DateTime.UtcNow) — there is NO FixedTimeProvider here (unlike the vitest tier).
 * Date-fragile flows flaked S77/S78, so the journeys compute every target at
 * RUNTIME in UTC and deliberately AVOID boundary days (month-end, year-end,
 * weekends). Nothing is hardcoded.
 *
 * Re-run tolerance: each run derives a per-run nonce from Date.now(); the nonce
 * selects a UNIQUE future month within a bounded forward window. Two consecutive
 * runs therefore target DIFFERENT months, so a re-run can never:
 *   • 409 on the POST /api/approval/submit existing-SUBMITTED guard
 *     (ApprovalEndpoints.cs:109 — the guard keys on employeeId+periodStart+periodEnd),
 *   • go green-but-weak against state a prior run left in the persistent local
 *     Postgres volume.
 *
 * The bounded window keeps the ApprovalDashboard month-nav (one "Naeste" click per
 * month, internal state, no URL param) to a deterministic, small click count.
 */

/** How far ahead the nonce window reaches, in months. A run picks one slot in
 *  [1, MONTH_WINDOW]; MONTH_WINDOW caps the dashboard "Naeste" click count. */
const MONTH_WINDOW = 18

export interface TargetMonth {
  /** Four-digit calendar year (UTC). */
  year: number
  /** 1-based calendar month (UTC). */
  month: number
  /** Whole months FORWARD from the current UTC month — the exact number of
   *  "Naeste" clicks needed to drive the ApprovalDashboard month-nav from "now"
   *  (which initialises to the current UTC month) onto this target. */
  forwardClicks: number
}

/** A per-run nonce derived from the wall clock. Distinct on every run (down to
 *  the second), so re-runs rotate onto a fresh month. */
export function runNonce(): number {
  return Math.floor(Date.now() / 1000)
}

/**
 * Resolve the journey's UNIQUE target month: a month `slot` (1..MONTH_WINDOW)
 * forward of the current UTC month, where `slot` is the per-run nonce rotated by
 * an optional `offset`. Pure function of (nonce, offset) — deterministic within a
 * single call.
 *
 * NOTE on the `offset` param (corrects an earlier over-claim): the two journeys
 * (skema-registration + approval) call `runNonce()` INDEPENDENTLY — each derives
 * its own per-spec wall-clock nonce — so passing distinct offsets does NOT
 * GUARANTEE distinct months (the two nonces can differ by any amount; `(nonceA+0)`
 * and `(nonceB+7)` may land on the same slot mod MONTH_WINDOW). Cross-spec
 * same-month is nonetheless benign — see the offset constants in each spec for the
 * real reason. The `offset` arg is only reliable WITHIN one spec, where the SAME
 * nonce is reused to carve out a second distinct slot.
 */
export function targetMonth(nonce: number, offset = 0): TargetMonth {
  const now = new Date()
  const baseYear = now.getUTCFullYear()
  const baseMonth0 = now.getUTCMonth() // 0-based

  // slot ∈ [1, MONTH_WINDOW] — never 0 (the current month may hold real data
  // or be a partial month near its own boundary).
  const slot = ((nonce + offset) % MONTH_WINDOW) + 1

  const totalMonths0 = baseMonth0 + slot
  const year = baseYear + Math.floor(totalMonths0 / 12)
  const month = (totalMonths0 % 12) + 1 // back to 1-based
  return { year, month, forwardClicks: slot }
}

/**
 * Pick a NON-boundary weekday inside the given month: a mid-month day that is
 * Monday–Friday, computed in UTC and never near the month edges. `which`
 * selects among the mid-month weekday candidates so two distinct, isolated
 * periods can live in the SAME month on DIFFERENT days (different days ⇒
 * different periods ⇒ no submit-409). Returns an ISO `YYYY-MM-DD` string.
 */
export function nonBoundaryWeekday(year: number, month: number, which = 0): string {
  const candidates = midMonthWeekdays(year, month)
  // The 13-day band always contains ≥ 9 weekdays, so candidates is never empty.
  const day = candidates[((which % candidates.length) + candidates.length) % candidates.length]
  return isoDate(year, month, day)
}

/** The mid-month [10..22] weekday (Mon–Fri, UTC) candidates for a month. The band
 *  is well clear of the month-start/-end edges (R4) and always yields ≥ 9 days. */
function midMonthWeekdays(year: number, month: number): number[] {
  const candidates: number[] = []
  for (let day = 10; day <= 22; day++) {
    const dow = new Date(Date.UTC(year, month - 1, day)).getUTCDay() // 0=Sun..6=Sat
    if (dow !== 0 && dow !== 6) candidates.push(day)
  }
  return candidates
}

/**
 * A per-run, nonce-rotated weekday INDEX into a month's mid-month weekday band.
 * Combining this with the nonce's month rotation spreads each run across the full
 * (month × day) slot space (≈ 18 × 9 ≈ 160 slots), so consecutive re-runs land on
 * DIFFERENT days even when the bounded month window recycles — a re-run can never
 * collide with an APPROVED/REJECTED period a PRIOR run left on a recurring day
 * (the /submit guard refuses re-submit over SUBMITTED/APPROVED — ApprovalEndpoints.cs:109).
 * `offset` lets one run carve out a second, distinct day in the same month.
 */
export function nonceWeekdayIndex(nonce: number, year: number, month: number, offset = 0): number {
  const count = midMonthWeekdays(year, month).length
  return ((nonce + offset) % count + count) % count
}

/**
 * The full set of mid-month non-boundary weekdays for a month as ISO date strings,
 * ROTATED to start at `startIndex` (nonce-derived). A resilient submit walks this
 * ordered list, trying each day until one yields a clean SUBMITTED period — so an
 * accumulated APPROVED/REJECTED period on a recurring slot (local persistent volume)
 * is simply stepped over. CI runs against a fresh ephemeral volume, so the first
 * candidate always wins there.
 */
export function rotatedWeekdays(year: number, month: number, startIndex: number): string[] {
  const candidates = midMonthWeekdays(year, month)
  const n = candidates.length
  const start = ((startIndex % n) + n) % n
  const out: string[] = []
  for (let i = 0; i < n; i++) {
    out.push(isoDate(year, month, candidates[(start + i) % n]))
  }
  return out
}

/** Format a (year, 1-based month, day) tuple as a zero-padded ISO date string. */
export function isoDate(year: number, month: number, day: number): string {
  const mm = String(month).padStart(2, '0')
  const dd = String(day).padStart(2, '0')
  return `${year}-${mm}-${dd}`
}
