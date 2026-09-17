# Sprint 142 — business dates move to the Danish day

| Field | Value |
|-------|-------|
| **Sprint** | 142 |
| **Status** | **COMPLETE** — 13 tasks, all DONE; Step 7a dual-lens absorbed; CI green |
| **Start Date** | 2026-09-16 |
| **End Date** | 2026-09-17 |
| **Orchestrator Approved** | **APPROVED** — Step 0b ran **two cycles, both lenses**. Cycle 1: Reviewer CHANGES-REQUIRED (nine of 64 rows unassigned; the OQ-4 deletes unassigned; **the sprint-wide carve-out itself wrong**; four coupled pairs split; predecessors wrong both ways) · Codex 2 BLOCKER (test ownership unprovable; validator↔picker "ordered" ≠ atomic). Cycle 2: Reviewer verified 7 of 9 fixes APPLIED, 2 PARTIAL — **B1: the coverage enumeration still contradicted the picker fix it was meant to certify** — plus W1–W6 absorbed (all-or-nothing MOVE tasks; helper ownership; rows 48/49 are dead code → deleted under OQ-4; two files need the `TimeProvider` seam; a per-task *failing* pin; `docs/` split out of an agent task per CLAUDE.md). Refinement rev 9 + OQ-10, OQ-11. **12 tasks, 64/64 rows assigned exactly once** |
| **Build Verified** | ✅ `dotnet build StatsTid.sln --no-incremental` → **0 errors, 145 warnings** — the S141 baseline, unmoved across all 13 tasks · `npx tsc --noEmit` clean |
| **Test Verified** | ✅ Unit **1264** · DemoSeed **170** · Regression non-Docker **128** · Frontend **894 / 74 files** · **CI GREEN run `35214127713`, sha `afbae25`, all 7 jobs** — including the full Docker-gated regression suite (**1971 tests**), which is where this sprint's discriminating pins actually execute |
| **Orchestrator model** | Refinement revs 1–9, rulings OQ-1…OQ-10, Step 0a and this log: **Opus 5**. Both Step-4 review lenses ran on the review floor (Fable 5.1, hook-enforced). Same disclosed deviation as S141: the rulings were *proposed* on Opus; the *reviews* that checked them ran on the floor |
| **Sprint-start commit** | `e4207a2` (S141 close bookkeeping) — the `codex review --base` anchor for Step 7a |
| **CI at start** | **green** — run `35071162514`, sha `e4207a2`, all jobs |

## Sprint Goal

**Today the system thinks a Danish HR user's day ends at 01:00 or 02:00 in the morning, not at midnight.** A user working
late — say 00:30 on the 5th in Copenhagen — is, in UTC, still on the 4th. Every "today" the product computes comes from the
UTC calendar, so their change is recorded as taking effect **yesterday**. Sprint 142 moves every *business date* onto the
Danish (Europe/Copenhagen) calendar day, which is the day Danish employment law actually means.

**Why it is worth a sprint of its own.** The owner's question during S141 — *"Why not update the system's clock to the Danish
clock? We will only have Danish users"* — has an obvious answer, and the obviousness is the trap. The UTC day was never
chosen. The frontend sent `new Date().toISOString().slice(0,10)` because that is the easy way to get a date string in
JavaScript; the backend's same-day validators were then deliberately made UTC **to agree with that call**; and every writer,
both `users.*` caches, the login-token mint and S141's new as-of-today reads followed the validators. So an accident at the
edge propagated inward until it looked like a decision. Changing it is therefore not a one-line clock swap — it is a
coordinated move across roughly 250 sites, where a partial move is *worse than no move*, because two halves of the system
disagreeing about what day it is produces wrong dates rather than late ones.

**The line that governs every site — and it is the whole sprint in one sentence:**

- **Business dates move.** An `effective_from`, a validator's "is this in the past?", a stored effective-date stamp, a
  default date offered to the user — these are *calendar facts about employment*, and Danish employment law reckons them on
  the Danish calendar.
- **Instants stay UTC.** `created_at`, `updated_at`, audit timestamps, outbox ordering, JWT expiry — these are *moments in
  time*, and UTC is exactly right for them. Moving an instant would corrupt the audit trail and event ordering, which are
  inviolable invariants. **A site that looks like a date but is really an instant must be left alone**, and this is the
  single most likely way for this sprint to do harm.

## Step 0a — entropy scan

| Check | Result |
|-------|--------|
| Working tree | **clean** at `e4207a2` |
| Untracked source | none under `src/`, `frontend/src/`, `tests/` |
| CI health | **green** on HEAD (run `35071162514`); previous run also green |
| `tools/check_docs.py` | **cannot run** — Python is absent from this machine (standing, verified in S141). The docs gate, the db-schema generator and the design-sync checker are **CI-only** this sprint |
| **Stale agent worktrees** | **★ FINDING — 24 worktrees + 50 branches, all merged. Now actively blocking.** See below |

### The worktree finding — no longer cosmetic

S141 found eight stale worktrees (a *worktree* is a separate full checkout of the repo, given to each agent so parallel
agents cannot collide on the same file). It recorded teardown as owed at Step 7 and **that teardown never happened**, so the
count has grown to **24 worktrees plus 50 `worktree-agent-*` branches** — 26 of those branches no longer even have a
directory.

**They have crossed from waste into obstruction.** In this planning session alone, four repo-wide searches and a plain
`git status` **timed out**, because git and grep are walking 24 complete copies of the codebase. S142 is the worst possible
sprint to carry that cost: it is nothing but a repo-wide sweep across ~250 sites.

**Verified lossless before proposing any delete:** `git branch --no-merged master --list "worktree-agent-*"` returns **zero**.
All 50 branches are ancestors of master, so every commit in every worktree is already reachable from `master`. **Owner
authorised the full teardown (2026-09-16), to be executed once the in-flight census agents finish reading the repository** —
24 worktrees (including 2 flagged `locked`) and all 50 branches.

**The transferable lesson, which is about the harness and not about tidiness:** S141 recorded this as owed and the record
alone did not cause it to happen. The same failure shape produced S141's task-ledger gate — work that is *named* but not
*gated* does not get done. Teardown verification belongs in `sprint-close-guard.ps1`, not in a sentence in a sprint log.

## Review posture

**Step 4 (refinement) — COMPLETE, both lenses, two cycles each, zero BLOCKERs at cycle 2.**

| Lens | Cycle 1 | Cycle 2 |
|------|---------|---------|
| External (Codex) | findings absorbed into revs 2–7 | **all six technical checks clean**, 1 WARNING |
| Internal (Reviewer) | findings absorbed into revs 2–7 | **APPROVED-WITH-WARNINGS** — 0 BLOCKER, 5 WARNING, 6 NOTE |

**Both lenses independently found the same hole, from different directions**, which is the strongest signal this process
produces: the census justifying the plan **existed only in a conversation**, while the refinement claimed it was "enumerated
by file and line". That is the exact loss pattern CLAUDE.md records for S125's finding F4. Rev 8 enumerated the deferred
remainder in the document; rev 9 went further and **dispatched two agents to write the full census to disk** — split
production/test so neither holds the whole surface at once:

- `.claude/sweeps/SWEEP-s142-production-clock-census.md`
- `.claude/sweeps/SWEEP-s142-test-clock-census.md`

**The most instructive finding of the refinement (internal W2)** was not that a verdict was wrong, but *why* it was wrong.
I had cleared eight sites with the reasoning "S141 removed the refusal, so nothing compares those values to today." That is
true of `TemporalWriteRouter` — and it is not the only way a clock reaches a business date. The POST-users writer **stamps**
`effective_from` from the server clock; a stamp is not a comparison, so the argument never touched it. **I justified a
verdict with one mechanism and applied it to a set chosen by a different criterion.** The remedy is structural, not a
re-check: **mechanism is now a required column** of the census, so no site can be cleared by an argument that merely happens
to be true of its neighbour.

**Step 0b (plan review) — cycle 1 found four blockers in a plan I had just described to the owner as ready.**

| Lens | Cycle 1 verdict | What it caught |
|------|-----------------|----------------|
| Internal (Reviewer) | **CHANGES-REQUIRED** | Nine of 64 MOVE rows in no task; the OQ-4 deletes in no task; **the sprint-wide carve-out was itself wrong**; four coupled pairs split across tasks; the predecessor column wrong in both directions |
| External (Codex) | **BLOCKER ×2** | Test-side ownership unprovable (category shorthand cited by two tasks); the validator↔picker pair "handled by ordering", which is not atomicity |

**The two lenses converged on the carve-out from opposite directions**, which is the strongest signal this process produces
and the reason both are run. The internal lens found it by noticing the plan contradicted **OQ-3, recorded three tables
above it in this same document**. Codex found it by reading the code comments, which say *"when the writers move, this moves
with them"* — a condition S142 satisfies. Neither needed the other, and both were right.

**Three lessons worth keeping, because each is a different failure:**

1. **A count is not a list.** My task table summed to 55 of 64 because I built it from section groupings and never
   enumerated. This is the *same* failure the censuses were commissioned to prevent, committed while writing the plan that
   consumed them. The ledger now carries the row-by-row mapping.
2. **Ordered is not atomic.** A predecessor sequences two commits; it does not merge them. The state *between* them is real
   and can be checked out.
3. **Organising by layer manufactured every coupling failure.** Endpoints in one task and repositories in another meant a
   Copenhagen endpoint could be read back by a UTC repository. Slicing by domain dissolved all nine at once — the fix was
   structural, not nine separate patches.

## Owner rulings

| # | Ruling | Date |
|---|--------|------|
| **OQ-1** | Business dates are **always** the Copenhagen day — no per-surface exceptions | 2026-09-16 |
| **OQ-2** | **No residual** to migrate: nothing is deployed and there is no data, so no backfill and no dual-read window | 2026-09-16 |
| **OQ-3** | Two earlier dated rulings that fixed the UTC day are **superseded** — bookkeeping, not a reversal | 2026-09-16 |
| **OQ-4** | **Delete** the two dead code paths; **keep and fix** the migrator | 2026-09-16 |
| **OQ-5** | The demo seeder moves to the Danish day — *premise later corrected, see OQ-10* | 2026-09-16 |
| **OQ-6** | The catalog anchor moves too, for consistency | 2026-09-16 |
| **OQ-7** | Dates currently decided by the **database** move into the application, where the zone is explicit | 2026-09-16 |
| **OQ-8** | The schema-init statement is converted **in place**, so the rule holds everywhere with no documented exception | 2026-09-16 |
| **OQ-9** | **SPLIT** — correctness in S142, hygiene in S143. The line: *"would this be wrong, or merely undisciplined?"* All three defective test shapes count as wrong | 2026-09-16 |
| **OQ-10** | **Link the demo seeder to the shared helper** (`ProjectReference` → SharedKernel), making the Danish day explicit rather than circumstantially true | 2026-09-16 |
| **OQ-11** | **A missing Copenhagen zone refuses the application's start** — the silent UTC fallback is replaced by a boot assertion probing **both** offsets (winter `+01:00`, summer `+02:00`) | 2026-09-16 |
| **OQ-12** | **In the browser, an unresolvable zone disables the date picker with an explicit message** — loud, but contained to one control rather than blanking the config editor | 2026-09-17 |

### OQ-12 — the same principle as OQ-11, adapted to where it fails

TASK-14201's new frontend helper mirrors OQ-11: no degraded mode, because falling back to the browser's own clock would
**silently reinstate the precise defect this sprint removes**. That much is settled.

**But a server and a browser fail differently, and the ruling follows the difference.** A server either boots or does not,
and an operator reads a log. An uncaught throw during *render* blanks the whole config editor: the admin sees a white
screen with no explanation, and **every unrelated field on that page becomes unreachable too.**

Disabling the one control with an explicit message keeps everything OQ-11 was protecting — a wrong date still cannot be
entered, and the failure is still loud and visible — while confining the damage to the control that actually depends on the
zone. **Both options satisfy the correctness invariant, so the usability trade-off decides**, which is the invariant model
working exactly as written rather than being overridden.

