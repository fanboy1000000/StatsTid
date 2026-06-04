# Sprint 63 — Vacation day-count part-time correctness (Part A: ADR-031)

| Field | Value |
|-------|-------|
| **Sprint** | 63 |
| **Status** | complete |
| **Start Date** | 2026-06-03 |
| **End Date** | 2026-06-04 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes (solution 0 errors after every phase; final close build green) |
| **Test Verified** | yes — Unit 629/629 + Regression-plain 44/44 + FE 164/164; Docker-gated Regression/Smoke CI-pending (engine down at close — S61/S62 precedent; compile-proven) |

## Sprint Goal
Correct the VACATION + SPECIAL_HOLIDAY **day-count** to be part-time-fraction-independent per Ferieloven (flat 25/5 for everyone; only consumption + value pro-rate) — **ADR-031**, classified bug-with-no-past-impact. This reverts ADR-030 D8 (the S62 piecewise-*fraction* accrual, built on a part-time misconception surfaced by the 2026-06-03 deep-research) to flat `annualQuota × monthsElapsed / 12`, removing the now-dead S62 piecewise surface. **No schema migration** (config-value + code). §6 stk.2 consumption + `work_days_per_week` is the separate, launch-blocking **S64** (ADR-032). Refinement: `.claude/refinements/REFINEMENT-vacation-parttime-daycount-fix.md` (2 dual-lens cycles). Dashboard (grid+transferable) resumes after.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks pass; db-schema in sync (55 tables); KB INDEX 42 entries / 0 orphans / 0 dangling; sprint inventory through S62. |
| Pattern compliance spot-check | CLEAN | FAIL-001 `FindFirst("scopes")`: 0 hits. (PAT-005/auth unaffected — no new endpoints in Part A.) |
| Orphan detection | DEBT→addressed | S62's `EarnedToDatePiecewise`/`FractionPeriod`/`GetFractionHistoryAsync` become orphaned by this fix → removed in TASK-6303 (not left dead). |
| Documentation drift | CLEAN | MEMORY current through S62; this sprint adds ADR-031 + the §6/S64 launch-blocker note. |
| Quality grade review | RESOLVED at close | Rule Engine A++ holds — accrual SIMPLIFIED (piecewise surface removed; single flat pure fn; single-source guard green); QUALITY.md anchor → S63. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P2 deterministic rule engine + P6 payroll-adjacent quota gate; legal rule logic; supersedes a just-shipped ADR) |
| **External Codex** | invoked 2026-06-03 — cycle 1: 1B/2W/2N; cycle 2: **clean** ("absorptions verified, no new findings") |
| **Internal Reviewer** | invoked 2026-06-03 — cycle 1: 2B/3W/5N; cycle 2 (re-run 2026-06-04 — the 2026-06-03 run was killed by the session token limit mid-verification): **0 BLOCKERs, absorptions verified**, +1W/1minor/1N advisory (absorbed below) |
| **BLOCKERs resolved before Step 1** | yes — compile-bound test deletions re-sequenced into TASK-6303 (see Resolution) |

### Findings (cycle 1)

