// S143 / TASK-14310 — Proof 1: the Copenhagen timezone CAPABILITY test.
//
// PLAIN-LANGUAGE WHY. `../copenhagenDate.ts` (`copenhagenToday`) is the single place the frontend
// asks "what calendar day is it in Denmark?" — every business date in StatsTid (an agreement's
// effective date, an entitlement rule's start, which month a skema screen opens on) is answered
// through it. That module converts through the real IANA zone `Europe/Copenhagen`, and that
// conversion is only correct if the JavaScript runtime actually SHIPS timezone data for that zone.
// Some Node/V8 builds are compiled with no ICU data at all, or with the "small-icu" subset, and a
// runtime missing Europe/Copenhagen would make every business date in the product silently wrong —
// this test asserts the specific fact `copenhagenDate.ts` depends on, so that loss would be loud
// (a failing test) instead of silent. Concretely: Denmark observes CET (+01:00) in winter and CEST
// (+02:00) in summer (the DST swap `copenhagenDate.ts`'s own header calls out as the reason a fixed
// offset is wrong for half the year) — this test reads both offsets straight from the runtime and
// checks them against hand-written literals.
//
// WHAT THIS IS NOT: a "full ICU" test. Small-ICU builds are about LOCALE data coverage (number
// formats, calendar names, plural rules for languages other than en-US) and can independently still
// carry full IANA time-zone data — the two are separately configurable in a V8/Node build. A
// small-icu runtime can pass both assertions below; this test proves Europe/Copenhagen timezone
// resolution ALONE, nothing broader. An earlier draft of this sprint's own brief conflated the two
// and claimed this test proves "full ICU" — that was an overclaim, corrected in review.
//
// THREE TRAPS THIS TEST DELIBERATELY AVOIDS (each one caught someone in this sprint):
//
//   1) `Intl.DateTimeFormat`'s constructor takes `(locales, options)` — LOCALES FIRST. Passing
//      `{ timeZone: 'Europe/Copenhagen', timeZoneName: 'longOffset' }` as the FIRST argument makes
//      it parse as a (nonsensical) locales argument: every option, including the zone, is silently
//      ignored, and formatting falls back to the runtime's DEFAULT locale and zone. Such a test
//      would look like it asserts Copenhagen's offset while actually asserting whatever zone the
//      test runner's OS/container happens to default to — passing on a Danish machine and failing
//      (or worse, passing by accident) elsewhere, both for the wrong reason. An earlier draft of
//      THIS SPRINT's own specification had exactly this bug. Guarded below by always supplying a
//      locale first: `new Intl.DateTimeFormat('en-GB', { timeZone: ... })`.
//
//   2) `Date.prototype.getTimezoneOffset()` reports the offset of the BROWSER/OS's own local zone —
//      it cannot be pointed at an arbitrary IANA zone at all. A test built on it would pass on a
//      Danish machine and fail on UTC CI, both for the wrong reason (it never asks about Copenhagen
//      specifically). Not used anywhere below.
//
//   3) Named and scoped for exactly what it proves — "Copenhagen timezone capability" — never "full
//      ICU" (see above).
//
// RED PROOF. This test's failure mode is a MUTATED EXPECTED LITERAL — flip `'GMT+01:00'` to
// `'GMT+02:00'` (or vice versa) below and it fails immediately; that was done by hand against this
// exact file and reverted before this file was committed (see "REPORT" in the task's reply for the
// verification note). It is deliberately NOT proven by taking timezone data away from the runtime:
// every Node and browser runtime this product supports ships full IANA zone data, so building or
// sourcing a runtime WITHOUT Europe/Copenhagen data, purely to watch this test fail, is not a real
// risk this test needs to guard against, and not something a later reader should go looking for. If
// this test ever fails for real, the far more likely cause is the CI image's ICU configuration
// changing under it, not a bug in `copenhagenDate.ts`.
import { describe, it, expect } from 'vitest'

/** The Europe/Copenhagen UTC offset for `instant`, read via `Intl` with the zone pinned explicitly
 *  (locale FIRST — see trap 1 above) — e.g. `'GMT+01:00'`. */
function copenhagenOffset(instant: Date): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Europe/Copenhagen',
    timeZoneName: 'longOffset',
  }).formatToParts(instant)
  const offset = parts.find((part) => part.type === 'timeZoneName')?.value
  if (offset === undefined) {
    throw new Error(
      'Intl.DateTimeFormat did not report a timeZoneName part for Europe/Copenhagen — see the ' +
        'header comment above before assuming this means the zone is unavailable.',
    )
  }
  return offset
}

describe('Copenhagen timezone capability (the premise copenhagenDate.ts depends on)', () => {
  it('resolves Europe/Copenhagen to CET, +01:00, in January', () => {
    // 2026-01-15 noon UTC — deep in winter, nowhere near the spring DST transition.
    expect(copenhagenOffset(new Date('2026-01-15T12:00:00Z'))).toBe('GMT+01:00')
  })

  it('resolves Europe/Copenhagen to CEST, +02:00, in July', () => {
    // 2026-07-15 noon UTC — deep in summer, nowhere near the autumn DST transition.
    expect(copenhagenOffset(new Date('2026-07-15T12:00:00Z'))).toBe('GMT+02:00')
  })
})