*Recorded honestly: this cannot occur on any browser the product supports — every current runtime ships the full tz
database. It is settled now because it is cheap now and awkward later.*

### OQ-11 — why a documented "never crash" choice is being reversed

The UTC fallback in `CopenhagenBusinessDate` was a deliberate, commented decision, and it was **right for the code as it
was**: when UTC *was* the business calendar, falling back to UTC degraded to the same answer everything else used. **S142
destroys that property.** Afterwards the fallback does not degrade gracefully — it silently reinstates the exact defect this
sprint exists to remove, with no signal anywhere.

So the choice is not availability versus correctness in the abstract. It is: *would we rather the application not start, or
start and record employment dates a day early without telling anyone?* Domain correctness is an inviolable invariant and
availability is not on the trade-off list at all, so the ruling follows the invariant model rather than overriding it.

**The premise-removing option was checked and rejected on cost.** Embedding a timezone database would make host
configuration irrelevant, but `StatsTid.SharedKernel` has **zero** package and project references, and that purity is
load-bearing — it is what made OQ-10's seeder link cheap. Ending it to harden one lookup is a poor trade.

**The probe must check two offsets or it is decorative:** a winter-only check is passed by a hardcoded `+01:00` zone, which
is QUAL-005 — the original bug this class was written to prevent. Checking winter `+01:00` **and** summer `+02:00` refuses
UTC, a hardcoded `+01:00`, and a hardcoded `+02:00` alike. **A guard that cannot fail is the same defect as a test that
cannot fail**, which this sprint is already removing in three other places.

**Reachability, recorded honestly:** this cannot fire today — no Dockerfile exists for the application, `InvariantGlobalization`
is set nowhere, dev machines resolve the Windows id, CI runners carry the IANA database. It is closed now because the first
slim container image would make it reachable *and* invisible in the same moment.

### OQ-10 in full, because it corrects both a reviewer and me

The internal lens flagged the demo seeder as unruled, on the grounds that it "defaults to `DateTime.Today`". **Reading the
code, it does not** — the default is the constant `2026-06-15`, and the clock is reached only on the opt-in
`--reference-date rolling` branch, whose single caller is a copy-paste command in the tool's own README. Further,
`DateTime.Today` is the **machine-local** day, which on a Danish machine *already is* the Copenhagen day.

**I then overstated the cost of the option the owner chose.** I said the byte-exact demo-data pin would become sensitive to
production changes. It will not: the committed artifacts are generated at the pinned constant and **never traverse the clock
path**, and `StatsTid.SharedKernel` has no package or project references of its own, so the new edge carries zero transitive
footprint. What is actually surrendered is narrower than the S84 contract's wording implies — the tool is no longer
*literally* reference-free, so that comment must be rewritten rather than left to rot into a false claim.

**Why the owner's choice is right anyway:** the tool was correct only *because of a fact about the machine running it*. That
is the same class of accident the sprint exists to remove. A site that is correct for a reason nobody wrote down breaks the
first time the reason changes — here, the first time anyone runs the reseed on a UTC CI box.

## Decomposition

**Both censuses are on disk and every task below points at rows in them, never at a count.**

- `.claude/sweeps/SWEEP-s142-production-clock-census.md` — 139 rows · **64 MOVE** · 4 S143 · 22 LEAVE-with-reason · 49 LEAVE-instant · 2 UNRESOLVED
- `.claude/sweeps/SWEEP-s142-test-clock-census.md` — **68 S142** (61 strictly gating + 7 defective shapes) · ~32 S143 · ~230–250 SAFE · 2 UNRESOLVED

### ⛔ RETRACTED at Step 0b — the carve-out I wrote was the single most dangerous line in this plan

**What I wrote:** that `EffectiveDateBoundaryTests.cs:261, 401` pin 23:30 UTC on purpose and must NOT be "fixed", because
they prove the expiry refresh and the "cannot register" detector deliberately use the writers' UTC day (owner ruling
2026-09-14, QUAL-157) — and that this instruction goes in every agent prompt.

**It was wrong, and it contradicted four things at once**, including a ruling recorded three tables above it in this very
document:

- **OQ-3**, in this log's own rulings table, records the 2026-09-14 ruling as **superseded**.
- The refinement's acceptance criteria require those pins to "assert the rule (*matches the writers*) rather than the
  outcome, and still fail on divergence".
- The refinement's Risks section says plainly: *"One test suite will go red, and that is the correct signal … It must be
  rewritten, not silenced."*
- The **production census moves the very code those tests pin** — `DelegationExpiryService.cs:151` is row 42 (MOVE) and
  `HrFollowUpApprovalEndpoints.cs:275` is row 22 (MOVE).

**How it happened, because the mechanism is the lesson.** The two censuses disagree on exactly one point: the test census
read the September ruling as permanent (its Category 5), the production census moves the code underneath it. **I adopted
both sides** — the task table from one, the carve-out from the other — and never noticed they were incompatible.

**What it would have cost.** Agents obeying the prompt would move every writer to Copenhagen while leaving the expiry sweep
and the detector on UTC — *the precise split the Sprint Goal calls "worse than no move"* — and the two 23:30Z pins would
have stayed **green**, certifying the wrong behaviour. Nothing downstream catches it. **A sprint-wide instruction that
guarantees an invisible failure is worse than no instruction at all.**

### ✅ What replaces it

1. **The two clock facts are REWRITTEN, not preserved** — under the same 23:30Z instant, asserting the Copenhagen outcome
   with literals, and renamed so the method names stop asserting UTC. They stay the suite's only discriminating pins. They
   belong to the tasks that move rows 42 and 22 (TASK-14204 and TASK-14206 respectively), so the code and its proof move
   together.
2. **The real do-not-touch list is the census, by row number** — production census **H3 (rows 73-90, already on Copenhagen)**
   and **section I (rows 91-139, instants)**. That is what goes in every agent prompt. The instants are the genuine hazard:
   moving one corrupts the audit chain.
3. **Four comments are rewritten or deleted in the same commit as their sites** — `ReportingLineRepository.cs:44-52`,
   `DelegationExpiryService.cs:143-149`, `HrFollowUpApprovalEndpoints.cs:262-274`,
   `HrFollowUpApprovalReadRepository.cs:820-838`. Left alone they will mislead the next sweep **exactly as
   `init.sql:4263-4264` misled this one** — and that is no longer a hypothetical: it is the second time in this sprint that
   stale commentary has produced a wrong conclusion in a careful reader.

### Tasks

**Restructured at Step 0b, and the restructure is the finding.** My first table split the work **by architectural layer** —
endpoints in one task, repositories in another. That is what produced *every one* of the nine coupled-pair violations the
review found: an endpoint and the repository it calls kept landing in different tasks, so a user could be created against a
Copenhagen endpoint and read back through a UTC repository. **Slicing by domain instead of by layer dissolves all nine at
once**, and removes the cross-task ordering constraint the reviewer would otherwise have had to impose. The table below also
closes the coverage hole: my first one summed to **55 of 64** MOVE rows.

| Task | Scope | Census rows | Predecessor |
|------|-------|-------------|-------------|
| **TASK-14200** | **Harness seam.** Public `WithFixedInstant(DateTimeOffset)`; `HostAtInstant` promoted out of its private home. Three canonical instants — summer `22:30Z`, winter `23:30Z`, and a winter instant where both calendars agree — each failing a *different* wrong implementation | test Q5 | — |
| **TASK-14201** | **Config family + the frontend helper** — the four config endpoints and **`MondayDatePicker.tsx` (row 59)**, merged in rather than ordered after (see below). Converts four endpoint files to the `TimeProvider` seam: they read the ambient clock today, so no pin can reach them first. **Owns creation of the single frontend Copenhagen helper** (`Intl.DateTimeFormat(…, { timeZone: 'Europe/Copenhagen' })`) plus its unit test — none exists today, and browser-local is *not* the answer, it is a third wrong calendar. 14201 lands first and needs it for the picker, so creating it here is what makes 14209's dependency honest | prod 4, 5, 6, 14, 18, 19, 20, 32, 33, 34, **59** · test: the 10-file cluster (46 sites) + `MondayDatePicker.test.tsx` | 14200 |
| **TASK-14202** | **Admin & agreement codes** — `AdminEndpoints` **with** `UserAgreementCodeRepository` (row 52), the cache chain `:348 → :441 → :686-687` that feeds the login token. Split across tasks, a user created at 23:30Z would get `effective_from = F+1` while the refresh asks for `<= F`, leaving `users.agreement_code` **empty until UTC midnight**. Also the Shape-1 tolerant assertion at `AdminUserCreateAtomicTests.cs:424-425` | prod 1, 2, 3, **52** · test `AdminEndpointsAgreementCodeTests`, Cat-1 Shape 1 | 14200 |
| **TASK-14203** | **Approval & Skema** — handlers with the repository and authorizer that derive their own day, so the admission gate and the candidate CTE cannot describe different days | prod 7, 8, 9, 10, 11, **30, 31**, **35–41**, **43, 44, 45** · test `TeamOverviewAggregateTests` | 14200 |
| **TASK-14204** | **Reporting lines, stand-ins & delegation expiry** — 7 endpoint sites incl. row 25 (invisible to the standard grep) **with** `DelegationExpiryService` (row 42) and `ReportingLineRepository` (rows 50, 51). **Rewrites** `EffectiveDateBoundaryTests.cs:261` to assert the Copenhagen outcome; fixes the Shape-1 assertion at `:200-205`; **deletes** the three `DelegationExpiry_*` tests running private SQL against a retired mechanism | prod 23–29, **42**, **50, 51** · test Cat-1, Q6 | 14200 |
| **TASK-14205** | **Employee profile, history, eligibility & compliance** — endpoints with `EmployeeProfileRepository`. Row 15 is OQ-6 routing: left on UTC, a same-day profile edit is routed as a *new interval* instead of an update, writing a wrongly dated history row. **Must also convert `ComplianceEndpoints.cs` and `EntitlementEligibilityEndpoints.cs` to the `TimeProvider` seam** — neither file references `TimeProvider` at all today, so an agent will otherwise reach for `TimeProvider.System`, which gives a correct date that **no pin can reach**: right answer, untestable | prod **13, 15, 16, 17, 21**, **46, 47** | 14200 |
| **TASK-14206** | **HR follow-up detector** — row 22 with `HrFollowUpApprovalReadRepository.cs:840` and the two "DO NOT correct" comments. **Rewrites** `EffectiveDateBoundaryTests.cs:401`. Left on UTC while writers move, the detector reports gaps that do not exist — **nightly** | prod **22** + read-repo `:840` · test Cat-5 (rewritten) | 14200 |
| **TASK-14207** | **Settlement & balance** — row 12 with row 53. `VacationSettlementService.cs:1462-1473` exists to reproduce `BalanceEndpoints.cs:735`'s chain **byte-for-byte**; split, they disagree for 1–2 hours nightly. **Must add a parity pin** asserting both derive the same day at a divergent instant — that pin is what makes 14200 a real predecessor rather than an assumed one | prod **12**, **53** | 14200 *(hard — via the new parity pin)* |
| **TASK-14208** | **Dates the database decides + the OQ-4 deletes** — the migrator's two SQL sites, the `init.sql` block, the false "no-op" comment at `:4263-4264`, and deleting the two dead paths ruled under OQ-4. **What the `init.sql` pin may assert:** that the block *fired* (`effective_to IS NOT NULL` and `version = 2` on the seeded `emp005` row — which alone falsifies the `:4263-4264` comment) and that the statement text carries `AT TIME ZONE 'Europe/Copenhagen'`. **Not** a comparison against a Copenhagen "today" computed at run time — no `TimeProvider` can reach the database clock, so that pin would be Shape 3 or midnight-flaky, i.e. one of the defects this sprint removes. The migrator's own tests construct it directly and can take `FixedTimeProvider(DateTimeOffset)` today | prod 54, 55, 56, **69, 70** | — *(independent: DB-init verification, not the host seam)* |
| **TASK-14209** | **Frontend** — the remaining 5 sites, **consuming** the Copenhagen helper that TASK-14201 creates (see below), plus the Shape-3 self-referential tests and the `hireDate` near-cousin | prod **60–64** · test `useEditPerson.test.tsx:34,367,369`, `PersonDrawer.effectiveDate.test.tsx:17,240,282,328`, `PersonDrawer.hireDate.test.tsx:112,117,132` | **14201** *(hard — consumes its helper)* |
| **TASK-14210** | **Tooling** (OQ-10) — the SharedKernel reference, the rewritten self-containment comment, and a month-boundary pin that **injects a `TimeProvider` into the reference-date resolver** rather than going through the HTTP host, which is why this task needs no harness seam. The pin fires at the only instant that can change this tool's answer: a month boundary where the two calendars disagree | prod 57, 58 | — *(independent: resolver-level injection)* |
| **TASK-14211a** *(agent)* | **Startup guard** (OQ-11) — the fail-fast zone assertion in `Program.cs` with the **two-offset** probe (winter `+01:00` **and** summer `+02:00`; a winter-only check is passed by a hardcoded `+01:00` zone, which is QUAL-005, the very bug the helper exists to prevent) | prod Q6 | — *(no host seam needed)* |
| **TASK-14211b** *(Orchestrator-executed)* | **Decision record & docs** — the ADR, the 10 normative locations across 5 files, a **new** `QUALITY.md` section rather than an edited line (the file is a stack of *dated* re-grades; editing one falsifies the S139 record), and the five drifted citations that have already misled a review. **Split out because CLAUDE.md forbids any agent creating, modifying or deleting files under `docs/`** — the original single task would have required an agent to break a standing constraint. "N1" = production-census Q3 N1, the runbook instruction at `legacy-db-upgrade-runbook.md:184-188` | prod Q3 | — |

