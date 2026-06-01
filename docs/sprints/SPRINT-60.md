# Sprint 60 — Real monthly vacation accrual (activate MONTHLY_ACCRUAL)

| Field | Value |
|-------|-------|
| **Sprint** | 60 |
| **Status** | complete |
| **Start Date** | 2026-06-01 |
| **End Date** | 2026-06-01 |
| **Orchestrator Approved** | yes — 2026-06-01 |
| **Build Verified** | yes — `dotnet build` 0 errors |
| **Test Verified** | yes — 596 unit + 144 FE + 20 accrual/eligibility Docker regression, all green |

## Sprint Goal
Replace the IMMEDIATE up-front vacation grant with real Danish **monthly accrual** (Ferieloven *samtidighedsferie*) for VACATION (25/12 ≈ 2,08 d/md) and SPECIAL_HOLIDAY (5/12 ≈ 0,42 d/md), computed **earned-to-date as a pure rule-engine function of the as-of date**. Enforce the legally-correct **per-type bookable cap**: VACATION allows forskudsferie up to the dynamic ferieår cap (manager approval = the §7 agreement); SPECIAL_HOLIDAY allows **no forskud** (ferieaftale §13 stk.4). Add an HR-managed `employment_start_date` to pro-rate mid-year hires. Refinement: `.claude/refinements/REFINEMENT-monthly-vacation-accrual.md` (READY, dual-lens reviewed; forskud policy from cited deep-research). Payroll consequences (§8 wage-deduction, §7 termination modregning) are OUT of scope.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Cited ADRs (002/018/019/020/021/023/029) + danish-agreements.md resolve. |
| Pattern compliance spot-check | CLEAN | No new anti-patterns; reuses ADR-021 D4 consumption-time two-step + ADR-002 pure rule + S59 DOB field pattern. |
| Orphan detection | CLEAN | MONTHLY_ACCRUAL enum (ADR-021 D6 dead-code) gains its first live consumer — no longer orphan. |
| Documentation drift | CLEAN | S58/S59 logs present; db-schema in sync at S59 close. |
| Quality grade review | CLEAN | No change pending until sprint close. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P2 deterministic rule engine — new accrual fn + contract; P3 — none new; P4 version correctness — as-of-date earned-to-date + dated config; P7 — employment_start_date RBAC; schema migration — sentinel reseed + new column) |
| **External Codex** | invoked 2026-06-01 — cycle 1: 0B/6W; cycle 2: 0B/1W (bookableLimit/guardCap wording, fixed) |
| **Internal Reviewer** | invoked 2026-06-01 — cycle 1: 1B/2W/4N; cycle 2: 0B/1W (same wording nit) — BLOCKER-1 + all cycle-1 findings confirmed resolved |
| **BLOCKERs resolved before Step 1** | yes — BLOCKER-1 (sentinel-reseed 5-agreement-code scope) fixed in 6001/6003; **zero BLOCKERs at cycle-2; Plan Review converged** |

### Findings (cycle 1)
**BLOCKER-1 (Reviewer) — sentinel reseed undercounts.** init.sql seeds VACATION+SPECIAL_HOLIDAY for 5 agreement codes (AC/HK/PROSA + AC_RESEARCH + AC_TEACHING = 20 rows); `DefaultEntitlementConfigs` factory covers only AC/HK/PROSA → AC variants would silently stay IMMEDIATE / factory-vs-init divergence. → **FIXED**: 6001 reseeds all 5 codes + asserts no IMMEDIATE VACATION/SPECIAL_HOLIDAY sentinel remains; 6003 notes AC_RESEARCH/AC_TEACHING are init.sql-only.

**WARNINGs (folded):**
- (Reviewer) Balance `EmploymentProfileResolver` is fail-loud (throws/null) vs Balance's ADR-023 D3 graceful-fallback contract → **FIXED** 6005: `?? 1.0m` + tolerate exception; AC profile-less summary renders (no 500).
- (Reviewer) Skema anchors at `firstAbsenceDate` (batch MIN), not "absence.Date" → **FIXED** 6005 wording + 6009 boundary-batch determinism test.
- (Codex) Balance asOf under-specified → **FIXED**: requested month-end, same anchor in reconciliation.
- (Codex) `bookableLimit` (business, incl carryover) vs repo `guardCap` (carryover-excluded) terminology drift → **FIXED**: pinned in 6002/6004/6005.
- (Codex) rounding/earning curve under-pinned → **FIXED**: default exact-fractional, round on display; 6009 fractional-progression test.
- (Codex) 6009 dep was 6002–6006 vs ACs across 6001–6008 → **FIXED**: 6009 depends 6001–6008.
- (Codex) carryover-unchanged not pinned → **FIXED** 6005/6009 AC.

