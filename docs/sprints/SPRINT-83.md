# Sprint 83 — Full edge-auth serialization pass (close the S78 residual map; Reporting-Line → tightened A−)

| Field | Value |
|-------|-------|
| **Sprint** | 83 |
| **Status** | planned |
| **Start Date** | 2026-06-19 |
| **End Date** | — |
| **Orchestrator Approved** | yes — 2026-06-19 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 warnings 0 errors |
| **Test Verified** | yes — 856 unit + 977 regression + 6 smoke (no schema change, S82 greenfield) + 468 fe + 3 e2e = 2310 (+5); regression green (the ~53 first-run failures root-caused to the missing compose Postgres :5432 prerequisite + 1 transient shed — all re-run green; see Test Summary adjudication); CI authoritative on push |

## Sprint Goal
Close the tractable subset of the S78 edge-auth residual map (ADR-027 D18) under the existing drift-guarded advisory protocol — serialize the self-`DELETE /delegate` revoke (R1) and add a conditional current-tree advisory to the admin-vikar-REVOKE (R2) — and formally accept + document the genuinely-intractable residuals (R3 role-deactivation as a non-corrupting policy; R4 user-deactivation + R5 JWT-TTL as named platform residuals). Backend + docs ONLY — no schema, event, or FE change. Outcome: a **tightened, justified A−** residual map (NOT a flat A — owner-ruled OQ-3, the S77 over-claim guard).

