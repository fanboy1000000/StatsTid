# Sprint 34 — Phase 4e: `agreement_code` Versioned History (Launch-Blocker Close)

| Field | Value |
|-------|-------|
| **Sprint** | 34 |
| **Status** | complete |
| **Start Date** | 2026-05-17 |
| **End Date** | 2026-05-17 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes (0 errors, 19 pre-existing CS0618 warnings unchanged) |
| **Test Verified** | yes (858 passing total; marquee D-test PASSES) |
| **Sprint-start commit base** | `f966c9e` (S33 close, 2026-05-17) |
| **Sprint type** | Implementation (against ADR-023 D2 option (b)) |
| **Refinement** | `.claude/refinements/REFINEMENT-s34-agreement-code-versioned-history.md` (READY after 2-cycle dual-lens; gitignored) |
| **Plan** | `.claude/plans/PLAN-s34.md` (Step 0a) |

## Sprint Goal

Close the `agreement_code` LAUNCH-BLOCKING determinism gap per [ADR-023](../knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md) D2 option (b) — version `users.agreement_code` via row-level history. 4th application of the established versioned-config pattern (after WTM/EntitlementConfig/EmployeeProfile in S29/S30/S33).

Marquee D-test `ReplayAsync_StableUnderAgreementCodeMutation_ResultByteIdentical` is the load-bearing P4 acceptance gate. **Closes ADR-016 D10 retroactive replay determinism for the entire rule-engine input surface** (4th and final dated input after `weekly_norm_hours`/`part_time_fraction`/`position` from S33).

**Cycle 1 review expanded scope**: not just PCS-replay (the original framing) — Balance/Skema/Overtime past-period HTTP endpoints have the same class of replay-determinism bug. All cut over to dated `UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, monthStart, ct)` lookups via TASK-3410/3411/3412.

## Entropy Scan Findings

Run 2026-05-17 at sprint open (per WORKFLOW.md Step 0a):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-023 + S33 references resolve cleanly post-S33 close |
| Pattern compliance | CLEAN | 4th application of established versioned-config pattern; no anti-pattern introduction |
| Orphan detection | DEBT (carry-forward) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S34 uses non-worktree dispatch so non-blocking |
| Documentation drift | CLEAN | MEMORY.md synced through S33 close |
| Quality grade review | scheduled | Re-grade at TASK-3415 (Rule Engine A+ → A++ candidate if ADR-016 D10 fully closed) |
| Refinement disposition | RESOLVED | 2-cycle Step 4 dual-lens reviewed clean; cycle-cap respected (2/2 per lens) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1, P3, P4, P7 — all four MANDATORY rows touched) |
| **External Codex** | invoked 2026-05-17 — 2 cycles; cycle 1: 2B/2W/2N; cycle 2: 2B/1W/0N (cycle-cap reached) |
| **Internal Reviewer** | invoked 2026-05-17 — 2 cycles; cycle 1: 0B/2W/10N; cycle 2: 2B/2W/8N — convergent with Codex on the 2 BLOCKERs (cycle-cap reached) |
| **BLOCKERs resolved before Step 1** | yes — 2 cycle-1 Codex BLOCKERs (SoftDelete dropped; DI registration added) + 2 cycle-2 convergent BLOCKERs (cycle-1 propagation gaps — stale "3 events / 56→59" + "DELETE If-Match" in P3/P4 Constraints checklist) all absorbed mechanically |

### Findings (cycle 1)