**NOTEs:** SPECIAL_HOLIDAY carryover_max=0 → 6009 fixture asserts carryoverIn=0 (folded); agent assignments + dependency DAG confirmed correct; KB refs fresh; ADR-030 framing (activation, not correction) correct.

### Resolution
BLOCKER-1 fixed in 6001/6003 before Step 1; all cycle-1 WARNINGs folded into task descriptions/ACs (6002, 6004, 6005, 6009). **Cycle-2:** both lenses confirmed BLOCKER-1 + all cycle-1 findings resolved; the only cycle-2 finding was a `bookableLimit` (carryover-inclusive business cap) vs `guardCap` (carryover-excluded repo input) wording contradiction in the TASK-6002 AC — corrected to match 6004/6005. **Zero BLOCKERs at cycle-2 → Plan Review converged; plan approved to proceed to Step 1 (decompose).**

## Architectural Constraints Verified
- [x] P1 — additive; reuses ADR-021 D4 consumption boundary; no bounded-context breach
- [x] P2 — `earnedToDate` is a PURE rule-engine fn of the passed asOf date (no I/O, no wall-clock); replay-deterministic
- [x] P3 — no new events; entitlement_balances forward-only; total_quota keeps meaning "annual entitlement"
- [x] P4 — earned-to-date + dated config resolved as-of the consumption date (per ADR-021 D4); replay-stable
- [x] P7 — `employment_start_date` HR-scoped (set+read HROrAbove+OrgScope); not in Employee payloads/JWT

## Task Log

### TASK-6001 — Schema: activate MONTHLY_ACCRUAL + employment_start_date
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into `docker/postgres/init.sql`, cross-domain authorized) |
| **Components** | init.sql, db-schema |
| **KB Refs** | ADR-021 D5 (sentinel-not-supersession), ADR-029 (S59 DOB-field precedent), ADR-019/020 |
| **Depends on** | — |
**Description**: (a) **Sentinel reseed** (NOT supersession) the `0001-01-01` VACATION + SPECIAL_HOLIDAY entitlement_config rows to `accrual_model = MONTHLY_ACCRUAL` — **across ALL agreement codes seeded in init.sql** (resolves Plan-Review BLOCKER-1): AC/HK/PROSA **+ AC_RESEARCH + AC_TEACHING** (= 10 VACATION + 10 SPECIAL_HOLIDAY rows). Preserves the ADR-021 D5 invariant (no `(natural_key, effective_from)` pair disagrees on accrual_model). (b) Add `employment_start_date DATE NULL` to `users` via `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` (S59 birth_date pattern); confirm `users_audit` JSONB snapshot captures it. (c) Orchestrator regenerates `docs/generated/db-schema.md`.
**Validation Criteria**:
- [x] **Every** VACATION + SPECIAL_HOLIDAY `0001-01-01` sentinel row across **all 5 agreement codes** = MONTHLY_ACCRUAL; an assertion confirms **NO** VACATION/SPECIAL_HOLIDAY sentinel anywhere in init.sql remains IMMEDIATE (catches the AC_RESEARCH/AC_TEACHING hole).
- [x] No disagreeing `(natural_key, effective_from)` pair (D5 invariant holds).
- [x] `employment_start_date` nullable on users; users_audit captures it.
- [x] `tools/check_docs.py` passes (db-schema regenerated).

