# ADR-042 — The client's "today" comes from the server, once, at app start

| Field | Value |
|-------|-------|
| **Status** | accepted — owner-ruled 2026-09-23 (OQ-1a/1b, OQ-1d), implemented S143 |
| **Sprint** | S143 |
| **Extends** | [ADR-041](ADR-041-copenhagen-business-day.md) — which decided *which calendar*, and explicitly left *whose clock* to a follow-up |
| **Domains** | Frontend, Backend, Test |
| **Tags** | business-date, timezone, copenhagen, bootstrap, app-shell, clock, authority, fail-closed |

## The decision, in one sentence

**The browser never decides what day it is: the client reads the Copenhagen business day from the server once at app
start, shares it, and the app shell refuses to render until it has one.**

## What ADR-041 left open, and why it was not finished

ADR-041 moved every business date to the Copenhagen calendar day and recorded a follow-up in its own
*"What this ADR does not decide"*: display and navigation dates — which month a calendar view opens on — were still
**browser-local**, described there as *a different wrong calendar, not a lesser instance of this one*.

That framing understated it, and S143's refinement found out why by tracing what the value feeds.

**The month a screen opens on is not a display default.** `SkemaPage`'s seeded `year`/`month` is passed to `useSkema`,
which sends it as the period envelope of `POST /api/skema/{employeeId}/save` **and** of `POST /api/approval/send` — the
second of which *creates the approval period*. Every cell the user edits carries a date derived from that same month.

**So if the page opens on the wrong month, a person's hours are registered into that month and that month is submitted
for approval.** The server is told which month to use; it does not infer one, so nothing downstream corrects it.

## The decision, and the two alternatives rejected

Three shapes were on the table. All three fix the *zone*; they differ on *whose clock* and on *how the answer arrives*.

**1. The device's clock, in the Copenhagen zone** (`copenhagenToday()` at each site). Smallest change, no API work, no
round trip. Fixes the calendar for everyone and leaves one assumption: that the device's clock is set correctly. With the
filing consequence above, that assumption is now load-bearing rather than cosmetic.

**2. Endpoints answer "the current period" when none is given.** Marketed as deleting the question at the source.
**Rejected on evidence, not preference — the rival design was already deployed and had not worked.** `ArsoversigtPage`
has classified every month as past, current or future from a server-supplied `today` since S65, and it *still* read the
browser clock for its initial year, because **you must ask for a year before the server will tell you what year it is.**
Endpoint defaulting cannot escape that bootstrap; it also leaves the grid's today-highlight without a source, and its
promise ("the client never forms an opinion") holds only at initialisation — the client owns month state for navigation
regardless, so defaulting adds an empty window to state it keeps anyway.

**3. A bootstrap read — chosen.** One typed endpoint (`GET /api/calendar/today`) answering the Copenhagen business day
plus `secondsUntilNextMidnight`, read once at app start, shared by every screen through a context.

**Stated honestly: neither 2 nor 3 makes the server authoritative over what the client *sends*.** Both only supply a
correct default; a client can still post any month. The bootstrap's one real cost over defaulting is a startup
dependency, accepted because the API serving the day is the API the app cannot function without.

## Why a duration and not a timestamp

The response carries `secondsUntilNextMidnight`, not `nextMidnightUtc`. An absolute instant would force the client to
call `Date.now()` to turn it into a delay — reintroducing the device clock read through the one door being closed.

**A duration ages in transit**, which a threshold alone does not solve: a response reporting exactly five seconds that
arrives six seconds later is accepted as yesterday. So the client measures elapsed transit with a **monotonic** clock
and re-reads when the value arrives already spent.

The server computes it DST-safely: tomorrow's unspecified-kind local midnight, converted through the zone to UTC, minus
**the same captured instant** used to derive today. Adding 24 hours is wrong on the 23- and 25-hour days;
`CopenhagenBusinessDate.FromInstant(DateTimeOffset)` exists so one clock read serves both values (PAT-028).

## Failure is handled oppositely at startup and mid-session, on purpose

**At startup the shell is gated (OQ-1d).** If the read fails, no page renders — ADR-041's OQ-11 shape ("refuse rather
than guess") carried into the browser. The accepted cost: a transient failure at load makes the product unusable even
for reading. The alternative re-admits the device clock at exactly the moment it matters.

**Mid-session a refresh failure changes nothing visible.** The last confirmed day keeps serving and a retry runs, because
there may be unsaved work on screen and discarding it to protect against a day at most hours stale is a bad trade.

**A 401 is neither.** Retrying an expired token cannot succeed, so it ends the session and routes to login. This required
an opt-out on the shared HTTP client: its global handler reloads the page on any 401, which from a background timer
destroys exactly the unsaved work the asymmetry exists to protect. **A caller passing that opt-out owns the 401.**

That asymmetry reads as an inconsistency to anyone comparing the two paths, which is why it is argued in the code and
recorded here rather than merely implemented.

## Consequences

- **The gate sits inside `RequireAuth`, after the auth check** — never in `main.tsx`. Gating the app root would block
  `/login`, and with no token there is no calendar read, no render, and no way to log in. Review measured that class of
  loop at **26 reads and 25 reloads in under five seconds** before it was fixed; it now terminates *by construction*
  rather than by winning a race.
- **No frontend production source reads the browser clock for a business date.** A vitest AST guard enforces it, with
  `lib/copenhagenDate.ts` the single exemption — in the scanner's own code, not in a data file.
- **Testing**: a frozen clock cannot prove a single-read property, and forcing a non-Danish zone is mandatory or the test
  is vacuous on a Danish machine. Both lessons are ADR-041's testing consequence extended; see `SPRINT-143.md`.

## What this ADR does not decide

- **Whether the server should reject a month the client had no business sending.** Neither this design nor its rivals
  constrain what a client *posts* — only what it defaults to. If that matters later, it is a server-side validation
  question, not a calendar one.
- **Whether a user outside Denmark should ever see their own calendar.** Unchanged from ADR-041: the premise is that
  every user is Danish, and if that changes, ADR-041 is still the thing to revisit first.
