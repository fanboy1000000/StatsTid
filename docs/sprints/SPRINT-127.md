# Sprint 127 — The submit-time allocation gate: one validated send command

| Field | Value |
|-------|-------|
| **Sprint** | 127 |
| **Status** | closed |
| **Start Date** | 2026-08-06 |
| **End Date** | 2026-08-07 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes (0 warnings / 0 errors) |
| **Test Verified** | yes — local pyramid green (Unit 868/868, Regression 1455 + 42 FAIL-002 ReportingLine sheds isolation-cleared 42/42 vs fresh :5432, DemoSeed 94/94, Frontend 730/730 + tsc/lint/api-types clean); CI on push |

## Sprint Goal

Close the submit-time allocation hole the owner reported: *"I dont think an employee should be able to
send a month to approval if they have not allocated their hours."* Retire `POST /api/approval/submit`
and replace it with **one validated, month-keyed send command** behind two route adapters, executed
under the per-employee advisory lock, with server-resolved period dimensions.

**Design input**: `.claude/refinements/REFINEMENT-submit-allocation-gate.md` **rev 6** — the output of
**five dual-lens review rounds** (revs 1–5 all VOID/BLOCKED). The architecture survived all five
unchanged; every rejection was of evidence, mechanism, or acceptance criteria. Its Appendix A holds a
33-row table of claims asserted and found false — **read it before touching this sprint.**

Six owner rulings govern scope (refinement §7): **A** gate unconditionally · **B** all orgs need
projects · **R1** one state, no in-progress manager visibility · **R2** no HR override · **R3** the
free-range send form is removed · **R4** a leader may not send for another · **R5** R1 enforced at the
two display surfaces only · **R6** the legacy manager-approve bypass is accepted · **R7** (2026-08-06,
mid-sprint) the rejection reason is promoted to row level.

**R7 — ruled mid-sprint, discovered by TASK-12707 while implementing R1.** `rejectionReason` is still
served on the wire, but its only render site sits **inside** the expandable panel, and expansion is keyed
on one of the five figures R1 withholds — so for a `REJECTED` row the panel can no longer open and **that
branch is production-dead.** A leader could no longer see why they rejected a month.