### TASK-6002 — Rule Engine: earnedToDate + per-type bookableLimit
| Field | Value |
|-------|-------|
| **Agent** | Rule Engine |
| **Components** | ValidateEntitlementRequest, EntitlementValidationRule, earnedToDate fn |
| **KB Refs** | ADR-002 (pure fn), ADR-021 D4/D6, danish-agreements.md (2,08 / 0,42; §13 stk.4) |
| **Depends on** | — (contract; Backend wires it in 6005) |
**Description**: Add a pure `earnedToDate(annualQuota, partTimeFraction, ferieårStart, employmentStart, asOfDate) → daysEarned` (no I/O, no wall-clock; null employmentStart → full-ferieår). **Rounding default (pin):** accrue **exact fractional days**, round only for display — unless an SME changes it (Q3). Extend `ValidateEntitlementRequest` with the inputs (`accrualModel`, `ferieårStart`, `employmentStart`, `asOfDate`) and a **distinct `bookableLimit`** field (NOT overloading `EffectiveQuota`). **Terminology (pin — resolves Codex WARNING):** rule-engine `bookableLimit` = the *business cap INCLUDING carryover* (what the user may book); the repo `guardCap` (TASK-6004) = *carryover-EXCLUDED* because `CheckAndAdjustAsync` adds carryover itself. In `EntitlementValidationRule`: for MONTHLY_ACCRUAL the rejection cap = per-type — VACATION `earned + stillAccruableInFerieår (+carryover)` (forskud allowed); SPECIAL_HOLIDAY `earned (+carryover)` (NO forskud). The **20%-warning threshold + per-episode branch keep keying off ANNUAL quota**. IMMEDIATE types unchanged. SENIOR_DAY age gate untouched.
**Validation Criteria**:
- [x] `earnedToDate` pure + deterministic on passed asOf (unit-tested across the ferieår, part-time, employmentStart mid-year, null fallback).
- [x] Business `bookableLimit` per-type (carryover-INCLUSIVE — the pre-tx check): VACATION = earned + stillAccruable + carryover; SPECIAL_HOLIDAY = earned + carryover (no forskud). The repo `guardCap` (6004) is the SAME minus carryover (carryover-EXCLUDED), since `CheckAndAdjustAsync` re-adds carryover. The two named caps stay distinct.
- [x] Warning-threshold + per-episode still key off annual quota (no spurious early-ferieår warnings).
- [x] IMMEDIATE types byte-for-byte unchanged.

### TASK-6003 — Config flip in DefaultEntitlementConfigs
| Field | Value |
|-------|-------|
| **Agent** | Rule Engine (extended into `src/SharedKernel/**/Config/**`, cross-domain authorized) |
| **Components** | DefaultEntitlementConfigs |
| **KB Refs** | ADR-021 D5/D6 |
| **Depends on** | — |
**Description**: Set `AccrualModel = MONTHLY_ACCRUAL` for VACATION + SPECIAL_HOLIDAY in `DefaultEntitlementConfigs` (matches 6001). **Scope note (Plan-Review BLOCKER-1):** the factory's `AgreementCodes` covers **only AC/HK/PROSA** — AC_RESEARCH/AC_TEACHING are seeded **only in init.sql**, not the factory. So 6003 flips the factory's AC/HK/PROSA VACATION+SPECIAL_HOLIDAY; AC_RESEARCH/AC_TEACHING are covered by 6001's init.sql reseed + its no-IMMEDIATE-leftover assertion (do NOT silently assume the factory covers all 5). No quota/reset_month change.
**Validation Criteria**:
- [x] VACATION + SPECIAL_HOLIDAY factories (AC/HK/PROSA) return MONTHLY_ACCRUAL; pinning test updated; consistent with init.sql for those codes.
- [x] AC_RESEARCH/AC_TEACHING confirmed factory-absent (init.sql-only) — no factory/init divergence (the 6001 assertion is the safety net).

### TASK-6004 — Infra: CheckAndAdjustAsync split + employment_start read-model
| Field | Value |
|-------|-------|
| **Agent** | Infrastructure (extended into `src/SharedKernel/**/Models/User.cs`, cross-domain authorized) |
| **Components** | EntitlementBalanceRepository, User model, UserRepository |
| **KB Refs** | ADR-018, ADR-021 D4, ADR-029 (DOB read-model precedent) |
| **Depends on** | TASK-6001 (column) |
**Description**: (a) **Split `CheckAndAdjustAsync`** so the atomic guard WHERE-clause cap (`guardCap`, **carryover-EXCLUDED** — the method already adds `carryover_in`) and the first-INSERT `total_quota` seed (`seedQuota` = **annual entitlement**, unchanged meaning) are **distinct args** — no carryover double-count, no total_quota corruption. (b) Add `EmploymentStartDate` (DateOnly?) to the `User` model (init-only) + `UserRepository` read (`SELECT`) + version-guarded write, mirroring S59 birth_date.
**Validation Criteria**:
- [x] `CheckAndAdjustAsync` guardCap (carryover-excluded) vs seedQuota (annual) distinct; a test pins: guard rejects at the right cap AND a freshly-seeded row's total_quota = annual (not bookableLimit), carryover counted once.
- [x] `User.EmploymentStartDate` exposed; UserRepository read + write; HR-scoped.

