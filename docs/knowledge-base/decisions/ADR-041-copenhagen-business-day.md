# ADR-041 — Business dates are the Copenhagen calendar day

| Field | Value |
|-------|-------|
| **Status** | accepted — owner-ruled 2026-09-14, implemented S142 |
| **Sprint** | S142 |
| **Supersedes** | the UTC-day convention on the profile/agreement paths (S139 owner ruling OQ-3 (a)); closes QUAL-156, QUAL-157, QUAL-172 |
| **Domains** | Backend, Infrastructure, Rule Engine, Payroll Integration, SharedKernel, Frontend, Test |
| **Tags** | business-date, timezone, copenhagen, dst, effective-dating, utc, clock |

## The decision, in one sentence

**Every business date in StatsTid is the calendar day in Europe/Copenhagen; every instant stays UTC.**

## What was wrong

The product derived "today" from the **UTC calendar day**. All users are Danish, and Denmark runs UTC+01:00 in winter (CET)
and UTC+02:00 in summer (CEST). So for the one-to-two hours between Danish midnight and UTC midnight, the product believed
it was still **yesterday** — and business dates are not cosmetic. They are the `effective_from` a change is recorded at, the
`effective_to` a row is retired at, and the predicate deciding whether a dated row is history, in force, or still to come.

**An HR user working at 00:30 recorded a change as effective the day before.** Every night. On every surface.

## Why it happened — the part worth remembering

**The UTC day was never chosen.** The frontend sent `new Date().toISOString().slice(0,10)` because that is JavaScript's
easy path to a date string. The backend's same-day validators were then *deliberately* made UTC **to agree with that call**.
Every writer, both `users.*` caches, the login-token mint and later as-of-today reads followed the validators. Documentation
then recorded the convention as intentional, and a runbook instructed operators **not to "fix" it** by moving to Copenhagen.

*An accident at the edge propagated inward until it looked like a design, and was then written down as one.* By the time it
was questioned, four documents taught it as a rule and two code comments warned against correcting it.

## The rule

**Business dates MOVE to the Copenhagen day.** `effective_from` / `effective_to`, any validator asking "is this in the
past/future?", any stored effective-date stamp, any date offered to a user as a default. These are calendar facts about
Danish employment.

**Instants STAY UTC.** `created_at`, `updated_at`, audit timestamps, outbox ordering, JWT expiry. These are moments in time.
**Converting an instant to a calendar day and back corrupts ordering**, which would compromise the auditability invariant —
so mistaking an instant for a date is the single most damaging way to apply this ADR wrongly.

## Consequences

- **One helper, both sides.** `CopenhagenBusinessDate.Today(TimeProvider)` on the server;
  `copenhagenToday()` (`frontend/src/lib/copenhagenDate.ts`) in the browser. **Browser-local is explicitly NOT the answer —
  it is a third wrong calendar**, correct only when the user happens to sit in Denmark.
- **DST is resolved through the real zone, never a hardcoded offset.** A hardcoded `+01:00` is wrong for half the year; that
  is QUAL-005, the original defect the helper was written to prevent.
- **No degraded mode (OQ-11).** A host that cannot resolve Europe/Copenhagen **refuses to start**, with a message naming the
  zone and the fix. A silent fallback to UTC would look like resilience while reinstating this exact defect. The startup
  probe checks **both** seasonal offsets, because a winter-only check is passed by a hardcoded `+01:00` zone.
- **In the browser, the same rule is presented differently (OQ-12).** The helper still throws, but a UI boundary catches it
  and disables the affected control with an explanation rather than blanking the page. Same correctness, contained blast
  radius — a server either boots or does not, a screen has more options.
- **Dates the database decided move into the application (OQ-7).** A `CURRENT_DATE` in SQL is evaluated in whatever zone the
  Postgres container runs, which nobody sets, sees or tests. `src/**/*.cs` now contains **zero** executable `CURRENT_DATE` /
  `NOW()::date` business-date derivations.
- **Dead paths carrying the defect shape are deleted, not migrated (OQ-4).**
- **There is no residual to migrate (OQ-2)** — nothing is deployed and there is no data, so no backfill and no dual-read
  window. *A deployed system would have needed both, and this ADR would have been far more expensive.*

## Testing consequence — read this before writing a date test

**`WithFixedToday(DateOnly)` pins UTC midnight, the one instant where both calendars always agree.** A test built on it
passes under a correct *and* a broken implementation. It remains correct for tests whose subject is not the calendar.

A test whose subject **is** the business day must pin an exact instant where the calendars disagree, via
`WithFixedInstant(DateTimeOffset)` and the shared anchors in `BoundaryInstants` — and must assert **literal** expected
values. *A test that computes its expectation by calling the helper under test proves only that the helper agrees with
itself*; S142 found three such tests and deleted the shape.

**On a developer machine in Denmark, a frontend date test is vacuous unless it forces a non-Danish zone**, because the
browser-local day and the Danish day are the same day there. Force the zone, and assert that the forcing worked.

## What this ADR does not decide

- **Display and navigation dates** — which month a calendar view opens on — were filed to a follow-up sprint.
  **DONE in S143; see [ADR-042](ADR-042-client-today-from-the-server.md).** This entry described them as browser-local,
  *a different wrong calendar rather than a lesser instance of this one*, which was right about the calendar and wrong
  about the stakes: they are not display dates at all. The month a screen opens on is sent as the period envelope of the
  skema save and the approval send, so it decides **which month a person's hours are filed under**. ADR-042 settles it —
  the client reads the day from the server once at app start and the shell refuses to render without it.
- **Whether a user outside Denmark should ever see their own calendar.** The premise here is that every user is Danish. If
  that changes, this ADR is the thing to revisit first.
