# Sprint 62 — Piecewise per-month vacation accrual

| Field | Value |
|-------|-------|
| **Sprint** | 62 |
| **Status** | complete (validated; Docker integration tests CI-pending) |
| **Start Date** | 2026-06-03 |
| **End Date** | 2026-06-03 |
| **Orchestrator Approved** | yes — 2026-06-03 |
| **Build Verified** | yes — full solution `dotnet build` 0 errors (2026-06-03) |
| **Test Verified** | partial — 653 unit pass (0 failed, +24 vs S61's 629); Docker-gated integration tests CI-pending (Docker unavailable at close) |

## Sprint Goal
Move vacation `earnedToDate` (optjent) from the single-fraction model (one part-time fraction applied across all elapsed accrual months) to a **cumulative piecewise** model — each elapsed accrual month accrues at the part-time fraction anchored to that month's first day (whole-month policy), summed — across all three consumption sites (`/summary`, `/series`, Skema quota guard), preserving replay determinism (P4) and byte-equality for constant-fraction employees. Refinement: `.claude/refinements/REFINEMENT-s62-piecewise-accrual.md` (2-cycle dual-lens reviewed). Doc-debt (QUALITY.md/ROADMAP/INDEX anchor lag) folded into the close.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `tools/check_docs.py` all hard checks pass: db-schema in sync (55 tables), KB INDEX complete (42 entries, 0 orphans, 0 dangling), sprint inventory complete through S61. |
| Pattern compliance spot-check | CLEAN | FAIL-001 `FindFirst("scopes")`: 0 hits. `http://localhost`: only dev `launchSettings.json` profiles (benign). PAT-005: only test projects reference `RuleEngine.Api`; `Backend.Api` references SharedKernel + Infrastructure only — assembly isolation intact. RequireAuthorization: every endpoint covered except `AuthEndpoints` (login, intentionally anonymous). |
| Orphan detection | CLEAN | S60/S61 additions (`AccrualMath.cs`, `/series` endpoint, `employment_start_date` surface, `BalanceSeriesTests`) all referenced. |
| Documentation drift | CLEAN | MEMORY.md deferred items current: S61 follow-up #1 (EarnedToDate consolidation) recorded done; piecewise = this sprint; §8/§7 still deferred. |
| Quality grade review | DEBT | `check_docs.py` freshness WARNING: `QUALITY.md` anchored at S57, HEAD S61 (>3 behind). ROADMAP *Completed Sprints* table + sprints INDEX *Sprint Index* status table also lag ~S57. **Folded into the S62 close (TASK-6205).** Not blocking. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P2 deterministic rule engine + P4 version/replay + P6 payroll-adjacent quota gate; legal rule logic) |
| **External Codex** | invoked 2026-06-03 — cycle 1: 1B/2W/2N |
| **Internal Reviewer** | invoked 2026-06-03 — cycle 1: 0B/2W/5N |
| **BLOCKERs resolved before Step 1** | yes — agent-scope labels corrected per AGENTS.md Cross-Domain Authorization (see Resolution) |

### Findings (cycle 1)