### TASK-6005 — Backend: wire accrual into the two consumption seams
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | SkemaEndpoints POST quota validation, BalanceEndpoints summary |
| **KB Refs** | ADR-021 D4 (consumption two-step), ADR-002/PAT-005 (rule-engine HTTP), ADR-023 (dated profile) |
| **Depends on** | TASK-6002 (rule contract), 6003 (config), 6004 (repo split + User.EmploymentStartDate) |
**Description**: At BOTH seams, pass the new rule inputs (`accrualModel`, `ferieårStart`, `employmentStart` from User, `asOfDate`). **Anchors (pin — resolves WARNINGs):** Skema = **`firstAbsenceDate`** (the existing per-type batch anchor at `SkemaEndpoints.cs:~731`, the MIN of the batch — NOT loosely "absence.Date"); Balance = **requested month-end**; reconciliation tests use the matching anchor. **Source the dated `partTimeFraction`** via `EmploymentProfileResolver` (drop the hard-coded `1.0m` at `BalanceEndpoints.cs:161` / `SkemaEndpoints.cs:~712`) — **but the Balance seam must stay graceful (ADR-023 D3): `datedProfile?.PartTimeFraction ?? 1.0m` and tolerate the resolver's `EmployeeProfileNotFoundException`/null so a profile-less employee's summary still renders (no 500)**; Skema's seam already hard-fails on missing profile by design (keep per-seam). Enforce the business `bookableLimit` at the pre-tx check AND pass the carryover-EXCLUDED `guardCap` to the split `CheckAndAdjustAsync`. Balance summary reports earned/available for VACATION+SPECIAL_HOLIDAY; decide + apply the legacy top-level `vacationDaysEntitlement`/`vacationDaysUsed` disposition (stays annual vs earned vs deprecate) — no UI/API drift. Carryover/16-month expiry semantics **unchanged** except the cap split/count-once.
**Validation Criteria**:
- [x] Both seams pass asOf-dated inputs (Skema firstAbsenceDate / Balance month-end) + dated partTimeFraction; no hard-coded 1.0.
- [x] Balance seam stays graceful: a profile-less employee's summary renders (no 500) — falls back to 1.0.
- [x] VACATION forskud allowed up to dynamic cap; SPECIAL_HOLIDAY booking beyond earned rejected (422); both enforced pre-tx AND in the atomic guard.
- [x] Balance summary + Skema validation report a consistent VACATION number for the same as-of date; legacy fields disposition applied; carryover semantics unchanged.

### TASK-6006 — Backend: HR employment_start_date admin endpoint
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized into Security) |
| **Components** | employment-start admin set/read endpoint, Program.cs mapping |
| **KB Refs** | ADR-007, ADR-019 D2 (If-Match), SECURITY.md (OrgScope), ADR-029 (DOB endpoint precedent) |
| **Depends on** | TASK-6004 (User.EmploymentStartDate + UserRepository) |
**Description**: HR set/read `employment_start_date` (HROrAbove + OrgScopeValidator + If-Match), mirroring the S59 birth-date endpoints. Read gated HROrAbove+OrgScope; never in Employee-facing DTO/JWT/export.
**Validation Criteria**:
- [x] GET/PUT require HROrAbove+OrgScope+If-Match; cross-org rejected; not exposed to non-HR/Employee/JWT/export.

### TASK-6007 — UX: HR employment-start field
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | employee admin page (employment-start input); BalanceSummary sanity |
| **KB Refs** | FRONTEND.md, ADR-029 (DOB field UX precedent) |
| **Depends on** | TASK-6006 |
**Description**: Add an HR-only `employment_start_date` field to the employee admin page (the S59 DOB-field pattern in UserManagement), read-then-If-Match. Verify the existing monthly `BalanceSummary` still renders correct numbers now that VACATION/SPECIAL_HOLIDAY are earned-to-date (it consumes `/summary`); no Oversigt work here (separate parked sprint).
**Validation Criteria**:
- [x] HR can set/read employment-start (read-your-write); Danish labels; existing FE suite green + a test for the new field.

### TASK-6008 — Security audit
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance |
| **Components** | employment_start_date RBAC, endpoint authz |
| **KB Refs** | SECURITY.md, FAIL-001, ADR-029 |
| **Depends on** | 6005, 6006 |
**Description**: Verify `employment_start_date` never leaks to non-HR/Employee/JWT/export; set+read require HROrAbove+OrgScope; FindAll (not FindFirst) on scopes; new repo queries parameterized.
**Validation Criteria**:
- [x] No employment-start leak; authz + scope verified.