**Every coupled pair is inside a single task — including the one I first tried to solve with ordering:** 12↔53 (14207) ·
42↔26/28 (14204) · 14↔49 (14201) · 22↔`:840` (14206) · 25↔26/28 (14204) · 50↔its gating test (14204) · 1/2↔52 (14202) ·
7–11↔35–38 (14203) · **14↔59 (14201)**.

**Why ordering was not good enough for 14↔59, which is the subtler lesson of this review.** I first put the backend
validator in 14201 and its frontend picker in 14209, and declared the pair safe because 14209 lands *after* 14201. The
external lens rejected that: **a predecessor orders two commits, it does not merge them.** Anyone checking out the commit
*between* them gets a Copenhagen backend and a browser-local picker — the broken intermediate state the atomicity rule
exists to forbid. "Ordered" and "atomic" are not the same guarantee, and only one of them is what the criterion asked for.
The picker moves into 14201.

### Test-site ownership — by file, and by line where a file is shared

**Why this table exists.** The first version assigned test work by category label ("test Cat-1"), and *two different tasks
cited the same label*. Category shorthand cannot prove exactly-once ownership, so at least one site had no unambiguous
owner. **Ownership is assigned by file below; no two tasks share a test file except one, which is split by line.**

| Task | Test files owned |
|------|------------------|
| **14200** | `Hosting\StatsTidWebApplicationFactory.cs` (adds `WithFixedInstant`) · `HrFollowUp\EffectiveDateBoundaryTests.cs:476-479` (promotes `HostAtInstant`) |
| **14201** | The 11-file same-day-validator cluster — `Contracts\S118AgreementConfigSpecRuntimeTests.cs` · `S118EntitlementConfigSpecRuntimeTests.cs` · `S118WageTypeMappingSpecRuntimeTests.cs` · `S119ProfileSpecRuntimeTests.cs` · `S121EntitlementWriteSpecRuntimeTests.cs` · `S121WageTypeMappingUpdateSpecRuntimeTests.cs` · `Config\EntitlementConfigEndpointTests.cs` · `Config\EntitlementConfigFullDayOnlyAdminTests.cs` · `Config\WageTypeMappingEndpointTests.cs` · `Config\WageTypeMappingSupersessionTests.cs` — **plus** `frontend\src\components\config\__tests__\MondayDatePicker.test.tsx` |
| **14202** | `Outbox\AdminUserCreateAtomicTests.cs:424-425` (Shape 1) · `UserAgreementCode\AdminEndpointsAgreementCodeTests.cs` (`:116,128,169-170` and `:300-339`) |
| **14203** | `Approval\TeamOverviewAggregateTests.cs:315-330` |
| **14204** | `ReportingLine\ReportingLineRepositoryTests.cs` — `:200-206` (Shape 1, fix) and the three `DelegationExpiry_*` tests at `:673/692-699`, `:718/740-747`, `:763/782-789` (**delete**) · **`EffectiveDateBoundaryTests.cs:261` (rewrite)** |
| **14205** | `Contracts\S112EmployeeProfileSpecRuntimeTests.cs:73` · `Outbox\EmployeeProfileAtomicTests.cs:94,121` · `Outbox\SkemaEntitlementEligibilityGuardTests.cs:165-197` |
| **14206** | **`EffectiveDateBoundaryTests.cs:401` (rewrite)** · `Worklist\HrBackdateWorklistRepositoryTests.cs:64,133` — **the site that previously had no owner at all** |
| **14209** | `useEditPerson.test.tsx:34,367,369` · `PersonDrawer.effectiveDate.test.tsx:17,240,282,328` · `PersonDrawer.hireDate.test.tsx:112,117,132` |

**`EffectiveDateBoundaryTests.cs` is the one shared file** — `:261` → 14204, `:401` → 14206, `:476-479` → 14200. Three tasks
touching one file is a merge hazard in concurrent worktrees, so **14200 lands first and alone**, and 14204/14206 are told
explicitly that they own only their own fact.

### Reconciling the census's 68 against what this sprint actually does

The census headline is **68**. This plan's S142 test set is **67**, and the difference is two deliberate reclassifications,
recorded here so the number is never again quietly adjusted to match a list:

- **−3** — the e2e month-seed cluster (`e2e\helpers\dates.ts:68-71`, `skema-registration.spec.ts:64`,
  `approval.spec.ts:329-334`). The census flagged these as *conditionally* gating, the condition being whether
  `SkemaPage.tsx`'s month-seed is in scope. It is not — it is a navigation default, filed S143. **The condition resolves to
  "not in scope", so these follow it.** The S143 item must carry `e2e\helpers\dates.ts:5-6`, whose comment says approval
  "today" is `DateTime.UtcNow` — false the moment 14202 ships.
- **+2** — `EffectiveDateBoundaryTests.cs:261, 401`. The census filed these Category 5, "do not fix". **That classification
  was the error corrected above**: it read a superseded ruling as permanent. They are rewritten, not preserved.

### The four UNRESOLVED items, resolved before dispatch rather than by the agent doing the work

An implementing agent may settle its own scope question **only when both answers land inside its own files**. Two of these
failed that test, so they were traced at Step 0b instead:

- **Agreement-code PUT** — **NON-GATING.** The OQ-6 comparison is reached only when `CarryForwardToScheduledChange` is set,
  and compares the *truncating row's* boundary, never the client's date. *But* the cache refresh is a **second mechanism on
  the same PUT** — the same shape as the W2 error that prompted these censuses, caught this time.
- **Org-transfer fan-out** — **NON-GATING, and it was misassigned.** The fan-out only *stamps*; it never compares. The code
  is `AdminEndpoints` (14202), not reporting lines. The "Resolves UNRESOLVED-2" clause is deleted.
- **`GetCannotRegisterAsync`** — one caller in `src/`, none in `tests/`.
- **Roster deadline vs HR list** — assigned to 14203 with a pin that both surfaces derive the same day.

### Scope rulings applied, not newly made

- **The e2e month-seed cluster follows `SkemaPage.tsx`.** The census flagged it as conditional. `SkemaPage.tsx:218` is a
  navigation default, filed S143 as display-only, so the e2e cluster is **S143** too. This applies OQ-9's line rather than
  making new policy.
- **The 3 `DelegationExpiry_*` tests are deleted, not migrated.** They run a private copy of SQL against a mechanism
  production fully retired in favour of `manager_vikar`. Migrating them would carefully preserve tests of dead code.

### Fixed points, confirmed by both censuses

- **A harness task is a hard predecessor to every test task.** `WithFixedToday(DateOnly)` pins **UTC midnight** —
  deliberately, so both calendars agree — which means it **cannot detect this bug**. A shared public
  `WithFixedInstant(DateTimeOffset)` is needed, or five subsystem tasks each copy a private method and write pins that pass
  blind.
- **Acceptance criterion, sprint-wide:** expected values must be **literals under a pinned instant**, never a call to the
  helper under test. This closes the self-referential test shape directly.
- **Three pinned instants**, each failing a *different* wrong implementation — **corrected during TASK-14200, because the
  specification below was originally wrong in the sprint's own signature way:**
  - **Summer 22:30Z** (CEST +02:00 → already tomorrow in Copenhagen) — kills raw-UTC **and** a hardcoded `+01:00`.
  - **Winter 23:30Z** (CET +01:00 → already tomorrow) — kills raw-UTC; *passes* a hardcoded `+01:00`, which is why a third
    is needed.
  - **Winter 22:30Z** (CET +01:00 → calendars still agree) — kills a hardcoded `+02:00`.

  **The error:** the third instant was first specified as "a winter instant where the calendars agree, e.g. mid-morning".
  **Mid-morning cannot detect a `+02:00` bug** — at 10:00Z, adding one hour or two lands on the same calendar day, so the
  wrong implementation passes. The instant must sit in the narrow window where `+01:00` has not yet crossed midnight but
  `+02:00` has. **I wrote a specification for a test that cannot fail, inside the sprint that exists to delete those** —
  caught by the implementing agent, who reported it rather than quietly deviating. All three sit ≥10 weeks from the
  March/October DST transitions.
- **Commit atomicity:** a production site and the tests that gate on it land in **one commit**.
- **A startup assertion** for the silent UTC fallback in `CopenhagenBusinessDate`, so a missing zone database fails loudly at
  boot instead of quietly reintroducing the bug this sprint removes.
- **★ Every MOVE task must add or rewrite at least one test that FAILS on the old code** — pinned via `WithFixedInstant` at
  22:30Z or 23:30Z, asserting the literal next Danish day. **Without this, the sprint-wide criterion above is satisfiable by a
  test that cannot fail:** pinning `WithFixedToday(F)` and asserting the literal `F` uses a literal under a pinned instant,
  exactly as required, and passes whether or not the conversion ever happened. Four tasks already name a discriminating pin;
  the three largest did not. **This is the sprint's own "can this test fail?" question turned on the sprint itself** — the
  defect it spent two censuses removing from other people's tests.
- **Every stale comment in a touched file is rewritten in the same commit** — a rule, not the list of four named above.
  Known extras: `DelegationExpiryService.cs:50-58`, `UserAgreementCodeRepository.cs:245`, `EmployeeProfileRepository.cs:918,
  950`. This sprint has now had stale commentary produce a wrong conclusion in a careful reader **twice**
  (`init.sql:4263-4264`, and the QUAL-157 comments that produced the retracted carve-out). Treat a comment asserting the UTC
  day as part of the code it describes.
- **Row 25's instant→Danish-day derivation uses `CopenhagenBusinessDate.Zone` directly**, as
  `HrFollowUpSettlementEndpoints.cs:453-454` already does — so TASK-14204 and TASK-14211a do not both edit
  `CopenhagenBusinessDate.cs` in concurrent worktrees. If a new SharedKernel entry point is wanted instead, it belongs to
  TASK-14200, which lands first and alone.
- **TASK-14203's consumers include the admin tree reads.** Four of its repository rows are called from `AdminEndpoints.cs`
  (period-status tree, roster, person search, overlay search) — no coupling, since those handlers derive no day of their own,
  but the task must exercise `MedarbejderRosterReadTests` and `PeriodStatusAndPersonSearchReadsTests`, not only approval tests.

## Wave 1 — agent results

### TASK-14200 — harness seam · COMPLETE, build clean, and it corrected the brief

