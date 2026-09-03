# PAT-025 — Temporal write router over a lock-held whole-timeline snapshot, with one concurrency token per aggregate

| Field | Value |
|-------|-------|
| **ID** | PAT-025 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S138 (TASK-13801 — ADR-040 D8 as amended) |
| **Domains** | Infrastructure, Data Model, Backend |
| **Tags** | bitemporal, end-exclusive, backdating, dated-history, for-update, etag, if-match, users.version, adr-020, adr-040 |
| **Origin** | `src/Infrastructure/StatsTid.Infrastructure/Temporal/TemporalWriteRouter.cs`; `EmployeeProfileRepository.SupersedeAndCreateAsync`; `UserAgreementCodeRepository.SupersedeAndCreateAsync` |

## The problem (plain language)

Dated history ("this part-time fraction applied from the 10th until the 31st") used to be extendable only
at the END: the writer looked at the open row and either edited it (same day) or closed it and opened a new
one (a later day). Recording that something actually changed on a PAST date — inserting a row between two
existing rows, or splitting one — was impossible, and each new "case" written by hand at the SQL layer is a
fresh chance for an off-by-one day at the payroll boundary. Separately, a client that edits HISTORY holds no
token for the history row (the GET only ever issued the open row's version), so "what does If-Match mean for
a backdate?" had no answer.

## The pattern — three parts

1. **Lock the whole timeline once, in one order.** `SELECT … FOR UPDATE ORDER BY (effective_to IS NULL)
   DESC, effective_from` over the aggregate's rows. Every writer therefore takes the OPEN row's lock first
   (the pre-existing lock point, so existing callers' lock order is unchanged), then the history rows; two
   backdates into different history rows cannot interleave into an overlap, and writer deadlocks are ruled
   out by construction. The unique indexes (live partial-unique; history `(key, effective_from)`) stay as the
   backstop for the zero-row race — a 23505 is re-thrown as the conflict exception.
2. **Decide the case in a PURE router, execute exactly what it returns.** `TemporalWriteRouter.Decide(rows,
   from, today)` maps the lock-held snapshot to one of: **A** create (no rows) · **B'** update-in-place (a row
   starts at `from` — history or open; a zero-width same-day create+delete row is re-extended) · **C'** split
   the covering row (close it at `from`, insert `[from, covering.oldTo)` — open if the covering row was open)
   · **E** insert before the first row · **G** insert into a gap · **T** insert after the last row (the state a
   soft-delete leaves; becomes the open row) · refuse a FUTURE date (date-free exception). It returns the
   exact interval the new row must occupy. Being pure, the entire matrix is unit-tested without a database
   (`TemporalWriteRouterTests`: every case × live/history/gap/empty + per-day SWEEP tests asserting no
   overlaps, exactly one open row, the requested day covered). `TimelineSnapshot.Build` fails LOUD on an
   overlapping or duplicate-start timeline instead of routing a corrupt one.
3. **One concurrency token per aggregate, bumped on every timeline write.** The client's token is whatever
   the GET already issues: for the profile timeline the OPEN row's `version` (bumped even when only a history
   row changed, so the ETag is a monotonic "timeline version"; history rows' own version is never issued to
   a client); for the agreement-code timeline `users.version` (the users PUT already validates it), bumped
   by the repository atomically with the row write. Two writes against the same token serialize and the
   second 412s; "both succeed" only as sequential retries with refreshed ETags. The repository also refreshes
   the aggregate's live CACHES (`users.agreement_code`, `users.employment_category`) FROM THE ROW COVERING
   TODAY — never from the request — so a historical-only correction leaves them untouched by construction.

## What was given up, and why

- **Future-dating** — a future row cannot refresh the live caches on its effective date without a scheduler,
  and every open-ended-row reader (login token, profile GET, delete pre-read) would treat it as "current".
  Deferred to the increment that reworks those readers (ADR-040 §Amendment 2026-09-02). The router refuses
  futures so the invariant holds even if an endpoint validator is loosened by mistake.
- **A resurrecting PUT** — case T is a repository case for re-create callers (seeder, admin POST); the profile
  edit PUT keeps its "no open row → 404" so an edit can never quietly re-open a deliberately retired profile.

## Agent Guidance

- A new dated-history repository reuses `TemporalWriteRouter` and the sweep-test shape; a hand-rolled
  `if (predecessor.effective_from < req.from)` case ladder is a review finding.
- Endpoints take the post-write token from the result (`Version` for the profile aggregate;
  `UsersVersionAfter` for the agreement aggregate) and never compute `locked + 1` themselves.
- The "before" picture for audit rows, change detection and event predecessor fields comes from the writer's
  COVERING-row pre-image in the result — comparing against the live row makes a backdate that equals today's
  values a silent no-op.

## The audit debt that rides with the token (S138 Step-5a, Reviewer WARNING)

Giving the repository ownership of a denormalised cache **and** of the aggregate's version bump moves a
`users`-row write *into* the repository while leaving the paired `users_audit` row *outside* it, discoverable
only by reading `UsersCacheWritten` on the result. In S138 three callers honoured that and a fourth — the
user-create POST — did not, which broke the version chain (1 → 2 with nothing recording why) and handed the
caller a stale token. Nothing in the type system made the omission visible.

**So the obligation is part of the pattern, not an implementation detail:**
- every code path that reaches the writer MUST write the paired audit row when `UsersCacheWritten` is true;
- the response token comes from the party that performed the bump (`UsersVersionAfter`), never recomputed as
  `expected + 1`;
- prefer a test that asserts the audit CHAIN is continuous (no version transition without a row explaining
  it) over one that asserts a literal version number — the literal goes stale the moment another
  same-transaction write lands, and it hides exactly this defect.

The stronger form, if a third consumer ever appears, is to move the audit insert into the repository and have
the request record carry the actor (the worklist's `WorklistTrigger` already threads `ActorId` this way).