### TASK-6009 — Tests
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | unit + Docker regression |
| **KB Refs** | — |
| **Depends on** | 6001–6008 |
**Description**: Unit — `earnedToDate` matrix (ferieår progression with **exact-fractional** rounding, part-time, mid-year employmentStart, null fallback), per-type bookableLimit (VACATION forskud cap, SPECIAL_HOLIDAY no-forskud with the fixture asserting `carryoverIn = 0` so the cap proves no-forskud not a zero-carryover artifact), warning-on-annual, CheckAndAdjust guardCap-vs-seedQuota; config MONTHLY_ACCRUAL pin. Regression (Docker) — VACATION forskud allowed up to dynamic cap; SPECIAL_HOLIDAY booking-beyond-earned 422; both enforcement points; total_quota stays annual after first booking (carryover counted once); a **multi-date / ferieår-boundary-spanning batch** locks the firstAbsenceDate anchor; carryover semantics unchanged; IMMEDIATE types unchanged; reconciliation (summary == validation at the matching anchor); profile-less employee summary renders (no 500).
**Validation Criteria**:
- [x] All ACs across 6001–6008 covered; suites green; `dotnet build` clean.

## Orchestrator-authored artifacts (not agent tasks)
- **ADR-030** — Activate monthly vacation accrual. Supersedes ADR-021 D6 (MONTHLY_ACCRUAL now live for VACATION + SPECIAL_HOLIDAY) + annotates D5 (accurate as of S30; activation event recorded, not "corrected"). Records: compute-on-read pure fn; per-type bookableLimit (VACATION dynamic forskud cap w/ manager-approval-as-§7-agreement; SPECIAL_HOLIDAY no-forskud per ferieaftale §13 stk.4); employment_start_date as a non-dated pure input (correction re-derives uniformly); §8 wage-deduction + §7 termination modregning OUT of scope (payroll follow-up). Binds to the architectural event (accrual-model activation), not "S60".
- **ROADMAP.md** + **sprints/INDEX.md** — S60 row; coverage tracker.

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | Accrual 2,08 d/md (VACATION) + 0,42 d/md (SPECIAL_HOLIDAY) per Ferieloven §5 / ferieaftale §12; forskud cap = earned + still-accruable-in-ferieår (Ferieloven §7); SPECIAL_HOLIDAY no-forskud (ferieaftale §13 stk.4) — cited deep-research; unit + Docker regression cover the curve, both caps, both enforcement points |
| Wage type mappings produce correct SLS codes | N/A | No wage-type change |
| Overtime/supplement determinism | N/A | — |
| Absence effects correct | verified | VACATION/SPECIAL_HOLIDAY booking gated by per-type bookableLimit; IMMEDIATE absence types unchanged (regression-pinned) |
| Retroactive recalculation stable | verified | earned-to-date is a pure fn of as-of date (no wall-clock); replay-deterministic (unit-pinned) |
| Payroll consequences (§8 deduction, §7 modregning) | deferred | OUT of scope (ADR-030 D7) — tracked as payroll follow-up |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (Codex `codex review` prompt-alone, uncommitted) + internal Reviewer Agent (incl. TASK-6008 security audit) |
| **Sprint-start commit** | `b91e5a6` (S59) |
| **Review Cycles** | 3 |
| **Findings** | cycle 1: 1 BLOCKER (Codex legacy-DB activation) + 2 WARNING (W1 both, W2 Reviewer) + NOTEs (Reviewer security audit clean); cycle 2: BLOCKER+W1 resolved, 1 residual P2 (resolver still throws for IMMEDIATE non-pro-rated types); cycle 3: clean |
| **Resolution** | all resolved — cycle-3 "No findings" |