`WithFixedInstant(DateTimeOffset)` is now public on the shared fixture; `HostAtInstant` delegates to it rather than
duplicating the wiring, and the two facts other tasks own (`:261`, `:401`) were left untouched as instructed. The three
canonical instants live in a new `Hosting/BoundaryInstants.cs`, each carrying a doc comment naming the wrong implementation
it kills. The seam's self-test asserts **literal** dates on both calendars.

**Totals:** build **0 errors / 0 warnings** · Unit **1255 pass** · DemoSeed **165 pass** · non-Docker regression **106 pass**
(+2, the new self-test facts). Docker-gated tests not run locally — standing constraint, CI verifies them, and the agent
correctly declined to claim them green.

**★ The agent found a defect in my specification, and it is the sprint's own signature defect.** I specified the third
instant as "mid-morning, where the calendars agree". That cannot discriminate a hardcoded `+02:00` implementation — see the
corrected fixed point above. **A brief demanding tests that can fail contained a test that could not.** The agent reported
it rather than silently deviating, which is the behaviour the prompts ask for and the reason they ask for it.

*Second, smaller correction: the brief's "do not touch `CopenhagenBusinessDate.cs` — TASK-14211a owns it" read as though the
file were new. It is pre-existing, from the QUAL-005 work. The instruction was still right; the phrasing was not.*

### TASK-14211a — startup guard · COMPLETE, and it improved on the brief

The app now refuses to start on a host that cannot answer "which calendar day is it in Copenhagen?", rather than starting
and quietly recording Danish employees' business dates a day early. **Totals:** build **0 errors**, 145 warnings (the S141
baseline, all pre-existing) · Unit **1264 pass** (+9).

**★ The agent proved the guard can fail instead of asserting it.** It mutated the predicate to check winter only → the
hardcoded-`+01:00` test went RED; summer only → the hardcoded-`+02:00` test went RED; both restored → 9/9 green, no mutation
markers left. **That is the sprint's own standard applied unprompted**, and it is the difference between a guard and a
decoration. The rejection message is asserted on too (must name `Europe/Copenhagen`, `tzdata`, `ICU`,
`InvariantGlobalization`) — *a guard that fails without saying what to do just relocates the outage*.

**It found a better answer than the brief on the fallback.** I offered the startup assertion as a possible *sole* gate. The
agent removed the fallback from `ResolveCopenhagenZone()` instead, because **`StatsTid.SharedKernel` is linked by five
hosts** — a fallback living inside the class lets any of the other four silently reinstate the defect. Making the guarantee
a property of the *type* rather than of one composition root is right. (`Zone` became a `Lazy<T>` property so the actionable
message is not buried inside a `TypeInitializationException`.)

**The probe is anchored on 2024, deliberately — RATIFIED.** A probe pinned to *this year's* offsets encodes a legal
assumption with an expiry date: if the EU abolishes seasonal clock changes, correct future tz data would report the same
offset in both seasons and StatsTid would refuse to boot **because the law changed**. Anchoring on immutable past history
asks the right question — *does this zone carry real Danish DST machinery?* — while `Today()` transparently follows whatever
the law becomes.

#### Two escalations, both ruled by the Orchestrator

**1. The other four hosts do NOT get the boot gate. Recorded as a known, bounded residual.** The agent verified every
consumer of `CopenhagenBusinessDate` is reached only through `Backend.Api`, which has the gate. Adding it to
`Integrations.Payroll`, `Integrations.External`, `Orchestrator` and `RuleEngine.Api` would make four services refuse to boot
without tzdata **for zero correctness gain**, since none of them touches a Copenhagen date. And the correctness hole is
already closed for them: after this change the type *throws on first use*, loudly, with the same message. **Widening a
startup requirement to services that do not need it trades real deployability for no protection** — the gate belongs where
the zone is used. If any of those four ever takes a Copenhagen date, it gets the gate in that sprint.

**2. `--openapi` spec generation stays behind the gate — RATIFIED.** Generating the API contract is a boot; a host too
degraded to run is too degraded to describe the API it would have served. CI runners carry the IANA database, so the
contract-sync job is unaffected.

*Brief correction from the agent, worth recording: on .NET 8 Windows is ICU-backed, so the **IANA** id resolves directly on
a developer machine — the Windows registry id is a rarely-reached second fallback. My brief said the reverse. It changes no
conclusion, but the accurate statement of why this is currently unreachable is "Windows resolves the IANA id".*

### TASK-14210 — demo seeder · COMPLETE, golden pin provably unmoved

The seeder's "today" is now explicitly the Copenhagen day rather than whatever day the host machine happens to think it is.
**Totals:** build **0 errors**, 145 warnings (baseline, none from DemoSeed or SharedKernel) · DemoSeed **170 pass** (+5).

**The determinism guarantee was verified, not asserted** — `sha256` of the generated SQL and manifest, before and after,
against the committed artifact: identical. The agent went one better than the brief on *how* determinism is guaranteed: it
made the overload read the clock **only** on the `rolling` branch, so the csproj's claim that the deterministic paths "never
traverse the clock path" is now *a property you can see* rather than an argument about a discarded value.

**RED proved behaviourally, since a plain stash would not compile** (the seam is new). Substituting the pre-change semantics
into the `rolling` branch produced exactly two failures — the summer month boundary (expected 2026-08-01, got 2026-07-01)
and the winter *year* boundary (expected 2027-01-01, got 2026-12-01) — with the other ten tests staying green, so the RED is
isolated to the boundary window rather than general breakage.

**★ `rolling` has TWO callers, not one — my brief was wrong, and the correction strengthens OQ-10.** Besides the README, the
reseed command lives at `.claude/skills/launch-demo-system/SKILL.md:59` — the recipe actually run to bring the demo up. **A
skill command is precisely the thing that ends up executing somewhere other than a Danish laptop**, which is the whole
argument for making the calendar explicit rather than relying on the machine.

**⚠ Cross-task hazard, broadcast to every remaining task:** `docker/postgres/99-demo-seed.sql` is stored **CRLF** on a
Windows checkout (git autocrlf, 1,147,102 bytes) while the generator writes **LF** (1,139,853 bytes). They are identical
after `tr -d '\r'`. **Anyone verifying a byte-identical guarantee on Windows without normalising first will report a failure
that is not real.**

**A cross-task dependency that was already satisfied, and would not have been a sprint earlier.** The `DemoLoader` change
flips the last 1–2 hours of each Danish day from "yesterday" to "today" on the profile PUT's `effectiveFrom`. The agent
checked the server rather than assuming: S141/TASK-14104 removed the `if (body.EffectiveFrom > today) return 422` guard, so
a Danish day running ahead of a UTC host cannot trip a refusal. **Had S141 not landed first, this task would have broken the
demo loader** — worth recording as evidence that the increment ordering was doing real work.

### TASK-14208 — database-decided dates + the four deletions · COMPLETE

The migrator takes a **required** `TimeProvider` (not one defaulted to `TimeProvider.System` — *a silent default is exactly
how a caller keeps the old invisible-clock behaviour without noticing*), captures the Copenhagen day **once per run** so a
run straddling midnight cannot discover a tuple and then load zero rows for it, and binds `@today` into both queries.
`init.sql:4333` becomes `(NOW() AT TIME ZONE 'Europe/Copenhagen')::date` — DST-aware via Postgres' own tz database, never a
hardcoded offset. `created_at` stays `DateTime.UtcNow`: **an instant, not a date.**

All four dead paths deleted with caller evidence. The strongest case was stronger than "unused": `RoleConfigOverrideRepository`
**is never constructed anywhere** — no DI registration, no endpoint, no test.

**★ Two test defects the agent caught in its own tests — both the shapes this sprint exists to delete.**
1. A `DoesNotContain("CURRENT_DATE")` pin failed against *its own explanatory comment*. Fixed by stripping `--` comments
   first: the pin is about the statement, not the prose.
2. **The corrected-comment pin would have been a FALSE GREEN.** The original sentence wrapped across two comment lines, so a
   naive whitespace-normalise leaves the `--` marker mid-sentence and the forbidden string never matches — *the test would
   have passed on the uncorrected file*. Found by restoring the original and proving RED. **This is the only way that class
   of defect is ever found**, and it is the third time this sprint that a test has been checked for its ability to fail
   rather than its ability to pass.

**Verified RED-first:** against the pre-change `init.sql`, 3 of 5 fail; against a hand-reverted `CURRENT_DATE`, 2 fail. The
remaining 2 are premise pins documenting facts the false comment denied, green on both sides by design and labelled as such.

#### Escalations, ruled

**1. The second copy of the false claim IS corrected — `init.sql:4274`.** The `schema_migrations.notes` value still read
*"no-op greenfield"*, the same false statement **in the place an operator inspecting the ledger would read it**. The agent
left it, correctly citing its narrow scope and one real argument: it is stored data, and `ON CONFLICT DO NOTHING` means a
legacy database would keep the old text, creating a divergence nothing reconciles. **That argument is settled by OQ-2:
nothing is deployed and there is no data, so there are no legacy databases to diverge from.** Leaving a known-false
statement where an operator reads it is the exact defect that has already misled two careful readers this sprint. One-word
edit, Orchestrator-executed at merge.

**2. The migrator has no production caller — recorded, ruling unchanged.** The brief called it "live"; it is constructed
only by its tests. OQ-4 ruled "keep and fix the migrator" and it has real test coverage, so the ruling stands — but the
`CURRENT_DATE` there was never reachable in production, and the record should say so rather than imply a shipped defect.

**3. Greenfield behaviour genuinely changes, and that is the intent.** `docker-compose.yml` sets no `TZ` and
`postgres:16-alpine` defaults to UTC, so `CURRENT_DATE` *was* the UTC day. A fresh database created between 22:00/23:00 and
midnight UTC now stamps one day later. That is OQ-8 working, not a regression — recorded so nobody reports it as one.

**4. A docs row is now stale → TASK-14211b.** `quality-finding-register.md:341` (QUAL-156) prescribes leaving the migrator
as-is; OQ-7/OQ-8 supersede that. It also names **two** dead sites where OQ-4 found **four** — the two extra were never
registered because their defect shape is a UTC-day read rather than a SQL `CURRENT_DATE`.

**Sprint-level result of this task:** `src/**/*.cs` now contains **zero executable** `CURRENT_DATE` / `NOW()::date` /
`LOCALTIMESTAMP` business-date derivations; every remaining match is a comment.

**⚠ To verify at merge:** this agent reported the solution warning count as **290 → 290 (unchanged)**, while TASK-14200 and
TASK-14211a both reported **145**. The delta is almost certainly how the build was invoked rather than new warnings — *no
agent reported a warning in a file it touched* — but the integrated build must be measured once against the S141 baseline of
145 before wave 2 is dispatched. **Two numbers describing the same quantity is exactly the kind of discrepancy this sprint
has learned not to reconcile by picking one.**

### Step 5a — wave 1 dual-lens · APPROVED-WITH-WARNINGS, both lenses

**Both lenses cleared the two things this wave could most easily have broken:** every converted site
is a *business date*, and every *instant* — `created_at`, audit rows, event ordering — stayed on UTC. The four
deletions are genuinely dead (no interface, no DI factory, no reflection). `(NOW() AT TIME ZONE 'Europe/Copenhagen')::date`
is the correct Postgres idiom and, crucially, **independent of the server's own `TimeZone` setting** — which is the whole
point of moving the decision out of the database.

**Three findings absorbed, and two of them are the sprint's signature defect appearing inside the sprint's own work.**

**1. A test that could not detect the defect its task removed** (internal W2). The migrator's boundary fixture claimed
*"under the old UTC/server-clock behaviour every assertion below inverts."* That is true against a UTC-day regression — and
**false against a regression back to `CURRENT_DATE`**, which is what the task actually deleted: the server's real clock is
already past 2026-07-01, so the row starting 2026-07-01 is eligible and the row ending 2026-06-30 is expired — *exactly the
expected outcome*. **The test would have passed against the very bug it was written to catch.**