Never R1's intent: R1 governs in-progress **content** (figures changing while the employee repairs the
month); the rejection reason is the leader's own **past decision**. Leaving it unreachable would also have
made 12706's answer to the S124 rationale false in practice. Rejected alternatives: delete the dead branch
(S91 discipline, but it discards information the leader authored), or re-open expansion for rejected rows
(partially undoes R1). → **TASK-12713**.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py`: 61 entries, 0 orphans, 0 dangling |
| db-schema sync | CLEAN | 67 tables in sync |
| Sprint inventory | CLEAN | 126 sprints, all have logs |
| Documentation drift | DEBT (report-only) | ROADMAP.md + QUALITY.md anchored at S111, HEAD S126. Pre-existing since S121; out of scope. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 state machine, P2 rule outcome, P3 event/audit, P4 version correctness) |
| **Scope of review** | **DECOMPOSITION ONLY.** The design is not re-reviewed: rev 6 is the output of five dual-lens passes and the Orchestrator ruled further design review past the point of return. Both lenses were instructed not to re-litigate §§1–7. |
| **External Codex** | invoked 2026-08-06 — c1: **5B / 1W / 2N** · c2: **1B / 1W / 2N** · c3: **0B / 1W** — *"safe to dispatch"* |
| **Internal Reviewer** | invoked 2026-08-06 — c1: **5B / 7W / 4N** · c2: **1B / 7W / 5N** · c3: **0B / 2W / 2N** — **APPROVED WITH WARNINGS** |
| **BLOCKERs resolved before Step 1** | **yes — 11 total, all absorbed, converged at cycle 3.** c1: 10 (5 per lens). c2: both lenses converged on ONE — 12701b's scope could not reach the `PeriodOutcome` vocabulary it owns (`DemoGenerator.cs:442`, `DemoManifest.cs:119-122`, outside `Loading/**`). c3: **zero new BLOCKERs from either lens**; 4 warnings absorbed as one-line scope edits. |

**Step 0b closed 2026-08-06 at cycle 3, both lenses clean.** The single most valuable class of finding
was **declared scopes narrower than the task's own criteria** — it appeared in c1 (four tasks), was
re-created by the c1 restructure at a new seam (c2, one task), and left two residues in c3. The
structural lesson, recorded for future sprints: *the Constraint Validator reads the **declared** scope,
so phase ordering cannot rescue overlapping declarations — narrow every scope to named files, and never
let two concurrent tasks declare the same tree.*

**Cycle-2 note — the same failure mode reappeared at the seam the cycle-1 restructure created.** Both
lenses independently found it, and it is the third instance of "a declared scope narrower than the task's
own criteria". The structural fix applied in cycle 2 is to stop declaring broad scopes and let phase
ordering imply safety: **12700, 12708, 12711 and 12712 now carry disjoint named file lists** instead of
`tests/**`, because the Constraint Validator checks the *declared* scope and could never fire while four
concurrent tasks all declared the same tree.

### Findings (cycle 1) — both lenses BLOCKED

**Convergent BLOCKERs (both lenses):**
- **Agent scopes understate the files their own criteria require.** 12705 had **no `Scope` field at
  all**; 12702's scope excluded the repository test its AC-7(c) criterion demands; 12706's excluded the
  `ApprovalEndpoints.cs` comment it must amend; 12701's excluded the S116A fixture it owns. Cross-domain
  authorization is controlled by the **explicit file scope, not the label** (`AGENTS.md:48,53`), and
  Constraint Validator check 7 fires on every out-of-scope file.
- **The PAT-012 pipeline outputs were owned by nobody.** Retiring `/submit` changes
  `docs/api/openapi.json` and `frontend/src/lib/api-types.ts`; no agent may write under `docs/`
  (`CLAUDE.md:87`) and two CI gates check exactly those files (`check_openapi_sync.py`). Precedent
  exists and is now cited verbatim: **PAT-012 line 68** — *"the backend drain agent is cross-domain
  AUTHORIZED for the pipeline outputs `docs/api/openapi.json` + the generated FE `api-types.ts` — no
  other docs/ or frontend/ file"* (back-annotated at the S119 Step-0b review).
- **AC ownership was neither exact nor complete** — AC-1/4/5/6 unowned; AC-2/12/13 doubly owned;
  AC-8/9/10/11/15 had an implementation owner but no verification owner.
- **The three R1-reversal tests** named in refinement §3.7 were in no task.

**Codex-only BLOCKERs:**
- **The formal dependency table did not serialize shared files.** 12705 depended only on 12700 while
  both it and 12703 edit `ApprovalEndpoints.cs`; 12706 (Phase 1) had to edit that same file while Phase
  2 could start as soon as 12702 finished. **Worktree isolation prevents local overwrite, not merge
  conflicts or stale-base edits.**
- **TASK-12701 spanned two dependency stages and could not complete in Phase 1** — its loader
  verification must replace the live `/submit` call at `DemoLoader.cs:502`, which needs `/send` from
  12703.
- **TASK-12700 assigned to Test & QA contradicts that agent's own constraint** — *"Must run AFTER all
  implementation agents complete"* (`AGENTS.md:34`).

**Internal-only BLOCKER:**
- **TASK-12700's criteria were satisfiable without doing the work.** Encodings 1–3 are inline
  expressions inside three endpoint handlers — there is no callable function — so an agent would write a
  table re-implementing the arithmetic, which passes trivially. That is the exact defect 12705 exists to
  fix in `AllocationGateTests`.

**Adopted WARNINGs:** 12708 split three ways (too broad); the STY01 seed rows land inside 12701a so the
E2E is not gated on the DemoSeed generator; `GetByIdAsync` disambiguated (12703 re-resolves by natural
key inside the lock, so no new overload is needed).

**Both lenses NOTE the `init.sql` carve-out is legitimate** — `CLAUDE.md:82` protects init.sql *schema*;
the `projects` table already exists (`:1064`) and the seed rows are separate (`:1147`). Scope is phrased
as the exact seed block, no DDL.

## ⚠ LINE CITATIONS DRIFT AS THIS SPRINT PROCEEDS — VERIFY, DO NOT TRUST

Every `file:line` in this plan and in the refinement was accurate **at sprint start**. Phase 1 moved
them: TASK-12702 alone added 157 lines to `ApprovalPeriodRepository.cs`, and TASK-12703 restructured
`ApprovalEndpoints.cs`. TASK-12703 found **four** already stale and used the correct ones:

| Cited in the refinement | Actual, post-Phase-1 |
|---|---|
| overlap join `ApprovalPeriodRepository.cs:448-451` | **`:493-495`** |
| month-guard read surfaces `:1079` / `:1150` | **`:657-659`** / **`:1150-1152`** |
| the S124 rationale comment `ApprovalEndpoints.cs:1077-1078` | **~`:1018-1050`** |

**Every agent from Phase 2 onward: treat all line numbers here as hints, locate the construct by name or
content, and report any citation you found stale.** A task that edits by line number will edit the wrong
line. Symbol names, SQL fragments and comment text are stable; line numbers are not.

## Task Decomposition (post-0b restructure)

**Merge discipline (0b BLOCKER):** worktree isolation prevents local overwrite, **not** stale-base
edits. **Every successor task starts from the MERGED predecessor commit**, not from the sprint base.
The Orchestrator merges each phase before dispatching the next.

**Time-ordered constraints that cannot be recovered if missed:**
- **12700 before 12705** — a baseline captured after consolidation compares the shared predicate to
  itself and is worthless (AC-2).
- **C1 (contract regeneration) between 12703 and 12707** — the frontend cannot regenerate `api-types.ts`
  from a spec that has not been regenerated.

| Task | Title | Agent | Depends on | Owns AC |
|------|-------|-------|-----------|---------|
| 12700 | AC-2 characterization baseline | Test & QA ⚠ *documented exception* | — | AC-2 (capture) |
| 12701a | Seeding — structural | Backend tooling (cross-domain authorized) | — | AC-14(a) |
| 12702 | Repository primitives | Data Model (extended into Infrastructure, cross-domain authorized) | — | AC-7(c) |
| 12706 | R1 visibility predicate | Backend API (cross-domain authorized) | — | — |
| 12703 | Shared send command + both adapters | Backend API (cross-domain authorized) | 12702 | — |
| 12704 | Lock enrolments | Backend API (cross-domain authorized) | 12702 | — |
| **C1** | **Contract regeneration checkpoint** | **Orchestrator** | 12703 | — |
| 12705 | Predicate consolidation | Backend API (cross-domain authorized) | **12700, 12703** | AC-1, AC-2 (verify) |
| 12707 | Frontend | UX | 12703, **C1**, 12706 | AC-15 |
| 12701b | Seeding — loader/verifier conversion | Backend tooling (cross-domain authorized) | 12701a, **12703** | AC-14(b) |
| 12708 | Concurrency + atomicity suites | Test & QA | 12703, 12704 | AC-7(a,b,d,e), AC-17 |
| 12711 | Command behaviour matrix | Test & QA | 12703, 12705 | AC-3, 4, 5, 6, 8, 9, 10, 11, 12, 18 |
| 12712 | Compatibility + visibility rebuilds | Test & QA | 12701a, 12703, 12706 | AC-13 |
| 12709 | E2E rebuild | UX | 12701a, 12707 | AC-16 |
| 12710 | Docs | Orchestrator only | all | AC-19 |

**Phases** — 1: 12700, 12701a, 12702, 12706 · 2: 12703, 12704 · **C1** · 3: 12705, 12707, 12701b ·
4: 12708, 12711, 12712 · 5: 12709 · 6: 12710.

**Collision audit after restructure** — every pair that shares a file is now serialized by a formal
dependency, not by phase prose:

| File | Writer(s) — exact | Serialized by |
|---|---|---|
| `ApprovalEndpoints.cs` | 12703 → 12705 | 12705 dep 12703 |
| `ApprovalVisibility.cs` | 12706 **only** | sole writer (the `:27` citation sweep folded in) |
| `S116ApprovalSpecRuntimeTests.cs` | 12701a (SeedAsync project rows) → 12712 (op tests) | 12712 dep 12701a; disjoint regions |
| `AllocationGateTests.cs` | 12700 → 12705 | 12705 dep 12700 |
| `api-types.ts` | **12703 writes**; C1 verifies; 12707 scope-excluded | 12707 dep C1 |
| `DemoGenerator.cs`, `DemoManifest.cs`, `DemoLoader.cs` | 12701a → 12701b | 12701b dep 12701a |

**If C1 finds `api-types.ts` does not match `npm run gen:api`**, 12703 has already merged and 12707 is
scope-excluded from that file — **the Orchestrator regenerates it at the checkpoint.**

⚠ **Expected-RED window — CORRECTED by TASK-12706's census (2026-08-06).** The plan predicted three red
tests. **Only ONE goes red**, and the correction matters more than the count:

- **RED from the Phase-1 merge until Phase 4:** `TeamOverviewAggregateTests.cs:522` (the
  `[InlineData("REJECTED")]` arm). Do not chase it; do not loosen it.
- **STAY GREEN, and are stale:** `TeamOversigt.test.tsx:216` and `TeamRowDetail.test.tsx:628`. They are
  **hermetic** — `vi.stubGlobal('fetch', mockFetch)` (`:36`) — so a backend `.cs` change cannot reach
  them, and their fixtures **hardcode the withheld field** (`:75` pins `normRegistered: 100` on the
  REJECTED row). They will keep asserting behaviour that no longer exists and **will never self-report.**

> **This is the FE-mock-masks-backend-shape class** (S97→S99→S100 — the recurring failure that motivated
> the PAT-012 typed-contract program). **Green is not evidence here.** 12707 owns inverting them and must
> treat them as stale-by-construction, not as passing.

**Useful for 12707:** the frontend needs **no logic change**. `TeamOversigt.tsx:810` computes
`canExpand = row.normRegistered !== null` — keyed on the withheld field, not a status literal — so
production behaviour follows the backend automatically. Fixtures only.

---

### TASK-12700 — AC-2 characterization baseline
**Agent**: Test & QA · **Scope**: `tests/StatsTid.Tests.Regression/Outbox/AllocationGateTests.cs` and
**one new** characterization file beside it — narrowed from `tests/**` so the Constraint Validator can
actually fire (0b cycle 2, W3: 12700's old scope strictly contained 12702's and 12701a's files with no
dependency between them)

> ⚠ **Documented exception to `AGENTS.md:37`** ("Test & QA must run AFTER all implementation agents").
> This is a *characterization capture of existing behaviour* that is worthless after the code changes,
> so that constraint's own rationale does not reach it. Orchestrator-authorized, single-task, AC-scoped.
> **The final diff contains zero production files.** The discriminator below requires temporarily
> inverting production comparisons — those edits are **reverted before submission**, and the agent must
> **copy the file to the scratchpad first and restore from that copy**, never `git checkout -- <file>`
> (standing lesson: that restores HEAD and destroys any uncommitted work in the file).

Capture a value table with **independently stated expected verdicts** and run it against allocation
encodings 1–3 as they exist today.

**Validation criteria**
- **Falsifiability discriminator (0b BLOCKER):** the baseline must go **RED under THREE independent
  inversions — one per encoding** (`:1488` the gate, `:1109` team-overview, `:1284`
  allocation-breakdown). Verify each separately before submitting; inverting only the gate satisfies a
  singular reading and leaves two encodings unguarded (0b cycle 2, W2). A table that re-implements
  `Math.Abs(round(w,2)-round(a,2))` passes trivially and is worthless — that is the defect 12705 exists
  to fix in `AllocationGateTests`.
- **Fixture note:** exercising both verdicts at `:1488` needs an allocated (non-null `TaskId`) entry,
  i.e. a project row. Create your **own** fixture project — do **not** wait on or reuse 12701a's seeding;
  they are deliberately different fixtures and there is no dependency between the tasks.
- The three encodings are **inline expressions with no callable function**
  (`ApprovalEndpoints.cs:1488` gate, `:1109` team-overview, `:1284` allocation-breakdown), so the
  baseline must drive them through their **three HTTP surfaces**. Note the gate at `:1488` is
  unreachable until coverage passes — the fixture must satisfy coverage first.
- Expected verdicts stated independently, not read from the implementation
- Covers `0.00`, `0.01`, rounding-noise pairs (`7.40` vs `7.4`)
- **Documents its own limit**: `|Δ| == 0.005` is unreachable after 2dp rounding, so this cannot
  discriminate the `<`/`>` strictness split

---

### TASK-12701a — Seeding (structural)
**Agent**: Backend tooling (cross-domain authorized)
**Scope**: `tools/StatsTid.DemoSeed/**`, `tests/StatsTid.Tests.DemoSeed/**`,
`tests/StatsTid.Tests.Regression/Contracts/S116ApprovalSpecRuntimeTests.cs` (**fixture org project rows
only** — `:471`), and the **`INSERT INTO projects` block at `docker/postgres/init.sql:1149` only** —
no DDL, no other seed block, no migration ledger

DemoSeed has **no project model at all** — zero `Project` occurrences in `SqlEmitter.cs` (ends after role
rows, `:128`) or `StructuralModels.cs` (`DemoDataset` has no project collection, `:84`).

**Validation criteria (AC-14a)**
- Every organisation with active employees has ≥1 active project — `init.sql` baseline, the DemoSeed
  world, **and the S116A fixture orgs**
- The `init.sql` STY01 rows land here — that is all TASK-12709 needs, so the E2E is **not** gated on the
  DemoSeed generator
- `GoldenLegacyPinTests` goldens **deliberately regenerated** (project emission changes byte-exact SQL);
  regeneration, never a loosened assertion
- DemoSeed generates coverage-complete, per-day-balanced months
- **Does NOT touch `DemoLoader`'s `/submit` call** — that is 12701b, and it needs `/send` to exist

---

### TASK-12702 — Repository primitives
**Agent**: Data Model (extended into Infrastructure, cross-domain authorized)
**Scope**: `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs`,
`tests/StatsTid.Tests.Regression/Infrastructure/**`

1. `TryCreateIfAbsentAsync(conn, tx, period, ct) -> Guid?` —
   `INSERT … ON CONFLICT (employee_id, period_start, period_end) DO NOTHING RETURNING period_id`.
   **Copy the proven pattern at `PayrollExportService.cs:278-312`.** `CreateAsync` returns the
   client-generated `Guid.NewGuid()` and cannot express "someone else won".
2. A `(conn, tx)` overload of `GetByEmployeeAndPeriodAsync` — it opens its own connection today (`:65`).
   This natural-key read serves **both** the `/send` existence read and the Skema re-read; **no
   `GetByIdAsync` overload is needed** — 12703 re-resolves by natural key inside the lock.
3. A **follow-up UPDATE** helper writing `submitted_at`, `submitted_by`, `org_id`, `agreement_code`,
   `ok_version` by `period_id`. Do **not** extend the `status switch` (`:1553-1561`) — that is what
   keeps the reopen `DRAFT` null path intact.

**Validation criteria (AC-7c)**
- Two calls on the same natural key with **different candidate ids**: first returns its id, second
  returns **null**, row count stays one, the original id survives, the transaction remains usable
- The follow-up UPDATE needs no source-state guard (the conditional statement holds the row `FOR UPDATE`
  to end-of-transaction) — assert safe, do not re-guard

---

### TASK-12706 — R1 visibility predicate
**Agent**: Backend API (cross-domain authorized)
**Scope**: `src/Backend/StatsTid.Backend.Api/ApprovalVisibility.cs` **only**

Remove `REJECTED` from `IsSubmittedToManager` (`:30`). Two callers exist
(`ApprovalEndpoints.cs:1079`, `SkemaEndpoints.cs:515`) — **neither is edited here**; the
`ApprovalEndpoints.cs` rationale amendment moved into **12703** to remove the file collision.

**Also fold in here** (0b cycle 2, W5 — it is the only task already in this file): sweep
`ApprovalVisibility.cs:27`'s stale `init.sql:1103` citation — the status CHECK is at `:1118-1119`.
Removed from 12710, which is now docs-only.

**Validation criteria** — the predicate change only; behavioural verification is 12712 (AC-13).
**Scope limit (R5)**: the sibling read endpoints are **NOT** closed. `RES-002` follow-up.
⚠ Three tests go **expected-RED** on this merge until 12712 — see the Expected-RED window above.

---

### TASK-12703 — The shared send command + both route adapters
**Agent**: Backend API (cross-domain authorized)
**Scope**: `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`,
`src/Backend/StatsTid.Backend.Api/Contracts/**`, **and the PAT-012 pipeline outputs
`docs/api/openapi.json` + `frontend/src/lib/api-types.ts`** — authorized verbatim per **PAT-012 line
68**: *"the backend drain agent is cross-domain AUTHORIZED for the pipeline outputs … no other docs/ or
frontend/ file."* **No other `docs/` or `frontend/` file.**

Retire `POST /api/approval/submit`. Add `POST /api/approval/send { employeeId, year, month }`.
`employee-approve` becomes the **second adapter over the same command**.

Choreography: adapter pre-read (by-id reads `employee_id` for the lock key — immutable, no drift guard;
404 if absent) → tx with **explicit `IsolationLevel.ReadCommitted`** → `EmployeeConsumptionLock` first →
authoritative re-read in-lock **by natural key** → role floor → coverage then allocation → conditional
transition → follow-up UPDATE → event + audit.

**Validation criteria**
- `allowedSourceStates = {DRAFT, SUBMITTED, REJECTED}`; by-id arm swaps off the unconditional
  `UpdateStatusAsync` (`:1514`)
- Whole-month guard (409). **Boundary check only, not a `period_type` check** — a `WEEKLY` row spanning
  an exact month is accepted
- Role floor `self ? null : StatsTidRoles.LocalHR` (R4) — leader-for-another ⇒ **403**; LocalLeader
  self-send passes
- `agreementCode = GetByUserIdAtAsync(employeeId, monthStart) ?? user.AgreementCode`;
  `okVersion = OkVersionResolver.ResolveVersion(monthStart)` — **on both arms**, carried by the follow-up
  UPDATE, so a legacy row with wrong caller-supplied values is corrected on re-send
- Emit **`PeriodEmployeeApproved` only**; do **not** add `PeriodType`; audit `action` stays the literal
  `"SUBMITTED"` (`init.sql:903` has no `EMPLOYEE_APPROVED` member)
- **Both** adapters stamp `submitted_at`/`submitted_by`, including a reopen→re-send
- **Never** pass an `ApprovalAuthorityContext` on this transaction (`DesignatedApproverAuthorizer.cs:465`
  throws unless RepeatableRead)
- **Amend the S124 rationale comment at `ApprovalEndpoints.cs:1077-1078`** — quote and answer it; R1
  overrides it, but it must not be silently deleted (moved here from 12706)
- Regenerate `docs/api/openapi.json` and `api-types.ts`

---

### TASK-12704 — Lock enrolments
**Agent**: Backend API (cross-domain authorized)
**Scope**: `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`, `SkemaEndpoints.cs`

- `POST /api/time-entries` acquires `EmployeeConsumptionLock` as the **first statement** of its
  transaction (tx opens `:121-124`, first SQL `:127`)
- Skema's write tx **re-reads the exact month's approval status inside the transaction**, after the lock,
  before any write, returning the **409** its pre-tx conflict path already returns (`:681`)

**Validation criteria** — no second advisory lock; `ProjectionBackfillService` is **NOT** enrolled
(stated exception, refinement §3.4)

---

### **C1 — Contract regeneration checkpoint** (Orchestrator)
After 12703 merges, before 12707 dispatches: verify `docs/api/openapi.json` regenerated, the drift gate
(`tools/check_openapi_sync.py`) green, and `api-types.ts` matches `npm run gen:api`. **12707 cannot
start until this passes** — it regenerates nothing itself and would build against a stale spec.

---

### TASK-12705 — Allocation-predicate consolidation
**Agent**: Backend API (cross-domain authorized)
**Scope**: `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`, the new shared-predicate
file under `src/Backend/StatsTid.Backend.Api/`, `tests/StatsTid.Tests.Regression/Outbox/AllocationGateTests.cs`,
and **one new** AC-1 static-check file named `*ToleranceAllowList*` under
`tests/StatsTid.Tests.Regression/` (0b cycle 3: "under `tests/`" was the last un-narrowed scope)

Five encodings → one. `lib/allocation.ts:26` **stays** (ADR-028 D4). `SkemaGrid.tsx:328` is **NOT** an
encoding — it is `absenceOverNorm`, a different rule borrowing the tolerance value; **leave it alone**.

**Validation criteria (AC-1, AC-2 verify)**
- A **pure per-day predicate separated from data loading** — `hasWarning` and the breakdown work over
  month-wide roster-batched dictionaries; a naive extraction N+1s them (S125/F1)
- `AllocationGateTests` **calls the endpoint** instead of re-implementing the algorithm
- TASK-12700's baseline reproduces exactly
- **AC-1 static check** over code-bearing files against the allow-list. ⚠ A literal
  `AllocationTolerance|ALLOCATION_TOLERANCE|0.005` sweep returns ~7 further hits that are **comments**
  (`ApprovalEndpoints.cs:23`, `:1160`, `SkemaDayPanel.tsx:9`, `SkemaGrid.tsx:26`, `useSkema.ts:440`,
  `allocation.ts:7`, `allocation.test.ts:9`) — the check needs a comment-stripping rule or an extended
  allow-list

---

### TASK-12707 — Frontend
**Agent**: UX · **Scope**: `frontend/src/**` (excluding `api-types.ts`, produced by C1)

- `useSkema.submitAndApprove` collapses to **one** call; the 422 parse moves onto it (`:321` calls
  `setError` with no parse today; the parse lives only on the approve leg `:331-340`)
- *Mine perioder*: the free-range send form **removed** (R3) with its state and "Periode indsendt.";
  the list and re-send button stay
- **`MyPeriods.tsx` `statusBadgeClass` (`:23-31`) and `statusLabel` (`:33-41`) gain `EMPLOYEE_APPROVED`
  cases** — both fall to `default: return status` today, so every newly sent period would render the raw
  enum to the employee. A live bug this sprint would otherwise introduce.
- **Invert the two R1-reversal FE tests** (0b BLOCKER — previously unowned):
  `__tests__/TeamOversigt.test.tsx:216`, `__tests__/TeamRowDetail.test.tsx:628`.
  ⚠ **These will be GREEN when you start, and green is not evidence.** They are hermetic
  (`vi.stubGlobal('fetch', mockFetch)`) and their fixtures hardcode the now-withheld field
  (`TeamOversigt.test.tsx:75` pins `normRegistered: 100` on the REJECTED row; `TeamRowDetail`'s `row()`
  helper defaults `normRegistered: 140` and the `:628` case overrides only `status`/`rejectionReason`).
  They assert behaviour that no longer exists and cannot self-report. **No production logic change is
  needed** — `TeamOversigt.tsx:810` keys `canExpand` on the withheld field, so the UI already follows the
  backend. Fixtures only.
- Fix `api-typed-overloads.test.ts:442` — `/api/approval/submit` is a **type-level** union member, so
  this is a `tsc` compile break, not a test failure
- **The rest of the FE blast radius, named (0b cycle 2, W4 — the plan previously read as exhaustive and
  was not):** `pages/__tests__/SkemaPage.test.tsx:287` (mocks `/submit`);
  `pages/approval/__tests__/MyPeriods.test.tsx` (an entire file built around the `/submit` orgId body);
  `hooks/__tests__/approvalTypedWire.test.ts:233,258` (asserts `posts[0].url === '/api/approval/submit'`);
  and **`pages/SkemaPage.tsx:1107`** — a **production** grid-unlock mirror
  (`status === 'DRAFT' || status === 'SUBMITTED'`), not a test

---

### TASK-12701b — Seeding (loader/verifier conversion)
**Agent**: Backend tooling (cross-domain authorized) · **Scope**: `tools/StatsTid.DemoSeed/**`
(**broadened, 0b cycle 2 BLOCKER — both lenses**: the vocabulary this task owns is **not** in `Loading/` —
it is `DemoGenerator.cs:442`'s `outcomes` array and `DemoManifest.cs:119-122`. It cannot move to 12701a
either: emitting `EMPLOYEE_APPROVED` in Phase 1 while `DemoLoader.cs:537` still switches on `"SUBMITTED"`
would break the loader for two phases. The vocabulary flip and the loader that consumes it must land
together.)
**Depends on 12701a AND 12703** — it must replace the live `/submit` call at `DemoLoader.cs:502`

**Validation criteria (AC-14b)**
- Loader posts `/send`; the `PeriodOutcome` vocabulary flipped `SUBMITTED` → `EMPLOYEE_APPROVED` at
  **both** its sites (`DemoGenerator.cs:442`, `DemoManifest.cs:119-122`) together with
  `DemoLoader.cs:537`'s switch
- ⚠ `demo-manifest.full.json` is **gitignored** (`.gitignore:49`) — it never appears in a diff, so
  regeneration is a **run-the-tool step**, not a file edit. State it in the task output (FAIL-003 class:
  local disk ≠ committed tree). The DemoSeed golden *manifest* contains zero `SUBMITTED` occurrences, so
  this does not widen 12701a's golden regeneration.
- Verification asserts **manifest-derived exact status counts in the database, on a fresh load AND a
  rerun** — *not* "the loader completed": failures are warnings, warnings do not affect the exit code,
  the program returns 0, and on rerun any 409 is an idempotent skip (`DemoLoader.cs:513`)

---

### TASK-12708 — Concurrency + atomicity suites
**Agent**: Test & QA · **Scope**: `tests/StatsTid.Tests.Regression/Outbox/ApprovalAtomicTests.cs` and
**new** files named `*Concurrency*` / `*SendAtomicity*` under `tests/StatsTid.Tests.Regression/Approval/`
— named exactly, **not `Outbox/**`** (0b cycle 3: that directory holds 26 files including 12700's and
12705's `AllocationGateTests.cs`, so a broad grant would re-open the very hole cycle 2 closed — a
Phase-4 agent could rewrite the consolidated predicate test without check 7 firing)

- **AC-7(a)** two concurrent first sends — barrier is a **third connection polling `pg_locks` for
  `hashtext('employee-'||id)` until two waiters appear**; `Task.WhenAll` alone does not prove overlap
- **AC-7(b)** ⚠ **separate and mandatory** — (a) cannot detect the isolation failure: under REPEATABLE
  READ the loser misses the row, gets null from `ON CONFLICT`, and returns the **same** 409 with the same
  counts. Hold the winner's lock, start the loser, commit the winner, then **directly assert the loser's
  post-lock existence read returns the row before any create attempt**
- **AC-7(d)** send vs Skema save: *"no save commits into a month that is already sent"* — **not** "never
  both committing", which goes red on the correct Skema-wins ordering
- **AC-7(e)** send vs `/api/time-entries`: prove the time-entry request is waiting on the advisory lock
  and no projection row commits inside the send's window
- **AC-17** real-route forced-outbox-rollback through **both** adapters — `ApprovalAtomicTests`
  re-implements the orchestration inline (`:62-80`) and stays green after the endpoint is deleted

---

### TASK-12711 — Command behaviour matrix
**Agent**: Test & QA · **Scope**: **new** files named `*SendCommand*` under
`tests/StatsTid.Tests.Regression/Approval/` — disjoint from 12708 and 12712 (0b cycle 2, W3)

- **AC-3** allocation-gate falsifiability: a month passing coverage with a `work_time_projection` row and
  no allocation ⇒ 422 `kind:"allocation"`. ⚠ `worked` and `allocated` read **different tables** — a
  null-`TaskId` `NORMAL` entry alone lands in neither map and returns **200**. **Verify by removing the
  allocation validator and seeing this go red** — that temporary edit to `ApprovalEndpoints.cs` is
  **authorized under the same terms as 12700** (0b cycle 3): reverted before submission, **restored from
  a scratchpad copy, never `git checkout -- <file>`**, and the final diff contains **zero production
  files**. Without this clause the agent either skips the falsification — leaving AC-3 an assertion never
  shown capable of failing, this sprint's own headline failure class — or edits out of scope and trips
  the validator.
- **AC-4** rejection writes nothing, **per arm**: transition ⇒ every column unchanged (deadlines, both
  timestamp pairs, the three dimensions); create ⇒ **no row exists**. Neither writes audit or outbox
- **AC-5** no stranding × 4 source states (no row / `DRAFT` / `REJECTED` / **`SUBMITTED`**), each with
  source state unchanged **and** a corrected retry succeeding
- **AC-6** vacuous case — absence-covered month for a project-less employee, **asserting zero work-time
  rows** on those days
- **AC-8** one outbox event, one audit row with the literal `"SUBMITTED"` action, one audit_projection
  row, both timestamp pairs non-NULL from **both** adapters incl. reopen→re-send
- **AC-9** `SUBMITTED` retired on production routes (scoped — the repo SET branch stays, still exercised
  by `TxContractTests.cs:380`); already-`EMPLOYEE_APPROVED` ⇒ 409; deadlines non-NULL
- **AC-10** whole-month guard both paths; a `WEEKLY` row spanning an exact month is **accepted**
- **AC-11** R2+R4 authorization matrix, five roles, **both adapters**; leader-for-another ⇒ 403, not 422
- **AC-12** per-arm dimensions: create records `OK24` for a March month sent in April; transition
  **corrects** a row seeded with deliberately wrong `org_id`/`agreement_code`/`ok_version`
- **AC-18** a seeded `SUBMITTED` row failing allocation **remains manager-approvable** — recorded as
  intended per **R6**

---

### TASK-12712 — Compatibility + visibility rebuilds
**Agent**: Test & QA · **Scope**: the **named existing suites only** —
`Contracts/S116ApprovalSpecRuntimeTests.cs` (op tests `:333-400` + the e5 pins; **not** 12701a's
`SeedAsync` project rows), `Contracts/S120SkemaSpecRuntimeTests.cs`,
`Approval/TeamOverviewAggregateTests.cs`, `Performance/S106SeedScalePerfTests.cs`,
`Performance/S106SeedScalePerfFixture.cs`, `Approval/MedarbejderRosterReadTests.cs`,
`Approval/S106RosterUnitTagTests.cs` — **directory-prefixed** (0b cycle 3: the two S106 perf files live
under `Performance/`, not `Approval/`) — disjoint from 12708 and 12711 (0b cycle 2,
W3) · **Depends on 12701a** (co-edits `S116ApprovalSpecRuntimeTests.cs`, disjoint regions, starts from
the merged commit)

- **AC-13** `REJECTED` withheld at the two display surfaces; **companion test asserting the siblings
  still disclose** — recognizable non-zero sentinel figures, not merely a non-403 — plus
  `TeamOverviewAggregateTests.cs:514` inverted
- S116: five op tests change (`:333-343` delete/re-mint, `:359-369`, `:388-400` setups, `:349`/`:377`
  source states); the e4 populated fixture **cannot** move to `/send` (asserts `SUBMITTED`+`WEEKLY`); the
  e5 pending-null pins (`:102-108`, `:143`) **deleted**, the DRAFT-null at `:291` stays
- S120 `:170-202` calls the retired route and asserts literal `SUBMITTED`
- `S106SeedScalePerfTests.cs:383-389,468-470` assert `Status == "SUBMITTED"`
- The four non-whole-month fixtures (`MedarbejderRosterReadTests.cs:198`, `S106RosterUnitTagTests.cs:265`,
  `S106SeedScalePerfFixture.cs:264,416`)

---

### TASK-12709 — E2E rebuild
**Agent**: UX · **Scope**: `frontend/e2e/**` · **Depends on 12701a** (STY01 projects), **12707**

R3 removed the form `approval.spec.ts` builds its fixture with (`submitPeriodViaForm:59-84`).

**Validation criteria (AC-16)** — drive the real Skema page: register hours on **every expected
weekday**, allocate to a real project, send, drive the manager outcome. No single-day overlap trick
(that fixture *is* defect 3); no API/DB shortcut past the surface under test.

---

### TASK-12713 — Promote the rejection reason to row level (owner ruling R7)
**Agent**: UX · **Scope**: `frontend/src/pages/approval/TeamOversigt.tsx`,
`frontend/src/pages/approval/__tests__/TeamOversigt.test.tsx`,
`frontend/src/pages/approval/TeamOversigt.module.css` · **Added mid-sprint 2026-08-06**

Surface `rejectionReason` on the team-overview **row**, reachable without expanding, for `REJECTED` rows.

**Validation criteria**
- The reason is visible on a `REJECTED` row with the panel **closed** — `canExpand` stays keyed on the
  withheld figure and is **not** relaxed (that would partially undo R1)
- **No withheld figure is re-exposed**: the five month-derived fields stay absent for `REJECTED`
- The now-redundant in-panel render site is removed or made unreachable-by-construction — do not leave
  two render sites for one field (S91 dead-affordance discipline)
- A test asserts the reason is present with the row collapsed, and **falsify it**: with the promotion
  reverted the test must go red
- `tsc --noEmit`, `lint`, and `vitest` all green

### TASK-12710 — Docs
**Agent**: Orchestrator only · **Scope**: `docs/**` only — **docs-only** (0b cycle 2, W5: the
`ApprovalVisibility.cs:27` citation sweep moved to 12706, which is already in that file) · **Owns AC-19**

ADR-012 + ADR-028 as-built (sweep `ADR-012:49`'s stale "returns 403" — code returns 409); ADR-032/ADR-038
lock-order note naming the two out-of-order `approval_periods` row-lockers (`PayrollExportService.cs:262`,
the employee arm of reopen); `RES-002` + R5's recorded gap; DEP-004, audit-projection catalog and
**caller census** for the emitter change and the **replay-only parity disposition**
(`AuditProjectionParityTests` pins `TBD-defined-but-unemitted` at **zero**); the `ProjectionBackfill`
runbook quiescence requirement; `docs/FRONTEND.md`; `SYSTEM_TARGET.md`.
(The `ApprovalVisibility.cs:27` citation sweep moved to **12706** — this task touches no `src/` file.)

---

## Known-accepted holes (do not "fix" these)

| Hole | Ruling | Pinned by |
|------|--------|-----------|
| Legacy `SUBMITTED` rows are manager-approvable without validation | **R6** | AC-18 (12711) |
| Sibling read endpoints still disclose a rejected month's figures | **R5** | AC-13 companion (12712) |
| `ProjectionBackfillService` writes projections unlocked | §3.4 exception | Runbook (12710) |
| `/api/time-entries` has no approval-status check (post-send drift) | §4 carried | — |

## Tasks Completed

### TASK-12711 — Command behaviour matrix ✅ 2026-08-07 · **owns AC-3,4,5,6,8,9,10,11,12,18**
**Agent**: Test & QA (main tree). **Files**: NEW `Approval/SendCommandMatrixFixture.cs` + `SendCommandBehaviourTests.cs` + `SendCommandNoWriteTests.cs` + `SendCommandAuthorizationTests.cs`. **46/46 green**, Orchestrator-reverified.
AC-3 falsified by removing the allocation validator (RED→restored from a scratchpad backup, not `git checkout`); fixtures built so only the rule under test can 422 (absence covers coverage, a null-`TaskId` entry lands in neither map); AC-11/R2 pins admitted-then-422 (not 403); AC-12 `OK24` falsifies a resolve-at-today bug. Empty production diff, no stale citations, no production defects.

### TASK-12708 — Concurrency + atomicity suites ✅ 2026-08-07 · **owns AC-7(a,b,d,e), AC-17**
**Agent**: Test & QA (main tree). **Files**: NEW `Approval/SendConcurrencyTests.cs` (AC-7a/b-RC/b-RR/d/e), NEW `Approval/SendAtomicityTests.cs` (AC-17 both adapters); rebuilt `Outbox/ApprovalAtomicTests.cs` (the two stale inline `/submit` mirrors — green-after-endpoint-deletion, the exact failure class — removed; 3 live-endpoint tests kept). **10/10 green**, Orchestrator-reverified.
Real forcing, not observation: AC-7a/d/e use a THIRD connection holding `pg_advisory_xact_lock` + a `pg_locks` waiter-poll that times out loud (`Task.WhenAll` is not overlap); AC-7b is a real-primitive protocol test whose RepeatableRead branch (`Assert.Null`) is the falsifier for the ReadCommitted branch; AC-17 drives the real routes so a deleted endpoint 404s red.
⚠ **Worktree base hazard:** two worktree agents mis-branched from `feec8a6` (pre-S127) instead of the local WIP checkpoint — the worktree base did NOT reliably track an unpushed local commit. Both self-detected via a base-guard in the prompt and wrote nothing; re-run in the MAIN tree (reliable base). **Lesson: run Test & QA agents in the main tree, not worktrees, when the sprint base is an unpushed local checkpoint.**

### TASK-12712 — Compatibility + visibility rebuilds ✅ 2026-08-07 · **owns AC-13**
AC-13 done+green (the visibility half): `RejectedMonthVisibilityTests` both arms — withheld at the two display surfaces + `_R5Gap` confirms the siblings still disclose; `TeamOverviewAggregateTests` inversion; the S116/S120 op-test conversions to `/send`. **Fixture ruling (Orchestrator):** the four still-`SUBMITTED` / non-whole-month READ-side fixtures (`S106SeedScalePerfTests` + `S106SeedScalePerfFixture`, `MedarbejderRosterReadTests`, `S106RosterUnitTagTests`) are **RETAINED, not converted** — they verify roster composition / perf scaling / tile counts (orthogonal to the period status literal), their `SUBMITTED` seeds are genuine legacy read-coverage (R6/AC-9 deliberately retain legacy handling), and the `SUBMITTED`/`EMPLOYEE_APPROVED`→display-`SUBMITTED` mapping (`RosterContracts.cs`) is UNCHANGED by S127. Converting would lose legacy coverage; adding an `EMPLOYEE_APPROVED` case needs fixture surgery (the tree employees are pinned to specific status/role cases) and the new-vocab display path is already covered end-to-end by the 12711 suite.

### Step-7a absorption — test falsifiability strengthening ✅ 2026-08-07
Codex (external lens) found SIX falsifiability gaps the internal Reviewer cleared — the lens-complementarity earning its keep. A Test & QA agent verified + strengthened each (**54/54 green**, empty production diff, each proven RED-under-regression then restored):
- **F5** `AC7b` overclaimed production isolation (it composed the real primitives at an isolation it *passed in*, never invoking production's tx-open). Resolved: the ROUTE-level `AC7a` is documented as the real production-RC pin — flipping production to RepeatableRead makes AC7a go RED (the RR loser misses the winner's row → create arm → `ON CONFLICT` raises `40001` → `[200,500]` not `[200,409]`) — and `AC7b` is honestly reframed/renamed as a *mechanism* demonstration with its limit documented.
- **F6** the "exactly one event/projection" counts were filtered by expected type → now count TOTAL outbox events / audit_projection rows (a spurious other-type event is caught).
- **F7** the create-arm rollback test never queried `approval_audit` (no FK) → now asserts zero orphan audit rows.
- **F8** the AC-12 fixture had live `agreement_code` == dated-at-month → now live≠dated, so a regression to `user.AgreementCode` fails.
- **F9** R2's unbalanced-month case ran only via `/send` → now also through the by-id adapter.
- **F10** the AC-4 "every column unchanged" snapshot omitted `designated_approver_id`/`approval_method` → snapshot expanded.

### TASK-12709 — E2E rebuild ✅ 2026-08-06 (written, **not executed** — see below) · **owns AC-16**
**Agent**: UX · `approval.spec.ts` 161 → 474 lines, one test · `tsc --noEmit` clean · `playwright --list` discovers it

The honest end-to-end this sprint owed. It drives the **real Skema page**: for every Mon–Fri of a chosen
month it opens the day panel, enters `08:00`–`15:24`, asserts the panel's own `7,4 t` readout, allocates
to **`DRIFT-01`** (the STY01 project 12701a seeded), and asserts **`Alt fordelt ✓`** — the FE mirror of
the backend gate. Nothing seeded via API or DB. Then: send (asserting **exactly one**
`POST /api/approval/send` → 200, the grid going read-only, the footer reading "Afventer leder
godkendelse"), mgr03 **rejects**, emp001 **re-sends through the by-id adapter**, mgr03 **approves**. Both
manager verbs and **both send adapters** over one month's real registration.

It also asserts **R1 and R7 at the same moment**: after rejection the row-level strip `team-rejection-emp001`
carries the reason **while `team-detail-row-emp001` is absent** — proving the reason is reachable *and*
that `canExpand` was not relaxed — and Normtimer shows the em dash.

**Determinism**: identity moved from a day to a `(employee, year, month)` triple, so there is no day left
to rotate. A nonce picks 1-of-12 months and `findUnusedMonth` walks forward. The probe is deliberately
**not** load-bearing — the authoritative check is that the send returns 200, so a wrong month fails loudly.

⚠ **NOT EXECUTED, and the reason is correct.** The running stack **predates this sprint**: `/submit`
still answers 401 while it is deleted from source, `/send` 405s, and `STY01` has **0 projects** because
the volume predates 12701a's `init.sql` (which only runs on a fresh volume). Executing would require
`up -d --build` against mid-sprint source *and* `down -v`, destroying the owner's demo data — both
outside `frontend/e2e/**` and destructive to shared state. **It reported the spec as written-but-
unexecuted rather than claiming a run it did not observe.** CI's `e2e-tests` job builds fresh, so it will
be genuinely exercised there. **Carry to Step 5/7a: this AC is unproven until CI runs it.**

**It found a live cross-spec hazard and fixed it structurally.** `skema-registration.spec.ts` mutates the
same employee in parallel and carried a *written proof* of non-collision that depended on the **narrow**
fixture key — *"the lock keys on the exact span, and a one-day period is never found by a whole-month
save."* Widening this fixture from one day to one month made that argument **false**: a whole-month period
is exactly what the save's lock lookup finds. Nothing failed to compile; the comment still read as sound.
Fixed by making the separation **disjoint by construction** (approval takes a window 19 months out) rather
than by argument.

> **Lesson, recorded here because it will bite the next fixture change:** *widening a shared-fixture key
> obliges you to **re-derive**, not re-read, every recorded "these cannot collide" argument over the same
> subject — and prefer disjoint-by-construction key spaces to arguments about timing or nonces.*

**⚠ CI GAP FOUND (Orchestrator-verified): `frontend/tsconfig.json` has `include: ["src"]`, so
`npx tsc --noEmit` — the project gate *and* CI's typecheck — does not cover `frontend/e2e/**` at all.**
The agent typechecked the specs by explicit invocation; **nothing in CI does.** → follow-up item.

**Stale citations (5)**, incl. one substantive: the old spec claimed the Teamoversigt stepper "initialises
to the current **UTC** month" — it uses **local** `new Date()`. Also reported-not-fixed:
`skema-registration.spec.ts:67-72` lists lowercase Danish months where `DANISH_MONTHS` is Capitalized — it
passes only because role-name matching is case-insensitive.

**KB proposal APPROVED** → [PAT-018](../knowledge-base/patterns/PAT-018-negative-space-labels-defeat-substring-matching.md)
(the second proposal is recorded as the lesson block above).

### TASK-12701b — Seeding (loader/verifier conversion) ✅ 2026-08-06 · **owns AC-14(b)**
**Agent**: Backend tooling · Build 0 errors · `StatsTid.Tests.DemoSeed` **94/94** · 6 files, all in scope

# 🎯 **THE SPRINT'S HEADLINE PRECONDITION IS NOW MEASURED, NOT ASSERTED: 375 → 1 became 375 → 375.**

The loader posts `POST /api/approval/send` with exactly `{employeeId, year, month}`. The `PeriodOutcome`
vocabulary flipped `SUBMITTED` → `EMPLOYEE_APPROVED` at **four** sites in one change (generator array,
manifest property + doc, loader switch, and a new `ExpectedPeriodStatus` map). The array **length** is
unchanged, so `_rng.Next(outcomes.Length)` draws identical indices and no other manifest field moves — a
pre-S127 manifest still loads, and an unknown outcome **throws** rather than defaulting to "no expectation".

**AC-14(b) — two independent mechanisms, both verified firing:**
1. **Exit code** — a new failure counter returns **exit 6**, verification still running first so its FAIL
   lines print.
2. **`DemoVerifier` check 21** — manifest-derived exact counts on the natural key, four arms: per-status
   count == manifest (×3), `NONE` months carry **no** row, and **no period on a month the manifest does
   not describe** (the wrong-month tell for the new `{year, month}` body).

**Broken-world proof** on an isolated stack: one period row deleted + one day's hours perturbed so the
loader's own repair path could not mask it → `exit 6`, `[FAIL] … want EMPLOYEE_APPROVED, got NO ROW`.
**That run was a RERUN**, where the other period 409-skipped — the masking path did not hide it. *Under
the old counters it is byte-identical to the healthy run before it and would have returned 0.*

**Live end-to-end, full scale** (isolated stack, `/send` 401 + `/submit` 405 confirming the retirement):
fresh load `sent=375 approved=109 rejected=127 failures=0`, every arm exact, **exit 0, ALL CHECKS
PASSED**; rerun `monthsAlreadyComplete=493`, same exact counts, exit 0.

**One probe did NOT fire, and that was the best result** — it revealed the stray-period arm only sees
employees the manifest names. Documented in code rather than glossed.

**Bonus fix, flagged and accepted**: verifier check 6 now exempts the manifest's `AdminUserId` from
"primary_org is an ORGANISATION" — `demo_admin` homes at a MAO **by design** (check 17 already carved it
out for the same reason), so this check failed on *every* run. That is the long-standing
"known-benign exit-5" recorded in the launch skill. **This is the first time the verifier has exited 0.**

**Ops discipline**: the owner's stack was never touched — Orchestrator-verified identical before and
after (138/127/109/1) — and zero `s127b` containers or volumes remain.

**Carried, not fixed**: a rerun is **not write-free for REJECTED months** — `REJECTED` is a legitimate
send source (§3.2), so a rerun re-sends and re-rejects those 127 periods. Final status and counts are
identical, but one extra event pair per rejected month per rerun. Correct product behaviour; a write-free
rerun would need a status probe first. **Follow-up candidate.**

**Pre-existing defect found in the owner's live world**: `demo_styx1_0846` 2026-05 is `DRAFT` where the
manifest says `APPROVED` — *the old loader reported success on that load.* Disappears on next reseed.

**Six stale citations** reported (all drifted by 12701a): `DemoGenerator.cs:442→490`,
`DemoManifest.cs:119-122→138-142`, `DemoLoader.cs:537→593 / :502→570 / :513→581`, `ApiClient.cs:87→109`.

**KB proposals APPROVED (2)** → [PAT-017](../knowledge-base/patterns/PAT-017-isolated-compose-stack-for-end-to-end-verification.md),
[FAIL-006](../knowledge-base/failures/FAIL-006-warnings-that-never-reach-the-exit-code.md).

### TASK-12713 — Promote the rejection reason to row level (R7) ✅ 2026-08-06
**Agent**: UX · `tsc` 0 · `lint` 0 · **vitest 730/730** (726 → 730)

The reason now renders as a full-width strip immediately beneath the row, outside `isExpanded` — visible
on page load, no click, no panel. Full-width rather than in the Status cell deliberately: it is prose the
leader typed, and the narrow column would wrap it two words per line.

**Constraints held (Orchestrator-verified):** `canExpand` is byte-for-byte unchanged
(`row.normRegistered !== null`); no withheld figure re-exposed — asserted by literal string equality on
the strip, so any figure dragged along breaks it; **one render site** — the in-panel branch is deleted,
not disabled, and `.detailAlertError` removed from the CSS with it (the sole remaining occurrence is a
comment recording the deletion).

**Two-arm falsification** — the insight worth keeping: *one arm proves only half of "one field, one
place"*. Arm 1 (promotion reverted) → **4 RED**; arm 2 (second render site re-added) → the duplication
guard fires alone. The agent also reported that one of its four tests **correctly stayed green** under
arm 1, because a negative assertion cannot detect a missing promotion — and said so rather than claiming
4-for-4. Restores verified by `sha256sum` + `diff`, `touch`-ed per FAIL-005.

⚠ **The scope defect recurred — for the fourth time, on the one task that never saw Step 0b.**
12713's declared scope omitted `TeamRowDetail.test.tsx`, which its own criteria cannot avoid: 12707's
test *"the rejection reason is SERVED but no longer REACHABLE"* was written precisely to pin the gap R7
closes, so it went red the moment the promotion landed. Inverted in place. Step 0b caught this class
three times; 12713 was added **after** 0b closed, i.e. it is the only task that never went through the
lens that catches it. **Process lesson: a task added mid-sprint needs a scope check against its own
acceptance criteria before dispatch.**

**KB proposal APPROVED** → [PAT-016](../knowledge-base/patterns/PAT-016-container-predicate-silently-gates-its-contents.md).

### TASK-12705 — Allocation-predicate consolidation ✅ 2026-08-06 · **owns AC-1, AC-2 (verify)**
**Agent**: Backend API (cross-domain authorized) — **terminated early by a session limit**, mid-probe.
The implementation was complete; the Orchestrator ran the verification it was cut off before.

**Shipped**: new `src/Backend/StatsTid.Backend.Api/AllocationBalance.cs` — a `static class` exposing
`Tolerance`, `Scale`, `Round(decimal)` and `Evaluate(worked, allocated) → DayBalance`, where `DayBalance`
is a `readonly record struct` carrying `IsBalanced` / `IsImbalanced` / `UnderAllocated` / `OverAllocated`
/ `Direction`. **9 call sites** in `ApprovalEndpoints.cs` now use it, and **zero `AllocationTolerance`
occurrences remain in that file** — all three inline backend encodings collapsed into one.
Plus `tests/StatsTid.Tests.Regression/ArchitectureConstraints/ToleranceAllowListTests.cs`.

**Orchestrator verification (all run against the merged tree):**

| Check | Result |
|---|---|
| Build | 0 errors |
| **AC-1** `ToleranceAllowListTests` | **10/10 green** |
| **AC-1 falsified** — stray `0.005m` injected into a production file | **2 of 10 RED** → the check genuinely fires |
| **AC-2** characterization baseline (22 cases, written pre-change) | **22/22 green** → the collapse is behaviour-preserving |
| **Encoding #5 replaced** — `AllocationGateTests` | **8/8 green** |
| **…and falsified** — shared predicate inverted | **8/8 RED** |
| Characterization under the same inversion | fires (8 of 17 in the filtered subset) |
| All three suites, restored | **40/40 green** |

The `AllocationGateTests` result is the one that matters: the **old** version of that file passed with
the production gate **deleted** (12700 proved it). The rewrite goes 8/8 red when the shared predicate is
inverted, so it now exercises real code rather than mirroring it. Encoding #5 is genuinely gone, not
renamed.

Both probes used scratchpad backups, `touch`-ed the restored file per **FAIL-005**, and the restore was
**verified by diff** before re-running — not assumed.

**Scope respected**: `frontend/src/components/SkemaGrid.tsx` and `frontend/src/lib/allocation.ts` both
**untouched** (verified). Encoding #4 stays per ADR-028 D4, and the `SkemaGrid` site was correctly left
alone — it is `absenceOverNorm`, a different rule that merely borrows the tolerance value.

⚠ **Still unverified**: the N+1 constraint on the two roster-batched read surfaces. The tripwire for it is
live (12700 pinned the breakdown's raw month totals), and the baseline passes — but no performance
measurement was taken. Carry to Step 5a.

### TASK-12707 — Frontend ✅ 2026-08-06 · **owns AC-15**
**Agent**: UX · `tsc --noEmit` 0 · `lint` 0 · **vitest 726 tests, 0 failures** (720 → 726)

One-call send (`/api/approval/send`), 422 parse moved onto it, stranding comment and its condition
deleted, `deriveOkVersion` removed (a client-side copy of `OkVersionResolver`, now zero callers). R3 form
removed with its state, CSS and "Periode indsendt.". `EMPLOYEE_APPROVED` added to both switches, labelled
**"Indsendt"** — matching what `SUBMITTED` meant to the employee, and agreeing with `SkemaPage`'s footer.
`SkemaPage.tsx` gained an extracted `isPeriodLocked(status)` (the single FE mirror) and lost a silent dead
end (a missing `agreementCode` made "Godkend måned" do nothing).

**The R1 inversions were verified real, not cosmetic**: old fixtures restored under new assertions → **4
tests RED**, then restored from scratchpad backups. One is an independent second witness (a
`Norm-opfyldelse` count derived from `normRegistered !== null` across the roster).

**It found a THIRD stale R1 test** the plan did not list — `TeamRowDetail`'s "Begrundelse for afvisning"
case, stale by exactly the same construction (fixture inherited `row()`'s `normRegistered: 140`).

**AC-15: it added coverage because the existing coverage was hollow.** `approvalValidationError.test.ts`
is a verbatim mirror of the private formatter fed pre-parsed objects — it cannot see wire, hook or route,
and **stayed green through the version where the create leg had no parse at all**, which is the exact
failure AC-15 names. Three new tests drive the real button over the real `/send`; falsified by removing
the parse (both shape tests then fail on the missing Danish strings).

**Stale citations found** (5): `SkemaPage.tsx:1107` is the **footer send-affordance**, not "the grid-unlock
mirror" as the plan and refinement §9 both call it — the actual grid unlock is `:514` and needed no
change; plus `MyPeriods.tsx:323→324`, `TeamRowDetail.test.tsx:628→627`, `useSkema.ts:331-340→332-340`,
and the plan's `TeamOversigt.test.tsx:216` vs the refinement's `:218` (the refinement is right).

⚠ **FINDING — see "Open decision" below: the rejection reason is now unreachable in the UI.**

### TASK-12704 — Lock enrolments ✅ 2026-08-06 — **§2's invariant is now true**
**Agent**: Backend API (cross-domain authorized) · **Files**: `TimeEndpoints.cs`, `SkemaEndpoints.cs`
**Build**: `--no-incremental`, 0 errors, no warnings in either file

**Deliverable 1** — `POST /api/time-entries` enrolled. Orchestrator-verified order: tx at explicit
`ReadCommitted` → `EmployeeConsumptionLock.AcquireAsync` → outbox enqueue → projection insert → commit.
The acquire is the **first statement**, inside the existing `try` so the existing rollback covers it.
Same lock, same key — no second advisory lock. That endpoint writes `time_entries_projection`, the
**allocated** side of the gate, and was previously unlocked; this is the hole that blocked revision 4.

**Deliverable 2** — Skema re-reads inside its transaction, via TASK-12702's `(conn, tx)` overload, after
the lock and before any write. Verified by `awk` over the transaction range that **no** status re-read
existed before — confirming the refinement's claim on the current tree and that this is the only one.

**Better than specified**: rather than hand-copying the 409, the agent extracted `IsPeriodLockedForSave`
and `PeriodLockedForSaveConflict` as shared statics and pointed **both** the pre-transaction fast path
(`:714-715`) and the in-lock check (`:1500-1503`) at them. "The two 409s match" now holds **by
construction, not by inspection** — the read-side precedent being S124's shared `ApprovalVisibility`.

**One change beyond brief, flagged and accepted**: it also pinned `ReadCommitted` on Skema's write
transaction, which was on the bare overload. Behaviour-neutral, but Deliverable 2's correctness *depends*
on it — under REPEATABLE READ the snapshot predates the lock grant and the new re-read would be a
guaranteed no-op. Leaving it unpinned would have made its own comment an unenforced assumption.

**Stated as UNVERIFIED** (owns no tests, ran none): that the lock actually prevents the race at runtime;
deadlock-freedom of the enlarged lock set (it did confirm the time-entries transaction acquires nothing
else, so it cannot cycle from its own side); contention impact. TASK-12708 must assert the loser's
post-lock read **returns the winner's row** — PAT-015 notes the two isolation levels are
indistinguishable by outcome, so "a 409 came back" would pass against the broken version.

**Stale citation found**: refinement §3.4's `ApprovalPeriodRepository.cs:65` is now `:73` (Phase 1's doc
comment pushed it). Everything else it checked was exact — `TimeEndpoints.cs` and `SkemaEndpoints.cs`
were untouched by Phases 1/2a, which is why their citations survived.

**KB proposal APPROVED as an AMENDMENT to PAT-015** (the agent's own call — a separate entry would have
fragmented the rule family). Two checklist items added, the sharper being: *the in-lock re-read must use
the `(conn, tx)` overload — a repository method that opens its own connection reads outside the lock, so
**it looks like a re-read and is not one***, and the two overloads differ only by two leading arguments.

### TASK-12703 — The shared send command + both route adapters ✅ 2026-08-06
**Agent**: Backend API (cross-domain authorized) · **Files**: `ApprovalEndpoints.cs`,
`Contracts/ApprovalResponses.cs`, `docs/api/openapi.json`, `frontend/src/lib/api-types.ts` (4, all in scope)
**Build**: 0 errors, 0 new warnings · **Gates** (Orchestrator-verified): drift **green** (102 paths,
187 schemas), convention **green** (134 typed / 3 grandfathered / 9 declared — counts unchanged),
types-freshness **green** (`api-types.ts` byte-identical to a fresh `npm run gen:api`)

`ExecuteSendAsync` is a private static taking a `SendCommandServices` record, so each adapter passes one
argument built from its own DI parameters — never model-bound, therefore invisible to Swashbuckle. Order
as specified: ReadCommitted tx → lock first → natural-key re-read → floor → source-state 409 → dimensions
→ coverage → allocation → create arm → **one** conditional transition for **both** arms → stamp →
deadlines → audit → event. Orchestrator-verified: `IsolationLevel.ReadCommitted` pinned (`:1700`), lock
first (`:1713`), `/submit` retired, `/send` mapped (`:156`), and **`new PeriodSubmitted` now appears zero
times** — retained for replay, no longer emitted, exactly as §3.6 requires.

**The verification that mattered.** The create-arm transition was the one mechanism nothing had pinned —
five review revisions *argued* it. The agent executed the verbatim SQL against live Postgres inside
`BEGIN … ROLLBACK`: the conditional UPDATE's `FOR UPDATE` subselect on the row **its own transaction had
just inserted and not committed** returned `DRAFT`; `submitted_at` was NULL after the transition and
non-null after `StampSendAsync`; **0 rows survived the rollback**. Observed, not asserted.

It also proved the by-id pre-read needs no drift guard by exhaustive grep rather than by claim: 5
production `UPDATE approval_periods` sites, none touching `employee_id`/`period_start`/`period_end`, and
**zero** production DELETEs.

**Stated honestly as UNVERIFIED** (it owns no tests): no HTTP exercise of either adapter, so the role
floor *on these routes*, the whole-month 409, both 422 shapes, the audit/outbox row counts, the
transition-arm dimension correction and every concurrency claim are argued from code plus 12702's pinned
primitives — not observed. There is also **no per-route spec≡runtime assertion for `/api/approval/send`**
(PAT-012 step 6). All of it belongs to 12708 / 12711 / 12712.

**Three disclosed judgement calls, all accepted:**
1. The audit `comment` is now conditional — `"Employee self-approval"` when `actor == employee`
   (byte-stable), else `"Sent on behalf of {employeeId}"`. Not requested, but R4 makes HR-for-another a
   *sanctioned* path and writing "Employee self-approval" on that row would be a **false audit
   statement** (P3). Correct call.
2. The whole-month 409 echoes no period data — the guard precedes the floor, so echoing dates would
   widen pre-authorization disclosure past the existing 404.
3. The floor runs *after* the lock, per the specified choreography — so an unauthorized caller briefly
   holds the target's advisory lock. Verdict is identical either way (the floor's inputs are immutable).
   **Left as specified**; noted for the security review at Step 5a.

**KB proposal APPROVED** → [PAT-015](../knowledge-base/patterns/PAT-015-advisory-lock-read-committed-vs-memoized-authority.md).

### TASK-12701a — Seeding (structural) ✅ 2026-08-06 · **owns AC-14(a)**
**Agent**: Backend tooling (cross-domain authorized) · **Test delta**: DemoSeed 55 → 94 (+39)
**Created**: `ProjectCatalog.cs`, `AllocatedMonthBuilder.cs`, `DanishHolidays.cs` + 4 test files +
`Golden/pre-s127-legacy-smoke.{sql,manifest.json}`
**Modified**: the DemoSeed model/generator/emitter/loader-verifier, `init.sql` (projects block),
`99-demo-seed.sql` (regenerated), `S116ApprovalSpecRuntimeTests.cs` (SeedAsync project rows), both goldens

**AC-14(a) met — the headline number moves to zero.** All 13 orgs have projects; **0 of 3,251 active
users sit in a project-less org (was 1,319).** Orchestrator-verified: `init.sql` contains **no DDL**
(44 insertions, 1 deletion, projects block only), and **STY01 gets 4 projects** with a comment recording
why it is load-bearing — `emp001`/`mgr03` live there, so **TASK-12709's E2E is unblocked without the
generator**, exactly as the 0b cycle-1 review predicted.

**Balanced months.** `AllocatedMonthBuilder` is **pure derivation — no `Random` at all**; per-employee
variation comes from a stable FNV-1a salt, deliberately not `GetHashCode` (which .NET randomizes per
process, so it would have broken determinism). Every expected workday not on an absence gets a work-time
interval plus 1–2 allocations summing to it **exactly**, not merely within tolerance. Absence days stay
`worked==0 ∧ allocated==0`. Holidays are Easter-derived and pinned against the literal `init.sql:374-416`
rows. Scale: 493 activity months, 8,663 work days, 11,559 allocations.

**Golden regeneration, done honestly.** A regenerated golden cannot testify about its own regeneration —
so the agent preserved the untouched pre-S127 bytes as a second golden and added
`LegacyUnchangedByS127Tests`, pinning *current output minus exactly the S127 additions* against them. The
whole-file `Assert.Equal` was **not** loosened. Result: the manifest golden diff is **933 insertions, 0
deletions** — not one person, edge, absence, vikar or messy case moved.

**Four falsification probes**, each restored from scratchpad: wrong declared hours → 3 balance tests red;
skipped workday → coverage test red; an RNG draw inserted before activity generation → manifest legacy
pin red; before user generation → both pins red. A draw *after* the last consumer correctly does **not**
fire, and that limit is stated in the test's doc comment rather than papered over.

**Scope deviation — declared, and approved.** `docker/postgres/99-demo-seed.sql` is outside the literal
grant. Its own header reads *"GENERATED ARTIFACT. Produced deterministically by tools/StatsTid.DemoSeed.
Do not hand-edit"* — so regenerating it is the mechanical consequence of changing the emitter, and
leaving it stale would have shipped a demo world with no projects. **Orchestrator-approved.** The agent
declaring it rather than absorbing it silently is the behaviour the cross-domain rule exists to produce.

**Boundary respected**: `DemoLoader`'s `/submit` call and the `PeriodOutcome` vocabulary untouched
(comment added marking them 12701b's).

⚠ **Honest limit stated by the agent**: verified in SQL against a throwaway Postgres on `:55433` (the
owner's demo stack on `:5432` untouched), and gate-compliance proven at manifest level using the gates'
own arithmetic. **End-to-end confirmation still needs a real loader run** — which is 12701b.

**KB proposal APPROVED** → [FAIL-005](../knowledge-base/failures/FAIL-005-probe-restore-timestamp-stale-build.md).

### TASK-12700 — AC-2 characterization baseline ✅ 2026-08-06
**Agent**: Test & QA (documented `AGENTS.md:37` exception) · **Owns AC-2 (capture)**
**Files**: new `tests/StatsTid.Tests.Regression/Outbox/AllocationPredicateCharacterizationTests.cs`
(675 lines, **22 test cases**) + 12 doc-comment lines on `AllocationGateTests.cs`
**Zero production files in the diff** — verified independently by the Orchestrator
(`git diff --stat HEAD -- src/ docs/ frontend/ docker/ tools/` is empty).

22 reconciles exactly: two 8-case `[Theory]`s over the value table (16) + 1 `[Fact]` + 5 rounding-limit
rows. Under each inversion **16 failed / 6 passed** — the 6 being the 5 pure-arithmetic limit cases plus
the coverage-reachability pin, none of which touch the inverted expression.

**All three inversions run separately, each failing in BOTH directions and at its own surface:**

| inversion | result |
|---|---|
| `:1488` gate `<`→`>` | 16 failed — balanced C5 got 422, imbalanced C2 got 200 |
| `:1109` team-overview `>`→`<` | 16 failed at the `hasWarning` assertion (gate untouched) |
| `:1284` breakdown `>`→`<` | 16 failed at `hasAllocationImbalance` |

**The fixture insight that made the probe honest.** The gate only runs after the coverage check passes.
Filling the other weekdays with *balanced* work would satisfy coverage — but those days are also compared,
so an inverted gate would still 422 **for the wrong reason** and a status-only assertion would have stayed
green. Instead every other weekday carries a full-day `VACATION` row: absences satisfy coverage but live
in a table neither side of the comparison reads, leaving **exactly one comparable day**. The assertions
pin the exact `unbalancedDays` set, not the status code.

**Two findings carried to TASK-12705:**
1. **The allocation-breakdown reports month-level `worked`/`allocated` RAW**, rounding only inside the
   per-day comparison. Caught by case C5 at capture time (the agent's first draft asserted the rounded
   value and was wrong). Both raw and rounded totals are now pinned, so routing month totals through the
   shared per-day predicate during the collapse **goes red** — a mechanical guard on the refinement's
   §3.8 "pure per-day predicate, separated from data loading" constraint.
2. **`AllocationGateTests` is confidence-shaped non-evidence** — it re-implements `SumIntervalHours`, the
   allowlist, the rounding, the tolerance and both directions; all 7 tests pass with the production gate
   **deleted**. Its header now says so.

⚠ **FAIL-003**: the new characterization file is **untracked** — must be `git add`ed into the close commit.

**KB proposal APPROVED** → [PAT-014](../knowledge-base/patterns/PAT-014-characterization-baseline-one-inversion-per-encoding.md).

### TASK-12702 — Repository primitives ✅ 2026-08-06
**Agent**: Data Model (extended into Infrastructure, cross-domain authorized)
**Files**: `ApprovalPeriodRepository.cs` (**157 insertions, 0 deletions** — verified purely additive, so
`BuildUpdateStatusCommand`'s `status switch` is untouched as required) + new
`tests/StatsTid.Tests.Regression/Infrastructure/ApprovalPeriodSendPrimitivesTests.cs`
**Build**: clean · **AC-7c**: satisfied on both arms

- `TryCreateIfAbsentAsync` (`:1550`) — `ON CONFLICT (employee_id, period_start, period_end) DO NOTHING
  RETURNING period_id`, copied from the `PayrollExportService` precedent; conflict target verified
  against `init.sql:892`
- `GetByEmployeeAndPeriodAsync(conn, tx, …)` (`:109`) — the pre-existing self-managed overload gained a
  doc note that in-lock callers must not use it. No `GetByIdAsync` overload, as specified
- `StampSendAsync` (`:1738`) — in-transaction only, no self-managed overload; the "no source-state guard
  needed" argument asserted in the doc comment rather than re-guarded

**The agent ran an unrequested falsification probe and it paid.** It substituted the plausible wrong
implementation (plain INSERT + `catch 23505 → null`) and re-ran: **3 of 5 tests went red with `25P02:
current transaction is aborted`**, the two that do no post-conflict work stayed green. That variant
returns `null` correctly — *a test asserting only "the second call returns null" would have passed
against it.* Source restored from a scratchpad backup (not `git checkout --`), rebuilt, 5/5 green,
neighbouring `TxContractTests.ApprovalRepo_*` 4/4 green.

Tests use the **real `init.sql`** via a shared container fixture rather than hand-rolled DDL — correct,
since the constraint under test is exactly what a fixture copy could drift away from (the S122 lesson).

**Two findings handed forward to TASK-12703**, both pinned by tests rather than asserted:
`TryUpdateStatusConditionalAsync("EMPLOYEE_APPROVED")` leaves `submitted_at` **NULL** (so the follow-up
UPDATE does work the switch does not), and a subsequent `DRAFT` transition **re-nulls** it while the
three server-resolved dimensions survive — the reopen semantics the separate-statement design protects.
The seed row uses deliberately wrong dimensions (`STY01`/`AC`/`OK21`) so the correction is falsifiable
rather than a restatement of the seed.

⚠ **FAIL-003**: the new test file is **untracked** — it must be `git add`ed into the close commit. The
close guard's untracked-source gate will block otherwise.

**KB proposal APPROVED** → [PAT-013](../knowledge-base/patterns/PAT-013-on-conflict-vs-23505-transaction-liveness.md).

### TASK-12706 — R1 visibility predicate ✅ 2026-08-06
**Agent**: Backend API (cross-domain authorized) · **Files**: `src/Backend/StatsTid.Backend.Api/ApprovalVisibility.cs` (1 file)
**Build**: clean, 0 warnings 0 errors · **Scope**: respected, no cross-domain dependency declared

- `IsSubmittedToManager` → `"SUBMITTED" or "EMPLOYEE_APPROVED" or "APPROVED"`
- The S124 rationale (`ApprovalEndpoints.cs:1077-1078`) is **quoted verbatim and answered** in the doc
  comment, per R1. The answer is better than the plan asked for: *S124's argument assumes the month stays
  frozen at "these very numbers" — it does not, because a `REJECTED` month is editable again, so the
  manager was watching it change.* That is the in-progress state R1 rules out.
- R5's scope limit recorded in the same comment — the predicate makes the sibling endpoints'
  figures **un-displayed, not unreadable**.
- Stale citation fixed: `:27` cited `init.sql:1103` (a column in `CREATE TABLE absence_type_visibility`);
  the status CHECK is at `:1118-1119`. Verified independently by the Orchestrator.

**Finding that changed the plan** — the agent ran a full census rather than trusting the plan's list, and
found the expected-RED count was **1, not 3**. See the corrected Expected-RED block above. Cleared as
genuinely unaffected (checked, not assumed): `TeamOverviewAggregateTests.cs:560`,
`S116ApprovalSpecRuntimeTests.cs:183,295`, `AllocationBreakdownEndpointTests.cs`,
`S91TreePageHrAccessTests.cs:466`, `S120SkemaSpecRuntimeTests`, `SkemaPage.test.tsx`.

**Ops note**: worktree agents do not see untracked files — `docs/sprints/SPRINT-127.md` and
`.claude/refinements/` are untracked on master, so worktrees created from HEAD lack them. The agent read
them from `C:\StatsTid\` directly. Future phase prompts should state that path explicitly.
