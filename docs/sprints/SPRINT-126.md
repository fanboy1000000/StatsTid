# SPRINT-126 — the S125 review debt + the performance tail (F2/F4/F5/F6)

**Status**: CLOSED 2026-08-04 — after Step 7a returned BLOCKERs, both were ABSORBED before closing.
Both lenses ran (`.claude/reviews/SPRINT-126-step7a-{codex,reviewer}.md`): Codex 2 BLOCKER / 5 WARNING
/ 2 NOTE; Reviewer 1 BLOCKER / 3 WARNING / 9 NOTE. **The close was withheld until both BLOCKERs were
fixed** — the S125 mistake was closing over an unrun lens, and the guard exists to prevent that.
Remaining WARNINGs/NOTEs are carried, listed under Open follow-ups.

**Theme**: drain the review debt S125 left, and finish the performance analysis it started.

## What this sprint was

S125 closed single-lens; its internal review was run retrospectively and returned 0 BLOCKER /
4 WARNING / 6 NOTE, all deferred here (FU-0). The rest of the F-series performance analysis (FU-2) was
also outstanding. The owner scoped both into one sprint and ruled that F4 — whose description had been
LOST — be re-derived by a fresh sweep rather than guessed at.

## Tasks

### TASK-12600 — Part A: the FU-0 review debt (9 findings, one commit)
Every finding re-verified against `adee08e` before being actioned, not taken from the artifact.

| # | Outcome |
|---|---|
| N1 | The tautological assertion **deleted**, not re-parameterised — `perPending20 < perPending10` is implied by the `count10 == count20` above it at every K. Flatness span widened K=10 → **K=200**. Measured: **9 commands at both**, 14 ms. |
| W2 | Cross-organisation ids added to the combined differential test — `perf_o1_xco` specifically on the CANDIDATE side, the only side that exercises the cross-org ACTOR asymmetry. 126 → **192 pairs**, 60 admitted, 0 divergences. |
| W3 | `ApprovalAuthorityContext`'s snapshot precondition made enforceable — **took THREE designs; see below**. Final: a single-use latch + the authorizer guard (rejects `tx: null`, requires RepeatableRead-or-stronger). Zero public-overload changes. |
| N2 | The lazy-route "gated" escape hatch turned into a hard failure — see the finding below. |
| N3 | **Six** stale comment sites corrected (the review said four). |
| N4 | `MemoHits` deleted; `BuildStatementCount` — equally dead — made **asserted** instead. |
| N5 | `tx` passed at `DesignatedApproverAuthorizer.cs:368` and `:505`. |
| N6a | Projection transaction now `READ ONLY`, batched onto phase (1) so the command-count guard survives at 1. |
| N6b | Reads (3)/(4) in `IAuthorityFactsSource` **key-bounded** by `@unitIds`/`@leaderIds`. |

