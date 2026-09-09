# PAT-029 — Widening a clock seam is a defect sweep, not a find-and-replace

| Field | Value |
|-------|-------|
| **ID** | PAT-029 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S140 |
| **Domains** | Backend, Infrastructure, Test |
| **Tags** | timeprovider, business-date, clock-seam, census, exemption-comment, audit-consistency, pat-008, pat-028, qual-153, qual-154, qual-155, qual-156, qual-164 |

## Context

Two consecutive sprints converted raw clock reads to an injected `TimeProvider` so that date-dependent tests could
pin "today": S139 for eight regression suites (QUAL-153), S140 for the remaining six (QUAL-154). **Both sprints
found latent product defects while converting** — S139 three, S140 five — and in both the defects were invisible
until someone counted clock reads per *operation* rather than per *file*. S140 then found a business date that had
survived S139's conversion of the very same handler, one screen below a comment that had converted its sibling.

The pattern is therefore not "replace the clock reads". It is: **the act of widening a seam is the cheapest defect
sweep available for date correctness, and the file-level view is the wrong unit of analysis.**

## The pattern

**1. Enumerate by OPERATION, not by file.** Before converting, trace every business-date read reachable from the
operation — including reads hidden inside a callee's `?? DateTime.UtcNow` fallback and inside SQL
(`CURRENT_DATE` / `NOW()::date`). A callee fallback is a clock read the caller cannot see, and it does not appear in
the caller's own grep. Then: one read at the operation's entry point, threaded down (PAT-028); the callee's fallback
survives only for callers that genuinely own no date, and those call sites say so in a comment.

**2. The completeness check that actually holds.** *"No raw clock read remains in this file"* is strictly weaker than
*"this operation reads the clock once"*, and weaker again than *"every date derivation in this file reads the seam"*.
A file can pass the first and still hold the defect, because the second read lives in a repository the handler calls.
The check that finds it: for each write path, trace every DATE column and every event date field back to its variable;
they must trace to **one** variable per operation.

**3. Mind the regex blind spot.** The standard census pattern
(`DateTime\.(Today|UtcNow|Now)\b|DateOnly\.FromDateTime\(DateTime\.`) **cannot see** `DateOnly.FromDateTime(<local>)`
— the clock read is one assignment away. That single blind spot hid a business date through two conversion sprints.
Always sweep the second form too:

```
rg -n -P 'DateOnly\.FromDateTime\((?!DateTime\.)' src --glob '*.cs'
```

Most hits will be legitimate (database hydration from `reader.GetDateTime(...)`, domain data). The interesting ones
are dates derived from a *local variable that came from a clock*. In S140 this sweep found QUAL-164 immediately.

**4. An exemption comment is a seam's blind spot — phrase it as a property of the USE, not of the VALUE.** The S140
survivor was `DateOnly.FromDateTime(now)` where `now` was an audit timestamp a comment had explicitly exempted
("the audit `now` stamp keeps `DateTime.UtcNow` by design"). The exemption was *correct for the timestamp* and was
silently inherited by a business date derived from it. So:

- ❌ `// BY DESIGN: now stays on the real clock` — reads as blanket permission for anything derived from `now`.
- ✅ `// BY DESIGN: now is the @now bind for updated_at and is used for NOTHING else. It is not a business date, and
  nothing may derive one from it.` — states the scope and bans the derivation.

**5. Never route these through the seam**, and say so at each site: audit and creation timestamps (`CreatedAt`,
`created_at`, `occurred_at`, `updated_at`), token minting (a fixed provider under minting mints tokens "in the past"
against a real-clock validator), and `expires_at` authorization compares that must stay self-consistent with
SQL-seeded expiries in tests.

## The comment corollary — three sprints running, the false claim sat next to the bug

- S139: the profile soft-delete's "they cannot disagree" comment sat above two independent clock reads.
- S139: the admin create POST's "computed ONCE so the three can never disagree" sat above three reads.
- S140: `/approve`'s "Compute asOf at action-time" read as deliberate and hid a third, callee-side read.
- S140: an exemption written for a timestamp was inherited by a date derived from it.

**A comment asserting an invariant is where this defect class hides, because it reads as a completed decision.**
When converting, treat every such comment as a claim to verify. Rewrite it to state a **checkable** invariant ("one
value is passed to both") or delete it. And when a conversion makes an existing comment false — including one you
wrote earlier in the same sprint — correcting it is part of the change, not a tidy-up.

## Consequences

- Budget a seam widening as a *sweep*, not a mechanical edit: S140's twelve-site conversion produced five defect
  fixes, one of them an auditability defect (a database row and the event meant to reconstruct it took their dates
  from different clocks).
- New constructor and method parameters should be **optional** (`TimeProvider? timeProvider = null`, `DateOnly?
  closeDate = null`), so existing direct constructions in tests keep compiling and the conversion stays additive.
  If a test file must change to make the product compile, the change was not additive.
- Prefer an added read-only entry point over moving code: `+N/−0` on a shared file is provable behaviour
  preservation, and the existing suites remain the pin.

## Related

PAT-008 (the fixed `TimeProvider` WAF test pattern — the reason the seam exists) · PAT-028 (compute today once —
the rule this pattern enforces at scale) · QUAL-153 / QUAL-154 (the two conversion tranches) · QUAL-155 (the raw
business-date census, whose reproduce step now carries the widened regex) · QUAL-156 (the SQL clock sites) ·
QUAL-157 (the UTC-versus-Copenhagen day split, deliberately unresolved) · QUAL-164 (found by the widened sweep).