Moved to **2099-12-31 23:30Z → 2100-01-01**, which makes `CURRENT_DATE` invert both rows. **Deliberately a WINTER instant,
not the summer one the review suggested:** at 23:30Z both a `+01:00` and a `+02:00` Copenhagen land on the next day, so the
pin does not depend on whether seasonal clock changes still exist in 2099. A far-future *summer* pin would have encoded
exactly the legal assumption TASK-14211a's probe deliberately avoids — **two agents' decisions pulling in opposite
directions, reconciled rather than applied blindly.**

**2. The seam itself was never exercised** (external WARNING). `FixedInstantSeamTests` proves `FixedTimeProvider` works by
constructing it **directly** — so nothing proved that `WithFixedInstant` actually wires the pinned provider into a booted
host. Its own doc comment argued the seam "does exactly one thing", which is *an assertion about the code, not a test of
it*. **Seven wave-2 tasks pin through that seam.** Had it mis-wired — overridden by the host's own registration, or resolved
from another container — every one of those pins would have passed against correct and broken code alike: *a green suite
proving nothing, sitting underneath the entire tooling built to prevent exactly that.* Added a Docker-gated host-wiring
test; unverifiable locally, which is the right trade over unverified everywhere.

**3. The migrator's constructor doc claimed a production caller that does not exist** (internal N1). Corrected to say so
plainly, rather than sending the next reader hunting for it.

**Verified integration state:** build **0 errors / 145 warnings** on a clean `--no-incremental` build — the S141 baseline,
unmoved · Unit **1264** · DemoSeed **170** · non-Docker regression **111**.

**WARNING 1 was mine, and it was fair:** wave 1 had never been through CI — ten unpushed commits meant every Docker-gated
fact in the wave had executed *nowhere*, while I was describing the wave as verified. **Local green plus "CI will check it"
is not verification until someone pushes.**

**✅ CI GREEN — run `35106215656`, sha `a9e6c87`, ALL 7 JOBS:** `build-and-test` · `frontend-build` · Smoke (docker-compose)
· E2E (Playwright) · Documentation consistency (`check_docs.py`) · Secret scan (gitleaks) · Cyclomatic complexity. The
preceding run `35105431170` (sha `ea6fe86`) was also green. **This is the first execution anywhere of wave 1's Docker-gated
facts** — the six migrator fixtures, the greenfield `init.sql` pin, and the new host-wiring test for the seam. The docs job
also settles what I could not check locally, Python being absent from this machine.

**Wave 2 (7 tasks) is dispatched against this verified base.**

*Carried to TASK-14211b (Orchestrator-executed docs): `legacy-db-upgrade-runbook.md:181` and the QUAL-156 register row both
point at now-deleted code, and QUAL-156 names two dead sites where OQ-4 found four.*

## Wave 2 — agent results

### TASK-14204 — reporting lines, stand-ins & delegation expiry · COMPLETE

All 10 rows moved. Row 25 — the one invisible to the standard grep, deriving a business date from an *instant* — was
hoisted into a named local so the conversion is visible, with `SpecifyKind` pinning the instant as UTC so the conversion
can never silently pick up the host's offset. **The instant is unmoved; only the day it is attributed to.** Five stale
comments rewritten, three more than the brief named.

**The `:261` fact is rewritten, not preserved** — same 23:30Z instant, now asserting `"HK"` and a version bump plus the
audit row showing the transition, renamed so the method no longer asserts the UTC day.

**RED proved without Docker, and the method is worth recording.** Docker is unavailable here, so the Postgres-gated
assertion could not execute — but *the thing it discriminates on* could. The agent reverted the production files, invoked
the real private `DelegationExpiryService.Today()` by reflection (it reads only the clock, so no database), and applied the
sweep's own predicate to the test's literal seed rows: pre-change it resolves 2025-11-12 → `"AC"`, post-change 2025-11-13 →
`"HK"`. **A derivation-level RED against the real production method**, honestly labelled as not a container run. The
Docker-gated fact itself stays CI-verified only, and the agent explicitly declined to claim it green.

**A better instant than the brief suggested.** For the replacement of the OR-tolerant assertion the agent chose the
**summer** instant over the winter one, because *the winter instant is passed by a hardcoded `+01:00` implementation* —
QUAL-005, the bug the helper exists to prevent — whereas 22:30Z in July only crosses midnight under the real CEST offset.
Same reasoning the sprint applied twice already, reached independently.

**Totals:** build **0 errors / 145 warnings** (`--no-incremental`, baseline held) · Unit **1264** · non-Docker regression
**111** · net regression delta **−3** (the three dead-mechanism tests deleted; the rewrites are renames, not additions).

### TASK-14202 — admin & agreement codes · COMPLETE

Rows 1, 2, 3 and 52 converted; 14 comment blocks rewritten in `AdminEndpoints.cs` alone. **Worktree verified intact after
the stash incident** — the pop had completed before the collision, all four files present.

**In plain terms:** an HR admin creating a new hire at 00:30 Danish time dated that person's **entire record** — employment
start, profile interval, agreement interval, manager edge, and all three mirroring outbox events — one day early, every
night of the year. Every audit timestamp, `created_at` and outbox ordering value is untouched and still UTC.

**RED proved in two executable halves, neither of them a claimed container run.** (1) A scratchpad console app
project-referencing this worktree's real compiled `SharedKernel`, printing the actual assertion failures at both boundary
instants — 4 assertions RED pre-change, 0 post-change. (2) Source-level: with the production files reverted, the regression
**test project still compiled clean**, proving the new facts would have *run and failed* rather than failed to build. That
second half is the one people skip, and it is the difference between "my test fails" and "my test doesn't exist yet".

#### ★ The brief's failure mode was wrong, and the truth is worse

I wrote that splitting the endpoint from its repository would write `users.agreement_code` **empty** and leave the login
token with no agreement code until UTC midnight. **Neither happens.**

- `RefreshAgreementCodeCacheAsync` writes `SET agreement_code = COALESCE(@agreementCode, agreement_code)`, and the POST's
  own users INSERT already wrote the code earlier in the same transaction. With `todayCode` null, the COALESCE **keeps
  'AC'** — the column is never empty.
- `AuthEndpoints.cs:102-124` carries a defensive fallback: `canonicalAgreementCode ?? dbUser.AgreementCode`. A null
  canonical read logs *"Inconsistent state: user_agreement_codes has no live row for user {UserId}"* and **mints a working
  token anyway.**

So a split produces **no visible failure at all** — one warning line, a silent disagreement between the canonical store and
its cache, self-healing at UTC midnight. **The same-commit requirement is still exactly right; my reason for it was wrong in
the direction that matters.** *"It fails loudly" and "it hides" call for different vigilance*, and I had described a hiding
defect as a loud one. The corrected mechanism is now recorded at both sites and in the new tests' documentation, and the
coupling test asserts the **canonical read** directly rather than an empty cache column that would never have appeared.

**A stale comment the brief did not list**, found in a touched file: `UpdateUserRequest.EffectiveFrom`'s doc said the Danish
-day move was *"deferred to its own work"*. **That deferral is this sprint.** Rewritten to say precisely which half moved
(the server) and which has not (the frontend's `toISOString().slice(0,10)`, another task's scope) — because that field's
documentation is what a frontend author reads to learn what they may send.

**Totals:** build **0 errors / 145 warnings** (`--no-incremental`) · Unit **1264** · non-Docker regression **111**.

### TASK-14206 — HR follow-up detector · COMPLETE (committed `f82a2e6`)

Row 22 and the read-repository's `today` parameter moved together. The `:401` fact is rewritten to the Copenhagen outcome
at the same 23:30Z instant and renamed, and it now asserts four literals rather than one — `missingRecord`, `gapSince`,
`daysSinceGapStart` and a null `coveredFrom` — all derived from the SQL rather than from the helper under test. `:261` is
confirmed byte-identical and untouched.

**RED proved by replaying the rewritten assertion against the reverted production expression** with real xunit: expected
2025-11-13, actual 2025-11-12. Plus confirmation that the regression project still **compiles** when reverted, so the CI
failure would be an assertion failure and not a build break. Docker-gated facts CI-verified, not claimed green.

**Totals:** build **0 errors / 145 warnings** · Unit **1264** · non-Docker regression **111**.

#### ★ My brief named two stale comments. There were five.

The three I missed were all inside files this task owned, all asserting the retired UTC-day exception as current truth:
the read-repository's file-header "THREE RULES", the endpoint's class-doc shared rules, and — **the consequential one** —
`HrFollowUpApprovalResponses.cs:282`, the **API response-contract doc for the `Today` field itself**: *"`Today` is the UTC
day here, not the Copenhagen business day."* **That is what a frontend author reads to learn what the server sends them.**

**This is now the third time in this sprint that stale commentary has produced or nearly produced a wrong conclusion in a
careful reader** — `init.sql`'s "greenfield no-op", the QUAL-157 comments that generated my retracted carve-out, and now a
contract doc that would have taught the frontend the opposite of the truth. **My per-task comment lists have been
incomplete every single time.** The instruction to agents should be a grep, not a list: search each touched file for
`UTC day`, `writers' UTC`, `not the Copenhagen`, `deferred`.

#### ★ Confirmed merge hazard between TASK-14204 and TASK-14206

`EffectiveDateBoundaryTests.cs:513-516` — both tasks renamed their own fact, and each therefore had to fix its own
`<see cref>` on **adjacent lines** (`:514` and `:515`). **Expect a conflict; the resolution is to take both renames.**
Additionally the prose there says the two facts *"are owned by other S142 tasks and are unchanged"* — **now false for both**,
since both were rewritten. That sentence and the class doc at `:55-66` get one Orchestrator fix at merge; both agents
correctly declined to touch shared prose that would have collided.

#### A limit in the wave-1 harness, found by using it

**The three shared `BoundaryInstants` are all mid-month**, so they discriminate the *day* but not the *month*. The worklist
test needed a month-granular clip, so this task defined a local `2026-07-31T22:30Z`. **22:30 rather than 23:30 deliberately:
at 22:30 a hardcoded `+01:00` still answers "July", so one pin kills both the raw-UTC and the winter-offset-year-round
bugs** — and it does not discriminate a hardcoded `+02:00`, which the agent stated in the doc comment rather than leaving
implied. **If a second task needs a month boundary, promote this into `BoundaryInstants` at merge.**

**UNRESOLVED-1 settled in the census's favour:** `GetCannotRegisterAsync` has exactly one caller, so no second clock exists
and the endpoint+repository pair is the whole move.

**Two writer-side stale comments flagged out of scope** — `EmployeeProfileRepository.cs:351` and
`UserAgreementCodeRepository.cs:157`, both documenting "today is the writers' UTC day". The first was relayed to TASK-14205
mid-flight; **the second must be checked at merge**, since TASK-14202 rewrote `:245` in that file but not `:157`.

### TASK-14207 — settlement & balance parity · COMPLETE (committed `35b1d12`)

Rows 12 and 53 in one commit. `VacationSettlementService` now has **one** clock→business-day derivation with four callers
rather than two intentional clocks — a strictly stronger invariant than the one the census recorded.

**The parity pin asserts the LITERAL Danish day at each of the three boundary instants — never site-against-site.**
That distinction is the whole design: *before this change both sites agreed on the wrong day, so a parity-only test would
have passed.* Two RED scenarios were executed: a full revert (3 of 10 fail) and — the one that matters — **the half-move**,
settlement reverted while the reader moved, which fails 2 of 10 naming row 53 exactly.

**Totals:** build **0 errors / 145 warnings** · Unit **1264** · non-Docker regression **121** (+10). Three Docker-gated
facts CI-verified, not claimed green.

#### ★ My acceptance criteria were mutually exclusive, and the agent resolved it correctly

I required the parity pin to use the wave-1 harness (`WithFixedInstant`, which needs Postgres) **and** to be proven RED
locally (where Docker does not run). **One artifact cannot satisfy both.** The agent split it: a non-Docker parity pin over
`BoundaryInstants`, locally RED-proven, plus a Docker-gated behavioural companion using `WithFixedInstant`. Both harness
artifacts are exercised and neither claim is unverifiable. **A contradiction in the brief, surfaced rather than silently
resolved by dropping whichever half was inconvenient.**