Refinement: `.claude/refinements/REFINEMENT-s83-full-edge-auth-pass.md` (dual-lens reviewed; 2 convergent BLOCKERs resolved pre-plan). Owner rulings 2026-06-19: OQ-1 ACCEPT (R3 documented, not closed), OQ-2 ACCEPT (R4+R5 platform residuals), OQ-3 tightened A−.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py`: db-schema in sync (65 tables); KB INDEX complete (49 entries, 0 orphans, 0 dangling) |
| Pattern compliance spot-check | CLEAN | Lock protocol reuses S78 ADR-027 D18 primitives verbatim |
| Orphan detection | CLEAN | No new orphans |
| Documentation drift | CLEAN | sprint inventory through S82 complete; freshness OK |
| Quality grade review | CLEAN | Reporting-Line & Approval-Routing currently A− (ADR-027 D18 / QUALITY.md) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P7 security/access-control + P5 concurrency-serialization; auth-adjacent) |
| **External Codex** | invoked 2026-06-19 — cycle 1: 2 BLOCKER, 2 WARNING, 3 NOTE; cycle 2: "Clean — BLOCKERs resolved" (3 confirming NOTEs, no new BLOCKERs) |
| **Internal Reviewer** | invoked 2026-06-19 — cycle 1: 0 BLOCKER, 0 WARNING, 3 NOTE |
| **BLOCKERs resolved before Step 1** | yes — both Codex BLOCKERs (A: R1 single-key unsound → unified onto the persisted-anchored helper; B: active→inactive derive race → defensive-derive try/catch) |

### Findings (cycle 1)

_Codex findings ([[review-lens-complementarity]] — caught what the internal lens rated sound):_
- **BLOCKER-A** — TASK-8301 R1 single-key insufficient. `CloseByApproverAsync` closes by `absent_approver_id` only while the row carries persisted `TreeRootOrgId` (`ManagerVikarRepository.cs:290`, `ReportingLineEndpoints.cs:2127/2141`); an owner who transferred after creating the self-vikar has persisted≠current, so a current-root-only lock misses the old persisted tree. R1 mirrors R2.
- **BLOCKER-B** — TASK-8302 helper active→inactive race. `SELECT is_active` then derive can throw/500 under ReadCommitted if the subject deactivates in between (`DeriveEmployeeTreeRootInTxAsync` filters `is_active=TRUE`/throws, `ReportingLineRepository.cs:625/630`). Needs a defined fallback.
- WARNING — TASK-8301 RED test under-specified; must force persisted≠current owner-transfer divergence.
- WARNING — TASK-8302 waiter test must force persisted≠current and hold only the current-root advisory.
- NOTE — dual-key ordering IS deadlock-safe vs transfer (same id-sorted namespace; advisories before FOR UPDATE).
- NOTE — Endpoint 15 probe-outside + in-lock authorize anchor is sound.
- NOTE — R3 staying test-only (RBAC not coupled to tree lock) matches P1 + ADR-027 D13/D18.

_Internal Reviewer findings:_
- (0 BLOCKER / 0 WARNING) — decomposition, cross-scope grant, the conditional-dual-key design, the inactive-manager persisted-only fallback (non-corrupting), and the tightened-A− grade all judged architecturally sound vs ADR-027 D13/D15/D17/D18 + priority order.
- NOTE-1 — `DesignatedApproverAuthorizer` lives in Infrastructure (`src/Infrastructure/...`); line numbers correct.
- NOTE-2 — R4 `users.is_active` writer count: plan says 3, ADR-027 D18 says "~4"; reconcile in the doc task.
- NOTE-3 — assert `ManagerVikarEnded.TreeRootOrgId` continues to come from the pinned row (not the derived current root) on the inactive path.

### Resolution
- **BLOCKER-A** → merged TASK-8301+8302 into ONE task: both revoke endpoints adopt the shared `AcquireRevokeTreeLocksAsync` helper anchored on the **persisted** root; R1 gains a pre-lock persisted-root probe. R1-single-key framing removed.
- **BLOCKER-B** → helper uses a **defensive-derive** (try/catch `InvalidOperationException` → persisted-only), no is-active gate; same fallback on the drift-guard re-derive. A mid-flight deactivation cannot 500.
- WARNINGs → tests re-specified: R1 owner-transfer (persisted≠current) RED-on-old; waiter test forces persisted≠current holding only the current-root advisory; added an explicit active→inactive race test.
- NOTE-1/2/3 → TASK-8303 labels the authorizer as Infrastructure; TASK-8304 reconciles the writer count (D19 supersedes "~4" with the enumerated 3) + asserts the event `TreeRootOrgId` provenance.
- **Cycle 2 (verification of the cycle-1 edits)** runs before Step 1.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity preserved (RBAC bounded context kept separate from reporting-edge tree lock — R3 NOT dragged into the tree lock; dual-lens confirmed)
- [x] P3 — Event sourcing append-only respected (`ManagerVikarEnded` emission + audit trio unchanged; `TreeRootOrgId` still from the pinned row)
- [x] P4 — version-correctness preserved (CAS / row-version unchanged)
- [x] P5 — Integration isolation: uniform "all advisories before any row locks", id-sorted ordering preserved across transfer + both revoke paths (no new deadlock vector — dual-lens confirmed)
- [x] P7 — Security/access-control: revoke-safety preserved (persisted-root anchor retained); serialization tightened; R3/R4/R5 honestly documented as residuals
- [x] P8 — CI/CD: full pyramid green (pending CI run on push)
- [x] P9 — Usability: response contracts byte-stable

## Task Log

### TASK-8301 — R1: self-`DELETE /delegate` single-key drift-guarded serialization

| Field | Value |
|-------|-------|
| **ID** | TASK-8301 |
| **Status** | planned |
| **Agent** | Security/Backend + Infrastructure (cross-scope, Orchestrator-approved — the two revoke endpoints + the shared repo primitive are ONE coupled change in the same two files) |
| **Components** | Backend API (`ReportingLineEndpoints.cs` — Endpoint 13 self-DELETE + Endpoint 15 admin-revoke), Infrastructure (`ReportingLineRepository.cs`) |
| **KB Refs** | ADR-027 D15/D17/D18, ADR-032 D4 (advisory-lock precedent), ADR-018 D3, ADR-026 D2 |
| **Orchestrator Approved** | no |

> **Step-0b BLOCKER resolution (Codex cycle 1):** R1 and R2 are the SAME shape and are unified onto ONE helper. R1 was originally scoped single-key; Codex BLOCKER-A proved that is unsound — `CloseByApproverAsync` closes by `absent_approver_id` only (`ManagerVikarRepository.cs:290`, `ReportingLineEndpoints.cs:2127`) while the row carries a persisted `TreeRootOrgId`, so a self-delegation whose owner transferred AFTER creating it has persisted≠current, and a current-root-only lock misses the old persisted tree. R1 therefore also takes the persisted root as its authoritative anchor. Codex BLOCKER-B (active→inactive race) is resolved by a **defensive-derive** contract below.

**Description**: Both revoke endpoints adopt the S78 D18 drift-guarded in-lock discipline via a single new Infrastructure helper.

**The helper** — `ReportingLineRepository.AcquireRevokeTreeLocksAsync(conn, tx, persistedRoot, subjectId, ct)`:
- `roots = {persistedRoot}` (ALWAYS — the immutable revoke-authority anchor; survives an inactive/transferred subject).
- **Defensive-derive (resolves BLOCKER-B):** attempt `DeriveEmployeeTreeRootInTxAsync(subjectId)` inside a `try`/`catch (InvalidOperationException)`. On success add the current root; on throw (subject missing/inactive — incl. a deactivation that committed AFTER any is-active probe, the ReadCommitted race) treat as "no current root" → persisted-only. Do **not** pre-check `is_active` as a gate — the derive itself is the authority, and catching its throw closes the race window so a mid-flight deactivation can never 500 a revoke-safe path.
- Acquire each advisory in **distinct + id-sorted order** (the `AcquireTreeLocksForTransferAsync` idiom, `ReportingLineRepository.cs:726`) — preserving uniform lock ordering, deadlock-safe against the transfer path (which acquires {old,new} in the same sorted namespace; Codex NOTE confirms no cycle from overlapping {old,new} vs {persisted,current} sets).
- **Drift-guard the current root only** (persisted is a fixed column, cannot drift): if a current root was derived, re-derive under the held locks; on divergence throw `TreeRootDriftException` (caller's `TreeRootDriftRetry.RunAsync` → bounded retry → 409). If the re-derive now throws (subject deactivated under the lock) → fall back to persisted-only, no error.
- All advisories acquired BEFORE any row `FOR UPDATE`.

**Endpoint 13 — self-`DELETE /api/reporting-lines/delegate` (`:2080`):** currently `RepeatableRead`, no advisory. Add a pre-lock probe of the actor's active self-vikar row (`GetActiveByApproverAnyDateAsync(actorId)`) to obtain its persisted `TreeRootOrgId` (404 "No active self-delegation to revoke" if none — replaces the post-close null check, behavior-equivalent). Wrap the body in `TreeRootDriftRetry.RunAsync`, open `ReadCommitted`, call `AcquireRevokeTreeLocksAsync(conn, tx, probe.TreeRootOrgId, actorId, ct)` as the first in-tx action, then the existing `coveredCount` read + `CloseByApproverAsync(actorId)` + `ManagerVikarEnded`/audit trio. Response `{ revokedCount }` and the 401 arm unchanged.

**Endpoint 15 — admin `DELETE /api/admin/reporting-lines/{managerId}/vikar` (`:2470`):** already has the persisted-root advisory + FOR UPDATE + in-lock authorize/close (`:2508-2543`). Keep the pre-lock `probe` OUTSIDE the retry; wrap the conn/tx body in `TreeRootDriftRetry.RunAsync`; replace `AcquireTreeLockAsync(probe.TreeRootOrgId)` (`:2508`) with `AcquireRevokeTreeLocksAsync(conn, tx, probe.TreeRootOrgId, managerId, ct)`. Authorize (`:2525`) still anchors on the pinned row's persisted `TreeRootOrgId`; the emitted `ManagerVikarEnded.TreeRootOrgId` (`:2552`) still comes from the pinned row (NOT the newly-derived current root) — Reviewer NOTE-3. Response byte-stable; admin actor-org audit (S71) unchanged.

**Validation Criteria**:
- [ ] `AcquireRevokeTreeLocksAsync`: persisted-root always; current-root via defensive-derive (try/catch, no is-active gate); distinct id-sorted; drift-guard current-root-only with deactivation-under-lock fallback to persisted-only.
- [ ] Both endpoints wrapped in `TreeRootDriftRetry.RunAsync`; Endpoint 13 gains the persisted-root probe; both authorize/close + `ManagerVikarEnded.TreeRootOrgId` anchored on the pinned/persisted root; responses byte-stable.
- [ ] **Revoke-safety test (BLOCKER-1)**: admin-revoke of an **inactive** manager's row succeeds (persisted-only) — RED if the impl unconditionally derives the current root.
- [ ] **R1 owner-transfer test (BLOCKER-A)**: a self-delegation whose owner transferred (persisted≠current) — self-DELETE serializes on the persisted root + succeeds; verified RED on the pre-change (`RepeatableRead`, no-lock) code.
- [ ] **Active→inactive race test (BLOCKER-B)**: subject deactivates between/while deriving — the revoke does NOT 500; falls back to persisted-only and succeeds.
- [ ] **Waiter-based mutual-exclusion test**: force persisted≠current (active subject); a holder tx holds ONLY the current-root advisory; the racing revoke BLOCKS until commit (not a final-state assertion) — RED on the pre-change (persisted-only) code.
- [ ] No new deadlock vector: ordering asserted (all advisories before any `FOR UPDATE`, id-sorted) — covered by the transfer-vs-revoke race not deadlocking.
- [ ] `dotnet build` 0/0; existing self-delegation + vikar-revoke + transfer tests green.

> **Test-discrimination note (Step-7a Codex WARNING, resolved doc-precision):** the four concurrency tests split into two discriminator classes. **Tests 2 (R1 owner-transfer, persisted≠current) and 4 (waiter-based mutual-exclusion holding ONLY the current-root advisory) are RED on literal pre-S83 code** (old self-DELETE took no advisory; old admin-revoke locked only the persisted root) — they pin the NEW serialization. **Tests 1 (inactive-manager revoke succeeds) and 3 (active→inactive race, no 500) are defensive-fallback guards for the BLOCKER-B fix** — RED on a *naive unconditional-derive variant* (which would 500/throw), NOT on pre-S83 code (which was already revoke-safe because it never derived the current root). Both classes are load-bearing; the "RED-on-old" phrasing above means "RED on the implementation each test is guarding against".

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/ReportingLineRepository.cs` — new `AcquireRevokeTreeLocksAsync`
- `src/Backend/StatsTid.Backend.Api/Endpoints/ReportingLineEndpoints.cs` — Endpoint 13 + Endpoint 15 rewire
- `tests/**` — the RED-on-old concurrency tests (revoke-safety, R1 owner-transfer, active→inactive race, waiter-based mutual-exclusion)

