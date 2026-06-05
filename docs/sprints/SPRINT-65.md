# Sprint 65 — Årsoversigt: Design Handoff Direction E replaces the Oversigt page

| Field | Value |
|-------|-------|
| **Sprint** | 65 |
| **Status** | planned |
| **Start Date** | 2026-06-05 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |

## Sprint Goal
Replace the S61 `tid/oversigt` dashboard with the design-handoff **Direction E Årsoversigt** (year-at-a-glance: 6 current-balance tiles + a months × categories matrix), backed by a new read-only `GET /api/balance/{employeeId}/year-overview?year=Y` endpoint whose every quantity derives from the existing rule-engine/projection primitives (ADR-028 work-time + per-day dated norm, ADR-030/031 flat accrual, hours-based day-equivalent consumption). Design source: `design_handoff_oversigt/` (committed this sprint as design source of record). Refinement: `.claude/refinements/REFINEMENT-s65-aarsoversigt-direction-e.md` (Step-4 dual-lens reviewed: Codex 2 cycles → "Clean — absorptions verified"; Reviewer 0 BLOCKERs).

**Owner ratifications (2026-06-05):** S65/S66 swap ratified (ADR-032 §6 stk.2 + `work_days_per_week` becomes S66 — still LAUNCH-BLOCKING, event-bound per ADR-031 D6); Feriefridage added as 5th matrix group (tiles stay the designed 6); Arbejdstid semantics = worked-to-date + running diff for the current month, norm-as-projection + diff "–" for future months. **OQ-1 RESOLVED (owner 2026-06-06, deep-research-backed): December placement, as drawn.** The December-carryover hypothesis CONFIRMED: Ferielov §21 stk.2 fixes the transfer-agreement deadline at "senest den 31. december i ferieafholdelsesperioden" — 31 Aug is only the accrual-year end, not a legal transfer point (verdict: 23/25 claims confirmed 3-0; full citations in `docs/references/ferie-transfer-timing-research.md`). `boundaryMonth = 12` for ALL categories (display anchor); the VALUE stays computed at the type's model boundary (31 Aug for ResetMonth-9 types, 31 Dec for calendar types). Accepted tradeoff (owner-acknowledged via preview): the Sep–Dec saldo cells already include the carried-over days the Dec figure displays. Research bonus finding ratified into ROADMAP follow-up: real state-sector særlige feriedage accrue by CALENDAR year, taken 1 May–30 Apr (Cirkulære 021-24 §12 stk.2) — the system's ResetMonth-9 SPECIAL_HOLIDAY model is a documented simplification (invisible in S65: carryover 0 → "–").

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `tools/check_docs.py` all green: db-schema in sync (55 tables), KB INDEX 43 entries / 0 orphans / 0 dangling, sprint inventory complete through S64 |
| Pattern compliance spot-check | CLEAN | 0× `FindFirst("scopes")` (FAIL-001); 0 RuleEngine imports outside `src/RuleEngine` (PAT-005); no hardcoded `http://localhost` in non-test src; endpoint auth coverage full (AuthEndpoints 1/0 = anonymous login by design) |
| Orphan detection | CLEAN | No stale S63/S64 orphans (S64 was test-side; S63 deleted its dead surface with itself). Planned: this sprint deletes `LeaveOverview`/`AccrualTrend`/`useAccrualSeries` (sole-consumer chain verified) and FE-orphans `GET /api/balance/{id}/series` (kept; retire-or-consolidate follow-up recorded in ROADMAP) |
| Documentation drift | DRIFT (fixed) | **docs/FRONTEND.md still documented the pre-S57 blue palette** (`#0059B3` primary + 11 stale status/link hexes) despite the S57 oes.dk re-skin — fixed in-sprint (palette table synced to tokens.css; "tokens.css is canonical" note added) since the UX Agent consumes this doc. MEMORY otherwise current; S64 whole-workflow-green CI verification RESOLVED: run [27009829974](https://github.com/fanboy1000000/StatsTid/actions/runs/27009829974) green on all six jobs (fix-forward #2 `0fcf998` cleared the FE spinner-race flake from run 26985210958, whose `build-and-test` was already GREEN, proving the S64 fetch-depth fix); URL backfilled into SPRINT-64.md § Test Summary per the close-polish exemption |
| Quality grade review | CLEAN | Grades current @ S64 (CI/Tooling A−). This sprint targets Frontend (B) + Backend API (A) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (cross-domain: Backend + Frontend + Tests; P2-adjacent read surfaces; new shared helper extraction) |
| **External Codex** | pending |
| **Internal Reviewer** | pending |
| **BLOCKERs resolved before Step 1** | pending |

### Findings (cycle 1)
_pending_

### Resolution
_pending_

## Pinned API Contract (TASK-6502 ⇄ TASK-6503/6504 interface)

`GET /api/balance/{employeeId}/year-overview?year=YYYY` — `EmployeeOrAbove` + employee-self / `OrgScopeValidator` (mirror `/summary` BalanceEndpoints.cs:56-67). Pure read; no events; no schema change.

```jsonc
{
  "employeeId": "emp001",
  "year": 2026,
  "today": "2026-06-05",            // server date — sole past/current/future + "Nu" authority
  "header": {
    "employeeName": "Anna Berg",
    "agreementCode": "AC",           // dated read at today (user_agreement_codes)
    "okVersion": "OK26",             // OkVersionResolver at today — display context ONLY (matrix resolves per day)
    "weeklyNormHours": 37.0          // merged-config WeeklyNorm × current PartTimeFraction; null if no profile/config
  },
  "tiles": {
    "flexBalance": 22.5,             // latest FlexBalanceUpdated (same read as /summary:167-171)
    "ferieRemaining": 22.0,          // current entitlement-loop remaining (flat ADR-031 EarnedToDate + carryoverIn − used − planned)
    "careDayRemaining": 1.0,
    "seniorDayRemaining": 3.0,       // null when not seniorDayEligible
    "sickDaysYtd": 4,                // distinct SICK_DAY dates, current calendar year (not quota-gated → distinct-date is the right primitive)
    "childSickRemaining": 1.0,       // null when not childSickEligible
    "childSickEligible": true,       // S59/ADR-029 employee_entitlement_eligibility (opt-in)
    "seniorDayEligible": true        // birth_date + config min_age as of today (display affordance — NOT a re-encoding of the rule-engine gate)
  },
  "months": [                        // index 0..11 = Jan..Dec of the selected year
    {
      "month": 1,
      "workedHours": 150.2,          // Σ work_time_projection rows in the month (ADR-028)
      "normHours": 147.9,            // Σ DailyNormCalculator per-day norms; null if ANY norm-bearing day resolves null (ANNUAL_ACTIVITY / no dated profile)
      "diff": 2.3                    // workedHours − normHours for months ≤ today's month; null for future months (no fabricated performance)
    }
  ],
  "categories": [                    // order: VACATION, SPECIAL_HOLIDAY, CARE_DAY, SENIOR_DAY
    {
      "type": "VACATION",
      "label": "Ferie",
      "saldo": [10.4, 12.5, ...],    // end-of-month remaining: EarnedToDate(annualQuota, 1.0m, entitlementYearStart, employmentStart, monthEnd) + carryoverIn − cumulative afholdt within the entitlement year containing that month (Jan–Aug = ferieår Y−1, Sep–Dec = ferieår Y for ResetMonth-9 types; calendar types = calendar year). Sep shows the reset sawtooth — correct domain shape.
      "afholdt": [0, 1.5, ...],      // Σ absences_projection Hours of the type in the month ÷ StandardDayHours (day-equivalents — same math as the quota guard SkemaEndpoints.cs:738/:1076; future-dated rows = planlagt)
      "transferable": 5.0,           // min(max(0, earnedAtBoundary + carryoverIn − used − planned), carryoverMax); earnedAtBoundary COMPUTED at the type's model boundary (31 Aug of the selected year for ResetMonth-9 types — the ferieår spanning Sep Y−1–Aug Y; 31 Dec for calendar types); carryoverMax year-start dated (ADR-021 D2); deterministic derived boundary date
      "boundaryMonth": 12            // OQ-1 RESOLVED (owner 2026-06-06): 12 for ALL categories — DISPLAY anchor only. For ferie this is the legal §21 stk.2 transfer-agreement deadline (31 Dec of the calendar year whose ferieår ended 31 Aug); computation stays at the model boundary per `transferable` above
    }
  ]
}
```

Notes: camelCase per existing endpoints; decimals as numbers (`Math.Round(., 2)` per the `/summary` idiom); dates ISO `yyyy-MM-dd`. Graceful per ADR-023 D3: profile-less/inconsistent employees get nulls/zeros, never a 500. Months before `employment_start_date` resolve no dated profile → `normHours: null` → "–" cells (mechanical, no special-casing).

## Task Log

### TASK-6501 — Sprint open: scaffold + ROADMAP re-prioritization + design handoff commit

| Field | Value |
|-------|-------|
| **ID** | TASK-6501 |
| **Status** | in-progress |
| **Agent** | Orchestrator |
| **Components** | docs/sprints, ROADMAP.md, design_handoff_oversigt/ |
| **KB Refs** | — |
| **Orchestrator Approved** | — |

**Description**: SPRINT-65.md scaffold (this file); ROADMAP "Current position" block: S65 = Årsoversigt (this sprint, owner-ratified swap), S66 = ADR-032 §6 stk.2 + `work_days_per_week` (LAUNCH-BLOCKING, event-bound per ADR-031 D6); Tier-1 re-prioritization (1 planned sprint affected). Commit `design_handoff_oversigt/` as design source of record. Record the `/series` FE-orphaning follow-up + the `/summary` norm-fields retire-or-align follow-up in ROADMAP.

**Validation Criteria**:
- [ ] SPRINT-65.md exists with pinned contract + task specs
- [ ] ROADMAP next-sprint updated at OPEN (not close) — docs stay truthful while the sprint runs
- [ ] `design_handoff_oversigt/` committed (7 files)

---

### TASK-6502 — Year-overview endpoint + DailyNormCalculator extraction

| Field | Value |
|-------|-------|
| **ID** | TASK-6502 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized — S22/S24 convention; src/Backend/** only) |
| **Components** | Backend.Api (BalanceEndpoints, SkemaEndpoints, new Services/DailyNormCalculator.cs) |
| **KB Refs** | ADR-021 (D2 year-start dated config, D4 consumption-time lookup), ADR-023 D3 (graceful HTTP-read fallback), ADR-028 D3 (per-day norm semantics), ADR-030 D1–D6 (flat earned-to-date, employment_start_date), ADR-031 (fraction-independent day-count), ADR-003 (OK by entry date), ADR-007/ADR-009 (auth), DEP-004 (endpoint registry) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | MANDATORY (P2-adjacent + new shared abstraction) |
| **Orchestrator Approved** | — |

**Description**:
1. **Extract `DailyNormCalculator`** to `src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs`: the TASK-5603 per-day norm loop verbatim from `SkemaEndpoints.cs:310-364` (dated profile per day via `IEmploymentProfileResolver`, `ConfigResolutionService` merged config with per-request cache key `okVersion|agreement|position|fraction|orgId`, `OkVersionResolver.ResolveVersion(day)` per day, weekends 0, `ANNUAL_ACTIVITY` → null, `Math.Round(weekly × fraction / 5, 2)`). Refactor SkemaEndpoints GET /month onto it **behavior-preservingly** (existing regression tests must stay green unmodified). It is an I/O-orchestrating read-side helper — it must NOT live in SharedKernel (ADR-002 boundary; refinement Reviewer W1).
2. **Promote `StandardDayHours = 7.4m`** (SkemaEndpoints.cs:75) to one shared Backend.Api constant consumed by both SkemaEndpoints and the new endpoint — no second literal.
3. **Implement `GET /api/balance/{employeeId}/year-overview?year=YYYY`** in BalanceEndpoints per the Pinned API Contract: auth mirror of `/summary`; per-month worked (work_time_projection range read) + norm (DailyNormCalculator) + diff (null future); categories VACATION/SPECIAL_HOLIDAY/CARE_DAY/SENIOR_DAY with saldo/afholdt day-equivalent arrays honoring the entitlement-year straddle; transferable (Option-B flat formula) at boundaryMonth; tiles incl. sickDaysYtd (distinct SICK_DAY dates, current year) + eligibility flags (S59 eligibility read; birth_date + min_age for senior). All accrual via `AccrualMath.EarnedToDate(…, 1.0m, …)`; all configs year-start dated; no duplicated rule logic anywhere.

**Validation Criteria**:
- [ ] `dotnet build` 0 warnings 0 errors; existing Skema regression tests green unmodified
- [ ] Endpoint returns the pinned contract shape; 403 for foreign employeeId under Employee role
- [ ] No SharedKernel/RuleEngine file touched; no new literal `7.4`; no `DateTime.Now`/`Today` (server `today` from a single injected/derived `DateOnly` seam)
- [ ] Graceful (never 500) for profile-less employees per ADR-023 D3

---

### TASK-6503 — ArsoversigtPage + route swap + SkemaPage params + deletions + FE tests

| Field | Value |
|-------|-------|
| **ID** | TASK-6503 |
| **Status** | planned |
| **Agent** | UX Agent (frontend/**) |
| **Components** | frontend pages/hooks/routing |
| **KB Refs** | ADR-011 (design system: tokens, CSS Modules, AA), DEP-004; docs/FRONTEND.md; design_handoff_oversigt/README.md + reference/oversigt.{jsx,css} (Direction E only) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | per trigger table |
| **Orchestrator Approved** | — |

**Description**:
1. `useYearOverview(employeeId, year)` hook (the `useBalanceSummary` shape) typed to the pinned contract.
2. `ArsoversigtPage.tsx` + `ArsoversigtPage.module.css`: header row (h1 "Årsoversigt {year}" 26px/600; sub "{name} · {agreementCode} · Norm: {weeklyNormHours} t/uge" 14px secondary; ← year → ghost-Button switcher, centered label min-width 64px); 6 balance tiles (`repeat(6,1fr)` gap 14; white, 1px `--color-border`, 0 radius, padding 14px 16px; uppercase 11.5px/500 `--color-gray-500` label; 25px/700 tabular value + small unit; 12px secondary sub); year matrix in flush `Card`: fixed-layout semantic `<table>` (168px label col + 12 right-aligned month cols; `<th scope>`; gray-100 header, 2px bottom border; current-month col `--color-info-light` tint full height + 3px `--color-info` top accent + "Nu" tag 9.5px uppercase; group rows uppercase 12px/600 with 2px `--color-border-strong` border + 16px top padding; sub-row indent 26px; future months `--color-text-secondary`; signed diff `--color-success`/`--color-error`; transferable > 0 `--color-info`/600; 0/NA em-dash `--color-gray-400`; row hover `--color-gray-50`). **Five** matrix groups (owner-ratified): Arbejdstid (Arbejdstid + Diff. fra norm), Ferie, Feriefridage, Omsorgsdage, Seniordage (each: Saldo (rest) / Afholdt / Kan overføres). Cell rule (server `today` authority): past/current Arbejdstid = `workedHours`, Diff = signed `diff`; future Arbejdstid = `normHours` projected-styled, Diff "–"; `normHours: null` → "–". Tiles render "–" + unchanged layout when ineligible (`childSickEligible`/`seniorDayEligible` false). da-DK via `formatDanishNumber` (fractional day-equivalents like "0,5" expected); translate `ov-stat*`/`ov-y*` to CSS Modules with REAL tokens only — no kit.css/colors_and_type.css, no new hex, no `ov-seg*`/`#a7cfbf`.
3. Month-header drill-in → `/tid/registrering?year=Y&month=M` (button-in-th); SkemaPage `useSearchParams` init (defaults to today; ~5 lines).
4. Route swap in App.tsx (`tid/oversigt` → ArsoversigtPage); Sidebar label unchanged.
5. **Delete**: OversightPage.tsx/.module.css/test, LeaveOverview.tsx/.module.css/test, AccrualTrend.tsx/.module.css/test, useAccrualSeries.ts. **Do not touch**: ComplianceWarnings (SkemaPage consumer), useBalanceSummary, useCompliance.
6. FE vitest for the new page: tiles (da-DK values, sub-lines, ineligible "–"); matrix structure (5 groups, 13 cols, sub-rows); "Nu" highlight from a mocked server `today` (NOT client clock); future-cell rule (norm shown, diff "–"); fractional afholdt ("0,5"); transferable only at `boundaryMonth` in info styling; em-dashes; year-switcher refetch; drill-in navigation; SkemaPage param init.

**Validation Criteria**:
- [ ] `npx tsc --noEmit` clean; `npx vitest run` green (old tests removed with their components — expected count drop documented)
- [ ] Zero new hex values (grep); only tokens.css variables; semantic table with scoped headers
- [ ] Mock-based tests pin the server-`today` authority (render with today=2026-03-15 → Mar highlighted; viewing another year → no highlight)

---

### TASK-6504 — Year-overview regression suite

| Field | Value |
|-------|-------|
| **ID** | TASK-6504 |
| **Status** | planned |
| **Agent** | Test & QA Agent (tests/**) |
| **Components** | tests/StatsTid.Tests.Regression (Docker-gated; services-postgres in CI per S64) |
| **KB Refs** | ADR-021 D2, ADR-028, ADR-030, ADR-031; S64 census conventions (`RegressionSeed.SeedEmployeeAsync`, sequential runner, citation-gated assertions) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | per trigger table |
| **Orchestrator Approved** | — |

**Description**: New Docker-gated regression class(es) for the year-overview endpoint:
1. **Auth**: employee self 200; employee→foreign 403; leader-with-scope 200 (mirror BalanceSeriesTests auth shape).
2. **Skema reconciliation (marquee)**: seeded month with work-time rows → `months[m].diff` equals the sum of Skema GET /month per-day diffs for the same month (the shared-helper drift-proof, asserted end-to-end).
3. **Day-equivalents**: a 3,7 t (half-day) VACATION absence → that month's `afholdt = 0,5` and saldo drops 0,5 — not 1 (cites SkemaEndpoints.cs:738 semantics).
4. **Used-reconciliation**: Σ `afholdt` over the entitlement year-to-date == `entitlement_balances.used` for the seeded employee.
5. **Straddle**: absences in March (ferieår Y−1) and October (ferieår Y) count against their own ferieår; Sep saldo shows the reset; carryoverIn case included.
6. **Transferable determinism**: two identical requests byte-equal; formula = min(max(0, earnedAtBoundary + carryoverIn − used − planned), year-start carryoverMax); cap-0 type → 0; value only at `boundaryMonth`.
7. **Future months**: `diff: null` after the (controlled) today; planned future absence appears in `afholdt`.
8. **ANNUAL_ACTIVITY**: academic profile → `normHours: null` months.
9. **Graceful**: profile-less employee → 200 with nulls (ADR-023 D3), never 500.
NOTE: today-dependent assertions use the seeded/controlled clock seam pinned in TASK-6502 (no wall-clock-dependent expected values; the S31/S34 `-infinity` sentinel lesson applies to any DateOnly.MinValue SQL literals).

**Validation Criteria**:
- [ ] Suite green pristine + consecutive (S64 discipline); no `src/` changes required by tests (else declare cross-domain, never self-fix)
- [ ] Census-style citation comments OUTSIDE SQL raw-string literals (S64 lesson)

---

### TASK-6505 — ADR-030 annotation: the transferable display projection

| Field | Value |
|-------|-------|
| **ID** | TASK-6505 |
| **Status** | planned |
| **Agent** | Orchestrator (docs/ is Orchestrator-only) |
| **Components** | docs/knowledge-base/decisions/ADR-030 |
| **KB Refs** | ADR-030, ADR-031 |
| **Orchestrator Approved** | — |

**Description**: Carried-over obligation from the parked 2026-06-03 refinement: annotate ADR-030 with the transferable figure's definition, formula, as-of policy (boundary projection, "max if you book nothing more"), and explicit **non-equivalence** to the deferred §21/§26 settlement. Include the OQ-1 research verdict (RESOLVED 2026-06-06): legal transfer point = 31 Dec per Ferielov §21 stk.2 (display anchor `boundaryMonth = 12`); the model's Sep-rollover carryover is an accrual-side approximation of the legal transfer event; absent a §21 agreement by 31 Dec the untaken 5th week is auto-paid by 31 March (out of scope, settlement). Also record the særlige-feriedage model-vs-law gap (calendar-year accrual, 1 May–30 Apr taking, 2½% godtgørelse payout per Cirkulære 021-24 §12 stk.2 — vs the system's ResetMonth-9/carryover-0 simplification). Cite `docs/references/ferie-transfer-timing-research.md` (committed this sprint).

---

### TASK-6506 — Validation, Step 7a, close

| Field | Value |
|-------|-------|
| **ID** | TASK-6506 |
| **Status** | planned |
| **Agent** | Orchestrator |
| **Components** | — |
| **Orchestrator Approved** | — |

**Description**: `dotnet build` + full pyramid (sprint-test-validation skill: unit + regression pristine/consecutive + smoke + FE with delta arithmetic vs S64's 629/424/5/164); Constraint Validator per task; Step 5a Reviewer audits; Step 7a dual-lens sprint-end review; close gates (Step-7a artifacts + CI-health [needs the S64 fix-forward run green first] + Test-Verified line + repo-root CWD); ROADMAP Completed-Sprints row + INDEX update; commit + push.

## Phases & Dependencies

- **Phase 1 (parallel, worktree isolation)**: TASK-6502 (Backend) ∥ TASK-6503 (UX — builds against the pinned contract with mocked fetch)
- **Phase 2**: TASK-6504 (Test & QA — needs TASK-6502 merged)
- **Phase 3**: TASK-6505 + TASK-6506 (Orchestrator)

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified (display) | OQ-1 verdict recorded: legal ferie transfer point = 31 Dec (Ferielov §21 stk.2; state sector via Cirkulære 021-24 §3) → `boundaryMonth = 12`; the displayed figure is the model's Sep-rollover projection, NOT the legal residual-5th-week quantity (non-equivalence annotated in ADR-030 per TASK-6505; §21/§26 settlement stays deferred). Særlige-feriedage calendar-year/1 May–30 Apr gap (Cirkulære 021-24 §12 stk.2) recorded as ROADMAP follow-up. Flat accrual per ADR-031 reused unchanged. Full citations: `docs/references/ferie-transfer-timing-research.md` |
| Wage type mappings produce correct SLS codes | N/A | read-only sprint; no payroll path touched |
| Overtime/supplement calculations are deterministic | pending | year-overview must be a pure function of (employeeId, year, today, projections) — replay test TASK-6504.6 |
| Absence effects on norm/flex/pension are correct | pending | day-equivalent afholdt reconciliation TASK-6504.3/4 |
| Retroactive recalculation produces stable results | N/A | no recalculation surface |

## External Review (Step 7a)
_pending_

## Test Summary
_pending — S64 baseline: 629 unit + 424 regression + 5(+1) smoke + 164 FE_

## Agent Effectiveness
_pending_

## Sprint Retrospective
_pending_