**N6b is the interesting one — the review finding was rejected, then the rejection was itself wrong.**
The S125 review asked for the two global reads to be path-prefix scoped. The first refinement rejected
that on the grounds that scoping would fail OPEN. **Both review lenses independently refuted the
mechanism**: every read of those maps is a gate returning `None` on a miss, so dropping rows can only
move toward DENY. The fail-open case lived in `PrefetchedReportingLineDataSource` — a *resolver* that
picks a winner. The resolver lesson had been applied to a gate, inverting the project's own asymmetry
rule. The conclusion (don't path-scope) survived for the OPPOSITE reason: org-scoping risks locking out
a legitimate approver. The adopted fix — bounding by the employee-side keys the prefetch already holds
— is answer-identical **by construction** rather than by test, and came from the internal lens.

**N2 was not the finding it looked like.** Chasing the "assert a bound on gated routes" fix showed the
escape hatch was dead code resting on a false premise: `/global/overenskomster` and its `/new` sibling
sit under the SAME `RequireRole` element, so they cannot diverge for one actor; `ForbiddenPage` has
exactly one caller (`RequireRole`, a *route* guard returning it INSTEAD OF `<Outlet/>`), so a Forbidden
body proves the chunk was never fetched — the opposite of what the comment claimed; and admin01's role
clears every `minRole` in the app. It is now a hard failure with an explanatory message.

**W3 took three designs, and the first two both silently passed the case they existed to catch.**
(1) Bind the context to its `NpgsqlTransaction` by reference — defeated by Npgsql RECYCLING the
transaction instance per connection, so two sequential `BeginTransactionAsync` calls compare equal.
Caught by the test failing to throw. (2) Bind to the CONNECTION — catches a cross-connection hoist, but
two sequential snapshots on ONE connection are still indistinguishable; documented as a known gap and
**Step 7a correctly called that a BLOCKER**, since the guard's claim was broader than its reach.
(3) The fix: stop inferring the lifetime from an ADO.NET object. The rule is "one projection call", so
enforce it directly — a **single-use latch**, spent on `Dispose`, with the repository holding the
context in a `using`. Independent of pooling, no extra round-trip, and the latch sits on the MEMO
METHODS so the `ResolveEdgeAsync` path the repository calls directly (Step-7a Reviewer W2) is covered
too. The new test is the only one of the five that would have gone green against BOTH earlier designs,
and it was falsified: disabling the throw fails it.

**The transferable lesson**: two guards in a row were written against a *proxy* for the invariant
(which object are we on?) instead of the invariant itself (has this call ended?). Both compiled, both
passed, neither enforced anything.

### TASK-12601 — F5: the flex-balance full-stream replays
`employee-{id}` is the CONSOLIDATED stream (ADR-018 D6) and grows with every time registration. Four
sites answered "what is the latest flex balance?" four ways — three by replaying the entire stream in
memory, one by a hand-rolled inline `DISTINCT ON` with per-field JSON extraction. Now one rule, two
shapes (`ReadLatestOfTypeAsync` / `ReadLatestOfTypePerStreamAsync`), all four consumers repointed, both
deserializing through `EventSerializer` so the wire format stays owned by the serializer.

**The index fork was resolved with evidence, and the evidence changed the answer.** On a 1,100-event
stream whose one `FlexBalanceUpdated` sits at version 5, with the type made non-selective (18,001 rows
across 300 streams) as it is in production:

| | Rows removed by filter | Buffers |
|---|---|---|
| query rewrite alone | 1095 | 782 |
| + `idx_events_stream_type_version` | 0 | **4** |

"Bounded" was **not** earned by the rewrite alone. With a near-empty database the planner picked
`idx_events_event_type` and looked fine — a false all-clear that only realistic data exposed.

### TASK-12602 — F4: the lost finding, re-derived
F4's description existed nowhere in the repo — the F1–F6 analysis had lived only in the S125
conversation, and F4 was the one entry without an inline label. Owner ruled: re-derive by sweep.
Output → `docs/operations/performance-finding-register.md`, with the search method, an inventory of
every hit, and a disposition for each including the dismissed ones. Best candidate: the audit-log page
runs an exact `COUNT(*)` over the append-only `audit_projection` on **every request**, plus `OFFSET`
deep-paging — the only surviving request-path read whose cost grows with SYSTEM AGE. Confidence
recorded as moderate, not certain.

**The register is the real deliverable**: an analysis that lives in a conversation is one context
window from being unrecoverable.

### TASK-12603 — F2: reclassified, then fixed
The reported finding ("StrictMode double-fetch") is a non-defect — dev-only, and removing StrictMode
would be strictly negative. What it signalled: **zero** `AbortController` uses in `frontend/src`, and
only two hooks with any stale-response guard. The reachable defect is a stale WRITE when an effect's
inputs change mid-flight. Fixed at 13 hooks + 3 component sites on the existing in-repo
`latestRequestId` pattern. `AuditLogView` races despite `[]` deps because it fetches imperatively on
paging — and sets `page` from the response, so a stale landing would make the pager jump backwards.

### TASK-12604 — F6: ⛔ RETRACTED. The measurement was invalid.
A harness was written, numbers were produced, a 250 ms threshold and a "round-trip count dominates,
not chunk size" conclusion were derived from them — **and Step 7a found the harness measured neither
thing it claimed.** Two independent defects, both verified:

1. `page.goto()` performs a FULL DOCUMENT navigation (entry chunk, app boot, login state), not the
   client-side route transition F6 is about.
2. `<main>` permanently contains `mainInner` and the Suspense fallback (`AppLayout.tsx:15`), so the
   stop condition `not.toBeEmpty()` was **already true before the route mounted**. The recorded
   interval is "shell present", not "route content visible".

The second lens independently found the spec would also have **run in CI** — `playwright.config.ts`
has `testDir: 'e2e'` with no `testIgnore` and `npm run e2e` takes no args — against the dev server its
own docblock forbids, with 7 throttled routes at 60 s timeouts and `retries: 2`. The "excluded from the
normal run" claim in its header was implemented by nothing.

**Actioned**: the spec is DELETED (an invalid harness that also reds CI has no value), the conclusions
are RETRACTED in the register, and F6 is back to OPEN with the requirements for a valid harness
recorded: drive client-side navigation, and stop on a per-route testid rendered by the page component.
The chunk-size table survives — it comes from build output, not this harness.

**Kept**: the `preview.proxy` block in `vite.config.ts`. `server.proxy` genuinely does not apply to
preview, so measuring the production build against the real API was impossible without it, and a valid
harness will still need it.

**This is the sprint's own theme, committed by the sprint, into the register that documents it** — a
measurement that looks like evidence and is not. It was reported to the owner as a finding before Step
7a caught it.

## The recurring lesson, fifth sprint running

**Running the code caught five errors that reading it did not — three of them in this sprint's own
fixes, and two predicted by both review lenses:**
1. `NextResultAsync` (added because both lenses reasoned the batched `SET` would surface as a
   zero-column resultset) broke all four tests — Npgsql does not expose a non-row-returning statement
   as traversable.
2. The W3 guard bound to `NpgsqlTransaction` by reference **silently passed the exact case it guarded**
   — Npgsql recycles the instance per connection. Rebound to the connection, residual limit documented.
3. F5's index, above.
4. The F2 census claimed "1 of 29 hooks uses AbortController" — the grep had matched the English word
   *"signal:"* in a prose comment. Real count: zero. Two more hooks (`useAdmin`, `useSkema`) looked
   guarded because the grep matched *"ignored"*.
5. The F6 chunk-size table implied the wrong remedy.

**Falsification is now part of the work, not a nicety.** The F2 guard test was verified by REMOVING the
guard: 3 of its 4 tests fail without it. The 4th passes either way (React 18 made setState-after-unmount
a silent no-op) and is labelled in-file as a crash guard that does NOT exercise the stale-response
guard — rather than being left to read as evidence.

## Verification

- S106 perf/differential class **8/8**; W3 guard tests **3/3**; F5 reader tests **4/4**
- Consumer regression (Balance/Time/Approval/Skema) **594/594**
- Approval/Performance/ReportingLine/Security: 402 passed + **43 environmental**, isolation-cleared
  **59/59** (42 were the documented FAIL-002 fixed-port class; 1 a testcontainer start race)
- Frontend **715/715** (711 + 4 new), tsc + lint clean
- `tools/check_docs.py` all hard checks pass; `db-schema.md` regenerated for the new index

⚠ **A FULL regression was NOT run before this close.** Affected areas were covered; the complete suite
is CI's to verify. Recorded rather than implied.

## Open follow-ups

**FU-A — the submit-time allocation gate (owner-raised, refinement BLOCKED at rev 1).**
An employee can send a month to approval with hours unallocated, and — worse than first reported —
**an unallocated month can reach `APPROVED`**: manager approve accepts `SUBMITTED` and Teamoversigt's
Godkend button is never disabled by `hasWarning`, including bulk. Both lenses rejected the proposed
fix: `/submit` is the wrong choke point (ADR-012 defines `DRAFT → EMPLOYEE_APPROVED → APPROVED` and
explicitly rejected the three-step flow the app implements), and a repository-level gate is also wrong
(`CreateAsync` takes any caller-supplied status). Rev 2 must start from *should `/submit` produce a
manager-visible state at all?* Owner rulings that survive: gate unconditionally; all organisations
should have projects (11 of 13 currently have none — a **precondition**, not cleanup).
Evidence + both lens reports: `.claude/refinements/REFINEMENT-submit-allocation-gate.md`.

**FU-B — a pre-existing illegal transition**, found incidentally: `/submit`'s guard omits
`EMPLOYEE_APPROVED` and updates unconditionally after an out-of-transaction read, so it can downgrade
`EMPLOYEE_APPROVED → SUBMITTED`, including under a race.

**FU-C — carried, untouched**: W1/FU-1 (the self-approval CLASS + the HR/GlobalAdmin
`ORG_SCOPE_FALLBACK` ruling), W4 (the F3 lazy-route error boundary — a live regression producing a
permanent blank page, and higher-stakes than anything in this sprint), RES-002, the reopen fork,
DemoSeed time registrations, ROADMAP/QUALITY freshness debt (anchored S111).