---

### TASK-8303 — R3: serialized deactivated-approver guard test (the policy's IS-covered arm)

| Field | Value |
|-------|-------|
| **ID** | TASK-8303 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | Backend API (test only) |
| **KB Refs** | ADR-027 D13, ADR-007/009 (RBAC) |
| **Orchestrator Approved** | no |

**Description**: R3 (role-revoke racing an approval) is owner-ACCEPTED as a non-corrupting policy (OQ-1 a) — there is a real non-serialized window because the role lookup runs on its own connection with no RBAC lock (`src/Infrastructure/StatsTid.Infrastructure/DesignatedApproverAuthorizer.cs:131-145`, the `IsActiveLeaderOrAbove` query) and role-revoke takes no advisory (`AdminEndpoints.cs:1804/1810`). We do NOT close it (that would wrongly couple RBAC to the per-employee tree lock — P1). This task pins the **serialized arm that IS covered**: assert the in-lock approve re-check (`ApprovalEndpoints.cs:290/299`) rejects an approver who has been **deactivated** (`is_active=FALSE`) before the approval commits — i.e. the `IsEffectiveDesignatedApprover` `active+LeaderOrAbove` predicate fails closed. This documents, by test, the boundary between the covered (deactivation) and accepted-uncovered (RBAC role-revoke timing) cases. No production code change.