_Codex findings:_
- BLOCKER — TASK-6303/6304 sequencing — TASK-6303 deletes `EarnedToDatePiecewise`/`FractionPeriod`/`GetFractionHistoryAsync` and requires "solution builds 0 errors" BEFORE TASK-6304 deletes the tests that reference them (`AccrualMathTests.cs:192+`, `EmploymentProfileResolverTests.cs:32+`) — build cannot pass.
- WARNING — TASK-6302/ADR-031 — "remove `GetFractionHistoryAsync` fetches" contradicts "RETAIN … empty-history 422" (the guard reads the removed fetch's variable); clarify collapse onto the anchor guard.
- WARNING — removed-method test deletions under-enumerated (`EmploymentProfileResolverTests` has multiple standalone `FractionPeriod` tests beyond the range test).
- NOTE — agent assignment matches AGENTS.md cross-domain convention. NOTE — ADR-031 correctly supersedes only D8, preserves D1–D7 (incl. D6), classification + §6/S64 launch-blocker sound.

_Internal Reviewer findings (convergent):_
- BLOCKER-1 — `EmploymentProfileResolverTests.cs` is compile-bound to `FractionPeriod` (13 cases, L31-116) — whole file must be deleted, not just the range test; `AccrualMathTests.cs` piecewise cases (L173-366 incl. `WholeWindow` helper) likewise.
- BLOCKER-2 — phasing leaves a compile break unless the compile-bound test deletions land in the SAME commit as TASK-6303's method removal.
- WARNING-1 — flat rewrite must preserve the per-site asOf split: VACATION→`ferieaarEnd` (forskud), SPECIAL_HOLIDAY→`firstAbsenceDate` (no forskud, §13 stk.4) — a single collapsed asOf silently grants SPECIAL_HOLIDAY forskud.
- WARNING-2 — empty-history 422 (L860-867) is mechanically bound to the removed fetch → must be REMOVED with it; the anchor profile-missing 422 (L805-812, stays true via `isMonthlyAccrual`) is the surviving load-bearing guard.
- WARNING-3 — the idempotent seed UPDATE must be type-keyed to also cover the AC_RESEARCH/AC_TEACHING rows that exist ONLY in init.sql.
- NOTEs — removal set otherwise complete (`AccrualCalculator` forwards only to the surviving `EarnedToDate`; no DI/mapper refs); single-source guard stays green (count-of-one on `AccrualMath.cs` holds); D6 preservation real (flat call threads `employmentStart`); §6/S64 launch-blocker correctly event-bound; danish-agreements targets are L109/L110/L117 (not L107); delete private `ResolveFraction`, keep `MonthIndex`.

### Resolution

Both lenses converged on the test-sequencing BLOCKER. Plan edits applied 2026-06-03 (cycle 1 absorption):
- **TASK-6303** now deletes the compile-bound test files/cases (whole `EmploymentProfileResolverTests.cs` + the `FractionPeriod`/`EarnedToDatePiecewise`/`WholeWindow` cases in `AccrualMathTests.cs`) in the SAME commit as the method removal; grep validation spans `src/` AND `tests/`. **TASK-6304** keeps only the non-compile-bound HTTP rewrites + config-fixture flip + new tests.
- **TASK-6302** carries the per-site asOf criterion (W1) + the 422 reconcile (W2: anchor 422 retained, empty-history 422 removed with its fetch — ADR-031 D4 rewritten to match).
- **TASK-6301** UPDATE is type-keyed (covers AC_RESEARCH/AC_TEACHING); **TASK-6303** deletes `ResolveFraction`, keeps `MonthIndex`; **TASK-6305** targets danish-agreements L109/L110/L117.

Cycle 2 (verification of the absorbed plan): Codex **clean**; Reviewer **0 BLOCKERs — all 5 absorptions verified** (grep-confirmed: exactly 4 test files reference the removed surface — 2 compile-bound in TASK-6303, 2 comment-only in TASK-6304; no `IEmploymentProfileResolver` test doubles; `src/` consumer set exactly the 6 in-scope files). Cycle-2 advisory findings, absorbed as plan edits 2026-06-04 without a cycle 3 (enumeration/numeral corrections, not design changes — no new BLOCKERs, so the cycle-cap halt does not fire):
- W — `BalanceSeriesTests.Series_SelectedPoint_UnderMidFerieaarChange_ReconcilesWith_SummaryEarned` (L216-238) becomes vacuous-by-name under flat (seeds 1.0→0.5 to prove *piecewise* reconciliation) → TASK-6304 now renames/re-comments it as a fraction-INDEPENDENCE reconciliation test (both seams ignore the fraction seed and reconcile) rather than deleting coverage.
- minor — TASK-6301 validation said "all 10" rows; actual count is **20** (2 types × 5 agreement codes × 2 OK versions) → corrected.
- N — `/summary`'s `partTimeFraction` resolve + the generic `ProRateByPartTime` ternary (BalanceEndpoints L244-246) become value-dead for the current seed but STAY: the path is config-driven (admin CRUD can legitimately set the flag on another type) and the Balance seam's graceful `?? 1.0m` polarity is ADR-023 D3 — recorded as deliberate no-removal, not dead code.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity (AccrualMath the single accrual source — `AccrualMathSingleSourceTests` green post-deletion, count-of-one held since the deleted `/12m` lived in the same file; zero dangling refs src+tests; interface/impl/test trichotomy consistent — Step-7a Reviewer-verified)
- [x] P2 — Rule engine determinism (flat `EarnedToDate(…,1.0m,…)` pure — no I/O/wall-clock/state; the removed per-request fetch net-REDUCED I/O on the read path)
- [x] P3 — Event sourcing (read-only sprint; no events; no EventSerializer/projection change)
- [x] P4 — Replay correctness (flat accrual = pure fn of asOf + months-elapsed; ADR-030 D6 mid-hire pro-ration preserved via `employmentStart` threading at all 3 sites — Step-5a Reviewer-verified; dated two-step config reads untouched)
- [x] P5 — Integration isolation (resolver method removed cleanly; SharedKernel stays I/O-free; PAT-005 HTTP boundary untouched)
- [x] P6 — Payroll-adjacent quota gate (flat caps with the per-type asOf split preserved; `CheckAndAdjustAsync` guardCap/seedQuota + `BookableLimit` + `validationRequest` contracts byte-unchanged; anchor 422 retained — proven a STRICT SUPERSET of the removed empty-history guard; RuleEngine 20%-warning threshold flat via the flag)
- [x] P7 — Security (no endpoint/auth/route change; only an unused DI param pruned)
- [x] P8 — CI/CD (Unit 629/629; plain Regression 44/44; FE 164/164; renamed Docker-gated file correctly wired `[Trait("Category","Docker")]`+`IAsyncLifetime`, compiles — CI-pending per S61/S62 precedent; `check_docs.py` clean at close)
- [x] P9 — UX (`/series` part-timer curve == full-timer — the now-correct behavior; zero FE changes; dashboard stays parked, resumes atop the corrected model)

## Task Log

> Dependency phases: **P1** TASK-6301 (config flip) + TASK-6302 (Backend de-fraction — removes prod callers of the to-be-deleted methods) → **P2** TASK-6303 (delete the now-uncalled S62 piecewise surface **+ the compile-bound test cases that reference the removed types**, same commit — so the solution builds green at 6303) → **P3** TASK-6304 (HTTP-level test rewrites + config-fixture flip + new part-time==full-time tests — NOT compile-bound to the removed types) → **P4** TASK-6305 (ADR/docs/close). **Step-0b BLOCKER fix:** compile-bound test deletions (`EmploymentProfileResolverTests.cs` whole-file + the `FractionPeriod`/`EarnedToDatePiecewise` cases in `AccrualMathTests.cs`) ride with TASK-6303, NOT TASK-6304 — else 6303's "build green" can't pass.

### TASK-6301 — Config: `ProRateByPartTime=false` for VACATION + SPECIAL_HOLIDAY
| Field | Value |
|-------|-------|
| **ID** | TASK-6301 |
| **Status** | done 2026-06-04 (agent first-pass clean; diff verified vs report; central build 0 errors; Constraint Validator PASS; Step-5a Codex+Reviewer findings recorded below) |
| **Agent** | Data Model (extended into SharedKernel/Config + init.sql seed, cross-domain authorized) |
| **Components** | SharedKernel/Config (DefaultEntitlementConfigs), docker/postgres/init.sql |
| **KB Refs** | ADR-031 D2/D5, ADR-021 D4/D5 (year-start freezing), ADR-024 D3 (classification) |
| **External Review (Codex)** | planned (high-risk: legal rule logic) |

**Description**: Flip `ProRateByPartTime → false` for VACATION (`:75`) + SPECIAL_HOLIDAY (`:90`) in `DefaultEntitlementConfigs` (emits AC/HK/PROSA only); mirror in the init.sql entitlement seed. Add a **type-keyed idempotent UPDATE** (the seed uses `ON CONFLICT … DO NOTHING`, so non-fresh DBs won't pick up the change — S60 precedent): `UPDATE entitlement_configs SET pro_rate_by_part_time = false WHERE entitlement_type IN ('VACATION','SPECIAL_HOLIDAY')` — type-keyed so it also covers the **AC_RESEARCH + AC_TEACHING** variant rows that exist ONLY in init.sql (not in `DefaultEntitlementConfigs`).
**Validation**: greenfield reseed + legacy-upgrade UPDATE both yield `pro_rate_by_part_time = false` for **all 20** VACATION/SPECIAL_HOLIDAY rows (2 types × 5 agreement codes AC/HK/PROSA/AC_RESEARCH/AC_TEACHING × 2 OK versions — Reviewer cycle-2 numeral fix); IMMEDIATE types unchanged.

### TASK-6302 — Backend: revert the 3 accrual call-sites to flat
| Field | Value |
|-------|-------|
| **ID** | TASK-6302 |
| **Status** | done 2026-06-04 (agent first-pass clean; diff verified vs report; build 0 errors; asOf split + anchor-422 + contracts confirmed intact; Orchestrator small-task: pruned the now-unused `/series` `profileResolver` DI param the agent flagged; Constraint Validator PASS; Step-5a findings recorded below) |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api/Endpoints (BalanceEndpoints /summary + /series, SkemaEndpoints cap) |
| **KB Refs** | ADR-031 D2/D3/D4, ADR-030 D3/D4/D6 (preserved) |
| **External Review (Codex)** | planned (high-risk: payroll-adjacent quota gate + legal rule logic) |

**Description**: Replace the `EarnedToDatePiecewise(…, fractionHistory)` calls with the flat `AccrualMath.EarnedToDate(annualQuota, 1.0m, ferieaarStart, user.EmploymentStartDate, asOf)` at `/summary` (~L270), `/series` (~L481), and the Skema caps (~L876/:885). **Preserve the per-site asOf distinction (Step-0b W1):** VACATION cap asOf = `ferieaarEnd` (forskud whole-ferieår); SPECIAL_HOLIDAY cap asOf = `firstAbsenceDate` (no forskud, §13 stk.4) — do NOT collapse to one asOf (would silently give SPECIAL_HOLIDAY forskud). Threading `user.EmploymentStartDate` preserves ADR-030 D6 (mid-hire months-elapsed pro-ration). Remove the now-unused `GetFractionHistoryAsync` fetches (L206/L460/L852). **422 reconcile (Step-0b W2):** RETAIN the **anchor profile-missing 422** (L805-812, `GetByEmployeeIdAtAsync` under `fractionMatters` — stays true via `isMonthlyAccrual`, so NOT relaxed); the **empty-history 422 (L860-867) is removed together with its fetch** (its variable is gone; the anchor 422 covers missing-profile — ADR-031 D4). With `ProRateByPartTime=false`, `effectiveQuota` + the RuleEngine 20%-warning threshold go flat automatically. `CheckAndAdjustAsync`/`BookableLimit`/`seedQuota` + `validationRequest` shape unchanged.
**Validation**: a 50%-time employee's `/summary` earned + `/series` curve == a full-timer's; both Skema caps flat with their correct asOf (VACATION ferieaarEnd / SPECIAL_HOLIDAY firstAbsenceDate); anchor 422 still fires on missing profile; full-time unchanged.

> **Step 5a (Phase P1, TASK-6301+6302) — both lenses clean 2026-06-04.** Codex (per-task high-risk, prompt-alone on the uncommitted diff): "No domain-correctness issues found in the scoped diff. The flat day-count is applied at the three accrual call-sites, the VACATION/SPECIAL_HOLIDAY as-of split is preserved, and the seed plus legacy UPDATE cover the expected rows." Internal Reviewer: "No findings for the reviewed scope" — independently verified the 422-coverage shift is **strictly safe** (the retained anchor guard is a *superset* of the removed empty-history guard: any profile row covering `firstAbsenceDate` necessarily overlaps the old ferieår scan window; it also fail-closes on a missing agreement-code row — stricter, not looser), the `/summary`↔`/series` reconciliation invariant (identical year-start derivation → byte-identical flat call), D6 threading at all 3 sites, all 20 seed tuples, and the Backend→RuleEngine contract consistency (`partTimeFraction` forwarded but non-load-bearing for these types' cap — the §6 use is the S64 item). One informational NOTE, no action. Constraint Validator: PASS (0 RuleEngine imports / 0 hardcoded URLs / 0 FindFirst / 0 inlined accrual arithmetic).

### TASK-6303 — Remove the now-dead S62 piecewise surface
| Field | Value |
|-------|-------|
| **ID** | TASK-6303 |
| **Status** | done 2026-06-04 (agent first-pass clean; pure-deletion diff verified — 602 lines removed, 2 files deleted; build 0 errors; AccrualMath-filtered suite 39/39 incl. single-source guard; grep: 0 src/tests refs beyond the 6 known comment-only Regression lines; Step-5a Codex clean: "No dangling src/tests references … filtered test run passed with 39 tests, including the single-source guard" — internal per-task Reviewer pass deliberately skipped: the plan-review Reviewer cycle-2 had already grep-verified this exact deletion set, and the mechanical evidence (build + guard + grep) covers it; Step 7a reviews the cumulative diff) |
| **Agent** | Rule Engine + Data Model (extended into Infrastructure, cross-domain authorized) |
| **Components** | SharedKernel/Calendar (AccrualMath, FractionPeriod), SharedKernel/Interfaces (IEmploymentProfileResolver), Infrastructure (EmploymentProfileResolver) |
| **KB Refs** | ADR-031 D3 |
| **External Review (Codex)** | planned (high-risk: rule-engine surface change) |

**Description**: After TASK-6302 removes all prod callers, delete `AccrualMath.EarnedToDatePiecewise` + the private `ResolveFraction` helper + `FractionPeriod` (**keep `EarnedToDate` + `MonthIndex`** — `EarnedToDate` uses `MonthIndex`), and `IEmploymentProfileResolver.GetFractionHistoryAsync` + its `EmploymentProfileResolver` impl. **Same commit (Step-0b BLOCKER): delete the compile-bound test files/cases** that reference the removed types — the whole `tests/StatsTid.Tests.Unit/EmploymentProfileResolverTests.cs` (13 `FractionPeriod`/`Covers` cases — its sole purpose was the S62 projection contract) + the `EarnedToDatePiecewise`/`FractionPeriod` cases in `AccrualMathTests.cs` (incl. the `WholeWindow` helper) — else the Unit assembly won't compile. Confirm zero remaining references across **`src/` AND `tests/`** (grep) + `AccrualMathSingleSourceTests` green (the `/12m`+`MonthIndex(` fingerprints remain solely in `AccrualMath.cs` — count-of-one holds since the deleted method also lived there).
**Validation**: full solution builds 0 errors (incl. test projects); no dangling refs in src or tests; single-source guard green.

### TASK-6304 — Tests: rewrite S62 fraction-scaled tests flat + add part-time==full-time
| Field | Value |
|-------|-------|
| **ID** | TASK-6304 |
| **Status** | done 2026-06-04 (agent first-pass clean; Unit 629/629 green incl. the :70/:85 flips; Docker-gated rewrites compile, CI-pending; Orchestrator-authorized rename `SkemaPiecewiseAccrualTests` → `SkemaAccrualCapTests` — name-honesty, consistent with the cycle-2 Reviewer's name-intent W on the BalanceSeries test; zero stale piecewise refs in tests) |
| **Agent** | Test & QA |
| **Components** | Unit + Regression (Docker-gated) tests |
| **KB Refs** | ADR-031 D3 |
| **External Review (Codex)** | covered by Step 7a |

**Description**: (Compile-bound test deletions moved to TASK-6303.) Here, the **HTTP-level rewrites** (NOT compile-bound to the removed types — they call endpoints): `SkemaPiecewiseAccrualTests.Vacation_PiecewiseForskudCap_*` + `..._BeyondPiecewiseForskudCap_*` → flat 25-allowed / 26-rejected (any fraction); `BalanceSeriesTests.Series_MidFerieaarPartTimeChange_…` → flat unbent curve == full-timer's (update the stale "belt-and-suspenders empty-fraction-window" comment in `SkemaPiecewiseAccrualTests` — the fail-closed test now passes via the anchor 422). **Reviewer cycle-2 W:** `BalanceSeriesTests.Series_SelectedPoint_UnderMidFerieaarChange_ReconcilesWith_SummaryEarned` (~L216-238) becomes vacuous-by-name under flat (it seeded 1.0→0.5 to prove *piecewise* reconciliation) → RENAME/re-comment it as a fraction-INDEPENDENCE test (a mid-ferieår fraction-change seed bends NEITHER seam; both reconcile and equal the full-time figures) — keep the coverage, fix the intent. Flip `DefaultEntitlementConfigTests.cs:70/:85` `Assert.True → Assert.False`. ADD: a 50%-time employee earns flat 25/5 (`/summary` + `/series` == full-time); the surviving `BalanceSeriesTests` (past-asOf, monotonic, base reconciliation) unchanged.
**Validation**: unit suite green; Docker-gated tests compile (CI-pending if Docker unavailable at close).

### TASK-6305 — ADR-031 + docs + sprint close (Orchestrator)
| Field | Value |
|-------|-------|
| **ID** | TASK-6305 |
| **Status** | done 2026-06-04 (ADR-031 finalized incl. the Step-7a NOTE-1 D3 rephrase; KB INDEX ADR-031 row + ADR-030 D8-superseded annotation; danish-agreements L109/L110 cells + L117 prose corrected; ROADMAP/sprints-INDEX/QUALITY updated in the close commit) |
| **Agent** | Orchestrator |
| **Components** | docs (KB INDEX, danish-agreements.md, ROADMAP, sprints INDEX, QUALITY) |

**Description**: ADR-031 (drafted) → finalize + KB INDEX entry; correct `danish-agreements.md` **L109/L110** (the VACATION + SPECIAL_HOLIDAY entitlement-table Pro-Rate-Part-Time "Yes" cells) + the **L117** prose → "No (day-count flat per Ferieloven §5); part-time pro-rates consumption only (§6 stk.2)" (do NOT touch the unrelated `wage_type_mappings` Pro-Rate rows ~L155/165/190); ROADMAP Completed Sprints + current-position (S63) + record the **S64 §6 launch-blocker**; sprints INDEX + Test Progression; QUALITY anchor → S63.

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | VERIFIED | Ferieloven §5 stk.1: flat 25/5 day-count, fraction-independent (ADR-031, source-cited: borger.dk + Ferieloven §§5/6/21 + Medst. 021-24 §3 — 2026-06-03 deep-research, 24/25 claims adversarially verified); danish-agreements.md corrected |
| Overtime/supplement deterministic | N/A | Not touched (working-time uses of `part_time_fraction` deliberately untouched — Norm/Overtime/FlexBalance/Absence rules) |
| Absence effects correct | VERIFIED (day-count) / DEFERRED (§6 consumption) | VACATION/SPECIAL_HOLIDAY flat earned/quota/caps at all seams; §6 stk.2 consumption conversion = **S64, LAUNCH-BLOCKING** (ADR-031 D6 — interim over-entitlement of <5-day workers latent pre-launch, unrepresentable until `work_days_per_week` exists) |
| Retroactive recalculation stable | VERIFIED | Flat accrual = pure fn of asOf + months-elapsed; bug-with-no-past-impact (ADR-024 D3) → no recompute; pre-launch no past periods (the last free-correction window) |

## External Review (Step 7a)
| Field | Value |
|-------|-------|
| **Invoked** | 2026-06-04 — **clean cycle 1, BOTH lenses** (no fix cycle; cycle cap not engaged) |
| **Sprint-start commit** | `c09acbf` (S62 close) |
| **Command** | `codex review "..."` (prompt-alone, uncommitted — no intermediate commits on master) |
| **Codex verdict** | "No sprint-end consistency issues were found in the uncommitted diff. The flat vacation/special-holiday accrual cutover appears consistently applied across seeds, backend call-sites, deleted piecewise surface, and rewritten tests." (artifact: `.claude/reviews/SPRINT-63-step7a-codex.md`) |
| **Internal Reviewer verdict** | "No BLOCKER or WARNING findings for the reviewed scope" + 3 advisory NOTEs (1 = ADR-031 D3 test-count wording → absorbed in the close commit; 2 = delete+add vs git-mv rename, cosmetic; 3 = residual "piecewise" comment mentions confirmed-benign supersession context). Independently tied the −24 unit arithmetic (16 piecewise math cases + 8 resolver tests). (artifact: `.claude/reviews/SPRINT-63-step7a-reviewer.md`) |
| **Step 5a per-task trail** | Phase P1 (6301+6302): Codex clean + Reviewer clean (anchor-422 superset proof). TASK-6303: Codex clean (re-ran the filtered suite itself); internal per-task pass deliberately skipped — plan-review Reviewer cycle-2 had pre-verified the exact deletion set + mechanical evidence (build/guard/grep). Constraint Validator PASS on both phases. |

## Test Summary

Validated via the `sprint-test-validation` skill (all suites run 2026-06-04; previous + delta = current arithmetic checked):

| Suite | Previous (S62) | Current (S63) | Delta |
|-------|----------------|---------------|-------|
| Unit | 653 | **629** | **−24** (deleted S62 piecewise surface tests: 16 `AccrualMathTests` cases [9 Facts + 7 InlineData] + 8 discovered `EmploymentProfileResolverTests` [7 + 1 Skip] — exactly mirrors the S62 +24) |
| Regression (plain) | 44 | **44** | 0 (green locally) |
| Regression (Docker-gated) | CI-pending | CI-pending | net **+1** method on the S62 set: `SkemaAccrualCapTests` (renamed, 3 = 2 rewritten + 1 kept) + `BalanceSeriesTests` (2 rewritten/renamed + **1 added** `Summary_HalfTimeEmployee_EarnedEqualsFullTimeFlat_FractionIndependent`); compile-proven; Docker engine down at close (S61/S62 precedent) |
| Smoke | 4 (Docker) | CI-pending | 0 (Docker down) |
| Frontend | 164 | **164** | 0 (re-run despite zero FE changes — green) |

## Agent Effectiveness

| Task | Agent | Outcome |
|------|-------|---------|
| TASK-6301 | Data Model (ext. Config+init.sql) | First-pass clean; exact 20-row flip + S60-pattern UPDATE; 16 tool-uses / ~47k tokens |
| TASK-6302 | Backend API (cross-domain) | First-pass clean; all 3 call-sites + 422 reconcile + asOf split; proactively flagged the unused `/series` DI param (Orchestrator pruned); 15 tool-uses / ~80k tokens |
| TASK-6303 | Rule Engine + Data Model (ext. Infra + Unit tests) | First-pass clean; pure 602-line deletion; ran the filtered guard suite itself (39/39); 21 tool-uses / ~74k tokens |
| TASK-6304 | Test & QA | First-pass clean; rewrites + rename + new coverage; full Unit suite green; 59 tool-uses / ~127k tokens |
| Reviews | Codex ×4 (0b ×2, 5a ×2, 7a ×1 — 5 invocations) + Reviewer ×4 (0b cycle-2 re-run, 5a, 7a + the killed 0b cycle-2 from 2026-06-03) | Every cycle actionable or clean; zero re-dispatches required across the sprint |

All four implementation agents first-pass clean — zero re-dispatches. The Step-0b BLOCKER fix (compile-bound test deletions riding with TASK-6303) is what made the 6303 build-green gate passable in one pass.

## Sprint Retrospective

**What went well**: The session-limit recovery worked exactly as designed — the resumed session reconstructed state from the Codex session logs + the prior transcript (cycle-1 findings, the killed Reviewer cycle-2, the absorbed plan edits) and re-ran only the missing piece. Clean phased execution P1→P4, every agent first-pass, Step 7a clean cycle 1 on both lenses (second consecutive sprint) — the front-loaded dual-lens refinement + plan review continue to pay for themselves. The Reviewer's plan-review cycle-2 grep enumeration (exactly 4 test files, 2 compile-bound) made the 6303/6304 split mechanical. The anchor-422-superset proof (Step-5a Reviewer) upgraded ADR-031 D4 from "deliberate decision" to "proven no-coverage-loss".

**What to improve**: Docker unavailable at close for the THIRD consecutive sprint (S61/S62/S63) — the rewritten flat cap/curve tests are the load-bearing CI set and remain locally unverified; consider bringing the engine up before the next sprint that touches Docker-gated behavior. The S62→S63 whiplash (build piecewise, then revert it one sprint later) cost ~2 sprints of accrual work; the root cause was encoding a domain assumption (fraction-scaled day-count) without a primary-source legal check — the deep-research that caught it should have run before S60/S62, not after (cheap prevention: source-cite the legal basis line in every entitlement-rule ADR at refinement time, which ADR-031 now models).

**Knowledge produced**:
- **ADR-031** (formal) — vacation day-count is part-time-fraction-independent; supersedes ADR-030 D8; binds **S64 (§6 stk.2 consumption + `work_days_per_week`) as LAUNCH-BLOCKING**.
- danish-agreements.md Pro-Rate cells corrected with §5/§6 split + S64 pointer.
- Process datum: a mid-Step-0b session kill is fully recoverable from `~/.codex/sessions` rollout logs + the harness transcript (user prompts, agent results, and edit timestamps reconstruct the exact resume point).

**Standing open items**: ADR-032/S64 §6 stk.2 consumption (LAUNCH-BLOCKING, next); ADR-030 D7 §8/§7 payroll settlement (deferred); Oversigt grid+transferable dashboard (parked, resumes atop the corrected model); Docker-gated CI backlog (S61+S62+S63 sets).