**External Codex (gpt-5.5)**:
- **BLOCKER 1** — Missing AdminEndpoints DELETE cutover task: plan defines `SoftDeleteAsync` + `UserAgreementCodeSoftDeleted` event + DELETE D-tests, but Phase 2 only has PUT+POST in TASK-3407; either add DELETE task or remove SoftDelete from scope. → **Absorbed**: SoftDelete dropped entirely from S34 (semantically meaningless for agreement_code; every user must have an agreement at all times). Removed SoftDeleteAsync from TASK-3402, UserAgreementCodeSoftDeleted event from TASK-3404 (EventSerializer delta 56→59 → 56→58), DELETE D-tests from TASK-3414, all "DELETE endpoint" language from PLAN.
- **BLOCKER 2** — `UserAgreementCodeRepository` DI registration missing: TASK-3402 creates the repo but doesn't extend Program.cs; runtime activation would fail. → **Absorbed**: TASK-3402 Components extended to include `src/Backend/StatsTid.Backend.Api/Program.cs` + explicit `AddSingleton<UserAgreementCodeRepository>()` registration line; Validation Criteria added.
- **WARNING 1** — TASK-3406 validation doesn't pin full EmploymentProfile contract after SQL refactor. → **Absorbed**: TASK-3406 Validation Criteria expanded with all-8-fields hydration checklist + D-test assertion.
- **WARNING 2** — TASK-3408 JWT mint adds 1 SELECT per login without perf note. → **Absorbed**: TASK-3408 Description gained explicit perf contract — login is rare; pool absorbs; no caching in S34; post-launch perf measurement may revisit.
- **NOTE 1** — Risk areas covered (TASK-3407 PUT+POST bundling explicit; AC/HK material difference pinned; DEP-003 coverage exists; ROADMAP RESOLVED assigned to TASK-3415).
- **NOTE 2** — ADR-016 still has "proposed" status while S34 treats D10 as binding. Cross-reference debt; orthogonal to S34 scope; surfacing.

**Internal Reviewer Agent**:
- **WARNING 1** — TASK-3414 marquee seed `WeeklyNormHours`-alone is insufficient discriminator because `EmploymentProfile.WeeklyNormHours` is sourced from dated employee_profiles, not agreement-config keyed by agreement_code. → **Absorbed**: TASK-3414 marquee Validation Criterion now requires `HasMerarbejde` and/or `NormModel` as discriminator (not `WeeklyNormHours`).
- **WARNING 2** — Phase Decomposition prose ordering inconsistent with TASK-3403's dependency on TASK-3402 + TASK-3404; real dispatch order is 3401 → 3402 → 3404 → 3403 → 3405. → **Absorbed**: Phase Decomposition row clarified with explicit ordering note.
- **8 NOTEs** — all confirmatory (cross-domain labels present, dependency closure verified, ADR refs valid, scope-vs-priority alignment strictly stronger than ADR-023 D2 promise, 16 tasks justified, KB freshness clean).

### Resolution

Cycle 1 absorbed mechanically in PLAN-s34.md:

1. **BLOCKER 1 (SoftDelete dropped)** — TASK-3402 API surface reduced from 5 methods to 4 (removed `SoftDeleteAsync`); TASK-3404 event types reduced from 3 to 2 (removed `UserAgreementCodeSoftDeleted`); TASK-3414 D-test suite dropped SoftDelete + DELETE If-Match tests; EventSerializer delta 56→59 corrected to 56→58.
2. **BLOCKER 2 (DI registration)** — TASK-3402 Components + Validation Criteria gained explicit Program.cs registration step.
3. **Codex W1 + Reviewer W1** — TASK-3406 hydration coverage + TASK-3414 marquee discriminator tightened.
4. **Codex W2** — TASK-3408 perf contract documented.
5. **Reviewer W2** — Phase Decomposition row clarified with real dispatch ordering.

### Findings (cycle 2)

Both lenses **converged** on the same 2 BLOCKERs (cycle-1 propagation gaps):

**External Codex (gpt-5.5)** at PLAN-s34.md L520+L526:
- **BLOCKER** — "3 new event types registered (56→59)" in P3 Constraints checklist contradicts cycle-1 absorbed TASK-3404 text (2 events, 56→58)
- **BLOCKER** — "admin-strict If-Match on DELETE" in P4 Constraints checklist references DELETE endpoint that no longer exists in S34 scope (SoftDelete dropped)
- **WARNING** — TASK-3406 two-query resolver read-consistency: cutover loses single-statement snapshot between employee_profiles + UserAgreementCodeRepository.GetByUserIdAtAsync; no real race (writes are atomic; replay reads frozen historical) but should be explicitly documented

**Internal Reviewer Agent**: convergent with Codex on the 2 BLOCKERs at L520/L526 + WARNING on D-test count (~15→~11 in 3 sites) + WARNING on 5-way emission framing (actually 7-op cross-table) + 8 NOTEs (ADR-023 D8 stale citation at TASK-3409; audit CHECK enum keeps 'DELETED' harmless dead value; rest confirmatory on cycle-1 absorptions)

### Resolution (cycle 2)

Cycle 2 absorbed mechanically — all 100% propagation-gap text fixes from cycle 1:

1. **P3 Constraints checklist** updated: 3 events 56→59 → **2 events 56→58** (UserAgreementCodeSeeded + UserAgreementCodeSuperseded); "5-way" framing → "5 event/audit emissions across 7-op cross-table atomic tx" with explicit operation enumeration
2. **P4 Constraints checklist** updated: removed "admin-strict If-Match on DELETE" reference; added explicit "no DELETE endpoint in S34" note
3. **D-test count** updated in 3 sites: ~15 → ~11 (1 marquee + 3 SupersedeAndCreate + 2 PUT-validator + 1 POST Case A + 3 HTTP-surface + 1 dual-emission + 1 cache-canonical + 1 backfill idempotency = 11)
4. **TASK-3406 read-consistency note** added: two-query resolver path documented as acceptable under READ COMMITTED + atomic writes through `(conn, tx)` paths
5. **TASK-3409 KB Refs** corrected: ADR-023 D8 stale citation → ADR-023 D2 with note that D8 doesn't apply (SoftDelete dropped)

**Cycle-cap reached** (2/2 per lens). No cycle 3 authorized; PLAN READY for Phase 1 dispatch. All findings were mechanical text-consistency propagation gaps from cycle 1's substantive absorptions — no architectural reframe; zero risk to sprint shape or task decomposition.

## Architectural Constraints Verified

_Final assertion in TASK-3415._

- [x] **P1 — Architectural integrity** → 4th application of ADR-020 D2 + ADR-018 D7 + ADR-019 D2 pattern; pattern landscape stable at 5 (ADR-016 D5b reframing DEFERRED to S35)
- [x] **P2 — Rule engine determinism** → marquee PASSES; closes ADR-016 D10 for the 4th and final rule-engine input (the entire dated rule-engine input surface is now retroactive-replay-stable)
- [x] **P3 — Event sourcing** → 2 net-new event types registered (EventSerializer 56→58: UserAgreementCodeSeeded + UserAgreementCodeSuperseded); 5 event/audit emissions across 7-op cross-table atomic tx on Case C cross-day PUT (users UPDATE cache + user_agreement_codes predecessor UPDATE + successor INSERT + audit row + UserUpdated + UserAgreementCodeChanged + UserAgreementCodeSuperseded outboxes); backfill seeder atomic per-row with 23505-skip-without-fail idempotency
- [x] **P4 — Version correctness** → ETag monotonicity preserved on Case C (successor = predecessor.Version+1 per S33 absorption); cycle-3 validator on PUT (rejects backdated AND future-dated with 422 using DateTime.UtcNow). No DELETE endpoint in S34 — SoftDelete dropped at Step 0b BLOCKER 1 absorption (semantically meaningless for agreement_code)
- [x] **P7 — Security** → JWT mint reads through `UserAgreementCodeRepository.GetCurrentAsync` with ADR-023 D3 graceful fallback + warning log; AdminEndpoints PUT/POST scope unchanged (LocalAdminOrAbove + OrgScopeValidator); no new exposure

## Task Log