#### ★ My description of the half-move symptom was wrong — the second time this wave, and again worse than I said

I wrote that a divergence means *"the balance page would mark a period 'now' that the settlement engine considers past."*
**The settlement engine does no past/current/future marking with this value.** The agent traced every use of the variable
in scope: site 53's `today` has **exactly one consumer**, and what actually diverges is `todayAgreementCode`, which keys
`liveConfig` and the dated-config fallback terminal.

**So a half-move means the settlement engine values a closed ferieår against a DIFFERENT agreement's `annual_quota` and
`carryover_max` than the screen displays** — a money-shaped disagreement inside a replay-sensitive, ADR-033 D3 immutable
capture. The in-code comments now say that rather than repeating my version.

**The pattern is now established and it is about my analysis, not theirs.** Twice this wave an agent has traced a failure
mode I asserted and found the real one both *different* and *more severe* — the agreement-code cache (I said "empty column,
broken token"; truth: silent divergence that self-heals and hides), and this one (I said "a display marker disagrees";
truth: money computed against the wrong agreement). **I have been describing plausible symptoms instead of following the
value to its consumer.** The correction is mechanical: trace the variable, do not narrate it.

**Declared deviation, accepted:** no Docker-gated behavioural assertion for site 53's day. Observing it end-to-end needs a
contrived topology fighting the config seeder, in a test that could not be run locally. **A fragile, unverifiable CI test is
worse than a fully machine-checked local chain** — call site → adapter body → `using` binding → literal-pinned evaluation.
Correct judgement; recorded rather than hidden.

*Census row 53's note ("the file already runs both clocks on purpose") is stale after this change and should be updated.*

### TASK-14203 — approval & skema · COMPLETE

All 17 rows moved — handlers, the period repository and the authorizer together, so the admission gate and the candidate
query can never describe different days. **No stash used** (copy-aside); the foreign stash entry was seen and left alone.

**Totals:** build **0 errors / 145 warnings** · Unit **1264** · non-Docker regression **116**. Docker-gated boundary facts
CI-verified, not claimed green.

**★ The best-designed RED proof of the sprint, and the design choice is the point.** A stand-in approver whose inclusive
last day is 15 July, pinned at `2026-07-15 22:30Z` — Copenhagen is already the 16th, so the stand-in holds no authority.
Pre-change the code asked the UTC calendar, still said the 15th, and **granted**. 4 of 5 facts RED with real output; the
5th is the deliberate "calendars agree" control that must stay green in both states.

**The agent chose a fail-OPEN shape over a deny-side one on purpose**: this defect does not lock people out, it **grants
stale privilege**. That is the security-relevant direction, and it is the one a casual test would have missed by asserting
the easier "access denied" case. It also runs **without Postgres** — the authorizer's overloads accept in-memory sources —
and the `NpgsqlConnection` argument deliberately points at an unreachable host, so a future edit that stops honouring the
stubs **fails loudly rather than passing quietly**.

#### My caller mapping was wrong for one row, and it would have produced a test that covered nothing

I wrote that rows 38–41 are called from four `AdminEndpoints.cs` handlers. Rows 38, 39 and 41 check out. **Row 40 does
not** — it lives in a private method whose only caller is the roster, not person search, and the person-search handler
binds **no `today` at all**. A test written against person search, as my brief implied, would have asserted nothing. The
agent asserted row 40 through the roster instead.

#### ★ Row 31 is a correctness site, not the consistency site OQ-6 called it

OQ-6 ruled the catalog anchor moves "for consistency", on the strength of the site's own comment that nothing durable
depends on it. **True for the preferences themselves — and that same `today` also feeds `GetByUserIdAtAsync`, which selects
the employee's dated agreement code.** On a boundary day the two calendars can therefore select **different agreement
codes, and validate against different catalogs.**

The ruling does not change — the site moves either way — but the *reason* recorded for it was too generous, and the code
comment now states the real one. **This is the fourth time this sprint that a site's own comment understated what it
does**, and the third time the understatement came from trusting that comment rather than tracing the value.

#### Two smaller findings worth keeping

- **A comment broke a source-text guard.** `ScheduledChangeMarkerTests.cs` asserts a SQL constant name appears exactly three
  times in a file — a "no fourth hand-written copy" guard. **Merely naming the constant in a prose comment made it four and
  turned the guard red.** Worked around by paraphrasing, but the guard cannot distinguish a splice from a mention; brittle
  by construction, and outside this task's scope to fix.
- **A latent flake fixed at the source.** `InsertAbsenceTodayAsync` seeded UTC-today against an *unpinned* host; once the
  query moved to Copenhagen, seed and server would have disagreed nightly between 22:00 and midnight UTC. Now derived the
  same way the server does. **This is one instance of the wider flake surface TASK-14204 flagged** — fixture inputs built
  from the UTC day against validators that now mean the Danish day.

### TASK-14205 — employee profile, history, eligibility & compliance · COMPLETE

All 7 rows moved; both ambient-clock files (`ComplianceEndpoints`, `EntitlementEligibilityEndpoints`) converted to **real
`TimeProvider` injection**, not `TimeProvider.System` handed to the helper. `CreateAsync`'s stamp was routed through the
repository's single `Today()` rather than a second inline derivation, so **the file now has one calendar.** Roughly
**eighteen** stale comment blocks rewritten — including one that actively *argued for* UTC. **No stash used.**

**Totals:** build **0 errors / 145 warnings** · Unit **1264** · non-Docker regression **111**. The six new facts sit behind
HTTP + Postgres and are **CI-verified only; explicitly not claimed green.**

**RED proved two ways without Docker:** the regression project compiles cleanly with all five production files reverted
(so the failure is an assertion, not a build break), plus the decision arithmetic executed against the real `SharedKernel`
— including the carry-forward predicate flipping `True → False` and the history label flipping `SCHEDULED → CURRENT`.

#### ★ My brief's central claim was wrong, and a test built on it could not have failed