**Validation Criteria**:
- [ ] A test asserting approve/reject is denied (403/appropriate) when the designated approver is deactivated under the held lock.
- [ ] No production code touched; `dotnet build` + suite green.

**Files Changed**:
- `tests/**` — the deactivated-approver guard test

---

### TASK-8304 — R3/R4/R5 documentation + grade + ADR-027 D19 (Orchestrator, docs-only)

| Field | Value |
|-------|-------|
| **ID** | TASK-8304 |
| **Status** | planned |
| **Agent** | Orchestrator (docs-only) |
| **Components** | `docs/SECURITY.md`, `docs/QUALITY.md`, ADR-027, SPRINT-83.md |
| **KB Refs** | ADR-027 D18→D19, ADR-007/009 |
| **Orchestrator Approved** | no |

**Description**: Record the owner rulings as durable docs.
- `docs/SECURITY.md`: update the S78 residual map — R1 closed, R2 closed-modulo-the-named-inactive-manager corner, R3 accepted-policy (the real non-serialized RBAC window + why it's non-corrupting + reversible via operator `reopen`, not auto-healed), R4 (user-deactivation) and R5 (JWT TTL `JwtSettings.cs:8` = 480 min) as named accepted platform residuals deferred to roadmap. **Reconcile the R4 writer count (Reviewer NOTE-2):** the S78 map / ADR-027 D18 says "~4 paths"; enumerate the precise endpoint-reachable + background writers — `ReportingLineEndpoints.cs:1484` (remove, tree-lock domain) + `UserRepository.cs:445` (employment-end-date, employee-lock domain) + `SettlementCloseService.cs:517` (background Step-A flip, employee-lock domain) — and state D19 supersedes the "~4" estimate with the enumerated count so SECURITY.md and ADR-027 D19 agree.
- `docs/QUALITY.md`: Reporting-Line & Approval-Routing held at **tightened A−** (NOT A — owner OQ-3) with the restated residual map; record why (R3 accepted-not-closed + R4/R5 platform). The S77 flat-A over-claim guard cited.
- ADR-027: add **D19** (the conditional dual-key revoke-serialization decision for R1/R2 + the R3 accept ruling + the R4/R5 acceptance; the inactive-manager narrow residual).
- ROADMAP: record R4/R5 + the inactive-manager corner as tracked follow-ups.

**Validation Criteria**:
- [ ] SECURITY.md residual map updated; QUALITY.md tightened A−; ADR-027 D19 added; ROADMAP follow-ups recorded; `check_docs.py` green.

**Files Changed**:
- `docs/SECURITY.md`, `docs/QUALITY.md`, `docs/knowledge-base/decisions/ADR-027-reporting-line-hierarchy.md`, `ROADMAP.md`, `docs/sprints/SPRINT-83.md`

---

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 856 | all passing (unchanged — no unit change) |
| Regression | 977 | all passing — see adjudication below |
| Smoke | 6 | unchanged — no schema/init.sql change (S82 greenfield holds) |
| Frontend | 468 | unchanged — no FE change |
| E2E (gated) | 3 | unchanged — CI `e2e-tests` job |
| **Total** | 2310 | — |

S83 delta: **+5 regression tests** (4 revoke/auth concurrency in `AdminVikarOnBehalfTests` + 1 deactivated-approver pin in `DesignatedApproverAuthorityTests`); .NET regression-only, unit/FE/smoke/e2e unchanged.

**Regression adjudication (NOT a regression, NOT FAIL-002 churn).** The first two full-suite runs reported ~53 failures. Root-caused to **the documented SPRINT-68 ops prerequisite**: the ReportingLine-era classes `ManagerVikarEngineTests` (15) + `ReportingLineRepositoryTests` (37) hardcode `Host=localhost;Port=5432` (`ManagerVikarEngineTests.cs:28-29`) and require the **compose Postgres on :5432** to be running (else connection-refused — *not* a regression); it was not up during those runs. After `docker compose up postgres` (init.sql → 65 tables), the two classes pass **52/52 in 1s**. The remaining 1 failure was a single transient testcontainer shed in `FeriehindringResolutionTests` (true FAIL-002), which passes **17/17** on its own. **All failures were connection/container-start (`InitializeAsync`/`Socket`), ZERO assertion failures**; my change is C# repo/endpoint logic that cannot affect container startup, and the new helper's direct blast radius (`AdminVikarOnBehalfTests` + `DesignatedApproverAuthorityTests` + `ReportingLineWriteLifecycleTests` + `ApprovalConcurrencyHardeningTests`) passed 78/78 exclusively. The full 977 is green by composition (run-2's 924 pass ∪ the re-run 53) + a confirmatory clean full run with compose Postgres up. **CI (Linux + services-postgres, immune to both the local :5432-compose requirement and Docker-Desktop-Windows sheds) is the authoritative full-suite gate on push.**

## Plan Review / External Review artifacts
- Step-0b: `## Plan Review (Step 0b)` above (Codex 2 cycles, Reviewer 1 cycle).
- Step-7a: `.claude/reviews/SPRINT-83-step7a-codex.md` + `.claude/reviews/SPRINT-83-step7a-reviewer.md` (both verdict PASS, 0 BLOCKER; 2 doc-precision WARNINGs resolved).

## Sprint Retrospective

**What went well**: The Step-0b external lens caught two real BLOCKERs (R1 single-key unsoundness; the active→inactive 500 race) that the internal lens rated architecturally sound — both fixed pre-code, cycle-2 verified ([[review-lens-complementarity]], now the load-bearing instrument across S70–S83). The implementing agent's RED-on-old discipline (incl. proving the defensive try/catch via a naive-derive variant) was exactly right. Backend+docs only; no schema/event/FE churn.

**What to improve**: The test-count was provisionally mis-stated (+2 vs the real +5) in the docs before the counting run — both Step-7a lenses caught it. Run the count earlier next time.

**Knowledge produced**: No new KB entry — `AcquireRevokeTreeLocksAsync` is the third member of the existing S78 drift-guarded-acquire family (ADR-027 D19 records the decision). The R3 RBAC-vs-tree-lock bounded-context separation is captured in D19 + SECURITY.md.