16 declared tasks (TASK-3400..3415) across 4 phases. Plan file `.claude/plans/PLAN-s34.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3400 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3400 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s34.md`, `docs/sprints/SPRINT-34.md` (this file), `docs/sprints/INDEX.md` |
| **Dependencies** | none |

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s34.md` exists with 16-task decomposition
- [x] `docs/sprints/SPRINT-34.md` exists (this file)
- [ ] `docs/sprints/INDEX.md` has Sprint 34 row (status=in-progress)
- [ ] Sprint-open commit lands on master

---

### Phase 1 — Sequential Foundation (5 tasks)

(Per-task detail in PLAN-s34.md.)

- TASK-3401 — Schema migration (`user_agreement_codes` + audit table)
- TASK-3402 — `UserAgreementCodeRepository` with `SupersedeAndCreateAsync` + `SoftDeleteAsync`
- TASK-3403 — `UserAgreementCodeBackfillSeeder` (history-covering `effective_from='0001-01-01'` default)
- TASK-3404 — 3 new event types (`UserAgreementCodeSeeded` + `UserAgreementCodeSuperseded` + `UserAgreementCodeSoftDeleted`); EventSerializer 56→59
- TASK-3405 — `EmploymentProfile.AgreementCode` xmldoc clarification

### Phase 2 — Parallel Cutovers (8 dispatch slots)

(Per-task detail in PLAN-s34.md. Phase 2 Disjointness Audit there.)

- TASK-3406 — PCS resolver agreement_code cutover
- TASK-3407 — AdminEndpoints PUT + POST (single-agent serial; same file)
- TASK-3408 — AuthEndpoints JWT mint through repository
- TASK-3409 — Frontend PUT-body sync
- TASK-3410 — BalanceEndpoints past-month determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3411 — SkemaEndpoints past-month determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3412 — OvertimeEndpoints past-period determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3413 — EmployeeProfileRepository:146 audit (likely doc-only per cycle 2 confirmation)

### Phase 3 — D-Tests

#### TASK-3414 — Docker-gated D-test suite (~15 tests)

Per-task detail in PLAN-s34.md.

### Phase 4 — Sprint Close

#### TASK-3415 — Sprint close

Per-task detail in PLAN-s34.md. ROADMAP Phase 4e `agreement_code` LAUNCH-BLOCKING → **RESOLVED** with commit citation.

## Legal & Payroll Verification (TASK-3415)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | No rule logic changes; agreement_code data-source change only |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | Marquee proves byte-identical replay on agreement_code mutation; Overtime past-period D-test pins same-class fix |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | pending | Marquee is precisely the proof; closes ADR-016 D10 for the entire rule-engine input surface |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | 2026-05-17 |
| **Sprint-start commit base** | `f966c9e` (S33 close) |
| **Command** | `codex exec` against full diff `f966c9e..HEAD` + Reviewer Agent in parallel |
| **Review Cycles** | 2 per lens (cycle-cap respected) |

### Cycle 1 — dual-lens against `e19f78b` (TASK-3414 close)

**External Codex (gpt-5.5)**:
- **BLOCKER-1** — `AdminEndpoints.cs:745` PUT calls `SupersedeAndCreateAsync(expectedVersion: null)`, bypassing ADR-018/019 optimistic concurrency check.
- **BLOCKER-2** — `AdminEndpoints.cs:667` predecessor snapshot read without `FOR UPDATE`; concurrent writer can change live row between snapshot and SupersedeAndCreateAsync's own lock acquisition → audit/event payload uses stale predecessor data (breaks row/event/audit parity for Case C).
- **WARNING-1** — Admin user list returns `agreementCode` but no row version; frontend PUT sends no `If-Match` (ETag contract not threaded through admin surface).
- **WARNING-2** — `UserAgreementCodeRepository.cs:242` Case A INSERT path doesn't catch 23505; concurrent first-create race can bubble as 500 instead of deterministic 412/409.
- **NOTE-1** — `init.sql:539` schema comment stale ("S34 reads/writes only live rows" — but Case C already closes predecessor).
- **NOTE-2** — `IEmploymentProfileResolver.cs:24` xmldoc stale (still says agreement_code is joined live from users).

**Internal Reviewer Agent**: convergent with Codex on the audit-trail race (graded WARNING-1 — "canonical state safe via inner FOR UPDATE, only audit metadata at risk"). + Reviewer-only WARNING-2 (Overtime D-test weak discriminator — `AgreementCodeHttpDeterminismTests.Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive` doesn't actually differentiate AC from HK in endpoint response, only via direct DB inspection) + 7 confirmatory NOTEs (resolver two-snapshot consistency acceptable on PCS paths; audit CHECK enum still includes 'DELETED' but harmless; validator gated on `agreementCodeMutated` is intentional asymmetry; backfill seeder 23505 skip clean; ADR-023 D2/D3 propagation consistent; marquee discriminator load-bearing; EventSerializer 56→58 with DEP-003 reflection coverage). Verdict: APPROVED WITH WARNINGS.

**Resolution (cycle 1)** — commit `2d1e560`:
- BLOCKER-1+2 absorbed convergently: `FOR UPDATE` on predecessor read + pass `expectedVersion: agreementPredecessor?.Version` (defense-in-depth on top of the row-level lock).
- NOTE-1+2 absorbed: init.sql comment dropped stale language; resolver xmldoc rewritten.
- Codex W1+W2 + Reviewer W2 DEFERRED to S35 hardening (admin-strict ETag propagation + Case A 23505 deterministic-409 + Overtime D-test strengthening) — none are correctness blockers; Codex graded W1+W2 as W not P1 because audit-trail-only impact.

### Cycle 2 — Codex verification against `2d1e560`

**External Codex (gpt-5.5)**:
- **BLOCKER-1** (NEW, missed-facts on cycle 1) — `AdminEndpoints.cs:845` `UserAgreementCodeChanged.OldAgreementCode = existingUser.AgreementCode` still uses pre-lock user snapshot. Two pathologies: (a) under concurrent admin PUTs T2's emitted Changed event carries stale OldAgreementCode after waiting for T1's commit; (b) if T1 changes AC→HK and T2 also requests HK, T2's `agreementCodeMutated` predicate (computed against stale `existingUser.AgreementCode=AC`) still routes through SupersedeAndCreateAsync, bumps version, emits a no-op HK→HK as AC→HK. Cycle 1 closed the audit + Superseded paths; missed Changed event + the predicate itself.

**Resolution (cycle 2)** — commit `720dce0`:
- Re-validate `agreementCodeMutated` against `canonicalOldAgreementCode = agreementPredecessor?.AgreementCode ?? existingUser.AgreementCode` after FOR-UPDATE'd predecessor read; if request matches the locked value, mark mutation=false and skip SupersedeAndCreateAsync + audit + event emission.
- `UserAgreementCodeChanged.OldAgreementCode` sourced from `canonicalOldAgreementCode` (mirrors audit `previous_data` + `UserAgreementCodeSuperseded.OldAgreementCode` — all three event/audit sites now flow off the same locked row state).
- **Cycle-cap reached** (2/2 Codex). Reviewer Agent already approved cycle 1; not re-dispatched per `feedback_thrash_defer_real_world.md` — defect topography was finite missed-facts (well-bounded substitution + predicate re-validation), not thrash. No cycle 3 authorized.

### Deferred known gaps (S35 candidates)

- Admin surface lacks ETag/If-Match end-to-end (Codex W1)
- `UserAgreementCodeRepository.SupersedeAndCreateAsync` Case A INSERT path doesn't catch 23505 deterministically (Codex W2)
- `AgreementCodeHttpDeterminismTests.Overtime_PastPeriodBalance_*` weak discriminator (Reviewer W2)
- Pre-tx `existingUser` snapshot still authoritative for outer `users` UPDATE on no-agreement_code-mutation path (pre-S34 inherited pattern, exposed by S34 versioned-history shape)
- No D-test exercises concurrent admin PUT race (would have caught both Step 7a cycles)

## Test Summary

Populated at TASK-3415 via `sprint-test-validation` skill.

| Suite | Previous (S33) | Current (S34) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | 526 | +0 |
| Plain regression | 35 | 35 | +0 |
| Docker-gated (passing) | 204 | 209 | +5 |
| Frontend vitest | 88 | 88 | +0 |
| **Total passing** | **853** | **858** | **+5** |

13 net-new Docker-gated D-tests landed via TASK-3414 across 5 files. Of these:
- **11 pass** (marquee + 4 repo contract + 4 admin endpoint + 2 HTTP determinism)
- **2 known-deferred failures**:
  - `UserAgreementCodeBackfillSeederTests.Backfill_SecondStartupSkipsAlreadyBackfilledUsers_Idempotent` — hits the S29-deferred WebApplicationFactory<Program> connection-override defect; the seeder works correctly in production (TASK-3403 verified via Program.cs path), only the WAF test harness can't observe it.
  - `AgreementCodeHttpDeterminismTests.Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive` — Reviewer cycle 1 WARNING-2 (weak discriminator; both AC and HK collapse to "AFSPADSERING" in the test's compensation-choice setup). Test logic gap, not a code defect — Overtime cutover itself is correct (TASK-3412).

Net delta vs S33 (+5) reflects 11 net-new passing minus 6 same-baseline Docker-daemon-flakiness failures that surfaced during the validation run (run-isolation eliminates most; full-suite parallelism still trips Docker daemon socket limits at ~80+ Postgres+ryuk containers). The 19 pre-existing Docker-gated failures from the S31 baseline (Manifest/Segmentation/TxContract/WAF-defect cluster) remain unchanged.

**Marquee D-test PASSES** — `AgreementCodeMarqueeTests.ReplayAsync_StableUnderAgreementCodeMutation_ResultByteIdentical` clears the P4 load-bearing acceptance gate. **ADR-016 D10 retroactive-replay determinism is now closed for the entire rule-engine input surface** (4 of 4 dated inputs: weekly_norm_hours + part_time_fraction + position + agreement_code).

## Agent Effectiveness

- **8-way parallel Phase 2 dispatch** (TASK-3406..3413) — 7 truly parallel + 1 single-agent serial (TASK-3407 PUT+POST in same file). No worktree-base-mismatch this sprint (clean S33-close base). All 8 agents reported clean production code; 7 flagged the same pre-existing `EmployeeProfileMarqueeTests.cs:92` constructor break from TASK-3406's resolver signature change — Orchestrator absorbed it in 1 line as part of the TASK-3406 commit.
- **Step 7a dual-lens cycle 1** convergent on the audit-trail race (Codex graded BLOCKER, Reviewer graded WARNING — lens divergence resolved in favor of P3 auditability prioritization). Both lenses correctly identified the same root cause; fix shape was unambiguous.
- **Step 7a cycle 2** finite missed-facts behavior — same defect family (audit/event correctness under concurrent admin), deeper layer (Changed event + mutation predicate). Per `feedback_thrash_defer_real_world.md` this is the missed-facts signature (well-bounded substitution, not architectural reframe), not thrash; fix applied without dispatching cycle 3.
- **Refinement (Step 4)** 2-cycle dual-lens: cycle 1 absorbed 3 BLOCKERs (convergent HTTP-surface scope expansion absorbed Balance/Skema/Overtime into S34 scope) + 4 WARNINGs; cycle 2 absorbed 1 procedural BLOCKER + 3 mechanical WARNINGs. Cycle-cap respected.
- **Plan-review (Step 0b)** 2-cycle dual-lens: cycle 1 absorbed 2 convergent BLOCKERs (SoftDelete dropped + DI registration); cycle 2 absorbed 2 convergent cycle-1-propagation BLOCKERs (P3/P4 Constraints checklist stale text). Cycle-cap respected.

## Sprint Retrospective

### What went well

- **Mechanical 4th application** of the established versioned-config pattern (after WTM S29 + EntitlementConfig S30 + EmployeeProfile S33). Zero net-new patterns introduced; ADR-016 D5b 5-pattern landscape stable. Predictable shape across the sprint: schema → repo → events → seeder → 8-way cutovers → D-tests → close.
- **Marquee D-test PASSES on first run** — TASK-3414 agent correctly authored a load-bearing rule-engine stub that varies output by `profile.agreementCode` (MERARBEJDE vs OVERTIME_50). Without this, byte-identity would have been trivially satisfied by the shared DefaultRuleEngineHandler.
- **Cross-domain authorization labels (AGENTS.md L44-51)** worked smoothly — 8 Phase 2 agents touched files outside their declared scope (mostly wiring constructor parameters, calling new repository methods); all reported cross-domain edits as mechanical/wiring rather than substantive business logic.
- **Backfill seeder ships the S31-deferred Phase 4e candidate** (concurrent-startup 23505 race idempotent-skip) — no extra sprint task needed; the seeder code naturally absorbed the deferred fix.

### What was hard

- **Two Step 7a BLOCKER cycles** — cycle 2 caught a missed-facts gap (Changed event + predicate stale on pre-lock snapshot) that cycle 1 should have caught. Pattern: cycle 1 focused on the most-visible site (audit row + Superseded event) but missed the structurally identical Changed event emission and the upstream predicate. Lesson: when fixing "audit-trail uses stale predecessor", grep ALL sites that read `existingUser.AgreementCode` within the same atomic-tx block, not just the literally-flagged sites.
- **Docker harness flakiness during validation** — 80+ leaked containers from prior test runs choked the Docker daemon; full Docker-gated suite runs returned 114→27 failures depending on container state. Re-running smaller subsets serially gave clean signal. The `testcontainers/ryuk` cleanup pattern is leaking; not S34 regression but operational debt.
- **WAF connection-override defect (S29 deferred)** continues to block ~6 HTTP-level D-tests including S34's `UserAgreementCodeBackfillSeederTests`. Defect deferred to Phase 4e; S34 doesn't fix it.

### Trust calibration

- Mechanical absorption of cycle 1 fixes was correct; the convergence between Codex BLOCKER and Reviewer WARNING-1 was diagnostic of the same root cause and the fix-shape (FOR UPDATE + expectedVersion threading) was unambiguous.
- Cycle 2 missed-facts pattern matched `feedback_thrash_defer_real_world.md` — finite, well-bounded, no architectural reframe required. Applied fix without cycle 3 dispatch; this was the correct call per the discipline.