I wrote that row 15 *"decides OQ-6 routing: whether an edit is an update to the row covering today, or a new dated
interval… writing a wrongly dated history row."* **It does not.** Routing is `TemporalWriteRouter.Decide(timeline,
requestFrom, today)`, and **no branch of that method reads `today`** — the router's own comment says so explicitly. Cases
B′/C′/E/G/T are decided purely by the *request's* `EffectiveFrom` against the locked timeline.

**The agent's own words on why this mattered: "the obvious test built on the brief's premise cannot fail."** My wrong
premise would have produced a vacuous test — inside the sprint whose entire purpose is deleting vacuous tests.

**What row 15 actually decides**, and where the durable defect really lives, is the carry-forward test
`boundary > today` (`EmployeeProfileEndpoints.cs:987`). On the UTC day, **a row that has ALREADY TAKEN EFFECT is classified
as a future scheduled change and receives a second routed write into it** — an un-asked-for change to a live row. It also
decides the covers-today **404**: a profile whose only row begins today (*which is exactly what the demo seeder now
writes*) could not be edited at all.

**This is the third failure mode I asserted that turned out wrong on tracing** — and the three form one pattern: I described
a plausible consequence rather than following the value to its consumer. The census was right in all three cases; my prose
around it was not.

#### Row 47 — RULED: convert, do not delete, and register the finding

`EmployeeProfileRepository.CreateAsync` has **zero production callers** — the boot seeder and `AdminEndpoints` both INSERT
inline — so it has the same shape as rows 48/49, which OQ-4 deleted. **It is not the same case: it has three live test
callers** exercising real dating semantics. OQ-4 deleted paths with *no* callers of any kind; deleting this one means ruling
those tests worthless, which is a larger decision than a date sprint should take in passing. **Converted, as the agent did.**

The residual is real and gets registered rather than lost: *a repository method with no production callers whose behaviour
is asserted only by tests is a latent divergence risk* — it can drift from the two inline INSERTs that do the real work,
and the tests would keep passing. Quality-register item, not an S142 deletion.

#### The grep beat my list again — and caught a doubly-wrong contract doc

My "known stale" line numbers were **pre-merge** (TASK-14208 had already removed the shim and shifted the file). Grepping
instead caught the one my mid-flight message predicted would be missed:
**`S112EmployeeProfileSpecRuntimeTests.cs:20`**, a class doc asserting `effectiveFrom` *"MUST be today (UTC)"* **and**
citing an ADR-023 D8 validator that S138/S141 already deleted — **two wrong statements in one sentence, in a file a
contract reader consults.**

#### Two harness notes for the merge

- **A second task hit the month-boundary limit** and defined its own local instant. **Promote it into `BoundaryInstants` at
  merge.** The agent's observation is the reason it matters: *the month version is the payroll-visible form of this defect*,
  because every export, settlement and approval period here is month-bounded.
- **Both seasons are exercised** in the new class (CEST for the marquee facts, CET for the deletes), so a hardcoded `+01:00`
  "fix" — the QUAL-005 shape — cannot pass it.
- **Row 13 has no cheap behavioural discriminator**; proving it needs a full authority-window fixture belonging to another
  task. Converted and on the seam, **pinned by reasoning only — declared rather than hidden.**

### TASK-14201 — config family + the frontend helper · COMPLETE (wave 2's largest)

All eleven rows moved. The four config endpoints now take `TimeProvider` **by injection** — they previously read the
ambient wall clock, so nothing could pin them. `ConfigEndpoints.cs:180`'s `CreatedAt = DateTime.UtcNow` deliberately
untouched: **an instant.** The new single frontend helper `frontend/src/lib/copenhagenDate.ts` replaces the picker's
browser-local `formatLocalDate`, which was **deleted rather than left unused.**

**In plain terms:** between Danish midnight and UTC midnight, the four config-editing endpoints believed it was still
yesterday and **refused the date on the Danish admin's own wall calendar** — while the picker on that same screen was
asking a *third* clock, the browser's.

**Totals:** build **0 errors / 145 warnings** · Unit **1264** · non-Docker regression **111** · **frontend 887 passed
across 74 files** (proving the zone forcing does not leak) · `tsc --noEmit` clean.

#### ★ The owner's machine is Danish — so every frontend boundary test would have passed against the bug

**Without explicitly forcing a test zone, the browser-local day and the Copenhagen day are identical on this machine**, and
every frontend fact would have been silently vacuous. The agent forced the zone, and then **added a guard-on-the-guard**:
an assertion in both files that fails loudly if the forcing ever stops working, rather than letting the suite go quietly
green. *This is the most transferable finding of wave 2 — it applies to every frontend date test this project will ever
write, not just S142's.*

**Three independent falsifications, with real output:** picker reverted to browser-local under `America/New_York` (3 fail),
helper swapped for browser-local (2 fail), helper swapped for a hardcoded `+02:00` (1 fail). All expected values literals.

#### ★ A `tsc` trap my acceptance criteria would have missed

I asked for *"`npm test` for the files you touch."* The frontend `tsconfig` sets `types: ["vitest/globals"]` with
`include: ["src"]`, so `process` is untyped — **the agent's first zone-forcing draft kept `npm test` green while breaking
`npm run build`.** Resolved by reaching `process` through `globalThis` in a dedicated helper, with no tsconfig change.
**Acceptance criteria for frontend work must name the type-check, not only the test run.**

#### Corrections to the brief

- **The picker's own test was never in the failure class I described.** It used `9999-01-01` and `2020-01-15` — nowhere
  near a boundary. Nothing to fix; **boundary coverage added instead**, which is what it always lacked.
- **"~46 sites" vs 31.** Both true of different things: **31 distinct expressions**, three of which are `Today()` helpers
  with 8, 2 and 3 call sites behind them. Neither number was wrong; they count different nouns.
- **My `:199` diagnosis was imprecise, and the real defect is the familiar one.** The same-day check *runs* before the
  reset-month guard but *passes* normally, so the reset-month guard usually produced the 422. The always-present defect is
  weaker and worse: **the test asserted only the status code, never the body — so it could not tell two different 422s
  apart, and would have passed against a deleted reset-month guard.** Fixed by asserting the body.
- **The test census names a file that does not exist** (`LocalAgreementProfileEndpoints.cs`); the route is served by
  `ConfigEndpoints.cs`. The *assignment* was right, the name was not — recorded in the file.

#### Deliberate self-reference, flagged so a reviewer does not misread it

The 31 fixture sites remain self-referential by design: those facts test 201/412/428 semantics and only need *a date the
gate accepts*. **Every site says so in a comment, and the correctness claim is carried by four new clock-pinned facts
instead.** Flagged because it otherwise reads exactly like the Shape-3 anti-pattern this sprint exists to delete — *the
distinction is whether the date is the subject of the assertion or merely its setup.*

**Gap declared, not hidden:** no pinned fact for the DELETE soft-close stamps (rows 6/20/34) — same converted expression,
same files as the POST pins, and a dedicated fact costs another container boot per family.

### TASK-14209 — the frontend moves to the Copenhagen day · COMPLETE (the last half of the fix)

Five production sites on the shared helper. `todayIsoUtc()` renamed to `todayIso()` — **a name asserting UTC would actively
mislead the next reader** now that it returns the Danish day, which is the same stale-commentary defect this sprint has
caught four times, caught once more before it could be created.

**Every render-path read wrapped per OQ-12**, and two were moved into their event handlers instead — which fixes the UTC bug
and the blank-the-section hazard in one move. `EffectiveDatePicker`'s `today` prop widened to `string | null` so an
unresolved zone can be passed through **honestly** rather than guessed at.

**RED proved:** reverting the helper made **6 tests fail with `expected '2026-07-15' to be '2026-07-16'`** — the exact
off-by-one — across all three rewritten files; restored, 31/31 pass, byte-for-byte diff confirmed.

**Totals:** build **0 errors / 145 warnings** · `tsc --noEmit` clean · frontend **892 tests / 74 files**.

**Four more corrections to my brief**, consistent with every other task this sprint: `PersonDrawer.tsx` had **four** call
sites, not the one I named; `ApproverSection.tsx` is under `editPerson/`, not `enhedsspor/`; and **my stated baseline of 887
tests was wrong — the agent measured 889 by restoring HEAD rather than trusting me**, which is the only way that error was
ever going to surface.

**A different defect shape found and correctly left alone:** `SkemaGrid`, `TeamOversigt`, `ArsoversigtPage`, `SkemaPage` and
`useSkema` compute today from **browser-local** rather than UTC — *wrong in the other direction*, outside this sprint's
UTC-specific census, and filed S143. Worth flagging loudly for that sprint: **browser-local is a third calendar, not a
lesser version of the same bug.**

### CI — the first real run of the Docker-gated pins, and what it caught

**Six regression failures, one root cause, zero product defects.** Every one was
`InvalidCastException: Unable to cast 'System.DateTime' to 'System.DateOnly'` — four in TASK-14205's boundary class, two in
TASK-14202's. **Npgsql boxes a Postgres `DATE` column as `DateTime`**, so `(DateOnly)scalar` compiles cleanly and throws only
**against a real database** — which is unreachable on this machine.

**This is exactly what the agents' "CI-verified, not claimed green" caveat was protecting.** Every one of them refused to
call these tests passing, and every one was right to: the failure is a type error at the database boundary that no amount of
local reasoning could have surfaced. *The discipline of declining to claim an unverifiable result is what made this a
twenty-minute fix instead of a mystery.*

Fixed to the suite's established idiom, `DateOnly.FromDateTime((DateTime)raw)`, behind a named `ScalarDateAsync` helper that
records **why** the direct cast is wrong — so the next boundary test cannot repeat it. Verified suite-wide that no direct
scalar `DateOnly` cast survives anywhere.

**One frontend failure, assessed as a flake and not claimed otherwise.** `SkemaPage.test.tsx` failed on the wave-2 run and
**passed on the next one**, passes locally under Copenhagen *and* under a forced UTC zone, and took **25.5 seconds on CI
against 0.46 locally** — a loaded runner, not a wrong result. Recorded as a flake with the evidence, not asserted as fixed.

**Runs:** `35197952262` (wave 2) — 6 failures + the frontend flake. `35201223479` (sweep) — the same 6, expected, since it
predates the fix; **frontend passed**. The run carrying the fix was still in flight at the time of writing; the regression
suite takes roughly 1h45m.

### Step 7a — internal lens: CLOSE-WITH-WARNINGS, all three absorbed

**The headline it gave, verified independently by grep at HEAD rather than read from the log:** across `src/**/*.cs` there
are **zero** live `DateTime.UtcNow.Date`, `GetUtcNow().Date`, `DateOnly.FromDateTime(DateTime.UtcNow|Today|Now)`,
`DateTime.Today` or `GetLocalNow()`; **zero** executable `CURRENT_DATE` / `NOW()::date` / `LOCALTIMESTAMP` (all 30 matches
are comments); and no live `toISOString().slice(0,10)` in the frontend. **Every coupled pair moved in one commit**, listed
side by side. **No instant was moved anywhere in twelve tasks.**

It also checked the deferral properly: the four remaining browser-local reads feed a CSS class and the `year`/`month` view
state. *A registration's date is the grid cell's own `(year, month, day)`, never "today".* **No navigation default can reach
a stored date.**

#### ★ WARNING 1 — an owner ruling that the shipped code could not deliver

**OQ-12 said an unresolvable zone must disable the date control with a message rather than blank the page. The code could
not do that**, and the tests could not see it. `Intl.DateTimeFormat` was constructed at **module scope**, so a `RangeError`
fires during *module evaluation* — before any importing component exists, let alone its `try/catch`. **Every OQ-12 guard in
the UI was dead code, and the user would have got the blank screen the ruling exists to prevent, one layer earlier than
anyone was looking.**

The OQ-12 component tests `vi.doMock` the *function* to throw when called — **a failure the real module cannot produce**. So
the tests proved the guard worked against a failure mode that did not exist, while the real one walked past them. *This is
the sprint's own signature defect — a test that cannot fail for the right reason — found in the work written to satisfy a
ruling about it.*

**Fixed:** the formatter is now built lazily on first call, so the throw lands inside `copenhagenToday()` where the guards
can catch it; still constructed once, so the render-path cost is unchanged. **Two new facts exercise the REAL module with a
real failing `Intl`** — proved RED against the old version, where *the import itself rejects*.

#### WARNING 2 — two live contract comments still teaching the retired rule

Both in the "what the next author reads before calling this" class the sprint had already identified as most dangerous:
`EmploymentHistoryResponses.cs` (the `Today` field's own contract doc) and `frontend/src/hooks/useAdmin.ts`, the latter
**wrong twice over** — it cited a validator S141 deleted *and* instructed the reader to "stamp today (UTC) so the validator
passes", which is an instruction to reintroduce the exact defect. Both rewritten.

#### WARNING 3 — the declared coverage gap was wrong, and the fix is structural

The log declared **three** unpinned rows; enumeration found about **twenty**. Every task met its "at least one failing pin"
criterion, so this was record-accuracy plus durability, not a defect — **but nothing would have failed if a single site were
reverted to the UTC day.**

**Closed with a repo-wide source-text guard** (`BusinessDateCalendarGuardTests`) that scans all of `src/` for the retired
day-derivation shapes *and* for database-decided days — **pinning all 64 rows at once for the cost of one non-Docker test
rather than twenty container boots.** Comments and string literals are stripped before matching, deliberately: this
repository *quotes* the retired shapes when explaining why they were removed, and a guard that fired on its own explanation
would push authors toward deleting the explanation. **Proved able to fail**: injecting `DateOnly.FromDateTime(DateTime.UtcNow.Date)`
into a production endpoint turns it red with the file and expression named.

*It cannot prove a site computes the right day — only that it does not compute the retired one. It is the net under the
behavioural pins, not a replacement for them.*

### Step 7a — external lens (Codex): NO BLOCKERS

Clean on six of seven questions, including the two that mattered most:

- **Q1 instant-vs-date — clean.** Every converted value is a business date. It specifically checked the one that looks
  doubtful (the stand-in start date derived from a stored instant) and confirmed the instant itself stays UTC; only its
  *calendar projection* is Copenhagen-derived. **No instant was mistaken for a date anywhere in twelve tasks.**
- **Q2 is the defect gone — clean** for the 22:00/23:00–midnight window across endpoint, repository, scheduled job, SQL,
  seeder and frontend. It also checked the deferral specifically: **browser-local navigation defaults select a view period
  and never supply a stored effective date.**
- Q4 tests, Q5 fallback, Q6 deletions, Q7 documentation — all clean. No surviving direct `(DateOnly)` scalar cast; no
  document or touched source comment still teaches the retired rule as current.

#### WARNING (Q2/Q3) — one writer/reader source split, recorded with its true severity

On the stand-in creation path, `effectiveFrom` is computed from the **injected** clock while `created_at` is persisted from
the **real** clock (`DateTime.UtcNow`) — and the read then derives its displayed date *from `created_at`*. So the writer and
the reader take the same logical value from two different sources.

**What this is and is not.** `created_at` is an instant and correctly stays UTC; nothing here converts an instant to a date
wrongly. In production both are the same system clock, so divergence requires two statements microseconds apart to straddle
a midnight tick. **The real cost is testability, not live correctness: a pinned test cannot control `created_at`, so that
displayed value is not pinnable.** TASK-14204 independently flagged the identical shape in the migrator and called it an
optional tidy.

**The clean fix is small** — read `created_at` from the injected provider too (still UTC, still an instant), so the two
values cannot diverge and the read becomes pinnable.

#### NOTE (Q4b) — the one moved family without a divergent-instant pin

`init.sql`'s conversion cannot be pinned through a `TimeProvider` at all: **no injected clock reaches the database's own
clock.** Its test therefore proves the block *fires* and that the statement text carries the Copenhagen zone, rather than
behaviour at a controlled divergent instant. That was a deliberate choice recorded at the time — the alternative test would
have been self-referential or midnight-flaky, i.e. one of the shapes this sprint deleted.

### ⚠ HARNESS DEFECT — the census was invisible to every agent that needed it

**`.claude/sweeps/` is gitignored, and a worktree is a separate checkout built from git's index. So the 87 KB census existed
only in the Orchestrator's working copy.** Eleven agents were told *"the census is the authority; read it for your rows"*
and **not one of them could open it.** The twelfth said so plainly — "the census file doesn't exist, I grepped for the sites
myself" — and was right about its own tree.

**The irony is the finding.** Both review lenses had BLOCKED this sprint's plan until the census was written to disk instead
of living only in a conversation — the S125 loss pattern. It was then written somewhere only its author could read, which is
a smaller version of the same failure. *An artefact nobody but its author can open is not much better than one never
written.*

**What saved it** was that the briefs inlined each agent's specific rows and `file:line` references, so the facts arrived
even though the file did not — and several agents re-derived them from code, which is precisely how the briefs' own errors
got caught. **That is the mitigation, not a lucky escape.** Standing rule added to `docs/AGENTS.md`: inline what the agent
needs, track the artefact if it is durable, or copy it into the worktree — and **never write "read `<gitignored path>`" in a
prompt**, because it reads as an instruction and arrives as a dead end.

### ✅ S141's worktree-freshness rule paid for itself, on the Orchestrator

The flake-surface sweep agent ran its mandated first check, found its worktree at `a9e6c87` — **before all seven wave-2
merges** — and **stopped without touching a file**, exactly as `docs/AGENTS.md` instructs.

**The cause was mine and it is the documented one.** A worktree branches from the **remote-tracking** branch, and I had
merged wave 2 locally without pushing, so `origin/master` still pointed at the last thing I pushed. The rule exists
verbatim in `AGENTS.md` because S141 hit this nine times in one sprint.

**What stopping prevented, in the agent's own reasoning:** it would have classified and "fixed" stale copies of
`ReportingLineWriteLifecycleTests.cs` and `TeamOverviewAggregateTests.cs` — the two files its brief cites as its sharpest
lead and its worked example — **both already modified by wave-2 tasks. Its fixes would have silently reverted theirs at
merge.** It would also have pinned against a `BoundaryInstants` missing the month-boundary instant added minutes earlier.

**It also declined to fix the problem unilaterally**, correctly noting that rebasing or merging is a repo-state decision
its brief did not authorize, in a sprint that had already had one cross-agent git collision. *That is the right instinct:
the agent that fixed the last git problem on its own authority is the one that nearly lost another agent's work.*

Fixed at the source — `origin/master` pushed to `22f01d0` — and the agent authorized to merge and proceed. **A governance
rule written after a previous sprint's failure caught the current sprint's Orchestrator making the same mistake.**

### ⚠ HARNESS DEFECT — `git stash` is repository-global, and the Orchestrator caused it

**A worktree isolates the working tree. It does not isolate the stash.** TASK-14204 stashed to prove its RED; TASK-14202
stashed concurrently; TASK-14202's entry became `stash@{0}`, so **TASK-14204's `pop` applied another task's
`AdminEndpoints.cs` and `UserAgreementCodeRepository.cs` into its worktree and dropped that task's stash.** Recovery
happened only because the agent noticed the foreign files, reverted them, rebuilt the other agent's stash from the dangling
commit `fee142e` via `git stash store`, and then popped its own.

**This was my instruction.** I put *"stash your production change, confirm red, restore"* into seven concurrent prompts.
The goal was right — a test must be proven able to fail — the mechanism was wrong. **Six running agents were warned
mid-flight**, and the durable fix is a standing rule in `docs/AGENTS.md`: use `git diff > patch` + `git checkout --` +
`git apply`, and more generally *a command is safe for parallel agents only if its state lives in the working tree*.
`git stash`, `git worktree`, tags, `git config --local` and `refs/` are all repository-global.

### Carried to the wave-2 merge

- **A stale class-level comment both boundary tasks share.** `EffectiveDateBoundaryTests.cs:56-66` still says *both* clock
  facts use "the WRITERS' UTC DAY". It is already wrong for 14204's half and will be wrong for 14206's. **Deliberately left
  by 14204 to avoid conflicting with 14206 on the same lines** — correct call; the Orchestrator fixes it once at merge.
- **One line edited outside the owned fact**, justified: renaming the method broke a `<see cref>` in `HostAtInstant`'s doc
  comment, which would raise CS1574 and push warnings off the 145 baseline. Only the token naming 14204's own method was
  touched; 14206's cref on the adjacent line was left alone.
- **★ A new flake surface for the sprint as a whole, outside any one task's scope.** Several Docker-gated suites build
  request *inputs* from `DateOnly.FromDateTime(DateTime.UtcNow)` — none asserts a production-derived date, so none breaks
  today. But after S142 a CI run landing between 22:00/23:00 UTC and midnight sends a UTC date to a validator that now means
  the *Danish* day. Sharpest case: `ReportingLineWriteLifecycleTests.cs:1499` PUTs `effectiveFrom = <UTC today>` to
  `AdminEndpoints.cs` — TASK-14202's file. **Must be swept before close**, or the sprint ships the nightly flake it was run
  to remove.

## Census — production surface

**Artifact: `.claude/sweeps/SWEEP-s142-production-clock-census.md` (798 rows of working, 139 classified sites).**

| Verdict | Rows | Breakdown |
|---------|------|-----------|
| **MOVE** | **64** | Backend API 34 · Infrastructure C# 19 · Infrastructure SQL 2 · `init.sql` 1 · `tools/` 2 · frontend 6 |
| **S143** | 4 | frontend display-only — `SkemaGrid:124-131`, `ArsoversigtPage:119`, `SkemaPage:218`, `TeamOversigt:384` |
| **LEAVE-with-reason** | 22 | dead code to delete 2 · matches the grep shape but reads no clock 2 · already on Copenhagen 18 |
| **LEAVE-instant** | 49 | enumerated individually, plus a class rule and a reproducible grep for the remainder |
| **UNRESOLVED** | 2 | both attached to already-Copenhagen rows; both risk *inaction*, not a wrong date |

**The census contradicted the internal reviewer on four points, which is exactly what it was commissioned to do.** Ordered by
how much they change the work:

### 1. The schema-init conversion IS observable — and `init.sql` contains a false claim about itself

Rev 9 recorded OQ-8 as *"verified by reading, not by test"*, on the reviewer's ground that the table is empty when the block
runs, so nothing can observe it. **Verified directly and it is wrong.** `init.sql:2792-2793` seeds
`('emp005','ladm01','STY02','ACTING','2026-06-01','SELF_DELEGATION','mgr01','2026-07-01')` with `effective_to` defaulting to
NULL. The block 1,519 lines later at `:4311-4315` runs
`UPDATE reporting_lines SET effective_to = CURRENT_DATE ... WHERE source='SELF_DELEGATION' AND relationship='ACTING' AND
effective_to IS NULL` — which **matches that row on every greenfield database**. The comment at `:4263-4264` stating *"On a
greenfield DB this is a no-op (no open SELF_DELEGATION ACTING rows exist)"* is false, and is almost certainly what the
reviewer read and believed.

**Three consequences:** the conversion is **testable**, so OQ-8 gets a real pin instead of an assurance; the false comment
must be corrected in the same task, because it is what manufactured the wrong inference and will do so again; and this is a
live instance of OQ-7 — a *business* date (`effective_to` on a reporting line) currently decided by the **database
server's** clock in whatever zone the Postgres container happens to run.

### 2. Four of five cited line numbers were comments, not code

The reviewer's stamp-site citations `AdminEndpoints.cs:930, 1047, 1119, 1283` are all inside block comments. The single clock
read is `:960`; the real stamp sites are `:962`, `:1077`, `:1131`→`:1137`, `:1258`, `:1290`, `:1370`. **The reviewer's
*mechanism* claim was correct and is confirmed** — including the cache-refresh chain
`UserAgreementCodeRepository.cs:348 → :441 → :678-697`. Only the coordinates were wrong. Also a scoping correction: the
"eight sites" are eight **tests**, so they belong to the test census and were never this one's to list.

### 3. `docs/QUALITY.md` must gain a new section, not an edited line

`QUALITY.md:56` sits inside `## S139 re-grade (2026-09-07)`. The file is a stack of **dated** re-grade sections, so editing
that line would **falsify the S139 record** rather than update the rule — a documentation change that destroys history to
look tidy. Correct action: add an S142 section. The list also grew: **10 normative locations across 5 files**, not "four
documents" — including `PAT-028:46` carrying the same retired example as `:35`, and three **live register rows**
(`quality-finding-register.md:341, 342, 394`) that need re-adjudication rather than a find-and-replace. The reviewer's
ADR-018 / ADR-020 "historical, leave alone" classification was right.