### Findings
- **BLOCKER (Codex c1)** — entitlement_configs seed is `ON CONFLICT DO NOTHING`, so legacy/existing DBs kept IMMEDIATE VACATION/SPECIAL_HOLIDAY rows → monthly accrual only activated on fresh bootstrap. **FIXED** (init.sql): added an idempotent `UPDATE entitlement_configs SET accrual_model='MONTHLY_ACCRUAL' WHERE entitlement_type IN ('VACATION','SPECIAL_HOLIDAY') AND accrual_model='IMMEDIATE'` after the seed (flips existing rows; whole natural-key family agrees, D5-safe).
- **WARNING W1 (both lenses)** — a 3rd `EarnedToDate` copy in `BalanceEndpoints` was not covered by the PAT-005 reconciliation test (silent-drift risk). **FIXED**: the reconciliation test now reflects BOTH `SkemaEndpoints` and `BalanceEndpoints` copies vs `AccrualCalculator`. *Follow-up:* consolidate the pure `EarnedToDate` into SharedKernel to delete both Backend copies (a SharedKernel calc does not violate PAT-005).
- **WARNING W2 (Reviewer) + cycle-2 residual (Codex)** — the Skema profile-missing 422 had broadened to ALL absence types (incl. IMMEDIATE non-pro-rated CARE_DAY). First fix narrowed the *null* case to fraction-relevant types; cycle-2 noted the resolver could still THROW (→500) for those types. **FINAL FIX**: the resolver is SKIPPED entirely unless the fraction is load-bearing (`isMonthlyAccrual || ProRateByPartTime`); when it matters, both a null return and a caught `EmployeeProfileNotFoundException` map to a clean 422. IMMEDIATE non-pro-rated types behave exactly as pre-S60 (fraction = 1.0, no resolver call). Cycle-3 verified clean.
- **Reviewer security audit (TASK-6008): clean** — `employment_start_date` confined to the two HR-gated endpoints + `users_audit`; not in any Employee DTO/JWT/export/admin-list projection; GET+PUT require HROrAbove+OrgScope (+If-Match on PUT); FindAll (FAIL-001); parameterized.
- NOTEs: dead-ish `useBookableLimit` disjunct (clarity); Balance anchor monthStart→monthEnd (intentional, ADR-030 D5); SPECIAL_HOLIDAY may legitimately WARNING early in the ferieår (low earned) — flagged for UX awareness.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 596 | all passing (S59 559 + S60 ~37: AccrualCalculator, BookableLimit, config pin, reconciliation) |
| Regression (Docker) | 20 reviewed | all passing — 7 new `SkemaMonthlyAccrualGuardTests` (forskud cap, no-forskud, both enforcement points, total_quota stays annual, boundary-batch anchor, IMMEDIATE unchanged) + 13 S59 eligibility (no regression) |
| Frontend | 144 | all passing (142 + 2 employment-start field) |
| Smoke | — | N/A (Docker) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 9 (+ Orchestrator: ADR-030, init.sql UPDATE/db-schema, ROADMAP, INDEX) |
| Constraint Violations | 0 (Orchestrator self-check per phase) |
| Reviewer Findings | Step-0b 1B/2W/4N; Step-7a 0B/2W + clean security audit |
| External Review Cycles | 2 (sprint-end) + 2 (Step-0b plan) |
| External Findings | Step-7a: 1 BLOCKER + 1 WARNING (all fixed) |
| Re-dispatches | 0 domain re-dispatches; Orchestrator made 4 Step-7a/merge fixes (missing using, init.sql UPDATE, W1 test, W2 narrowing) |
| First-Pass Rate | 9/9 domain tasks first-pass; defects (legacy-DB activation, drift gap, 422 breadth) surfaced by Step-7a external review |

## Sprint Retrospective

**What went well**: The deep-research forskudsferie answer (cited primary sources) produced the legally-correct *dynamic* cap rather than the wrong binary I first posed; the compute-on-read pure-fn design kept determinism clean. Lens complementarity again: Codex caught the legacy-DB `ON CONFLICT DO NOTHING` BLOCKER (activation only on fresh bootstrap) that the Reviewer's row-level check missed, while the Reviewer caught the 422-breadth behavior change and ran the security audit.

**What to improve**: PAT-005 forced a Backend-local `EarnedToDate` duplicate that proliferated to a *third* copy before the reconciliation test caught the gap — a pure cross-cutting calc belongs in SharedKernel from the start (follow-up). Data-activation changes guarded by `ON CONFLICT DO NOTHING` need an explicit idempotent UPDATE for non-fresh DBs — a recurring init.sql idiom to apply by default.

**Knowledge produced**: ADR-030 (activate MONTHLY_ACCRUAL; supersedes ADR-021 D6, annotates D5). **Follow-ups:** (1) consolidate `EarnedToDate` into SharedKernel; (2) payroll §8 wage-deduction + §7 termination modregning; (3) the parked **Oversigt** sprint (now shows real accrued values).