_Codex findings:_
- BLOCKER — TASK-6201/6202/6203 — agent scopes don't match AGENTS.md (Rule Engine doesn't own `tests/**`; Data Model doesn't own Infrastructure; "API Integration" owns outbound integrations/Resilience, NOT `Backend/**/Endpoints`).
- WARNING — TASK-6203/6204 — gate alignment under-specified: assert Skema still sends carryover-INCLUSIVE `BookableLimit` + passes carryover-EXCLUDED `guardCap` + annual `seedQuota` to `CheckAndAdjustAsync` + preserves request-contract shape.
- WARNING — TASK-6202 — KB cite: ADR-018 D9 is not the end-exclusive authority (that's ADR-018 D8; read-site predicate audit D10).
- NOTE — phase order `6201→6202→6203→6204→6205` is closed; 6201/6202 file-disjoint in SharedKernel once test ownership is authorized.
- NOTE — scope/defer choices tight (§8/§7 correctly non-load-bearing; doc-debt-in-close acceptable).

_Internal Reviewer findings:_
- WARNING — TASK-6202 — agent label incomplete: Data Model writes `src/Infrastructure/**/EmploymentProfileResolver.cs` (out of Data Model scope); prior sprints (S30/S31/S33) used `Data Model (extended into Infrastructure, cross-domain authorized)`.
- WARNING — TASK-6201/6203 — single-source guard mechanics: pin the falsifiable target to the endpoint files (no executable `/12m` or `MonthIndex(` enters `BalanceEndpoints.cs`/`SkemaEndpoints.cs`; `FormerMirrorSites` theory stays green), not a vague "guard unchanged."
- NOTE — TASK-6202 — state the new method is a single-table `employee_profiles`-only query (no `users` JOIN, no agreement-code coupling — unlike the single-point sibling, which fail-loud-throws on a missing agreement-code row).
- NOTE — TASK-6203 — add a criterion that SPECIAL_HOLIDAY's piecewise `asOf` stays `firstAbsenceDate` (no forskud, §13 stk.4) while VACATION's stays `ferieaarEnd` (both now route one `EarnedToDatePiecewise` call → easy asOf-swap regression).
- NOTE — KB refs current; phase order + dependency closure sound; doc-debt bundling appropriate; Step 5a/7a coverage adequate. P5 line authorizes the Infrastructure write — align the task label to match.

### Resolution

Both lenses converged on the agent-scope labels (Codex BLOCKER = Reviewer WARNING). Plan edits applied (cycle 1):
- **TASK-6201** → `Rule Engine (extended into tests/StatsTid.Tests.Unit, cross-domain authorized)` — the pure-function unit tests ride with the math (TDD-natural); explicit per AGENTS.md Cross-Domain Authorization.
- **TASK-6202** → `Data Model (extended into Infrastructure + tests, cross-domain authorized)` (S30/S31/S33 precedent); KB cite ADR-018 D9 → **D8/D10 + ADR-023/ADR-016 D5b**; added the single-table-no-JOIN-no-agreement-code-coupling phrase.
- **TASK-6203** → `Backend API (cross-domain authorized)` (S22 TASK-2205 precedent); added single-source-guard-on-endpoint-files criterion + SPECIAL_HOLIDAY-asOf=firstAbsenceDate / VACATION-asOf=ferieårEnd criterion + gate-contract-preservation criterion.
- **TASK-6204** → added explicit gate-alignment test criterion (carryover-inclusive `BookableLimit` + carryover-excluded `guardCap` + annual `seedQuota` + request-contract shape).
- Phase order, dependency closure, scope/defer, KB freshness (else), Step 5a/7a coverage: confirmed sound by both lenses — no change.

Cycle 2 (verification of the absorbed plan): re-ran both lenses → **clean** (see note at end of section).

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (piecewise math + version-walk live ONLY in SharedKernel/AccrualMath; Backend.Api references only SharedKernel+Infrastructure — no PAT-005 cross-ref; `AccrualMathSingleSourceTests` green)
- [x] P2 — Rule engine determinism maintained (`EarnedToDatePiecewise` pure: no I/O, no wall-clock, no state — Reviewer-verified; 653 unit pass)
- [x] P3 — Event sourcing append-only semantics respected (read-only change; emits no events; no EventSerializer change)
- [x] P4 — Replay correctness: per-month fraction resolved from versioned history at the month anchor; determinism + past-`asOf`-unaffected-by-later-change tests authored (Docker CI-pending)
- [x] P5 — Integration isolation (resolver range query in Infrastructure; SharedKernel stays I/O-free)
- [x] P6 — Payroll-adjacent quota gate correctness: Skema `CheckAndAdjustAsync` + HTTP `BookableLimit` contracts unchanged; fail-closed retained (Reviewer-verified, 0 findings)
- [x] P7 — Security and access control (no endpoint surface change; existing auth retained)
- [x] P8 — CI/CD enforcement (`AccrualMathSingleSourceTests` passes; solution builds 0 errors; new Docker tests wired into the Docker CI job — CI-pending locally)
- [x] P9 — Usability and UX (`/series` curve now precise AND monotonic; no FE change, no UI regression)

## Task Log

> Dependency phases: **P1** TASK-6201 (SharedKernel pure math, foundational) → **P2** TASK-6202 (resolver range method, needs `FractionPeriod`) → **P3** TASK-6203 (Backend 3-site cutover, needs 6201+6202) → **P4** TASK-6204 (Test & QA integration/Docker, needs 6203) → **P5** TASK-6205 (ADR-030 D8 + doc-debt, Orchestrator, after impl validated).

### TASK-6201 — SharedKernel: `EarnedToDatePiecewise` + `FractionPeriod`

| Field | Value |
|-------|-------|
| **ID** | TASK-6201 |
| **Status** | complete |
| **Agent** | Rule Engine (extended into tests/StatsTid.Tests.Unit, cross-domain authorized) |
| **Components** | SharedKernel/Calendar (AccrualMath + FractionPeriod), AccrualMath unit tests |
| **KB Refs** | ADR-030 (D1 superseded sub-decision; D8 to be added), ADR-002 (pure rule engine), ADR-021 D4/D5 (config freezing), PAT-005 |
| **Constraint Validator** | pass (self-check: pure, no I/O/DB/URLs/events/endpoints/FindFirst; 3 files in declared scope) |
| **Reviewer Audit** | performed — **no findings** (independently re-derived clamp/anchor/gap-backward/short-circuit; verified tests assert invariants non-vacuously) |
| **External Review (Codex)** | deferred to sprint-end Step 7a full-diff (Orchestrator discretion — design was Codex-reviewed at refinement + plan stages; consolidating external review avoids 3× redundant per-task runs) |
| **Orchestrator Approved** | yes — 2026-06-03 |

**Build/Test evidence**: full solution builds clean (0 warnings/0 errors on touched projects); `dotnet test --filter ~AccrualMath` → **55/55 pass, 0 failed** (11 new piecewise tests + 4 single-source guards + AccrualCalculator parity). Validated independently by Orchestrator.

**Description**: Add a pure `AccrualMath.EarnedToDatePiecewise(annualQuota, ferieaarStart, employmentStart?, asOf, IReadOnlyList<FractionPeriod> fractionHistory)` beside the existing single-fraction `EarnedToDate` (which stays). Define `FractionPeriod = (DateOnly From, DateOnly? To, decimal Fraction)` (end-exclusive) in SharedKernel/Calendar. Iterate the SAME windowed, `[0,12]`-clamped elapsed-month set (`clamp(MonthIndex(asOf) − MonthIndex(accrualStart) + 1, 0, 12)`, `accrualStart = max(ferieårStart, employmentStart)`), anchor month `i` at `accrualStart.AddMonths(i)` (**month-START**, OQ-1a), and compute `annualQuota × Σfraction / 12m`. **Mandatory constant-single-period short-circuit** → legacy `annualQuota × f × monthsElapsed / 12m` (guarantees byte-equality; exactness also follows from the `NUMERIC(4,3)` scale bound). Gap rule: carry last-known fraction forward; carry **first-known fraction backward** for a pre-history in-window month (never cap-inflating `1.0m` when any period exists); `1.0m` only when history is entirely empty. Pure — no I/O, no wall-clock, no state.

**Validation Criteria**:
- [ ] Constant-fraction input ⇒ result equals legacy `EarnedToDate` **exactly** (equality test; short-circuit + NUMERIC(4,3) scale bound).
- [ ] Clamp survives summation — fraction change beyond month 12 / `asOf` past ferieår-end ⇒ full-12-month accruable, no over-sum (mirrors `EarnedToDate_ClampsAtTwelveMonths`).
- [ ] Mid-year drop AND rise verified; mid-ferieår hire anchors first month at `employmentStart`'s month.
- [ ] Monotonicity property test on the S61 scenario (full-time Sep–Dec → 0.5 Jan): precise AND non-decreasing.
- [ ] Gap carry-backward never prices an in-window month at `1.0m` when any period exists.
- [ ] `AccrualMathSingleSourceTests` passes unchanged (no `/12m`/`MonthIndex(` fingerprint leak outside AccrualMath.cs).

**Files Changed** (planned):
- `src/SharedKernel/StatsTid.SharedKernel/Calendar/AccrualMath.cs` — new method + `FractionPeriod`
- `tests/StatsTid.Tests.Unit/AccrualMathTests.cs` — piecewise cases

---

### TASK-6202 — Infrastructure: `GetFractionHistoryAsync` resolver range method

| Field | Value |
|-------|-------|
| **ID** | TASK-6202 |
| **Status** | complete |
| **Agent** | Data Model (extended into Infrastructure + tests, cross-domain authorized) |
| **Components** | SharedKernel/Interfaces (IEmploymentProfileResolver), Infrastructure (EmploymentProfileResolver), resolver test |
| **KB Refs** | ADR-022/ADR-023 (employee profile versioning), ADR-018 D8 (end-exclusive `effective_to`; D10 read-site predicate), ADR-023/ADR-016 D5b (consumption-time dated lookup), ADR-023 D3 (graceful fallback) |
| **Constraint Validator** | pass (self-check: Infrastructure read, no RuleEngine import, no URLs/events/endpoints/FindFirst; 3 files in declared cross-domain scope) |
| **Reviewer Audit** | deferred to sprint-end Step 7a (mechanical change matching the reviewed spec; predicate verified by inspection — row-overlap end-exclusive + ORDER BY) |
| **External Review (Codex)** | deferred to sprint-end Step 7a full-diff |
| **Orchestrator Approved** | yes — 2026-06-03 |

**Build/Test evidence**: **full solution build succeeded, 0 errors** (19 pre-existing CA2100 warnings in Regression test files, unrelated). `EmploymentProfileResolver` is the **only** implementer of `IEmploymentProfileResolver` (verified) — the interface addition broke no fakes/mocks. Resolver unit tests: 8 pass + 1 Docker-gated skip (`~EmploymentProfileResolver`). `GetByEmployeeIdAtAsync` byte-unchanged.

**Description**: Add `GetFractionHistoryAsync(employeeId, DateOnly from, DateOnly to, ct)` to `IEmploymentProfileResolver` (SharedKernel) + impl in `EmploymentProfileResolver` (Infrastructure). **Single-table `employee_profiles`-only query** — NO `users` JOIN, NO `UserAgreementCodeRepository` lookup, NO `EmployeeProfileNotFoundException` throw (unlike the single-point sibling `GetByEmployeeIdAtAsync`, which fail-loud-throws on a missing agreement-code row; fraction history needs only `part_time_fraction` + the temporal columns). Row-overlap predicate `effective_from < @to AND (effective_to IS NULL OR effective_to > @from)`, `ORDER BY effective_from`, projecting to `FractionPeriod` (from TASK-6201). Returns an **empty list** (no throw) on no rows — caller decides polarity. No N+1.

**Validation Criteria**:
- [ ] Single query; overlap predicate mirrors the existing end-exclusive single-point form; `ORDER BY effective_from`.
- [ ] Empty list on no rows (no throw); upper bound chosen so a covered anchor yields a non-empty window.
- [ ] Resolver test: multi-version employee returns ordered periods; single `'0001-01-01'`-anchored employee returns one open period.

**Files Changed** (planned):
- `src/SharedKernel/StatsTid.SharedKernel/Interfaces/IEmploymentProfileResolver.cs` — new method
- `src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs` — impl
- `tests/...` — resolver range test

---

### TASK-6203 — Backend: cut over `/summary`, `/series`, Skema guard to piecewise

| Field | Value |
|-------|-------|
| **ID** | TASK-6203 |
| **Status** | complete |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api/Endpoints (BalanceEndpoints, SkemaEndpoints) |
| **KB Refs** | ADR-030 (D3 guardCap/seedQuota, D4 forskud, D5 determinism), ADR-023 D3 (Balance graceful 1.0) |
| **Constraint Validator** | pass (self-check: no RuleEngine import — rule call stays HTTP/PAT-005; no new endpoints/auth; no arithmetic inlined; 2 files in declared scope) |
| **Reviewer Audit** | performed — **no findings** (all 8 invariants verified: Skema 422+empty-window fail-closed retained; asOf-split not swapped; guardCap/bookableLimit/seedQuota/validationRequest/CheckAndAdjustAsync unchanged; /summary graceful; /series workaround removed; reconciliation byte-identical; no single-source leak; no dead code) + 1 advisory NOTE **accepted** |
| **External Review (Codex)** | deferred to sprint-end Step 7a full-diff |
| **Orchestrator Approved** | yes — 2026-06-03 |

**Build evidence**: full solution build **succeeded, 0 errors**. **Accepted NOTE**: `/summary` fetches fraction history once over `[monthEnd−1yr, monthEnd+1d)` (employee-level, 1 query) rather than per-type `[ferieaarStart, +1yr)` like `/series`; Reviewer *proved* both windows resolve identically at all in-range anchors for every reset month, and the single fetch is more efficient — kept as-is (correct + faster; the subtlety is provability, not behavior).

**Description**: Fetch `fractionHistory` once per request (via TASK-6202) and call `EarnedToDatePiecewise` at all three sites: (1) `/summary` `earned` at the resolved month-end (graceful 1.0 on empty history); (2) `/series` — replace the single-fraction projection with one piecewise call per point (curve now naturally monotonic), DELETE the S61 single-fraction workaround comment + per-point logic, NO inlined arithmetic in the endpoint; (3) Skema guard — RETAIN the fail-closed `422 employment_profile_missing` anchor guard BEFORE piecewise, fail-closed if the returned range window is empty, then compute SPECIAL_HOLIDAY cap (earned-to-date @ firstAbsenceDate) and VACATION forskud cap (whole-ferieår: past historical + current carried forward). `guardCap` stays carryover-EXCLUDED; `CheckAndAdjustAsync` + HTTP `BookableLimit` contracts unchanged.

**Validation Criteria**:
- [ ] All three sites use piecewise; `/summary` `earned` ↔ `/series` selected-month point reconcile byte-for-byte.
- [ ] `/series` single-fraction workaround removed; curve monotonic non-decreasing on a mid-year change. **No executable `/12m` or `MonthIndex(` enters `BalanceEndpoints.cs` or `SkemaEndpoints.cs`** (the `FormerMirrorSites_OnlyDelegate_NoFormulaBody` theory stays green) — the endpoints only call `EarnedToDatePiecewise`.
- [ ] Skema retains fail-closed 422 on missing anchor (not silent 1.0); fail-closed on empty window; VACATION walk only prices months ≥ `accrualStart`.
- [ ] **asOf split preserved**: SPECIAL_HOLIDAY piecewise `asOf` = `firstAbsenceDate` (no forskud, §13 stk.4); VACATION forskud `asOf` = `ferieaarEnd` — not swapped by the single-call refactor.
- [ ] Gate contract preserved: Skema sends carryover-INCLUSIVE `BookableLimit` to the RuleEngine pre-check; passes carryover-EXCLUDED `guardCap` + annual `seedQuota` to `CheckAndAdjustAsync`; `ValidateEntitlementRequest` request-contract shape unchanged.

**Files Changed** (planned):
- `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`

---

### TASK-6204 — Test & QA: reconciliation, fail-closed, determinism, monotonicity (integration/Docker)

| Field | Value |
|-------|-------|
| **ID** | TASK-6204 |
| **Status** | complete (tests CI-pending — Docker unavailable at close) |
| **Agent** | Test & QA |
| **Components** | Regression / Docker-gated tests |
| **KB Refs** | ADR-016 D10 (replay determinism), ADR-030 |
| **Constraint Validator** | pass (self-check: tests/** scope; no production code) |
| **Reviewer Audit** | folded into Step 7a full-diff (test logic reviewed there); Orchestrator independently verified the piecewise cap arithmetic (16.67 = 25×8/12) + the 15d-allows/17d-rejects boundary |
| **External Review (Codex)** | covered by sprint-end Step 7a |
| **Orchestrator Approved** | yes — 2026-06-03 |

**Build evidence**: Regression test project **compiles, 0 errors** (20 pre-existing warnings, none in the new files). **Tests cannot run locally — Docker unavailable** (same as S61 close); all 5 new/rewritten tests are `[Trait("Category","Docker")]` → **CI-pending**. The superseded S61 `Series_MidFerieaarPartTimeChange_UsesSelectedMonthFraction_Monotonic` was **rewritten** to assert piecewise (selection-independent, per-month trajectory, monotonic-with-bend) — critical: that test pinned the now-superseded single-fraction projection and would otherwise fail the Docker CI. New: reconciliation-under-change, past-asOf determinism, Skema fail-closed 422, Skema piecewise forskud cap (both sides). Constant-fraction emp001 tests unchanged (no-regression proof).

**Description**: HTTP-surface + Docker-gated coverage: `/summary`↔`/series` reconciliation; `/series` monotonicity under a mid-ferieår fraction change (extend S61 `BalanceSeriesTests`); Skema fail-closed (missing anchor) + mid-year-change forskud cap moving in BOTH directions; replay/determinism (re-derive past `asOf` after a later change ⇒ byte-identical); VACATION-walk coverage (carry-backward never prices in-window at 1.0).

**Validation Criteria**:
- [ ] Reconciliation, monotonicity, fail-closed, determinism, walk-coverage tests present and passing (Docker-gated tests CI-pending if Docker unavailable at close — recorded).
- [ ] Gate-contract test: a Skema vacation booking with carryover present still passes carryover-INCLUSIVE `BookableLimit` to the pre-check and carryover-EXCLUDED `guardCap` + annual `seedQuota` to `CheckAndAdjustAsync` (no double-count / no contract drift after the piecewise swap).

**Files Changed** (planned):
- `tests/StatsTid.Tests.Regression/EmployeeProfile/BalanceSeriesTests.cs` + new piecewise/Skema/determinism tests

---

### TASK-6205 — ADR-030 D8 amendment + doc-debt close (Orchestrator)

| Field | Value |
|-------|-------|
| **ID** | TASK-6205 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs (KB, ROADMAP, sprints INDEX, QUALITY) |
| **KB Refs** | ADR-030 |
| **Constraint Validator** | n/a (documentation-only) |
| **Reviewer Audit** | n/a (docs); ADR-030 D8 framing reviewed at refinement + plan stages |
| **Orchestrator Approved** | yes — 2026-06-03 |

**Evidence**: ADR-030 **D8** added (piecewise model + anchor policy + monotonicity + forskud carry-forward + intentional asymmetry; D7 §8/§7 marked still-deferred). Doc-debt closed: ROADMAP *Completed Sprints* table backfilled S58→S62; INDEX *Sprint Index* table backfilled S58→S62 (+ totals → S1–S62, 42 KB entries) + Test Progression S62 row; QUALITY anchor 57→62 + S58–S62 refresh note. `tools/check_docs.py` → **all hard checks pass, freshness WARNING cleared** (checked against S62).

**Description**: Amend ADR-030 with **D8** (piecewise per-month accrual supersedes the D1 single-fraction sub-decision + the S61 `/series` single-fraction note): windowed-≤12 summation invariant, `annualQuota × Σf / 12m` + operation-order/short-circuit rationale, month-START anchor POLICY (intra-month pro-ration out of scope), hire first-month anchor, fraction-piecewise-vs-quota-frozen asymmetry (intentional), same-day-only⇒no-future-edit + forskud carry-forward, monotonicity-by-construction, `/summary` change-month basis shift. Doc-debt close: refresh ROADMAP *Completed Sprints* table + sprints INDEX *Sprint Index* status table + QUALITY.md anchor to S62 (clears the entropy DEBT).

**Validation Criteria**:
- [ ] ADR-030 D8 recorded; `tools/check_docs.py` freshness WARNING cleared; INDEX/ROADMAP/QUALITY anchors current.

**Files Changed** (planned):
- `docs/knowledge-base/decisions/ADR-030-monthly-vacation-accrual-activation.md`
- `ROADMAP.md`, `docs/sprints/INDEX.md`, `docs/QUALITY.md`

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified (unit) | Ferieloven samtidighedsferie: per-month accrual at that month's terms (piecewise); AccrualMath unit tests pin drop/rise/hire/clamp |
| Wage type mappings produce correct SLS codes | N/A | No wage-type change |
| Overtime/supplement calculations are deterministic | N/A | Not touched |
| Absence effects on norm/flex/pension are correct | verified (unit) + CI-pending (HTTP) | VACATION/SPECIAL_HOLIDAY earned-to-date + forskud cap; Skema fail-closed + cap tests Docker-gated (CI-pending) |
| Retroactive recalculation produces stable results | verified (unit) + CI-pending (HTTP) | Pure-fn determinism unit-proven; past-asOf-unaffected-by-later-change HTTP test Docker-gated (CI-pending) |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — 2026-06-03 |
| **Sprint-start commit** | `b8b5281` (S61 close) |
| **Command** | `codex review "..."` (prompt-alone, uncommitted diff) |
| **Review Cycles** | 1 |
| **Findings** | **0** — "Clean — no findings" |
| **Resolution** | n/a — clean cycle 1. Codex confirmed: `EarnedToDatePiecewise` pure/deterministic; endpoint cutovers preserve guard/cap contracts; ≤12 clamp + constant-fraction byte-compat retained; tests/docs consistent. |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 653 (+24 vs S61's 629) | all passing (1 skipped — resolver Docker-gated placeholder) |
| Regression (plain) | 44 | unchanged |
| Regression (Docker-gated) | +~5 new (BalanceSeriesTests rewrite/+2, SkemaPiecewiseAccrualTests +3) | **CI-pending** — Docker unavailable at close |
| Frontend (vitest) | 164 | unchanged — no FE work this sprint |
| **Total (non-Docker)** | **817** (653 unit + 164 FE) | — |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 5 |
| Constraint Violations | 0 |
| Reviewer Findings | Plan-review (0b): 1B (agent-scope labels) + 2W + 5N, all absorbed pre-Step-1 / Per-task (5a): TASK-6201 0, TASK-6203 0 |
| External Review Cycles | Refinement (Step 4): 2 · Plan (0b): 2 · Step 7a: 1 |
| External Findings | Refinement: 2B+2W+~4N (absorbed) · Plan: 1B+2W (absorbed) · Step 7a: **0** |
| Re-dispatches | 0 |
| First-Pass Rate | 100% (5/5 accepted without re-dispatch) |

## Sprint Retrospective

**What went well**: Clean phased execution (SharedKernel math → resolver → Backend cutover → tests → docs), each task first-pass. The `quota × Σf / 12m` formulation simultaneously satisfied the windowed-clamp BLOCKER (Reviewer) and the decimal-order WARNING (Codex) at refinement time. Dropping the planned RuleEngine forwarder (dead code, Reviewer-caught at plan review) *shrank* scope. Step 7a clean on cycle 1 — the upfront refinement+plan dual-lens reviews front-loaded the correctness work. Independent Orchestrator validation caught a stuck-shell relative-path issue (used absolute paths thereafter) and confirmed the interface addition had a single implementer (no broken fakes).

**What to improve**: Docker unavailable at close again (S61 precedent) → the 5 new/rewritten integration tests are CI-pending; the rewritten S61 single-fraction test is the load-bearing one (it would fail the Docker CI if not updated — done, but unverifiable locally). A periodic cleanup of the ~60+ stale `.claude/worktrees/` agent worktrees (surfaced during validation) would de-noise raw `find`/`grep`.

**Knowledge produced**:
- **ADR-030 D8** (formal) — cumulative piecewise per-month accrual; supersedes the single-fraction sub-decision of D1 + the S61 `/series` note.
- Candidate KB PATs (noted; not yet filed — file if they recur): (a) *row-overlap vs point-in-time temporal predicate* (`effective_from < @to` for window reads vs `<= @asOf` for point reads, both end-exclusive); (b) *single-source guard counts files, not occurrences* (new arithmetic inside the canonical file keeps the guard green); (c) *assert piecewise-curve shape, not difference-of-rounded-points* (per-point 2dp rounding makes consecutive-diff exact-constants brittle).
- Standing follow-up: ADR-030 **D7** §8/§7 payroll settlement (wage-deduction + termination modregning) — still deferred.
