// S142 / TASK-14201 — the frontend's single source of truth for "what day is it?"
//
// WHY THIS EXISTS (the plain-language version). Every date the user picks or is offered as a
// BUSINESS date — the day an agreement profile, an entitlement rule or a wage-type mapping takes
// effect — is a fact about Danish employment law. It is the calendar day in Copenhagen, not the
// calendar day where the person filling in the form happens to be sitting, and not the calendar
// day at Greenwich. That is owner ruling OQ-1 (2026-09-16): "the screen always asks what day it
// is in Copenhagen, whoever is using it and wherever they are."
//
// Before S142 the frontend had NO such helper. Screens computed today either from the browser's
// own zone (`new Date().getFullYear()/getMonth()/getDate()`) or from UTC
// (`new Date().toISOString().slice(0, 10)`). Both are wrong, and they are wrong in DIFFERENT
// directions, which is worse than either alone: the same admin could be offered one "today" by
// the date picker and have the server apply another. Browser-local is not a safe default and is
// explicitly NOT the fix — it is simply a third wrong calendar.
//
// THE BACKEND COUNTERPART is `CopenhagenBusinessDate.Today(TimeProvider)` in
// `src/SharedKernel/StatsTid.SharedKernel/Calendar/CopenhagenBusinessDate.cs`. The two must agree,
// which is the whole point: a picker that offers a day the server then refuses (or silently files
// under a different day) is the same off-by-one defect S142 exists to remove, relocated to the
// browser.
//
// INSTANTS ARE NOT BUSINESS DATES. `created_at`, `updated_at`, audit timestamps and token expiry
// are moments in time, are stored and ordered in UTC, and must NEVER be routed through this
// module. Converting an instant to a Copenhagen calendar day and back corrupts ordering; this
// helper answers "which Danish calendar day is it?", nothing else.
//
// NO DEGRADED MODE (mirrors backend owner ruling OQ-11). If a runtime cannot resolve the
// Europe/Copenhagen zone, `Intl.DateTimeFormat` throws a RangeError and this module lets it
// propagate rather than quietly falling back to the browser's zone or UTC. A fallback would look
// like resilience while silently reinstating the exact defect this sprint removes — a Danish user
// working at 00:30 having their change recorded as effective YESTERDAY, with no signal anywhere.
// A loud failure is recoverable; a silently wrong effective date is not. Every browser and Node
// runtime this product supports ships IANA timezone data, so this is a guard, not a live risk.

/** The IANA zone id every business date in StatsTid is measured against. */
export const COPENHAGEN_TIME_ZONE = 'Europe/Copenhagen'

// Built once: constructing an Intl.DateTimeFormat is comparatively expensive and this is called
// on render. 'en-CA' is irrelevant to the output below — we assemble from formatToParts by part
// TYPE, never by relying on a locale's field order or separators.
const copenhagenParts = new Intl.DateTimeFormat('en-CA', {
  timeZone: COPENHAGEN_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/**
 * The Copenhagen calendar date of `now`, as an ISO `yyyy-MM-dd` string — the same shape
 * `<input type="date">` and the backend's `DateOnly` both use.
 *
 * DST-correct by construction: the conversion goes through the real Europe/Copenhagen zone, which
 * is UTC+01:00 (CET) in winter and UTC+02:00 (CEST) from late March to late October. A hardcoded
 * offset gets the day wrong for half the year — that was the QUAL-005 bug on the backend side.
 *
 * @param now The instant to read. Defaults to the current wall clock. Pass an explicit `Date` to
 *   pin it deterministically (this is the test seam, mirroring the backend's injected
 *   `TimeProvider`).
 * @throws RangeError if the runtime cannot resolve Europe/Copenhagen. Deliberately not caught —
 *   see the "NO DEGRADED MODE" note at the top of this file.
 */
export function copenhagenToday(now: Date = new Date()): string {
  const parts = copenhagenParts.formatToParts(now)
  let year = ''
  let month = ''
  let day = ''
  for (const part of parts) {
    if (part.type === 'year') year = part.value
    else if (part.type === 'month') month = part.value
    else if (part.type === 'day') day = part.value
  }
  if (!year || !month || !day) {
    // Unreachable with the options above; fail loudly rather than emit a malformed date that
    // would be silently accepted by an <input type="date"> as an empty value.
    throw new RangeError(
      `Could not read a ${COPENHAGEN_TIME_ZONE} calendar date from this runtime's Intl data.`,
    )
  }
  return `${year}-${month}-${day}`
}
