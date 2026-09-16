# Sprint 142 — business dates move to the Danish day

| Field | Value |
|-------|-------|
| **Sprint** | 142 |
| **Status** | **in progress — wave 1 dispatched** (14200 harness · 14208 database-decided dates + deletes · 14210 tooling · 14211a startup guard) |
| **Start Date** | 2026-09-16 |
| **End Date** | — |
| **Orchestrator Approved** | **APPROVED** — Step 0b ran **two cycles, both lenses**. Cycle 1: Reviewer CHANGES-REQUIRED (nine of 64 rows unassigned; the OQ-4 deletes unassigned; **the sprint-wide carve-out itself wrong**; four coupled pairs split; predecessors wrong both ways) · Codex 2 BLOCKER (test ownership unprovable; validator↔picker "ordered" ≠ atomic). Cycle 2: Reviewer verified 7 of 9 fixes APPLIED, 2 PARTIAL — **B1: the coverage enumeration still contradicted the picker fix it was meant to certify** — plus W1–W6 absorbed (all-or-nothing MOVE tasks; helper ownership; rows 48/49 are dead code → deleted under OQ-4; two files need the `TimeProvider` seam; a per-task *failing* pin; `docs/` split out of an agent task per CLAUDE.md). Refinement rev 9 + OQ-10, OQ-11. **12 tasks, 64/64 rows assigned exactly once** |
| **Build Verified** | — |
| **Test Verified** | — |
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
| TASK-14200 | **COMPLETE (pending Step 5a)** — harness seam (`WithFixedInstant`) |
| TASK-14201 | PLANNED — config family + profile archive |
| TASK-14202 | PLANNED — admin & agreement codes (+ login-token cache) |
| TASK-14203 | PLANNED — approval & skema (+ period repo, authorizer) |
| TASK-14204 | PLANNED — reporting lines, stand-ins & delegation expiry |
| TASK-14205 | PLANNED — employee profile, history, eligibility, compliance |
| TASK-14206 | PLANNED — HR follow-up detector |
| TASK-14207 | PLANNED — settlement & balance parity pair |
| TASK-14208 | **COMPLETE (pending Step 5a)** — database-decided dates + OQ-4 deletes |
| TASK-14209 | PLANNED — frontend |
| TASK-14210 | **COMPLETE (pending Step 5a)** — tooling (OQ-10) |
| TASK-14211a | **COMPLETE (pending Step 5a)** — startup guard, two-offset probe (OQ-11) |
| TASK-14211b | PLANNED — decision record & docs (Orchestrator-executed) |

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