### 4. My own prompt's hint was stale, and three documents carry the same staleness

I told the census that `DelegationExpiryService` was on the database clock. **S140 already converted it** to a bound
`@today` (`:151`, `:174-186`); only comments remain, and the same is true of `ReportingLineRepository`. Just **two** live
database-clock day sites survive — `LocalAgreementProfileMigrator.cs:385-386, 430-431` — plus two dead ones. Three documents
still describe the pre-S140 state, which is where my stale hint came from: **a wrong doc propagated into a planning prompt
and would have produced wasted tasks.**

### Two confirmations that sharpened into something better

- **The date picker bites in *both* directions.** Behind Copenhagen it silently backdates; ahead of it the save is a hard
  400. For a Danish admin this sprint *fixes* the screen rather than merely aligning it.
- **The startup assertion needs a two-offset probe.** Checking only a winter instant (`+01:00`) would be passed by a
  hardcoded `+01:00` zone — which is precisely the QUAL-005 bug the `CopenhagenBusinessDate` class exists to prevent. It must
  probe winter `+01:00` **and** summer `+02:00`.

## Task ledger

| Task | Disposition |
|------|-------------|
| TASK-14200 | **DONE** — harness seam (`WithFixedInstant`) |
| TASK-14201 | **DONE** — config family + the frontend Copenhagen helper + the picker (merged) |
| TASK-14202 | **DONE** — admin & agreement codes with the login-token cache, one commit (merged) |
| TASK-14203 | **DONE** — approval & skema with period repo and authorizer (merged) |
| TASK-14204 | **DONE** — reporting lines, stand-ins & delegation expiry (merged) |
| TASK-14205 | **DONE** — employee profile, history, eligibility, compliance (merged) |
| TASK-14206 | **DONE** — HR follow-up detector (merged) |
| TASK-14207 | **DONE** — settlement & balance parity pair (merged) |
| TASK-14208 | **DONE** — database-decided dates + OQ-4 deletes |
| TASK-14209 | **DONE** — frontend on the Copenhagen day; the last half of the fix (merged) |
| TASK-14212 | **DONE** — test-clock sweep: all 75 UTC-today test sites classified INERT, reasons recorded in code (merged) |
| TASK-14210 | **DONE** — tooling (OQ-10) |
| TASK-14211a | **DONE** — startup guard, two-offset probe (OQ-11) |
| TASK-14211b | **DONE** — ADR-041, 10 normative doc locations across 5 files, a new QUALITY.md section, QUAL-156/157/172 closed |

**Coverage check, done by enumeration rather than by summing a table** — the failure that produced the Step-0b blocker:

```
 1,2,3→14202    4,5,6→14201     7-11→14203      12→14207       13→14205
 14→14201       15,16,17→14205  18,19,20→14201  21→14205       22→14206
 23-29→14204    30,31→14203     32,33,34→14201  35-41→14203    42→14204
 43,44,45→14203 46,47→14205     48→14208 (DEL)  49→14208 (DEL) 50,51→14204
 52→14202       53→14207        54,55,56→14208  57,58→14210    59→14201
 60-64→14209
```

**All 64 MOVE rows assigned exactly once; none assigned twice.** Plus rows 69 and 70 (the OQ-4 deletes) in TASK-14208.

**Row 59 was wrong here for one revision, and the way it was wrong is worth recording.** The enumeration still read
`59-64→14209` after the picker had been moved into 14201 — so **the plan's proof of its own headline claim contradicted the
claim**, on precisely the row both reviewers had fought over. A reader working from the ledger (which is what it is for,
including the sprint-close reviewer) would have re-created the split that Codex blocked. *A derived artefact that is not
regenerated when its source changes is worse than no artefact*, because it carries the authority of having been checked.

**Rows 48 and 49 are DELETED, not converted — OQ-4 extended, not a new ruling.** Both were filed as MOVE by the census,
which did not check for callers. Verified repo-wide: `EmployeeProfileRepository.UpsertAsync`'s request type
`EmployeeProfileUpsertRequest` has **zero** references outside its declaring file, and the only `DeactivateAsync` callers
anywhere (`PositionOverrideEndpoints.cs:377`, `ProjectEndpoints.cs:217`) resolve to *other* repositories — never
`LocalAgreementProfileRepository`. OQ-4 already ruled this exact trade-off: **a dead path carrying a defect shape is deleted,
not carefully migrated.** Converting them would have been harmless and pointless, and would have left two more sites for the
next census to re-flag. *This also removes 14201's stated reason for pulling in the profile repository — the
"inverted interval" hazard described an operation no user can reach.*

### The ledger gate is necessary but not sufficient

*Every `TASK-<n>` must carry `DONE | CUT | DEFERRED | DROPPED` before `sprint-close-guard.ps1` will pass — the gate S141
produced because Wave 4 was planned, never dispatched, and nothing noticed until CI went red.*

**But the gate permits exactly the outcome this sprint's goal forbids.** Nothing stops a MOVE task being marked `DEFERRED`
at close, which would leave one subsystem on UTC while its neighbours are on Copenhagen — the permanent split the Sprint
Goal calls "worse than no move". It is concrete, not theoretical: the boundary refresh threads **one** `today` into both the
stand-in sweep and the cache refresh over `user_agreement_codes` and `employee_profiles`, whose writers live in three
different tasks.

**Therefore: TASK-14201 through TASK-14209 are all-or-nothing.** A transient split *between commits* is acceptable; a split
*at close* is not. `CUT` or `DEFERRED` on any of them requires an owner ruling that names the residual window — how long
which surface stays on the wrong calendar — rather than a disposition word in a table.
