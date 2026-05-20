# Sprint 35 — Phase 4e: S34 Hardening Sweep + AC Family Compensation Seed Bug Fix

| Field | Value |
|-------|-------|
| **Sprint** | 35 |
| **Status** | complete |
| **Start Date** | 2026-05-18 |
| **End Date** | 2026-05-20 |
| **Orchestrator Approved** | yes — 2026-05-20 |
| **Build Verified** | yes (0 errors, 19 pre-existing CS0618 warnings) |
| **Test Verified** | yes (526 unit + 35 plain regression + 218 Docker-gated passing + 90 frontend = **869** total; +12 vs S34 corrected 857) |
| **Sprint-start commit base** | `328a027` (S34 close, 2026-05-17) |
| **Sprint type** | Implementation (against ROADMAP rule correction policy committed 2026-05-18) |
| **Refinement** | `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (READY post 2-cycle dual-lens absorption + Step 0b cycle 1) |
| **Plan** | `.claude/plans/PLAN-s35.md` (Step 0a + Step 0b dual-lens) |

## Sprint Goal

Close 6 deferred items from S34 (admin surface ETag, Case A 23505, Overtime D-test, outer-users-UPDATE stale-snapshot, concurrent admin PUT D-test, ADR-016 D5b reframe) PLUS fix the AC family compensation seed bug discovered during S35 cycle-1 refinement (classified as **bug-with-no-past-impact** under ROADMAP rule correction policy committed 2026-05-18 — pre-launch posture, no past periods exist, no retroactive recompute needed).

**S34-deferred closure status (target)**:
- Items #1 (admin ETag), #2 (Case A 23505 → 409), #3 (Overtime D-test discriminator), #4 (outer-users-UPDATE stale-snapshot), #5 (concurrent admin PUT D-test) → **closing in S35**
- Item #6 (ADR-016 D5b reframe) → **DROPPED** per Reviewer cycle-1 BLOCKER (semantic-shift-not-aggregation; D5b's current 5-pattern enumeration is correct; reframe introduced new errors)
- Net: 5/6 CLOSED + 1/6 DROPPED with documented rationale + 1 net-new pre-launch bug-with-no-past-impact correction (AC family `DefaultCompensationModel`)

**Strategic context**: S35 operates under ROADMAP "Deployment Model" section + Phase 4e bullets committed 2026-05-18 — single logical deployment (150 institutions), glocal rule encoding (global interpretation, local-only on rule-delegated parameters), supersession-by-default + bug-correction-when-classified rule correction policy with NO per-institution opt-in/out. AC seed correction is the first concrete application of the bug-correction-when-classified policy under pre-launch posture (free correction window).

**Out-of-scope for S35** (deferred to S36+ multi-sprint program per `.claude/plans/PROGRAM-s36-s41-domain-correctness.md`): full source register + role-within-agreement modeling + ADR-024 + ADR-025 + ADR-027 (post-launch).

## Entropy Scan Findings

Run 2026-05-18 at sprint open (per WORKFLOW.md Step 0a):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-018 + ADR-019 + ADR-020 + ADR-017 + ADR-013 + ROADMAP Deployment Model + Phase 4e bullets all resolve cleanly post-S34 |
| Pattern compliance | CLEAN | S35 is established admin-strict If-Match pattern application (4th surface after S25 agreement_configs / position_override_configs / wage_type_mappings; users is the last unprotected admin write) |
| Orphan detection | DEBT (carry-forward from S34) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S35 uses non-worktree dispatch so non-blocking |
| Documentation drift | DRIFT-IDENTIFIED | SPRINT-34.md INDEX miscount: claimed 209 Docker-gated passing; true count is 208 (Overtime D-test failing at S34 close — Expected AFSPADSERING / Actual UDBETALING). Correction folded into TASK-3510 close. `danish-agreements.md` missing Compensation Model section (added by TASK-3504). |
| Quality grade review | SCHEDULED | Re-grade at TASK-3510. Backend API **A- → A** candidate (last unprotected admin write closed); Infrastructure stays A; Rule Engine stays A++ (unchanged); Security **B → B+** candidate; Domain Correctness new category — partial credit for AC seed fix; full grading deferred to S41 |
| Refinement disposition | READY | 2-cycle Step 4 dual-lens absorbed + Step 0b cycle 1 dual-lens absorbed; cycle-cap respected (2/2 per lens for refinement; 1/2 for Step 0b — mechanical-only absorptions per `feedback_dont_pause_for_reviews.md`) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1, P3, P7 — three rows touched plus first concrete application of ROADMAP rule correction policy) |
| **External Codex** | invoked 2026-05-18 — cycle 1: 3B/2W/2N |
| **Internal Reviewer** | invoked 2026-05-18 — cycle 1: 1B/2W/4N (divergent from Codex BLOCKERs — healthy dual-lens behavior, complementary not redundant) |
| **BLOCKERs resolved before Phase 1** | yes — 4 cycle-1 BLOCKERs (3 Codex + 1 Reviewer) all absorbed mechanically; no architectural forks; no user decisions required |

### Findings (cycle 1)

**External Codex (gpt-5.5)**:
- **BLOCKER 1** — TASK-3503 bug classification internally inconsistent ("NO × NO" framing collided with "inverts the rule" wording). **→ Absorbed**: clarified to "was-agreed: NO + materially-wrong-past-impact: NO" with explicit binary framework reference per ROADMAP rule correction policy.
- **BLOCKER 2** — TASK-3503 cross-domain authorization conflicts with validation criteria (read-only audit but "AC-pinned tests updated"). **→ Absorbed**: TASK-3503 scope tightened to read-only inspection + audit list production; test edits (if any surface) folded into TASK-3508/3509; AGENTS.md L35-36 + L44-57 governance respected.
- **BLOCKER 3** — TASK-3508 dependency on TASK-3503 is false (`EmployeeCompensationChoice` discriminator holds independently of `DefaultCompensationModel` seed). **→ Absorbed**: TASK-3508 dependency on TASK-3503 REMOVED; baseline-failure criterion rewritten to "stash the S34 cutover code, not the seed fix"; verified via init.sql:1099 (AC=FALSE) + L1111 (HK=TRUE).
- **WARNING 1** — TASK-3506 missing PUT response ETag validation. **→ Absorbed**: validation criterion added for PUT 200 response carrying `ETag: "<newVersion>"` header + body `version` matching `version_after`.
- **WARNING 2** — TASK-3503 source proof too weak for first ROADMAP correction-policy application. **→ Absorbed**: commit body template requires exact URLs/titles/sections + classifier name + verification date.
- **NOTE 1+2** — Confirmatory; no action.

**Internal Reviewer Agent**:
- **BLOCKER 1** — TASK-3502 cites `AuthEndpoints.cs` as 3rd `SupersedeAndCreateAsync` call site but AuthEndpoints only calls `GetCurrentAsync` (verified at L20+L47). **→ Absorbed**: dropped AuthEndpoints from TASK-3502 scope; "3 endpoint sites" → "2 endpoint sites + 1 seeder".
- **WARNING 1** — TASK-3506 precedent cite improvement — should cite `EmployeeProfileEndpoints.cs:282-291` (FOR-UPDATE re-read + audit + OCE catch with explicit `await tx.RollbackAsync(ct)`) instead of `WageTypeMappingEndpoints.cs:497`. **→ Absorbed**: cite swapped + explicit rollback discipline added.
- **WARNING 2** — TASK-3501 column name `audit_at` vs `employee_profile_audit` precedent `timestamp` (S31 era vs S34 era convention). **→ Absorbed**: precedent cite swapped from `employee_profile_audit` to `user_agreement_codes_audit` (init.sql:584).
- **NOTE 1-4** — S25 line-number freshness check (non-blocking); cross-domain label precision (informational); column DEFAULT decision (kept `'UDBETALING'` as documentary legacy fallback per explicit choice); SUPERSEDED forward-compat confirmed.

### Resolution

All 4 BLOCKERs + 4 WARNINGs absorbed mechanically in PLAN-s35.md. No architectural forks. No user decisions required. Per `feedback_dont_pause_for_reviews.md`, mechanical-only absorptions don't gate dispatch on a verification cycle. **Cycle 2 NOT dispatched** — scope unchanged by absorptions (all were spec clarifications + cite corrections + 1 phantom-site removal).

## Architectural Constraints Verified

- [x] **P1 — Architectural integrity** → 4th surface application of admin-strict If-Match contract (ADR-019 D2), closes the last unprotected admin write; pattern landscape stable (no net-new pattern)
- [x] **P3 — Event sourcing / auditability** → new `users_audit` table with version-transition columns per ADR-019 D8; admin PUT audit trail under concurrent admin now correctness-guaranteed via FOR-UPDATE + If-Match; Step 7a cycle 1 Reviewer W1+W2 absorption closed the last `existingUser` pre-tx-snapshot residuals on the Case A safety-net + PUT 200 response paths
- [x] **P7 — Security / access control** → admin-strict If-Match on `/api/admin/users` PUT (was missing); concurrent PUT race correctness pinned by TASK-3509 D-test 6; cross-org binding preserved; Step 7a close in-flight defect absorbed concurrent admin POST users_pkey 23505 → 409 (was bubbling as 500)
- [x] **NEW: ROADMAP rule correction policy first application** — AC family seed bug correction (TASK-3503) = first concrete classification + execution under the policy committed 2026-05-18; classifier (Orchestrator/Claude) + verification date (2026-05-18) + source URLs cited in TASK-3503 commit body

## Task Log

11 declared tasks (TASK-3500..3510) across 5 phases. Plan file `.claude/plans/PLAN-s35.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3500 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3500 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s35.md`, `.claude/plans/PROGRAM-s36-s41-domain-correctness.md`, `docs/sprints/SPRINT-35.md` (this file), `docs/sprints/INDEX.md`, `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (sync-debt housekeeping per Step 0b cycle-3 Reviewer advisory) |
| **Dependencies** | none |

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s35.md` exists with 11-task decomposition
- [x] `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` exists with 7-sprint program
- [x] `docs/sprints/SPRINT-35.md` exists (this file)
- [ ] `docs/sprints/INDEX.md` has Sprint 35 row (status=in-progress)
- [ ] REFINEMENT cycle-1 vestigial framing reconciled (3 sites: L56, L163, L214)
- [ ] Sprint-open commit lands atop `328a027`

---

### Phase 1 — Sequential Foundation (5 tasks)

(Per-task detail in PLAN-s35.md.)

- TASK-3501 — Schema migration `s35-d1-users-version` + `users_audit` table
- TASK-3502 — `UserAgreementCodeRepository.SupersedeAndCreateAsync` Case A 23505 catch + 409 mapping
- TASK-3503 — AC family compensation seed bug fix (pre-launch bug-with-no-past-impact)
- TASK-3504 — `docs/references/danish-agreements.md` Compensation Model section
- TASK-3505 — `UserRepository.GetByIdWithVersionAsync` methods (in-tx FOR-UPDATE + non-tx variants)

### Phase 2 — Parallel Cutovers (3 dispatch slots, non-worktree)

(Per-task detail in PLAN-s35.md.)

- TASK-3506 — AdminEndpoints `{GET, PUT, POST} /api/admin/users` — admin-strict If-Match + version bumping + users_audit (single-agent serial; same file)
- TASK-3507 — Frontend admin UI migration to `apiFetchWithEtag<T>` + banner-with-retry
- TASK-3508 — Overtime D-test rewrite to PUT compensation-choice (strong discriminator via 400/200)

### Phase 3 — D-Tests

#### TASK-3509 — Docker-gated D-test suite (~8 tests)

Per-task detail in PLAN-s35.md.

### Phase 4 — Sprint Close

#### TASK-3510 — Sprint close

Per-task detail in PLAN-s35.md. ROADMAP Phase 4e: 5 items RESOLVED + 1 DROPPED + AC seed correction documented as first concrete application of rule correction policy.

## Legal & Payroll Verification (TASK-3510)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | AC family `DefaultCompensationModel` UDBETALING → AFSPADSERING corrects encoding to match AC overenskomst cirkulære §4. Sources cited in TASK-3503 commit body. |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | TASK-3508 rewrites the failing-at-S34-close Overtime D-test with a strong PUT-based discriminator; pinning the S34 dated-lookup cutover, not the AC seed. |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | N/A | No rule-engine input surface changes (AC seed correction is forward-only per bug-with-no-past-impact pre-launch classification) |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | 2026-05-20 |
| **Sprint-start commit base** | `328a027` (S34 close) |
| **Command** | `codex exec -s read-only` against full diff `328a027..HEAD` + Reviewer Agent in parallel |
| **Review Cycles** | 2 per lens (cycle-cap respected) |

### Cycle 1 — dual-lens against `6029e36` (TASK-3509 close)

**External Codex (gpt-5.5)**: 1 BLOCKER + 1 WARNING + 1 NOTE — verdict **BLOCKED**.
- **BLOCKER-1** — `users.version` greenfield-only migration. The base CREATE TABLE block at `init.sql:467` bakes the column for fresh databases, but the IF NOT EXISTS CREATE is a no-op on legacy DBs, and the greenfield-only ledger insert at L627-629 still wrote `s35-d1-users-version-and-audit` ledger row — so legacy DBs would never receive the column and PUT `version = version + 1` would 500.
- **WARNING-1** — `/api/admin/organizations/{orgId}/users` list endpoint projection missing `primaryOrgId` + `version`; frontend table at `UserManagement.tsx:368` renders empty cells; frontend `User` interface required version (lied).
- **NOTE-1** — confirmatory; core PUT concurrency shape sound.

**Internal Reviewer Agent**: 0 BLOCKER + 2 WARNING + 10 NOTE — verdict **APPROVED WITH WARNINGS**.
- **W1** — Case A safety-net `UserAgreementCodeChanged.OldAgreementCode` at `AdminEndpoints.cs:1109` still read pre-tx `existingUser.AgreementCode` instead of the FOR-UPDATE'd `lockedUser.AgreementCode`. Item #4 (outer-users-UPDATE stale-snapshot) was closed on the dominant path but the safety-net residual remained.
- **W2** — PUT 200 response body at `AdminEndpoints.cs:1217-1219` sourced fields from `request.X ?? existingUser.X` outside the try block instead of the four locked-row-derived `newX` locals. Smell, not bug (If-Match invariant makes `existingUser` converge with `lockedUser` for the 200 path), but inconsistent with the EmployeeProfileEndpoints precedent and load-bearing on a global invariant a local reader cannot verify.
- **10 NOTEs** — mostly confirmatory; documentation accuracy + test-fixture honesty + frontend coverage observations.

**Resolution (cycle 1)** — commit `d1f5a72`:
- Codex B1 absorbed: removed greenfield-only ledger insert at L625-629; added guarded ALTER block at end of init.sql with `ALTER TABLE users ADD COLUMN IF NOT EXISTS version` + ledger INSERT + IF NOT FOUND guard, mirroring S22/S25 pattern.
- Codex W1 absorbed: new `UserRepository.GetByOrgWithVersionAsync` method returning `IReadOnlyList<(User, long Version)>`; list endpoint projection now includes both `primaryOrgId` and `version`.
- Reviewer W1 absorbed: substituted `lockedUser.AgreementCode` for `existingUser.AgreementCode` at L1109 — closes Case A safety-net residual.
- Reviewer W2 absorbed: hoisted four `newX` locals above the try block (declared with `existingUser` seed for definite assignment), assigned inside the try from `request.X ?? lockedUser.X`, used them in the 200 response body. Mirrors EmployeeProfileEndpoints precedent.
- Test fixture `S35VersionMigrationTests.cs` xmldoc rewritten to describe the new init.sql topology.

### Cycle 2 — verification against `d1f5a72`

**External Codex (gpt-5.5)**: 1 BLOCKER + 3 NOTE — verdict **BLOCKED**.
- **BLOCKER-1** (cycle-2 missed-facts, finite per `feedback_thrash_defer_real_world.md`) — ledger-poisoned legacy DBs from the pre-cycle-1 form would have the `s35-d1-users-version-and-audit` ledger row WITHOUT `users.version`; the cycle-1 guarded block then short-circuits on `IF NOT FOUND` and never repairs the column.
- **3 NOTEs** — confirmatory: Codex W1 / Reviewer W1 / Reviewer W2 closures all verified.

**Internal Reviewer Agent**: 0 BLOCKER + 0 WARNING + 4 NOTE — verdict **APPROVED**.
- All 4 cycle-1 absorptions verified correct; the 4 NOTEs are documentation drift and stylistic, non-blocking.

**Resolution (cycle 2)** — commit `3ca1eaf`:
- Codex B1-cycle-2 absorbed: moved the `ALTER TABLE users ADD COLUMN IF NOT EXISTS version` ABOVE the `IF NOT FOUND THEN RETURN` guard so it runs unconditionally. ADD COLUMN IF NOT EXISTS is idempotent so unconditional execution is safe; the ALTER now repairs ledger-poisoned legacy DBs as well as greenfield + clean-legacy paths. The ledger INSERT + guard still bound any FUTURE one-shot work for this migration to a single run.
- **Cycle-cap reached** (2/2 per lens). Per `feedback_thrash_defer_real_world.md` the cycle-2 finding was finite missed-facts (same defect family — init.sql migration ordering — well-bounded fix). No cycle 3 authorized.

### Step 7a close — in-flight defect absorption (commit `874c1a9`)

Sprint-close Docker-gated test run surfaced 2 S35-introduced D-test failures that static review (4 cycle-passes) did not catch — same shape as S29 TASK-2912 / S33 TASK-3312 in-flight defect absorption pattern:

- **AdminEndpoints POST `/api/admin/users`** — race on `users_pkey` 23505 fires BEFORE the user_agreement_codes Case A 23505 in concurrent admin POSTs. TASK-3502's `ConcurrentSeedConflictException` catch never gets a chance to run on this path. Added explicit `PostgresException SqlState=23505` catch before the generic catch, mapping to 409 with structured body + `ConstraintName` in the log line. Symmetric with the pre-flight 409 path.
- **Seeder_ConcurrentStartup test** — used `_harness.EventStore` which constructs `PostgresEventStore(factory)` without an `OutboxServiceContext`. Fixed in-test by constructing a local `PostgresEventStore(factory, new OutboxServiceContext("backend-api"))` per the SkemaProjectionAtomicTests + ProfileRowVersionTests precedent.

Both fixes mechanical. Build clean; Docker-gated count went 216 → 218 passing.

## Test Summary

Per `sprint-test-validation` skill run 2026-05-20 at sprint close.

| Suite | Previous (S34 corrected) | Current (S35) | Delta |
|-------|--------------------------|---------------|-------|
| Unit | 526 | 526 | +0 |
| Plain regression | 35 | 35 | +0 |
| Docker-gated (passing) | 208 | 218 | +10 |
| Frontend vitest | 88 | 90 | +2 |
| **Total passing** | **857** | **869** | **+12** |

**S34 baseline correction**: S34's INDEX row claimed 858 passing; cycle-2 verification surfaced that the Overtime D-test (`Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive`) was actually failing at S34 close (Expected `AFSPADSERING` / Actual `UDBETALING` — the AC family compensation seed bug TASK-3503 fixed in S35). True S34 baseline = 857. S35 deltas computed against the corrected baseline. Correction documented in this row + SPRINT-34.md note at top.

**Docker-gated failures at S35 close**: 26 of 244 total (218 passing). All 26 are pre-existing pre-S35 baseline (TxContractTests + Segmentation + Manifest + WAF<Program> + flaky harness paths documented in S29/S30/S31/S33/S34 INDEX entries — Phase 4e candidates per pre-launch posture). Zero S35-introduced failures at close.

## Sprint Retrospective

### What went well

- **4th application of the established admin-strict If-Match pattern** (after S25 agreement_configs / position_override_configs / wage_type_mappings) on the last unprotected admin write surface (`/api/admin/users`). Zero net-new architectural patterns; ADR-019 D2 landscape stable.
- **First ROADMAP rule-correction-policy application** — AC family `DefaultCompensationModel` (`UDBETALING` → `AFSPADSERING`) classified as bug-with-no-past-impact under pre-launch posture, source-cited (AC overenskomst cirkulære 043-19 §4), forward-only with no past-period impact. Clean precedent for future seed corrections.
- **Step 7a 2-cycle dual-lens worked as designed** — cycle 1 caught 4 findings convergent across two lenses (Codex B1 + W1; Reviewer W1 + W2); cycle 2 caught a finite cycle-1 missed-facts (ledger-poisoned legacy DBs) per the established `feedback_thrash_defer_real_world.md` defect-topography heuristic. Lens divergence in cycle 1 was healthy (Codex found production-readiness; Reviewer found audit-trail residuals).
- **In-flight defect absorption pattern continues to surface real defects** — sprint-close Docker-gated run caught 2 S35-introduced failures that 4 cycle-passes of static review missed. Marquee suite remains load-bearing. Same shape as S29 TASK-2912 / S33 TASK-3312 precedent.
- **Mechanical absorption discipline** — all 4 Step 7a cycle-1 findings, both cycle-2 findings, and both close-time in-flight defects were mechanical fixes (no architectural forks, no user-strategic decisions during cycles). Cycle-cap (2 per lens) respected.

### What was hard

- **First Codex run hung** — initial Bash invocation of `codex exec` with `cat | codex` pattern under Windows-backslash paths failed silently; output file stayed 0 bytes after ~4 min with the codex process showing 0.06s of CPU. Retry with `Get-Content -Raw | codex - | Tee-Object` worked correctly. Pattern for Windows: prefer PowerShell stdin pipe + literal-path args over Bash + path-interpolated `cat`.
- **Cycle-2 missed-facts on the same defect topography** — Codex's cycle-1 BLOCKER absorption introduced a new BLOCKER in the same area (legacy-DB upgrade path → ledger-poisoned DBs). This is the expected `feedback_thrash_defer_real_world.md` finite missed-facts pattern; resolved via well-bounded one-line move-above-the-guard.
- **2 D-test failures escaped 4 cycle-passes of static review** — neither Codex nor Reviewer ran the actual suite; both did static analysis only. The POST users_pkey 23505 catch gap was visible in the source but the race-ordering wasn't obvious without dynamic verification. Reinforces the marquee-suite-as-final-arbiter convention.

### Trust calibration

- Mechanical absorption of cycle 1 fixes (4 findings) was correct on first pass; cycle 2 verification caught the finite missed-facts. No architectural reframe needed at any cycle.
- Lens divergence in cycle 1 (Codex graded BLOCKED; Reviewer graded APPROVED WITH WARNINGS) was diagnostic — Codex's stricter grading on production-readiness vs Reviewer's stricter grading on audit-trail invariants — neither lens was wrong; both findings were real and complementary. The combined dual-lens output covered the full defect surface.
- The 2 D-test failures at close are NOT a trust-calibration concern — they're the established in-flight-defect pattern that the marquee suite is designed to catch. The fact that this pattern is now showing on a 4th sprint in a row (S29/S33/S34/S35) suggests it's an intrinsic property of complex-tx + race-shaped sprints rather than a process gap.
- Step 7a 2-cycle cap held cleanly. Per the discipline, the cycle-2 finite missed-facts was the natural stopping point — moving the ALTER above the guard is a clean fix without architectural impact.
